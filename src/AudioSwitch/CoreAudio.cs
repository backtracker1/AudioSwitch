using System.Runtime.InteropServices;

namespace AudioSwitch;

internal enum Role { Console, Multimedia, Communications }
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey { public Guid Format; public uint Id; }
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public IntPtr Pointer;
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, Role role, out IDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-C0A3D7E6B5D7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr instance);
    [PreserveSig] int OpenPropertyStore(uint mode, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}
// Undocumented Windows policy interface. Preserve every preceding vtable slot.
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(IntPtr id, IntPtr format);
    [PreserveSig] int GetDeviceFormat(IntPtr id, int defaultFormat, IntPtr format);
    [PreserveSig] int ResetDeviceFormat(IntPtr id);
    [PreserveSig] int SetDeviceFormat(IntPtr id, IntPtr endpoint, IntPtr mix);
    [PreserveSig] int GetProcessingPeriod(IntPtr id, int defaultPeriod, IntPtr period, IntPtr minimum);
    [PreserveSig] int SetProcessingPeriod(IntPtr id, IntPtr period);
    [PreserveSig] int GetShareMode(IntPtr id, IntPtr mode);
    [PreserveSig] int SetShareMode(IntPtr id, IntPtr mode);
    [PreserveSig] int GetPropertyValue(IntPtr id, ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetPropertyValue(IntPtr id, ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
    [PreserveSig] int SetEndpointVisibility(IntPtr id, int visible);
}

internal sealed class CoreAudio : IDisposable
{
    private readonly IDeviceEnumerator enumerator = (IDeviceEnumerator)Activator.CreateInstance(
        Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
    private static readonly PropertyKey FriendlyName = new() { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    internal string? DefaultId(Role role)
    {
        var hr = enumerator.GetDefaultAudioEndpoint(0, role, out var device);
        try
        {
            if (hr == unchecked((int)0x80070490)) return null;
            Check(hr);
            Check(device.GetId(out var id));
            return id;
        }
        finally { Release(device); }
    }

    internal IReadOnlyList<Endpoint> Devices()
    {
        Check(enumerator.EnumAudioEndpoints(0, 15, out var collection));
        try
        {
            Check(collection.GetCount(out var count));
            var result = new List<Endpoint>();
            for (uint i = 0; i < count; i++)
            {
                Check(collection.Item(i, out var device));
                try
                {
                    Check(device.GetId(out var id));
                    Check(device.GetState(out var state));
                    Check(device.OpenPropertyStore(0, out var store));
                    try
                    {
                        var key = FriendlyName;
                        Check(store.GetValue(ref key, out var value));
                        try
                        {
                            var name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null;
                            result.Add(new Endpoint(id, name ?? "", state));
                        }
                        finally { Check(PropVariantClear(ref value)); }
                    }
                    finally { Release(store); }
                }
                finally { Release(device); }
            }
            return result;
        }
        finally { Release(collection); }
    }

    internal void Switch(string id)
    {
        var previous = new[] { DefaultId(Role.Console), DefaultId(Role.Multimedia) };
        var policy = (IPolicyConfig)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), true)!)!;
        try
        {
            Check(policy.SetDefaultEndpoint(id, Role.Console));
            Check(policy.SetDefaultEndpoint(id, Role.Multimedia));
            for (var i = 0; i < 20; i++)
            {
                if (DefaultId(Role.Console) == id && DefaultId(Role.Multimedia) == id) return;
                Thread.Sleep(50);
            }
            throw new InvalidOperationException("Windows did not confirm the selected playback device.");
        }
        catch (Exception error)
        {
            // Best effort restoration of only the roles this application modifies.
            var restored = true;
            for (var i = 0; i < previous.Length; i++)
                if (previous[i] is { } oldId)
                    restored &= policy.SetDefaultEndpoint(oldId, (Role)i) >= 0;
            throw new InvalidOperationException("Audio switching failed. " +
                (restored ? "Previous defaults were restored where available. " : "Check your default playback devices. ") + error.Message, error);
        }
        finally { Release(policy); }
    }
    public void Dispose() => Release(enumerator);
}
