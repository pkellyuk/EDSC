# ONNX Models for Face Tracking

This directory should contain the ONNX models needed for face tracking.

## Required Models

To use the face tracking feature, you need to download the following models from AITrack:

1. **detection.onnx** - Face detection model
2. **lm_fast_exp1.onnx** - Fast landmark detection model (or lm_m.onnx, lm_f.onnx for better accuracy)

## Where to Get the Models

### Option 1: Download from AITrack GitHub Releases

1. Go to: https://github.com/AIRLegend/aitrack/releases
2. Download the latest release ZIP
3. Extract the files
4. Copy the `.onnx` files from the `models` folder to this directory

### Option 2: Clone AITrack Repository

```bash
git clone https://github.com/AIRLegend/aitrack.git
cd aitrack
# Copy models to your EDSC Models directory
cp models/*.onnx /path/to/EDSC/src/EDSC.Desktop/Models/
```

## Model Variants

AITrack provides different landmark models with varying accuracy/speed tradeoffs:

- **lm_fast_exp1.onnx** - Fastest, lowest precision (recommended for testing)
- **lm_m.onnx** - Medium speed and precision
- **lm_f.onnx** - Slower, highest precision

The application is configured to use `lm_fast_exp1.onnx` by default.

## File Structure

After downloading, this directory should contain:

```
Models/
├── README.md (this file)
├── detection.onnx
└── lm_fast_exp1.onnx
```

## License Note

The ONNX models are part of the AITrack project:
- Repository: https://github.com/AIRLegend/aitrack
- License: MIT License
- Credit to AIRLegend for creating these models
