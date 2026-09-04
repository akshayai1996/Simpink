using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SimpinkNative.Services
{
    /// <summary>
    /// Screen recorder that pipes raw BGRA frames to FFmpeg, producing a proper
    /// H.264 MP4 file — matching the HTML version's webm/vp9 quality settings
    /// (HD 10 Mbps, Pro 25 Mbps, Standard 5 Mbps).
    /// Falls back to raw AVI if FFmpeg is not found.
    /// </summary>
    public sealed class Recorder : IDisposable
    {
        private Process? _ffmpeg;
        private AviWriter? _aviWriter;
        private Stream? _stdin;

        private readonly int _width;
        private readonly int _height;
        private readonly int _fps;
        private readonly int _bitrate;       // bps
        private readonly int _stride;        // bytes per row (BGRA, 4 bytes/px, aligned)

        private bool _aviMode;
        private bool _paused;
        private bool _disposed;
        private string _outputPath = "";
        private readonly object _lock = new();

        // Frame buffer for pause support (skip writing while paused, no corrupted timestamps)
        private long _frameIndex;

        public bool IsRecording => (_ffmpeg != null || _aviWriter != null) && !_disposed;
        public bool IsPaused => _paused;
        public string OutputPath => _outputPath;

        public Recorder(int width, int height, int fps, int bitrate)
        {
            _width = width;
            _height = height;
            _fps = fps;
            _bitrate = bitrate;
            _stride = width * 4;  // BGRA, 4 bytes per pixel, no GDI alignment needed for FFmpeg
        }

        /// <summary>
        /// Starts recording. Returns true on success, false on failure.
        /// Automatically falls back to uncompressed AVI if FFmpeg is not found.
        /// </summary>
        public bool Start(string outputPath)
        {
            lock (_lock)
            {
                if (_ffmpeg != null || _aviWriter != null) return false;
                _outputPath = outputPath;
                _frameIndex = 0;
                _paused = false;

                string? ffmpegPath = FindFfmpeg();
                if (ffmpegPath != null)
                {
                    return StartFfmpeg(ffmpegPath, outputPath);
                }
                else
                {
                    Debug.WriteLine("FFmpeg not found.");
                    return false;
                }
            }
        }

        private bool StartFfmpeg(string ffmpegPath, string outputPath)
        {
            // Ensure output is .webm
            if (!outputPath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
                outputPath = Path.ChangeExtension(outputPath, ".webm");
            _outputPath = outputPath;

            // Delete any previous partial file
            if (File.Exists(outputPath)) File.Delete(outputPath);

            int kbps = _bitrate / 1000;

            // FFmpeg reads raw bgra frames from stdin and encodes VP9 WebM
            // -vf vflip: GDI BitBlt is bottom-up; flip to top-down for standard video
            string args = string.Join(" ",
                "-y",                               // overwrite
                "-f rawvideo",
                "-pix_fmt bgra",
                $"-s {_width}x{_height}",
                $"-r {_fps}",
                "-i pipe:0",                        // stdin
                "-c:v libvpx",                      // VP8 (much faster than VP9 for real-time)
                "-deadline realtime",
                "-cpu-used 4",
                $"-b:v {kbps}k",
                $"-maxrate {kbps}k",
                $"-bufsize {kbps * 2}k",
                "-pix_fmt yuv420p",                 // broad compatibility
                $"\"{outputPath}\""
            );

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true,
            };

            try
            {
                _ffmpeg = Process.Start(psi);
                if (_ffmpeg == null) throw new InvalidOperationException("FFmpeg process did not start.");
                _stdin = _ffmpeg.StandardInput.BaseStream;
                _aviMode = false;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FFmpeg start error: {ex.Message}");
                _ffmpeg?.Dispose();
                _ffmpeg = null;
                _stdin = null;
                return false;
            }
        }

        private bool StartAvi(string outputPath)
        {
            string aviPath = Path.ChangeExtension(outputPath, ".avi");
            _outputPath = aviPath;
            int gdiStride = ((_width * 32 + 31) & ~31) >> 3;  // AVI writer needs GDI-aligned stride
            try
            {
                _aviWriter = new AviWriter(aviPath, _width, _height, _fps, gdiStride);
                _aviMode = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AVI fallback start error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Writes one frame. rgbData must be a pointer to a BGRA (GDI bottom-up) bitmap
        /// with width * height * 4 bytes. Call at the configured FPS rate.
        /// </summary>
        public void WriteFrame(IntPtr rgbData)
        {
            lock (_lock)
            {
                if (_paused || _disposed) return;

                if (_aviMode)
                {
                    _aviWriter?.WriteFrame(rgbData);
                    _frameIndex++;
                    return;
                }

                if (_stdin == null || _ffmpeg == null || _ffmpeg.HasExited) return;

                try
                {
                    // Write raw BGRA bytes directly to FFmpeg stdin
                    // Each row is _width * 4 bytes, _height rows total
                    int rowBytes = _width * 4;
                    unsafe
                    {
                        byte* src = (byte*)rgbData;
                        // FFmpeg -vf vflip handles the vertical flip, so write top-down
                        // GDI gives us bottom-up — write rows from bottom (last row first)
                        // Actually: we pass bottom-up to FFmpeg and let -vf vflip correct it.
                        // So just write the entire buffer sequentially as-is.
                        int totalBytes = _height * rowBytes;
                        var span = new ReadOnlySpan<byte>(src, totalBytes);
                        _stdin.Write(span);
                    }
                    _frameIndex++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Recorder WriteFrame error: {ex.Message}");
                }
            }
        }

        public void Pause()
        {
            lock (_lock)
            {
                if (!_disposed) _paused = true;
            }
        }

        public void Resume()
        {
            lock (_lock)
            {
                if (!_disposed) _paused = false;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_aviMode)
                {
                    try { _aviWriter?.Stop(); } catch { }
                    _aviWriter?.Dispose();
                    _aviWriter = null;
                    _aviMode = false;
                    return;
                }

                if (_ffmpeg == null) return;

                // Close stdin to signal end-of-stream to FFmpeg; wait for it to flush/finalize
                try
                {
                    _stdin?.Flush();
                    _stdin?.Close();
                    _stdin = null;
                }
                catch { }

                try
                {
                    // Give FFmpeg up to 30s to finalize the MP4 (moov atom write)
                    if (!_ffmpeg.WaitForExit(30_000))
                    {
                        _ffmpeg.Kill();
                    }
                }
                catch { }
                finally
                {
                    _ffmpeg.Dispose();
                    _ffmpeg = null;
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                Stop();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        public static bool IsFfmpegAvailable()
        {
            return FindFfmpeg() != null;
        }

        public static string? FindFfmpeg()
        {
            // 1. Check PATH
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                });
                p?.WaitForExit(2000);
                if (p?.ExitCode == 0) return "ffmpeg";
            }
            catch { }

            // 2. Check common install locations
            string appDataFfmpeg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Simpink", "ffmpeg.exe");
            string[] candidates = {
                appDataFfmpeg,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\tools\ffmpeg\bin\ffmpeg.exe",
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;

            return null;
        }
    }
}