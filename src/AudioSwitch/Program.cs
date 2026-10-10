namespace AudioSwitch;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(false, @"Local\AudioSwitch.OneShot");
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return 0;
        try
        {
            using var audio = new CoreAudio();
            var target = DeviceSelection.Select(audio.Devices(), audio.DefaultId(Role.Console));
            audio.Switch(target.Id);
            ShowNotification("AudioSwitch", "Active output: " + target.Name, false);
            return 0;
        }
        catch (Exception error)
        {
            ShowNotification("AudioSwitch — unable to switch", error.Message, true);
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static void ShowNotification(string title, string message, bool error)
    {
        // A temporary non-activating popup; no notification-area icon or resident process.
        using var popup = new NotificationForm(title, message, error);
        Application.Run(popup);
    }
}

internal sealed class NotificationForm : Form
{
    private readonly System.Windows.Forms.Timer timer = new();
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; }
    }
    internal NotificationForm(string title, string message, bool error)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(380, error ? 165 : 115);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        var heading = new Label { Text = title, Location = new Point(18, 14), Size = new Size(344, 25), Font = new Font("Segoe UI", 11, FontStyle.Bold) };
        var text = new Label { Text = message, Location = new Point(18, 45), Size = new Size(344, error ? 102 : 57), Font = new Font("Segoe UI", 10) };
        Controls.AddRange(new Control[] { heading, text });
        Shown += (_, _) =>
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
            timer.Start();
        };
        timer.Interval = error ? 8000 : 2500;
        timer.Tick += (_, _) => Close();
        Click += (_, _) => Close();
        heading.Click += (_, _) => Close();
        text.Click += (_, _) => Close();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
