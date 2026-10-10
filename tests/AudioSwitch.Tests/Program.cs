using AudioSwitch;
using System.Runtime.InteropServices;

if (args.Contains("--com-smoke"))
{
    if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("COM smoke test requires Windows"); return 1; }
    try
    {
        CoreAudio.VerifyPropertyMarshalling();
        Console.WriteLine("Native IPropertyStore PROPERTYKEY/PROPVARIANT Unicode round trip passed.");
        using var audio = new CoreAudio();
        var devices = audio.Devices();
        foreach (var device in devices)
        {
            Console.WriteLine($"Endpoint id={device.Id}; state=0x{device.State:X}; name={device.Name}; error={device.NameError}");
            if (device.NameError is not null && (device.State == 0 || device.State == 1))
                throw new InvalidOperationException("An active/unknown-state endpoint's friendly name could not be retrieved: " + device.NameError);
            if (device.NameError is null && string.IsNullOrWhiteSpace(device.Name))
                throw new InvalidOperationException("Enumeration returned an empty friendly name without an error.");
        }
        _ = audio.DefaultId(Role.Console);
        CoreAudio.VerifyPolicyActivation();
        var names = devices.Count(d => d.NameError is null);
        Console.WriteLine($"COM enumeration passed: {devices.Count} endpoints, {names} friendly names read; no defaults changed.");
        if (devices.Count == 0) Console.WriteLine("No runner audio endpoints: real endpoint retrieval unexercised; native memory property-store retrieval was exercised.");
        return 0;
    }
    catch (Exception error) { Console.Error.WriteLine(error); Console.Error.WriteLine(Diagnostics.LogPath); return 1; }
}

var diagnosticsDirectory = Path.Combine(Path.GetTempPath(), "AudioSwitch-tests-" + Guid.NewGuid());
Diagnostics.LogPath = Path.Combine(diagnosticsDirectory, "test.log");
var tests = new List<(string, Action)>();
void Test(string name, Action action) => tests.Add((name, action));
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
var jbl = new Endpoint("jbl-id", "Headphones (JBL Quantum810 Wireless Game)", 1);
var asus = new Endpoint("asus-id", "Speakers (ASUS Xonar D2 Audio Device)", 1);
var pair = new[] { jbl, asus };
Test("Explicit JBL selection", () => Assert(DeviceSelection.SelectTarget(pair, DeviceKind.Jbl) == jbl));
Test("Explicit ASUS selection", () => Assert(DeviceSelection.SelectTarget(pair, DeviceKind.Asus) == asus));
Test("Case insensitive JBL names", () => Assert(DeviceSelection.Matches(jbl.Name.ToLowerInvariant(), DeviceKind.Jbl)));
Test("Case insensitive ASUS names", () => Assert(DeviceSelection.Matches(asus.Name.ToLowerInvariant(), DeviceKind.Asus)));
foreach (var invalid in new[] { "JBL Quantum810 Wireless Chat", "JBL Quantum910 Wireless Game", "JBL Quantum8100 Wireless Game" })
    Test("Reject " + invalid, () => Assert(!DeviceSelection.Matches(invalid, DeviceKind.Jbl)));
foreach (var invalid in new[] { "S/PDIF", "SPDIF", "Converter", "Digital" })
    Test("Reject ASUS " + invalid, () => Assert(!DeviceSelection.Matches(asus.Name + invalid, DeviceKind.Asus)));
foreach (var invalid in new[] { "D2X", "D20" })
    Test("Reject ASUS " + invalid, () => Assert(!DeviceSelection.Matches("ASUS Xonar " + invalid + " Audio Device", DeviceKind.Asus)));
Test("ASUS headphones are not speakers", () => Assert(!DeviceSelection.Matches("Headphones (ASUS Xonar D2 Audio Device)", DeviceKind.Asus)));
Test("Cached disconnected names preserve status", () =>
{
    var latest = DeviceMenuState.PreserveKnownInactiveNames(new[] { jbl with { Name = "", State = 8, NameError = "0xE000020B" }, asus }, pair);
    var item = DeviceMenuState.Build(latest, DeviceKind.Jbl, asus.Id);
    Assert(!item.Enabled && item.Status == "disconnected");
    Assert(DeviceMenuState.Build(latest, DeviceKind.Asus, asus.Id).Enabled);
});
Test("Never use cached name for unreadable active device", () =>
{
    var latest = DeviceMenuState.PreserveKnownInactiveNames(new[] { jbl with { Name = "", State = 1, NameError = "0xE000020B" } }, pair);
    Assert(latest[0].Name.Length == 0 && !DeviceMenuState.Build(latest, DeviceKind.Jbl, jbl.Id).Enabled);
});
Test("Missing target rejected", () => Throws(() => DeviceSelection.SelectTarget(new[] { jbl }, DeviceKind.Asus)));
foreach (uint state in new uint[] { 2, 4, 8 })
    Test("Unavailable state " + state, () => Throws(() => DeviceSelection.SelectTarget(new[] { jbl, asus with { State = state } }, DeviceKind.Asus)));
