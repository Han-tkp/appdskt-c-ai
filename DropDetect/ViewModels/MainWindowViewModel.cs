using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using DropDetect.Services;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Data.Converters;
using System.Management;
using System.Runtime.Versioning;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using OpenCvSharp;

namespace DropDetect.ViewModels;

public class HardwareDevice : ObservableObject
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Performance { get; set; } = "ปานกลาง";
    public string PerformanceColor { get; set; } = "#FAB387";
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IVisionService _visionService;
    private readonly IExcelExportService _excelExportService;
    private readonly IAppSettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly IAnalysisService _analysisService;
    private readonly DispatcherTimer _telemetryTimer;

    [ObservableProperty] private bool _hasUnsavedChanges = false;
    [ObservableProperty] private string _hotkeyConflictMessage = "";

    [ObservableProperty] private string _statusText = "Ready - Waiting to start camera";
    [ObservableProperty] private WriteableBitmap? _cameraImage;
    [ObservableProperty] private ObservableCollection<string> _availableLenses = new() { "4x", "10x" };
    [ObservableProperty] private string _lensSelection = "10x";
    partial void OnLensSelectionChanged(string value) { HasUnsavedChanges = true; }

    [ObservableProperty] private bool _isSettingsOpen = false;
    [ObservableProperty] private ObservableCollection<HardwareDevice> _hardwareDevices = new();
    [ObservableProperty] private ObservableCollection<string> _availableHardware = new() { "CPU" };
    [ObservableProperty] private string _selectedHardware = "CPU";

    [ObservableProperty] private bool _isCameraRunning = false;
    partial void OnIsCameraRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CameraStatusColor));
        OnPropertyChanged(nameof(CameraStatusLabel));
    }

    [ObservableProperty] private bool _isCameraLoading = false;
    partial void OnIsCameraLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CameraStatusColor));
        OnPropertyChanged(nameof(CameraStatusLabel));
    }

    [ObservableProperty] private string _loadingMessage = "Loading...";

    public string CameraStatusColor => IsCameraLoading ? "#FAB387" : IsCameraRunning ? "#a6e3a1" : "#585b70";
    public string CameraStatusLabel => IsCameraLoading ? "Loading…" : IsCameraRunning ? "LIVE" : "Offline";

    partial void OnSelectedHardwareChanged(string value) { HasUnsavedChanges = true; }

    [ObservableProperty] private string _currentHardwareSpecs = "Loading...";

    [SupportedOSPlatform("windows")]
    private async Task FetchHardwareSpecsAsync()
    {
        var devices = new List<HardwareDevice>();
        var providers = new List<string> { "CPU" };

        await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "Unknown CPU";
                    devices.Add(new HardwareDevice { Name = name, Type = "CPU", Performance = "ดี", PerformanceColor = "#a6e3a1" });
                    break;
                }

                using var gpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                int gpuIdx = 0;
                foreach (var obj in gpuSearcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "Unknown GPU";
                    string perf = "ปานกลาง";
                    string color = "#FAB387";

                    if (name.Contains("RTX") || name.Contains("RX 6") || name.Contains("RX 7") || name.Contains("A7") || name.Contains("A5")) { perf = "ดีมาก"; color = "#a6e3a1"; }
                    else if (name.Contains("GTX") || name.Contains("RX 5") || name.Contains("Iris") || name.Contains("Quadro")) { perf = "ดี"; color = "#89b4fa"; }

                    devices.Add(new HardwareDevice { Name = name, Type = "GPU", Performance = perf, PerformanceColor = color });
                    providers.Add($"DirectML: {name} ({gpuIdx})");
                    gpuIdx++;
                }
            }
            catch { }
        });

        Dispatcher.UIThread.Post(() =>
        {
            HardwareDevices.Clear();
            foreach (var d in devices) HardwareDevices.Add(d);
            AvailableHardware.Clear();
            foreach (var p in providers) AvailableHardware.Add(p);
            CurrentHardwareSpecs = devices.Count > 0 ? $"{devices[0].Name}" : "N/A";
        });
    }

    [ObservableProperty] private ObservableCollection<string> _availableModels = new() { "yolov8n_4x.onnx", "yolov8n_10x.onnx" };
    [ObservableProperty] private string _selectedModelPath4x = "yolov8n_4x.onnx";
    [ObservableProperty] private string _selectedModelPath10x = "yolov8n_10x.onnx";

    partial void OnSelectedModelPath4xChanged(string value) { HasUnsavedChanges = true; }
    partial void OnSelectedModelPath10xChanged(string value) { HasUnsavedChanges = true; }

    private void RefreshModelList()
    {
        string onnxFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fileonnx");
        if (!Directory.Exists(onnxFolder)) Directory.CreateDirectory(onnxFolder);
        var files = Directory.GetFiles(onnxFolder, "*.onnx");
        Dispatcher.UIThread.Post(() =>
        {
            AvailableModels.Clear();
            foreach (var f in files) AvailableModels.Add(Path.GetFileName(f));

            // Fallback selection if saved setting not found
            if (!AvailableModels.Contains(SelectedModelPath4x) && AvailableModels.Contains("yolov8n_4x.onnx")) SelectedModelPath4x = "yolov8n_4x.onnx";
            if (!AvailableModels.Contains(SelectedModelPath10x) && AvailableModels.Contains("yolov8n_10x.onnx")) SelectedModelPath10x = "yolov8n_10x.onnx";
        });
    }

    [RelayCommand]
    private async Task ImportModel()
    {
        var picker = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
        if (picker == null) return;
        var files = await picker.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import ONNX Model", FileTypeFilter = new[] { new FilePickerFileType("ONNX Model") { Patterns = new[] { "*.onnx" } } } });
        if (files.Count > 0)
        {
            string onnxFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fileonnx");
            string dest = Path.Combine(onnxFolder, files[0].Name);
            File.Copy(files[0].Path.LocalPath, dest, true);
            RefreshModelList();
            StatusText = $"Model imported: {files[0].Name}";
        }
    }

    [RelayCommand] private void OpenModelsFolder() => Process.Start(new ProcessStartInfo { FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fileonnx"), UseShellExecute = true });

    [ObservableProperty] private ObservableCollection<string> _availableThemes = new() { "System", "Dark", "Light" };
    [ObservableProperty] private string _selectedTheme = "System";
    partial void OnSelectedThemeChanged(string value) { HasUnsavedChanges = true; }

    [ObservableProperty] private double _layoutFontScale = 1.0;
    partial void OnLayoutFontScaleChanged(double value) { HasUnsavedChanges = true; }

    [ObservableProperty] private ObservableCollection<string> _availableFonts = new() { "Default System", "Google Sans", "THSarabunNew" };
    [ObservableProperty] private string _selectedFont = "Default System";
    partial void OnSelectedFontChanged(string value) { HasUnsavedChanges = true; }

    [ObservableProperty] private ObservableCollection<string> _availableLanguages = new() { "English", "ภาษาไทย" };
    [ObservableProperty] private string _selectedLanguage = "English";
    partial void OnSelectedLanguageChanged(string value) { HasUnsavedChanges = true; }

    // --- Commands ---
    [RelayCommand]
    private async Task StartCamera()
    {
        IsCameraLoading = true; LoadingMessage = "Starting Camera...";
        await Task.Run(() => _visionService.StartCamera(CameraIndex, LensSelection, SelectedCameraApi, CameraResolution));

        // Sync state with actual hardware status
        IsCameraRunning = _visionService.IsRunning;
        IsCameraLoading = false;

        if (IsCameraRunning) StatusText = "Camera Live.";
        else StatusText = "Failed to open camera. Try another Index or API.";
    }

    [RelayCommand]
    private async Task StopCamera()
    {
        IsCameraLoading = true; LoadingMessage = "Stopping Camera...";
        await Task.Run(() => _visionService.StopCamera());
        IsCameraRunning = false; IsCameraLoading = false; StatusText = "Camera Stopped.";
    }

    [RelayCommand]
    private void Analyze()
    {
        if (!_isLiveAnalysisActive) { _visionService.SetLiveAnalysis(true); _isLiveAnalysisActive = true; AnalyzeButtonText = $"⏹ Stop Live AI ({LiveAiHotkeyDisplay})"; }
        else { _visionService.SetLiveAnalysis(false); _isLiveAnalysisActive = false; AnalyzeButtonText = $"▶ Start Live AI ({LiveAiHotkeyDisplay})"; }
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (ReportData.Count == 0) { StatusText = "No data to export."; return; }
        var picker = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
        if (picker == null) return;

        try
        {
            var file = await picker.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Excel Report",
                SuggestedFileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                DefaultExtension = ".xlsx",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Excel Workbook")
                    {
                        Patterns = new[] { "*.xlsx" },
                        MimeTypes = new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
                    }
                }
            });
            if (file != null)
            {
                StatusText = "Exporting Excel...";
                string path = file.TryGetLocalPath() ?? file.Path.LocalPath;
                await _excelExportService.ExportAsync(ReportData.ToList(), path);
                StatusText = "Excel exported successfully!";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Excel export failed: {ex.Message}";
            // Log to console for debugging
            Console.WriteLine($"[ExportError] {ex}");
        }
    }

    [RelayCommand]
    private async Task SelectDirectory()
    {
        var picker = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.StorageProvider;
        if (picker == null) return;
        var folders = await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Output Directory" });
        if (folders.Count > 0) OutputDirectory = folders[0].Path.LocalPath;
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        RefreshModelList();
        if (OperatingSystem.IsWindows()) _ = FetchHardwareSpecsAsync();
        LoadInitialSettings(); // Reset to saved state on open
        HasUnsavedChanges = false;
        HotkeyConflictMessage = "";
        ShowSettingsRequested?.Invoke();
    }

    [RelayCommand] private void AskCloseSettings() { CloseSettingsRequested?.Invoke(); }

    // --- Snapshot: Debounce guard ---
    private DateTime _lastSnapshotTime = DateTime.MinValue;
    private DispatcherTimer? _snapshotLabelTimer;

    [ObservableProperty] private bool _isSnapshotResultVisible = false;

    [RelayCommand]
    public void TakeSnapshotCommand()
    {
        if (!IsCameraRunning) return; // ไม่ทำงานถ้ากล้องยังไม่เปิด

        // Debounce: ป้องกัน double-trigger ภายใน 1 วินาที
        var now = DateTime.UtcNow;
        if ((now - _lastSnapshotTime).TotalSeconds < 1.0) return;
        _lastSnapshotTime = now;

        _visionService.RequestAnalysis();

        // แสดง label overlay เป็นเวลา SnapshotFreezeDurationMs แล้วซ่อนเอง
        IsSnapshotResultVisible = true;
        _snapshotLabelTimer?.Stop();
        _snapshotLabelTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(SnapshotFreezeDurationMs, 500))
        };
        _snapshotLabelTimer.Tick += (_, _) =>
        {
            IsSnapshotResultVisible = false;
            _snapshotLabelTimer?.Stop();
        };
        _snapshotLabelTimer.Start();
    }
    [RelayCommand] public void TakeSnapshotUICommand() => TakeSnapshotCommand();
    [RelayCommand] private void UnfreezeCamera() => _visionService.UnfreezeCamera();

    [RelayCommand] private void StartListeningForSnapshotHotkey() { IsListeningForSnapshotHotkey = true; SnapshotHotkeyDisplay = "Press any key..."; HotkeyConflictMessage = ""; }
    [RelayCommand] private void StartListeningForLiveAiHotkey() { IsListeningForLiveAiHotkey = true; LiveAiHotkeyDisplay = "Press any key..."; HotkeyConflictMessage = ""; }

    public Action? ShowSettingsRequested { get; set; }
    public Action? CloseSettingsRequested { get; set; }

    // --- Logic ---
    public MainWindowViewModel(IVisionService visionService, IExcelExportService excelExportService, IAnalysisService analysisService, IAppSettingsService settingsService, ILocalizationService localizationService)
    {
        _visionService = visionService; _excelExportService = excelExportService; _analysisService = analysisService; _settingsService = settingsService; _localizationService = localizationService;
        LoadInitialSettings();
        ApplySettingsToSystem();
        HasUnsavedChanges = false;

        _visionService.OnFrameProcessed += OnFrameProcessed;
        for (int i = 1; i <= 3; i++) SlideList.Add(new SlideItemViewModel { SlideName = $"Slide {i}" });
        _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _telemetryTimer.Tick += UpdateTelemetry; _telemetryTimer.Start();
        if (OperatingSystem.IsWindows()) _ = FetchHardwareSpecsAsync();
        RefreshModelList();
        UpdateLocalizedStrings();
    }

    private void LoadInitialSettings()
    {
        var s = _settingsService.LoadSettings();
        LensSelection = s.LensSelection; SelectedHardware = s.HardwareProvider; SelectedCameraApi = s.SelectedCameraApi;
        SelectedTheme = s.SelectedTheme; SelectedFont = s.SelectedFont; LayoutFontScale = s.LayoutFontScale > 0 ? s.LayoutFontScale : 1.0;
        SelectedLanguage = s.SelectedLanguage; ConfidenceThreshold = s.ConfidenceThreshold; TargetSampleSize = s.TargetSampleSize;
        OutputDirectory = s.OutputDirectory; CameraResolution = s.CameraResolution; CameraIndex = s.CameraIndex;
        SelectedModelPath4x = s.SelectedModelPath4x; SelectedModelPath10x = s.SelectedModelPath10x;
        FilterMinUm = s.FilterMinUm; FilterMaxUm = s.FilterMaxUm;
        if (Enum.TryParse<Avalonia.Input.Key>(s.SnapshotHotkey, out var k1)) SnapshotHotkey = k1;
        if (Enum.TryParse<Avalonia.Input.Key>(s.LiveAiHotkey, out var k2)) LiveAiHotkey = k2;
        SnapshotHotkeyDisplay = SnapshotHotkey.ToString();
        LiveAiHotkeyDisplay = LiveAiHotkey.ToString();
        AnalyzeButtonText = $"▶ Start Live AI ({LiveAiHotkeyDisplay})";
    }

    [RelayCommand]
    private void ApplySettings()
    {
        // --- จดจำค่า Camera settings ก่อนที่จะ save ---
        var prevCameraApi = _settingsService.LoadSettings().SelectedCameraApi;
        var prevCameraResolution = _settingsService.LoadSettings().CameraResolution;
        var prevCameraIndex = _settingsService.LoadSettings().CameraIndex;

        var s = new AppSettings
        {
            LensSelection = LensSelection,
            HardwareProvider = SelectedHardware,
            SelectedCameraApi = SelectedCameraApi,
            SelectedTheme = SelectedTheme,
            SelectedFont = SelectedFont,
            LayoutFontScale = LayoutFontScale,
            SelectedLanguage = SelectedLanguage,
            ConfidenceThreshold = ConfidenceThreshold,
            TargetSampleSize = TargetSampleSize ?? 200,
            OutputDirectory = OutputDirectory,
            SnapshotHotkey = SnapshotHotkey.ToString(),
            LiveAiHotkey = LiveAiHotkey.ToString(),
            CameraResolution = CameraResolution,
            CameraIndex = CameraIndex,
            SelectedModelPath4x = SelectedModelPath4x,
            SelectedModelPath10x = SelectedModelPath10x,
            FilterMinUm = FilterMinUm,
            FilterMaxUm = FilterMaxUm
        };
        _settingsService.SaveSettings(s);

        // --- ตรวจสอบว่า Camera settings เปลี่ยนหรือไม่ ---
        bool cameraChanged = prevCameraApi != SelectedCameraApi
                          || prevCameraResolution != CameraResolution
                          || prevCameraIndex != CameraIndex;

        ApplySettingsToSystem(cameraSettingsChanged: cameraChanged);
        HasUnsavedChanges = false;
        StatusText = cameraChanged ? "Settings applied. Restarting camera..." : "Settings applied.";
    }

    private void ApplySettingsToSystem(bool cameraSettingsChanged = false)
    {
        _visionService?.SetModelPaths(SelectedModelPath4x, SelectedModelPath10x);
        _visionService?.SetHardwareProvider(SelectedHardware);
        _visionService?.SetLens(LensSelection);
        _visionService?.SetThreshold(ConfidenceThreshold);
        _visionService?.SetSnapshotFreezeDuration(SnapshotFreezeDurationMs);

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = SelectedTheme switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default };
            if (SelectedFont == "Google Sans") Application.Current.Resources["AppFontFamily"] = Application.Current.Resources["GoogleSansFont"];
            else if (SelectedFont == "THSarabunNew") Application.Current.Resources["AppFontFamily"] = Application.Current.Resources["THSarabunNewFont"];
            else Application.Current.Resources["AppFontFamily"] = FontFamily.Default;
        }
        UpdateLocalizedStrings();
        AnalyzeButtonText = _isLiveAnalysisActive ? $"⏹ Stop Live AI ({LiveAiHotkeyDisplay})" : $"▶ Start Live AI ({LiveAiHotkeyDisplay})";

        // ⚠️ รีสตาร์ทกล้องเฉพาะเมื่อ Camera Settings (Index / API / Resolution) เปลี่ยนจริงๆ เท่านั้น
        if (cameraSettingsChanged && IsCameraRunning) _ = RestartCameraAsync();
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        var s = new AppSettings();
        LensSelection = s.LensSelection; SelectedHardware = s.HardwareProvider; SelectedCameraApi = s.SelectedCameraApi;
        SelectedTheme = s.SelectedTheme; SelectedFont = s.SelectedFont; LayoutFontScale = s.LayoutFontScale;
        SelectedLanguage = s.SelectedLanguage; ConfidenceThreshold = s.ConfidenceThreshold; TargetSampleSize = s.TargetSampleSize;
        CameraResolution = s.CameraResolution; SnapshotHotkey = Avalonia.Input.Key.Space; LiveAiHotkey = Avalonia.Input.Key.F5;
        SnapshotHotkeyDisplay = SnapshotHotkey.ToString(); LiveAiHotkeyDisplay = LiveAiHotkey.ToString();
        SelectedModelPath4x = "yolov8n_4x.onnx"; SelectedModelPath10x = "yolov8n_10x.onnx";
        FilterMinUm = 5.0; FilterMaxUm = 30.0;
        HasUnsavedChanges = true;
    }

    [ObservableProperty] private string _vmdLabel = "VMD";
    [ObservableProperty] private string _inFrameLabel = "In-Frame";
    [ObservableProperty] private string _accumulatedLabel = "Accumulated";
    [ObservableProperty] private string _spanLabel = "SPAN";
    [ObservableProperty] private string _outOfBoundsLabel = "Out-of-Bounds";
    [ObservableProperty] private string _currentSlideLabel = "Current Slide";
    [ObservableProperty] private string _slideStatusLabel = "Slide Status";
    [ObservableProperty] private string _targetSizeLabel = "Target Size";
    [ObservableProperty] private string _locTabAnalyze = "🔬 Analyze";
    [ObservableProperty] private string _locTabReport = "📊 Report";
    [ObservableProperty] private string _locFileStoragePath = "File Storage Path";
    [ObservableProperty] private string _locCurrentOutputDir = "Current Output Directory:";
    [ObservableProperty] private string _locBtnChangeOutputDir = "📁 Change Output Directory";
    [ObservableProperty] private string _locBtnSaveSnapshot = "📷 Save Manual Snapshot";
    [ObservableProperty] private string _locHardwareAcceleration = "Hardware Acceleration";
    [ObservableProperty] private string _locExecutionProvider = "Execution Provider:";
    [ObservableProperty] private string _locCameraBackendApi = "Camera Backend API:";
    [ObservableProperty] private string _locCameraResolution = "Camera Resolution:";
    [ObservableProperty] private string _locSystemHardwareInfo = "System Hardware Information:";
    [ObservableProperty] private string _locAiConfidenceThreshold = "AI Confidence Threshold:";
    [ObservableProperty] private string _locSnapshotHotkeyLabel = "Snapshot Hotkey:";
    [ObservableProperty] private string _locLiveAiHotkeyLabel = "Live AI Toggle Hotkey:";
    [ObservableProperty] private string _locSnapshotHoldDuration = "Snapshot Hold Duration:";
    [ObservableProperty] private string _locAppearanceSettings = "Appearance Settings";
    [ObservableProperty] private string _locColorTheme = "Color Theme:";
    [ObservableProperty] private string _locObjLens = "Objective Lens:";
    [ObservableProperty] private string _locFilterSizeUm = "🔍 Filter Size µm";
    [ObservableProperty] private string _locSlideWorkflowTracker = "Slide Workflow Tracker:";
    [ObservableProperty] private string _locItemsInReport = "Items in Report:";
    [ObservableProperty] private string _locLocNameData = "Location Name Data:";
    [ObservableProperty] private string _locBtnAddData = "➕ Add data to Excel Report";
    [ObservableProperty] private string _locTotalDropletsRecorded = "Total Droplets Recorded";
    [ObservableProperty] private string _locBtnExportExcel = "💾 Export to Excel";

    private void UpdateLocalizedStrings()
    {
        if (SelectedLanguage == "ภาษาไทย")
        {
            VmdLabel = "VMD (เฉลี่ย)"; InFrameLabel = "ในภาพ"; AccumulatedLabel = "สะสม"; SpanLabel = "SPAN";
            OutOfBoundsLabel = "เกินระยะ"; CurrentSlideLabel = "สไลด์"; SlideStatusLabel = "สถานะ"; TargetSizeLabel = "จำนวนเป้าหมาย";
            LocTabAnalyze = "🔬 วิเคราะห์"; LocTabReport = "📊 รายงาน"; LocFileStoragePath = "จัดเก็บไฟล์";
            LocCurrentOutputDir = "ไดเรกทอรีบันทึก:"; LocBtnChangeOutputDir = "📁 เปลี่ยนโฟลเดอร์";
            LocBtnSaveSnapshot = "📷 บันทึกภาพ"; LocHardwareAcceleration = "การเร่งความเร็วฮาร์ดแวร์";
            LocExecutionProvider = "ตัวประมวลผล:"; LocCameraBackendApi = "Camera API:"; LocCameraResolution = "ความละเอียด:";
            LocSystemHardwareInfo = "ข้อมูลฮาร์ดแวร์:"; LocAiConfidenceThreshold = "ความแม่นยำ AI:";
            LocSnapshotHotkeyLabel = "ปุ่มลัด Snapshot:"; LocLiveAiHotkeyLabel = "ปุ่มลัดเปิด/ปิด Live AI:"; LocSnapshotHoldDuration = "เวลาค้างภาพ:";
            LocAppearanceSettings = "รูปลักษณ์"; LocColorTheme = "ธีมสี:"; LocObjLens = "เลนส์:";
            LocFilterSizeUm = "🔍 กรองขนาด µm"; LocSlideWorkflowTracker = "ระบบติดตามสไลด์:";
            LocItemsInReport = "ข้อมูลในรายงาน:"; LocLocNameData = "ชื่อสถานที่/สไลด์:";
            LocBtnAddData = "➕ เพิ่มเข้าตาราง"; LocTotalDropletsRecorded = "ละอองทั้งหมด"; LocBtnExportExcel = "💾 ส่งออก Excel";
        }
    }

    [ObservableProperty][NotifyPropertyChangedFor(nameof(OutputDirectoryColor))] private string _outputDirectory = "";
    public string OutputDirectoryColor => string.IsNullOrWhiteSpace(OutputDirectory) ? "#F38BA8" : "#A6E3A1";
    partial void OnOutputDirectoryChanged(string value) { HasUnsavedChanges = true; }

    [RelayCommand]
    private void RetakeSlide(SlideItemViewModel slide)
    {
        if (slide == null) return;
        slide.CapturedDroplets = new System.Collections.Generic.List<double>();
        slide.DropletsCaptured = 0;
        slide.IsAnalyzed = false;
        slide.StatusText = "⏳ Pending";
        var existingReport = ReportData.FirstOrDefault(r => r.LocationName == slide.SlideName);
        if (existingReport != null)
        {
            ReportData.Remove(existingReport);
            ReportItemCount = ReportData.Count;
        }
        StatusText = $"{slide.SlideName} cleared.";
    }

    [ObservableProperty] private int _dropletCount = 0;
    [ObservableProperty] private int _accumulatedCount = 0;
    [ObservableProperty] private string _vmdText = "0.00";
    [ObservableProperty] private string _spanText = "0.00";
    [ObservableProperty] private string _evaluationStatus = "N/A";
    [ObservableProperty] private bool _isWarningVisible = false;

    [ObservableProperty] private double _filterMinUm = 5.0;
    partial void OnFilterMinUmChanged(double value) { HasUnsavedChanges = true; }
    [ObservableProperty] private double _filterMaxUm = 30.0;
    partial void OnFilterMaxUmChanged(double value) { HasUnsavedChanges = true; }

    public string FilterRangeLabel => $"{FilterMinUm:F1} - {FilterMaxUm:F1} µm";
    [ObservableProperty] private string _filterMinText = "5.0";
    [ObservableProperty] private string _filterMaxText = "30.0";
    [ObservableProperty] private string _whoPassText = "N/A";
    [ObservableProperty] private string _whoPassColor = "Gray";
    [ObservableProperty] private double _vmdProgressValue = 0;
    [ObservableProperty] private string _inRangePercent = "0%";
    [ObservableProperty] private string _outOfBoundsCount = "0";
    [ObservableProperty] private string _inRangeCountText = "0/0";
    [ObservableProperty] private int _cameraIndex = 0;

    [ObservableProperty] private string _cameraResolution = "1280x960";
    partial void OnCameraResolutionChanged(string value) { HasUnsavedChanges = true; }

    [ObservableProperty] private string _selectedCameraApi = "Auto";
    partial void OnSelectedCameraApiChanged(string value) { HasUnsavedChanges = true; }

    [ObservableProperty] private ObservableCollection<string> _availableCameraApis = new() { "Auto", "DirectShow", "Media Foundation" };
    [ObservableProperty] private ObservableCollection<string> _availableResolutions = new() { "1600x1200", "1280x960", "800x600" };
    [ObservableProperty] private ObservableCollection<SlideItemViewModel> _slideList = new();
    [ObservableProperty] private SlideItemViewModel? _selectedSlide;

    [ObservableProperty] private int? _targetSampleSize = 200;
    partial void OnTargetSampleSizeChanged(int? value) { HasUnsavedChanges = true; }

    [ObservableProperty] private Avalonia.Input.Key _snapshotHotkey = Avalonia.Input.Key.Space;
    [ObservableProperty] private string _snapshotHotkeyDisplay = "Space";
    [ObservableProperty] private bool _isListeningForSnapshotHotkey = false;

    [ObservableProperty] private Avalonia.Input.Key _liveAiHotkey = Avalonia.Input.Key.F5;
    [ObservableProperty] private string _liveAiHotkeyDisplay = "F5";
    [ObservableProperty] private bool _isListeningForLiveAiHotkey = false;

    public static FuncValueConverter<bool, IBrush> HotkeyButtonBrushConverter { get; } =
        new(isListening => isListening ? Brushes.Red : Brushes.Gray);

    [ObservableProperty] private int _snapshotFreezeDurationMs = 1500;
    partial void OnSnapshotFreezeDurationMsChanged(int value)
    {
        HasUnsavedChanges = true;
        var sec = value / 1000.0;
        if (Math.Abs((_snapshotFreezeDurationSeconds ?? 1.5) - sec) > 0.01) SnapshotFreezeDurationSeconds = sec;
    }

    [ObservableProperty] private double? _snapshotFreezeDurationSeconds = 1.5;
    partial void OnSnapshotFreezeDurationSecondsChanged(double? value)
    {
        if (!value.HasValue) return;
        int ms = (int)Math.Round(value.Value * 1000);
        if (SnapshotFreezeDurationMs != ms) SnapshotFreezeDurationMs = ms;
    }

    [ObservableProperty] private string _telemetryText = "";
    [ObservableProperty] private string _analyzeButtonText = "▶ Start Live AI";
    private bool _isLiveAnalysisActive = false;

    [ObservableProperty] private float _confidenceThreshold = 0.25f;
    partial void OnConfidenceThresholdChanged(float value) { HasUnsavedChanges = true; }

    [ObservableProperty] private int _reportItemCount = 0;
    public ObservableCollection<AnalysisReportItem> ReportData { get; } = new();

    // Snapshot overlay: bitmap ที่แสดงผล snapshot ค้างไว้บนภาพ live
    [ObservableProperty] private WriteableBitmap? _snapshotOverlayBitmap;
    private DispatcherTimer? _overlayTimer;

    private void OnFrameProcessed(object? sender, VisionEventArgs e)
    {
        Interlocked.Increment(ref _framesProcessedThisSecond);

        // Use Post instead of InvokeAsync to avoid blocking the vision thread
        Dispatcher.UIThread.Post(() =>
        {
            if (e.ProcessedBitmap != null)
            {
                if (e.IsSnapshot)
                {
                    // เก็บ bitmap ที่มี label box ค้างไว้เป็น overlay
                    SnapshotOverlayBitmap = e.ProcessedBitmap;

                    // ตั้ง timer ซ่อน overlay หลังจาก SnapshotFreezeDurationMs
                    _overlayTimer?.Stop();
                    _overlayTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(Math.Max(SnapshotFreezeDurationMs, 500))
                    };
                    _overlayTimer.Tick += (_, _) =>
                    {
                        SnapshotOverlayBitmap = null;
                        IsSnapshotResultVisible = false;
                        _overlayTimer?.Stop();
                    };
                    _overlayTimer.Start();

                    IsSnapshotResultVisible = true;
                    // ไม่อัพเดต CameraImage จาก snapshot frame
                }
                else
                {
                    // live frame - แสดงปกติ
                    CameraImage = e.ProcessedBitmap;
                }
            }

            if (e.Analysis != null)
            {
                DropletCount = e.Analysis.Count;
                AccumulatedCount = e.Analysis.TotalAccumulatedCount;
                VmdText = e.Analysis.Dv05_VMD.ToString("F2");
                SpanText = e.Analysis.Span.ToString("F2");
                bool pass = e.Analysis.Dv05_VMD >= 10 && e.Analysis.Dv05_VMD <= 30;
                WhoPassText = pass ? "✅ PASS" : "❌ FAIL";
                WhoPassColor = pass ? "#a6e3a1" : "#f38ba8";
            }
        }, DispatcherPriority.Render);
    }

    private int _framesProcessedThisSecond = 0;
    private void UpdateTelemetry(object? sender, EventArgs e)
    {
        using var proc = Process.GetCurrentProcess();
        long ramMb = proc.WorkingSet64 / (1024 * 1024);
        int fps = Interlocked.Exchange(ref _framesProcessedThisSecond, 0);
        TelemetryText = $"System Metrics: RAM {ramMb} MB | {fps} FPS | {SelectedHardware}";
    }

    public void UpdateHotkey(Avalonia.Input.Key key)
    {
        if (IsListeningForSnapshotHotkey)
        {
            if (key == LiveAiHotkey) { HotkeyConflictMessage = "⚠️ Key is already used for Live AI."; IsListeningForSnapshotHotkey = false; SnapshotHotkeyDisplay = SnapshotHotkey.ToString(); return; }
            SnapshotHotkey = key; SnapshotHotkeyDisplay = key.ToString(); IsListeningForSnapshotHotkey = false; HasUnsavedChanges = true; HotkeyConflictMessage = "";
        }
        else if (IsListeningForLiveAiHotkey)
        {
            if (key == SnapshotHotkey) { HotkeyConflictMessage = "⚠️ Key is already used for Snapshot."; IsListeningForLiveAiHotkey = false; LiveAiHotkeyDisplay = LiveAiHotkey.ToString(); return; }
            LiveAiHotkey = key; LiveAiHotkeyDisplay = key.ToString(); IsListeningForLiveAiHotkey = false; HasUnsavedChanges = true; HotkeyConflictMessage = "";
        }
    }

    public void ToggleIgnoreDroplet(double x, double y) => _visionService.ToggleIgnoreDroplet(x, y);

    [RelayCommand]
    private void AddToReport()
    {
        var raw = _visionService.GetSessionDroplets();
        if (raw.Count == 0 || SelectedSlide == null) return;
        if (SelectedSlide.CapturedDroplets == null) SelectedSlide.CapturedDroplets = new();
        SelectedSlide.CapturedDroplets.AddRange(raw);
        SelectedSlide.DropletsCaptured = SelectedSlide.CapturedDroplets.Count;
        SelectedSlide.IsAnalyzed = true;
        SelectedSlide.StatusText = $"✅ Accumulated ({SelectedSlide.DropletsCaptured})";

        var downsampled = _analysisService.DownsampleDroplets(SelectedSlide.CapturedDroplets, TargetSampleSize ?? 200);
        var result = _analysisService.CalculateStatistics(downsampled);
        result.TotalAccumulatedCount = SelectedSlide.DropletsCaptured;

        var existing = ReportData.FirstOrDefault(r => r.LocationName == SelectedSlide.SlideName);
        if (existing != null) ReportData[ReportData.IndexOf(existing)] = new AnalysisReportItem { LocationName = SelectedSlide.SlideName, Timestamp = DateTime.Now, Result = result };
        else ReportData.Add(new AnalysisReportItem { LocationName = SelectedSlide.SlideName, Timestamp = DateTime.Now, Result = result });

        ReportItemCount = ReportData.Count;
        _visionService.ClearSession();
        _visionService.UnfreezeCamera();
        AccumulatedCount = 0;
        if (!string.IsNullOrWhiteSpace(OutputDirectory)) SaveSnapshot();
    }

    [RelayCommand]
    private void CycleCamera()
    {
        try
        {
            CameraIndex++;
            if (CameraIndex > 5) CameraIndex = 0; // Cycle through 0, 1, 2, 3, 4, 5

            StatusText = $"Switching to Camera Index: {CameraIndex}...";
            if (IsCameraRunning) _ = RestartCameraAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error switching camera: {ex.Message}";
        }
    }
    private async Task RestartCameraAsync() { await StopCamera(); await StartCamera(); }

    [RelayCommand]
    private void SaveSnapshot()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory)) return;
        var (raw, processed) = _visionService.GetLatestFrames();
        if (raw == null && processed == null) return;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string locName = SelectedSlide?.SlideName ?? "Snapshot";
        if (raw != null) { Cv2.ImWrite(Path.Combine(OutputDirectory, $"{locName}_Raw_{timestamp}.jpg"), raw); raw.Dispose(); }
        if (processed != null) { Cv2.ImWrite(Path.Combine(OutputDirectory, $"{locName}_Processed_{timestamp}.jpg"), processed); processed.Dispose(); }
    }
}
