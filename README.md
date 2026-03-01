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
- **Operating System**: Windows 10/11
- **Framework**: [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- **IDE**: Visual Studio 2022 or Visual Studio Code

### Installation

1. **Clone the repository**:
   ```bash
   git clone https://github.com/Han-tkp/appdskt-c-ai.git
   cd appdskt-c-ai/DropDetect
   ```

2. **Restore Dependencies**:
   Ensure you have all required NuGet packages installed.
   ```bash
   dotnet restore
   ```

3. **Provide AI Brain (ONNX Model)**:
   Place your custom-trained YOLOv8 `.onnx` weights into the designated project directory (or adjust the path within `VisionService.cs`).

4. **Run the Application**:
   ```bash
   dotnet run
   ```

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
