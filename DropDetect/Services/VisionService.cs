using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenCvSharp;

namespace DropDetect.Services;

public class VisionEventArgs : EventArgs
{
    public WriteableBitmap? ProcessedBitmap { get; set; }
    public AnalysisResult? Analysis { get; set; }
    /// <summary>true = frame นี้คือผลจากการกด Snapshot (ไม่ใช่ live frame ทั่วไป)</summary>
    public bool IsSnapshot { get; set; }
}

public interface IVisionService : IDisposable
{
    event EventHandler<VisionEventArgs>? OnFrameProcessed;
    bool IsRunning { get; }
    void StartCamera(int cameraIndex, string currentLensStr, string cameraApi, string resolution);
    void StopCamera();
    void SetThreshold(float threshold);
    void SetLens(string lensStr);
    void SetHardwareProvider(string provider);
    void SetLiveAnalysis(bool enabled);
    void RequestAnalysis();
    void SetModelPaths(string model4x, string model10x);
    void UnfreezeCamera();
    void SetSnapshotFreezeDuration(int milliseconds);
    void ToggleIgnoreDroplet(double x, double y);
    System.Collections.Generic.List<double> GetSessionDroplets();
    void ClearSession();
    (Mat? Raw, Mat? Processed) GetLatestFrames();
}

public class TrackedObject
{
    public int Id { get; set; }
    public Point2f Centroid { get; set; }
    public double DiameterUm { get; set; }
    public OpenCvSharp.Point[] Contour { get; set; } = Array.Empty<OpenCvSharp.Point>();
    public int MissingFrames { get; set; }
}

public class ObjectTracker
{
    private int _nextId = 1;
    private System.Collections.Generic.List<TrackedObject> _trackedObjects = new();

    public System.Collections.Generic.List<TrackedObject> Update(
        System.Collections.Generic.List<Point2f> newCentroids,
        System.Collections.Generic.List<double> newDiameters,
        System.Collections.Generic.List<OpenCvSharp.Point[]> newContours)
    {
        var currentObjects = new System.Collections.Generic.List<TrackedObject>();
        bool[] matchedNew = new bool[newCentroids.Count];
        bool[] matchedOld = new bool[_trackedObjects.Count];

        for (int i = 0; i < newCentroids.Count; i++)
        {
            var newC = newCentroids[i];
            var newD = newDiameters[i];

            int bestMatchIdx = -1;
            double minCost = double.MaxValue;

            for (int j = 0; j < _trackedObjects.Count; j++)
            {
                if (matchedOld[j]) continue;

                var oldObj = _trackedObjects[j];
                double dist = Math.Sqrt(Math.Pow(newC.X - oldObj.Centroid.X, 2) + Math.Pow(newC.Y - oldObj.Centroid.Y, 2));
                double sizeRatio = Math.Max(newD, oldObj.DiameterUm) / Math.Max(1.0, Math.Min(newD, oldObj.DiameterUm));

                if (dist < 80 && sizeRatio < 1.5) // Max 80px movement, 1.5x size drift
                {
                    double cost = dist + (Math.Abs(newD - oldObj.DiameterUm) * 2);
                    if (cost < minCost)
                    {
                        minCost = cost;
                        bestMatchIdx = j;
                    }
                }
            }

            if (bestMatchIdx != -1)
            {
                matchedNew[i] = true;
                matchedOld[bestMatchIdx] = true;

                var matchedObj = _trackedObjects[bestMatchIdx];
                matchedObj.Centroid = newC;
                matchedObj.DiameterUm = newD;
                matchedObj.Contour = newContours[i];
                matchedObj.MissingFrames = 0;

                currentObjects.Add(matchedObj);
            }
        }

        for (int i = 0; i < newCentroids.Count; i++)
        {
            if (!matchedNew[i])
            {
                currentObjects.Add(new TrackedObject
                {
                    Id = _nextId++,
                    Centroid = newCentroids[i],
                    DiameterUm = newDiameters[i],
                    Contour = newContours[i],
                    MissingFrames = 0
                });
            }
        }

        for (int j = 0; j < _trackedObjects.Count; j++)
        {
            if (!matchedOld[j])
            {
                var oldObj = _trackedObjects[j];
                oldObj.MissingFrames++;
                if (oldObj.MissingFrames < 5) // Keep unseen objects alive for 5 frames
                {
                    currentObjects.Add(oldObj);
                }
            }
        }

        _trackedObjects = currentObjects;

        var visible = new System.Collections.Generic.List<TrackedObject>();
        foreach (var obj in currentObjects)
            if (obj.MissingFrames == 0) visible.Add(obj);

        return visible;
    }

