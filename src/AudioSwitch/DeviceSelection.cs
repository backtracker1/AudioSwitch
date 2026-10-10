namespace AudioSwitch;

internal enum DeviceKind { Jbl, Asus }
internal sealed record Endpoint(string Id, string Name, uint State);
internal static class DeviceSelection
{
    internal static bool Matches(string name, DeviceKind kind)
    {
        var n = name.ToUpperInvariant();
        if (kind == DeviceKind.Jbl)
            return n.Contains("JBL") && n.Contains("QUANTUM") && System.Text.RegularExpressions.Regex.IsMatch(n, @"810\b")
                && n.Contains("WIRELESS") && n.Contains("GAME") && !n.Contains("CHAT");
        return n.Contains("ASUS") && n.Contains("XONAR") && System.Text.RegularExpressions.Regex.IsMatch(n, @"\bD2\b")
            && !n.Contains("D2X") && !n.Contains("S/PDIF") && !n.Contains("SPDIF")
            && !n.Contains("CONVERTER") && !n.Contains("DIGITAL");
    }

    internal static Endpoint Select(IReadOnlyList<Endpoint> devices, string? currentId)
    {
        // IDs establish current identity; names classify only the intended hardware.
        var current = devices.FirstOrDefault(d => d.Id == currentId);
        var target = current is not null && Matches(current.Name, DeviceKind.Jbl)
            ? DeviceKind.Asus : DeviceKind.Jbl;
        var matching = devices.Where(d => Matches(d.Name, target)).ToArray();
        var active = matching.Where(d => d.State == 1).ToArray();
        if (active.Length == 1) return active[0];
        if (active.Length > 1)
            throw new InvalidOperationException($"Multiple active {target} endpoints match. Disconnect duplicate devices and retry.");
        throw new InvalidOperationException(matching.Length == 0
            ? $"The {target} playback device was not found. Connect it and check Windows Sound settings."
            : $"The {target} playback device is disconnected or disabled. Enable and connect it in Windows Sound settings.");
    }
}
