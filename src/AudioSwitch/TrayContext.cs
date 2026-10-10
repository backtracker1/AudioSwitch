using System.Reflection;
using System.Runtime.InteropServices;

namespace AudioSwitch;

internal sealed class TrayContext : ApplicationContext
{
    private readonly ContextMenuStrip menu = new() { AutoClose = true, ShowCheckMargin = true, ShowImageMargin = false };
    private readonly ToolStripMenuItem jbl = new("🎧 JBL Quantum810 Wireless Game");
    private readonly ToolStripMenuItem asus = new("🔊 ASUS Xonar D2 Audio Device");
    private readonly ToolStripMenuItem exit = new("Exit");
    private readonly NotifyIcon tray;
    private readonly Icon headphones = LoadIcon("headphones.ico");
    private readonly Icon speakers = LoadIcon("speakers.ico");
    private readonly Icon neutral = LoadIcon("audio.ico");
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 15000 };
    private IReadOnlyList<Endpoint> devices = Array.Empty<Endpoint>();
    private string? current;
    private bool disposed;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    private static Icon LoadIcon(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AudioSwitch.Icons." + name)
            ?? throw new InvalidOperationException("Missing embedded tray icon: " + name);
        using var icon = new Icon(stream, 32, 32);
        return (Icon)icon.Clone();
    }

    internal TrayContext()
    {
        menu.Items.AddRange(new ToolStripItem[] { jbl, asus, new ToolStripSeparator(), exit });
        tray = new NotifyIcon { Icon = neutral, Text = "AudioSwitch", ContextMenuStrip = menu, Visible = true };
        // NotifyIcon handles right-click and TaskbarCreated (Explorer restarts).
        // Explicit left-click uses the very same persistent context menu.
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) menu.Show(Cursor.Position); };
        menu.Opening += (_, _) => RefreshDevices();
        menu.Opened += (_, _) => SetForegroundWindow(menu.Handle);
        jbl.Click += (_, _) => SwitchTo(DeviceKind.Jbl);
        asus.Click += (_, _) => SwitchTo(DeviceKind.Asus);
        exit.Click += (_, _) => ExitThread();
        refreshTimer.Tick += (_, _) => RefreshDefault();
        RefreshDevices();
        refreshTimer.Start();
    }

    private void RefreshDevices()
    {
        try
        {
            using var audio = new CoreAudio();
            devices = DeviceMenuState.PreserveKnownInactiveNames(audio.Devices(), devices);
            current = audio.DefaultId(Role.Console);
            ApplyState();
        }
        catch (Exception error)
        {
            Diagnostics.Write(error.ToString());
            jbl.Checked = asus.Checked = false;
            jbl.Enabled = asus.Enabled = false;
            jbl.Text = "🎧 JBL Quantum810 Wireless Game (enumeration error)";
            asus.Text = "🔊 ASUS Xonar D2 Audio Device (enumeration error)";
            jbl.ToolTipText = asus.ToolTipText = error.Message;
            tray.Icon = neutral;
            tray.Text = "AudioSwitch — enumeration error; see log";
        }
    }

    private void RefreshDefault()
    {
        if (menu.Visible) return; // Never auto-close or replace a menu the user is using.
        try
        {
            using var audio = new CoreAudio();
            var id = audio.DefaultId(Role.Console);
            if (id != current) { current = id; ApplyState(); }
        }
        catch (Exception error) { Diagnostics.Write(error.ToString()); }
    }

    private void ApplyState()
    {
        var jblState = DeviceMenuState.Build(devices, DeviceKind.Jbl, current);
        var asusState = DeviceMenuState.Build(devices, DeviceKind.Asus, current);
        ApplyItem(jbl, "🎧 JBL Quantum810 Wireless Game", jblState);
        ApplyItem(asus, "🔊 ASUS Xonar D2 Audio Device", asusState);
        tray.Icon = jblState.Checked ? headphones : asusState.Checked ? speakers : neutral;
        tray.Text = jblState.Checked ? "AudioSwitch — JBL headphones" : asusState.Checked ? "AudioSwitch — ASUS speakers" : "AudioSwitch — other/no default output";
        if (devices.Any(d => d.NameError is not null))
            jbl.ToolTipText = asus.ToolTipText = "Some endpoint properties could not be read. See " + Diagnostics.LogPath;
    }
    private static void ApplyItem(ToolStripMenuItem item, string name, DeviceMenuState state)
    {
        item.Text = name + (state.Status.Length == 0 ? "" : " (" + state.Status + ")");
        item.Checked = state.Checked;
        item.Enabled = state.Enabled;
        item.ToolTipText = state.Status;
    }

    private void SwitchTo(DeviceKind kind)
    {
        try
        {
            using var audio = new CoreAudio();
            var target = DeviceSelection.SelectTarget(audio.Devices(), kind);
            Diagnostics.Write($"Switch requested: {target.Name}; id={target.Id}");
            audio.Switch(target.Id);
        }
        catch (Exception error)
        {
            Diagnostics.Write(error.ToString());
            // Native tray error only; the application stays alive for another attempt.
            tray.ShowBalloonTip(8000, "AudioSwitch — unable to switch",
                error.Message + "\nLog: " + Diagnostics.LogPath, ToolTipIcon.Error);
        }
        finally { RefreshDevices(); }
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }
    private void DisposeResources()
    {
        if (disposed) return;
        disposed = true;
        refreshTimer.Stop();
        refreshTimer.Dispose();
        tray.Visible = false;
        tray.Dispose();
        menu.Dispose();
        headphones.Dispose();
        speakers.Dispose();
        neutral.Dispose();
        Diagnostics.Write("AudioSwitch tray exited");
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeResources();
        base.Dispose(disposing);
    }
}
