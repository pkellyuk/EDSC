using EDSC.Services;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        public bool IsInitialized
        {
            get
            {
                return _isInitialized;
            }
        }

        public async Task InitializeAsync(string modelsPath)
        {
            Debug.WriteLine("[FaceTrackingService] Entry: InitializeAsync");

            if (string.IsNullOrEmpty(modelsPath))
            {
                Debug.WriteLine("[FaceTrackingService] modelsPath is null or empty");
                throw new ArgumentNullException(nameof(modelsPath));
            }

            if (!Directory.Exists(modelsPath))
            {
                Debug.WriteLine($"[FaceTrackingService] Models directory not found: {modelsPath}");
                throw new DirectoryNotFoundException($"Models directory not found: {modelsPath}");
            }

            try
            {
                var faceDetectionPath = Path.Combine(modelsPath, "detection.onnx");
                var landmarkPath = Path.Combine(modelsPath, "lm_fast_exp1.onnx");

                if (!File.Exists(faceDetectionPath))
                {
                    Debug.WriteLine($"[FaceTrackingService] Face detection model not found: {faceDetectionPath}");
                    throw new FileNotFoundException($"Face detection model not found: {faceDetectionPath}");
                }

                if (!File.Exists(landmarkPath))
                {
                    Debug.WriteLine($"[FaceTrackingService] Landmark model not found: {landmarkPath}");
                    throw new FileNotFoundException($"Landmark model not found: {landmarkPath}");
                }

                Debug.WriteLine("[FaceTrackingService] Loading face detection model");
                _faceDetectionSession = new InferenceSession(faceDetectionPath);

                Debug.WriteLine("[FaceTrackingService] Loading landmark model");
                _landmarkSession = new InferenceSession(landmarkPath);

                _isInitialized = true;

                Debug.WriteLine("[FaceTrackingService] Initialization complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FaceTrackingService] Error during initialization: {ex.Message}");
                throw;
            }

            Debug.WriteLine("[FaceTrackingService] Exit: InitializeAsync");
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
                Debug.WriteLine("[FaceTrackingService] Service not initialized");
                return null;
            }

            try
            {
                // Load image from bytes
                using (var image = Image.Load<Rgb24>(frameData))
                {
                    // TODO: Implement face detection
                    // TODO: Implement landmark detection
                    // TODO: Calculate head pose

                    // For now, return dummy data to test the pipeline
                    return new HeadPose
                    {
                        X = 0,
                        Y = 0,
                        Z = 600,
                        Yaw = 0,
                        Pitch = 0,
                        Roll = 0
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FaceTrackingService] Error processing frame: {ex.Message}");
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
