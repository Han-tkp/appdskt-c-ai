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
}

public interface IVisionService : IDisposable
{
    event EventHandler<VisionEventArgs>? OnFrameProcessed;
    void StartCamera(int cameraIndex, string currentLensStr, string cameraApi);
    void StopCamera();
    void SetThreshold(float threshold);
    void SetLens(string lensStr);
    void SetHardwareProvider(string provider);
    void RequestAnalysis();
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

    public void StartCamera(int cameraIndex, string currentLensStr, string cameraApi)
    {
        if (_isRunning) StopCamera();

        _currentLensStr = currentLensStr;

        if (cameraApi == "DirectShow")
        {
            _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
        }
        else if (cameraApi == "Media Foundation")
        {
            _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.MSMF);
        }
        else // Auto
        {
            _capture = new VideoCapture(cameraIndex); // Default ANY (Usually maps to MSMF on Windows)

            if (!_capture.IsOpened())
            {
                _capture.Dispose();
                _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.MSMF); // Media Foundation
            }

            if (!_capture.IsOpened())
            {
                _capture.Dispose();
                _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW); // DirectShow (Legacy)
            }
        }

        if (!_capture.IsOpened())
        {
            _capture?.Dispose();
            _capture = null;
            throw new Exception($"Failed to open camera index {cameraIndex}");
        }

        // Removed forcing 1280x720 to prevent driver rejection (black screens).
        // _capture.FrameWidth = 1280;
        // _capture.FrameHeight = 720;

        _isRunning = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        // Background Thread for AI and Camera
        _cameraTask = Task.Run(() => ProcessCameraLoop(token), token);
    }

    public void StopCamera()
    {
        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        _cameraTask?.Wait(2000); // Wait 2s max
        _capture?.Dispose();
        _capture = null;
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

    private void ReloadModelForLens()
    {
        string modelFileName = _currentLensStr == "4x" ? "yolov8n_4x.onnx" : "yolov8n_10x.onnx";
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

    private bool _isAnalyzingContinuous = false;
    private ObjectTracker _tracker = new ObjectTracker();
    private System.Collections.Generic.HashSet<int> _ignoreList = new();
    private System.Collections.Generic.Dictionary<int, double> _sessionDroplets = new();
    private readonly object _lockObj = new object();

    private Mat? _lastRawFrame = null;
    private Mat? _lastProcessedFrame = null;

    public void RequestAnalysis()
    {
        _isAnalyzingContinuous = !_isAnalyzingContinuous; // Toggle Pause/Resume
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

        while (_isRunning && !token.IsCancellationRequested && _capture != null)
        {
            try
            {
                if (!_capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(10);
                    continue;
                }

                AnalysisResult? stats = null;
                WriteableBitmap avaloniaBitmap;

                // Save raw frame clone safely
                Mat currentRaw = frame.Clone();

                if (_isAnalyzingContinuous)
                {
                    // AI
                    var inferenceResult = _inferenceService.RunInference(frame, _confThreshold);

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

                        // WHO Recommendation / Logic: Filter out specks (<1 µm) and huge background blobs/streaks (>300 µm)
                        if (umDiameter >= 1.0 && umDiameter <= 300.0)
                        {
                            validUmDiameters.Add(umDiameter);
                            validContours.Add(inferenceResult.Contours[i]);
                            validCentroids.Add(inferenceResult.Centroids[i]);
                        }
                    }

                    // Update Tracker
                    var activeDroplets = _tracker.Update(validCentroids, validUmDiameters, validContours);

                    var finalUmDiameters = new System.Collections.Generic.List<double>();
                    var activeContours = new System.Collections.Generic.List<OpenCvSharp.Point[]>();

                    using Mat overlay = frame.Clone();

                    lock (_lockObj)
                    {
                        foreach (var drop in activeDroplets)
                        {
                            if (!_ignoreList.Contains(drop.Id))
                            {
                                finalUmDiameters.Add(drop.DiameterUm);
                                activeContours.Add(drop.Contour);

                                // Absolute Freeze Logic: If droplet already exists, lock its dimension to the original value
                                if (_sessionDroplets.TryGetValue(drop.Id, out double frozenUm))
                                {
                                    drop.DiameterUm = frozenUm; // Force current frame to match historic frozen value
                                }
                                else
                                {
                                    _sessionDroplets[drop.Id] = drop.DiameterUm; // Save newly detected droplet permanently
                                }

                                // Draw ID and Micron size in a blue badge
                                string line1 = $"{drop.Id}:";
                                string line2 = $"L={drop.DiameterUm:F2} µm";

                                var size1 = Cv2.GetTextSize(line1, HersheyFonts.HersheySimplex, 0.4, 1, out int baseline1);
                                var size2 = Cv2.GetTextSize(line2, HersheyFonts.HersheySimplex, 0.4, 1, out int baseline2);

                                int boxWidth = Math.Max(size1.Width, size2.Width) + 10;
                                int boxHeight = size1.Height + size2.Height + 15;

                                var pt = new OpenCvSharp.Point((int)drop.Centroid.X + 20, (int)drop.Centroid.Y - 20);

                                // Light Blue BGR: 219, 161, 52
                                Scalar badgeColor = new Scalar(219, 161, 52);

                                // Connect badge to centroid with a thin line
                                Cv2.Line(frame, new OpenCvSharp.Point((int)drop.Centroid.X, (int)drop.Centroid.Y), new OpenCvSharp.Point(pt.X, pt.Y), badgeColor, 1, LineTypes.AntiAlias);

                                // Draw Background Rect
                                Cv2.Rectangle(frame, new OpenCvSharp.Rect(pt.X, pt.Y - size1.Height - 5, boxWidth, boxHeight), badgeColor, -1);

                                // Draw Text
                                Cv2.PutText(frame, line1, new OpenCvSharp.Point(pt.X + 5, pt.Y + 5), HersheyFonts.HersheySimplex, 0.4, new Scalar(255, 255, 255), 1, LineTypes.AntiAlias);
                                Cv2.PutText(frame, line2, new OpenCvSharp.Point(pt.X + 5, pt.Y + size1.Height + 10), HersheyFonts.HersheySimplex, 0.4, new Scalar(255, 255, 255), 1, LineTypes.AntiAlias);
                            }
                            else
                            {
                                // Remove from session history if user clicked to ignore it
                                _sessionDroplets.Remove(drop.Id);

                                // Draw ignored in red
                                Cv2.DrawContours(frame, new[] { drop.Contour }, -1, new Scalar(0, 0, 255), 2);
                                Cv2.PutText(frame, $"Ignored", new OpenCvSharp.Point((int)drop.Centroid.X + 10, (int)drop.Centroid.Y - 10),
                                    HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 0, 255), 1, LineTypes.AntiAlias);
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

                    // Draw Masks/Contours on frame for valid droplets
                    if (activeContours.Count > 0)
                    {
                        // Use the same light blue instead of green
                        Cv2.DrawContours(overlay, activeContours, -1, new Scalar(219, 161, 52), -1);
                        Cv2.AddWeighted(overlay, 0.2, frame, 0.8, 0, frame); // Make fill more transparent
                        Cv2.DrawContours(frame, activeContours, -1, new Scalar(219, 161, 52), 1);
                    }

                    avaloniaBitmap = CreateWriteableBitmapFromMat(frame);
                }
                else
                {
                    avaloniaBitmap = CreateWriteableBitmapFromMat(frame);
                }

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
                    Analysis = stats
                });

                // Throttle slightly to simulate ~30fps
                Thread.Sleep(30);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                string logMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Camera Loop Error: {ex.Message}\n{ex.StackTrace}\n";
                try
                {
                    string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string logDir = System.IO.Path.Combine(appDataPath, "DropDetect");
                    if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                    string logFile = System.IO.Path.Combine(logDir, "analyze_debug.log");
                    System.IO.File.AppendAllText(logFile, logMsg);
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
            PixelFormat.Bgra8888
        );

        using (var buf = bitmapOutput.Lock())
        {
            int size = bgraMat.Width * bgraMat.Height * bgraMat.ElemSize();
            unsafe
            {
                Buffer.MemoryCopy((void*)bgraMat.Data, (void*)buf.Address, buf.RowBytes * bgraMat.Height, size);
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
