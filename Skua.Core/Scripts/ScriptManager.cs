using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Messaging;
using Skua.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Skua.Core.Scripts;

public partial class ScriptManager : ObservableObject, IScriptManager, IDisposable
{
    private static readonly Regex _versionRegex = new(@"^/\*[\s\S]*?version:\s*(\d+\.\d+\.\d+\.\d+)[\s\S]*?\*/", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex _accessibilityRegex = new(@"\b(public\s+|internal\s+|private\s+)?class\s+\w+", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly string _cacheScriptsDir = Path.Combine(ClientFileSources.SkuaScriptsDIR, "Cached-Scripts");
    public ScriptManager(
        ILogService logger,
        Lazy<IScriptInterface> scriptInterface,
        Lazy<IScriptHandlers> handlers,
        Lazy<IScriptSkill> skills,
        Lazy<IScriptDrop> drops,
        Lazy<IScriptWait> wait,
        Lazy<IAuraMonitorService> auraMonitorService,
        IDiagnosticsService diagnostics,
        IScriptPathResolver pathResolver)
    {
        _lazyBot = scriptInterface;
        _lazyHandlers = handlers;
        _lazySkills = skills;
        _lazyDrops = drops;
        _lazyWait = wait;
        _lazyAuraMonitor = auraMonitorService;
        _logger = logger;
        _diagnostics = diagnostics;
        _pathResolver = pathResolver;
    }

    private readonly Lazy<IScriptInterface> _lazyBot;
    private readonly Lazy<IScriptHandlers> _lazyHandlers;
    private readonly Lazy<IScriptSkill> _lazySkills;
    private readonly Lazy<IScriptDrop> _lazyDrops;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IAuraMonitorService> _lazyAuraMonitor;
    private readonly ILogService _logger;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IScriptPathResolver _pathResolver;

    private IScriptHandlers Handlers => _lazyHandlers.Value;
    private IScriptSkill Skills => _lazySkills.Value;
    private IScriptDrop Drops => _lazyDrops.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IAuraMonitorService AuraMonitor => _lazyAuraMonitor.Value;

    private Thread? _currentScriptThread;
    private readonly object _threadLock = new();
    private readonly object _stateLock = new();
    private bool _stoppedByScript;
    private bool _runScriptStoppingBool;
    private readonly object _configuredLock = new();
    private readonly Dictionary<string, bool> _configured = new();
    private readonly object _refCacheLock = new();
    private readonly List<string> _refCache = new();
    private readonly ReaderWriterLockSlim _includedFilesLock = new();
    private readonly List<string> _includedFiles = new();
    private ScriptLoadContext? _currentLoadContext;
    private string? _scriptRunId;

    [ObservableProperty]
    private bool _scriptRunning = false;

    [ObservableProperty]
    private bool _scriptPaused = false;

    public ManualResetEventSlim ScriptPauseEvent { get; } = new(true);

    partial void OnScriptPausedChanged(bool value)
    {
        if (value)
        {
            Skills.Pause();
            ScriptPauseEvent.Reset();
        }
        else
        {
            _lazyBot.Value.Auto.Stop();
            Skills.Resume();
            ScriptPauseEvent.Set();
        }
    }

    [ObservableProperty]
    private string _loadedScript = string.Empty;

    [ObservableProperty]
    private string _compiledScript = string.Empty;

    private string? GetScriptCorrelationId()
    {
        lock (_threadLock)
        {
            return _scriptRunId;
        }
    }

    public IScriptOptionContainer? Config { get; set; }

    public IScriptOptionContainer? RunningConfig { get; private set; }

    public string? OverrideStorage { get; set; }
    public bool SilentConfig { get; set; }

    public CancellationTokenSource? ScriptCts { get; private set; }

    public bool ShouldExit => ScriptCts?.IsCancellationRequested ?? false;

    public async Task<Exception?> StartScript()
    {
        lock (_threadLock)
        {
            if (ScriptRunning)
            {
                _logger.ScriptLog("Script already running.");
                return new Exception("Script already running.");
            }
            ScriptRunning = true;
            _scriptRunId = Guid.NewGuid().ToString("N");
        }

        string scriptCorrelationId;
        lock (_threadLock)
        {
            scriptCorrelationId = _scriptRunId!;
        }

        bool startAnnounced = false;

        // Read before the first await: callers (scheduler, Army Queue) set and clear OverrideStorage around their
        // own work, so it may belong to another task by the time this start builds its options.
        string? overrideStorage = OverrideStorage;

        using IDiagnosticActivity startActivity = _diagnostics.BeginActivity("Script", "Start", scriptCorrelationId);

        try
        {
            await _lazyBot.Value.Auto.StopAsync();

            UnloadPreviousScript();

            string scriptContent = File.ReadAllText(LoadedScript);
            object? script = await Task.Run(() => Compile(scriptContent));

            // Use this start's own container throughout: Config is shared and another configuration task (e.g. the
            // Army Queue) can replace it while this start awaits.
            IScriptOptionContainer? config = BuildScriptConfig(script, overrideStorage);
            if (config is not null)
                Config = config;

            bool hasOptions = config != null && (config.Options.Count > 0 || config.MultipleOptions.Count > 0);
            bool needsConfig;
            lock (_configuredLock)
            {
                needsConfig = hasOptions && _configured.TryGetValue(config!.Storage, out bool b) && !b;
            }

            // A saved enum choice the script no longer has was reset to its default on load: show the options
            // window with the explanation before the script runs, even if the script skips its own options window.
            bool hasResets = config != null && config.ResetNotices.Count > 0;

            if (needsConfig || hasResets)
            {
                if (!SilentConfig)
                    await Task.Run(() => config!.Configure());
                else if (hasResets)
                {
                    foreach (string notice in config!.ResetNotices)
                        _logger.ScriptLog($"[Options] {notice}");
                }

                if (needsConfig)
                {
                    lock (_configuredLock)
                    {
                        _configured[config!.Storage] = true;
                    }
                }
            }

            // The running script reads Bot.Config through RunningConfig, which only this run sets, so a later
            // change to Config by another configuration task doesn't reach it. Cleared when the run ends.
            RunningConfig = config;

            Handlers.Clear();

            lock (_stateLock)
            {
                _runScriptStoppingBool = false;
            }

            Thread scriptThread = new(() =>
            {
                Exception? exception = null;
                ScriptCts = new();


                try
                {
                    script?.GetType().GetMethod("ScriptMain")?.Invoke(script, new object[] { _lazyBot.Value });
                }
                catch (Exception e)
                {
                    Exception actualException = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;

                    bool stoppedByScript;
                    lock (_stateLock)
                    {
                        stoppedByScript = _stoppedByScript;
                    }

                    if ((actualException is not OperationCanceledException || !stoppedByScript) && (e is not TargetInvocationException || !stoppedByScript))
                    {
                        exception = e;
                        Trace.WriteLine($"Error while running script:\r\nMessage: {(e.InnerException is not null ? e.InnerException.Message : e.Message)}\r\nStackTrace: {(e.InnerException is not null ? e.InnerException.StackTrace : e.StackTrace)}");

                        StrongReferenceMessenger.Default.Send<ScriptErrorMessage, int>(new(e), (int)MessageChannels.ScriptStatus);

                        lock (_stateLock)
                        {
                            _runScriptStoppingBool = true;
                        }
                    }
                }
                finally
                {
                    bool shouldSendStoppingMessage;
                    lock (_stateLock)
                    {
                        _stoppedByScript = false;
                        shouldSendStoppingMessage = _runScriptStoppingBool;
                    }

                    if (shouldSendStoppingMessage)
                    {
                        StrongReferenceMessenger.Default.Send<ScriptStoppingMessage, int>((int)MessageChannels.ScriptStatus);
                        try
                        {
                            Task<bool?> messageTask = Task.Run(async () => await StrongReferenceMessenger.Default.Send<ScriptStoppingRequestMessage, int>(new(exception), (int)MessageChannels.ScriptStatus));
                            if (messageTask.Wait(TimeSpan.FromSeconds(2)))
                            {
                                switch (messageTask.Result)
                                {
                                    case true:
                                        Trace.WriteLine("Script finished successfully.");
                                        break;

                                    case false:
                                        Trace.WriteLine("Script finished early or with errors.");
                                        break;

                                    default:
                                        break;
                                }
                            }
                            else
                            {
                                Trace.WriteLine("Script stopping message timed out.");
                            }
                        }
                        catch { }
                    }

                    script = null;
                    Skills.Stop();
                    Drops.Stop();

                    AuraMonitor.StopMonitoring();
                    UnloadPreviousScript();
                    ScriptCts?.Dispose();
                    ScriptCts = null;
                    ScriptPaused = false;
                    _diagnostics.RecordEvent("Script", "Stopped", correlationId: scriptCorrelationId);
                    StrongReferenceMessenger.Default.Send<ScriptStoppedMessage, int>((int)MessageChannels.ScriptStatus);
                    RunningConfig = null;
                    ScriptRunning = false;
                    lock (_threadLock)
                    {
                        if (string.Equals(_scriptRunId, scriptCorrelationId, StringComparison.Ordinal))
                            _scriptRunId = null;
                    }
                }
            })
            {
                Name = "Script Thread",
                IsBackground = true
            };

            // Announce the start before the thread runs: a script that ends at once sends its stopped (and error)
            // messages from its own thread, and those must not reach listeners before this one (the loader would
            // stay "[Running]", the feed would log the start after the stop).
            StrongReferenceMessenger.Default.Send<ScriptStartedMessage, int>((int)MessageChannels.ScriptStatus);
            startAnnounced = true;
            _diagnostics.RecordEvent("Script", "Started", correlationId: scriptCorrelationId);

            lock (_threadLock)
            {
                _currentScriptThread = scriptThread;
                scriptThread.Start();
            }

            return null;
        }
        catch (Exception e)
        {
            _diagnostics.RecordEvent("Script", "StartFailed", correlationId: scriptCorrelationId);
            lock (_threadLock)
            {
                ScriptRunning = false;
                _scriptRunId = null;
            }
            // Started was sent but the thread never ran: close the run for its listeners.
            if (startAnnounced)
                StrongReferenceMessenger.Default.Send<ScriptStoppedMessage, int>((int)MessageChannels.ScriptStatus);
            return e;
        }
    }

    public async Task RestartScriptAsync()
    {
        Trace.WriteLine("Restarting script");
        await StopScript(false);
        await Task.Run(async () =>
        {
            await Task.Delay(5000);
            await StartScript();
        });
    }

    public async ValueTask StopScript(bool runScriptStoppingEvent = true)
    {
        ScriptPaused = false;

        string? scriptCorrelationId;
        lock (_threadLock)
        {
            scriptCorrelationId = _scriptRunId;
        }

        _diagnostics.RecordEvent("Script", "StopRequested", correlationId: scriptCorrelationId);
        lock (_stateLock)
        {
            _runScriptStoppingBool = runScriptStoppingEvent;
            _stoppedByScript = true;
        }

        ScriptCts?.Cancel();

        if (Thread.CurrentThread.Name == "Script Thread")
        {
            ScriptCts?.Token.ThrowIfCancellationRequested();
            return;
        }

        await Wait.ForTrueAsync(() => ScriptCts == null, 30).ConfigureAwait(false);

        Thread? thread;
        lock (_threadLock)
        {
            thread = _currentScriptThread;
        }

        if (thread?.IsAlive == true)
        {
            await Task.Run(() =>
            {
                if (!thread.Join(TimeSpan.FromSeconds(5)))
                {
                    try
                    {
                        thread.Interrupt();
                        if (!thread.Join(TimeSpan.FromSeconds(3)))
                        {
                            // More aggressive termination attempts
                            try
                            {
                                // Mark the thread as background so it doesn't prevent app shutdown
                                if (!thread.IsBackground)
                                    thread.IsBackground = true;

                                // Give one final chance with a shorter timeout
                                if (!thread.Join(TimeSpan.FromSeconds(1)))
                                {
                                    _logger?.ScriptLog("Script thread is unresponsive and may continue running until process exit.");
                                }
                            }
                            catch (Exception termEx)
                            {
                                _logger?.ScriptLog($"Error during thread termination: {termEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.ScriptLog($"Error stopping script thread: {ex.Message}");
                    }
                }
            }).ConfigureAwait(false);
        }

        OnPropertyChanged(nameof(ScriptRunning));
    }

    [RequiresUnreferencedCode("This method may require code that cannot be statically analyzed for trimming. Use with caution.")]
    public object? Compile(string source)
    {
        string? scriptCorrelationId;
        lock (_threadLock)
        {
            scriptCorrelationId = _scriptRunId;
        }

        using IDiagnosticActivity compileActivity = _diagnostics.BeginActivity("Script", "Compile", scriptCorrelationId);
        CheckScriptVersionRequirement(source);

        Stopwatch sw = Stopwatch.StartNew();

        _includedFilesLock.EnterWriteLock();
        try
        {
            _includedFiles.Clear();
        }
        finally
        {
            _includedFilesLock.ExitWriteLock();
        }

        HashSet<string> references = GetReferences();
        string final = ProcessSources(source, ref references);

        // Debug: Check if final source is empty or contains no classes (disabled for performance)
#if DEBUG_VERBOSE

#endif

        // Check if the processed source contains any class definitions
        if (string.IsNullOrWhiteSpace(final))
        {
            throw new ScriptCompileException($"Script file '{LoadedScript}' is empty after processing includes and references.", final);
        }

        // Use regex to check for class definitions more robustly
        if (!_accessibilityRegex.IsMatch(final))
        {
            throw new ScriptCompileException($"Script file '{LoadedScript}' contains no class definitions after processing includes and references. Scripts must contain at least one class.", final);
        }

        ConcurrentDictionary<string, string> sourceCache = new(StringComparer.OrdinalIgnoreCase);

        // Recursively discover all transitive includes using single-pass BFS and populate sourceCache
        DiscoverAllIncludes(references, sourceCache);

        ScriptLoadContext loadContext = new();
        lock (_stateLock)
        {
            _currentLoadContext = loadContext;
        }

        using IDiagnosticActivity includeActivity = _diagnostics.BeginActivity("Script", "CompileIncludes", scriptCorrelationId);
        List<string> compiledIncludes = CompileIncludedFiles(references, loadContext, sourceCache);

        references.UnionWith(compiledIncludes);

        int cacheHash;
        _includedFilesLock.EnterReadLock();
        try
        {
            cacheHash = ComputeCacheHash(final, new List<string>(_includedFiles));
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }
        CompiledScript = final;
        string scriptName = Path.GetFileNameWithoutExtension(LoadedScript);

        Compiler compiler = Ioc.Default.GetRequiredService<Compiler>();
        compiler.AllowReferencesInCode = true;

        if (references.Count > 0)
            compiler.AddAssemblies(references.ToArray());

        bool cacheHit = Compiler.IsCompiled(cacheHash);
        _diagnostics.RecordEvent("Script", cacheHit ? "CompileCacheHit" : "CompileCacheMiss", correlationId: scriptCorrelationId);
        dynamic? assembly = compiler.CompileClass(final, cacheHash, loadContext, scriptName);

        sw.Stop();
        Trace.WriteLine($"Script compilation took {sw.ElapsedMilliseconds}ms.");

        return compiler.Error
            ? throw new ScriptCompileException(compiler.ErrorMessage, compiler.GeneratedClassCodeWithLineNumbers)
            : (object?)assembly;
    }

    private HashSet<string> GetReferences()
    {
        HashSet<string> references = new();

        lock (_refCacheLock)
        {
            if (_refCache.Count == 0 && Directory.Exists(ClientFileSources.SkuaPluginsDIR))
            {
                foreach (string file in Directory.EnumerateFiles(ClientFileSources.SkuaPluginsDIR, "*.dll"))
                {
                    if (CanLoadAssembly(file))
                    {
                        _refCache.Add(file);
                        references.Add(file);
                    }
                }
            }
            else
            {
                references.UnionWith(_refCache);
            }
        }

        return references;
    }

    private string ProcessSources(string source, ref HashSet<string> references)
    {
        List<string> filesToInclude = new();
        StringBuilder sb = new(source.Length);

        using (StringReader reader = new(source))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("//cs_"))
                {
                    string[] parts = trimmed.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        string cmd = parts[0][5..];
                        switch (cmd)
                        {
                            case "ref":
                                string? local = ResolveImport(LoadedScript, parts[1]);
                                if (local is not null)
                                    references.Add(local);
                                break;

                            case "include":
                                string? localSource = ResolveImport(LoadedScript, parts[1]);
                                if (localSource is not null)
                                    filesToInclude.Add(localSource);
                                break;
                        }
                    }
                    continue;
                }

                sb.AppendLine(line);
            }
        }

        if (filesToInclude.Count > 0)
        {
            _includedFilesLock.EnterWriteLock();
            try
            {
                _includedFiles.AddRange(filesToInclude);
            }
            finally
            {
                _includedFilesLock.ExitWriteLock();
            }
        }

        return sb.ToString().Trim();
    }

    private string? ResolveImport(string ownerFile, string specifier)
    {
        Skua.Core.Models.Scripts.ScriptPathResolution resolution = _pathResolver.Resolve(ownerFile, specifier);
        if (resolution.IsAmbiguous)
            _diagnostics.RecordEvent("Script", "ImportAmbiguous", correlationId: GetScriptCorrelationId());

        return resolution.SelectedPath;
    }

    private void DiscoverAllIncludes(HashSet<string> references, ConcurrentDictionary<string, string> sourceCache)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> queue = new();

        _includedFilesLock.EnterReadLock();
        try
        {
            foreach (string file in _includedFiles)
            {
                if (visited.Add(file))
                    queue.Enqueue(file);
            }
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }

        while (queue.Count > 0)
        {
            string currentFile = queue.Dequeue();
            if (!File.Exists(currentFile))
                continue;

            try
            {
                string fileContent = File.ReadAllText(currentFile);
                sourceCache[currentFile] = fileContent;

                List<string> newIncludes = ExtractIncludeDirectivesFromSource(fileContent, currentFile, references);
                if (newIncludes.Count > 0)
                {
                    List<string> filesToAdd = new();
                    foreach (string include in newIncludes)
                    {
                        if (visited.Add(include) && File.Exists(include))
                        {
                            filesToAdd.Add(include);
                            queue.Enqueue(include);
                        }
                    }

                    if (filesToAdd.Count > 0)
                    {
                        _includedFilesLock.EnterWriteLock();
                        try
                        {
                            _includedFiles.AddRange(filesToAdd);
                        }
                        finally
                        {
                            _includedFilesLock.ExitWriteLock();
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    private List<string> ExtractIncludeDirectivesFromSource(string source, string ownerFile, HashSet<string> references)
    {
        List<string> includes = new();
        ReadOnlySpan<char> sourceSpan = source.AsSpan();

        int start = 0;
        int newlinePos;
        while ((newlinePos = sourceSpan[start..].IndexOf('\n')) >= 0)
        {
            ReadOnlySpan<char> line = sourceSpan[start..(start + newlinePos)].Trim();

            if (line.StartsWith("using"))
                break;

            if (line.StartsWith("//cs_"))
            {
                string lineStr = new(line);
                string[] parts = lineStr.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string cmd = parts[0][5..];
                    switch (cmd)
                    {
                        case "ref":
                            string? refLocal = ResolveImport(ownerFile, parts[1]);
                            if (refLocal is not null)
                                references.Add(refLocal);
                            break;

                        case "include":
                            string? includeLocal = ResolveImport(ownerFile, parts[1]);
                            if (includeLocal is not null)
                                includes.Add(includeLocal);
                            break;
                    }
                }
            }

            start += newlinePos + 1;
        }

        return includes;
    }

    public void LoadScriptConfig(object? script)
    {
        IScriptOptionContainer? opts = BuildScriptConfig(script, OverrideStorage);
        if (opts is not null)
            Config = opts;
    }

    private IScriptOptionContainer? BuildScriptConfig(object? script, string? overrideStorage)
    {
        if (script is null)
            return null;

        IScriptOptionContainer opts = Ioc.Default.GetRequiredService<IScriptOptionContainer>();
        Type t = script.GetType();
        FieldInfo? storageField = t.GetField("OptionsStorage");
        FieldInfo? optsField = t.GetField("Options");
        FieldInfo? multiOptsField = t.GetField("MultiOptions");
        FieldInfo? dontPreconfField = t.GetField("DontPreconfigure");
        if (multiOptsField is not null)
        {
            string[] optFieldNames = (string[])multiOptsField.GetValue(script)!;
            List<FieldInfo> multiOpts = new(optFieldNames.Length);
            foreach (string optField in optFieldNames)
            {
                FieldInfo? field = t.GetField(optField);
                if (field != null)
                    multiOpts.Add(field);
            }
            foreach (FieldInfo opt in multiOpts)
            {
                List<IOption> parsedOpt = (List<IOption>)opt.GetValue(script)!;
                parsedOpt.ForEach(o => o.Category = opt.Name.Replace('_', ' '));
                opts.MultipleOptions.Add(opt.Name, parsedOpt);
            }
        }
        if (optsField is not null)
            opts.Options.AddRange((List<IOption>)optsField.GetValue(script)!);
        if (!string.IsNullOrWhiteSpace(overrideStorage)) opts.Storage = overrideStorage; else if (storageField is not null) opts.Storage = (string)storageField.GetValue(script)!;
        lock (_configuredLock)
        {
            if (dontPreconfField is not null)
                _configured[opts.Storage] = (bool)dontPreconfField.GetValue(script)!;
            else if (optsField is not null)
                _configured[opts.Storage] = false;
        }

        opts.SetDefaults();
        opts.Load();
        return opts;
    }

    private static bool CanLoadAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ComputeCacheHash(string source, List<string> includedFiles)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        byte[] sourceHash = sha256.ComputeHash(sourceBytes);
        using MemoryStream ms = new();
        ms.Write(sourceHash, 0, sourceHash.Length);

        foreach (string file in includedFiles.OrderBy(f => f))
        {
            if (File.Exists(file))
            {
                byte[] pathBytes = Encoding.UTF8.GetBytes(file);
                ms.Write(pathBytes, 0, pathBytes.Length);

                long ticks = File.GetLastWriteTimeUtc(file).Ticks;
                byte[] ticksBytes = BitConverter.GetBytes(ticks);
                ms.Write(ticksBytes, 0, ticksBytes.Length);
            }
        }

        byte[] combinedHash = sha256.ComputeHash(ms.ToArray());
        return BitConverter.ToInt32(combinedHash, 0);
    }

    private void CheckScriptVersionRequirement(string source)
    {
        Match match = _versionRegex.Match(source);
        if (match.Success)
        {
            string requiredVersionStr = match.Groups[1].Value;
            Version? currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            if (Version.TryParse(requiredVersionStr, out Version? requiredVersion) && currentVersion != null && currentVersion < requiredVersion)
            {
                throw new ScriptVersionException(requiredVersionStr, currentVersion.ToString());
            }
        }
    }

    private List<string> CompileIncludedFiles(HashSet<string> references, ScriptLoadContext loadContext, ConcurrentDictionary<string, string> sourceCache)
    {
        ConcurrentDictionary<string, string> compiledPaths = new();
        ConcurrentDictionary<string, bool> compilationCompleted = new();
        object lockObj = new();

        ConcurrentDictionary<string, List<string>> dependencyGraph = new();
        ConcurrentDictionary<string, (string source, string fileName, int hash, string cachePath)> fileInfoCache = new();

        string cacheDir = _cacheScriptsDir;

        ConcurrentBag<string> validCachedFiles = new();

        List<string> includedFilesSnapshot;
        _includedFilesLock.EnterReadLock();
        try
        {
            includedFilesSnapshot = new List<string>(_includedFiles);
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }

        Parallel.ForEach(includedFilesSnapshot, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, includedFile =>
        {
            string includeSource = sourceCache.TryGetValue(includedFile, out string? cached) ? cached : File.ReadAllText(includedFile);
            sourceCache[includedFile] = includeSource;
            CheckScriptVersionRequirement(includeSource);
            List<string> deps = ExtractIncludeDependencies(includeSource, includedFile);
            List<string> normalizedDeps = new();
            foreach (string dep in deps)
            {
                string? matchingFile;
                _includedFilesLock.EnterReadLock();
                try
                {
                    matchingFile = _includedFiles.FirstOrDefault(path => path.Equals(dep, StringComparison.Ordinal));
                    if (matchingFile is null && OperatingSystem.IsWindows())
                        matchingFile = _includedFiles.FirstOrDefault(path => path.Equals(dep, StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    _includedFilesLock.ExitReadLock();
                }
                if (matchingFile is not null)
                    normalizedDeps.Add(matchingFile);
            }
            dependencyGraph[includedFile] = normalizedDeps;
        });

        HashSet<string> allReferencedFiles;
        _includedFilesLock.EnterReadLock();
        try
        {
            allReferencedFiles = new HashSet<string>(_includedFiles);
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }

        List<string> newlyAddedFiles = new();
        foreach (KeyValuePair<string, List<string>> kvp in dependencyGraph)
        {
            foreach (string dep in kvp.Value)
            {
                if (File.Exists(dep) && !allReferencedFiles.Contains(dep))
                {
                    allReferencedFiles.Add(dep);
                    newlyAddedFiles.Add(dep);
                }
            }
        }

        if (newlyAddedFiles.Count > 0)
        {
            _includedFilesLock.EnterWriteLock();
            try
            {
                _includedFiles.AddRange(newlyAddedFiles);
            }
            finally
            {
                _includedFilesLock.ExitWriteLock();
            }

            foreach (string newFile in newlyAddedFiles)
            {
                string includeSource = sourceCache.TryGetValue(newFile, out string? cached) ? cached : File.ReadAllText(newFile);
                sourceCache[newFile] = includeSource;
                CheckScriptVersionRequirement(includeSource);
                List<string> deps = ExtractIncludeDependencies(includeSource, newFile);
                List<string> normalizedDeps = new();
                foreach (string dep in deps)
                {
                    string? matchingFile;
                    _includedFilesLock.EnterReadLock();
                    try
                    {
                        matchingFile = _includedFiles.FirstOrDefault(path => path.Equals(dep, StringComparison.Ordinal));
                        if (matchingFile is null && OperatingSystem.IsWindows())
                            matchingFile = _includedFiles.FirstOrDefault(path => path.Equals(dep, StringComparison.OrdinalIgnoreCase));
                    }
                    finally
                    {
                        _includedFilesLock.ExitReadLock();
                    }
                    if (matchingFile is not null)
                        normalizedDeps.Add(matchingFile);
                }
                dependencyGraph[newFile] = normalizedDeps;
            }
        }

        List<string> orderedFiles;
        _includedFilesLock.EnterReadLock();
        try
        {
            orderedFiles = SortByDependencyOrder(dependencyGraph, new List<string>(_includedFiles));
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }

        // Compute dependency-aware hashes in topological order (leaves first)
        Dictionary<string, int> fileHashes = new(StringComparer.OrdinalIgnoreCase);
        using (SHA256 sha256 = SHA256.Create())
        {
            foreach (string file in orderedFiles)
            {
                string src = sourceCache.TryGetValue(file, out string? cached) ? cached : File.ReadAllText(file);
                byte[] srcBytes = Encoding.UTF8.GetBytes(src);
                if (dependencyGraph.TryGetValue(file, out List<string>? fileDeps) && fileDeps.Count > 0)
                {
                    using MemoryStream ms = new();
                    ms.Write(srcBytes, 0, srcBytes.Length);
                    foreach (string dep in fileDeps.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    {
                        if (fileHashes.TryGetValue(dep, out int depHash))
                        {
                            ms.Write(BitConverter.GetBytes(depHash), 0, 4);
                        }
                    }
                    byte[] combined = sha256.ComputeHash(ms.ToArray());
                    fileHashes[file] = BitConverter.ToInt32(combined, 0);
                }
                else
                {
                    byte[] hashBytes = sha256.ComputeHash(srcBytes);
                    fileHashes[file] = BitConverter.ToInt32(hashBytes, 0);
                }
            }
        }

        // Verify disk cache for each file
        foreach (string file in orderedFiles)
        {
            string includeFileName = Path.GetFileNameWithoutExtension(file);
            int includeHash = fileHashes[file];
            string compiledPath = Path.Combine(cacheDir, $"{includeHash}-{includeFileName}.dll");

            if (Compiler.FastVerifyAssembly(compiledPath))
            {
                validCachedFiles.Add(file);
                compiledPaths[file] = compiledPath;
                compilationCompleted[file] = true;
                Compiler.RegisterCompiled(includeHash, compiledPath);
                loadContext.RegisterKnownAssembly(includeFileName, compiledPath);
                fileInfoCache[file] = (string.Empty, includeFileName, includeHash, compiledPath);
            }
            else
            {
                string src = sourceCache.TryGetValue(file, out string? cached) ? cached : File.ReadAllText(file);
                fileInfoCache[file] = (src, includeFileName, includeHash, compiledPath);
            }
        }

        _diagnostics.RecordEvent("Script", "IncludeCacheFiles", validCachedFiles.Count, "files", GetScriptCorrelationId());

        HashSet<string> compiledFiles = new(validCachedFiles);

        foreach (string cachedFile in validCachedFiles)
        {
            compilationCompleted[cachedFile] = true;
        }

        while (compiledFiles.Count < orderedFiles.Count)
        {
            List<string> readyBatch = new();

            foreach (string file in orderedFiles)
            {
                if (compiledFiles.Contains(file))
                    continue;

                bool allDepsReady = true;
                if (dependencyGraph.TryGetValue(file, out List<string>? fileDeps))
                {
                    foreach (string dep in fileDeps)
                    {
                        if (!compilationCompleted.GetValueOrDefault(dep, false))
                        {
                            allDepsReady = false;
                            break;
                        }
                    }
                }

                if (allDepsReady)
                    readyBatch.Add(file);
            }

            if (readyBatch.Count == 0)
                break;

            Parallel.ForEach(readyBatch, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
            {
                try
                {
                    CompileIncludeRecursive(file, references, loadContext, compiledPaths, compilationCompleted, lockObj, fileInfoCache);
                }
                catch (Exception ex)
                {
                    throw;
                }
            });

            foreach (string file in readyBatch)
                compiledFiles.Add(file);
        }

        fileInfoCache.Clear();

        return compiledPaths.Values.ToList();
    }


    private void CompileIncludeRecursive(
        string includedFile,
        HashSet<string> references,
        ScriptLoadContext loadContext,
        ConcurrentDictionary<string, string> compiledPaths,
        ConcurrentDictionary<string, bool> compilationCompleted,
        object lockObj,
        ConcurrentDictionary<string, (string source, string fileName, int hash, string cachePath)> fileInfoCache)
    {
        if (compiledPaths.ContainsKey(includedFile))
            return;

        try
        {
            (string source, string fileName, int hash, string cachePath) info = fileInfoCache[includedFile];
            string includeSource = info.source;
            string includeFileName = info.fileName;
            int includeHash = info.hash;
            string compiledPath = info.cachePath;

            HashSet<string> includeReferences = new(references);

            lock (lockObj)
            {
                foreach (KeyValuePair<string, string> kvp in compiledPaths)
                {
                    includeReferences.Add(kvp.Value);
                }
            }

            string processedInclude = ProcessIncludeDirectives(includeSource, includedFile, ref includeReferences);

            Compiler includeCompiler = Ioc.Default.GetRequiredService<Compiler>();
            includeCompiler.AllowReferencesInCode = true;

            if (includeReferences.Count > 0)
                includeCompiler.AddAssemblies(includeReferences.ToArray());

            includeCompiler.CompileClassToType(processedInclude, includeHash, loadContext, includeFileName);

            if (includeCompiler.Error)
            {
                throw new ScriptCompileException(
                    $"Error compiling included file '{includedFile}':\n{includeCompiler.ErrorMessage}",
                    includeCompiler.GeneratedClassCodeWithLineNumbers);
            }

            if (File.Exists(compiledPath))
            {
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        using FileStream fs = new(compiledPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        if (fs.Length > 0)
                            break;
                    }
                    catch
                    {
                        Thread.Sleep(10);
                    }
                }

                lock (lockObj)
                {
                    compiledPaths[includedFile] = compiledPath;
                    compilationCompleted[includedFile] = true;
                    loadContext.RegisterKnownAssembly(includeFileName, compiledPath);

                    if (!Compiler.IsLoaded(compiledPath))
                    {
                        try
                        {
                            using FileStream stream = File.OpenRead(compiledPath);
                            Assembly loadedAssembly = loadContext.LoadFromStream(stream);
                            Compiler.RegisterLoaded(compiledPath, loadedAssembly);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not ScriptCompileException)
        {
            throw new ScriptCompileException($"Failed to compile included file '{includedFile}': {ex.Message}", string.Empty);
        }
    }

    private List<string> SortByDependencyOrder(ConcurrentDictionary<string, List<string>> dependencyGraph, List<string> files)
    {
        List<string> result = new();
        HashSet<string> visited = new();
        HashSet<string> visiting = new();

        foreach (string file in files)
        {
            if (!visited.Contains(file))
            {
                VisitDependencies(file, dependencyGraph, visited, visiting, result, files);
            }
        }

        return result;
    }

    private void VisitDependencies(string file, ConcurrentDictionary<string, List<string>> dependencyGraph,
        HashSet<string> visited, HashSet<string> visiting, List<string> result, List<string> allFiles)
    {
        if (visiting.Contains(file))
            return;

        if (visited.Contains(file))
            return;

        visiting.Add(file);

        // Visit all dependencies first
        if (dependencyGraph.TryGetValue(file, out List<string>? dependencies))
        {
            foreach (string dependency in dependencies)
            {
                if (allFiles.Contains(dependency))
                {
                    VisitDependencies(dependency, dependencyGraph, visited, visiting, result, allFiles);
                }
            }
        }

        visiting.Remove(file);
        visited.Add(file);
        result.Add(file);
    }

    private List<string> GetCompilationOrder(ConcurrentDictionary<string, List<string>> dependencyGraph, List<string> files)
    {
        List<string> result = new();
        HashSet<string> visited = new();
        HashSet<string> visiting = new();

        foreach (string file in files)
        {
            if (!visited.Contains(file))
            {
                VisitFile(file, dependencyGraph, visited, visiting, result, files);
            }
        }

        return result;
    }

    private void VisitFile(string file, ConcurrentDictionary<string, List<string>> dependencyGraph,
        HashSet<string> visited, HashSet<string> visiting, List<string> result, List<string> allFiles)
    {
        if (visiting.Contains(file))
            return;

        if (visited.Contains(file))
            return;

        visiting.Add(file);

        if (dependencyGraph.TryGetValue(file, out List<string>? deps))
        {
            foreach (string dep in deps)
            {
                if (allFiles.Contains(dep) && !visited.Contains(dep))
                {
                    VisitFile(dep, dependencyGraph, visited, visiting, result, allFiles);
                }
            }
        }

        visiting.Remove(file);
        visited.Add(file);
        result.Add(file);
    }

    private void GetTransitiveDependencies(string file, ConcurrentDictionary<string, List<string>> dependencyGraph, HashSet<string> allDeps)
    {
        if (!dependencyGraph.TryGetValue(file, out List<string>? directDeps))
            return;

        foreach (string dep in directDeps)
        {
            if (allDeps.Add(dep))
            {
                GetTransitiveDependencies(dep, dependencyGraph, allDeps);
            }
        }
    }

    private List<string> ExtractIncludeDependencies(string source, string ownerFile)
    {
        List<string> dependencies = new();
        ReadOnlySpan<char> sourceSpan = source.AsSpan();

        int start = 0;
        int newlinePos;
        while ((newlinePos = sourceSpan[start..].IndexOf('\n')) >= 0)
        {
            ReadOnlySpan<char> line = sourceSpan[start..(start + newlinePos)].Trim();

            if (line.StartsWith("using"))
                break;

            if (line.StartsWith("//cs_include "))
            {
                string lineStr = new(line);
                string[] parts = lineStr.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string? includePath = ResolveImport(ownerFile, parts[1]);
                    if (includePath is not null)
                        dependencies.Add(includePath);
                }
            }

            start += newlinePos + 1;
        }

        return dependencies;
    }


    private string ProcessIncludeDirectives(string source, string ownerFile, ref HashSet<string> references)
    {
        StringBuilder sb = new(source.Length);
        using (StringReader reader = new(source))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("//cs_"))
                {
                    string[] parts = trimmed.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[0][5..] == "ref")
                    {
                        string? local = ResolveImport(ownerFile, parts[1]);
                        if (local is not null)
                            references.Add(local);
                    }
                    continue;
                }

                sb.AppendLine(line);
            }
        }

        return sb.ToString().Trim();
    }

    private void UnloadPreviousScript()
    {
        ScriptLoadContext? context;
        lock (_stateLock)
        {
            context = _currentLoadContext;
            _currentLoadContext = null;
        }

        Compiler.ClearSessionRegistries();

        if (context is null)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                WeakReference weak = new(context);
                context.Unload();

                if (weak.IsAlive)
                {
                    GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
                }
            }
            catch
            {
            }
        });
    }

    public void SetLoadedScript(string path)
    {
        LoadedScript = path;
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_threadLock)
        {
            thread = _currentScriptThread;
        }

        if (thread?.IsAlive == true)
        {
            ScriptCts?.Cancel();
            if (!thread.Join(TimeSpan.FromSeconds(5)))
            {
                _logger?.ScriptLog("Script thread did not exit during disposal.");
            }
        }
        ScriptCts?.Dispose();
        _includedFilesLock?.Dispose();
    }
}
