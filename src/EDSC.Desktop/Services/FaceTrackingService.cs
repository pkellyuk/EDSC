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
            Console.WriteLine($"[FaceTrackingService] Entry: ProcessFrameAsync - Frame size: {frameData?.Length ?? 0}");

            if (frameData == null || frameData.Length == 0)
            {
                Console.WriteLine("[FaceTrackingService] Exit: ProcessFrameAsync - frameData is null or empty");
                return null;
            }

            Console.WriteLine($"[FaceTrackingService] Frame data size: {frameData.Length} bytes");

            if (!_isInitialized || _faceDetectionSession == null || _landmarkSession == null)
            {
                Console.WriteLine("[FaceTrackingService] Service not initialized");
                return null;
            }

            try
            {
                // Load image from bytes
                using (var image = Image.Load<Rgb24>(frameData))
                {
                    Console.WriteLine($"[FaceTrackingService] Image loaded: {image.Width}x{image.Height}");

                    // Step 1: Detect face
                    Console.WriteLine("[FaceTrackingService] Starting face detection");
                    var faceBox = await DetectFaceAsync(image);
                    if (faceBox == null)
                    {
                        Console.WriteLine("[FaceTrackingService] Exit: ProcessFrameAsync - No face detected");
                        return null;
                    }

                    Console.WriteLine($"[FaceTrackingService] Face detected at: x={faceBox.Value.x}, y={faceBox.Value.y}, w={faceBox.Value.width}, h={faceBox.Value.height}");

                    // Step 2: Detect landmarks
                    Console.WriteLine("[FaceTrackingService] Starting landmark detection");
                    var landmarks = await DetectLandmarksAsync(image, faceBox.Value);
                    if (landmarks == null || landmarks.Length != 66)
                    {
                        Console.WriteLine($"[FaceTrackingService] Exit: ProcessFrameAsync - Invalid landmarks count: {landmarks?.Length ?? 0}");
                        return null;
                    }

                    Console.WriteLine($"[FaceTrackingService] Landmarks detected: {landmarks.Length} points");

                    // Step 3: Calculate head pose from landmarks
                    Console.WriteLine("[FaceTrackingService] Calculating head pose");
                    var pose = CalculateHeadPose(landmarks, image.Width, image.Height);

                    if (pose != null)
                    {
                        Console.WriteLine($"[FaceTrackingService] Pose calculated: X={pose.X:F2}, Y={pose.Y:F2}, Z={pose.Z:F2}, Yaw={pose.Yaw:F2}, Pitch={pose.Pitch:F2}, Roll={pose.Roll:F2}");

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

                        Console.WriteLine("[FaceTrackingService] Exit: ProcessFrameAsync - Success");
                    }
                    else
                    {
                        Console.WriteLine("[FaceTrackingService] Exit: ProcessFrameAsync - Pose calculation failed");
                    }

                    return pose;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Error processing frame: {ex.Message}");
                Console.WriteLine($"[FaceTrackingService] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private async Task<(float x, float y, float width, float height)?> DetectFaceAsync(Image<Rgb24> image)
        {
            Console.WriteLine("[FaceTrackingService] Entry: DetectFaceAsync");

            if (_faceDetectionSession == null)
            {
                Console.WriteLine("[FaceTrackingService] Exit: DetectFaceAsync - session is null");
                return null;
            }

            try
            {
                // Resize image to detection size
                using (var resized = image.Clone(ctx => ctx.Resize(FaceDetectionWidth, FaceDetectionHeight)))
                {
                    Console.WriteLine($"[FaceTrackingService] Image resized to {FaceDetectionWidth}x{FaceDetectionHeight}");

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

                    Console.WriteLine("[FaceTrackingService] Tensor filled, running inference");

                    // Run inference
                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input", tensor)
                    };

                    using (var results = _faceDetectionSession.Run(inputs))
                    {
                        Console.WriteLine("[FaceTrackingService] Inference complete");
                        var output = results.FirstOrDefault()?.AsEnumerable<float>().ToArray();

                        if (output == null)
                        {
                            Console.WriteLine("[FaceTrackingService] Exit: DetectFaceAsync - output is null");
                            return null;
                        }

                        Console.WriteLine($"[FaceTrackingService] Output length: {output.Length}");
                        if (output.Length > 0)
                        {
                            Console.WriteLine($"[FaceTrackingService] Output values: [{string.Join(", ", output.Take(Math.Min(10, output.Length)).Select(v => v.ToString("F4")))}...]");
                        }

                        if (output.Length >= 4)
                        {
                            Console.WriteLine($"[FaceTrackingService] Raw output[0-3]: x={output[0]:F4}, y={output[1]:F4}, w={output[2]:F4}, h={output[3]:F4}");

                            // Check if output format appears to be [x1, y1, x2, y2] (corner coordinates)
                            // vs [x, y, width, height] format
                            bool isCornerFormat = output[2] > output[0] && output[3] > output[1]; // x2 > x1 and y2 > y1
                            Console.WriteLine($"[FaceTrackingService] Detected format: {(isCornerFormat ? "corner coordinates" : "x,y,width,height")}");

                            float scaleX = (float)image.Width;
                            float scaleY = (float)image.Height;

                            (float x, float y, float width, float height) result;

                            if (isCornerFormat)
                            {
                                // Convert from [x1, y1, x2, y2] to [x, y, width, height]
                                float x1 = (output[0] + 1f) * 0.5f * scaleX; // Normalize from [-1,1] to [0,1] then scale
                                float y1 = (output[1] + 1f) * 0.5f * scaleY;
                                float x2 = (output[2] + 1f) * 0.5f * scaleX;
                                float y2 = (output[3] + 1f) * 0.5f * scaleY;

                                result = (
                                    Math.Max(0, x1),
                                    Math.Max(0, y1),
                                    Math.Max(0, x2 - x1),
                                    Math.Max(0, y2 - y1)
                                );
                            }
                            else
                            {
                                // Assume [centerX, centerY, width, height] format, normalize from [-1,1] to [0,1]
                                float centerX = (output[0] + 1f) * 0.5f * scaleX;
                                float centerY = (output[1] + 1f) * 0.5f * scaleY;
                                float width = (output[2] + 1f) * 0.5f * scaleX;
                                float height = (output[3] + 1f) * 0.5f * scaleY;

                                // Convert from center format to top-left format
                                result = (
                                    Math.Max(0, centerX - width / 2),
                                    Math.Max(0, centerY - height / 2),
                                    Math.Max(0, width),
                                    Math.Max(0, height)
                                );
                            }

                            Console.WriteLine($"[FaceTrackingService] Exit: DetectFaceAsync - Success: x={result.Item1:F2}, y={result.Item2:F2}, w={result.Item3:F2}, h={result.Item4:F2}");
                            return result;
                        }
                        else
                        {
                            Console.WriteLine($"[FaceTrackingService] Exit: DetectFaceAsync - Insufficient output length: {output.Length}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Face detection error: {ex.Message}");
                Console.WriteLine($"[FaceTrackingService] Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("[FaceTrackingService] Exit: DetectFaceAsync - Returning null");
            return null;
        }

        private async Task<Vector2[]?> DetectLandmarksAsync(Image<Rgb24> image, (float x, float y, float width, float height) faceBox)
        {
            Console.WriteLine("[FaceTrackingService] Entry: DetectLandmarksAsync");

            if (_landmarkSession == null)
            {
                Console.WriteLine("[FaceTrackingService] Exit: DetectLandmarksAsync - session is null");
                return null;
            }

            // Validate face box parameters
            if (faceBox.width <= 0 || faceBox.height <= 0)
            {
                Console.WriteLine($"[FaceTrackingService] Exit: DetectLandmarksAsync - Invalid face box dimensions: w={faceBox.width}, h={faceBox.height}");
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

                Console.WriteLine($"[FaceTrackingService] Face box: x={faceBox.x:F2}, y={faceBox.y:F2}, w={faceBox.width:F2}, h={faceBox.height:F2}");
                Console.WriteLine($"[FaceTrackingService] Crop region: x={cropX}, y={cropY}, w={cropWidth}, h={cropHeight}");
                Console.WriteLine($"[FaceTrackingService] Image size: {image.Width}x{image.Height}");

                if (cropWidth <= 0 || cropHeight <= 0)
                {
                    Console.WriteLine($"[FaceTrackingService] Exit: DetectLandmarksAsync - Invalid crop dimensions: w={cropWidth}, h={cropHeight}");
                    return null;
                }

                using (var cropped = image.Clone(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight))))
                using (var resized = cropped.Clone(ctx => ctx.Resize(LandmarkWidth, LandmarkHeight)))
                {
                    Console.WriteLine($"[FaceTrackingService] Face region resized to {LandmarkWidth}x{LandmarkHeight}");

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

                    Console.WriteLine("[FaceTrackingService] Tensor filled, running landmark inference");

                    // Run inference
                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input", tensor)
                    };

                    using (var results = _landmarkSession.Run(inputs))
                    {
                        Console.WriteLine("[FaceTrackingService] Landmark inference complete");
                        var output = results.FirstOrDefault()?.AsEnumerable<float>().ToArray();

                        if (output == null)
                        {
                            Console.WriteLine("[FaceTrackingService] Exit: DetectLandmarksAsync - output is null");
                            return null;
                        }

                        Console.WriteLine($"[FaceTrackingService] Landmark output length: {output.Length}");
                        if (output.Length > 0)
                        {
                            Console.WriteLine($"[FaceTrackingService] Landmark sample values: [{string.Join(", ", output.Take(Math.Min(10, output.Length)).Select(v => v.ToString("F4")))}...]");
                        }

                        if (output.Length >= 132) // 66 landmarks * 2 coordinates
                        {
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

                            Console.WriteLine($"[FaceTrackingService] Exit: DetectLandmarksAsync - Success with {landmarks.Length} landmarks");
                            return landmarks;
                        }
                        else
                        {
                            Console.WriteLine($"[FaceTrackingService] Exit: DetectLandmarksAsync - Insufficient output length: {output.Length}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FaceTrackingService] Landmark detection error: {ex.Message}");
                Console.WriteLine($"[FaceTrackingService] Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("[FaceTrackingService] Exit: DetectLandmarksAsync - Returning null");
            return null;
        }

        private HeadPose? CalculateHeadPose(Vector2[] landmarks, int imageWidth, int imageHeight)
        {
            Debug.WriteLine("[FaceTrackingService] Entry: CalculateHeadPose");

            try
            {
                // Extract the key landmarks we need
                var image2DPoints = new Vector2[LandmarkIndices.Length];
                for (int i = 0; i < LandmarkIndices.Length; i++)
                {
                    image2DPoints[i] = landmarks[LandmarkIndices[i]];
                }

                Debug.WriteLine($"[FaceTrackingService] Key landmarks extracted: {image2DPoints.Length} points");

                // Simplified camera matrix (focal length estimation)
                float focalLength = imageWidth;
                float cx = imageWidth / 2f;
                float cy = imageHeight / 2f;

                Debug.WriteLine($"[FaceTrackingService] Camera params: focal={focalLength}, cx={cx}, cy={cy}");

                // Use simplified pose estimation
                // For a full implementation, you'd use OpenCV's solvePnP
                // Here we'll do a basic estimation from landmark positions

                // Calculate center of face
                float centerX = image2DPoints.Average(p => p.X);
                float centerY = image2DPoints.Average(p => p.Y);

                Debug.WriteLine($"[FaceTrackingService] Face center: ({centerX:F2}, {centerY:F2})");

                // Estimate distance based on face size
                float faceWidth = Math.Abs(image2DPoints[2].X - image2DPoints[3].X); // Eye corners
                float avgFaceWidthMm = 140f; // Average human face width in mm
                float z = (avgFaceWidthMm * focalLength) / Math.Max(faceWidth, 1f);

                Debug.WriteLine($"[FaceTrackingService] Face width: {faceWidth:F2}, estimated Z: {z:F2}");

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

                Debug.WriteLine($"[FaceTrackingService] Rotation: Yaw={yaw:F2}, Pitch={pitch:F2}, Roll={roll:F2}");

                // Convert 2D center to 3D position
                float x = (centerX - cx) * z / focalLength;
                float y = (centerY - cy) * z / focalLength;

                Debug.WriteLine($"[FaceTrackingService] Position: X={x:F2}, Y={-y:F2}, Z={z:F2}");
                Debug.WriteLine("[FaceTrackingService] Exit: CalculateHeadPose - Success");

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
                Debug.WriteLine($"[FaceTrackingService] Pose calculation error: {ex.Message}");
                Debug.WriteLine($"[FaceTrackingService] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        public void Dispose()
        {
            Debug.WriteLine("[FaceTrackingService] Entry: Dispose");

            _faceDetectionSession?.Dispose();
            _faceDetectionSession = null;

            _landmarkSession?.Dispose();
            _landmarkSession = null;

            _isInitialized = false;

            Debug.WriteLine("[FaceTrackingService] Exit: Dispose");
        }
    }
}
