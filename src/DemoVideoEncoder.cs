using System.Diagnostics;

namespace Beutl.HeadlessUITests.Demos;

// Both the original capture and the camera pass publish only successfully encoded files.
internal sealed class DemoVideoEncoder : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _errors;
    private readonly string _partialPath;
    private readonly string _outputPath;
    private bool _completed;

    public DemoVideoEncoder(string outputPath, int width, int height, int frameRate, string pixelFormat)
    {
        _outputPath = Path.GetFullPath(outputPath);
        if (File.Exists(_outputPath)) throw new IOException($"Output already exists: {_outputPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
        _partialPath = Path.Combine(Path.GetDirectoryName(_outputPath)!, $".{Guid.NewGuid():N}.partial.mp4");
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string arg in new[]
        {
            "-hide_banner", "-loglevel", "error", "-f", "rawvideo", "-pixel_format", pixelFormat,
            "-video_size", $"{width}x{height}", "-framerate", frameRate.ToString(), "-i", "pipe:0",
            "-vf", "scale=in_range=pc:out_range=tv:out_color_matrix=bt709",
            "-an", "-c:v", "libx264", "-preset", "fast", "-crf", "18", "-pix_fmt", "yuv420p",
            "-colorspace", "bt709", "-color_trc", "bt709", "-color_primaries", "bt709",
            "-movflags", "+faststart", "-y", _partialPath
        }) start.ArgumentList.Add(arg);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start FFmpeg.");
        _errors = _process.StandardError.ReadToEndAsync();
    }

    public static string Executable => Environment.GetEnvironmentVariable("BEUTL_DEMO_FFMPEG") ?? "ffmpeg";

    public async Task WriteAsync(byte[] pixels)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await _process.StandardInput.BaseStream.WriteAsync(pixels, timeout.Token);
        }
        catch (IOException ex)
        {
            throw new IOException($"FFmpeg stopped accepting frames: {await _errors.WaitAsync(timeout.Token)}", ex);
        }
    }

    public async Task CompleteAsync()
    {
        _process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await _process.WaitForExitAsync(timeout.Token);
        if (_process.ExitCode != 0) throw new InvalidOperationException($"FFmpeg failed: {await _errors}");
        File.Move(_partialPath, _outputPath);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync().ConfigureAwait(false);
        _process.Dispose();
        if (!_completed) File.Delete(_partialPath);
    }
}