    public System.Collections.Generic.List<TrackedObject> GetVisibleObjects()
    {
        var visible = new System.Collections.Generic.List<TrackedObject>();
        foreach (var obj in _trackedObjects)
            if (obj.MissingFrames == 0) visible.Add(obj);
        return visible;
    }

    public void Reset()
    {
        _trackedObjects.Clear();
        _nextId = 1;
    }
}


public class VisionService : IVisionService
{
    private readonly ICalibrationService _calibrationService;
    private readonly IAnalysisService _analysisService;
    private readonly IInferenceService _inferenceService;

    private VideoCapture? _capture;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _cameraTask;
    private bool _isRunning;

    private float _confThreshold = 0.25f;
    private string _currentLensStr = "10x";
    private string _currentProvider = "CPU";

    private static readonly object _logLock = new object();

    public bool IsRunning => _isRunning;
    public event EventHandler<VisionEventArgs>? OnFrameProcessed;

    public VisionService(
        ICalibrationService calibrationService,
        IAnalysisService analysisService,
        IInferenceService inferenceService)
    {
        _calibrationService = calibrationService;
        _analysisService = analysisService;
        _inferenceService = inferenceService;

        // Initialize Dual AI Models dynamically based on default lens
        ReloadModelForLens();
    }

    public void StartCamera(int cameraIndex, string currentLensStr, string cameraApi, string resolution)
    {
        try
        {
            if (_isRunning) StopCamera();

            _currentLensStr = currentLensStr;
            Console.WriteLine($"[VisionService] StartCamera Request - Index: {cameraIndex}, API: {cameraApi}, Res: {resolution}");

            if (cameraApi == "DirectShow")
            {
                _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
                Console.WriteLine($"[VisionService] DirectShow Attempt: {(_capture.IsOpened() ? "SUCCESS" : "FAILED")}");
            }
            else if (cameraApi == "Media Foundation")
            {
                _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.MSMF);
                Console.WriteLine($"[VisionService] Media Foundation Attempt: {(_capture.IsOpened() ? "SUCCESS" : "FAILED")}");
            }
            else // Auto
            {
                Console.WriteLine("[VisionService] Auto API Selection: Probing backends...");
                // Prioritize DSHOW for external USB microscope cameras on Windows
                _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
                if (_capture.IsOpened())
                {
                    Console.WriteLine("[VisionService] Auto (DSHOW) Attempt: SUCCESS");
                }
                else
                {
                    Console.WriteLine("[VisionService] Auto (DSHOW) Attempt: FAILED. Trying MSMF...");
                    _capture.Dispose();
                    _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.MSMF);
                    if (_capture.IsOpened())
                    {
                        Console.WriteLine("[VisionService] Auto (MSMF) Attempt: SUCCESS");
                    }
                    else
                    {
                        Console.WriteLine("[VisionService] Auto (MSMF) Attempt: FAILED. Trying Default ANY...");
                        _capture.Dispose();
                        _capture = new VideoCapture(cameraIndex); // Default ANY
                        Console.WriteLine($"[VisionService] Auto (Default ANY) Attempt: {(_capture.IsOpened() ? "SUCCESS" : "FAILED")}");
                    }
                }
            }

            if (_capture == null || !_capture.IsOpened())
            {
                _capture?.Dispose();
                _capture = null;
                Console.WriteLine($"[VisionService] ERROR: Failed to open camera index {cameraIndex} with any backend.");
                return;
            }

            // Parse and apply standard microscope 4:3 resolutions
            int width = 1280;
            int height = 960;

            if (!string.IsNullOrEmpty(resolution) && resolution.Contains("x"))
            {
                var parts = resolution.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                {
                    width = w;
                    height = h;
                }
            }

