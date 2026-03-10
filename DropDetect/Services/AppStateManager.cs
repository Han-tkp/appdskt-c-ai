using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Input;

namespace DropDetect.Services;

public class AppSettings
{
    // Appearance
    public string SelectedTheme { get; set; } = "System";
    public string SelectedFont { get; set; } = "Default System";
    public double LayoutFontScale { get; set; } = 1.0;
    public string SelectedLanguage { get; set; } = "English";

    // Hardware & Camera
    public string HardwareProvider { get; set; } = "CPU";
    public string SelectedCameraApi { get; set; } = "Auto";
    public string CameraResolution { get; set; } = "1280x960";
    public int LastSelectedApiIndex { get; set; } = 0;
    public int LastSelectedResolutionIndex { get; set; } = 0;

    // Analysis
    public int LastSelectedLensIndex { get; set; } = 1;
    public double AnalysisThreshold { get; set; } = 25.0; // Slider value
    public int TargetSampleSize { get; set; } = 200;
    public string SelectedModelPath4x { get; set; } = "yolov8n_4x.onnx";
    public string SelectedModelPath10x { get; set; } = "yolov8n_10x.onnx";
    public double FilterMinUm { get; set; } = 5.0;
    public double FilterMaxUm { get; set; } = 30.0;
    public int SnapshotFreezeDurationMs { get; set; } = 1500;

    // Output
    public string OutputDirectory { get; set; } = "";

    // Hotkeys
    public int SnapshotHotkeyInt { get; set; } = (int)Key.Space;
    public int LiveAiHotkeyInt { get; set; } = (int)Key.F5;
}

public class AppStateManager
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DropDetect");

    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "appsettings.json");
    private static readonly string TempSettingsFilePath = Path.Combine(AppDataFolder, "appsettings.json.tmp");

    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public AppSettings CurrentSettings { get; private set; } = new();

    public AppStateManager()
    {
        EnsureFolderExists();
        LoadSettings();
    }

    private void EnsureFolderExists()
    {
        if (!Directory.Exists(AppDataFolder))
        {
            Directory.CreateDirectory(AppDataFolder);
        }
    }

    public void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppStateManager] Failed to load settings: {ex.Message}");
            // Fallback to default if corrupted
            CurrentSettings = new AppSettings();
        }
    }

    public async Task SaveSettingsAsync()
    {
        // 1. Atomic Write Pattern: Write to temp file first to prevent corruption during crash
        try
        {
            EnsureFolderExists();

            string json = JsonSerializer.Serialize(CurrentSettings, _jsonOptions);

            // Write to .tmp file
            using (var stream = new FileStream(TempSettingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(json);
                await writer.FlushAsync();
            }

            // 2. Safely replace the original file
            if (File.Exists(SettingsFilePath))
            {
                File.Replace(TempSettingsFilePath, SettingsFilePath, null);
            }
            else
            {
                File.Move(TempSettingsFilePath, SettingsFilePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppStateManager] Failed to save settings: {ex.Message}");
            // Optional: Alert UI
        }
    }
}
