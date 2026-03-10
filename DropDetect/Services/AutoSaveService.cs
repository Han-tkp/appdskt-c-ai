using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DropDetect.ViewModels; // needed if referencing classes

namespace DropDetect.Services;

public interface IAutoSaveService
{
    // โฟลเดอร์ที่เก็บไฟล์กู้คืน
    string AutoSaveDirectory { get; }
    
    // ตั้งค่าโปรเจกต์ปัจจุบัน (ให้ Service รู้ว่าควรดึงอัปเดตจากโมเดลไหน)
    void SetCurrentProjectContextProvider(Func<DropletProject> projectProvider);

    // แจ้ง Service ว่ามี Event สำคัญเกิดขึ้น เพื่อหน่วงเวลาแล้วเขียนไฟล์
    void TriggerAutoSave();

    // ลบไฟล์ autosave ทิ้ง (ใช้เวลา Save As หรือ Save สำเร็จ หรือ Clean Exit)
    void ClearAutoSave();

    // ดึงหาไฟล์ AutoSave ล่าสุดตอนเปิดโปรแกรม
    string? GetLastAutoSaveFilePath();

    // กำหนด Event/Callback เพื่อส่ง Error กลับไป UI สื่อสารให้ User ทราบ (Silent Failure Handling)
    event EventHandler<string>? OnAutoSaveFailed;
}

public class AutoSaveService : IAutoSaveService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false }; // ไม่ต้อง Indent เพื่อความไว
    private Func<DropletProject>? _projectProvider;
    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();

    public string AutoSaveDirectory { get; }

    public event EventHandler<string>? OnAutoSaveFailed;

    public AutoSaveService()
    {
        AutoSaveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DropDetect",
            "AutoSave"
        );

        if (!Directory.Exists(AutoSaveDirectory))
        {
            try
            {
                Directory.CreateDirectory(AutoSaveDirectory);
            }
            catch { /* Ignore - handled during save if it fails */ }
        }
    }

    public void SetCurrentProjectContextProvider(Func<DropletProject> projectProvider)
    {
        _projectProvider = projectProvider;
    }

    public void TriggerAutoSave()
    {
        if (_projectProvider == null) return;

        // ยกเลิก CancellationToken เก่า ถ้ามี (การ Debounce ไม่ให้ Save ถี่เกินไป)
        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
        }

        var ct = _debounceCts.Token;

        Task.Run(async () =>
        {
            try
            {
                // หน่วงเวลาเล็กน้อย (Debounce 2 วินาที) เผื่อมีการ Trigger รัวๆ
                await Task.Delay(2000, ct);

                if (ct.IsCancellationRequested) return;

                // ถึงเวลาเซฟ
                await PerformAutoSaveAsync();
            }
            catch (TaskCanceledException)
            {
                // โดน Cancel เพราะมี Trigger ใหม่เข้ามา
            }
            catch (Exception ex)
            {
                OnAutoSaveFailed?.Invoke(this, $"Auto-save failed: {ex.Message}");
            }
        });
    }

    private async Task PerformAutoSaveAsync()
    {
        var proj = _projectProvider?.Invoke();
        if (proj == null) return;

        if (!Directory.Exists(AutoSaveDirectory))
            Directory.CreateDirectory(AutoSaveDirectory);

        string tempPath = Path.Combine(AutoSaveDirectory, "recovery.json.tmp");
        string finalPath = Path.Combine(AutoSaveDirectory, "recovery.json");

        // เขียนลง temp file ก่อน (Atomic Write)
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(fs, proj, _jsonOptions);
        }

        // สลับชื่อไฟล์
        File.Move(tempPath, finalPath, overwrite: true);
    }

    public void ClearAutoSave()
    {
        try
        {
            string finalPath = Path.Combine(AutoSaveDirectory, "recovery.json");
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
            string tempPath = Path.Combine(AutoSaveDirectory, "recovery.json.tmp");
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch { /* ignored */ }
    }

    public string? GetLastAutoSaveFilePath()
    {
        string finalPath = Path.Combine(AutoSaveDirectory, "recovery.json");
        return File.Exists(finalPath) ? finalPath : null;
    }
}
