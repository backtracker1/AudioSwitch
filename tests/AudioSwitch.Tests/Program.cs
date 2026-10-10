using AudioSwitch;

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
int failures = 0;
foreach (var (name, action) in tests)
{
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed");
return failures == 0 ? 0 : 1;
