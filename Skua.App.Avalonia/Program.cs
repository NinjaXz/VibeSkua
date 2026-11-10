using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Velopack;

using Skua.Core.Models;

namespace Skua.App.Avalonia;

internal static class Program
{
    private static string? _embeddedLifecycleLogPath;

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--embed", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                Directory.CreateDirectory(directory);
                _embeddedLifecycleLogPath = Path.Combine(directory, $"{Environment.ProcessId}.lifecycle.log");
                LogEmbeddedProcessEvent("Program.Main");
                AppDomain.CurrentDomain.ProcessExit += (_, _) => LogEmbeddedProcessEvent("ProcessExit");
                AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                    LogEmbeddedProcessEvent($"UnhandledException (IsTerminating={eventArgs.IsTerminating}): {eventArgs.ExceptionObject}");
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.WriteLine($"Embedded startup logging failed: {exception}");
            }
        }

        InitializeAssemblyResolvers();
        RunApp(args);
    }

    private static void LogEmbeddedProcessEvent(string eventName)
    {
        if (_embeddedLifecycleLogPath is null)
            return;

        try
        {
            File.AppendAllText(
                _embeddedLifecycleLogPath,
                $"{DateTimeOffset.UtcNow:O} | PID={Environment.ProcessId} | {eventName}{Environment.NewLine}");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.WriteLine($"Embedded process logging failed: {exception}");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    private static void InitializeAssemblyResolvers()
    {
        string assembliesDir = Path.Combine(AppContext.BaseDirectory, "Assemblies");
        HashSet<string>? assembliesSet = null;
        if (Directory.Exists(assembliesDir))
        {
            try
            {
                SetDllDirectory(assembliesDir);
                assembliesSet = new HashSet<string>(
                    Directory.EnumerateFiles(assembliesDir, "*.dll").Select(Path.GetFileName)!,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
            }
        }

        string arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
        string runtimeNativeDir = Path.Combine(AppContext.BaseDirectory, "Assemblies", "runtimes", arch, "native");
        HashSet<string>? runtimeNativeSet = null;
        if (Directory.Exists(runtimeNativeDir))
        {
            try
            {
                runtimeNativeSet = new HashSet<string>(
                    Directory.EnumerateFiles(runtimeNativeDir, "*.dll").Select(Path.GetFileName)!,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
            }
        }

        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            if (assemblyName.Name == null || assemblyName.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                return null;

            string fileName = $"{assemblyName.Name}.dll";
            if (assembliesSet != null && assembliesSet.Contains(fileName))
            {
                return context.LoadFromAssemblyPath(Path.Combine(assembliesDir, fileName));
            }

            string rootPath = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(rootPath))
                return context.LoadFromAssemblyPath(rootPath);

            return null;
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (assembly, unmanagedDllName) =>
        {
            string fileName = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? unmanagedDllName
                : $"{unmanagedDllName}.dll";

            if (assembliesSet != null && assembliesSet.Contains(fileName))
                return NativeLibrary.Load(Path.Combine(assembliesDir, fileName));

            if (runtimeNativeSet != null && runtimeNativeSet.Contains(fileName))
                return NativeLibrary.Load(Path.Combine(runtimeNativeDir, fileName));

            return IntPtr.Zero;
        };

        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            if (args.Name.Contains(".resources"))
                return null;

            Assembly? existing = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName == args.Name);
            if (existing != null)
                return existing;

            string assemblyName = new AssemblyName(args.Name).Name + ".dll";
            if (assembliesSet != null && assembliesSet.Contains(assemblyName))
            {
                return Assembly.LoadFrom(Path.Combine(assembliesDir, assemblyName));
            }

            string rootPath = Path.Combine(AppContext.BaseDirectory, assemblyName);
            if (File.Exists(rootPath))
                return Assembly.LoadFrom(rootPath);

            return null;
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApp(string[] args)
    {
        try
        {
            ClientFileSources.EnsureInitialized();
            System.Threading.Tasks.Task.Run(() => Skua.App.Avalonia.Flash.FlashUtil.PreloadSwf());
            VelopackApp.Build().Run();
            if (Skua.Shared.Avalonia.Utils.AppShortcutHelper.IsInstalled())
            {
                Skua.Shared.Avalonia.Utils.AppShortcutHelper.CreateOrUpdateShortcuts(createIfMissing: false);
            }
            Environment.CurrentDirectory = AppContext.BaseDirectory;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash_program.txt"), ex.ToString());
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Software },
                CompositionMode = new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.RedirectionSurface }
            });
#if DEBUG
        builder = builder.WithDeveloperTools();
#endif
        return builder.LogToTrace();
    }
}
