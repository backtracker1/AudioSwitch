using System.Runtime.InteropServices;

namespace AudioSwitch;

internal enum Role { Console, Multimedia, Communications }
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey { public Guid Format; public uint Id; }
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(2)] public ushort Reserved1;
    [FieldOffset(4)] public ushort Reserved2;
    [FieldOffset(6)] public ushort Reserved3;
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
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
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
    // Slots 3–12 are deliberately uncallable placeholders: these undocumented
    // methods have variant signatures across Windows versions. Only slot 13
    // (SetDefaultEndpoint) is invoked. IUnknown occupies slots 0–2.
    void Reserved1();
    void Reserved2();
    void Reserved3();
    void Reserved4();
    void Reserved5();
    void Reserved6();
    void Reserved7();
    void Reserved8();
    void Reserved9();
    void Reserved10();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
    void ReservedVisibility();
}

internal sealed class CoreAudio : IDisposable
{
    internal static readonly Guid EnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    internal static readonly Guid PolicyClass = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    private readonly IDeviceEnumerator enumerator;
    private readonly ComApartment apartment;
    internal CoreAudio()
    {
        apartment = new ComApartment();
        try { enumerator = ComActivation.Create<IDeviceEnumerator>(EnumeratorClass); }
        catch { apartment.Dispose(); throw; }
    }
    internal static void VerifyPolicyActivation()
    {
        var policy = ComActivation.Create<IPolicyConfig>(PolicyClass);
        Release(policy);
    }
    internal static readonly PropertyKey FriendlyName = new() { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    private static int Call(string operation, Func<int> action) => Diagnostics.Operation(operation, () =>
    {
        var hr = action();
        Diagnostics.Write($"{operation}: HRESULT=0x{hr:X8}");
        Marshal.ThrowExceptionForHR(hr);
        return hr;
    });
    private static void Release(object? value) { if (OperatingSystem.IsWindows() && value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    internal string? DefaultId(Role role)
    {
        IDevice device = null!;
        var hr = Diagnostics.Operation($"IMMDeviceEnumerator.GetDefaultAudioEndpoint({role})", () =>
        {
            var result = enumerator.GetDefaultAudioEndpoint(0, role, out device);
            Diagnostics.Write($"GetDefaultAudioEndpoint({role}): HRESULT=0x{result:X8}");
            if (result != unchecked((int)0x80070490)) Marshal.ThrowExceptionForHR(result);
            return result;
        });
        try
        {
            if (hr == unchecked((int)0x80070490)) return null;
            string id = "";
            Call("IMMDevice.GetId", () => device.GetId(out id));
            return id;
        }
        finally { Release(device); }
    }

    internal IReadOnlyList<Endpoint> Devices()
    {
        IDeviceCollection collection = null!;
        Call("IMMDeviceEnumerator.EnumAudioEndpoints(render, all states)", () => enumerator.EnumAudioEndpoints(0, 15, out collection));
        try
        {
            uint count = 0;
            Call("IMMDeviceCollection.GetCount", () => collection.GetCount(out count));
            var result = new List<Endpoint>();
            for (uint i = 0; i < count; i++)
            {
                IDevice device = null!;
                string id = $"unreadable-index-{i}";
                uint state = 0;
                try
                {
                    Call($"IMMDeviceCollection.Item({i})", () => collection.Item(i, out device));
                    Call("IMMDevice.GetId", () => device.GetId(out id));
                    Call("IMMDevice.GetState", () => device.GetState(out state));
                    var name = ReadFriendlyName(device, id, state);
                    result.Add(new Endpoint(id, name, state));
                }
                catch (Exception error)
                {
                    // All-state enumeration includes removed device instances. Keep
                    // explicit diagnostics/status instead of aborting valid endpoints.
                    // Unreadable active/unknown-state entries block safe selection.
                    Diagnostics.Write($"ENDPOINT ERROR id={id}; state=0x{state:X}; HRESULT=0x{error.HResult:X8}; reason={EndpointReadErrors.Describe(error.HResult)}; {error}");
                    result.Add(new Endpoint(id, "", state, $"0x{error.HResult:X8}: {error.Message}"));
                }
                finally { Release(device); }
            }
            return result;
        }
        finally { Release(collection); }
    }

    private static string ReadFriendlyName(IDevice device, string id, uint state)
    {
        IPropertyStore store = null!;
        try
        {
            Call($"IMMDevice.OpenPropertyStore id={id}; state=0x{state:X}", () => device.OpenPropertyStore(0, out store));
            return ReadName(store, $"endpoint id={id}; state=0x{state:X}");
        }
        finally { Release(store); }
    }

    private static string ReadName(IPropertyStore store, string context)
    {
        var key = FriendlyName;
        PropVariant value = default;
        try
        {
            Call($"IPropertyStore.GetValue(PKEY_Device_FriendlyName {key.Format:B}/{key.Id}) {context}",
                () => store.GetValue(ref key, out value));
            Diagnostics.Write($"Friendly-name PROPVARIANT vt={value.Type}; {context}");
            if (value.Type != 31 || value.Pointer == IntPtr.Zero)
                throw new InvalidOperationException($"Friendly name is not VT_LPWSTR (vt={value.Type}); {context}");
            var name = Marshal.PtrToStringUni(value.Pointer);
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException($"Empty friendly name; {context}");
            return name;
        }
        finally
        {
            // GetValue can initialize a variant even on failure. Always clear it.
            var hr = PropVariantClear(ref value);
            if (hr < 0) Diagnostics.Write($"PropVariantClear: HRESULT=0x{hr:X8}; {context}");
        }
    }

    [DllImport("propsys.dll", ExactSpelling = true)]
    private static extern int PSCreateMemoryPropertyStore(in Guid iid, out IPropertyStore store);
    internal static void VerifyPropertyMarshalling()
    {
        IPropertyStore store = null!;
        var iid = typeof(IPropertyStore).GUID;
        using var apartment = new ComApartment();
        try
        {
            Call("PSCreateMemoryPropertyStore", () => PSCreateMemoryPropertyStore(in iid, out store));
            var key = FriendlyName;
            const string expected = "Speakers (ASUS Xonar D2 Audio Device) — native Unicode test";
            var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(expected) };
            try { Call("IPropertyStore.SetValue(native property marshalling test)", () => store.SetValue(ref key, ref value)); }
            finally { PropVariantClear(ref value); }
            if (ReadName(store, "native property marshalling test") != expected)
                throw new InvalidOperationException("PROPERTYKEY/PROPVARIANT native round trip failed.");
        }
        finally { Release(store); }
    }

    internal void Switch(string id)
    {
        // Re-open the selected ID just before switching; refuse stale/inactive IDs.
        IDevice target = null!;
        try
        {
            Call("IMMDeviceEnumerator.GetDevice(selected ID)", () => enumerator.GetDevice(id, out target));
            uint state = 0;
            Call("IMMDevice.GetState(selected ID)", () => target.GetState(out state));
            if (state != 1) throw new InvalidOperationException("The selected device is no longer active. Reopen the menu and reconnect it.");
        }
        finally { Release(target); }
        var previous = new[] { DefaultId(Role.Console), DefaultId(Role.Multimedia) };
        var policy = ComActivation.Create<IPolicyConfig>(PolicyClass);
        try
        {
            Call("IPolicyConfig.SetDefaultEndpoint(Console)", () => policy.SetDefaultEndpoint(id, Role.Console));
            Call("IPolicyConfig.SetDefaultEndpoint(Multimedia)", () => policy.SetDefaultEndpoint(id, Role.Multimedia));
            for (var i = 0; i < 20; i++)
            {
                if (DefaultId(Role.Console) == id && DefaultId(Role.Multimedia) == id)
                {
                    Diagnostics.Write("Verified Console and Multimedia defaults: " + id);
                    return;
                }
                Thread.Sleep(50);
            }
            Diagnostics.Write("Default endpoint verification timed out");
            throw new InvalidOperationException("Windows did not confirm the selected playback device.");
        }
        catch (Exception error)
        {
            // Best effort restoration of only the roles this application modifies.
            var restored = true;
            for (var i = 0; i < previous.Length; i++)
                if (previous[i] is { } oldId)
                {
                    try { Call($"IPolicyConfig.SetDefaultEndpoint(rollback {(Role)i})", () => policy.SetDefaultEndpoint(oldId, (Role)i)); }
                    catch (Exception rollbackError) { Diagnostics.Write(rollbackError.ToString()); restored = false; }
                }
            throw new InvalidOperationException("Audio switching failed. " +
                (restored ? "Previous defaults were restored where available. " : "Check your default playback devices. ") + error.Message, error);
        }
        finally { Release(policy); }
    }
    public void Dispose()
    {
        try { Release(enumerator); }
        finally { apartment.Dispose(); }
    }
}
