using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Scripts;
using Skua.Core.Services;
using Skua.Core.Utils;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.PortableExecutable;

namespace Skua.Core.AppStartup;

public static class Services
{
    public static IServiceCollection AddCommonServices(this IServiceCollection services)
    {
        services.AddTransient(typeof(Lazy<>), typeof(LazyInstance<>));

        services.AddSingleton(typeof(IMessenger), WeakReferenceMessenger.Default);
        services.AddSingleton(typeof(StrongReferenceMessenger), StrongReferenceMessenger.Default);

        services.AddSingleton<IDecamelizer, Decamelizer>();
        services.AddSingleton<IGetScriptsService, GetScriptsService>();
        services.AddSingleton<IProcessService, ProcessStartService>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<ICustomScriptService, CustomScriptService>();
        services.AddSingleton<IScriptPathResolver, ScriptPathResolver>();
        services.AddSingleton<ILoadoutService, LoadoutService>();
        services.AddSingleton<IDiscordWebhookService, DiscordWebhookService>();

        return services;
    }


    private static bool IsManagedAssembly(string filePath)
    {
        try
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < 64)
                return false;

            using PEReader peReader = new(fs);
            return peReader.HasMetadata;
        }
        catch
        {
            return false;
        }
    }

    private static List<PortableExecutableReference>? _cachedBaseReferences;
    private static readonly object _referenceCacheLock = new();

    public static Compiler CreateCompiler(IServiceProvider s)
    {
        Compiler compiler = new();

        if (_cachedBaseReferences == null)
        {
            lock (_referenceCacheLock)
            {
                if (_cachedBaseReferences == null)
                {
                    Dictionary<string, string> assemblyMap = new(StringComparer.OrdinalIgnoreCase);

                    void AddAssemblyPath(string? path)
                    {
                        if (string.IsNullOrEmpty(path) || !File.Exists(path))
                            return;

                        string fileName = Path.GetFileName(path);
                        if (assemblyMap.ContainsKey(fileName))
                            return;

                        if (IsManagedAssembly(path))
                            assemblyMap[fileName] = path;
                    }

                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location) && !asm.Location.Contains("xunit"))
                        {
                            AddAssemblyPath(asm.Location);
                        }
                    }

                    if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
                    {
                        foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                        {
                            AddAssemblyPath(path);
                        }
                    }

                    string? runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
                    if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir))
                    {
                        foreach (string dll in Directory.EnumerateFiles(runtimeDir, "*.dll"))
                        {
                            AddAssemblyPath(dll);
                        }

                        if (OperatingSystem.IsWindows())
                        {
                            string? dotnetShared = Path.GetDirectoryName(runtimeDir);
                            string? dotnetSharedRoot = Path.GetDirectoryName(dotnetShared);
                            string versionDir = Path.GetFileName(runtimeDir);
                            if (!string.IsNullOrEmpty(dotnetSharedRoot))
                            {
                                string desktopDir = Path.Combine(dotnetSharedRoot, "Microsoft.WindowsDesktop.App", versionDir);
                                if (Directory.Exists(desktopDir))
                                {
                                    foreach (string dll in Directory.EnumerateFiles(desktopDir, "*.dll"))
                                    {
                                        AddAssemblyPath(dll);
                                    }
                                }
                            }
                        }
                    }

                    string[] refPaths = {
                        typeof(object).GetTypeInfo().Assembly.Location,
                        typeof(Console).GetTypeInfo().Assembly.Location,
                        typeof(object).Assembly.Location,
                        typeof(Enumerable).Assembly.Location,
                        typeof(ScriptManager).Assembly.Location,
                        typeof(System.Text.RegularExpressions.Regex).Assembly.Location,
                        typeof(ZipArchive).Assembly.Location,
                    };

                    foreach (string path in refPaths)
                    {
                        AddAssemblyPath(path);
                    }

                    List<PortableExecutableReference> refs = new(assemblyMap.Count);
                    foreach (string path in assemblyMap.Values)
                    {
                        try
                        {
                            refs.Add(MetadataReference.CreateFromFile(path));
                        }
                        catch
                        {
                        }
                    }

                    _cachedBaseReferences = refs;
                }
            }
        }

        compiler.AddAssemblies(_cachedBaseReferences);
        compiler.AddNamespaces(new[]
        {
            "System",
            "System.Collections",
            "System.Collections.Generic",
            "System.Diagnostics",
            "System.Drawing",
            "System.Dynamic",
            "System.Globalization",
            "System.IO",
            "System.Linq",
            "System.Net",
            "System.Net.Http",
            "System.Reflection",
            "System.Runtime.CompilerServices",
            "System.Text",
            "System.Text.RegularExpressions",
            "System.Threading",
            "System.Threading.Tasks",
            "System.Timers",
            "System.Windows.Forms",
            "Microsoft.Win32",
            "Skua.Core",
            "Skua.Core.Interfaces",
            "Skua.Core.Models",
            "Skua.Core.Models.Items",
            "Skua.Core.Models.Monsters",
            "Skua.Core.Models.Players",
            "Skua.Core.Models.Quests",
            "Skua.Core.Models.Servers",
            "Skua.Core.Models.Shops",
            "Skua.Core.Models.Skills",
            "Skua.Core.Models.Auras",
            "Skua.Core.Models.Factions",
            "Skua.Core.Options",
            "Skua.Core.Utils",
            "CommunityToolkit.Mvvm.DependencyInjection",
            "Newtonsoft.Json",
            "Newtonsoft.Json.Linq",
        });
        compiler.SaveGeneratedCode = true;
        return compiler;
    }
}
