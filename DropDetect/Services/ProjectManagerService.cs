using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DropDetect.Services;

// --- Schema Models ---

public class ProjectSettings
{
    public string LensSelection { get; set; } = "10x";
    public double AnalysisThreshold { get; set; } = 25.0;
    public int TargetSampleSize { get; set; } = 200;
}

public class SlideSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string LocationName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // Relative paths to images inside the ZIP (e.g., "images/raw_1.jpg")
    public string? RawImagePath { get; set; }
    public string? ProcessedImagePath { get; set; }

    public AnalysisResult Result { get; set; } = new();
    public List<DropletData> CapturedDroplets { get; set; } = new();
    public List<double> RawDiameters { get; set; } = new();
}

public class DropletProject : ObservableObject
{
    public string SchemaVersion { get; set; } = "1.0";

    // Dirty Flag - Not serialized to JSON
    private bool _isDirty;
    [JsonIgnore]
    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastSavedAt { get; set; } = DateTime.Now;

    public ProjectSettings Settings { get; set; } = new();
    public List<SlideSession> Sessions { get; set; } = new();
}

// --- Service Interface & Implementation ---

public interface IProjectManagerService
{
    Task ExportProjectAsync(DropletProject project, string destinationZipPath, string sourceImagesFolder);
    Task<DropletProject> ImportProjectAsync(string sourceZipPath, string extractionFolder);
}

public class ProjectManagerService : IProjectManagerService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task ExportProjectAsync(DropletProject project, string destinationZipPath, string sourceImagesFolder)
    {
        project.LastSavedAt = DateTime.Now;

        // Serialize Project Data
        string projectJson = JsonSerializer.Serialize(project, _jsonOptions);

        // Run Zipping in Background to avoiud UI freeze
        await Task.Run(() =>
        {
            // Use a temporary zip file to prevent corruption if process fails halfway
            string tempZipPath = destinationZipPath + ".tmp";
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

            using (var archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
            {
                // 1. Write project_data.json
                var entry = archive.CreateEntry("project_data.json", CompressionLevel.Fastest);
                using (var entryStream = entry.Open())
                using (var writer = new StreamWriter(entryStream))
                {
                    writer.Write(projectJson);
                }

                // 2. Add Images from source folder to images/ directory in ZIP
                if (Directory.Exists(sourceImagesFolder))
                {
                    string[] imageFiles = Directory.GetFiles(sourceImagesFolder, "*.jpg");
                    foreach (string imgPath in imageFiles)
                    {
                        string fileName = Path.GetFileName(imgPath);
                        string entryName = $"images/{fileName}";

                        // Robust FileShare.ReadWrite to avoid locking issues with camera/AI
                        var imgEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                        using (var imgEntryStream = imgEntry.Open())
                        using (var fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            fs.CopyTo(imgEntryStream);
                        }
                    }
                }
            }

            // Safely swap files (Atomic Write for Zip)
            if (File.Exists(destinationZipPath))
                File.Replace(tempZipPath, destinationZipPath, null);
            else
                File.Move(tempZipPath, destinationZipPath);

            project.IsDirty = false;
        });
    }

    public async Task<DropletProject> ImportProjectAsync(string sourceZipPath, string extractionFolder)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(sourceZipPath))
                throw new FileNotFoundException($"Project file not found: {sourceZipPath}");

            string tempExtract = extractionFolder + "_temp";
            if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
            Directory.CreateDirectory(tempExtract);

            // Extract ZIP
            ZipFile.ExtractToDirectory(sourceZipPath, tempExtract);

            // 1. Read project_data.json
            string jsonPath = Path.Combine(tempExtract, "project_data.json");
            if (!File.Exists(jsonPath))
                throw new InvalidDataException("Invalid project file: missing project_data.json");

            string json = File.ReadAllText(jsonPath);
            DropletProject project = JsonSerializer.Deserialize<DropletProject>(json, _jsonOptions)
                                     ?? throw new InvalidDataException("Failed to parse project data.");

            // 2. Compatibility check
            if (project.SchemaVersion != "1.0")
            {
                // Future migration logic goes here
            }

            // Ensure clean extraction folder for images
            if (Directory.Exists(extractionFolder))
                Directory.Delete(extractionFolder, true);
            Directory.CreateDirectory(extractionFolder);

            // Move extracted images to extractionFolder
            string extractedImagesDir = Path.Combine(tempExtract, "images");
            if (Directory.Exists(extractedImagesDir))
            {
                foreach (string file in Directory.GetFiles(extractedImagesDir))
                {
                    File.Move(file, Path.Combine(extractionFolder, Path.GetFileName(file)));
                }
            }

            // Cleanup temp extract
            Directory.Delete(tempExtract, true);

            project.IsDirty = false;
            return project;
        });
    }
}
