using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Skua.Plugin.Omniscope;

/// <summary>
/// Represents one searchable object or source entry in the OMNISCOPE developer catalog.
/// </summary>
internal sealed class GameDataRecord
{
    public string Category { get; init; } = string.Empty;
    public string DisplayCategory => Category switch
    {
        "CLIENT CORE" => "Client Core",
        "NETWORK / API" => "Network / API",
        "GAME UI" => "Game UI",
        "WORLD / MAP" => "World / Map",
        "PLAYER / AVATAR" => "Player / Avatar",
        "INVENTORY / ITEMS" => "Inventory / Items",
        "COMBAT / SKILLS" => "Combat / Skills",
        "GAME SYSTEMS" => "Game Systems",
        "OTHER" => "Other Client Code",
        "MAP SHOP" => "Map Shop",
        "MAP ITEM" => "Map Item",
        _ => Category
    };
    public string Name { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Metadata { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string SearchText { get; init; } = string.Empty;
    public int SourceFunctionCount { get; init; }
    public int? SourceShopId { get; init; }
    public int? SourceQuestId { get; init; }
    public string? SourceMapName { get; init; }
    public object? SourceObject { get; init; }
}

/// <summary>
/// Exports and indexes ActionScript source from the packaged AQW client SWF using the bundled FFDec tool.
/// </summary>
internal static class SwfSourceIndexer
{
    private const int MaximumSourceFiles = 5000;
    private const long MaximumSourceBytes = 64L * 1024 * 1024;
    private const long MaximumFileBytes = 512L * 1024;
    private static readonly Regex ClassNameRegex = new(@"(?:public|internal|protected)?\s*(?:final\s+)?class\s+([A-Za-z0-9_]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FunctionRegex = new(@"\bfunction\s+([A-Za-z0-9_]+)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Locates the bundled FFDec JAR relative to either the application or plugin directory.
    /// </summary>
    /// <returns>The directory containing ffdec.jar.</returns>
    public static string ResolveFfdecDirectory()
    {
        string assemblyDirectory = Path.GetDirectoryName(typeof(SwfSourceIndexer).Assembly.Location) ?? AppContext.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "FFDec"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "FFDec")),
            Path.Combine(assemblyDirectory, "FFDec"),
            Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "FFDec"))
        };

        string? ffdecDirectory = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static path => File.Exists(Path.Combine(path, "ffdec.jar")));

        return ffdecDirectory ?? throw new DirectoryNotFoundException(
            $"The bundled FFDec runtime was not found. Checked: {string.Join("; ", candidates)}");
    }

    /// <summary>
    /// Resolves the private Java runtime packaged beside FFDec, with system Java as a development fallback.
    /// </summary>
    /// <param name="ffdecDirectory">Directory containing ffdec.jar and the optional Runtime folder.</param>
    /// <returns>The Java executable path.</returns>
    private static string ResolveJavaExecutable(string ffdecDirectory)
    {
        string bundledJava = Path.Combine(ffdecDirectory, "Runtime", "bin", "java.exe");
        if (File.Exists(bundledJava))
            return bundledJava;

        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            string javaHomeExecutable = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(javaHomeExecutable))
                return javaHomeExecutable;
        }

        string? pathEnvironment = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnvironment))
        {
            foreach (string path in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string pathJava = Path.Combine(path, "java.exe");
                if (File.Exists(pathJava))
                    return pathJava;
            }
        }

        throw new FileNotFoundException(
            $"VibeSkua's bundled Java 21 runtime is missing. Expected: {bundledJava}. Run package-build.ps1 to restore the portable runtime.",
            bundledJava);
    }

    /// <summary>
    /// Decompiles the supplied SWF when no matching export exists, then builds a bounded searchable source index.
    /// </summary>
    /// <param name="swfPath">Path to the packaged AQW client SWF.</param>
    /// <param name="ffdecDirectory">Directory containing the bundled FFDec JAR.</param>
    /// <param name="cancellationToken">Token used to cancel the export operation.</param>
    /// <returns>The export directory and indexed ActionScript source records.</returns>
    public static async Task<SwfSourceIndexResult> DecompileAsync(string swfPath, string ffdecDirectory, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(swfPath))
            throw new FileNotFoundException("The packaged AQW client SWF could not be found.", swfPath);

        string ffdecJarPath = Path.GetFullPath(Path.Combine(ffdecDirectory, "ffdec.jar"));
        if (!File.Exists(ffdecJarPath))
            throw new FileNotFoundException("The bundled FFDec JAR could not be found.", ffdecJarPath);

        FileInfo swfInfo = new(swfPath);
        string versionKey = $"{swfInfo.Length}_{swfInfo.LastWriteTimeUtc.Ticks}";
        string exportDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VibeSkua",
            "Omniscope",
            "DecompiledClient",
            versionKey);
        string markerPath = Path.Combine(exportDirectory, ".omniscope-export-complete");
        Directory.CreateDirectory(exportDirectory);

        bool hasExportedScripts = Directory.EnumerateFiles(exportDirectory, "*.as", SearchOption.AllDirectories).Any();
        if (!File.Exists(markerPath) || !hasExportedScripts)
        {
            ProcessStartInfo startInfo = new(ResolveJavaExecutable(ffdecDirectory))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.SystemDirectory
            };
            startInfo.Environment["VLC_VERBOSE"] = "-1";
            startInfo.ArgumentList.Add("-Xmx1536m");
            startInfo.ArgumentList.Add("--enable-native-access=ALL-UNNAMED");
            startInfo.ArgumentList.Add("--add-opens=java.base/java.lang=ALL-UNNAMED");
            startInfo.ArgumentList.Add("--add-opens=java.base/java.io=ALL-UNNAMED");
            startInfo.ArgumentList.Add("--add-opens=java.base/java.util=ALL-UNNAMED");
            startInfo.ArgumentList.Add("--add-opens=java.base/sun.nio.fs=ALL-UNNAMED");
            startInfo.ArgumentList.Add("--add-opens=java.desktop/sun.awt=ALL-UNNAMED");
            startInfo.ArgumentList.Add("--add-opens=java.desktop/sun.java2d=ALL-UNNAMED");
            startInfo.ArgumentList.Add("-Djava.net.preferIPv4Stack=true");
            startInfo.ArgumentList.Add("-Djna.nosys=true");
            startInfo.ArgumentList.Add("-Djava.util.Arrays.useLegacyMergeSort=true");
            startInfo.ArgumentList.Add("-jar");
            startInfo.ArgumentList.Add(ffdecJarPath);
            startInfo.ArgumentList.Add("-export");
            startInfo.ArgumentList.Add("script");
            startInfo.ArgumentList.Add(exportDirectory);
            startInfo.ArgumentList.Add(swfPath);

            using Process process = new() { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("FFDec could not be started.");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(
                    $"The bundled Java runtime could not be started ({ex.NativeErrorCode}). Executable: {startInfo.FileName}",
                    ex);
            }

            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Priority adjustment is optional; decompilation can continue with the default priority.
            }

            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);

                throw;
            }

            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);

            string standardOutput = await standardOutputTask.ConfigureAwait(false);
            string standardError = await standardErrorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string diagnostic = string.Join(Environment.NewLine, new[] { standardError, standardOutput }.Where(static value => !string.IsNullOrWhiteSpace(value)));
                if (diagnostic.Length > 5000)
                    diagnostic = diagnostic[^5000..];

                throw new InvalidOperationException($"FFDec exited with code {process.ExitCode}.{Environment.NewLine}{diagnostic}");
            }

            await File.WriteAllTextAsync(markerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);
        }

        List<GameDataRecord> records = await ReadSourceRecordsAsync(exportDirectory, cancellationToken).ConfigureAwait(false);
        if (records.Count == 0)
            throw new InvalidDataException("FFDec completed but did not export any ActionScript source files.");

        return new SwfSourceIndexResult(exportDirectory, records);
    }

    private static async Task<List<GameDataRecord>> ReadSourceRecordsAsync(string exportDirectory, CancellationToken cancellationToken)
    {
        List<GameDataRecord> records = new();
        long totalBytes = 0;

        foreach (string filePath in Directory.EnumerateFiles(exportDirectory, "*.as", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (records.Count >= MaximumSourceFiles)
                break;

            FileInfo fileInfo = new(filePath);
            if (fileInfo.Length <= 0 || fileInfo.Length > MaximumFileBytes || totalBytes + fileInfo.Length > MaximumSourceBytes)
                continue;

            string source;
            try
            {
                source = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            totalBytes += fileInfo.Length;
            string relativePath = Path.GetRelativePath(exportDirectory, filePath);
            Match classMatch = ClassNameRegex.Match(source);
            string name = classMatch.Success ? classMatch.Groups[1].Value : Path.GetFileNameWithoutExtension(filePath);
            string category = ClassifySource(relativePath, source);
            int functionCount = FunctionRegex.Matches(source).Count;
            string[] matchingSignals = new[] { "achievement", "badge", "quest", "shop", "drop", "mapitem", "reward", "requirement" }
                .Where(signal => source.Contains(signal, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            string summary = $"{functionCount:N0} functions  /  {fileInfo.Length:N0} bytes" +
                (matchingSignals.Length > 0 ? $"  /  {string.Join(", ", matchingSignals)}" : string.Empty);
            string details = $"CATEGORY: {category}{Environment.NewLine}" +
                $"SOURCE: {relativePath}{Environment.NewLine}" +
                $"SIZE: {fileInfo.Length:N0} bytes{Environment.NewLine}" +
                $"FUNCTIONS: {functionCount:N0}{Environment.NewLine}" +
                $"INDEXED: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}" +
                $"{Environment.NewLine}------------------------------------------------------------{Environment.NewLine}" +
                source;

            records.Add(new GameDataRecord
            {
                Category = category,
                Name = name,
                Summary = summary,
                Metadata = relativePath,
                Details = details,
                SearchText = $"{category} {name} {relativePath} {summary} {source}",
                SourceFunctionCount = functionCount
            });
        }

        return records;
    }

    private static string ClassifySource(string relativePath, string source)
    {
        string fileName = Path.GetFileNameWithoutExtension(relativePath);
        string searchable = $"{fileName} {relativePath} {source}";

        if (ContainsAny(searchable, "socket", "packet", "gateway", "network", "connection", "request", "response", "sendto", "recvfrom", "geturl"))
            return "NETWORK / API";

        if (ContainsAny(searchable, "mcinterface", "interface", "textfield", "button", "panel", "window", "popup", "tooltip", "dialog", "movieclip", "hud", "frame"))
            return "GAME UI";

        if (ContainsAny(searchable, "map", "world", "room", "cell", "zone", "location", "teleport", "spawn"))
            return "WORLD / MAP";

        if (ContainsAny(searchable, "avatar", "player", "character", "username", "guild", "health", "mana", "stats"))
            return "PLAYER / AVATAR";

        if (ContainsAny(searchable, "quest", "achievement", "faction", "reputation", "house", "enhancement", "pet", "class"))
            return "GAME SYSTEMS";

        if (ContainsAny(searchable, "combat", "skill", "attack", "target", "battle", "damage", "cooldown", "aggro", "monster"))
            return "COMBAT / SKILLS";

        if (ContainsAny(searchable, "inventory", "item", "shop", "bank", "drop", "reward", "buy", "sell"))
            return "INVENTORY / ITEMS";

        if (ContainsAny(searchable, "loader", "bootstrap", "config", "client", "main", "root", "initializ", "startup"))
            return "CLIENT CORE";

        return "OTHER";
    }

    private static bool ContainsAny(string source, params string[] terms) =>
        terms.Any(term => source.Contains(term, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Describes the result of exporting and indexing the client SWF.
/// </summary>
internal sealed record SwfSourceIndexResult(string ExportDirectory, IReadOnlyList<GameDataRecord> Records);
