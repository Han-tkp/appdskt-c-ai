using System;
using System.IO;
using System.Text.Json;

namespace DropDetect.Services;

public class AppSettings
{
    public string SelectedTheme { get; set; } = "System";
    public string SelectedFont { get; set; } = "Default System";
    public double LayoutFontScale { get; set; } = 1.0;
    public string SelectedLanguage { get; set; } = "English";
    public string LensSelection { get; set; } = "10x";
    public string HardwareProvider { get; set; } = "CPU";
    public string SelectedCameraApi { get; set; } = "Auto";
    public float ConfidenceThreshold { get; set; } = 0.25f;
    public int TargetSampleSize { get; set; } = 200;
    public string OutputDirectory { get; set; } = "";
    public string SnapshotHotkey { get; set; } = "Space";
    public string LiveAiHotkey { get; set; } = "F5";
    public string CameraResolution { get; set; } = "1280x960";
    public int CameraIndex { get; set; } = 0;
    public int SnapshotFreezeDurationMs { get; set; } = 1500;
    public string SelectedModelPath4x { get; set; } = "yolov8n_4x.onnx";
    public string SelectedModelPath10x { get; set; } = "yolov8n_10x.onnx";
    public double FilterMinUm { get; set; } = 5.0;
    public double FilterMaxUm { get; set; } = 30.0;
}

public interface IAppSettingsService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
}

public class AppSettingsService : IAppSettingsService
{
    private readonly string _settingsFilePath;

    public AppSettingsService()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appDir = Path.Combine(appDataPath, "DropDetect");
        if (!Directory.Exists(appDir)) Directory.CreateDirectory(appDir);
        _settingsFilePath = Path.Combine(appDir, "settings.json");
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, options));
        }
        catch { }
    }
}
