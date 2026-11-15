using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace GogglesUsb;

internal sealed class RelayHost : IDisposable
{
    private const string GuiZipUrl =
        "https://github.com/fpv-wtf/dji-moonlight-gui/releases/download/v1.0.2/dji-moonlight-gui-v1.0.2.zip";

    private const int Width = 1440;
    private const int Height = 810;

    private const int Fps = 120;
    private const int BitrateKbps = 20_000;

    private readonly AppPaths _paths;
    private Process? _stream;

    public RelayHost(AppPaths paths) => _paths = paths;

    public event Action<string>? Log;
    public event Action? Exited;
    public event Action? GogglesConnected;
    public event Action? ConnectTimedOut;
    public event Action? DecodeOverflow;
    private int _connectFails;
    private int _overflows;

    public string? ExePath
    {
        get
        {
            if (!Directory.Exists(_paths.RelayDir)) return null;
            try
            {
                return Directory.GetFiles(_paths.RelayDir, "moonlight.exe", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }
    }

    public bool IsPaired
    {
        get
        {
            try
            {
                return Directory.Exists(_paths.KeysDir) && Directory.GetFiles(_paths.KeysDir).Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsStreaming => _stream is { HasExited: false };

    public bool SawConnected { get; private set; }

    public async Task EnsureInstalledAsync(IProgress<string> log, IProgress<double> progress, CancellationToken ct)
    {
        if (ExePath is not null) return;

        Directory.CreateDirectory(_paths.RelayDir);
        var zip = Path.Combine(_paths.DataDir, "relay-download.zip");
        await Downloader.DownloadAsync(GuiZipUrl, zip, log, progress, ct).ConfigureAwait(false);

        ZipFile.ExtractToDirectory(zip, _paths.RelayDir, true);
        Downloader.TryDelete(zip);

        Directory.CreateDirectory(_paths.KeysDir);
        if (ExePath is null) throw new InvalidOperationException("压缩包里没有 moonlight.exe");
    }

    public async Task<bool> PairAsync(string pin, CancellationToken ct)
    {
        var exe = ExePath ?? throw new InvalidOperationException("转发器不可用");
        var args = $"pair -pin {pin} -port {SunshineHost.Port} -keydir \"{_paths.KeysDir}\"";

        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["HOME"] = _paths.DataDir;

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("启动 moonlight.exe 失败");
        var output = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        var done = await Task.Run(() => p.WaitForExit(30000), ct).ConfigureAwait(false);
        if (!done)
        {
            try { p.Kill(true); } catch { }
            return false;
        }

        var text = output.ToString();
        return p.ExitCode == 0 && !text.Contains("Failed", StringComparison.OrdinalIgnoreCase);
    }

    public bool StartStream(out string error)
    {
        error = string.Empty;
        if (IsStreaming)
        {
            error = "已经在投屏了";
            return false;
        }

        var exe = ExePath ?? throw new InvalidOperationException("转发器不可用");
        var args = "stream" +
                   " -app Desktop" +
                   " -platform dji_net" +
                   $" -port {SunshineHost.Port}" +
                   $" -bitrate {BitrateKbps}" +
                   $" -fps {Fps}" +
                   $" -width {Width}" +
                   $" -height {Height}" +
                   " -codec h264" +
                   " -localaudio" +
                   " -viewonly" +
                   $" -keydir \"{_paths.KeysDir}\"";

        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["HOME"] = _paths.DataDir;

        SawConnected = false;
        _connectFails = 0;
        _overflows = 0;
        _stream = Process.Start(psi) ?? throw new InvalidOperationException("启动 moonlight.exe 失败");
        _stream.OutputDataReceived += (_, e) => OnOutput(e.Data);
        _stream.ErrorDataReceived += (_, e) => OnOutput(e.Data);
        _stream.BeginOutputReadLine();
        _stream.BeginErrorReadLine();
        _stream.EnableRaisingEvents = true;
        WriteDiag("=== stream started: " + args);
        _stream.Exited += (_, _) =>
        {
            WriteDiag("=== stream exited, code=" + (_stream?.ExitCode.ToString() ?? "?"));
            Exited?.Invoke();
        };
        return true;
    }

    private void WriteDiag(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(_paths.DataDir, "relay.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch
        {

        }
    }

    private void OnOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        WriteDiag(line);
        Log?.Invoke(line);

        if (line.Contains("queue overflow", StringComparison.OrdinalIgnoreCase))
        {

            if (++_overflows == 3)
            {
                _overflows = 0;
                DecodeOverflow?.Invoke();
            }
            return;
        }

        if (line.Contains("connect failed", StringComparison.OrdinalIgnoreCase))
        {
            if (++_connectFails == 6)
            {
                ConnectTimedOut?.Invoke();
            }
            return;
        }

        _connectFails = 0;
        if (line.Contains("Connected", StringComparison.OrdinalIgnoreCase))
        {
            SawConnected = true;
            GogglesConnected?.Invoke();
        }
    }

    public bool HasGogglesSocket()
    {
        var proc = _stream;
        if (proc is null || proc.HasExited) return false;
        var pid = proc.Id.ToString();

        try
        {
            var psi = new ProcessStartInfo("netstat", "-ano -p TCP")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return true;

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.Contains(":42069", StringComparison.Ordinal)) continue;
                if (!line.Contains("ESTABLISHED", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && parts[^1] == pid) return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    public void Stop()
    {
        var p = _stream;
        _stream = null;
        if (p is null) return;
        try
        {
            if (!p.HasExited) p.Kill(true);
            p.WaitForExit(4000);
        }
        catch
        {

        }
        finally
        {
            try { p.Dispose(); } catch { }
        }
    }

    public void Dispose() => Stop();
}
