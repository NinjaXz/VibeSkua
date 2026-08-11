using System.Reflection;

namespace Skua;

public static class AppInfo
{
    public static string Version { get; } = GetVersion();

    public static string Title => $"VibeSkua v{Version}";

    private static string GetVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+')[0];

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
