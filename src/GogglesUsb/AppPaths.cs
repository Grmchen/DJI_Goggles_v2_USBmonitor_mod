namespace GogglesUsb;

internal sealed class AppPaths
{
    public string DataDir { get; }
    public bool IsPortable { get; }

    public AppPaths()
    {
        var custom = Environment.GetEnvironmentVariable("GOGGLESUSB_HOME");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            DataDir = Path.GetFullPath(custom!);
            Directory.CreateDirectory(DataDir);
            return;
        }

        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (IsWritable(exeDir))
        {
            DataDir = exeDir;
            IsPortable = true;
            return;
        }

        DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GogglesUsb");
        Directory.CreateDirectory(DataDir);
    }

    public string SunshineDir => Path.Combine(DataDir, "sunshine");
    public string ConfigDir => Path.Combine(SunshineDir, "config");
    public string CredentialsDir => Path.Combine(ConfigDir, "credentials");
    public string ConfFile => Path.Combine(ConfigDir, "sunshine.conf");
    public string AppsFile => Path.Combine(ConfigDir, "apps.json");
    public string StateFile => Path.Combine(ConfigDir, "sunshine_state.json");
    public string LogFile => Path.Combine(ConfigDir, "sunshine.log");

    public string RelayDir => Path.Combine(DataDir, "relay");
    public string KeysDir => Path.Combine(DataDir, "keys");

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".write_probe_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
