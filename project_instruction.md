# Project Specification: DropDetect (AI-Powered Droplet Size Analysis)

## 1. Project Overview
Build a Web Application using Python to analyze chemical droplet sizes from a microscope camera. The system will detect droplets, calculate their real-world size in micrometers (µm), and evaluate them against WHO standards.

## 2. Tech Stack
- **Web Framework:** Streamlit (Easy to debug via Chrome)
- **Computer Vision:** opencv-python-headless
- **AI Inference:** onnxruntime (DirectML/CPU)
- **Data & Math:** numpy, pandas

## 3. AI Model Details
- **Model File:** `best.onnx` (YOLOv11 Instance Segmentation)
- **Input Size:** 640x640 (RGB)
- **Classes:** 1 Class (Droplet)
- **Output:** Bounding boxes and Polygon Masks.

## 4. Core Logic & Math
### 4.1 Lens Calibration
The microscope has two lenses. The application must map pixels to micrometers (µm) based on the selected lens:
- **4x Lens:** 1 pixel = 2.54 µm (Example calibration ratio)
- **10x Lens:** 1 pixel = 1.02 µm (Example calibration ratio)

### 4.2 Size Calculation
For each detected polygon (droplet):
1. Calculate the area in pixels using `cv2.contourArea()`.
2. Calculate the equivalent diameter in pixels: `Diameter_px = 2 * sqrt(Area_px / PI)`
3. Convert to micrometers: `Diameter_µm = Diameter_px * Lens_Ratio`

### 4.3 WHO Standard Calculation (VMD & SPAN)
Use numpy to calculate these statistics from the list of all `Diameter_µm`:
- **Dv0.1:** 10th percentile size
- **Dv0.5 (VMD):** 50th percentile size (Median)
- **Dv0.9:** 90th percentile size
- **SPAN:** `(Dv0.9 - Dv0.1) / Dv0.5`
- **Pass/Fail Standard:** VMD must be between 10 µm and 30 µm.

## 5. Development Steps for AI IDE
Please implement the following steps in `app.py`:

**Step 1: UI Layout (Streamlit)**
- Create a sidebar for settings:
  - Dropdown to select Lens (4x or 10x).
  - File uploader for testing (accepts .jpg, .png).
- Main area to display the uploaded image and the processed image side-by-side.

**Step 2: ONNX Inference Engine**
- Write a function to load `best.onnx` using `onnxruntime.InferenceSession`.
- Preprocess the uploaded image (resize to 640x640, normalize /255.0, transpose to CHW, expand dims).
- Run inference and extract masks.

**Step 3: Post-processing & Drawing**
- Convert ONNX mask outputs back to image coordinates.
- Draw transparent green polygons over the detected droplets on the original image.
- Calculate VMD and SPAN.

**Step 4: Results Dashboard**
- Display VMD and SPAN using `st.metric()`.
- Display a Pass/Fail alert based on the WHO standard (VMD 10-30 µm).
- Show a Pandas DataFrame listing the sizes of all detected droplets.

## 6. Execution
Run `streamlit run app.py` and open the Chrome browser to verify the UI and logic. If there are any errors in the terminal, capture them and fix the code immediately.