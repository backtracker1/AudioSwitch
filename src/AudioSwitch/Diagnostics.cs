using System.Runtime.InteropServices;

namespace AudioSwitch;

internal static class Diagnostics
{
    internal static string LogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioSwitch", "AudioSwitch.log");
    internal static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024)
                File.Move(LogPath, LogPath + ".previous", true);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [pid {Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch (Exception) { /* Logging failures must not replace the audio error. */ }
    }
    internal static T Operation<T>(string operation, Func<T> action)
    {
        Write("BEGIN " + operation);
        try { return action(); }
        catch (Exception error)
        {
            Write($"FAIL {operation}: HRESULT=0x{error.HResult:X8} {error}");
            throw new COMException($"{operation} failed (HRESULT 0x{error.HResult:X8}): {error.Message}", error.HResult);
        }
    }
}

internal static class ComActivation
{
    // Request the intended interface directly, so failed activation/QI reports
    // the native HRESULT and CLSID/IID, rather than an anonymous managed cast.
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);

    internal static T Create<T>(Guid clsid) where T : class
    {
        var iid = typeof(T).GUID;
        var operation = $"CoCreateInstance<{typeof(T).Name}> CLSID={clsid:B} IID={iid:B} CLSCTX_INPROC_SERVER";
        return Diagnostics.Operation(operation, () =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows COM is required.");
            var hr = CoCreateInstance(in clsid, IntPtr.Zero, 1, in iid, out var pointer);
            try
            {
                Diagnostics.Write($"{operation}: HRESULT=0x{hr:X8}");
                Marshal.ThrowExceptionForHR(hr);
                return Diagnostics.Operation($"Marshal.GetTypedObjectForIUnknown<{typeof(T).Name}> IID={iid:B}",
                    () => (T)Marshal.GetTypedObjectForIUnknown(pointer, typeof(T)));
            }
            finally { if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
        });
    }
}

internal sealed class ComApartment : IDisposable
{
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
    private bool ownsInitialization;
    internal ComApartment()
    {
        Diagnostics.Operation("CoInitializeEx(STA)", () =>
        {
            var hr = CoInitializeEx(IntPtr.Zero, 2);
            Diagnostics.Write($"CoInitializeEx(STA): HRESULT=0x{hr:X8}");
            // An already initialized MTA is usable. Never uninitialize a scope
            // we did not initialize. S_OK and S_FALSE both require balancing.
            if (hr == unchecked((int)0x80010106)) return hr;
            Marshal.ThrowExceptionForHR(hr);
            ownsInitialization = true;
            return hr;
        });
    }
    public void Dispose()
    {
        if (!ownsInitialization) return;
        ownsInitialization = false;
        CoUninitialize();
        Diagnostics.Write("CoUninitialize completed");
    }
}
