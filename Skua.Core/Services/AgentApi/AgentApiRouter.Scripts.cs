using System.Globalization;
using Skua.Core.Models;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Script visibility and control: the agent can read the script folder, see
/// what is running, write a script it generated, run it, and stop it.
///
/// Writes are confined to the folder the client loads scripts from. Without
/// that rule an agent holding a token could overwrite any C# file on disk.
/// </summary>
public sealed partial class AgentApiRouter
{
    /// <summary>
    /// One row of the script library. The path is relative to the scripts folder
    /// and is what the run, write and options routes take as "name".
    /// </summary>
    internal sealed record ScriptRow(
        string Path, string FileName, long Size, string ModifiedUtc, string Title, string Description, string Tags)
    {
        public bool Matches(string filter) =>
            Path.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Tags.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record ScriptHeader(string Title, string Description, string Tags)
    {
        /// <summary>Reads the name/description/tags block comment at the top of a script.</summary>
        public static ScriptHeader Read(string file)
        {
            string title = string.Empty, description = string.Empty, tags = string.Empty;
            bool inside = false;
            try
            {
                int lineNo = 0;
                foreach (string line in File.ReadLines(file))
                {
                    if (++lineNo > 40)
                        break;

                    // The library writes "/* name: X" then plain lines, one per key,
                    // then "*/". A line that is not a comment ends the header.
                    string text = line.Trim();
                    if (!inside)
                    {
                        if (text.StartsWith("/*"))
                        {
                            inside = true;
                            text = text[2..].Trim();
                        }
                        else if (text.StartsWith("//"))
                        {
                            text = text[2..].Trim();
                        }
                        else
                        {
                            if (text.Length > 0)
                                break;
                            continue;
                        }
                    }

                    if (text.EndsWith("*/"))
                    {
                        inside = false;
                        text = text[..^2];
                    }

                    string body = text.TrimStart('/', '*', ' ').Trim();
                    int colon = body.IndexOf(':');
                    if (colon <= 0)
                        continue;

                    string key = body[..colon].Trim().ToLowerInvariant();
                    string value = body[(colon + 1)..].Trim();
                    switch (key)
                    {
                        case "name":
                            title = value;
                            break;
                        case "description":
                            description = value;
                            break;
                        case "tags":
                            tags = value;
                            break;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return new ScriptHeader(title, description, tags);
        }
    }

    private object BuildScripts(AgentApiRequest request)
    {
        string filter = QueryText(request, "filter", string.Empty);
        int limit = Math.Clamp(QueryInt(request, "limit", 300), 1, 1000);
        int from = Math.Max(0, QueryInt(request, "from", 0));

        if (!Directory.Exists(_scriptsRoot))
            return new { root = _scriptsRoot, exists = false, scanned = 0, count = 0, from, returned = 0, scripts = new List<ScriptRow>() };

        // The shipped library is near 2000 files in folders, so the walk is recursive
        // and capped: an agent asks with a filter instead of pulling the whole list.
        List<ScriptRow> matched = new();
        int scanned = 0;
        foreach (string file in Directory.EnumerateFiles(_scriptsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (scanned >= _options.MaxScriptRows)
                break;

            scanned++;
            FileInfo info = new(file);
            ScriptHeader header = ScriptHeader.Read(file);
            ScriptRow row = new(
                Path.GetRelativePath(_scriptsRoot, file).Replace('\\', '/'),
                info.Name,
                info.Length,
                info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                header.Title,
                header.Description,
                header.Tags);
            if (filter.Length == 0 || row.Matches(filter))
                matched.Add(row);
        }

        matched.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        List<ScriptRow> page = matched.GetRange(from, Math.Min(limit, Math.Max(0, matched.Count - from)));

        return new { root = _scriptsRoot, exists = true, scanned, count = matched.Count, from, returned = page.Count, scripts = page };
    }

    private object BuildScriptStatus() => new
    {
        running = Safe(() => _scriptManager.ScriptRunning, false),
        paused = Safe(() => _scriptManager.ScriptPaused, false),
        loadedScript = Safe(() => Path.GetFileName(_scriptManager.LoadedScript), string.Empty),
        loadedPath = Safe(() => _scriptManager.LoadedScript, string.Empty),
        recentLogs = Safe(() => _logService.GetLogs(LogType.Script).TakeLast(20).ToList(), new List<string>()),
    };

    private async Task<object?> PostScriptsAsync(string path, AgentApiRequest request, CancellationToken ct) => path switch
    {
        "/scripts/write" => await WriteScriptAsync(request, ct).ConfigureAwait(false),
        "/scripts/run" => await RunScriptAsync(request, ct).ConfigureAwait(false),
        "/scripts/pause" or "/pause" => await SetPausedAsync(true, ct).ConfigureAwait(false),
        "/scripts/resume" or "/resume" => await SetPausedAsync(false, ct).ConfigureAwait(false),
        "/scripts/stop" or "/stop" => await StopScriptAsync(ct).ConfigureAwait(false),
        "/script/options" => WriteScriptOptions(request),
        _ => Known(GetRoutes, path)
            ? throw new AgentApiException(405, "method_not_allowed")
            : throw new AgentApiException(404, "not_found"),
    };

    private async Task<object?> WriteScriptAsync(AgentApiRequest request, CancellationToken ct)
    {
        ScriptWriteRequest body = AgentApiJson.Read<ScriptWriteRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string name = ValidateScriptName(body.Name);

        if (string.IsNullOrEmpty(body.Content))
            throw new AgentApiException(400, "content is required");
        if (System.Text.Encoding.UTF8.GetByteCount(body.Content) > _options.MaxScriptBytes)
            throw new AgentApiException(413, $"script exceeds {_options.MaxScriptBytes} bytes");

        string target = ResolveScriptPath(name);

        return await RunScriptActionAsync(async () =>
        {
            bool exists = File.Exists(target);
            if (exists && !body.Overwrite)
                throw new AgentApiException(409, "file_exists: set overwrite to true to replace it");

            // Write beside the target, then move: a crash mid-write cannot leave
            // a half-written script where the client would load it.
            string? parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            string temp = target + ".tmp";
            await File.WriteAllTextAsync(temp, body.Content, System.Text.Encoding.UTF8, ct).ConfigureAwait(false);
            if (body.Overwrite)
                File.Move(temp, target, overwrite: true);
            else
                File.Move(temp, target);

            FileInfo info = new(target);
            return (object)new
            {
                name,
                fileName = Path.GetFileName(target),
                path = target,
                size = info.Length,
                created = !exists,
                overwritten = exists,
            };
        }, ct).ConfigureAwait(false);
    }

    private async Task<object?> RunScriptAsync(AgentApiRequest request, CancellationToken ct)
    {
        ScriptRunRequest body = AgentApiJson.Read<ScriptRunRequest>(request.Body, request.BodyLength, _options.MaxBodyBytes);
        string name = ValidateScriptName(body.Name);
        string target = ResolveScriptPath(name);

        return await RunScriptActionAsync(async () =>
        {
            if (!File.Exists(target))
                throw new AgentApiException(404, "script_not_found");

            if (_scriptManager.ScriptRunning)
            {
                if (!body.Force)
                    throw new AgentApiException(409, "script_already_running: set force to true to replace it");
                await _scriptManager.StopScript(false).ConfigureAwait(false);
                _bot.Wait.ForTrue(() => !_scriptManager.ScriptRunning, 30);
            }

            _scriptManager.SetLoadedScript(target);

            // A script that declares options opens the client's options window, and an
            // agent cannot click it. The mobile remote control sets the same flag here
            // (RemoteControlService.StartScriptAsync), so the behaviour is consistent.
            bool wasSilent = _scriptManager.SilentConfig;
            _scriptManager.SilentConfig = body.Silent;
            Exception? startError;
            try
            {
                // StartScript returns the compile or launch exception instead of
                // throwing: an agent that just wrote a script needs the C# error.
                startError = await _scriptManager.StartScript().ConfigureAwait(false);
            }
            finally
            {
                _scriptManager.SilentConfig = wasSilent;
            }

            bool started = _bot.Wait.ForTrue(() => _scriptManager.ScriptRunning, 30);
            if (!started)
                throw new AgentApiException(500, startError is null
                    ? "script_failed_to_start"
                    : "script_failed_to_start: " + startError.Message);

            return (object)new
            {
                started = true,
                running = true,
                name,
                silent = body.Silent,
                path = target,
            };
        }, ct).ConfigureAwait(false);
    }

    private async Task<object?> SetPausedAsync(bool paused, CancellationToken ct)
    {
        return await RunScriptActionAsync(async () =>
        {
            if (!_scriptManager.ScriptRunning)
                throw new AgentApiException(409, "no_script_running");

            // The client owns the pause flag; going through it keeps the UI
            // button, the script's own checks and this endpoint in agreement.
            await _scriptManager.SetPausedAsync(paused).ConfigureAwait(false);
            return (object)new
            {
                paused,
                running = Safe(() => _scriptManager.ScriptRunning, false),
            };
        }, ct).ConfigureAwait(false);
    }

    private async Task<object?> StopScriptAsync(CancellationToken ct)
    {
        return await RunScriptActionAsync(async () =>
        {
            if (!_scriptManager.ScriptRunning)
                throw new AgentApiException(409, "no_script_running");

            await _scriptManager.StopScript(true).ConfigureAwait(false);
            bool stopped = _bot.Wait.ForTrue(() => !_scriptManager.ScriptRunning, 30);
            if (!stopped)
                await _scriptManager.StopScript(false).ConfigureAwait(false);

            return (object)new
            {
                stopped = Safe(() => !_scriptManager.ScriptRunning, true),
                forced = !stopped,
            };
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects any name that could leave the script folder: a path separator, a
    /// parent reference, a drive or UNC prefix, or a control character.
    /// </summary>
    internal static string ValidateScriptName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new AgentApiException(400, "name is required");

        // A name may be a relative path: the shipped library keeps scripts in folders
        // such as Army/Farm, and ResolveScriptPath refuses anything escaping the root.
        string name = raw.Trim().Replace('\\', '/');
        if (name.Length > 160)
            throw new AgentApiException(400, "name is too long (max 160)");
        if (name.Contains(':') || name.StartsWith('/') || name.EndsWith('/') || name.Any(char.IsControl))
            throw new AgentApiException(400, "name contains an invalid character");

        string[] segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new AgentApiException(400, "name is required");
        foreach (string segment in segments)
        {
            if (segment == "." || segment.Contains("..") || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new AgentApiException(400, "name contains an invalid character");
        }

        if (!segments[segments.Length - 1].EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            name += ".cs";

        return name;
    }

    private string ResolveScriptPath(string name)
    {
        string root = _scriptsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _scriptsRoot
            : _scriptsRoot + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(root, name));

        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new AgentApiException(400, "path_outside_scripts_root");

        return target;
    }
}
