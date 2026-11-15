using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GogglesUsb;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--selftest")
            {
                return SelfTest.Run();
            }
            using var mutex = new Mutex(true, @"Global\GogglesUsb.SingleInstance", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("GogglesUsb 已经在运行了。", "GogglesUsb", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                var dir = AppContext.BaseDirectory;
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}", new UTF8Encoding(false));
            }
            catch
            {

            }
            return 1;
        }
    }
}

internal static class SelfTest
{
    public static int Run()
    {
        var report = new StringBuilder();
        var ok = true;
        try
        {
            var paths = new AppPaths();
            var sunshine = new SunshineHost(paths);
            var relay = new RelayHost(paths);

            report.AppendLine($"GogglesUsb selftest  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine("数据目录: " + paths.DataDir + (paths.IsPortable ? "（便携）" : "（非便携）"));
            report.AppendLine("Sunshine: " + (sunshine.ExePath ?? "<缺失>"));
            report.AppendLine("转发器  : " + (relay.ExePath ?? "<缺失>"));
            report.AppendLine("已配对  : " + relay.IsPaired);

            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                var d = (nic.Description + " " + nic.Name).ToLowerInvariant();
                if (!(d.Contains("rndis") || d.Contains("remote ndis"))) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        report.AppendLine($"USB 网卡 {nic.Name}: {ua.Address}");
                    }
                }
            }

            report.AppendLine(ok ? "RESULT: OK" : "RESULT: FAIL");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            report.AppendLine("RESULT: FAIL " + ex);
            return 1;
        }
        finally
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest.txt"), report.ToString(), new UTF8Encoding(false));
            }
            catch
            {

            }
        }
    }
}
