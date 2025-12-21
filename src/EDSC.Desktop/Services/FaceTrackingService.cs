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

        // Model input sizes
        private const int FaceDetectionWidth = 160;
        private const int FaceDetectionHeight = 120;
        private const int LandmarkWidth = 114;
        private const int LandmarkHeight = 114;

        // 3D face model points (simplified - key facial landmarks)
        private static readonly Vector3[] Model3DPoints = new[]
        {
            new Vector3(0.0f, 0.0f, 0.0f),           // Nose tip
            new Vector3(0.0f, -63.6f, -12.5f),        // Chin
            new Vector3(-43.3f, 32.7f, -26.0f),       // Left eye left corner
            new Vector3(43.3f, 32.7f, -26.0f),        // Right eye right corner
            new Vector3(-28.9f, -28.9f, -24.1f),      // Left mouth corner
            new Vector3(28.9f, -28.9f, -24.1f)        // Right mouth corner
        };

        // Corresponding 2D landmark indices (0-based, out of 66 landmarks)
        private static readonly int[] LandmarkIndices = new[] { 30, 8, 36, 45, 48, 54 };

        public bool IsInitialized
        {
            get
            {
                return _isInitialized;
            }
        }

        public async Task InitializeAsync(string modelsPath)
        {
            Console.WriteLine("[FaceTrackingService] Entry: InitializeAsync");

            if (string.IsNullOrEmpty(modelsPath))
            {
                Console.WriteLine("[FaceTrackingService] modelsPath is null or empty");
                throw new ArgumentNullException(nameof(modelsPath));
            }

            if (!Directory.Exists(modelsPath))
            {
                Console.WriteLine($"[FaceTrackingService] Models directory not found: {modelsPath}");
                throw new DirectoryNotFoundException($"Models directory not found: {modelsPath}");
            }

            try
            {
                var faceDetectionPath = Path.Combine(modelsPath, "detection.onnx");
                var landmarkPath = Path.Combine(modelsPath, "lm_fast_exp1.onnx");

                if (!File.Exists(faceDetectionPath))
                {
                    Console.WriteLine($"[FaceTrackingService] Face detection model not found: {faceDetectionPath}");
                    throw new FileNotFoundException($"Face detection model not found: {faceDetectionPath}");
                }

                if (!File.Exists(landmarkPath))
                {
                    Console.WriteLine($"[FaceTrackingService] Landmark model not found: {landmarkPath}");
                    throw new FileNotFoundException($"Landmark model not found: {landmarkPath}");
                }

                Console.WriteLine("[FaceTrackingService] Loading face detection model");
                _faceDetectionSession = new InferenceSession(faceDetectionPath);

                Console.WriteLine("[FaceTrackingService] Loading landmark model");
                _landmarkSession = new InferenceSession(landmarkPath);

                _isInitialized = true;

                Console.WriteLine("[FaceTrackingService] Initialization complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Error during initialization: {ex.Message}");
                Console.WriteLine($"[FaceTrackingService] Stack trace: {ex.StackTrace}");
                throw;
            }

            Console.WriteLine("[FaceTrackingService] Exit: InitializeAsync");
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
                    // Step 1: Detect face
                    var faceBox = await DetectFaceAsync(image);
                    if (faceBox == null)
                    {
                        return null;
                    }

                    // Step 2: Detect landmarks
                    var landmarks = await DetectLandmarksAsync(image, faceBox.Value);
                    if (landmarks == null || landmarks.Length != 66)
                    {
                        return null;
                    }

                    // Step 3: Calculate head pose from landmarks
                    var pose = CalculateHeadPose(landmarks, image.Width, image.Height);

                    if (pose != null)
                    {
                        // Add visualization data
                        pose.FaceBox = new FaceBox
                        {
                            X = faceBox.Value.x,
                            Y = faceBox.Value.y,
                            Width = faceBox.Value.width,
                            Height = faceBox.Value.height
                        };

                        pose.Landmarks = landmarks.Select(lm => new LandmarkPoint
                        {
                            X = lm.X,
                            Y = lm.Y
                        }).ToArray();
                    }

                    return pose;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Error processing frame: {ex.Message}");
                return null;
            }
        }

        private async Task<(float x, float y, float width, float height)?> DetectFaceAsync(Image<Rgb24> image)
        {
            if (_faceDetectionSession == null)
            {
                return null;
            }

            try
            {
                // Resize image to detection size
                using (var resized = image.Clone(ctx => ctx.Resize(FaceDetectionWidth, FaceDetectionHeight)))
                {
                    // Convert to tensor (1, 3, 120, 160) - NCHW format
                    var tensor = new DenseTensor<float>(new[] { 1, 3, FaceDetectionHeight, FaceDetectionWidth });

                    // Fill tensor with normalized RGB values
                    for (int y = 0; y < FaceDetectionHeight; y++)
                    {
                        for (int x = 0; x < FaceDetectionWidth; x++)
                        {
                            var pixel = resized[x, y];
                            tensor[0, 0, y, x] = pixel.R / 255f;
                            tensor[0, 1, y, x] = pixel.G / 255f;
                            tensor[0, 2, y, x] = pixel.B / 255f;
                        }
                    }

                    // Run inference
                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input", tensor)
                    };

                    using (var results = _faceDetectionSession.Run(inputs))
                    {
                        var output = results.FirstOrDefault()?.AsEnumerable<float>().ToArray();

                        if (output == null || output.Length < 4)
                        {
                            return null;
                        }

                        // The face detection model processes a 120x160 image but returns coordinates
                        // relative to this resized image. We need to scale them back to original image space.
                        // Most face detection models output normalized coordinates [0,1] relative to the model input.

                        float scaleX = (float)image.Width / FaceDetectionWidth;
                        float scaleY = (float)image.Height / FaceDetectionHeight;

                        // Determine if format is corner coordinates [x1, y1, x2, y2] vs center format [cx, cy, w, h]
                        bool isCornerFormat = output.Length >= 4 &&
                                           (output[2] > output[0] && output[3] > output[1]); // x2 > x1 and y2 > y1


                        if (isCornerFormat)
                        {
                            // Corner format: [x1, y1, x2, y2] - coordinates are normalized [0,1] of model input
                            float x1 = output[0] * image.Width;
                            float y1 = output[1] * image.Height;
                            float x2 = output[2] * image.Width;
                            float y2 = output[3] * image.Height;

                            return (
                                Math.Max(0, Math.Min(x1, image.Width)),
                                Math.Max(0, Math.Min(y1, image.Height)),
                                Math.Max(0, Math.Min(x2 - x1, image.Width)),
                                Math.Max(0, Math.Min(y2 - y1, image.Height))
                            );
                        }
                        else
                        {
                            // Center format: [centerX, centerY, width, height] - coordinates are normalized [0,1] of model input
                            float centerX = output[0] * image.Width;
                            float centerY = output[1] * image.Height;
                            float width = output[2] * image.Width;
                            float height = output[3] * image.Height;

                            // Convert from center format to top-left format
                            return (
                                Math.Max(0, Math.Min(centerX - width / 2, image.Width)),
                                Math.Max(0, Math.Min(centerY - height / 2, image.Height)),
                                Math.Max(0, Math.Min(width, image.Width)),
                                Math.Max(0, Math.Min(height, image.Height))
                            );
                        }
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
                // Crop face region with some padding
                int padding = 20;
                int cropX = Math.Max(0, (int)faceBox.x - padding);
                int cropY = Math.Max(0, (int)faceBox.y - padding);
                int cropWidth = Math.Min(image.Width - cropX, (int)faceBox.width + 2 * padding);
                int cropHeight = Math.Min(image.Height - cropY, (int)faceBox.height + 2 * padding);

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
                            tensor[0, 0, y, x] = gray;
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

                        // The landmark model processes a 114x114 cropped face region and returns
                        // normalized coordinates [0,1] relative to this cropped region
                        float scaleX = cropWidth;
                        float scaleY = cropHeight;

                        for (int i = 0; i < 66; i++)
                        {
                            // Landmark coordinates are typically normalized [0,1] relative to the model input (cropped region)
                            float normX = Math.Max(0, Math.Min(1, output[i * 2]));
                            float normY = Math.Max(0, Math.Min(1, output[i * 2 + 1]));

                            // Convert from normalized cropped coordinates back to original image coordinates
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

        private HeadPose? CalculateHeadPose(Vector2[] landmarks, int imageWidth, int imageHeight)
        {
            try
            {
                // Extract the key landmarks we need
                var image2DPoints = new Vector2[LandmarkIndices.Length];
                for (int i = 0; i < LandmarkIndices.Length; i++)
                {
                    image2DPoints[i] = landmarks[LandmarkIndices[i]];
                }

                // Simplified camera matrix (focal length estimation)
                float focalLength = imageWidth;
                float cx = imageWidth / 2f;
                float cy = imageHeight / 2f;

                // Use simplified pose estimation
                // For a full implementation, you'd use OpenCV's solvePnP
                // Here we'll do a basic estimation from landmark positions

                // Calculate center of face
                float centerX = image2DPoints.Average(p => p.X);
                float centerY = image2DPoints.Average(p => p.Y);

                // Estimate distance based on face size
                float faceWidth = Math.Abs(image2DPoints[2].X - image2DPoints[3].X); // Eye corners
                float avgFaceWidthMm = 140f; // Average human face width in mm
                float z = (avgFaceWidthMm * focalLength) / Math.Max(faceWidth, 1f);

                // Estimate rotation from landmark geometry
                // Yaw: horizontal rotation
                float leftEyeX = image2DPoints[2].X;
                float rightEyeX = image2DPoints[3].X;
                float eyeMidX = (leftEyeX + rightEyeX) / 2f;
                float yaw = (float)(Math.Atan2(eyeMidX - centerX, focalLength) * (180f / MathF.PI));

                // Pitch: vertical rotation
                float eyeY = (image2DPoints[2].Y + image2DPoints[3].Y) / 2f;
                float chinY = image2DPoints[1].Y;
                float pitch = (float)(Math.Atan2(eyeY - chinY, focalLength / 2f) * (180f / MathF.PI));

                // Roll: tilt rotation
                float roll = (float)(Math.Atan2(image2DPoints[3].Y - image2DPoints[2].Y,
                                        image2DPoints[3].X - image2DPoints[2].X) * (180f / MathF.PI));

                // Convert 2D center to 3D position
                float x = (centerX - cx) * z / focalLength;
                float y = (centerY - cy) * z / focalLength;

                return new HeadPose
                {
                    X = x,
                    Y = -y,  // Invert Y to match typical coordinate system
                    Z = z,
                    Yaw = yaw,
                    Pitch = pitch,
                    Roll = roll
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Pose calculation error: {ex.Message}");
                return null;
            }
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
