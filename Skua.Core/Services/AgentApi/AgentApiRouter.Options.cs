using System.Text;
using System.Text.RegularExpressions;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Script options: the choices a script would show in its window.
///
/// A client-owned script keeps its choices in one text file per storage name
/// (ScriptOptionContainer writes options/{Storage}.cfg as "Name=Value" lines,
/// read back by OptionContainer.Load). An agent that cannot click a window
/// needs the same information as data: what a script asks, what it defaults to,
/// and what is stored now.
/// </summary>
public sealed partial class AgentApiRouter
{
    private static readonly Regex StoragePattern = new("OptionsStorage\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex CategoryPattern = new("MultipleOptions(?:\\.Add|\\[)\\s*(?:\\(\\s*)?\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>One script's choices: names, types, defaults and stored values.</summary>
    internal sealed record ScriptOptionRow(string Name, string Type, string Label, string Default, string Value, string Category);

    private object ReadScriptOptions(AgentApiRequest request)
    {
        string name = ValidateScriptName(QueryText(request, "name", string.Empty));
        string script = ResolveScriptPath(name);
        if (!File.Exists(script))
            throw new AgentApiException(404, "script_not_found");

        return BuildScriptOptions(script, name, null);
    }

    private object WriteScriptOptions(AgentApiRequest request)
    {
        ScriptOptionsRequest body = AgentApiJson.Read<ScriptOptionsRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string name = ValidateScriptName(body.Name);
        string script = ResolveScriptPath(name);
        if (!File.Exists(script))
            throw new AgentApiException(404, "script_not_found");

        Dictionary<string, string> supplied = body.Values ?? new Dictionary<string, string>();
        _ = supplied;
        if (supplied.Count == 0)
            throw new AgentApiException(400, "values is required");

        string source = ReadScriptSource(script);
        List<ScriptOptionRow> rows = ParseOptionRows(source);
        string storage = ReadStorage(source, name);

        // Only a name the script actually declares may be stored: a typo would sit in
        // the .cfg forever and be invisible until the wrong choice is blamed.
        Dictionary<string, string> cfg = ReadOptionFile(StorageFile(storage));
        foreach ((string key, string value) in supplied)
        {
            ScriptOptionRow row = rows.Find(r => key.Equals(r.Name, StringComparison.OrdinalIgnoreCase))
                ?? throw new AgentApiException(400, "unknown_option: " + key);
            if (value.Length > 120)
                throw new AgentApiException(400, "value for " + key + " must be 1-120 characters");

            string cfgKey = row.Category.Length == 0 ? row.Name : row.Category + ":" + row.Name;
            cfg[cfgKey] = value;
        }

        WriteOptionFile(StorageFile(storage), cfg);
        return BuildScriptOptions(script, name, storage, cfg, rows);
    }

    private object BuildScriptOptions(string script, string name, string? storage, Dictionary<string, string>? cfg = null, List<ScriptOptionRow>? rows = null)
    {
        string source = ReadScriptSource(script);
        rows ??= ParseOptionRows(source);
        storage ??= ReadStorage(source, name);
        string file = StorageFile(storage);
        bool exists = File.Exists(file);
        cfg ??= ReadOptionFile(file);

        List<ScriptOptionRow> answer = rows
            .Select(row => row with
            {
                Value = cfg.GetValueOrDefault(row.Category.Length == 0 ? row.Name : row.Category + ":" + row.Name, string.Empty),
            })
            .ToList();

        return new
        {
            name,
            storage,
            optionsFile = file,
            configured = exists,
            options = answer,
        };
    }

    private string StorageFile(string storage)
    {
        string safe = new(storage.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (safe.Length == 0)
            safe = "default";

        string file = Path.GetFullPath(Path.Combine(_optionsRoot, safe + ".cfg"));
        string root = _optionsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _optionsRoot
            : _optionsRoot + Path.DirectorySeparatorChar;
        if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new AgentApiException(400, "path_outside_options_root");

        return file;
    }

    private static string ReadScriptSource(string script)
    {
        try
        {
            return File.ReadAllText(script);
        }
        catch (IOException)
        {
            throw new AgentApiException(500, "script_unreadable");
        }
        catch (UnauthorizedAccessException)
        {
            throw new AgentApiException(500, "script_unreadable");
        }
    }

    private static string ReadStorage(string source, string name)
    {
        Match match = StoragePattern.Match(source);
        if (match.Success)
            return match.Groups[1].Value;

        // ScriptOptionContainer defaults to "default", and scripts that omit
        // OptionsStorage share one file. Mirror that, do not invent a per-file name.
        return "default";
    }

    /// <summary>
    /// Finds every "new Option&lt;T&gt;(...)" in the script: the questions it would ask.
    /// Positional scanning, not a regex, because a default value may itself contain
    /// brackets and the argument list may span lines.
    /// </summary>
    private static List<ScriptOptionRow> ParseOptionRows(string source)
    {
        List<ScriptOptionRow> rows = new();
        int at = 0;
        while ((at = source.IndexOf("new Option", at, StringComparison.Ordinal)) >= 0)
        {
            if (at + 10 >= source.Length || source[at + 10] != '<')
            {
                // "new OptionDictionary" and friends: not an option declaration.
                at += 10;
                continue;
            }

            int open = source.IndexOf('(', at + 11);
            if (open < 0)
                break;

            int close = MatchingParen(source, open);
            if (close < 0)
                break;

            string type = NormalizeGeneric(source[(at + 11)..(open - 1)]);
            List<string> args = SplitArguments(source[(open + 1)..close]);
            string optName = args.Count > 0 ? Unquote(args[0]) : string.Empty;
            if (optName.Length > 0)
            {
                rows.Add(new ScriptOptionRow(
                    optName,
                    type,
                    args.Count > 1 ? Unquote(args[1]) : optName,
                    args.Count > 3 ? args[3].Trim() : string.Empty,
                    string.Empty,
                    ReadCategory(source, at)));
            }

            at = close;
        }

        return rows;
    }

    /// <summary>Returns the index of the ")" that closes the "(" at open, or -1.</summary>
    private static int MatchingParen(string source, int open)
    {
        int depth = 0;
        bool inText = false;
        bool inChar = false;

        for (int i = open; i < source.Length; i++)
        {
            char c = source[i];
            bool escaped = i > 0 && source[i - 1] == '\\';

            if (c == '"' && !inChar && !escaped)
                inText = !inText;
            else if (c == '\\' && !inText && !escaped)
                inChar = !inChar;

            if (inText || inChar)
                continue;

            if (c == '(')
                depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>Restores the closing brackets a nested generic loses to the "(" that follows it.</summary>
    private static string NormalizeGeneric(string raw)
    {
        string value = raw.Trim();
        int opens = value.Count(c => c == '<');
        int closes = value.Count(c => c == '>');
        return closes < opens ? value + new string('>', opens - closes) : value;
    }

    private static string ReadCategory(string source, int index)
    {
        int start = source.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        int end = source.IndexOf('\n', index);
        string line = source[start..(end < 0 ? source.Length : end)];
        Match match = CategoryPattern.Match(line);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>Splits a constructor argument list on commas that sit outside quotes and brackets.</summary>
    private static List<string> SplitArguments(string text)
    {
        List<string> parts = new();
        StringBuilder current = new();
        int depth = 0;
        bool inText = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\'))
                inText = !inText;

            if (!inText)
            {
                if (c is '(' or '[' or '{' or '<')
                    depth++;
                else if (c is ')' or ']' or '}' or '>')
                    depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
            }

            current.Append(c);
        }

        if (current.ToString().Trim() is { Length: > 0 } last)
            parts.Add(last);

        return parts;
    }

    private static string Unquote(string raw)
    {
        string value = raw.Trim();
        if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
            value = value[1..^1];
        return value.Replace("\\\"", "\"");
    }

    private static Dictionary<string, string> ReadOptionFile(string file)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(file))
            return values;

        try
        {
            foreach (string line in File.ReadAllLines(file))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                // A choice may be grouped: "Category:Name=Value". Keep the whole
                // left side as the key; '=' is the only separator that matters here.
                int eq = trimmed.IndexOf('=');
                if (eq > 0)
                    values[trimmed[..eq].Trim()] = trimmed[(eq + 1)..].Trim();
            }
        }
        catch (IOException)
        {
            return values;
        }
        catch (UnauthorizedAccessException)
        {
            return values;
        }

        return values;
    }

    private static void WriteOptionFile(string file, Dictionary<string, string> values)
    {
        string? dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Same move-on-top write the script writer uses: a crash cannot leave a
        // half-written .cfg where the client would load it.
        string temp = file + ".tmp";
        File.WriteAllLines(temp, values.Select(kvp => kvp.Key + "=" + kvp.Value));
        File.Move(temp, file, overwrite: true);
    }

    private static string QueryText(AgentApiRequest request, string key, string fallback)
    {
        return request.Query.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private sealed record ScriptOptionsRequest
    {
        public string Name { get; init; } = string.Empty;
        public Dictionary<string, string>? Values { get; init; }
    }
}
