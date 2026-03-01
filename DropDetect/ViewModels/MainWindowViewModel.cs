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

namespace DropDetect.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IVisionService _visionService;
    private readonly IExcelExportService _excelExportService;

    [ObservableProperty]
    private string _statusText = "Ready - Waiting to start camera";

    [ObservableProperty]
    private WriteableBitmap? _cameraImage;

    [ObservableProperty]
    private ObservableCollection<string> _availableLenses = new() { "4x", "10x" };

    [ObservableProperty]
    private string _lensSelection = "10x";

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
        }
    }

    [ObservableProperty]
    private ObservableCollection<string> _availableCameraApis = new() { "Auto", "DirectShow", "Media Foundation" };

    [ObservableProperty]
    private string _selectedCameraApi = "Auto";

    [ObservableProperty]
    private float _confidenceThreshold = 0.25f;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputDirectoryColor))]
    private string _outputDirectory = string.Empty;

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

    // Camera State
    [ObservableProperty]
    private int _cameraIndex = 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor("StartCameraCommand")]
    [NotifyCanExecuteChangedFor("StopCameraCommand")]
    [NotifyCanExecuteChangedFor("AnalyzeCommand")]
    private bool _isCameraRunning = false;

    [ObservableProperty]
    private bool _isCameraLoading = false;

    [ObservableProperty]
    private string _loadingMessage = "Loading...";

    [ObservableProperty]
    private ObservableCollection<SlideItemViewModel> _slideList = new();

    [ObservableProperty]
    private SlideItemViewModel? _selectedSlide;

    [ObservableProperty]
    private int _targetSampleSize = 200;

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

    public MainWindowViewModel(IVisionService visionService, IExcelExportService excelExportService, IAnalysisService analysisService)
    {
        _visionService = visionService;
        _excelExportService = excelExportService;
        _analysisService = analysisService;
        _visionService.OnFrameProcessed += OnFrameProcessed;

        for (int i = 1; i <= 3; i++)
        {
            SlideList.Add(new SlideItemViewModel { SlideName = $"Slide {i}" });
        }
        if (SlideList.Count > 0) SelectedSlide = SlideList[0];

        _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _telemetryTimer.Tick += UpdateTelemetry;
        _telemetryTimer.Start();
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
                _visionService.StartCamera(CameraIndex, LensSelection, SelectedCameraApi);
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
    private string _analyzeButtonText = "🧠 Analyze (Live)";

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private void Analyze()
    {
        if (_analyzeButtonText.Contains("Live"))
        {
            StatusText = "Tracking Live Analysis with WHO Metrology...";
            AnalyzeButtonText = "⏹ Stop Analysis";
            _lastAnalysisResult = null;
        }
        else
        {
            StatusText = "Analysis Paused. Review and Add to Report.";
            AnalyzeButtonText = "🧠 Analyze (Live)";
        }

        _visionService.RequestAnalysis();
    }
    private bool CanAnalyze() => IsCameraRunning;

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

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
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

        // Safe dispatch to Avalonia UI Thread
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CameraImage = e.ProcessedBitmap;

            if (e.Analysis != null)
            {
                _lastInferenceTimeMs = e.Analysis.InferenceTimeMs;
                _lastAnalysisResult = e.Analysis;

                DropletCount = e.Analysis.Count;
                AccumulatedCount = e.Analysis.TotalAccumulatedCount; // <--- This was missing!
                VmdText = $"{e.Analysis.Dv05_VMD:F2}";
                SpanText = e.Analysis.Span.ToString("F2");

                IsWarningVisible = AccumulatedCount < TargetSampleSize;

                EvaluationStatus = e.Analysis.IsPassed ? "PASS" : "FAIL";

                if (DropletCount == 0 && AccumulatedCount == 0)
                {
                    StatusText = $"Scanning... 0 in view. (Accumulated: {AccumulatedCount})";
                }
                else
                {
                    StatusText = $"Scanning... {DropletCount} in view. Total unique accumulated: {AccumulatedCount}";
                }
            }
        });
    }

    public void ToggleIgnoreDroplet(double x, double y)
    {
        _visionService.ToggleIgnoreDroplet(x, y);
    }
}
