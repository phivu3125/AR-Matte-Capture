# AR Matte Capture

Real-time background removal and AR marker tracking in Unity, powered by neural-network video matting (RVM) and ArUco marker detection (OpenCV).

## Use Case

Camera-composited AR/MR — not VR. A physical webcam feed is processed in real time:

1. **RVM** removes the background from the live video, producing a clean foreground matte displayed on a Canvas `RawImage`.
2. **ArUco tracking** detects printed markers in the same camera feed and positions 3D objects in the scene to match marker locations, enabling interaction between virtual objects and the physical environment.

## Tech Stack

| Component        | Version                          | Notes                                 |
| ---------------- | -------------------------------- | ------------------------------------- |
| Unity            | 6000.0.60f1 (Unity 6)            |                                       |
| Render Pipeline  | URP 17.2.0                       | Universal Render Pipeline             |
| Unity Sentis     | 2.3.0 (`com.unity.ai.inference`) | ONNX model inference on GPU/CPU       |
| OpenCV for Unity | 4.5.5 (Enox Software)            | Unity asset wrapping OpenCV 4.5.5 C++ |

## Project Structure

```
Assets/
├── Models/
│   ├── rvm_mobilenetv3_fp16.onnx      # Lightweight RVM model
│   ├── rvm_mobilenetv3_fp32.onnx
│   ├── rvm_resnet50_fp16.onnx         # High-quality RVM model
│   └── rvm_resnet50_fp32.onnx
├── Scripts/
│   ├── RVMCore.cs                     # Real-time video matting pipeline
│   └── ArucoMarkerTracker.cs          # ArUco marker detection & tracking
├── Scenes/
│   └── Demo.unity                     # Main scene
└── OpenCVForUnity/                    # OpenCV 4.5.5 Unity plugin (Enox Software)
```

## How It Works

### 1. Real-Time Video Matting (RVMCore)

`RVMCore` captures frames from a webcam (default 1920x1080 @ 30 fps) and runs them through a Robust Video Matting (RVM) ONNX model via Unity Sentis.

- **Model options**: MobileNetV3 (fast, lightweight) or ResNet50 (higher quality). Both available in FP16 and FP32.
- **Backend modes**: GPU (compute shader) or CPU fallback.
- **Processing**: Configurable frame skip (`processEveryNFrames`) to balance quality vs. performance.
- **Mask post-processing**: Threshold, feather, erode, and dilate parameters to refine the alpha matte.
- **Output**: A composited `RenderTexture` with the foreground over a solid/transparent background, displayed on a Canvas `RawImage`. The `mirrorCamera` option flips the output horizontally for selfie-style display.
- **Public API**:
  - `GetSourceTexture()` — raw camera feed as `RenderTexture`
  - `GetWebCamTexture()` — underlying `WebCamTexture` for direct CPU access
  - `GetAlphaMaskTexture()` — the alpha matte `RenderTexture`

### 2. ArUco Marker Tracking (ArucoMarkerTracker)

`ArucoMarkerTracker` reads the webcam feed directly (bypassing the GPU pipeline) and detects ArUco markers using OpenCV.

- **Input**: `WebCamTexture` pixels via `Utils.webCamTextureToMat()` — zero-copy CPU read with a reusable `Color32[]` buffer. Only processes when `webcam.didUpdateThisFrame` is true.
- **Low-res detection**: Configurable detection resolution (default 320x240). The full-res webcam frame is resized on the CPU with `Imgproc.resize()` before detection. This significantly reduces OpenCV processing time without affecting RVM or display quality.
- **Detection**: `Aruco.detectMarkers()` finds markers, then 2D corner positions are averaged to get each marker's center pixel.
- **Coordinate mapping**: The 2D marker center is normalized to UV coordinates (0-1), then mapped to the Canvas `RawImage` world position via bilinear interpolation of the display quad's corners. This correctly handles mirroring and aspect ratio.
- **Smoothing**: `Vector3.SmoothDamp` runs every `Update()` frame (decoupled from detection rate) to interpolate the 3D object toward the latest detected position, ensuring smooth motion regardless of detection frame rate.
- **Frame skip**: `processEveryNFrames` throttle for additional control.

## Getting Started

### Prerequisites

- Unity 6 (6000.0.60f1 or compatible)
- [OpenCV for Unity](https://assetstore.unity.com/packages/tools/integration/opencv-for-unity-21088) (Enox Software, v2.6.0+)
- A webcam
- NVIDIA GPU recommended (for Sentis GPU backend)

### Setup

1. Clone the repository.
2. Open the project in Unity 6.
3. Import **OpenCV for Unity** from the Asset Store (not included in repo).
4. Open `Assets/Scenes/Demo.unity`.
5. Press Play.

## Configuration

All components are configured via the Unity Inspector on their respective GameObjects in the Demo scene.

### RVMCore

| Field                  | Default     | Description                              |
| ---------------------- | ----------- | ---------------------------------------- |
| Model Type             | MobileNetV3 | MobileNetV3 (fast) or ResNet50 (quality) |
| Use FP16 Model         | true        | Half-precision for faster inference      |
| Backend Mode           | GPU         | GPU compute or CPU fallback              |
| Process Every N Frames | 1           | Frame skip (1 = every frame)             |
| Webcam Resolution      | 1920x1080   | Requested webcam resolution              |
| Webcam FPS             | 30          | Requested frame rate                     |
| Mirror Camera          | true        | Flip horizontally for selfie view        |
| Mask Threshold         | 0.5         | Alpha cutoff                             |
| Mask Feather           | 0.02        | Edge softness                            |

### ArucoMarkerTracker

| Field                  | Default     | Description                             |
| ---------------------- | ----------- | --------------------------------------- |
| Dictionary ID          | DICT_4X4_50 | ArUco dictionary type                   |
| Target Marker ID       | 0           | Which marker ID to track                |
| Detection Width        | 320         | Low-res detection width (0 = full-res)  |
| Detection Height       | 240         | Low-res detection height (0 = full-res) |
| Process Every N Frames | 1           | Frame skip                              |
| Smooth Time            | 0.05        | SmoothDamp duration (seconds)           |
| Depth Offset           | 0           | Z-axis offset for tracked object        |

## License

This project uses third-party assets and models under their respective licenses:

- **RVM (Robust Video Matting)**: [Apache 2.0](https://github.com/PeterL1n/RobustVideoMatting)
- **OpenCV**: [Apache 2.0](https://opencv.org/license/)
- **OpenCV for Unity**: Commercial license (Enox Software)
