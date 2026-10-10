using AudioSwitch;

if (args.Contains("--com-smoke"))
{
    if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("COM smoke test requires Windows"); return 1; }
    try
    {
        using var audio = new CoreAudio();
        var devices = audio.Devices();
        _ = audio.DefaultId(Role.Console);
        CoreAudio.VerifyPolicyActivation();
        Console.WriteLine($"COM activation/enumeration passed ({devices.Count} endpoints); no defaults changed.");
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
Test("JBL to ASUS", () => Assert(DeviceSelection.Select(pair, jbl.Id) == asus));
Test("ASUS to JBL", () => Assert(DeviceSelection.Select(pair, asus.Id) == jbl));
Test("Unrelated default to JBL", () => Assert(DeviceSelection.Select(pair, "other") == jbl));
Test("No default to JBL", () => Assert(DeviceSelection.Select(pair, null) == jbl));
Test("Case insensitive names", () => Assert(DeviceSelection.Matches(jbl.Name.ToLowerInvariant(), DeviceKind.Jbl)));
Test("Chat rejected", () => Assert(!DeviceSelection.Matches("JBL Quantum810 Wireless Chat", DeviceKind.Jbl)));
Test("Other JBL rejected", () => Assert(!DeviceSelection.Matches("JBL Quantum910 Wireless Game", DeviceKind.Jbl)));
foreach (var invalid in new[] { "S/PDIF", "SPDIF", "Converter", "Digital" })
    Test("Reject ASUS " + invalid, () => Assert(!DeviceSelection.Matches(asus.Name + invalid, DeviceKind.Asus)));
Test("D20 rejected", () => Assert(!DeviceSelection.Matches("ASUS Xonar D20 Audio Device", DeviceKind.Asus)));
Test("Quantum8100 rejected", () => Assert(!DeviceSelection.Matches("JBL Quantum8100 Wireless Game", DeviceKind.Jbl)));
Test("D2X rejected", () => Assert(!DeviceSelection.Matches("ASUS Xonar D2X Audio Device", DeviceKind.Asus)));
Test("Missing target rejected", () => Throws(() => DeviceSelection.Select(new[] { jbl }, jbl.Id)));
foreach (uint state in new uint[] { 2, 4, 8 })
    Test("Unavailable state " + state, () => Throws(() => DeviceSelection.Select(new[] { jbl, asus with { State = state } }, jbl.Id)));
Test("Duplicate active target rejected", () => Throws(() => DeviceSelection.Select(new[] { jbl, asus, asus with { Id = "duplicate" } }, jbl.Id)));
Test("Inactive duplicate ignored", () => Assert(DeviceSelection.Select(new[] { jbl, asus, asus with { Id = "duplicate", State = 8 } }, jbl.Id) == asus));
Test("ID determines default identity", () => Assert(DeviceSelection.Select(pair, "old-jbl-id") == jbl));
Test("Chat default falls back to game", () => Assert(DeviceSelection.Select(pair.Append(new Endpoint("chat", "JBL Quantum810 Wireless Chat", 1)).ToArray(), "chat") == jbl));
Test("IMMDeviceCollection SDK IID", () => Assert(typeof(IDeviceCollection).GUID == new Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")));
Test("Enumerator CLSID/IID", () => Assert(CoreAudio.EnumeratorClass == new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E") && typeof(IDeviceEnumerator).GUID == new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")));
Test("Policy CLSID/IID", () => Assert(CoreAudio.PolicyClass == new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9") && typeof(IPolicyConfig).GUID == new Guid("F8679F50-850A-41CF-9C72-430F290290C8")));
Test("Policy SetDefaultEndpoint vtable position", () =>
{
    var methods = typeof(IPolicyConfig).GetMethods().OrderBy(m => m.MetadataToken).ToArray();
    Assert(methods.Length == 12 && methods[10].Name == "SetDefaultEndpoint");
    Assert(methods[10].ReturnType == typeof(int));
    Assert(methods[10].GetParameters()[0].GetCustomAttributes(typeof(System.Runtime.InteropServices.MarshalAsAttribute), false)
        .Cast<System.Runtime.InteropServices.MarshalAsAttribute>().Single().Value == System.Runtime.InteropServices.UnmanagedType.LPWStr);
});
Test("Operation diagnostics retain failing HRESULT", () =>
{
    try { Diagnostics.Operation<int>("test.QueryInterface", () => throw new System.Runtime.InteropServices.COMException("No interface", unchecked((int)0x80004002))); }
    catch (System.Runtime.InteropServices.COMException error)
    {
        Assert(error.HResult == unchecked((int)0x80004002) && error.Message.Contains("test.QueryInterface") && error.Message.Contains("80004002"));
        var log = File.ReadAllText(Diagnostics.LogPath);
        Assert(log.Contains("FAIL test.QueryInterface: HRESULT=0x80004002") && log.Contains("No interface"));
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
