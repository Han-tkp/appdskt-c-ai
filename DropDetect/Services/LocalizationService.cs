using System.Collections.Generic;

namespace DropDetect.Services;

public interface ILocalizationService
{
    string CurrentLanguage { get; set; }
    string GetString(string key);
    Dictionary<string, string> GetAllStrings();
}

public class LocalizationService : ILocalizationService
{
    private string _currentLanguage = "English";
    public string CurrentLanguage 
    { 
        get => _currentLanguage; 
        set => _currentLanguage = value; 
    }

    private readonly Dictionary<string, Dictionary<string, string>> _resources = new()
    {
        ["English"] = new()
        {
            ["VmdLabel"] = "VMD (Dv0.5)",
            ["InFrameLabel"] = "In-Frame",
            ["AccumulatedLabel"] = "Accumulated",
            ["SpanLabel"] = "SPAN",
            ["OutOfBoundsLabel"] = "Out-of-Bounds",
            ["CurrentSlideLabel"] = "Current Slide",
            ["SlideStatusLabel"] = "Slide Status",
            ["TargetSizeLabel"] = "Target Size",
            ["TabAnalyze"] = "🔬 Analyze",
            ["TabReport"] = "📊 Report",
            ["FileStoragePath"] = "File Storage Path",
            ["CurrentOutputDir"] = "Current Output Directory:",
            ["BtnChangeOutputDir"] = "📁 Change Output Directory",
            ["BtnSaveSnapshot"] = "📷 Save Manual Snapshot",
            ["HardwareAcceleration"] = "Hardware Acceleration & Engine",
            ["ExecutionProvider"] = "Execution Provider:",
            ["CameraBackendApi"] = "Camera Backend API:",
            ["CameraResolution"] = "Camera Resolution:",
            ["SystemHardwareInfo"] = "System Hardware Information:",
            ["AiConfidenceThreshold"] = "AI Confidence Threshold:",
            ["SnapshotHotkey"] = "Snapshot Hotkey:",
            ["SnapshotHoldDuration"] = "Snapshot Hold Duration:",
            ["AppearanceSettings"] = "Appearance Settings",
            ["ColorTheme"] = "Color Theme:",
            ["ObjLens"] = "Objective Lens:",
            ["AnalysisMode"] = "Analysis Mode",
            ["FilterSizeUm"] = "🔍 Filter Size µm",
            ["SlideWorkflowTracker"] = "Slide Workflow Tracker:",
            ["ItemsInReport"] = "Items in Report:",
            ["LocNameData"] = "Location Name Data:",
            ["BtnAddData"] = "➕ Add data to Excel Report",
            ["TotalDropletsRecorded"] = "Total Droplets Recorded",
            ["BtnExportExcel"] = "💾 Export to Excel",
            ["ReadyStatus"] = "Ready - Waiting to start camera",
            ["LiveAnalysisActive"] = "Live Analysis Mode Active.",
            ["LiveAnalysisStopped"] = "Live Analysis Stopped.",
            ["SnapshotAnalysing"] = "📸 Snapshot taken — analysing frame..."
        },
        ["ภาษาไทย"] = new()
        {
            ["VmdLabel"] = "VMD (เฉลี่ยหยดน้ำ)",
            ["InFrameLabel"] = "หยดในภาพ",
            ["AccumulatedLabel"] = "หยดสะสมรวม",
            ["SpanLabel"] = "การกระจาย",
            ["OutOfBoundsLabel"] = "เกินระยะคำนวณ",
            ["CurrentSlideLabel"] = "สไลด์ปัจจุบัน",
            ["SlideStatusLabel"] = "สถานะสไลด์",
            ["TargetSizeLabel"] = "จำนวนที่ต้องการ",
            ["TabAnalyze"] = "🔬 วิเคราะห์",
            ["TabReport"] = "📊 รายงาน",
            ["FileStoragePath"] = "เส้นทางจัดเก็บไฟล์ข้อมูล",
            ["CurrentOutputDir"] = "ไดเรกทอรีปัจจุบันที่บันทึก:",
            ["BtnChangeOutputDir"] = "📁 เปลี่ยนโฟลเดอร์สำหรับบันทึกผล",
            ["BtnSaveSnapshot"] = "📷 บันทึกภาพหน้าจอแบบแมนนวล",
            ["HardwareAcceleration"] = "การเร่งฮาร์ดแวร์และเครื่องยนต์ AI",
            ["ExecutionProvider"] = "ตัวประมวลผล (Execution Provider):",
            ["CameraBackendApi"] = "ระบบเชื่อมต่อกล้อง (API):",
            ["CameraResolution"] = "ความละเอียดกล้อง (Resolution):",
            ["SystemHardwareInfo"] = "ข้อมูลฮาร์ดแวร์ของระบบ:",
            ["AiConfidenceThreshold"] = "ความมั่นใจของ AI (Confidence Threshold):",
            ["SnapshotHotkey"] = "ปุ่มลัดสำหรับถ่าย Snapshot:",
            ["SnapshotHoldDuration"] = "ระยะเวลาค้างหน้าจอ Snapshot:",
            ["AppearanceSettings"] = "การตั้งค่ารูปลักษณ์โปรแกรม",
            ["ColorTheme"] = "ธีมสีโปรแกรม (Theme):",
            ["ObjLens"] = "กำลังขยายเลนส์ (Lens):",
            ["AnalysisMode"] = "โหมดวิเคราะห์ (Mode):",
            ["FilterSizeUm"] = "🔍 กรองขนาดไมครอน (Filter µm)",
            ["SlideWorkflowTracker"] = "ระบบติดตามสไลด์งาน:",
            ["ItemsInReport"] = "จำนวนข้อมูลในรายงาน:",
            ["LocNameData"] = "ข้อมูลสถานที่หรือชื่อสไลด์:",
            ["BtnAddData"] = "➕ เพิ่มเข้าตารางรายงาน Excel",
            ["TotalDropletsRecorded"] = "ละอองทั้งหมดในสไลด์",
            ["BtnExportExcel"] = "💾 สร้างไฟล์ Excel ส่งออก",
            ["ReadyStatus"] = "พร้อมใช้งาน - รอเปิดกล้อง",
            ["LiveAnalysisActive"] = "เปิดโหมดวิเคราะห์สด AI แล้ว",
            ["LiveAnalysisStopped"] = "หยุดการวิเคราะห์สด AI แล้ว",
            ["SnapshotAnalysing"] = "📸 บันทึกภาพแล้ว — กำลังประมวลผลเม็ดน้ำยา..."
        }
    };

    public string GetString(string key)
    {
        if (_resources.TryGetValue(CurrentLanguage, out var lang) && lang.TryGetValue(key, out var value))
            return value;
        return key; // Fallback to key itself
    }

    public Dictionary<string, string> GetAllStrings()
    {
        return _resources.ContainsKey(CurrentLanguage) ? _resources[CurrentLanguage] : _resources["English"];
    }
}
