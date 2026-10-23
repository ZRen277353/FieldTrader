using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FieldTrader.Market;

public sealed record CapturedScreen(Frame Frame, string ContentHash, int Width = 0, int Height = 0,
    long WindowHandle = 0, int ScreenX = 0, int ScreenY = 0, bool Live = false);
public sealed record VisionResult(string FrameId, DateTimeOffset CapturedAt, string ContentHash,
    int Width, int Height, VisualToken[] Regions, VisualToken[] Texts, double YoloMs, double OcrMs, double TotalMs);
public interface IScreenSource { Task<CapturedScreen> CaptureAsync(CancellationToken token); }
public interface IVisionPipeline { Task<VisionResult> AnalyzeAsync(CapturedScreen screen, CancellationToken token); }
public sealed class ImageFileCapture(string path, DateTimeOffset? capturedAt = null) : IScreenSource, IMarketCapture {
    public async Task<CapturedScreen> CaptureAsync(CancellationToken token) {
        var bytes = await File.ReadAllBytesAsync(path, token);
        return new(new(Guid.NewGuid().ToString("N"), capturedAt ?? File.GetLastWriteTimeUtc(path), Path.GetFullPath(path)),
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    async Task<Frame> IMarketCapture.CaptureAsync(CancellationToken token) => (await CaptureAsync(token)).Frame;
}
public sealed record VisionConfiguration(string PythonExecutable, string[] DependencyDirectories, string YoloModel,
    string YoloSha256, string OcrConfig, string OcrDetectionModel, string OcrRecognitionModel,
    string OcrClassificationModel, string CacheDirectory, int Threads, string[] Classes) {
    public static VisionConfiguration Load(string path) => JsonSerializer.Deserialize<VisionConfiguration>(File.ReadAllText(path))
        ?? throw new InvalidDataException("模型配置为空");
}
public static class Geometry {
    public static bool Contains(Region outer, Region inner) => outer.Valid && inner.Valid &&
        inner.X >= outer.X - .003 && inner.Y >= outer.Y - .003 &&
        inner.X + inner.Width <= outer.X + outer.Width + .003 && inner.Y + inner.Height <= outer.Y + outer.Height + .003;
    public static double CenterY(Region r) => r.Y + r.Height / 2;
    public static double CenterX(Region r) => r.X + r.Width / 2;
    public static string Normalize(string text) => string.Concat(text.Normalize(System.Text.NormalizationForm.FormKC)
        .Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
}
