using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;

namespace GogglesUsb;

internal sealed class SunshineHost : IDisposable
{
    public const int Port = 48010;

    private readonly AppPaths _paths;
    private Process? _proc;
    private readonly StringBuilder _tail = new();
    private readonly object _lock = new();

    public SunshineHost(AppPaths paths) => _paths = paths;

    public event Action? PinRequested;

    public bool IsRunning => _proc is { HasExited: false };

    public string? ExePath
    {
        get
        {
            var direct = Path.Combine(_paths.SunshineDir, "sunshine.exe");
            if (File.Exists(direct)) return direct;
            try
            {
                if (Directory.Exists(_paths.SunshineDir))
                {
                    return Directory.GetFiles(_paths.SunshineDir, "sunshine.exe", SearchOption.AllDirectories).FirstOrDefault();
                }
            }
            catch
            {

            }
            return null;
        }
    }

    public async Task EnsureInstalledAsync(IProgress<string> log, IProgress<double> progress, CancellationToken ct)
    {
        if (ExePath is not null) return;

        Directory.CreateDirectory(_paths.SunshineDir);
        var asset = await Downloader.ResolveSunshineAssetAsync(log, ct).ConfigureAwait(false);
        var zip = Path.Combine(_paths.DataDir, "sunshine-download.zip");
        await Downloader.DownloadAsync(asset, zip, log, progress, ct).ConfigureAwait(false);

        var temp = Path.Combine(_paths.DataDir, "sunshine-unzip");
        Downloader.TryDelete(temp);
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(zip, temp, true);
            var exe = Directory.GetFiles(temp, "sunshine.exe", SearchOption.AllDirectories).FirstOrDefault()
                      ?? throw new InvalidOperationException("压缩包里没有 sunshine.exe");
            CopyTree(Path.GetDirectoryName(exe)!, _paths.SunshineDir);
        }
        finally
        {
            Downloader.TryDelete(temp);
            Downloader.TryDelete(zip);
        }

        if (ExePath is null) throw new InvalidOperationException("Sunshine 不可用");
    }

    public void Start()
    {
        var exe = ExePath ?? throw new InvalidOperationException("Sunshine 不可用");
        Directory.CreateDirectory(_paths.ConfigDir);
        File.WriteAllText(_paths.ConfFile, BuildConfig(), new UTF8Encoding(false));
        File.WriteAllText(_paths.AppsFile, AppsJson, new UTF8Encoding(false));

        var psi = new ProcessStartInfo(exe, $"-0 \"{_paths.ConfFile}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        _proc = Process.Start(psi) ?? throw new InvalidOperationException("启动 sunshine.exe 失败");
        _ = PumpAsync(_proc.StandardOutput);
        _ = PumpAsync(_proc.StandardError);
    }

    private async Task PumpAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var pending = new StringBuilder();
        var sawPin = false;
        try
        {
            while (true)
            {
                var n = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
                if (n <= 0) break;
                var chunk = new string(buffer, 0, n);

                if (chunk.Contains("insert pin", StringComparison.OrdinalIgnoreCase))
                {
                    if (!sawPin)
                    {
                        sawPin = true;
                        PinRequested?.Invoke();
                    }
                }
                else if (sawPin)
                {
                    sawPin = false;
                }

                pending.Append(chunk);
                var text = pending.ToString();
                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length - 1; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (line.Length == 0) continue;
                    lock (_lock)
                    {
                        _tail.AppendLine(line);
                        if (_tail.Length > 4000) _tail.Remove(0, 2000);
                    }
                }
                pending.Clear();
                pending.Append(lines[^1]);
            }
        }
        catch
        {

        }
    }

    public bool SubmitPin(string pin)
    {
        if (_proc is null || _proc.HasExited) return false;
        try
        {
            _proc.StandardInput.Write(pin + "\n");
            _proc.StandardInput.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> WaitReadyAsync(int timeoutMs = 30000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (_proc is { HasExited: true })
            {
                throw new InvalidOperationException("Sunshine 启动后立刻退出了");
            }
            if (IsPortBusy(Port)) return true;
            await Task.Delay(300).ConfigureAwait(false);
        }
        return false;
    }

    public bool TryFreePort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo("netstat", "-ano -p TCP")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return !IsPortBusy(port);

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.Contains(":" + port, StringComparison.Ordinal)) continue;
                if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5 || !int.TryParse(parts[^1], out var pid)) continue;

                try
                {
                    using var proc = Process.GetProcessById(pid);
                    var path = proc.MainModule?.FileName;
                    if (path is not null && Path.GetFileName(path).Equals("sunshine.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill(true);
                        proc.WaitForExit(3000);
                    }
                }
                catch
                {

                }
            }

            Thread.Sleep(400);
            return !IsPortBusy(port);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPortBusy(int port)
    {
        try
        {
            using var c = new TcpClient();
            var t = c.ConnectAsync("127.0.0.1", port);
            return t.Wait(400) && t.IsCompletedSuccessfully;
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        var p = _proc;
        _proc = null;
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

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(from, file);
            var dest = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    private const string AppsJson = """
    {
      "env": {},
      "apps": [
        { "name": "Desktop", "image-path": "desktop.png" }
      ]
    }
    """;

    private string BuildConfig()
    {
        var sb = new StringBuilder();
        sb.AppendLine("sunshine_name = GogglesUsb");
        sb.AppendLine("port = " + Port);
        sb.AppendLine("upnp = disabled");
        sb.AppendLine("address_family = ipv4");
        sb.AppendLine("origin_web_ui_allowed = pc");
        sb.AppendLine("min_log_level = info");
        sb.AppendLine("system_tray = disabled");
        sb.AppendLine("locale = zh");
        sb.AppendLine("file_apps = " + _paths.AppsFile);
        sb.AppendLine("file_state = " + _paths.StateFile);
        sb.AppendLine("log_path = " + _paths.LogFile);
        sb.AppendLine("pkey = " + Path.Combine(_paths.CredentialsDir, "cakey.pem"));
        sb.AppendLine("cert = " + Path.Combine(_paths.CredentialsDir, "cacert.pem"));
        sb.AppendLine("stream_audio = enabled");
        sb.AppendLine("lan_encryption_mode = 0");

        sb.AppendLine("dd_configuration_option = disabled");
        sb.AppendLine("dd_resolution_option = disabled");
        sb.AppendLine("dd_refresh_rate_option = disabled");
        return sb.ToString();
    }
}