Test("Duplicate active target rejected", () => Throws(() => DeviceSelection.SelectTarget(new[] { jbl, asus, asus with { Id = "duplicate" } }, DeviceKind.Asus)));
Test("Inactive duplicate ignored", () => Assert(DeviceSelection.SelectTarget(new[] { jbl, asus, asus with { Id = "duplicate", State = 8 } }, DeviceKind.Asus) == asus));
Test("Menu JBL checkmark", () => Assert(DeviceMenuState.Build(pair, DeviceKind.Jbl, jbl.Id).Checked && !DeviceMenuState.Build(pair, DeviceKind.Asus, jbl.Id).Checked));
Test("External change refreshes checkmark", () => Assert(!DeviceMenuState.Build(pair, DeviceKind.Jbl, asus.Id).Checked && DeviceMenuState.Build(pair, DeviceKind.Asus, asus.Id).Checked));
Test("Other default leaves both unchecked", () => Assert(!DeviceMenuState.Build(pair, DeviceKind.Jbl, "other").Checked && !DeviceMenuState.Build(pair, DeviceKind.Asus, "other").Checked));
Test("No default leaves unchecked", () => Assert(!DeviceMenuState.Build(pair, DeviceKind.Jbl, null).Checked));
Test("Missing device disabled in menu", () => Assert(!DeviceMenuState.Build(new[] { jbl }, DeviceKind.Asus, jbl.Id).Enabled));
Test("Disabled device status", () => Assert(DeviceMenuState.Build(new[] { asus with { State = 2 } }, DeviceKind.Asus, null).Status == "disabled"));
Test("Unplugged device status", () => Assert(DeviceMenuState.Build(new[] { asus with { State = 8 } }, DeviceKind.Asus, null).Status == "disconnected"));
Test("Stale inactive metadata does not abort valid devices", () => Assert(DeviceMenuState.Build(pair.Append(new Endpoint("ghost", "", 8, "0xE000020B")).ToArray(), DeviceKind.Jbl, jbl.Id).Enabled));
Test("Unreadable active endpoint blocks unsafe selection", () => Assert(!DeviceMenuState.Build(pair.Append(new Endpoint("bad", "", 1, "0xE000020B")).ToArray(), DeviceKind.Jbl, jbl.Id).Enabled));
Test("Unreadable unknown state blocks unsafe selection", () => Assert(!DeviceMenuState.Build(pair.Append(new Endpoint("bad", "", 0, "0xE000020B")).ToArray(), DeviceKind.Asus, jbl.Id).Enabled));
Test("Unavailable metadata is explicit in menu status", () => Assert(DeviceMenuState.Build(new[] { new Endpoint("ghost", "", 8, "0xE000020B") }, DeviceKind.Jbl, null).Status.Contains("names unavailable")));
Test("Raw SetupAPI missing instance recognized", () => Assert(EndpointReadErrors.Describe(unchecked((int)0xE000020B)).Contains("no longer exists")));
Test("HRESULT SetupAPI missing instance recognized", () => Assert(EndpointReadErrors.Describe(unchecked((int)0x800F020B)).Contains("no longer exists")));
Test("Access errors remain distinct", () => Assert(EndpointReadErrors.Describe(unchecked((int)0x80070005)) == "access denied"));
Test("PROPERTYKEY native x64 size/offset/key", () => Assert(Marshal.SizeOf<PropertyKey>() == 20 && Marshal.OffsetOf<PropertyKey>(nameof(PropertyKey.Id)).ToInt32() == 16 && CoreAudio.FriendlyName.Format == new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0") && CoreAudio.FriendlyName.Id == 14));
Test("PROPVARIANT native x64 size/offset", () => Assert(Marshal.SizeOf<PropVariant>() == 24 && Marshal.OffsetOf<PropVariant>(nameof(PropVariant.Pointer)).ToInt32() == 8));
Test("Property store IID and GetValue by-reference marshaling", () =>
{
    Assert(typeof(IPropertyStore).GUID == new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"));
    var method = typeof(IPropertyStore).GetMethod("GetValue")!;
    Assert(method.GetParameters()[0].ParameterType == typeof(PropertyKey).MakeByRefType());
    Assert(method.GetParameters()[1].IsOut && method.GetParameters()[1].ParameterType == typeof(PropVariant).MakeByRefType());
});
Test("IMMDeviceCollection SDK IID", () => Assert(typeof(IDeviceCollection).GUID == new Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")));
Test("Enumerator CLSID/IID", () => Assert(CoreAudio.EnumeratorClass == new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E") && typeof(IDeviceEnumerator).GUID == new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")));
Test("Policy CLSID/IID", () => Assert(CoreAudio.PolicyClass == new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9") && typeof(IPolicyConfig).GUID == new Guid("F8679F50-850A-41CF-9C72-430F290290C8")));
Test("Policy SetDefaultEndpoint vtable position", () =>
{
    var methods = typeof(IPolicyConfig).GetMethods().OrderBy(m => m.MetadataToken).ToArray();
    Assert(methods.Length == 12 && methods[10].Name == "SetDefaultEndpoint" && methods[10].ReturnType == typeof(int));
    Assert(methods[10].GetParameters()[0].GetCustomAttributes(typeof(MarshalAsAttribute), false)
        .Cast<MarshalAsAttribute>().Single().Value == UnmanagedType.LPWStr);
});
Test("Operation diagnostics retain property HRESULT", () =>
{
    try { Diagnostics.Operation<int>("IPropertyStore.GetValue endpoint=ghost", () => throw new COMException("No device instance", unchecked((int)0xE000020B))); }
    catch (COMException error)
    {
        Assert(error.HResult == unchecked((int)0xE000020B) && error.Message.Contains("IPropertyStore.GetValue"));
        var log = File.ReadAllText(Diagnostics.LogPath);
        Assert(log.Contains("endpoint=ghost: HRESULT=0xE000020B") && log.Contains("No device instance"));
        return;
    }
    throw new Exception("Expected COM error");
});
int failures = 0;
foreach (var (name, action) in tests)
{
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed");
Directory.Delete(diagnosticsDirectory, true);
return failures == 0 ? 0 : 1;
