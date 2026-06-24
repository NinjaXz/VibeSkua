using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Windows;
using Velopack;

namespace Skua.App.WPF;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        AppDomain currentDomain = AppDomain.CurrentDomain;
        currentDomain.AssemblyResolve += new ResolveEventHandler(ResolveAssemblies);
        currentDomain.UnhandledException += CurrentDomain_UnhandledException;

        RunVelopack();

        ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;

        App app = new();
        app.InitializeComponent();
        app.Run();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunVelopack()
    {
        try
        {
            VelopackApp.Build()
                .OnAfterInstallFastCallback((v) =>
                {
                    try
                    {
                        var shortcuts = new Velopack.Windows.Shortcuts();
                        shortcuts.CreateShortcut("Skua.Manager.exe", Velopack.Windows.ShortcutLocation.Desktop | Velopack.Windows.ShortcutLocation.StartMenuRoot, false, null, null);
                    }
                    catch { }
                })
                .Run();
        }
        catch { }
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception ex = (Exception)e.ExceptionObject;
        MessageBox.Show($"Application Crash.\r\nMessage: {ex.Message}\r\nStackTrace: {ex.StackTrace}", "Application");
    }

    private static Assembly? ResolveAssemblies(object? sender, ResolveEventArgs args)
    {
        if (args.Name.Contains(".resources"))
            return null;

        Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName == args.Name);
        if (assembly != null)
            return assembly;

        string assemblyName = new AssemblyName(args.Name).Name + ".dll";
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "Assemblies", assemblyName);
        if (!File.Exists(assemblyPath))
        {
            assemblyPath = Path.Combine(AppContext.BaseDirectory, assemblyName);
            return File.Exists(assemblyPath) ? Assembly.LoadFrom(assemblyPath) : null;
        }
        return Assembly.LoadFrom(assemblyPath);
    }
}