            try
            {
                Console.WriteLine($"[VisionService] Configuring Camera Index {cameraIndex}...");

                // Set resolution first without forcing FourCC
                // Many generic USB microscopes fail and return black frames if FourCC is explicitly set to MJPG when they only support YUY2
                Console.WriteLine($"[VisionService] Setting resolution to {width}x{height}");
                _capture.Set(VideoCaptureProperties.FrameWidth, width);
                _capture.Set(VideoCaptureProperties.FrameHeight, height);

                // Give the driver a moment to apply changes
                Thread.Sleep(500);

                // Verify if settings applied, otherwise try a safer resolution
                if (_capture.Get(VideoCaptureProperties.FrameWidth) == 0)
                {
                    Console.WriteLine("[VisionService] WARNING: Resolution set failed. Falling back to 640x480...");
                    _capture.Set(VideoCaptureProperties.FrameWidth, 640);
                    _capture.Set(VideoCaptureProperties.FrameHeight, 480);
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VisionService] Warning: Could not set camera properties - {ex.Message}");
            }

            // Warm-up grab to ensure the camera is actually producing frames.
            try
            {
                using var testFrame = new Mat();
                bool result = _capture.Read(testFrame);
                if (!result || testFrame.Empty())
                {
                    Console.WriteLine("[VisionService] Warning: Camera opened but failed to capture test frame.");
                }
            }
            catch { }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            // Background Thread for AI and Camera
            _cameraTask = Task.Run(() => ProcessCameraLoop(token), token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VisionService] CRITICAL Error in StartCamera: {ex.Message}");
            _capture?.Dispose();
            _capture = null;
            _isRunning = false;
        }
    }

    public void StopCamera()
    {
        _isRunning = false;
        _cancellationTokenSource?.Cancel();

        // Wait for task to finish properly
        if (_cameraTask != null)
        {
            _cameraTask.Wait(1000);
            _cameraTask = null;
        }

        if (_capture != null)
        {
            _capture.Release(); // Explicitly release camera handle
            _capture.Dispose();
            _capture = null;
        }

        // Brief pause to allow Windows/DirectShow to release hardware lock
        Thread.Sleep(200);
    }

    public void SetThreshold(float threshold) => _confThreshold = threshold;

    public void SetLens(string lensStr)
    {
        if (_currentLensStr != lensStr)
        {
            _currentLensStr = lensStr;
            ReloadModelForLens(); // Hotswap ONNX model
        }
    }

    public void SetHardwareProvider(string provider)
    {
        _currentProvider = provider;
        ReloadModelForLens(); // Reload current model into new hardware (GPU/CPU)
    }

    public void SetModelPaths(string model4x, string model10x)
    {
        _customModelFileName4x = model4x;
        _customModelFileName10x = model10x;
        ReloadModelForLens();
    }

    private void ReloadModelForLens()
    {
        string defaultModelName = _currentLensStr == "4x" ? "yolov8n_4x.onnx" : "yolov8n_10x.onnx";
        string customModelName = _currentLensStr == "4x" ? _customModelFileName4x : _customModelFileName10x;
        string modelFileName = defaultModelName;

        // Override with custom model if user selected one
        if (!string.IsNullOrEmpty(customModelName) && customModelName != "Default Model" && customModelName != "No models found in fileonnx/")
        {
            modelFileName = customModelName;
        }

        string modelPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fileonnx", modelFileName);

        lock (_lockObj)
        {
            try
            {
                _inferenceService.InitializeModel(modelPath, _currentProvider);
                Console.WriteLine($"[VisionService] Successfully loaded AI Model: {modelFileName} via {_currentProvider}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VisionService] Failed to load AI Model {modelFileName}: {ex.Message}");
            }
        }
    }

    private bool _isLiveAnalysisEnabled = false;
    private bool _requestSnapshot = false;
    private string _customModelFileName4x = "";
    private string _customModelFileName10x = "";
    private int _snapshotFreezeDurationMs = 1500; // default 1.5s
    private ObjectTracker _tracker = new ObjectTracker();
    private System.Collections.Generic.HashSet<int> _ignoreList = new();
    private System.Collections.Generic.Dictionary<int, double> _sessionDroplets = new();
    private readonly object _lockObj = new object();

    private Mat? _lastRawFrame = null;
    private Mat? _lastProcessedFrame = null;

    public void SetLiveAnalysis(bool enabled)
    {
        lock (_lockObj)
        {
            _isLiveAnalysisEnabled = enabled;
        }
    }

    public void RequestAnalysis()
    {
        lock (_lockObj)
        {
            // Trigger a single-frame analysis pass and save it to the session.
            // Do NOT freeze the camera. It must stay live.
            _requestSnapshot = true;
        }
    }

    public void UnfreezeCamera()
    {
        lock (_lockObj)
        {
            // Removed unused frozen state tracking
        }
    }

    public void SetSnapshotFreezeDuration(int milliseconds)
    {
        _snapshotFreezeDurationMs = Math.Clamp(milliseconds, 200, 10000);
    }

    public System.Collections.Generic.List<double> GetSessionDroplets()
    {
        lock (_lockObj)
        {
            return new System.Collections.Generic.List<double>(_sessionDroplets.Values);
        }
    }

    public void ClearSession()
    {
        lock (_lockObj)
        {
            _sessionDroplets.Clear();
            _ignoreList.Clear();
            _tracker.Reset();
        }
    }

    public (Mat? Raw, Mat? Processed) GetLatestFrames()
    {
        lock (_lockObj)
        {
            return (_lastRawFrame?.Clone(), _lastProcessedFrame?.Clone());
        }
    }

    public void ToggleIgnoreDroplet(double x, double y)
    {
        lock (_lockObj)
        {
            var currentVisible = _tracker.GetVisibleObjects();
            foreach (var obj in currentVisible)
            {
                if (Cv2.PointPolygonTest(obj.Contour, new Point2f((float)x, (float)y), false) >= 0)
                {
                    if (_ignoreList.Contains(obj.Id))
                        _ignoreList.Remove(obj.Id);
                    else
                        _ignoreList.Add(obj.Id);
                    break;
                }
            }
        }
    }

    private void ProcessCameraLoop(CancellationToken token)
    {
        using Mat frame = new Mat();
        Stopwatch readFailureStopwatch = new Stopwatch();

        while (_isRunning && !token.IsCancellationRequested && _capture != null)
        {
            try
            {
                if (!_capture.Read(frame) || frame.Empty())
                {
                    if (!readFailureStopwatch.IsRunning) readFailureStopwatch.Restart();

                    if (readFailureStopwatch.ElapsedMilliseconds > 5000)
                    {
                        Console.WriteLine("[VisionService] ERROR: Camera stopped sending frames. Shutting down service.");
                        _isRunning = false;
                        break;
                    }
                    Thread.Sleep(10);
                    continue;
                }
                readFailureStopwatch.Reset();

                // Detect if the frame is completely black (driver/exposure issue)
                var meanColor = Cv2.Mean(frame);
                if (meanColor.Val0 < 1.0 && meanColor.Val1 < 1.0 && meanColor.Val2 < 1.0)
                {
                    Console.WriteLine("[VisionService] WARNING: Frame is completely black (Mean < 1.0). Driver issue or lens is blocked.");
                }

                AnalysisResult? stats = null;
                WriteableBitmap avaloniaBitmap;

                // Save raw frame clone safely
                Mat currentRaw = frame.Clone();

                bool triggerAI = false;
                bool isLive = false;

                lock (_lockObj)
                {
                    if (_requestSnapshot)
                    {
                        triggerAI = true;
                        _requestSnapshot = false; // Consume the trigger signal
                    }
                    isLive = _isLiveAnalysisEnabled;
                }

                // AI Processing is triggered if snapshot requested OR if Live Analysis is on
                if (triggerAI || isLive)
                {
                    // AI
                    InferenceResult? inferenceResult = null;
                    try
                    {
                        inferenceResult = _inferenceService.RunInference(frame, _confThreshold);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[VisionService] AI Inference Error: {ex.Message}");
                    }

                    if (inferenceResult != null)
                    {
                        // Math (Px to Um) and Size Filtering out Dust/Artifacts
                        double baseRatio = _calibrationService.GetPixelToMicronRatio(_currentLensStr);
                        double resolutionScale = 1280.0 / (double)frame.Width; // Compensate for non-720p native cameras
                        double ratio = baseRatio * resolutionScale;

                        var validUmDiameters = new System.Collections.Generic.List<double>();
                        var validContours = new System.Collections.Generic.List<OpenCvSharp.Point[]>();
                        var validCentroids = new System.Collections.Generic.List<Point2f>();

                        for (int i = 0; i < inferenceResult.DiametersPx.Count; i++)
                        {
                            double umDiameter = inferenceResult.DiametersPx[i] * ratio;
                            var centroid = inferenceResult.Centroids[i];

                            // Margin Exclusion (Vignette edge safe-zone): 40px padding
                            int margin = 40;
                            if (centroid.X < margin || centroid.X > (frame.Width - margin) ||
                                centroid.Y < margin || centroid.Y > (frame.Height - margin))
                            {
                                continue; // Skip droplets touching the dark edge rings
                            }

                            // WHO Recommendation / Logic: Filter out specks (<1 µm) and huge background blobs/streaks (>300 µm)
                            if (umDiameter >= 1.0 && umDiameter <= 300.0)
                            {
                                validUmDiameters.Add(umDiameter);
                                validContours.Add(inferenceResult.Contours[i]);
                                validCentroids.Add(centroid);
                            }
                        }

                        // Update Tracker
                        var activeDroplets = _tracker.Update(validCentroids, validUmDiameters, validContours);

                        var finalUmDiameters = new System.Collections.Generic.List<double>();
                        var activeContours = new System.Collections.Generic.List<OpenCvSharp.Point[]>();
                        var outOfBoundsContours = new System.Collections.Generic.List<OpenCvSharp.Point[]>();

                        using Mat overlay = frame.Clone();

                        lock (_lockObj)
                        {
                            foreach (var drop in activeDroplets)
                            {
                                if (!_ignoreList.Contains(drop.Id))
                                {
                                    // Freeze logic: lock droplet size to first-seen value
                                    if (_sessionDroplets.TryGetValue(drop.Id, out double frozenUm))
                                        drop.DiameterUm = frozenUm;
                                    else
                                        _sessionDroplets[drop.Id] = drop.DiameterUm;

                                    finalUmDiameters.Add(drop.DiameterUm);

                                    bool isOobContour = drop.DiameterUm < 5.0 || drop.DiameterUm > 30.0;
                                    if (isOobContour) outOfBoundsContours.Add(drop.Contour);
                                    else activeContours.Add(drop.Contour);
                                }
                                else
                                {
                                    // Ignored: remove from session and draw X label
                                    _sessionDroplets.Remove(drop.Id);
                                    Cv2.DrawContours(frame, new[] { drop.Contour }, -1, new Scalar(0, 0, 255), 2);
                                    Cv2.PutText(frame, "X", new OpenCvSharp.Point((int)drop.Centroid.X - 5, (int)drop.Centroid.Y + 5),
                                        HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);
                                }
                            }
                        }

                        // Calculate stats on the current frame's droplets only (minus ignored)
                        stats = _analysisService.CalculateStatistics(finalUmDiameters);

                        if (stats != null)
                        {
                            lock (_lockObj)
                            {
                                stats.TotalAccumulatedCount = _sessionDroplets.Count;
                                stats.InferenceTimeMs = inferenceResult.InferenceTimeMs;
                            }
                        }

                        // Draw contour fills FIRST so labels render on top
                        if (activeContours.Count > 0)
                        {
                            using Mat fillOverlay = frame.Clone();
                            Cv2.DrawContours(fillOverlay, activeContours, -1, new Scalar(219, 161, 52), -1);
                            Cv2.AddWeighted(fillOverlay, 0.2, frame, 0.8, 0, frame);
                            Cv2.DrawContours(frame, activeContours, -1, new Scalar(219, 161, 52), 1);
                        }

                        if (outOfBoundsContours.Count > 0)
                        {
                            using Mat redOverlay = frame.Clone();
                            Cv2.DrawContours(redOverlay, outOfBoundsContours, -1, new Scalar(0, 0, 220), -1);
                            Cv2.AddWeighted(redOverlay, 0.25, frame, 0.75, 0, frame);
                            Cv2.DrawContours(frame, outOfBoundsContours, -1, new Scalar(0, 0, 220), 2);
                        }

                        // Anti-Overlap Logic: Track occupied screen space for labels
                        var occupiedLabels = new System.Collections.Generic.List<OpenCvSharp.Rect>();

                        // Draw badges (labels) AFTER fills so they are on top
                        lock (_lockObj)
                        {
                            foreach (var drop in activeDroplets)
                            {
                                if (_ignoreList.Contains(drop.Id)) continue;

                                bool isOutOfBounds = drop.DiameterUm < 5.0 || drop.DiameterUm > 30.0;
                                Scalar badgeColor = isOutOfBounds ? new Scalar(0, 0, 200) : new Scalar(180, 120, 30);

                                // Get bounding rect from contour
                                var rect = Cv2.BoundingRect(drop.Contour);

                                // Draw thin bounding rectangle around droplet
                                Cv2.Rectangle(frame, rect, badgeColor, 1);

                                string label = $"{drop.DiameterUm:F1} µm";
                                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.38, 1, out int baseline);

                                int padX = 4, padY = 3;
                                int bw = textSize.Width + padX * 2;
                                int bh = textSize.Height + padY * 2;

                                // Initial anchor position: Above the bounding box
                                int bx = Math.Clamp(rect.X, 0, frame.Width - bw - 1);
                                int by = rect.Y - bh - 2;

                                // Anti-Overlap check: If overlapping another label, move it down
                                bool overlap = true;
                                int attempts = 0;
                                while (overlap && attempts < 3)
                                {
                                    overlap = false;
                                    var currentLabelRect = new OpenCvSharp.Rect(bx, by, bw, bh);
                                    foreach (var occ in occupiedLabels)
                                    {
                                        if (occ.IntersectsWith(currentLabelRect))
                                        {
                                            overlap = true;
                                            by += bh + 2; // Move down
                                            attempts++;
                                            break;
                                        }
                                    }
                                }

                                // Clamp final position to frame
                                bx = Math.Clamp(bx, 0, frame.Width - bw - 1);
                                by = Math.Clamp(by, 0, frame.Height - bh - 1);

                                var finalLabelRect = new OpenCvSharp.Rect(bx, by, bw, bh);
                                occupiedLabels.Add(finalLabelRect);

                                // Draw Badge Background and Text
                                Cv2.Rectangle(frame, finalLabelRect, badgeColor, -1);
                                Cv2.PutText(frame, label,
                                    new OpenCvSharp.Point(bx + padX, by + bh - padY - 1),
                                    HersheyFonts.HersheySimplex, 0.38,
                                    new Scalar(255, 255, 255), 1, LineTypes.AntiAlias);
                            }
                        }
                    }

                    avaloniaBitmap = CreateWriteableBitmapFromMat(frame);
                }
                else
                {
                    // Full Live View (No Analysis)
                    avaloniaBitmap = CreateWriteableBitmapFromMat(frame);
                }

                // isSnapshotFrame: true เมื่อ user กด Snapshot (ไม่ใช่ live mode)
                bool isSnapshotFrame = triggerAI && !isLive;

                // Update last frames securely for UI to fetch
                lock (_lockObj)
                {
                    _lastRawFrame?.Dispose();
                    _lastRawFrame = currentRaw;

                    _lastProcessedFrame?.Dispose();
                    _lastProcessedFrame = frame.Clone();
                }

                // 3. Fire event to UI
                OnFrameProcessed?.Invoke(this, new VisionEventArgs
                {
                    ProcessedBitmap = avaloniaBitmap,
                    Analysis = stats,
                    IsSnapshot = isSnapshotFrame
                });

                // Throttle slightly to simulate ~30fps
                Thread.Sleep(30);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Ensure currentRaw is disposed in case of error mid-loop
                frame.Dispose();
                // We'll let the next loop iteration re-initialize frame

                string logMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Camera Loop Error: {ex.Message}\n{ex.StackTrace}\n";
                try
                {
                    string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string logDir = System.IO.Path.Combine(appDataPath, "DropDetect");
                    if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                    string logFile = System.IO.Path.Combine(logDir, "analyze_debug.log");
                    lock (_logLock)
                    {
                        System.IO.File.AppendAllText(logFile, logMsg);
                    }
                }
                catch { /* Failsafe, do nothing if logging fails */ }
                Console.WriteLine($"Camera Loop Error: {ex.Message}");
            }
        }
    }

    private WriteableBitmap CreateWriteableBitmapFromMat(Mat mat)
    {
        using Mat bgraMat = new Mat();
        Cv2.CvtColor(mat, bgraMat, ColorConversionCodes.BGR2BGRA);

        var bitmapOutput = new WriteableBitmap(
            new PixelSize(bgraMat.Width, bgraMat.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul
        );

        using (var buf = bitmapOutput.Lock())
        {
            unsafe
            {
                byte* pSrc = (byte*)bgraMat.Data;
                byte* pDest = (byte*)buf.Address;

                int rowBytesSrc = bgraMat.Width * 4;
                int rowBytesDest = buf.RowBytes;

                for (int y = 0; y < bgraMat.Height; y++)
                {
                    Buffer.MemoryCopy(pSrc + (y * rowBytesSrc), pDest + (y * rowBytesDest), rowBytesDest, rowBytesSrc);
                }
            }
        }
        return bitmapOutput;
    }

    public void Dispose()
    {
        StopCamera();
        _inferenceService?.Dispose();
    }
}
