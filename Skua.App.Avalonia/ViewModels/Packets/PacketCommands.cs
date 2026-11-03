using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Skua.App.Avalonia.ViewModels.Packets;

public static class PacketCommands
{
    // Quotes inside a JSON string are escaped, so chat text can't match this
    private static readonly Regex _jsonCmd = new("\"cmd\":\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex _xmlAction = new("action='([^']+)'", RegexOptions.Compiled);

    // Every command a raw packet carries: a JSON packet lists its nested ones too, such as the auras in a ct
    public static string[] Parse(string packet)
    {
        if (packet.StartsWith('%'))
            return ParseString(packet);

        Regex? regex = packet.StartsWith('{') ? _jsonCmd : packet.StartsWith('<') ? _xmlAction : null;
        if (regex is null)
            return Array.Empty<string>();

        return regex.Matches(packet).Select(m => m.Groups[1].Value).Distinct().ToArray();
    }

    // Outbound %xt%zm%command%room%args%, inbound %xt%command%room%args%
    private static string[] ParseString(string packet)
    {
        string[] parts = packet.Split('%', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return Array.Empty<string>();
        if (parts[1] != "zm")
            return new[] { parts[1] };
        if (parts.Length < 3)
            return Array.Empty<string>();

        // "cmd" wraps a sub-command, such as %xt%zm%cmd%1%tfer%name%map%
        List<string> commands = new() { parts[2] };
        if (parts[2] == "cmd" && parts.Length >= 5)
            commands.Add(parts[4]);
        return commands.ToArray();
    }
}
