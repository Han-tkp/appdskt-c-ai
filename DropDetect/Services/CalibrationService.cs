using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DropDetect.Services;

public interface ICalibrationService
{
    double GetPixelToMicronRatio(string lensType);
    void EnsureCalibrationFilesExist();
}

public class CalibrationData
{
    [JsonPropertyName("pixel_to_micron_ratio")]
    public double PixelToMicronRatio { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class CalibrationService : ICalibrationService
{
    private readonly string _calibrationDir;

    public CalibrationService()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _calibrationDir = Path.Combine(appDataPath, "DropDetect", "calibration");
        EnsureCalibrationFilesExist();
    }

    public void EnsureCalibrationFilesExist()
    {
        if (!Directory.Exists(_calibrationDir))
        {
            Directory.CreateDirectory(_calibrationDir);
        }

        var path4x = Path.Combine(_calibrationDir, "4x.json");
        if (!File.Exists(path4x))
        {
            // Value derived from user's 4x.json reference (6e-07 m = 0.692 µm)
            var data4x = new CalibrationData { PixelToMicronRatio = 0.692137, Description = "Custom Calibrated: 4X Lens" };
            File.WriteAllText(path4x, JsonSerializer.Serialize(data4x, new JsonSerializerOptions { WriteIndented = true }));
        }

        var path10x = Path.Combine(_calibrationDir, "10x.json");
        if (!File.Exists(path10x))
        {
            // Value derived from user's 10x.json reference (2.7e-07 m = 0.279 µm)
            var data10x = new CalibrationData { PixelToMicronRatio = 0.279263, Description = "Custom Calibrated: 10X Lens" };
            File.WriteAllText(path10x, JsonSerializer.Serialize(data10x, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public double GetPixelToMicronRatio(string lensType)
    {
        string filename = $"{lensType}.json";
        string path = Path.Combine(_calibrationDir, filename);

        try
        {
            if (File.Exists(path))
            {
                string jsonString = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<CalibrationData>(jsonString);
                if (data != null)
                {
                    return data.PixelToMicronRatio;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading calibration file {filename}: {ex.Message}");
        }

        return 1.0; // Fallback
    }
}
