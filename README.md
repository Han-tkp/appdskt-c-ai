# DropDetect AI (C# Application)

![DropDetect Banner](https://via.placeholder.com/800x200?text=DropDetect+AI+-+Microscopy+Droplet+Analysis)

**DropDetect** is a cutting-edge desktop application built with **C# (.NET 8 Avalonia)** and **OpenCvSharp**. It utilizes advanced AI (YOLOv8 via ONNX Runtime) to accurately detect, measure, and analyze liquid droplets from real-time USB microscope camera feeds.

The system is specifically engineered to replace manual measurement processes, offering real-time AI computer vision that perfectly matches manual bounding box dimensions at the sub-micron level.

---

## 🎯 Key Features

- **Real-Time AI Detection**: Uses YOLOv8 Object Detection to instantly recognize and frame droplets.
- **Accurate Sub-Micron Measurement**: Converts pixel sizes to true micrometers ($\mu m$) using highly calibrated hardware profiles (e.g., `0.279 µm/px` for a 10x lens).
- **Geometric Precision**: Calculates true diameter by averaging the Tight Bounding Box Width and Height ($ \frac{W + H}{2} $), eliminating area-based distortion algorithms.
- **Absolute AI Freezing**: Implements a permanent "Dimension Freeze" upon first detection, completely stopping numerical fluctuation (Jitter) while maintaining droplet tracking IDs.
- **Microscope Camera Integration**: Deeply integrates with high-resolution USB microscopes, bypassing standard webcams and virtual cameras (like OBS).
- **Automated Excel Reporting**: Generates beautiful `.xlsx` reports with embedded **Conditional Formatting (Data Bars)** for intuitive Volume Median Diameter (VMD) and SPAN analysis.

---

## 🖼️ User Interface 

The Application features a clean desktop UI designed for laboratory environments:
- **Left Panel**: Quick-access settings including Lens Selection dropdown (`10x`, `4x`), Camera Resolution overrides, and inference Confidence Threshold sliders.
- **Center Canvas**: Live microscope feed with Azure-Blue overlaid bounding boxes, unique Droplet IDs, and real-time micrometer thickness labels.
- **Right Panel (Dashboard)**: Real-time statistical analysis charts, VMD metrics, and immediate Excel Report generation.

---

## 🚀 Getting Started

### Prerequisites
- **Framework**: [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- **IDE**: Visual Studio 2022 (Windows/Mac) or Visual Studio Code
- **AI Model**: YOLOv8 `.onnx` weights (Opset 12 recommended)

### 🌍 Cross-Platform Build & Run

Since DropDetect is built on Avalonia UI, it natively supports deployment across multiple operating systems. Ensure you have the `.NET 8.0 SDK` installed before running these commands.

1. **Clone the repository**:
   ```bash
   git clone https://github.com/Han-tkp/appdskt-c-ai.git
   cd appdskt-c-ai/DropDetect
   ```

2. **Restore Dependencies**:
   ```bash
   dotnet restore
   ```

3. **Run or Publish by Operating System**:

   #### 🪟 Windows (x64)
   *Run directly in development:*
   ```bash
   dotnet run
   ```
   *Publish as a standalone `.exe`:*
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   ```

   #### 🐧 Linux (x64)
   *Note: Ensure you have `libfontconfig1` and `libfreetype6` installed on your distro for UI rendering.*
   ```bash
   dotnet run
   ```
   *Publish as a standalone compiled binary:*
   ```bash
   dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
   ```

   #### 🍎 macOS (Apple Silicon ARM64 & Intel x64)
   *For M1/M2/M3 (ARM64):*
   ```bash
   dotnet publish -c Release -r osx-arm64 --self-contained
   ```
   *For Intel Macs (x64):*
   ```bash
   dotnet publish -c Release -r osx-x64 --self-contained
   ```

4. **Provide AI Brain (ONNX Model)**:
   Place your custom-trained YOLOv8 `.onnx` weights into the designated project directory (or adjust the hardcoded path within `VisionService.cs`).

---

## 🛠️ Calibration Setup

For DropDetect to render accurate physical dimensions, the lens calibration cache must match the physical microscope hardware:

1. The app auto-generates calibration configurations in `bin/Debug/net8.0/data/calibration/`.
2. By default, the system applies predefined ratios aligned with the user's specific hardware calibrations:
   - **4x Lens**: ~ `0.692 µm/px`
   - **10x Lens**: ~ `0.279 µm/px`

*(If measurements appear distorted, clear the `.json` cache in that folder and restart the app to force regeneration).*

---

## 💻 Tech Stack
- **UI Framework**: Avalonia UI
- **Computer Vision**: OpenCvSharp4 
- **AI Inference Engine**: Microsoft ML ONNX Runtime
- **Exporting Engine**: ClosedXML

---
*Developed as an AI-driven migration from legacy Python scripts to a fully native, fast, and robust C# Windows Application.*
