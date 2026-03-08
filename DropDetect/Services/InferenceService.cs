using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace DropDetect.Services;

public class InferenceResult
{
    public List<Point[]> Contours { get; set; } = new();
    public List<double> DiametersPx { get; set; } = new();
    public List<Point2f> Centroids { get; set; } = new();
    public double InferenceTimeMs { get; set; } = 0;
}

public interface IInferenceService : IDisposable
{
    void InitializeModel(string modelPath, string hardwareProvider = "CPU");
    InferenceResult RunInference(Mat frame, float confidenceThreshold);
}

public class InferenceService : IInferenceService
{
    private InferenceSession? _session;
    private string? _inputName;
    private readonly object _modelLock = new object();
    private static readonly object _logLock = new object();

    public void InitializeModel(string modelPath, string hardwareProvider = "CPU")
    {
        lock (_modelLock)
        {
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelPath);
            if (!File.Exists(fullPath))
            {
                // Fallback for debugging paths if ran from project dir
                fullPath = modelPath;
            }

            if (!File.Exists(fullPath))
            {
                Console.WriteLine($"Error: Model file not found at {fullPath}");
                return;
            }

            try
            {
                var options = new SessionOptions();

                if (hardwareProvider.Contains("GPU") || hardwareProvider.Contains("DirectML"))
                {
                    try
                    {
                        int deviceId = 0;
                        var match = System.Text.RegularExpressions.Regex.Match(hardwareProvider, @"DeviceId:\s*(\d+)");
                        if (match.Success)
                        {
                            deviceId = int.Parse(match.Groups[1].Value);
                        }
                        options.AppendExecutionProvider_DML(deviceId);
                        Console.WriteLine($"DirectML Execution Provider appended successfully (Device: {deviceId}).");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"DirectML initialization failed: {ex.Message}. Falling back to CPU.");
                        options.AppendExecutionProvider_CPU();
                    }
                }
                else
                {
                    options.AppendExecutionProvider_CPU();
                }

                _session?.Dispose();
                _session = new InferenceSession(fullPath, options);
                if (_session.InputMetadata.Count > 0)
                {
                    _inputName = _session.InputMetadata.Keys.First();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing ONNX Model: {ex.Message}");
            }
        }
    }

    public InferenceResult RunInference(Mat frame, float confidenceThreshold)
    {
        var result = new InferenceResult();

        lock (_modelLock)
        {
            if (_session == null || _inputName == null || frame.Empty())
                return result;

            int modelWidth = 640;
            int modelHeight = 640;

        // 1. Pre-process frame
        // YOLO requires RGB, 640x640, Float32 Tensor, NCHW order, normalized 0-1
        using Mat resized = new Mat();
        using Mat rgb = new Mat();

        Cv2.Resize(frame, resized, new Size(modelWidth, modelHeight));
        Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

        // Convert data to float
        using Mat floatMat = new Mat();
        rgb.ConvertTo(floatMat, MatType.CV_32FC3, 1.0 / 255.0); // Normalize to 0.0 - 1.0

        // BCHW format
        float[] chwFloatArray = new float[1 * 3 * modelHeight * modelWidth];
        unsafe
        {
            float* pData = (float*)floatMat.DataPointer;
            for (int r = 0; r < modelHeight; r++)
            {
                for (int c = 0; c < modelWidth; c++)
                {
                    // OpenCV is interleaved (HWC)
                    int idxMat = (r * modelWidth + c) * 3;
                    chwFloatArray[0 * modelHeight * modelWidth + r * modelWidth + c] = pData[idxMat];     // R
                    chwFloatArray[1 * modelHeight * modelWidth + r * modelWidth + c] = pData[idxMat + 1]; // G
                    chwFloatArray[2 * modelHeight * modelWidth + r * modelWidth + c] = pData[idxMat + 2]; // B
                }
            }
        }

        var tensor = new DenseTensor<float>(chwFloatArray, new int[] { 1, 3, modelHeight, modelWidth });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };

        // 2. Inference
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var results = _session.Run(inputs);
            sw.Stop();
            result.InferenceTimeMs = sw.Elapsed.TotalMilliseconds;

            // We process Results[0] (Detection boxes/classes/masks weights) and Results[1] (Mask prototypes)
            if (results.Count > 0)
            {
                var outputTensor = results[0].AsTensor<float>();

                // DEBUG: Log the tensor shape to verify Yolov11 structure
                var dims = outputTensor.Dimensions;
                string dimStr = string.Join("x", dims.ToArray());
                try
                {
                    lock (_logLock)
                    {
                        System.IO.File.AppendAllText("analyze_debug.log", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Tensor Output Shape: {dimStr}\n");
                    }
                }
                catch { }

                // Determine orientation: [1, Features, Proposals] vs [1, Proposals, Features]
                bool isTransposed = dims.Length > 2 && dims[1] > dims[2]; // e.g. 1x8400x37 -> True

                int numProposals = isTransposed ? dims[1] : dims[2];
                int featureCount = isTransposed ? dims[2] : dims[1];

                var allDetections = new List<DetectionInfo>();

                for (int i = 0; i < numProposals; i++)
                {
                    float cx = isTransposed ? outputTensor[0, i, 0] : outputTensor[0, 0, i];
                    float cy = isTransposed ? outputTensor[0, i, 1] : outputTensor[0, 1, i];
                    float w = isTransposed ? outputTensor[0, i, 2] : outputTensor[0, 2, i];
                    float h = isTransposed ? outputTensor[0, i, 3] : outputTensor[0, 3, i];
                    float conf = isTransposed ? outputTensor[0, i, 4] : outputTensor[0, 4, i];

                    // Scale back to original resolution
                    cx *= (frame.Width / 640.0f);
                    cy *= (frame.Height / 640.0f);
                    w *= (frame.Width / 640.0f);
                    h *= (frame.Height / 640.0f);

                    if (conf >= confidenceThreshold)
                    {
                        // Filter out impossibly large boxes (e.g., background gradients)
                        if (w > frame.Width * 0.85 || h > frame.Height * 0.85) continue;

                        allDetections.Add(new DetectionInfo { Confidence = conf, Cx = cx, Cy = cy, W = w, H = h });
                    }
                }

                // --- Non-Maximum Suppression (NMS) ---
                float nmsThreshold = 0.45f;
                var filteredDetections = new List<DetectionInfo>();
                allDetections = allDetections.OrderByDescending(d => d.Confidence).ToList();
                bool[] isSuppressed = new bool[allDetections.Count];

                for (int i = 0; i < allDetections.Count; i++)
                {
                    if (isSuppressed[i]) continue;

                    var currentBox = allDetections[i];
                    filteredDetections.Add(currentBox);

                    for (int j = i + 1; j < allDetections.Count; j++)
                    {
                        if (!isSuppressed[j] && CalculateIoU(currentBox, allDetections[j]) > nmsThreshold)
                        {
                            isSuppressed[j] = true;
                        }
                    }
                }

                // Add filtered boxes to display
                foreach (var det in filteredDetections)
                {
                    Point[] pts = new Point[] {
                        new Point(det.Left, det.Top),
                        new Point(det.Right, det.Top),
                        new Point(det.Right, det.Bottom),
                        new Point(det.Left, det.Bottom)
                    };

                    double area = Cv2.ContourArea(pts);
                    if (area > 5)
                    {
                        result.Contours.Add(pts);
                        // Geometric Fix: Prevent area-to-circle mathematical inflation
                        // Direct average of bounding box Width and Height perfectly equals 1:1 diameter
                        double diameterPx = (det.W + det.H) / 2.0;
                        result.DiametersPx.Add(diameterPx);
                        result.Centroids.Add(new Point2f(det.Cx, det.Cy));
                    }
                }
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Inference execution error: {ex.Message}");
        }

        } // End of _modelLock
        return result;
    }



    public void Dispose()
    {
        _session?.Dispose();
        GC.SuppressFinalize(this);
    }

    private class DetectionInfo
    {
        public float Confidence { get; set; }
        public float Cx { get; set; }
        public float Cy { get; set; }
        public float W { get; set; }
        public float H { get; set; }
        public int Left => (int)(Cx - W / 2);
        public int Top => (int)(Cy - H / 2);
        public int Right => (int)(Cx + W / 2);
        public int Bottom => (int)(Cy + H / 2);
        public float Area => W * H;
    }

    private float CalculateIoU(DetectionInfo boxA, DetectionInfo boxB)
    {
        float xA = Math.Max(boxA.Left, boxB.Left);
        float yA = Math.Max(boxA.Top, boxB.Top);
        float xB = Math.Min(boxA.Right, boxB.Right);
        float yB = Math.Min(boxA.Bottom, boxB.Bottom);

        float interArea = Math.Max(0, xB - xA) * Math.Max(0, yB - yA);
        if (interArea == 0) return 0;

        float boxAArea = boxA.Area;
        float boxBArea = boxB.Area;

        return interArea / (boxAArea + boxBArea - interArea);
    }
}
