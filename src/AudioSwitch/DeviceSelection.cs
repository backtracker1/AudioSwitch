namespace AudioSwitch;

internal enum DeviceKind { Jbl, Asus }
internal sealed record Endpoint(string Id, string Name, uint State, string? NameError = null);
internal static class DeviceSelection
{
    internal static bool Matches(string name, DeviceKind kind)
    {
        var n = name.ToUpperInvariant();
        if (kind == DeviceKind.Jbl)
            return n.Contains("JBL") && n.Contains("QUANTUM") && System.Text.RegularExpressions.Regex.IsMatch(n, @"810\b")
                && n.Contains("WIRELESS") && n.Contains("GAME") && !n.Contains("CHAT");
        return n.Contains("SPEAKER") && n.Contains("ASUS") && n.Contains("XONAR") && System.Text.RegularExpressions.Regex.IsMatch(n, @"\bD2\b")
            && !n.Contains("D2X") && !n.Contains("S/PDIF") && !n.Contains("SPDIF")
            && !n.Contains("CONVERTER") && !n.Contains("DIGITAL");
    }

    internal static Endpoint SelectTarget(IReadOnlyList<Endpoint> devices, DeviceKind kind)
    {
        var item = DeviceMenuState.Build(devices, kind, null);
        if (!item.Enabled || item.Target is null)
            throw new InvalidOperationException($"{kind}: {item.Status}. Reconnect/enable the device or inspect AudioSwitch.log.");
        return item.Target;
    }
}

internal sealed record DeviceMenuState(Endpoint? Target, bool Enabled, bool Checked, string Status)
{
    internal static IReadOnlyList<Endpoint> PreserveKnownInactiveNames(IReadOnlyList<Endpoint> latest, IReadOnlyList<Endpoint> previous)
    {
        return latest.Select(d =>
        {
            if (d.State is 0 or 1 || d.NameError is null || d.Name.Length != 0) return d;
            var known = previous.FirstOrDefault(p => p.Id == d.Id && p.Name.Length != 0);
            return known is null ? d : d with { Name = known.Name };
        }).ToArray();
    }

    internal static DeviceMenuState Build(IReadOnlyList<Endpoint> devices, DeviceKind kind, string? currentId)
    {
        var candidates = devices.Where(d => DeviceSelection.Matches(d.Name, kind)).ToArray();
        var active = candidates.Where(d => d.State == 1).ToArray();
        var selected = candidates.Any(d => d.Id == currentId);
        if (devices.Any(d => d.NameError is not null && (d.State == 0 || d.State == 1)))
            return new(null, false, selected, "enumeration error — see log");
        if (active.Length > 1) return new(null, false, selected, "multiple matching devices");
        if (active.Length == 1) return new(active[0], true, selected, "");
        if (candidates.Length == 0)
            return new(null, false, false, devices.Any(d => d.NameError is not null) ? "not found; some device names unavailable" : "not connected");
        var status = candidates.Any(d => (d.State & 2) != 0) ? "disabled" : "disconnected";
        return new(null, false, selected, status);
    }
}

internal static class EndpointReadErrors
{
    internal static string Describe(int hr) => hr switch
    {
        unchecked((int)0xE000020B) or unchecked((int)0x800F020B) => "device instance no longer exists (SetupAPI)",
        unchecked((int)0x80070490) => "endpoint/property not found",
        unchecked((int)0x80070005) => "access denied",
        _ => "endpoint metadata retrieval failed"
    };
}
