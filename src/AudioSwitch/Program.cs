namespace AudioSwitch;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(false, @"Local\AudioSwitch.Tray");
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return 0;
        try
        {
            Diagnostics.Write($"AudioSwitch tray started; OS={Environment.OSVersion}; x64={Environment.Is64BitProcess}");
            using var context = new TrayContext();
            Application.Run(context);
            return 0;
        }
        catch (Exception error)
        {
            Diagnostics.Write(error.ToString());
            MessageBox.Show("AudioSwitch could not start.\n" + error.Message + "\nLog: " + Diagnostics.LogPath,
                "AudioSwitch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
