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
    private object BuildScripts()
    {
        if (!Directory.Exists(_scriptsRoot))
            return new { root = _scriptsRoot, exists = false, scripts = new List<object>() };

        List<object> scripts = Directory.EnumerateFiles(_scriptsRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(file =>
            {
                FileInfo info = new(file);
                return (object)new
                {
                    name = Path.GetFileName(file),
                    fileName = info.Name,
                    size = info.Length,
                    modifiedUtc = info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                };
            })
            .OrderBy(script => ((string)((dynamic)script).name), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new { root = _scriptsRoot, exists = true, scripts };
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
            string temp = target + ".tmp";
            await File.WriteAllTextAsync(temp, body.Content, System.Text.Encoding.UTF8, ct).ConfigureAwait(false);
            if (body.Overwrite)
                File.Move(temp, target, overwrite: true);
            else
                File.Move(temp, target);

            FileInfo info = new(target);
            return (object)new
            {
                name = Path.GetFileName(target),
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
            // StartScript returns the compile or launch exception instead of
            // throwing: an agent that just wrote a script needs the C# error.
            Exception? startError = await _scriptManager.StartScript().ConfigureAwait(false);

            bool started = _bot.Wait.ForTrue(() => _scriptManager.ScriptRunning, 30);
            if (!started)
                throw new AgentApiException(500, startError is null
                    ? "script_failed_to_start"
                    : "script_failed_to_start: " + startError.Message);

            return (object)new
            {
                started = true,
                running = true,
                name = Path.GetFileName(target),
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

        string name = raw.Trim();
        if (name.Length > 80)
            throw new AgentApiException(400, "name is too long (max 80)");
        if (name.Contains("..") || name.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || name.Any(char.IsControl))
            throw new AgentApiException(400, "name contains an invalid character");
        if (name.Contains('/') || name.Contains('\\') || name.Contains(':'))
            throw new AgentApiException(400, "name must not contain a path");

        if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
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
