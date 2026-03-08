using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace DropDetect.Services;

public class AppSettings
{
    public string SelectedTheme { get; set; } = "System";
    public string SelectedFont { get; set; } = "Default System";
    public string SelectedLanguage { get; set; } = "English";
    public string OutputDirectory { get; set; } = string.Empty;
    public string LensSelection { get; set; } = "10x";
    public string HardwareProvider { get; set; } = "CPU";
    public string SelectedCameraApi { get; set; } = "Auto";
    public float ConfidenceThreshold { get; set; } = 0.25f;
    public int TargetSampleSize { get; set; } = 200;
    public string SnapshotHotkey { get; set; } = "Space";
    public string CameraResolution { get; set; } = "1280x960";
    public int SnapshotFreezeDurationMs { get; set; } = 1500; // How long to freeze frame after snapshot (ms)
    public double LayoutFontScale { get; set; } = 1.0;
    public string SelectedModelPath4x { get; set; } = string.Empty;
    public string SelectedModelPath10x { get; set; } = string.Empty;
}

public interface IAppSettingsService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
}

public class AppSettingsService : IAppSettingsService
{
    private readonly string _settingsFilePath;
    private readonly string _directoryPath;

    public AppSettingsService()
    {
        _directoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropDetect");
        _settingsFilePath = Path.Combine(_directoryPath, "appsettings.json");
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading settings: {ex.Message}");
        }

        // Return default settings if file doesn't exist or parsing fails
        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(_directoryPath))
            {
                Directory.CreateDirectory(_directoryPath);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }
}
