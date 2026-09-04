using EDSC.Services;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EDSC.Desktop.Services
{
    /// <summary>
    /// Face tracking service using ONNX Runtime
    /// </summary>
    public class FaceTrackingService : IFaceTrackingService
    {
        private InferenceSession? _faceDetectionSession;
        private InferenceSession? _landmarkSession;
        private bool _isInitialized;
        private readonly List<Prior> _priors = new List<Prior>();
        private readonly object _smoothingLock = new object();
        private HeadPose? _lastSmoothedPose;

        // PnP warm start: previous solution used as the initial guess for the next frame
        private readonly double[] _prevRvec = new double[3];
        private readonly double[] _prevTvec = new double[3];
        private bool _hasPrevPnpSolution;

        // Landmark-driven tracking: crop the next frame around the previous landmarks instead of re-detecting.
        // The detector is frontal-biased and drops out at moderate head angles; the landmark model does not.
        private (float x, float y, float width, float height)? _trackedFaceBox;
        private int _framesSinceDetection;
        private const int DetectorVerifyInterval = 15;   // frames between detector sanity checks while tracking
        private const float DetectorReseedIoU = 0.3f;    // below this overlap the detector's box wins

        /// <summary>
        /// Human-readable result of the most recent frame, for the preview status line.
        /// </summary>
        public string LastStatus { get; private set; } = "Idle";

        // Model input sizes
        private const int FaceDetectionWidth = 160;
        private const int FaceDetectionHeight = 120;
        private const int LandmarkWidth = 114;
        private const int LandmarkHeight = 114;
        private const float DetectionScoreThreshold = 0.8f;
        private const float DetectionNmsThreshold = 0.5f;
        private const int DetectionTopK = 7;
        private const float LandmarkMean = 0.445313568967f;
        private const float LandmarkStd = 0.269246187f;
        private static readonly float[] DetectionVariance = new[] { 0.1f, 0.2f };
        private const float BaseTranslationScale = 0.1f; // model units (mm) -> cm for Opentrack

        // Reject a PnP solution whose reprojection error exceeds this fraction of the eye-corner distance
        private const double MaxReprojectionErrorRatio = 0.3;

        // 3D face model points in millimetres, camera convention: X right, Y down, Z away from camera.
        // A frontal face facing the camera therefore has identity rotation.
        private static readonly Vector3[] Model3DPoints = new[]
        {
            new Vector3(0.0f, 0.0f, 0.0f),            // Nose tip
            new Vector3(0.0f, 63.6f, 12.5f),          // Chin
            new Vector3(-43.3f, -32.7f, 26.0f),       // Image-left eye outer corner
            new Vector3(43.3f, -32.7f, 26.0f),        // Image-right eye outer corner
            new Vector3(-28.9f, 28.9f, 24.1f),        // Image-left mouth corner
            new Vector3(28.9f, 28.9f, 24.1f)          // Image-right mouth corner
        };

        // Distance between the two eye-corner model points, used for the initial depth estimate
        private static readonly double ModelEyeCornerDistance = 86.6;

        // Corresponding 2D landmark indices (0-based, out of 66 landmarks)
        private static readonly int[] LandmarkIndices = new[] { 30, 8, 36, 45, 48, 54 };

        private struct Prior
        {
            public float X;
            public float Y;
            public float Width;
            public float Height;

            public Prior(float x, float y, float width, float height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }
        }

        private struct FaceCandidate
        {
            public float X;
            public float Y;
            public float Width;
            public float Height;
            public float Score;
        }

        public bool IsInitialized
        {
            get
            {
                return _isInitialized;
            }
        }

        public float TranslationScale { get; set; } = 1f;

        public float YawScale { get; set; } = 1f;

        public float RotationScale { get; set; } = 1f;

        public float RollScale { get; set; } = 1f;

        public float SmoothingStrength { get; set; } = 0.5f;

        public async Task InitializeAsync(string modelsPath)
        {
            if (string.IsNullOrEmpty(modelsPath))
            {
                throw new ArgumentNullException(nameof(modelsPath));
            }

            if (!Directory.Exists(modelsPath))
            {
                throw new DirectoryNotFoundException($"Models directory not found: {modelsPath}");
            }

            try
            {
                var faceDetectionPath = Path.Combine(modelsPath, "detection.onnx");
                var landmarkPath = Path.Combine(modelsPath, "lm_fast_exp1.onnx");

                if (!File.Exists(faceDetectionPath))
                {
                    throw new FileNotFoundException($"Face detection model not found: {faceDetectionPath}");
                }

                if (!File.Exists(landmarkPath))
                {
                    throw new FileNotFoundException($"Landmark model not found: {landmarkPath}");
                }

                _faceDetectionSession = new InferenceSession(faceDetectionPath);
                _landmarkSession = new InferenceSession(landmarkPath);

                GeneratePriors();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Error during initialization: {ex.Message}");
                Console.WriteLine($"[FaceTrackingService] Stack trace: {ex.StackTrace}");
                throw;
            }
            await Task.CompletedTask;
        }

        public async Task<HeadPose?> ProcessFrameAsync(byte[] frameData)
        {
            if (frameData == null || frameData.Length == 0)
            {
                return null;
            }

            if (!_isInitialized || _faceDetectionSession == null || _landmarkSession == null)
            {
                return null;
            }

            try
            {
                // Load image from bytes
                using (var image = Image.Load<Rgb24>(frameData))
                {
                    (float x, float y, float width, float height)? trackedBox;
                    int framesSinceDetection;
                    lock (_smoothingLock)
                    {
                        trackedBox = _trackedFaceBox;
                        framesSinceDetection = _framesSinceDetection;
                    }

                    Vector2[]? landmarks = null;
                    HeadPose? pose = null;
                    (float x, float y, float width, float height) usedBox = default;
                    string status = string.Empty;
                    string source = "roi";

                    // Fast path: crop around the previous frame's landmarks and skip the detector
                    if (trackedBox != null)
                    {
                        landmarks = await DetectLandmarksAsync(image, trackedBox.Value);
                        if (landmarks == null || landmarks.Length != 66)
                        {
                            status = "landmark model returned no result";
                        }
                        else if (!LandmarksFitCrop(landmarks, trackedBox.Value))
                        {
                            status = "landmarks left the crop";
                        }
                        else
                        {
                            pose = CalculateHeadPose(landmarks, image.Width, image.Height, out status);
                            usedBox = trackedBox.Value;
                        }
                    }

                    // Run the detector when tracking failed, or periodically as a sanity check
                    (float x, float y, float width, float height)? detected = null;
                    bool detectorRan = false;
                    if (pose == null || framesSinceDetection >= DetectorVerifyInterval)
                    {
                        detected = await DetectFaceAsync(image);
                        detectorRan = true;
                    }

                    // While tracking, a detector hit somewhere else means the crop has drifted: re-seed from it
                    if (pose != null && detected != null && BoxIoU(detected.Value, usedBox) < DetectorReseedIoU)
                    {
                        pose = null;
                        status = "detector disagreed with tracked crop";
                    }

                    if (pose == null)
                    {
                        source = "detector";

                        if (detected == null)
                        {
                            ResetSmoothing();
                            LastStatus = trackedBox != null
                                ? $"Face lost: {status}; detector found nothing"
                                : "No face detected";
                            return null;
                        }

                        landmarks = await DetectLandmarksAsync(image, detected.Value);
                        if (landmarks == null || landmarks.Length != 66)
                        {
                            ResetSmoothing();
                            LastStatus = "Landmark model returned no result";
                            return null;
                        }

                        pose = CalculateHeadPose(landmarks, image.Width, image.Height, out status);
                        usedBox = detected.Value;

                        if (pose == null)
                        {
                            ResetSmoothing();
                            LastStatus = status;
                            return null;
                        }
                    }

                    // Add visualization data
                    pose.FaceBox = new FaceBox
                    {
                        X = usedBox.x,
                        Y = usedBox.y,
                        Width = usedBox.width,
                        Height = usedBox.height
                    };

                    pose.Landmarks = landmarks!.Select(lm => new LandmarkPoint
                    {
                        X = lm.X,
                        Y = lm.Y
                    }).ToArray();

                    // Crop for the next frame follows the landmarks we just found, but eased so the
                    // crop does not jump every frame and feed its own jitter back into the landmarks
                    var nextBox = BuildTrackingBox(landmarks!, image.Width, image.Height);
                    lock (_smoothingLock)
                    {
                        _trackedFaceBox = source == "roi" ? EaseTrackingBox(_trackedFaceBox, nextBox) : nextBox;
                        _framesSinceDetection = detectorRan ? 0 : framesSinceDetection + 1;
                    }

                    LastStatus = $"Tracking via {source}: {status}";

                    return ApplySmoothing(pose);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Error processing frame: {ex.Message}");
                ResetSmoothing();
                LastStatus = $"Error: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Build the crop for the next frame from this frame's landmarks. The landmark cloud spans
        /// eyebrows to chin, so extra room is added above for the forehead to approximate the
        /// detector's box plus its padding, which is what the landmark model was trained on.
        /// </summary>
        private static (float x, float y, float width, float height)? BuildTrackingBox(Vector2[] landmarks, int imageWidth, int imageHeight)
        {
            if (landmarks == null || landmarks.Length == 0)
            {
                return null;
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var lm in landmarks)
            {
                if (lm.X < minX) minX = lm.X;
                if (lm.Y < minY) minY = lm.Y;
                if (lm.X > maxX) maxX = lm.X;
                if (lm.Y > maxY) maxY = lm.Y;
            }

            float w = maxX - minX;
            float h = maxY - minY;
            if (w < 16f || h < 16f)
            {
                return null;
            }

            float x1 = MathF.Max(0f, minX - w * 0.15f);
            float x2 = MathF.Min(imageWidth, maxX + w * 0.15f);
            float y1 = MathF.Max(0f, minY - h * 0.30f);
            float y2 = MathF.Min(imageHeight, maxY + h * 0.10f);

            float outW = x2 - x1;
            float outH = y2 - y1;
            if (outW < 24f || outH < 24f)
            {
                return null;
            }

            return (x1, y1, outW, outH);
        }

        /// <summary>
        /// Move the tracked crop part of the way toward the newly computed one. A large jump
        /// (the face moved quickly) is taken in full so the crop never lags behind the face.
        /// </summary>
        private static (float x, float y, float width, float height)? EaseTrackingBox(
            (float x, float y, float width, float height)? previous,
            (float x, float y, float width, float height)? target)
        {
            if (target == null)
            {
                return null;
            }

            if (previous == null)
            {
                return target;
            }

            var p = previous.Value;
            var n = target.Value;

            float prevCx = p.x + p.width / 2f;
            float prevCy = p.y + p.height / 2f;
            float nextCx = n.x + n.width / 2f;
            float nextCy = n.y + n.height / 2f;
            float shift = MathF.Sqrt((nextCx - prevCx) * (nextCx - prevCx) + (nextCy - prevCy) * (nextCy - prevCy));

            if (shift > n.width * 0.15f || MathF.Abs(n.width - p.width) > n.width * 0.2f)
            {
                return target;
            }

            const float alpha = 0.3f;
            return (
                p.x + (n.x - p.x) * alpha,
                p.y + (n.y - p.y) * alpha,
                p.width + (n.width - p.width) * alpha,
                p.height + (n.height - p.height) * alpha);
        }

        /// <summary>
        /// Reject landmark sets that are implausible for the crop they came from, which is what the
        /// landmark model produces when the face has moved out of the tracked region.
        /// </summary>
        private static bool LandmarksFitCrop(Vector2[] landmarks, (float x, float y, float width, float height) crop)
        {
            if (landmarks == null || landmarks.Length == 0 || crop.width <= 0f || crop.height <= 0f)
            {
                return false;
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var lm in landmarks)
            {
                if (float.IsNaN(lm.X) || float.IsNaN(lm.Y))
                {
                    return false;
                }

                if (lm.X < minX) minX = lm.X;
                if (lm.Y < minY) minY = lm.Y;
                if (lm.X > maxX) maxX = lm.X;
                if (lm.Y > maxY) maxY = lm.Y;
            }

            float w = maxX - minX;
            float h = maxY - minY;
            float relW = w / crop.width;
            float relH = h / crop.height;

            if (relW < 0.25f || relW > 1.25f || relH < 0.25f || relH > 1.25f)
            {
                return false;
            }

            float centerX = (minX + maxX) / 2f;
            float centerY = (minY + maxY) / 2f;
            return centerX >= crop.x && centerX <= crop.x + crop.width
                && centerY >= crop.y && centerY <= crop.y + crop.height;
        }

        private static float BoxIoU(
            (float x, float y, float width, float height) a,
            (float x, float y, float width, float height) b)
        {
            float x1 = MathF.Max(a.x, b.x);
            float y1 = MathF.Max(a.y, b.y);
            float x2 = MathF.Min(a.x + a.width, b.x + b.width);
            float y2 = MathF.Min(a.y + a.height, b.y + b.height);

            float inter = MathF.Max(0f, x2 - x1) * MathF.Max(0f, y2 - y1);
            float union = a.width * a.height + b.width * b.height - inter;
            return union <= 0f ? 0f : inter / union;
        }

        private async Task<(float x, float y, float width, float height)?> DetectFaceAsync(Image<Rgb24> image)
        {
            if (_faceDetectionSession == null)
            {
                return null;
            }

            try
            {
                if (_priors.Count == 0)
                {
                    GeneratePriors();
                }

                using (var resized = image.Clone(ctx => ctx.Resize(FaceDetectionWidth, FaceDetectionHeight)))
                {
                    var tensor = new DenseTensor<float>(new[] { 1, 3, FaceDetectionHeight, FaceDetectionWidth });

                    for (int y = 0; y < FaceDetectionHeight; y++)
                    {
                        for (int x = 0; x < FaceDetectionWidth; x++)
                        {
                            var pixel = resized[x, y];
                            tensor[0, 0, y, x] = pixel.B;
                            tensor[0, 1, y, x] = pixel.G;
                            tensor[0, 2, y, x] = pixel.R;
                        }
                    }

                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input", tensor)
                    };

                    using (var results = _faceDetectionSession.Run(inputs))
                    {
                        var loc = results.First(r => r.Name == "loc").AsTensor<float>().ToArray();
                        var conf = results.First(r => r.Name == "conf").AsTensor<float>().ToArray();
                        var iou = results.First(r => r.Name == "iou").AsTensor<float>().ToArray();

                        int priorCount = _priors.Count;
                        if (loc.Length < priorCount * 14 || conf.Length < priorCount * 2 || iou.Length < priorCount)
                        {
                            return null;
                        }

                        var candidates = new List<FaceCandidate>(priorCount);
                        for (int i = 0; i < priorCount; i++)
                        {
                            float clsScore = conf[i * 2 + 1];
                            float iouScore = iou[i];
                            if (iouScore < 0f)
                            {
                                iouScore = 0f;
                            }
                            else if (iouScore > 1f)
                            {
                                iouScore = 1f;
                            }

                            float score = MathF.Sqrt(clsScore * iouScore);
                            if (score < DetectionScoreThreshold)
                            {
                                continue;
                            }

                            var prior = _priors[i];
                            int locOffset = i * 14;
                            float cx = (prior.X + loc[locOffset + 0] * DetectionVariance[0] * prior.Width) * FaceDetectionWidth;
                            float cy = (prior.Y + loc[locOffset + 1] * DetectionVariance[0] * prior.Height) * FaceDetectionHeight;
                            float w = prior.Width * MathF.Exp(loc[locOffset + 2] * DetectionVariance[0]) * FaceDetectionWidth;
                            float h = prior.Height * MathF.Exp(loc[locOffset + 3] * DetectionVariance[1]) * FaceDetectionHeight;

                            candidates.Add(new FaceCandidate
                            {
                                X = cx - w / 2f,
                                Y = cy - h / 2f,
                                Width = w,
                                Height = h,
                                Score = score
                            });
                        }

                        if (candidates.Count == 0)
                        {
                            return null;
                        }

                        var kept = ApplyNms(candidates, DetectionScoreThreshold, DetectionNmsThreshold, DetectionTopK);
                        if (kept.Count == 0)
                        {
                            return null;
                        }

                        float scaleX = (float)image.Width / FaceDetectionWidth;
                        float scaleY = (float)image.Height / FaceDetectionHeight;

                        int bestIndex = 0;
                        float bestDistance = float.MaxValue;
                        float imageCenterX = image.Width / 2f;
                        float imageCenterY = image.Height / 2f;

                        for (int i = 0; i < kept.Count; i++)
                        {
                            var face = kept[i];
                            float centerX = (face.X + face.Width / 2f) * scaleX;
                            float centerY = (face.Y + face.Height / 2f) * scaleY;
                            float dx = imageCenterX - centerX;
                            float dy = imageCenterY - centerY;
                            float distance = dx * dx + dy * dy;

                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                bestIndex = i;
                            }
                        }

                        var selected = kept[bestIndex];
                        float scaledX = selected.X * scaleX;
                        float scaledY = selected.Y * scaleY;
                        float scaledW = selected.Width * scaleX;
                        float scaledH = selected.Height * scaleY;

                        return ApplyFaceBoxPadding(scaledX, scaledY, scaledW, scaledH, image.Width, image.Height);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Face detection error: {ex.Message}");
            }

            return null;
        }

        private async Task<Vector2[]?> DetectLandmarksAsync(Image<Rgb24> image, (float x, float y, float width, float height) faceBox)
        {
            if (_landmarkSession == null)
            {
                return null;
            }

            // Validate face box parameters
            if (faceBox.width <= 0 || faceBox.height <= 0)
            {
                return null;
            }

            try
            {
                int cropX = Math.Max(0, (int)MathF.Floor(faceBox.x));
                int cropY = Math.Max(0, (int)MathF.Floor(faceBox.y));
                int cropWidth = Math.Min(image.Width - cropX, (int)MathF.Ceiling(faceBox.width));
                int cropHeight = Math.Min(image.Height - cropY, (int)MathF.Ceiling(faceBox.height));

                if (cropWidth <= 0 || cropHeight <= 0)
                {
                    return null;
                }

                using (var cropped = image.Clone(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight))))
                using (var resized = cropped.Clone(ctx => ctx.Resize(LandmarkWidth, LandmarkHeight)))
                {
                    // Convert to grayscale tensor (1, 1, 114, 114) - NCHW format
                    var tensor = new DenseTensor<float>(new[] { 1, 1, LandmarkHeight, LandmarkWidth });

                    // Convert to grayscale and normalize to [0, 1] range
                    for (int y = 0; y < LandmarkHeight; y++)
                    {
                        for (int x = 0; x < LandmarkWidth; x++)
                        {
                            var pixel = resized[x, y];
                            // Convert RGB to grayscale using standard weights: 0.299*R + 0.587*G + 0.114*B
                            float gray = (0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B) / 255.0f;
                            tensor[0, 0, y, x] = (gray - LandmarkMean) / LandmarkStd;
                        }
                    }

                    // Run inference
                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input", tensor)
                    };

                    using (var results = _landmarkSession.Run(inputs))
                    {
                        var output = results.FirstOrDefault()?.AsEnumerable<float>().ToArray();

                        if (output == null || output.Length < 132) // 66 landmarks * 2 coordinates
                        {
                            return null;
                        }

                        var landmarks = new Vector2[66];

                        // Convert normalized coordinates back to original image space
                        float scaleX = cropWidth;
                        float scaleY = cropHeight;

                        for (int i = 0; i < 66; i++)
                        {
                            // Landmarks are in normalized coordinates [0, 1] relative to cropped region
                            float normX = output[i * 2];
                            float normY = output[i * 2 + 1];

                            // Convert to original image coordinates
                            landmarks[i] = new Vector2(
                                cropX + normX * scaleX,
                                cropY + normY * scaleY
                            );
                        }

                        return landmarks;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Landmark detection error: {ex.Message}");
            }

            return null;
        }

        private void GeneratePriors()
        {
            _priors.Clear();

            int inputW = FaceDetectionWidth;
            int inputH = FaceDetectionHeight;

            int featureMap2W = ((inputW + 1) / 2) / 2;
            int featureMap2H = ((inputH + 1) / 2) / 2;
            int featureMap3W = featureMap2W / 2;
            int featureMap3H = featureMap2H / 2;
            int featureMap4W = featureMap3W / 2;
            int featureMap4H = featureMap3H / 2;
            int featureMap5W = featureMap4W / 2;
            int featureMap5H = featureMap4H / 2;
            int featureMap6W = featureMap5W / 2;
            int featureMap6H = featureMap5H / 2;

            var featureMapSizes = new (int Width, int Height)[]
            {
                (featureMap3W, featureMap3H),
                (featureMap4W, featureMap4H),
                (featureMap5W, featureMap5H),
                (featureMap6W, featureMap6H)
            };

            var minSizes = new[]
            {
                new[] { 10.0f, 16.0f, 24.0f },
                new[] { 32.0f, 48.0f },
                new[] { 64.0f, 96.0f },
                new[] { 128.0f, 192.0f, 256.0f }
            };

            int[] steps = { 8, 16, 32, 64 };

            for (int i = 0; i < featureMapSizes.Length; i++)
            {
                var size = featureMapSizes[i];
                var step = steps[i];
                var mins = minSizes[i];

                for (int h = 0; h < size.Height; h++)
                {
                    for (int w = 0; w < size.Width; w++)
                    {
                        for (int j = 0; j < mins.Length; j++)
                        {
                            float sKx = mins[j] / inputW;
                            float sKy = mins[j] / inputH;
                            float cx = (w + 0.5f) * step / inputW;
                            float cy = (h + 0.5f) * step / inputH;

                            _priors.Add(new Prior(cx, cy, sKx, sKy));
                        }
                    }
                }
            }
        }

        private static List<FaceCandidate> ApplyNms(List<FaceCandidate> candidates, float scoreThreshold, float nmsThreshold, int topK)
        {
            var sorted = candidates
                .Where(c => c.Score >= scoreThreshold)
                .OrderByDescending(c => c.Score)
                .ToList();

            if (topK > 0 && sorted.Count > topK)
            {
                sorted = sorted.Take(topK).ToList();
            }

            var kept = new List<FaceCandidate>(sorted.Count);
            foreach (var candidate in sorted)
            {
                bool suppressed = false;
                foreach (var existing in kept)
                {
                    if (ComputeIoU(candidate, existing) >= nmsThreshold)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (!suppressed)
                {
                    kept.Add(candidate);
                }
            }

            return kept;
        }

        private static float ComputeIoU(FaceCandidate a, FaceCandidate b)
        {
            float x1 = MathF.Max(a.X, b.X);
            float y1 = MathF.Max(a.Y, b.Y);
            float x2 = MathF.Min(a.X + a.Width, b.X + b.Width);
            float y2 = MathF.Min(a.Y + a.Height, b.Y + b.Height);

            float interW = MathF.Max(0f, x2 - x1);
            float interH = MathF.Max(0f, y2 - y1);
            float interArea = interW * interH;
            float union = a.Width * a.Height + b.Width * b.Height - interArea;

            if (union <= 0f)
            {
                return 0f;
            }

            return interArea / union;
        }

        private static (float x, float y, float width, float height) ApplyFaceBoxPadding(
            float x,
            float y,
            float width,
            float height,
            int imageWidth,
            int imageHeight)
        {
            float cropX1 = x - width * 0.1f;
            float cropY1 = y - height * 0.1f;
            float cropX2 = x + width + width * 0.1f;
            float cropY2 = y + height + height * 0.1f;

            cropX1 = MathF.Max(0f, cropX1);
            cropY1 = MathF.Max(0f, cropY1);
            cropX2 = MathF.Min(imageWidth, cropX2);
            cropY2 = MathF.Min(imageHeight, cropY2);

            return (cropX1, cropY1, MathF.Max(0f, cropX2 - cropX1), MathF.Max(0f, cropY2 - cropY1));
        }

        private HeadPose? CalculateHeadPose(Vector2[] landmarks, int imageWidth, int imageHeight, out string status)
        {
            status = "pose not computed";

            try
            {
                // Extract the key landmarks we need
                var image2DPoints = new Vector2[LandmarkIndices.Length];
                for (int i = 0; i < LandmarkIndices.Length; i++)
                {
                    image2DPoints[i] = landmarks[LandmarkIndices[i]];
                }

                // Pinhole camera. Focal length is approximated as the image width until a FOV setting exists.
                double focalLength = imageWidth;
                double cx = imageWidth / 2.0;
                double cy = imageHeight / 2.0;

                double eyeDistancePx = Vector2.Distance(image2DPoints[2], image2DPoints[3]);
                if (eyeDistancePx < 1.0)
                {
                    status = "eye corners coincide";
                    return null;
                }

                double maxRms = Math.Max(2.0, eyeDistancePx * MaxReprojectionErrorRatio);

                var rvec = new double[3];
                var tvec = new double[3];
                double rms = double.PositiveInfinity;
                bool solved = false;

                // First try: refine from the previous frame's solution
                bool hasPrev;
                lock (_smoothingLock)
                {
                    hasPrev = _hasPrevPnpSolution;
                    if (hasPrev)
                    {
                        Array.Copy(_prevRvec, rvec, 3);
                        Array.Copy(_prevTvec, tvec, 3);
                    }
                }

                if (hasPrev)
                {
                    solved = PnpSolver.TrySolve(Model3DPoints, image2DPoints, focalLength, focalLength, cx, cy, rvec, tvec, out rms)
                        && rms <= maxRms;
                }

                // Second try: start from a frontal face at the depth implied by the eye spacing
                if (!solved)
                {
                    double z0 = focalLength * ModelEyeCornerDistance / eyeDistancePx;
                    rvec[0] = 0.0;
                    rvec[1] = 0.0;
                    rvec[2] = 0.0;
                    tvec[0] = (image2DPoints[0].X - cx) * z0 / focalLength;
                    tvec[1] = (image2DPoints[0].Y - cy) * z0 / focalLength;
                    tvec[2] = z0;

                    solved = PnpSolver.TrySolve(Model3DPoints, image2DPoints, focalLength, focalLength, cx, cy, rvec, tvec, out rms)
                        && rms <= maxRms;
                }

                lock (_smoothingLock)
                {
                    if (solved)
                    {
                        Array.Copy(rvec, _prevRvec, 3);
                        Array.Copy(tvec, _prevTvec, 3);
                        _hasPrevPnpSolution = true;
                    }
                    else
                    {
                        _hasPrevPnpSolution = false;
                    }
                }

                if (!solved)
                {
                    status = $"pose rejected (rms {rms:F1}px > {maxRms:F1}px)";
                    return null;
                }

                status = $"rms {rms:F1}px";

                // Decompose R = Ry(yaw) * Rx(pitch) * Rz(roll) in the X-right, Y-down, Z-forward frame.
                // Signs are chosen so that: yaw > 0 when the face turns toward image-right,
                // pitch > 0 when the face tilts up, roll > 0 when the image-right side of the face drops.
                var r = new double[9];
                PnpSolver.Rodrigues(rvec[0], rvec[1], rvec[2], r);

                double yawRad = -Math.Atan2(r[2], r[8]);
                double pitchRad = Math.Asin(Math.Clamp(r[5], -1.0, 1.0));
                double rollRad = Math.Atan2(r[3], r[4]);

                const double RadToDeg = 180.0 / Math.PI;
                double translationScale = BaseTranslationScale * TranslationScale;

                return new HeadPose
                {
                    X = tvec[0] * translationScale,
                    Y = -tvec[1] * translationScale,  // Y up is positive
                    Z = tvec[2] * translationScale,
                    Yaw = yawRad * RadToDeg * YawScale,
                    Pitch = pitchRad * RadToDeg * RotationScale,
                    Roll = rollRad * RadToDeg * RollScale
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Pose calculation error: {ex.Message}");
                status = $"pose error: {ex.Message}";
                return null;
            }
        }

        private void ResetSmoothing()
        {
            lock (_smoothingLock)
            {
                _lastSmoothedPose = null;
                _hasPrevPnpSolution = false;
                _trackedFaceBox = null;
                _framesSinceDetection = 0;
            }
        }

        private HeadPose ApplySmoothing(HeadPose pose)
        {
            if (pose == null)
            {
                return pose;
            }

            float strength = Math.Clamp(SmoothingStrength, 0f, 0.95f);

            lock (_smoothingLock)
            {
                if (strength <= 0f)
                {
                    _lastSmoothedPose = ClonePose(pose);
                    return pose;
                }

                if (_lastSmoothedPose == null)
                {
                    _lastSmoothedPose = ClonePose(pose);
                    return pose;
                }

                float alpha = 1f - strength;
                var smoothed = new HeadPose
                {
                    X = Lerp(_lastSmoothedPose.X, pose.X, alpha),
                    Y = Lerp(_lastSmoothedPose.Y, pose.Y, alpha),
                    Z = Lerp(_lastSmoothedPose.Z, pose.Z, alpha),
                    Yaw = Lerp(_lastSmoothedPose.Yaw, pose.Yaw, alpha),
                    Pitch = Lerp(_lastSmoothedPose.Pitch, pose.Pitch, alpha),
                    Roll = Lerp(_lastSmoothedPose.Roll, pose.Roll, alpha),
                    FaceBox = pose.FaceBox,
                    Landmarks = pose.Landmarks
                };

                _lastSmoothedPose = ClonePose(smoothed);
                return smoothed;
            }
        }

        private static double Lerp(double from, double to, float t)
        {
            return from + (to - from) * t;
        }

        private static HeadPose ClonePose(HeadPose pose)
        {
            return new HeadPose
            {
                X = pose.X,
                Y = pose.Y,
                Z = pose.Z,
                Yaw = pose.Yaw,
                Pitch = pose.Pitch,
                Roll = pose.Roll,
                FaceBox = pose.FaceBox,
                Landmarks = pose.Landmarks
            };
        }

        public void Dispose()
        {
            _faceDetectionSession?.Dispose();
            _faceDetectionSession = null;

            _landmarkSession?.Dispose();
            _landmarkSession = null;

            _isInitialized = false;
        }
    }
}
