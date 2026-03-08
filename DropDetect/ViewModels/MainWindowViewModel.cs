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
using System.Management;

namespace DropDetect.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IVisionService _visionService;
    private readonly IExcelExportService _excelExportService;
    private readonly IAppSettingsService _settingsService;

    [ObservableProperty]
    private string _statusText = "Ready - Waiting to start camera";

    [ObservableProperty]
    private WriteableBitmap? _cameraImage;

    [ObservableProperty]
    private ObservableCollection<string> _availableLenses = new() { "4x", "10x" };

    [ObservableProperty]
    private string _lensSelection = "10x";

    partial void OnLensSelectionChanged(string value) => SaveCurrentSettings();

    [ObservableProperty]
    private bool _isSettingsOpen = false;

    [ObservableProperty]
    private ObservableCollection<string> _availableHardware = new() { "CPU", "GPU (DirectML)" };

    [ObservableProperty]
    private string _selectedHardware = "CPU";

    partial void OnSelectedHardwareChanged(string value)
    {
        if (_visionService != null)
        {
            _visionService.SetHardwareProvider(value);
            StatusText = $"Hardware switched to: {value}";
            SaveCurrentSettings();
        }
    }

    [ObservableProperty]
    private string _currentHardwareSpecs = "Loading hardware information...";

    private async Task FetchHardwareSpecsAsync()
    {
        try
        {
            var hardwareList = new System.Collections.Generic.List<string> { "CPU" };
            string cpuName = "Unknown CPU";

            await Task.Run(() =>
            {
                // Fetch CPU
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                    foreach (var obj in searcher.Get())
                    {
                        cpuName = obj["Name"]?.ToString() ?? "Unknown CPU";
                        break;
                    }
                }
                catch { }

                // Fetch GPU
                try
                {
                    int gpuIndex = 0;
                    using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                    foreach (var obj in searcher.Get())
                    {
                        var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                        hardwareList.Add($"DirectML: {name} (DeviceId: {gpuIndex})");
                        gpuIndex++;
                    }
                }
                catch { }
            });

            Dispatcher.UIThread.Post(() =>
            {
                if (hardwareList.Count > 1)
                {
                    // hardwareList[0] is "CPU", the rest are GPUs
                    string gpus = string.Join("\n", hardwareList.Skip(1));
                    CurrentHardwareSpecs = $"CPU: {cpuName}\nGPU: {gpus}";
                }
                else
                {
                    CurrentHardwareSpecs = $"CPU: {cpuName}\nGPU: None (DirectML not detected)";
                }

                AvailableHardware.Clear();
                foreach (var hw in hardwareList)
                {
                    AvailableHardware.Add(hw);
                }

                // Restore selected hardware if it exists in the list
                if (!AvailableHardware.Contains(SelectedHardware))
                {
                    SelectedHardware = "CPU";
                }
            });
        }
        catch (Exception)
        {
            CurrentHardwareSpecs = "Unable to fetch hardware details.";
        }
    }

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = new() { "Default Model" };

    [ObservableProperty]
    private string _selectedModelPath4x = "Default Model";

    [ObservableProperty]
    private string _selectedModelPath10x = "Default Model";

    partial void OnSelectedModelPath4xChanged(string value)
    {
        if (_visionService != null)
        {
            _visionService.SetModelPaths(SelectedModelPath4x, SelectedModelPath10x);
            if (LensSelection == "4x") StatusText = $"Model (4x) switched to: {value}";
        }
        SaveCurrentSettings();
    }

    partial void OnSelectedModelPath10xChanged(string value)
    {
        if (_visionService != null)
        {
            _visionService.SetModelPaths(SelectedModelPath4x, SelectedModelPath10x);
            if (LensSelection == "10x") StatusText = $"Model (10x) switched to: {value}";
        }
        SaveCurrentSettings();
    }

    private void RefreshModelList()
    {
        try
        {
            string onnxFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fileonnx");
            if (!System.IO.Directory.Exists(onnxFolder))
            {
                System.IO.Directory.CreateDirectory(onnxFolder);
            }

            var files = System.IO.Directory.GetFiles(onnxFolder, "*.onnx", System.IO.SearchOption.AllDirectories);

            Dispatcher.UIThread.Post(() =>
            {
                AvailableModels.Clear();
                if (files.Length == 0)
                {
                    AvailableModels.Add("No models found in fileonnx/");
                    if (SelectedModelPath4x == "Default Model" || string.IsNullOrEmpty(SelectedModelPath4x)) SelectedModelPath4x = "No models found in fileonnx/";
                    if (SelectedModelPath10x == "Default Model" || string.IsNullOrEmpty(SelectedModelPath10x)) SelectedModelPath10x = "No models found in fileonnx/";
                }
                else
                {
                    foreach (var file in files)
                    {
                        AvailableModels.Add(System.IO.Path.GetFileName(file));
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning models: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenModelsFolder()
    {
        try
        {
            string onnxFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fileonnx");
            if (!System.IO.Directory.Exists(onnxFolder))
            {
                System.IO.Directory.CreateDirectory(onnxFolder);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
            {
                FileName = onnxFolder,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error opening models folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RefreshModels()
    {
        RefreshModelList();
        StatusText = "Model list refreshed";
    }

    [ObservableProperty]
    private ObservableCollection<string> _availableCameraApis = new() { "Auto", "DirectShow", "Media Foundation" };

    [ObservableProperty]
    private string _selectedCameraApi = "Auto";

    partial void OnSelectedCameraApiChanged(string value)
    {
        SaveCurrentSettings();
        if (IsCameraRunning)
        {
            _ = RestartCameraAsync();
        }
    }
    [ObservableProperty]
    private ObservableCollection<string> _availableThemes = new() { "System", "Dark", "Light" };

    [ObservableProperty]
    private string _selectedTheme = "System";

    partial void OnSelectedThemeChanged(string value)
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = value switch
            {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };
        }
        SaveCurrentSettings();
    }

    [ObservableProperty]
    private float _confidenceThreshold = 0.25f;

    partial void OnConfidenceThresholdChanged(float value) => SaveCurrentSettings();

    // ---- Font & Language ----
    [ObservableProperty]
    private ObservableCollection<string> _availableFonts = new() { "Default System", "Google Sans", "THSarabunNew" };

    [ObservableProperty]
    private string _selectedFont = "Default System";

    [ObservableProperty]
    private double _layoutFontScale = 1.0;

    partial void OnLayoutFontScaleChanged(double value) => SaveCurrentSettings();

    partial void OnSelectedFontChanged(string value)
    {
        if (Application.Current != null)
        {
            if (value == "Google Sans")
            {
                Application.Current.Resources["AppFontFamily"] = Application.Current.Resources["GoogleSansFont"];
            }
            else if (value == "THSarabunNew")
            {
                Application.Current.Resources["AppFontFamily"] = Application.Current.Resources["THSarabunNewFont"];
            }
            else
            {
                Application.Current.Resources["AppFontFamily"] = FontFamily.Default;
            }
        }
        SaveCurrentSettings();
    }

    [ObservableProperty]
    private ObservableCollection<string> _availableLanguages = new() { "English", "ภาษาไทย" };

    [ObservableProperty]
    private string _selectedLanguage = "English";

    partial void OnSelectedLanguageChanged(string value)
    {
        UpdateLocalizedStrings();
        SaveCurrentSettings();
    }

    // Localized Strings for Dashboard NavBar
    [ObservableProperty] private string _vmdLabel = "VMD (Dv0.5)";
    [ObservableProperty] private string _inFrameLabel = "In-Frame";
    [ObservableProperty] private string _accumulatedLabel = "Accumulated";
    [ObservableProperty] private string _spanLabel = "SPAN";
    [ObservableProperty] private string _outOfBoundsLabel = "Out-of-Bounds";
    [ObservableProperty] private string _currentSlideLabel = "Current Slide";
    [ObservableProperty] private string _slideStatusLabel = "Slide Status";
    [ObservableProperty] private string _targetSizeLabel = "Target Size";

    // Additional System-wide Localized Strings
    [ObservableProperty] private string _locTabAnalyze = "🔬 Analyze";
    [ObservableProperty] private string _locTabReport = "📊 Report";
    [ObservableProperty] private string _locFileStoragePath = "File Storage Path";
    [ObservableProperty] private string _locCurrentOutputDir = "Current Output Directory:";
    [ObservableProperty] private string _locBtnChangeOutputDir = "📁 Change Output Directory";
    [ObservableProperty] private string _locBtnSaveSnapshot = "📷 Save Manual Snapshot";
    [ObservableProperty] private string _locHardwareAcceleration = "Hardware Acceleration & Engine";
    [ObservableProperty] private string _locExecutionProvider = "Execution Provider:";
    [ObservableProperty] private string _locCameraBackendApi = "Camera Backend API:";
    [ObservableProperty] private string _locCameraResolution = "Camera Resolution:";
    [ObservableProperty] private string _locSystemHardwareInfo = "System Hardware Information:";
    [ObservableProperty] private string _locAiConfidenceThreshold = "AI Confidence Threshold:";
    [ObservableProperty] private string _locSnapshotHotkey = "Snapshot Hotkey:";
    [ObservableProperty] private string _locSnapshotHoldDuration = "Snapshot Hold Duration:";
    [ObservableProperty] private string _locAppearanceSettings = "Appearance Settings";
    [ObservableProperty] private string _locColorTheme = "Color Theme:";
    [ObservableProperty] private string _locObjLens = "Objective Lens:";
    [ObservableProperty] private string _locAnalysisMode = "Analysis Mode";
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
            VmdLabel = "VMD (เฉลี่ยหยดน้ำ)";
            InFrameLabel = "หยดในภาพ";
            AccumulatedLabel = "หยดสะสมรวม";
            SpanLabel = "การกระจาย";
            OutOfBoundsLabel = "เกินระยะคำนวณ";
            CurrentSlideLabel = "สไลด์ปัจจุบัน";
            SlideStatusLabel = "สถานะสไลด์";
            TargetSizeLabel = "จำนวนที่ต้องการ";

            LocTabAnalyze = "🔬 วิเคราะห์";
            LocTabReport = "📊 รายงาน";
            LocFileStoragePath = "เส้นทางจัดเก็บไฟล์ข้อมูล";
            LocCurrentOutputDir = "ไดเรกทอรีปัจจุบันที่บันทึก:";
            LocBtnChangeOutputDir = "📁 เปลี่ยนโฟลเดอร์สำหรับบันทึกผล";
            LocBtnSaveSnapshot = "📷 บันทึกภาพหน้าจอแบบแมนนวล";
            LocHardwareAcceleration = "การเร่งฮาร์ดแวร์และเครื่องยนต์ AI";
            LocExecutionProvider = "ตัวประมวลผล (Execution Provider):";
            LocCameraBackendApi = "ระบบเชื่อมต่อกล้อง (API):";
            LocCameraResolution = "ความละเอียดกล้อง (Resolution):";
            LocSystemHardwareInfo = "ข้อมูลฮาร์ดแวร์ของระบบ:";
            LocAiConfidenceThreshold = "ความมั่นใจของ AI (Confidence Threshold):";
            LocSnapshotHotkey = "ปุ่มลัดสำหรับถ่าย Snapshot:";
            LocSnapshotHoldDuration = "ระยะเวลาค้างหน้าจอ Snapshot:";
            LocAppearanceSettings = "การตั้งค่ารูปลักษณ์โปรแกรม";
            LocColorTheme = "ธีมสีโปรแกรม (Theme):";
            LocObjLens = "กำลังขยายเลนส์ (Lens):";
            LocAnalysisMode = "โหมดวิเคราะห์ (Mode):";
            LocFilterSizeUm = "🔍 กรองขนาดไมครอน (Filter µm)";
            LocSlideWorkflowTracker = "ระบบติดตามสไลด์งาน:";
            LocItemsInReport = "จำนวนข้อมูลในรายงาน:";
            LocLocNameData = "ข้อมูลสถานที่หรือชื่อสไลด์:";
            LocBtnAddData = "➕ เพิ่มเข้าตารางรายงาน Excel";
            LocTotalDropletsRecorded = "ละอองทั้งหมดในสไลด์";
            LocBtnExportExcel = "💾 สร้างไฟล์ Excel ส่งออก";
        }
        else
        {
            VmdLabel = "VMD (Dv0.5)";
            InFrameLabel = "In-Frame";
            AccumulatedLabel = "Accumulated";
            SpanLabel = "SPAN";
            OutOfBoundsLabel = "Out-of-Bounds";
            CurrentSlideLabel = "Current Slide";
            SlideStatusLabel = "Slide Status";
            TargetSizeLabel = "Target Size";

            LocTabAnalyze = "🔬 Analyze";
            LocTabReport = "📊 Report";
            LocFileStoragePath = "File Storage Path";
            LocCurrentOutputDir = "Current Output Directory:";
            LocBtnChangeOutputDir = "📁 Change Output Directory";
            LocBtnSaveSnapshot = "📷 Save Manual Snapshot";
            LocHardwareAcceleration = "Hardware Acceleration & Engine";
            LocExecutionProvider = "Execution Provider:";
            LocCameraBackendApi = "Camera Backend API:";
            LocCameraResolution = "Camera Resolution:";
            LocSystemHardwareInfo = "System Hardware Information:";
            LocAiConfidenceThreshold = "AI Confidence Threshold:";
            LocSnapshotHotkey = "Snapshot Hotkey:";
            LocSnapshotHoldDuration = "Snapshot Hold Duration:";
            LocAppearanceSettings = "Appearance Settings";
            LocColorTheme = "Color Theme:";
            LocObjLens = "Objective Lens:";
            LocAnalysisMode = "Analysis Mode";
            LocFilterSizeUm = "🔍 Filter Size µm";
            LocSlideWorkflowTracker = "Slide Workflow Tracker:";
            LocItemsInReport = "Items in Report:";
            LocLocNameData = "Location Name Data:";
            LocBtnAddData = "➕ Add data to Excel Report";
            LocTotalDropletsRecorded = "Total Droplets Recorded";
            LocBtnExportExcel = "💾 Export to Excel";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputDirectoryColor))]
    private string _outputDirectory = string.Empty;

    partial void OnOutputDirectoryChanged(string value) => SaveCurrentSettings();

    public string OutputDirectoryColor => string.IsNullOrWhiteSpace(OutputDirectory) ? "#F38BA8" : "#A6E3A1";

    // Metrics
    [ObservableProperty]
    private int _dropletCount = 0;

    [ObservableProperty]
    private string _vmdText = "0.00";

    [ObservableProperty]
    private string _spanText = "0.00";

    [ObservableProperty]
    private string _evaluationStatus = "N/A";

    [ObservableProperty]
    private bool _isWarningVisible = false;

    // ---- µm Range Filter ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterRangeLabel))]
    private double _filterMinUm = 5.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterRangeLabel))]
    private double _filterMaxUm = 30.0;

    public string FilterRangeLabel => $"{FilterMinUm:F1} – {FilterMaxUm:F1} um";

    // String wrappers for plain TextBox input (no spinner arrows)
    private string _filterMinText = "5.0";
    public string FilterMinText
    {
        get => _filterMinText;
        set
        {
            if (_filterMinText == value) return;
            _filterMinText = value;
            OnPropertyChanged();
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double d) && d >= 0)
            {
                FilterMinUm = d;
            }
        }
    }

    private string _filterMaxText = "30.0";
    public string FilterMaxText
    {
        get => _filterMaxText;
        set
        {
            if (_filterMaxText == value) return;
            _filterMaxText = value;
            OnPropertyChanged();
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0)
            {
                FilterMaxUm = d;
            }
        }
    }

    partial void OnFilterMinUmChanged(double value)
    {
        if (value > FilterMaxUm) FilterMaxUm = value;
        RefreshDashboard();
    }
    partial void OnFilterMaxUmChanged(double value)
    {
        if (value < FilterMinUm) FilterMinUm = value;
        RefreshDashboard();
    }

    // ---- Computed Dashboard Properties ----
    [ObservableProperty]
    private string _whoPassText = "— N/A —";

    [ObservableProperty]
    private string _whoPassColor = "#44475a"; // neutral gray when N/A

    [ObservableProperty]
    private double _vmdProgressValue = 0;

    [ObservableProperty]
    private string _inRangePercent = "0.0%";

    [ObservableProperty]
    private string _outOfBoundsCount = "0";

    [ObservableProperty]
    private string _inRangeCountText = "0 / 0";


    // Camera State
    [ObservableProperty]
    private int _cameraIndex = 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor("StartCameraCommand")]
    [NotifyCanExecuteChangedFor("StopCameraCommand")]
    [NotifyCanExecuteChangedFor("AnalyzeCommand")]
    [NotifyPropertyChangedFor(nameof(CameraStatusColor))]
    [NotifyPropertyChangedFor(nameof(CameraStatusLabel))]
    private bool _isCameraRunning = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CameraStatusColor))]
    [NotifyPropertyChangedFor(nameof(CameraStatusLabel))]
    private bool _isCameraLoading = false;

    /// <summary>Color of the camera status indicator dot.</summary>
    public string CameraStatusColor =>
        IsCameraLoading ? "#FAB387" :   // orange = loading
        IsCameraRunning ? "#a6e3a1" :   // green  = live
        "#585b70";                       // gray   = offline

    /// <summary>Short camera state label next to the dot.</summary>
    public string CameraStatusLabel =>
        IsCameraLoading ? "Loading…" :
        IsCameraRunning ? "LIVE" :
        "Offline";

    [ObservableProperty]
    private string _loadingMessage = "Loading...";

    [ObservableProperty]
    private ObservableCollection<SlideItemViewModel> _slideList = new();

    [ObservableProperty]
    private SlideItemViewModel? _selectedSlide;

    [ObservableProperty]
    private int _targetSampleSize = 200;

    partial void OnTargetSampleSizeChanged(int value) => SaveCurrentSettings();

    [ObservableProperty]
    private string _snapshotHotkey = "Space";

    partial void OnSnapshotHotkeyChanged(string value) => SaveCurrentSettings();

    [ObservableProperty]
    private ObservableCollection<string> _availableHotkeys = new() { "Space", "Enter", "Return", "S", "H", "C", "A" };

    // Snapshot freeze duration (how long to hold the analysed frame on screen)
    [ObservableProperty]
    private int _snapshotFreezeDurationMs = 1500;

    partial void OnSnapshotFreezeDurationMsChanged(int value)
    {
        _visionService?.SetSnapshotFreezeDuration(value);
        SaveCurrentSettings();
    }

    [ObservableProperty]
    private ObservableCollection<string> _availableResolutions = new() { "1600x1200", "1280x960", "800x600" };

    [ObservableProperty]
    private string _cameraResolution = "1280x960";

    partial void OnCameraResolutionChanged(string value)
    {
        SaveCurrentSettings();
        if (IsCameraRunning)
        {
            _ = RestartCameraAsync();
        }
    }

    private async Task RestartCameraAsync()
    {
        try
        {
            // Detach entirely from the current UI context to prevent deadlocks
            await Task.Run(async () =>
            {
                // Force Stop
                _visionService.StopCamera();
                await Task.Delay(500); // Give hardware half a second to release the lens
            });

            // Start again
            await StartCamera();
        }
        catch (Exception ex)
        {
            StatusText = $"Restart failed: {ex.Message}";
        }
    }

    [ObservableProperty]
    private int _accumulatedCount = 0;

    [ObservableProperty]
    private int _reportItemCount = 0;

    public ObservableCollection<AnalysisReportItem> ReportData { get; } = new();

    private AnalysisResult? _lastAnalysisResult;

    private readonly IAnalysisService _analysisService;
    private readonly DispatcherTimer _telemetryTimer;
    private double _lastInferenceTimeMs = 0;

    [ObservableProperty]
    private string _telemetryText = "System Metrics: RAM 0 MB | 0.0 ms Inference | CPU";

    public MainWindowViewModel(IVisionService visionService, IExcelExportService excelExportService, IAnalysisService analysisService, IAppSettingsService settingsService)
    {
        _visionService = visionService;
        _excelExportService = excelExportService;
        _analysisService = analysisService;
        _settingsService = settingsService;

        LoadInitialSettings();

        _visionService.OnFrameProcessed += OnFrameProcessed;

        for (int i = 1; i <= 3; i++)
        {
            SlideList.Add(new SlideItemViewModel { SlideName = $"Slide {i}" });
        }
        if (SlideList.Count > 0) SelectedSlide = SlideList[0];

        _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _telemetryTimer.Tick += UpdateTelemetry;
        _telemetryTimer.Start();

        _ = FetchHardwareSpecsAsync();
        RefreshModelList();

        // Push initial model paths to vision service
        _visionService.SetModelPaths(SelectedModelPath4x, SelectedModelPath10x);
    }

    private void LoadInitialSettings()
    {
        var settings = _settingsService.LoadSettings();

        // Initial value assignment bypassing normal setters if possible, or letting them run to trigger effects
        LensSelection = settings.LensSelection;
        SelectedHardware = settings.HardwareProvider;
        SelectedCameraApi = settings.SelectedCameraApi;
        SelectedTheme = settings.SelectedTheme;
        SelectedFont = settings.SelectedFont;
        LayoutFontScale = settings.LayoutFontScale > 0 ? settings.LayoutFontScale : 1.0;
        SelectedLanguage = settings.SelectedLanguage;
        ConfidenceThreshold = settings.ConfidenceThreshold;
        TargetSampleSize = settings.TargetSampleSize;
        OutputDirectory = settings.OutputDirectory;
        SnapshotHotkey = settings.SnapshotHotkey;
        CameraResolution = settings.CameraResolution;
        SnapshotFreezeDurationMs = settings.SnapshotFreezeDurationMs;
        SelectedModelPath4x = string.IsNullOrEmpty(settings.SelectedModelPath4x) ? "Default Model" : settings.SelectedModelPath4x;
        SelectedModelPath10x = string.IsNullOrEmpty(settings.SelectedModelPath10x) ? "Default Model" : settings.SelectedModelPath10x;
    }

    private void SaveCurrentSettings()
    {
        if (_settingsService == null) return; // Prevent saving before Ctor finishes

        var settings = new AppSettings
        {
            LensSelection = this.LensSelection,
            HardwareProvider = this.SelectedHardware,
            SelectedCameraApi = this.SelectedCameraApi,
            SelectedTheme = this.SelectedTheme,
            SelectedFont = this.SelectedFont,
            LayoutFontScale = this.LayoutFontScale,
            SelectedLanguage = this.SelectedLanguage,
            ConfidenceThreshold = this.ConfidenceThreshold,
            TargetSampleSize = this.TargetSampleSize,
            OutputDirectory = this.OutputDirectory,
            SnapshotHotkey = this.SnapshotHotkey,
            CameraResolution = this.CameraResolution,
            SnapshotFreezeDurationMs = this.SnapshotFreezeDurationMs,
            SelectedModelPath4x = this.SelectedModelPath4x,
            SelectedModelPath10x = this.SelectedModelPath10x
        };
        _settingsService.SaveSettings(settings);
    }

    private int _framesProcessedThisSecond = 0;

    private void UpdateTelemetry(object? sender, EventArgs e)
    {
        using (var proc = System.Diagnostics.Process.GetCurrentProcess())
        {
            long ramMb = proc.WorkingSet64 / (1024 * 1024);
            string hardware = SelectedHardware;
            int fps = Interlocked.Exchange(ref _framesProcessedThisSecond, 0);

            TelemetryText = $"System Metrics: RAM {ramMb} MB | {fps} FPS | {_lastInferenceTimeMs:F1} ms Inference | {hardware}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartCamera))]
    private async Task StartCamera()
    {
        try
        {
            StatusText = $"Initializing Camera {CameraIndex} and connecting AI... Please wait.";
            LoadingMessage = "กำลังเปิดกล้อง และเตรียมความพร้อม AI...";
            IsCameraRunning = true; // Disable start button early
            IsCameraLoading = true; // Show loading overlay

            _visionService.SetLens(LensSelection);
            _visionService.SetThreshold(ConfidenceThreshold);

            await Task.Run(() =>
            {
                _visionService.StartCamera(CameraIndex, LensSelection, SelectedCameraApi, CameraResolution);
            });

            IsCameraLoading = false; // Hide loading overlay
            StatusText = $"Camera {CameraIndex} Live - Adjust focus, then press Analyze.";
        }
        catch (Exception ex)
        {
            IsCameraLoading = false;
            IsCameraRunning = false;
            StatusText = $"Camera Error: {ex.Message}";
        }
    }
    private bool CanStartCamera() => !IsCameraRunning;

    [RelayCommand(CanExecute = nameof(CanStopCamera))]
    private async Task StopCamera()
    {
        try
        {
            StatusText = "Stopping camera and releasing resources... Please wait.";
            LoadingMessage = "กำลังปิดกล้อง...";
            IsCameraLoading = true;

            await Task.Run(() =>
            {
                _visionService.StopCamera();
            });

            IsCameraRunning = false;
            IsCameraLoading = false;
            CameraImage = null; // Clear image specifically to indicate stopped state
            StatusText = "Camera Stopped";
        }
        catch (Exception ex)
        {
            IsCameraLoading = false;
            StatusText = $"Camera Stop Error: {ex.Message}";
        }
    }
    private bool CanStopCamera() => IsCameraRunning;

    [RelayCommand]
    private async Task CycleCamera()
    {
        CameraIndex++;
        if (CameraIndex > 5) CameraIndex = 0; // Loop between 0 and 5

        if (IsCameraRunning)
        {
            await StopCamera();
            await StartCamera();
        }
        else
        {
            StatusText = $"Ready - Selected Camera {CameraIndex}. Press Start.";
        }
    }

    [ObservableProperty]
    private string _analyzeButtonText = "▶ Start Live AI";

    private bool _isLiveAnalysisActive = false;

    // Explicit Dual Mode Selection
    [ObservableProperty]
    private bool _isLiveModeSelected = true;

    [ObservableProperty]
    private bool _isSnapshotModeSelected = false;

    partial void OnIsLiveModeSelectedChanged(bool value)
    {
        // If the user switches away from Live Mode while the AI is running, auto-stop it.
        if (!value && _isLiveAnalysisActive)
        {
            Analyze(); // Call the toggle to properly cleanly stop the live loop and reset UI
        }
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private void Analyze()
    {
        _isLiveAnalysisActive = !_isLiveAnalysisActive;

        if (_isLiveAnalysisActive)
        {
            AnalyzeButtonText = "⏸ Stop Live AI";
            StatusText = "Live Analysis Mode Active.";
            _visionService.SetLiveAnalysis(true);
        }
        else
        {
            AnalyzeButtonText = "▶ Start Live AI";
            StatusText = "Live Analysis Stopped.";
            _visionService.SetLiveAnalysis(false);
        }
    }
    private bool CanAnalyze() => IsCameraRunning;

    [RelayCommand]
    public void TakeSnapshotCommand() // Keyboard Hotkey Access
    {
        if (!IsCameraRunning)
        {
            StatusText = "Camera is not running. Cannot take snapshot.";
            return;
        }

        // Always request a single-frame AI analysis regardless of current mode
        StatusText = "📸 Snapshot taken — analysing frame...";
        _visionService.RequestAnalysis();
    }

    [RelayCommand]
    public void TakeSnapshotUICommand() // Mouse Button Access
    {
        TakeSnapshotCommand(); // Forward to the central hotkey logic
    }

    private void ShowSnapshotResult(string imagePath, AnalysisResult result)
    {
        // Logic to display the snapshot result, e.g., in a separate window or overlay
        // For now, just update status text
        StatusText = $"Snapshot taken. Detected {result.Count} droplets.";
    }

    private bool CanAddToReport() => SelectedSlide != null && !string.IsNullOrWhiteSpace(SelectedSlide.SlideName);

    [RelayCommand]
    private void RetakeSlide(SlideItemViewModel slide)
    {
        if (slide == null) return;

        slide.CapturedDroplets.Clear();
        slide.DropletsCaptured = 0;
        slide.IsAnalyzed = false;
        slide.StatusText = "⏳ Pending";

        var existingReport = ReportData.FirstOrDefault(r => r.LocationName == slide.SlideName);
        if (existingReport != null)
        {
            ReportData.Remove(existingReport);
            ReportItemCount = ReportData.Count;
        }

        StatusText = $"{slide.SlideName} cleared. Ready to retake.";
    }

    [RelayCommand]
    private void AddToReport()
    {
        var rawSessionDroplets = _visionService.GetSessionDroplets();

        if (rawSessionDroplets.Count == 0)
        {
            StatusText = "No valid analysis to add. Please scan the slide first.";
            return;
        }
        if (SelectedSlide == null || string.IsNullOrWhiteSpace(SelectedSlide.SlideName))
        {
            StatusText = "Please select a Slide before adding.";
            return;
        }

        var downsampled = _analysisService.DownsampleDroplets(rawSessionDroplets, TargetSampleSize);

        var finalResult = _analysisService.CalculateStatistics(downsampled);
        finalResult.TotalAccumulatedCount = rawSessionDroplets.Count;

        SelectedSlide.CapturedDroplets = new System.Collections.Generic.List<double>(downsampled);
        SelectedSlide.DropletsCaptured = downsampled.Count;
        SelectedSlide.IsAnalyzed = true;
        SelectedSlide.StatusText = $"✅ Analyzed ({downsampled.Count})";

        var existingReport = ReportData.FirstOrDefault(r => r.LocationName == SelectedSlide.SlideName);
        if (existingReport != null)
        {
            ReportData.Remove(existingReport);
        }

        var item = new AnalysisReportItem
        {
            LocationName = SelectedSlide.SlideName,
            Timestamp = DateTime.Now,
            Result = finalResult
        };

        ReportData.Add(item);
        ReportItemCount = ReportData.Count;

        _visionService.ClearSession();
        _visionService.UnfreezeCamera(); // Auto-Resume Workflow!
        AccumulatedCount = 0;

        StatusText = $"Added {SelectedSlide.SlideName} to Report ({finalResult.Count} drops sampled).";

        if (!string.IsNullOrWhiteSpace(OutputDirectory))
        {
            SaveSnapshot();
        }

        int currentIndex = SlideList.IndexOf(SelectedSlide);
        if (currentIndex >= 0 && currentIndex < SlideList.Count - 1)
        {
            SelectedSlide = SlideList[currentIndex + 1];
        }
    }

    public Action? ShowSettingsRequested { get; set; }
    public Action? CloseSettingsRequested { get; set; }

    [RelayCommand]
    private void ToggleSettings()
    {
        ShowSettingsRequested?.Invoke();
    }

    [RelayCommand]
    private void CloseSettings()
    {
        CloseSettingsRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SelectDirectory()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var storageProvider = desktop.MainWindow.StorageProvider;
            var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Output Directory for Snapshots",
                AllowMultiple = false
            });

            if (result != null && result.Count > 0)
            {
                // Use LocalPath if supported, but .Path.AbsolutePath or TryGetLocalPath is Avalonia safe.
                OutputDirectory = result[0].Path.LocalPath;
                StatusText = $"Output directory set to: {OutputDirectory}";
            }
        }
    }

    [RelayCommand]
    private void SaveSnapshot()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusText = "Please select an Output Directory first.";
            return;
        }

        var (raw, processed) = _visionService.GetLatestFrames();

        if (raw == null && processed == null)
        {
            StatusText = "No frames available to save. Please scan the slide first.";
            return;
        }

        try
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string locName = SelectedSlide == null || string.IsNullOrWhiteSpace(SelectedSlide.SlideName) ? "Snapshot" : SelectedSlide.SlideName;

            if (raw != null)
            {
                string rawPath = System.IO.Path.Combine(OutputDirectory, $"{locName}_Raw_{timestamp}.jpg");
                OpenCvSharp.Cv2.ImWrite(rawPath, raw);
                raw.Dispose();
            }

            if (processed != null)
            {
                string procPath = System.IO.Path.Combine(OutputDirectory, $"{locName}_Processed_{timestamp}.jpg");
                OpenCvSharp.Cv2.ImWrite(procPath, processed);
                processed.Dispose();
            }

            StatusText = $"Snapshots saved successfully to Output Directory.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving snapshots: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (ReportData.Count == 0)
        {
            StatusText = "Report is empty. Please add items first.";
            return;
        }

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var storageProvider = desktop.MainWindow.StorageProvider;
                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export WHO DropDetect Report",
                    DefaultExtension = "xlsx",
                    SuggestedFileName = $"DropletReport_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                    FileTypeChoices = new[] {
                        new FilePickerFileType("Excel Workbook") { Patterns = new[] { "*.xlsx" } }
                    }
                });

                if (file != null)
                {
                    StatusText = "Saving Excel Export...";
                    // Use file.TryGetLocalPath() if LocalPath isn't directly exposed safely. Or AbsolutePath.
                    string _path = file.Path.LocalPath;
                    await _excelExportService.ExportAsync(ReportData, _path);
                    StatusText = $"Export saved successfully to {file.Name}.";
                }
                else
                {
                    StatusText = "Export cancelled.";
                }
            }
            else
            {
                StatusText = "Export error: Storage Provider unavailable.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Export error: {ex.Message}";
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(LensSelection))
        {
            _visionService.SetLens(LensSelection);
        }
        if (e.PropertyName == nameof(ConfidenceThreshold))
        {
            _visionService.SetThreshold(ConfidenceThreshold);
        }
    }

    private void OnFrameProcessed(object? sender, VisionEventArgs e)
    {
        Interlocked.Increment(ref _framesProcessedThisSecond);

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CameraImage = e.ProcessedBitmap;

            if (e.Analysis != null)
            {
                _lastInferenceTimeMs = e.Analysis.InferenceTimeMs;
                _lastAnalysisResult = e.Analysis;

                DropletCount = e.Analysis.Count;
                AccumulatedCount = e.Analysis.TotalAccumulatedCount;
                IsWarningVisible = AccumulatedCount < TargetSampleSize;

                // Apply µm filter to build filtered stats
                var allDroplets = _visionService.GetSessionDroplets();
                var filtered = allDroplets
                    .Where(d => d >= FilterMinUm && d <= FilterMaxUm)
                    .ToList();

                int totalCount = allDroplets.Count;
                int inRange = filtered.Count;
                int oob = totalCount - inRange;

                InRangeCountText = $"{inRange} / {totalCount}";
                InRangePercent = totalCount > 0 ? $"{(inRange * 100.0 / totalCount):F1}%" : "0.0%";
                OutOfBoundsCount = oob.ToString();

                // Use filtered set for VMD/Span if available, else fall back to full
                AnalysisResult? displayResult = null;
                if (filtered.Count >= 3)
                    displayResult = _analysisService.CalculateStatistics(filtered);
                else
                    displayResult = e.Analysis;

                if (displayResult != null)
                {
                    double vmd = displayResult.Dv05_VMD;
                    VmdText = $"{vmd:F2}";
                    SpanText = displayResult.Span.ToString("F2");
                    EvaluationStatus = displayResult.IsPassed ? "PASS" : "FAIL";

                    // WHO badge
                    bool pass = vmd >= 10.0 && vmd <= 30.0;
                    WhoPassText = pass ? "✅ PASS" : "❌ FAIL";
                    WhoPassColor = pass ? "#a6e3a1" : "#f38ba8";

                    // VMD bar: 0–50 µm scale
                    VmdProgressValue = Math.Clamp(vmd / 50.0 * 100.0, 0, 100);
                }

                if (DropletCount == 0 && AccumulatedCount == 0)
                    StatusText = "Scanning... 0 in view.";
                else
                    StatusText = $"Scanning... {DropletCount} in view. Accumulated: {AccumulatedCount}";
            }
        });
    }

    private void RefreshDashboard()
    {
        // Trigger a dashboard recalculation using the last known analysis
        if (_lastAnalysisResult == null) return;

        var allDroplets = _visionService.GetSessionDroplets();
        var filtered = allDroplets.Where(d => d >= FilterMinUm && d <= FilterMaxUm).ToList();

        int totalCount = allDroplets.Count;
        int inRange = filtered.Count;
        int oob = totalCount - inRange;

        InRangeCountText = $"{inRange} / {totalCount}";
        InRangePercent = totalCount > 0 ? $"{(inRange * 100.0 / totalCount):F1}%" : "0.0%";
        OutOfBoundsCount = oob.ToString();

        AnalysisResult? displayResult = filtered.Count >= 3
            ? _analysisService.CalculateStatistics(filtered)
            : _lastAnalysisResult;

        if (displayResult != null)
        {
            double vmd = displayResult.Dv05_VMD;
            VmdText = $"{vmd:F2}";
            SpanText = displayResult.Span.ToString("F2");
            EvaluationStatus = displayResult.IsPassed ? "PASS" : "FAIL";

            bool pass = vmd >= 10.0 && vmd <= 30.0;
            WhoPassText = pass ? "✅ PASS" : "❌ FAIL";
            WhoPassColor = pass ? "#a6e3a1" : "#f38ba8";
            VmdProgressValue = Math.Clamp(vmd / 50.0 * 100.0, 0, 100);
        }
    }

    public void ToggleIgnoreDroplet(double x, double y)
    {
        _visionService.ToggleIgnoreDroplet(x, y);
    }
}
