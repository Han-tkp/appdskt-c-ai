# DropDetect AI - WHO Standard Droplet Analysis (C# .NET 8)

![DropDetect Banner](https://via.placeholder.com/800x200?text=DropDetect+AI+-+Microscopy+Droplet+Analysis)

**DropDetect** เป็นแอปพลิเคชัน Desktop ระดับองค์กร (Enterprise Desktop Application) ที่ถูกพัฒนาขึ้นใหม่ทั้งหมดด้วยภาษา **C# (.NET 8)** และโครงสร้าง **Avalonia UI** เพื่อใช้ทดแทนระบบ Python เดิม โดยมีจุดประสงค์หลักในการวิเคราะห์และประเมินขนาดละอองเครื่องพ่นหมอกควันผ่านวิดีโอกล้องจุลทรรศน์แบบ Real-time ให้สอดคล้องกับมาตรฐานขององค์การอนามัยโลก (WHO)

โปรแกรมผสานการทำงานของ **Computer Vision (OpenCV)** และ **AI Object Detection (YOLOv8)** เพื่อตรวจจับ กรอบหยดละออง พร้อมทั้งคำนวณค่าทางสถิติที่สำคัญเชิงลึกในระดับ Sub-micron ได้อย่างแม่นยำและเสถียร

---

## 🎯 ฟีเจอร์เด่น (Key Features & Innovations)

ตลอดเส้นทางการพัฒนากว่า 23 Phase โปรแกรมนี้ถูกยกระดับในทุกมิติ:

- **1. AI Inference คู่ตรรกะ (Dual Model Hotswapping)**
  รองรับการประมวลผลโมเดล YOLOv8 ผ่าน **ONNX Runtime** สามารถสลับสมอง AI ระหว่างเลนส์ 4x (`yolov8n_4x.onnx`) และ 10x (`yolov8n_10x.onnx`) กลางอากาศขณะกล้องยังทำงานอยู่ และรองรับการประมวลผลทั้ง `Hardware GPU (DirectML)` และ `CPU` หากสเปคเครื่องไม่เอื้ออำนวย
- **2. เครื่องมือชี้วัดความแม่นยำสูง (Precision Calibration)**
  วัดระยะพิกัดตกกระทบใหม่ให้ใกล้เคียงกับขนาดฮาร์ดแวร์จริงที่สุด โดยล้มเลิกการคำนวณทรงกลมด้วย Pi และใช้รูปแบบ `(กว้าง + ยาว) / 2` เพื่อกำจัดค่านอกข่ายกว่า 12.8% พร้อมตัวกรองขนาด 1µm-300µm กำจัดนอยส์เด็ดขาด
- **3. AI จอแช่แข็งเสถียรภาพ (Absolute Droplet Freezing)**
  ล็อคขนาด Dimension X,Y ของหยดละอองเป็นค่าคงที่ตลอดกาลตั้งแต่เฟรมแรกสุดที่ AI ค้นพบ ซึ่งช่วยแก้ปัญหาค่าวัด (VMD/SPAN) กระเพื่อมสั่นรัวจากภาวะแสงกล้องเต้น
- **4. ระบบแกะรอยอัจฉริยะแบบลบได้ (Smart Tracking & Interactive UI)**
  จำแนกหยดน้ำด้วย **Tracking ID** โดยระบบกันการนับซ้ำเม็ดเดิมขณะขยับกล้อง และเสริมด้วย **Interactive UI Deletion** ที่ให้ผู้ใช้งานคลิกเมาส์เจาะจงลงบนวิดีโอเพื่อลบเม็ดน้ำที่มีตำหนิออกจากหน้าจอและฐานข้อมูลสถิติได้ทันที 
- **5. สุ่มประเมินยอดตามสถิติวิจัย (Statistical Downsampling)**
  ถึงผู้ใช้จะสแกนละอองไป 600 หยด แต่สามารถกรอกขอ Target ไว้แค่ 200 หยด ระบบจะใช้คณิตศาสตร์ Stratified Sampling หารสุ่มตัวแทนมาจัดเรียงใหม่ เพื่อให้กราฟ D10, D50, D90 ตรงกับกราฟต้นฉบับดั้งเดิม (Zero VMD Loss) 
- **6. ฐานข้อมูลจัดการสไลด์และ Workflow**
  สร้างรายการหน้าจอ Slide Tracker เพื่อรวมกลุ่มก้อนข้อมูลแยกสไลด์ (เช่น สไลด์ที่ 1, สไลด์ที่ 2) ให้จัดการข้อมูลทีละล็อตก่อนสั่งสรุปผลรวมส่งออกไฟล์
- **7. สร้างสรุปตาราง Excel อย่างชาญฉลาด (Automated Smart Excel)**
  ใช้งาน `ClosedXML` ทอแผ่นงาน Excel มาตรฐาน WHO (ทต.ท่าช้าง) ทั้งค่าสถิติสะสม (Cumulative Volume) แบบหน้าต่อหน้า และหน้า Summary สรุปรายชื่อสไลด์ที่ตีกรอบด้วย **สีแดง-สีเขียวบนเงื่อนไขตาราง (Data Bars)** วัดเกณฑ์ความชี้วัดสอบผ่านให้สำเร็จรูป
- **8. ดีไซน์ไร้พรมแดน Modern UX/UI**
  ออกแบบด้วย **Avalonia UI** รองรับโหมดสี Dark, Light, System Color และขจัดขอบหน้าต่างแข็งๆ ของ Windows เป็น **Seamless Custom Title Bar** ไร้รอยต่อ

---

## � Tech Stack (โครงสร้างสถาปัตยกรรม)

1. **[Avalonia UI](https://avaloniaui.net/) (v11.3.12)**: ทำหน้าที่สร้าง UI Framework ครอสแพลตฟอร์ม ควบคุม Layout ทั้งหมด และจัดการ Dynamic Theme 
2. **CommunityToolkit.Mvvm (v8.0.0)**: หัวใจในการควบคุมหลังบ้าน ผูกข้อมูลระหว่างหน้าจอและโลจิก XAML ให้แยกขาดจากกัน (MVVM Design Pattern)
3. **[OpenCvSharp4](https://github.com/shimat/opencvsharp) (v4.13.0)**: ดูแลการกวาดอ่านกล้องผ่านระบบ DirectShow, ขยายเฟรมภาพแบบ Real-time, วาดรูปวาดหมายเลข Tracking ID ซ้อนทับบนวิดีโอเพียวๆ
4. **[Microsoft.ML.OnnxRuntime](https://onnxruntime.ai/) (v1.17.1)**: ขับเคลื่อนสมอง AI ด้วยการโหลดโมเดล ONNX ผ่านค่ายาวๆ ของ Graphic Card 
5. **[ClosedXML](https://github.com/ClosedXML/ClosedXML) (v0.105.0)**: จัดการส่งออกหน้า Excel รายงานผลระดับพรีเมียม ไร้ความจำเป็นต้องติดตั้ง Microsoft Office บนเครื่อง

---

## 🚀 เริ่มต้นการใช้งาน (Getting Started)

เนื่องจากโปรเจคนี้อิงกับ Avalonia UI จึงใช้เครื่องมือจาก .NET 8 ในการประกอบและกระจายซอร์สโค้ด

### สิ่งที่ระบบต้องการ
- **Framework**: [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) 
- **AI Model**: ตัวไฟล์ `.onnx` ของ YOLOv8 จะต้องถูกวางไว้ที่ `DropDetect/fileonnx/` เพื่อให้โปรแกรมโหลดสมองกลเองได้  

### คำสั่งสากลในการ Build & Run

**ดาวน์โหลดหรือโคลนโปรเจค**:
```bash
git clone https://github.com/Han-tkp/appdskt-c-ai.git
cd appdskt-c-ai/DropDetect
```

**รันโปรแกรมสดระหว่างการพัฒนา**:
```bash
dotnet run
```

**สร้างเป็นไฟล์ Executable (.exe) ใช้งานพร้อมแจกจ่าย (Standalone)**:
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```
*(ไฟล์แอปพลิเคชันจะอยู่ใน `/bin/Release/net8.0/win-x64/publish/DropDetect.exe` พร้อมเปิดงานได้ทันทีโดยไม่ต้องลงโปรแกรมต่อพ่วงเพิ่มเติม)*

---

## 🔒 ข้อมูลความปลอดภัย (Storage Info)
การจัดการไฟล์ Cache, Log ความผิดปกติระหว่างรันกล้องจุลทรรศน์ และไฟล์กำหนดค่า (Calibration) ทั้งหมด ถูกย้ายไปฝังซ่อนไว้ที่ `%APPDATA%/DropDetect/` ของผู้ใช้เท่านั้น เพื่อหลบเลี่ยงปัญหาการจัดการสิทธิ (Permission Access) ของ Windows (เช่น เครื่องในโซนราชการ) ทำให้พร้อมติดตั้งลงตรงไหนก็ได้

---
*Created dynamically for modern microscopy tracking operations. (Migrated structurally from an experimental Python codebase into a native C# Desktop Standard).*
