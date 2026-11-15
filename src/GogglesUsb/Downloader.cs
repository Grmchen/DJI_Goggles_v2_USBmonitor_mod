using System.Net.Http;
using System.Text.Json;

namespace GogglesUsb;

internal static class Downloader
{
    public static readonly string[] Mirrors =
    {
        "",
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/",
        "https://github.moeyy.xyz/",
    };

    public static async Task<string> DownloadAsync(string url, string dest, IProgress<string>? log,
        IProgress<double>? progress, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var mirror in Mirrors)
        {
            var full = mirror.Length == 0 ? url : mirror + url;
            try
            {
                log?.Report(mirror.Length == 0 ? "下载：" + url : "换镜像：" + mirror);
                await DownloadOneAsync(full, dest, progress, ct).ConfigureAwait(false);
                return dest;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                log?.Report("失败（" + Short(ex.Message) + "）");
            }
        }
        throw new InvalidOperationException("下载失败：" + (last?.Message ?? "网络不可用"), last);
    }

    private static async Task DownloadOneAsync(string url, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GogglesUsb/2.0");

        using var headCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headCts.CancelAfter(TimeSpan.FromSeconds(25));
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headCts.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(dest);

        var buf = new byte[81920];
        long got = 0;
        while (true)
        {
            var n = await src.ReadAsync(buf, ct).ConfigureAwait(false);
            if (n <= 0) break;
            await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
            got += n;
            if (total > 0)
            {
                progress?.Report(got * 100.0 / total);
            }
        }
    }

    public static async Task<string> ResolveSunshineAssetAsync(IProgress<string>? log, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GogglesUsb/2.0");
            var json = await http.GetStringAsync("https://api.github.com/repos/LizardByte/Sunshine/releases/latest", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                string? lite = null, any = null;
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (url is null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        !name.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
                        !name.Contains("AMD64", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (name.Contains("lite", StringComparison.OrdinalIgnoreCase)) lite ??= url;
                    else any ??= url;
                }
                var picked = lite ?? any;
                if (picked is not null) return picked;
            }
        }
        catch (Exception ex)
        {
            log?.Report("查询 Sunshine 版本失败（" + ex.Message + "），用内置地址。");
        }
        return "https://github.com/LizardByte/Sunshine/releases/download/v2026.914.233613/Sunshine-Windows-AMD64-lite.zip";
    }

    private static string Short(string s) => s.Length > 100 ? s[..100] + "…" : s;

    public static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch
        {

        }
    }
}
