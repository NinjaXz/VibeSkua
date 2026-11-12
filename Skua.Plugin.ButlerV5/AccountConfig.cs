using System.IO;
using Skua.Core.Models;
using Skua.Core.Models.Skills;

namespace ButlerV5;

/// <summary>Which CoreBots class the butler equips when it starts following.</summary>
public enum ButlerClassType { None, Farm, Solo, Dodge, Boss }

/// <summary>Where a butler goes when released / its master goes offline.</summary>
public enum ParkSpot { House, Whitemap, Stay }

/// <summary>
/// Per-ACCOUNT settings (the plugin options file is shared by every client on the PC,
/// so anything that must differ per account lives here instead), plus the lookup of
/// CoreBots' per-account class selections from its CBO storage files.
/// </summary>
public static class AccountConfig
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Skua",
        "butlerv5_options");

    private static string PathFor(string username) =>
        Path.Combine(Dir, $"{username.ToLowerInvariant()}.txt");

    public static ButlerClassType GetClassType(string username)
    {
        try
        {
            string path = PathFor(username);
            if (!File.Exists(path))
                return ButlerClassType.None;

            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("classtype=", StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse(line["classtype=".Length..].Trim(), ignoreCase: true, out ButlerClassType type))
                    return type;
            }
        }
        catch
        {
        }
        return ButlerClassType.None;
    }

    public static void SetClassType(string username, ButlerClassType type)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathFor(username), $"classtype={type}\n");
        }
        catch
        {
        }
    }

    /// <summary>CoreBots' "use the class already worn" class choice.</summary>
    public const string CboCurrentClass = "[Current]";

    /// <summary>
    /// The class name CoreBots has configured for this account, read from
    /// CBO_Storage({username}).txt ("FarmClassSelect: X"). May be <see cref="CboCurrentClass"/>.
    /// </summary>
    public static string? GetCboClassName(string username, ButlerClassType type) => type switch
    {
        ButlerClassType.Farm => GetCboValue(username, "FarmClassSelect:"),
        ButlerClassType.Solo => GetCboValue(username, "SoloClassSelect:"),
        ButlerClassType.Dodge => GetCboValue(username, "DodgeClassSelect:"),
        ButlerClassType.Boss => GetCboValue(username, "BossClassSelect:"),
        _ => null,
    };

    /// <summary>The class use mode CoreBots has configured ("Base", "Atk", ...), if any.</summary>
    public static string? GetCboClassMode(string username, ButlerClassType type) => type switch
    {
        ButlerClassType.Farm => GetCboValue(username, "FarmModeSelect:"),
        ButlerClassType.Solo => GetCboValue(username, "SoloModeSelect:"),
        ButlerClassType.Dodge => GetCboValue(username, "DodgeModeSelect:"),
        ButlerClassType.Boss => GetCboValue(username, "BossModeSelect:"),
        _ => null,
    };

    /// <summary>
    /// A skill set mode <paramref name="className"/> actually has: <paramref name="wanted"/> if it
    /// exists, else the mode named after the class type (Farm, Solo, Dodge), else Base, else the
    /// class's first mode. Skua's engine would run that first mode anyway when the asked-for one is
    /// missing; resolving it here lets the butler prefer a fitting mode and log the one it uses.
    /// A class with no skill sets at all keeps <paramref name="wanted"/> (the engine goes generic).
    /// </summary>
    public static ClassUseMode ResolveSkillMode(string className, ClassUseMode wanted, ButlerClassType type,
        IEnumerable<AdvancedSkill>? skillSets)
    {
        if (skillSets == null)
            return wanted;

        // Same class-name match as ScriptSkill.LoadAdvanced, in file order.
        List<ClassUseMode> modes = skillSets
            .Where(s => string.Equals(s.ClassName, className, StringComparison.CurrentCultureIgnoreCase))
            .Select(s => s.ClassUseMode)
            .ToList();
        if (modes.Count == 0 || modes.Contains(wanted))
            return wanted;

        ClassUseMode? typeMode = type switch
        {
            ButlerClassType.Farm => ClassUseMode.Farm,
            ButlerClassType.Solo => ClassUseMode.Solo,
            ButlerClassType.Dodge => ClassUseMode.Dodge,
            _ => null,
        };
        if (typeMode is ClassUseMode m && modes.Contains(m))
            return m;
        return modes.Contains(ClassUseMode.Base) ? ClassUseMode.Base : modes[0];
    }

    /// <summary>
    /// The account's CBO file the way VibeSkua's CoreBots options window loads it: from
    /// %APPDATA%\VibeSkua\options, else the legacy %APPDATA%\Skua\options copy.
    /// </summary>
    private static string? FindCboFile(string username)
    {
        string expected = $"CBO_Storage({username}).txt";
        foreach (string optionsDir in new[]
                 {
                     ClientFileSources.SkuaOptionsDIR,
                     Path.Combine(ClientFileSources.LegacySkuaDIR, "options"),
                 })
        {
            if (!Directory.Exists(optionsDir))
                continue;
            string? file = Directory.EnumerateFiles(optionsDir, "CBO_Storage(*).txt")
                .FirstOrDefault(f => Path.GetFileName(f).Equals(expected, StringComparison.OrdinalIgnoreCase));
            if (file != null)
                return file;
        }
        return null;
    }

    private static string? GetCboValue(string username, string key)
    {
        try
        {
            string? file = FindCboFile(username);
            if (file == null)
                return null;

            foreach (string line in File.ReadAllLines(file))
            {
                if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    string value = line[key.Length..].Trim();
                    return value.Length > 0 ? value : null;
                }
            }
        }
        catch
        {
        }
        return null;
    }
}
