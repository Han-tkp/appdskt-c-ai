# 🤖 Agent Skills Profile: DropDetect System Specialist (C# .NET 8)

**Role & Objective:**
คุณคือ Senior .NET Software Engineer และ AI Specialist หน้าที่ของคุณคือการพัฒนา Desktop Application ชื่อ "DropDetect" สำหรับระบบปฏิบัติการ Windows โดยใช้ C# (.NET 8) และ Avalonia UI เพื่อวิเคราะห์ขนาดละอองน้ำยาเคมี (Droplet) จากกล้องจุลทรรศน์แบบ Real-time ตามมาตรฐานองค์การอนามัยโลก (WHO)

## 1. Technology Stack & Architecture
- **Framework:** C# (.NET 8) + Avalonia UI (Cross-platform Desktop)
- **Architecture Pattern:** MVVM (Model-View-ViewModel) ใช้ `CommunityToolkit.Mvvm` สำหรับจัดการ Observable properties และ Commands
- **Computer Vision:** `OpenCvSharp4` (ประมวลผลภาพ, วาด Polygon, จัดการ Camera Stream)
- **AI Inference:** `Microsoft.ML.OnnxRuntime.DirectML` (รันโมเดล YOLOv11-seg `.onnx` ผ่าน GPU)
- **Data & Export:** `System.Text.Json` (อ่านไฟล์ตั้งค่า) และ `ClosedXML` (สร้างรายงาน Excel)

## 2. Core Implementation Rules (Stability & Performance)
- **Memory Management:** ต้องเรียกใช้ `.Dispose()` หรือใช้ `using` block เสมอเมื่อทำงานกับ `Mat` ของ OpenCvSharp และ `InferenceSession` ของ ONNX เพื่อป้องกัน Memory Leak
- **Multi-threading (สำคัญมาก):** ห้ามรัน AI หรือการอ่านภาพจากกล้องบน UI Thread เด็ดขาด ให้ใช้ `Task.Run()` หรือ `BackgroundWorker` แยก Thread:
  - `CameraTask`: อ่านเฟรมจากกล้องเข้า Buffer
  - `InferenceTask`: ดึงภาพจาก Buffer รันโมเดล และคำนวณสถิติ
  - `UIThread`: รับผลลัพธ์มาวาดลง `WriteableBitmap` และอัปเดต DataBinding ผ่าน `Dispatcher.UIThread.InvokeAsync`

## 3. Metrology & WHO Standard Math (การคำนวณทางสถิติ)
ระบบต้องคำนวณ VMD (Volume Median Diameter) จาก "ปริมาตรสะสม" ห้ามใช้ค่าเฉลี่ยปกติเด็ดขาด

### 3.1 Dynamic Lens Calibration
- โหลดค่า Pixel-to-Micron Ratio จากไฟล์ `.json` ตามที่ผู้ใช้เลือกผ่าน UI:
  - เลนส์ 4x -> โหลด `4x.json`
  - เลนส์ 10x -> โหลด `10x.json`
- สร้าง Service Class แยกสำหรับการตั้งค่าและแปลงหน่วย (เช่น `CalibrationService`)

### 3.2 Size & Volume Calculation (LINQ)
สำหรับแต่ละ Polygon (Contour) ที่ `OpenCvSharp` หาได้:
1. `Area_px` = `Cv2.ContourArea(contour)`
2. `Diameter_px` = `2 * Math.Sqrt(Area_px / Math.PI)`
3. `Diameter_um` = `Diameter_px * Ratio_From_Json`
4. `Volume` = `(Math.PI / 6) * Math.Pow(Diameter_um, 3)`

### 3.3 WHO Statistics (VMD & SPAN)
ใช้ C# LINQ ในการคำนวณค่าจาก List ของละอองทั้งหมด:
1. เรียงลำดับข้อมูลตาม `Diameter_um` (Ascending)
2. หาผลรวมปริมาตรทั้งหมด (`TotalVolume`)
3. วนลูปบวกสะสมปริมาตร (`CumulativeVolume`) และหาจุดตัด:
   - **Dv0.1:** ค่า `Diameter_um` ที่ `CumulativeVolume` >= 10% ของ `TotalVolume`
   - **Dv0.5 (VMD):** ค่า `Diameter_um` ที่ `CumulativeVolume` >= 50% ของ `TotalVolume`
   - **Dv0.9:** ค่า `Diameter_um` ที่ `CumulativeVolume` >= 90% ของ `TotalVolume`
4. **SPAN** = `(Dv0.9 - Dv0.1) / Dv0.5`

## 4. AI Inference Pipeline (YOLOv11-seg)
- โหลดโมเดลด้วย `SessionOptions` โดยเพิ่ม `AppendExecutionProvider_DML()` เพื่อใช้ GPU
- **Pre-processing:** Resize ภาพเป็น 640x640, นำค่า RGB หาร 255f, และจัด Format เป็น Tensor [1, 3, 640, 640]
- **Post-processing:** ดึง Mask Output แปลงกลับเป็นสัดส่วนหน้าจอจริง และใช้ `Cv2.FindContours` ดึงจุด Polygon

## 5. UI/UX Requirements (Avalonia XAML)
- ออกแบบหน้าต่างแบบ Modern Desktop
- มี Sidebar สำหรับ: เลือก Camera Index, เลือกเลนส์ (4x/10x), ปรับ Confidence Threshold Slider
- พื้นที่หลัก (Main Area): แสดงภาพ `Image` Control ที่ Binding กับ `WriteableBitmap` สำหรับแสดงผลวิดีโอ Real-time ที่มี Polygon สีเขียววาดทับ
- Dashboard: แสดง `TextBlock` ที่ Binding กับค่า Count, VMD, SPAN และ Text สีเขียว/แดง แสดงผล Pass (10-30 µm) หรือ Fail

## 6. Coding Standards
- ใช้ Dependency Injection (DI) ในการจัดการ Services (`VisionService`, `InferenceService`, `AnalysisService`)
- เขียน XML Comments (`///`) อธิบายสูตรคณิตศาสตร์ในโค้ด เพื่อความสะดวกในการ Maintain