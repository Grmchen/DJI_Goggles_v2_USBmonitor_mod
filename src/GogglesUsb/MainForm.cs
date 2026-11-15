using System.Net.Sockets;

namespace GogglesUsb;

internal sealed class MainForm : Form
{
    private readonly AppPaths _paths = new();
    private readonly SunshineHost _sunshine;
    private readonly RelayHost _relay;
    private readonly List<string> _logLines = new();
    private bool _busy;
    private bool _running;
    private bool _reconnecting;
    private CancellationTokenSource? _reconnectCts;
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 2000 };
    private int _watchFails;

    private StatusDot _dot = null!;
    private Label _lblStatus = null!;
    private PillButton _btnMain = null!;
    private TextBox _txtLog = null!;
    private Label _lblHint = null!;

    public MainForm()
    {
        _sunshine = new SunshineHost(_paths);
        _relay = new RelayHost(_paths);

        Text = "GogglesUsb";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.F(9f);
        ClientSize = new Size(520, 330);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        DoubleBuffered = true;

        BuildUi();

        _relay.Log += line => OnUi(() => AppendLog(line));
        _relay.ConnectTimedOut += () => OnUi(() =>
        {
            if (!_running || _reconnecting) return;
            AppendLog("眼镜没响应：USB 网卡在，但 192.168.42.5:42069 连不上（眼镜上没进 Moonlight？）");
            StopInternal("连不上眼镜");
        });
        _relay.DecodeOverflow += () => OnUi(() =>
        {
            if (!_running || _reconnecting) return;
            AppendLog("解码队列溢出，立即重连…");
            _ = ReconnectAsync();
        });

        _relay.GogglesConnected += () => OnUi(() =>
        {
            SetStatus("投屏中", Theme.Ok);
            AppendLog("已连上眼镜，画面应该出现了。");

        });
        _relay.Exited += () => OnUi(() =>
        {
            if (!_running || _reconnecting) return;
            _ = ReconnectAsync();
        });
    }

    private void BuildUi()
    {
        Controls.Add(new Label
        {
            Text = "GogglesUsb",
            Location = new Point(20, 14),
            AutoSize = true,
            Font = Theme.F(16f, true),
            ForeColor = Theme.Text,
            BackColor = Theme.Bg,
        });

        _dot = new StatusDot { Location = new Point(424, 22), BackColor = Theme.Bg, Size = new Size(12, 12) };
        Controls.Add(_dot);

        _lblStatus = new Label
        {
            Text = "未运行",
            Location = new Point(442, 17),
            Size = new Size(70, 22),
            Font = Theme.F(9.5f, true),
            ForeColor = Theme.Muted,
            BackColor = Theme.Bg,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        Controls.Add(_lblStatus);

        _lblHint = new Label
        {
            Text = "插好 USB 线，并在眼镜上进入 Moonlight",
            Location = new Point(20, 46),
            Size = new Size(480, 20),
            Font = Theme.F(8.8f),
            ForeColor = Theme.Muted,
            BackColor = Theme.Bg,
        };
        Controls.Add(_lblHint);

        _btnMain = new PillButton
        {
            Text = "开始投屏",
            Location = new Point(20, 74),
            Size = new Size(480, 62),
            Fill = Theme.Accent,
            FillHover = Theme.AccentHover,
            FillDown = Theme.AccentDown,
            TextColor = Color.White,
            BackColor = Theme.Bg,
            Font = Theme.F(12.5f, true),
        };
        _btnMain.Click += async (_, _) => await ToggleAsync();
        Controls.Add(_btnMain);

        _txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.None,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Field,
            ForeColor = Theme.Muted,
            Font = new Font("Consolas", 8.5f),
            Location = new Point(20, 152),
            Size = new Size(480, 158),
            WordWrap = true,
        };
        Controls.Add(_txtLog);
    }

    private async Task ToggleAsync()
    {
        if (_busy) return;
        if (_running)
        {
            StopInternal("手动停止");
            return;
        }

        _busy = true;
        SetBusy(true);
        try
        {
            SetStatus("准备中", Theme.Warn);

            if (IsPortListening(SunshineHost.Port) && !_sunshine.TryFreePort(SunshineHost.Port))
            {
                throw new InvalidOperationException($"端口 {SunshineHost.Port} 被占用");
            }

            await _sunshine.EnsureInstalledAsync(new Progress<string>(AppendLog), new Progress<double>(), CancellationToken.None);
            if (!_sunshine.IsRunning)
            {
                _sunshine.Start();
                if (!await _sunshine.WaitReadyAsync())
                {
                    throw new InvalidOperationException("Sunshine 没起来");
                }
            }

            await _relay.EnsureInstalledAsync(new Progress<string>(AppendLog), new Progress<double>(), CancellationToken.None);

            if (!_relay.IsPaired && !await PairAsync())
            {
                SetStatus("未运行", Theme.Muted);
                return;
            }

            var net = GogglesNet();
            if (!net.Ok)
            {
                throw new InvalidOperationException(
                    "没检测到眼镜的 USB 网络（192.168.42.x）。请检查：① USB 线是数据线且插好 ② 眼镜已开机并进入 Moonlight ③ “网络连接”里应出现 Remote NDIS 设备");
            }
            if (!string.IsNullOrEmpty(net.Dhcp) && net.Dhcp != "192.168.42.5")
            {
                throw new InvalidOperationException($"眼镜地址是 {net.Dhcp}，但转发器写死连 192.168.42.5");
            }

            if (!_relay.StartStream(out var error))
            {
                throw new InvalidOperationException(error);
            }

            await Task.Delay(1500);
            if (!_relay.IsStreaming)
            {
                throw new InvalidOperationException("转发器启动后立刻退出了");
            }

            if (!HasGogglesUsb())
            {
                AppendLog("没检测到 USB 网卡，确认眼镜已连接。");
            }

            _running = true;
            SetRunningUi(true);
            SetStatus("投屏中", Theme.Warn);
        }
        catch (Exception ex)
        {
            AppendLog("失败：" + ex.Message);
            SetStatus("未运行", Theme.Err);
            _relay.Stop();
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private async Task<bool> PairAsync()
    {
        var pin = Random.Shared.Next(1000, 10000).ToString();
        AppendLog("配对中（PIN " + pin + "）");

        var submitted = false;
        void OnPin()
        {
            if (submitted) return;
            submitted = _sunshine.SubmitPin(pin);
        }

        _sunshine.PinRequested += OnPin;
        try
        {
            return await _relay.PairAsync(pin, CancellationToken.None);
        }
        finally
        {
            _sunshine.PinRequested -= OnPin;
        }
    }

    private async Task ReconnectAsync()
    {
        if (_reconnecting) return;
        _reconnecting = true;
        _reconnectCts = new CancellationTokenSource();
        var ct = _reconnectCts.Token;

        SetStatus("重连中", Theme.Warn);
        AppendLog("串流中断，重置链路（Sunshine + 转发器）…");

        var succeeded = false;
        try
        {
            for (var attempt = 1; attempt <= 3 && !ct.IsCancellationRequested; attempt++)
            {
                _relay.Stop();
                _sunshine.Stop();
                await Task.Delay(1200, ct).ConfigureAwait(true);

                if (!GogglesNet().Ok)
                {
                    AppendLog($"第 {attempt} 次：没检测到眼镜的 USB 网络，等 2 秒…");
                    await Task.Delay(2000, ct).ConfigureAwait(true);
                    continue;
                }

                try
                {
                    _sunshine.Start();
                    if (!await _sunshine.WaitReadyAsync(20000))
                    {
                        AppendLog($"第 {attempt} 次：Sunshine 没起来");
                        continue;
                    }

                    if (!_relay.StartStream(out var err))
                    {
                        AppendLog($"第 {attempt} 次：转发器起不来（{err}）");
                        continue;
                    }

                    var until = DateTime.UtcNow.AddSeconds(10);
                    while (DateTime.UtcNow < until && !ct.IsCancellationRequested && !_relay.SawConnected)
                    {
                        await Task.Delay(200, ct).ConfigureAwait(true);
                    }

                    if (_relay.SawConnected)
                    {
                        succeeded = true;
                        break;
                    }

                    AppendLog($"第 {attempt} 次：眼镜没接受连接");
                }
                catch (Exception ex)
                {
                    AppendLog($"第 {attempt} 次失败：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {

        }
        finally
        {
            _reconnecting = false;
        }

        if (succeeded)
        {
            SetStatus("投屏中", Theme.Warn);
            AppendLog("已重新连上。");
            return;
        }

        if (!ct.IsCancellationRequested)
        {
            AppendLog("重置 3 次都没成功：请在眼镜上退出再进一次 Moonlight，然后点开始投屏。");
            StopInternal("重连失败");
        }
    }
    private void StopInternal(string reason)
    {
        try { _reconnectCts?.Cancel(); } catch { }
        _relay.Stop();
        _sunshine.Stop();
        _running = false;
        SetRunningUi(false);
        SetStatus("未运行", Theme.Muted);
        AppendLog("已停止（" + reason + "）");
    }

    private void WatchdogTick()
    {
        if (!_running || _reconnecting || _busy) return;
        if (!_relay.SawConnected) return;

        if (_relay.HasGogglesSocket())
        {
            _watchFails = 0;
            return;
        }

        if (++_watchFails >= 3)
        {
            _watchFails = 0;
            AppendLog("和眼镜的连接已经断了，自动重连…");
            _ = ReconnectAsync();
        }
    }

    private void SetRunningUi(bool running)
    {
        if (running) { _watchFails = 0; _watch.Start(); } else { _watch.Stop(); }
        _btnMain.Text = running ? "停止投屏" : "开始投屏";
        _btnMain.Fill = running ? Color.FromArgb(243, 244, 246) : Theme.Accent;
        _btnMain.FillHover = running ? Color.FromArgb(235, 237, 241) : Theme.AccentHover;
        _btnMain.FillDown = running ? Color.FromArgb(228, 232, 238) : Theme.AccentDown;
        _btnMain.TextColor = running ? Theme.Text : Color.White;
    }

    private void SetBusy(bool busy) => _btnMain.Enabled = !busy;

    private void SetStatus(string text, Color color)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
        _dot.DotColor = color;
        _dot.Invalidate();
    }

    private void AppendLog(string line)
    {
        _logLines.Add(line);
        try
        {
            File.AppendAllText(Path.Combine(_paths.DataDir, "app.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}", new System.Text.UTF8Encoding(false));
        }
        catch
        {

        }
        if (_logLines.Count > 60) _logLines.RemoveRange(0, 20);
        _txtLog.Text = string.Join(Environment.NewLine, _logLines);
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    private void OnUi(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }
        else
        {
            action();
        }
    }

    private static bool IsPortListening(int port)
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

    private sealed record GogglesNetInfo(bool Ok, string? LocalIp, string? Dhcp);

    private static GogglesNetInfo GogglesNet()
    {
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                var d = (nic.Description + " " + nic.Name).ToLowerInvariant();
                if (!(d.Contains("rndis") || d.Contains("remote ndis"))) continue;

                var props = nic.GetIPProperties();
                var ip = props.UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();

                string? dhcp = null;
                try
                {
                    var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + nic.Id);
                    dhcp = key?.GetValue("DhcpServer") as string;
                }
                catch
                {

                }

                return new GogglesNetInfo(true, ip, dhcp);
            }
        }
        catch
        {

        }
        return new GogglesNetInfo(false, null, null);
    }

    private static bool HasGogglesUsb()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Any(n =>
            {
                var d = (n.Description + " " + n.Name).ToLowerInvariant();
                return n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                       (d.Contains("rndis") || d.Contains("remote ndis"));
            });
        }
        catch
        {
            return false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;
        _relay.Stop();
        _sunshine.Stop();
    }
}
