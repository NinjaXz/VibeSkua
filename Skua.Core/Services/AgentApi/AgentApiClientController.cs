using System.Diagnostics;
using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// The real view of the running process, and the only place that starts another
/// client.
///
/// The tabbed client keeps every character in its own child process, so the
/// process that answers HTTP is usually the tab host with no character of its
/// own. The host finds its children by the port files they write, and it starts
/// a new client the same way the mobile remote control does: a fresh process
/// with the account, its stored password and a server.
/// </summary>
public sealed class AgentApiClientController : IClientController
{
    /// <summary>Folder where each client process publishes the port its endpoint answers on.</summary>
    public const string WorkerFolder = "VibeSkuaTabs";

    /// <summary>File name suffix, so the folder also holds the remote-control files.</summary>
    public const string WorkerFileSuffix = ".agent.txt";

    private readonly ISettingsService _settings;
    private readonly string _workerDir;

    public AgentApiClientController(ISettingsService settingsService, string? workerDir = null)
    {
        _settings = settingsService;
        _workerDir = string.IsNullOrWhiteSpace(workerDir) ? WorkerDir : workerDir!;
    }

    public static string WorkerDir => Path.Combine(Path.GetTempPath(), WorkerFolder);

    /// <summary>
    /// The app sets this for every process it starts as an embedded tab, so a
    /// child knows it holds the game and the host knows it does not.
    /// </summary>
    public static bool IsEmbedChild =>
        string.Equals(Environment.GetEnvironmentVariable("VIBESKUA_EMBED"), "1", StringComparison.Ordinal);

    /// <summary>Where this process writes its own port file.</summary>
    public static string OwnWorkerFile => Path.Combine(WorkerDir, Environment.ProcessId + WorkerFileSuffix);

    public string Role => IsEmbedChild ? "child" : "host";

    public bool CanHostGame => IsEmbedChild;

    public IReadOnlyList<ClientWorker> Workers => ReadWorkers();

    /// <summary>
    /// Starts a client process for a saved account. The password is the one the
    /// client already stores for that name, so an agent never carries a secret.
    /// </summary>
    public string? Start(string username, string? server)
    {
        if (IsEmbedChild)
            return "client_already_running: this process holds a character already";

        Dictionary<string, AccountData>? accounts = _settings.Get<Dictionary<string, AccountData>>("ManagedAccounts");
        if (accounts is null || !accounts.TryGetValue(username, out AccountData? account))
            return $"account_not_found: {username} is not in the saved accounts";

        string password = account.Password ?? string.Empty;
        if (password.StartsWith("enc:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                password = Skua.Core.Utils.CryptoUtils.Decrypt(password);
            }
            catch (Exception ex)
            {
                return $"account_unlock_failed: {ex.Message}";
            }
        }

        string chosen = string.IsNullOrWhiteSpace(server) ? _settings.Get("LastServer", "Twilly") : server!;

        try
        {
            using Process process = Process.Start(BuildStart(username, password, chosen))
                ?? throw new InvalidOperationException("the client process did not start");
            return null;
        }
        catch (Exception ex)
        {
            return $"client_start_failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Kills a client process and deletes the port file it wrote, so a wedged child
    /// stops showing up as a worker. The tree is killed because the app relaunches
    /// itself as an embedded tab, and the outer process would restart the child.
    /// </summary>
    public string? Stop(int pid)
    {
        if (pid <= 0)
            return "bad_pid: pid must be greater than zero";

        if (pid == Environment.ProcessId)
            return "cannot_stop_self: this process answers the agent API";

        try
        {
            using Process process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5000))
                return "client_stop_timeout: pid " + pid + " did not exit within 5 seconds";
        }
        catch (ArgumentException)
        {
            // Already gone: the goal of the call is met, so clean the file and say yes.
        }
        catch (Exception ex)
        {
            return "client_stop_failed: " + ex.GetType().Name + ": " + ex.Message;
        }

        try
        {
            File.Delete(Path.Combine(_workerDir, pid + WorkerFileSuffix));
        }
        catch
        {
            // A file another process already removed is the outcome we wanted anyway.
        }

        return null;
    }

    /// <summary>The same shape the remote control service uses for a launched account.</summary>
    private static ProcessStartInfo BuildStart(string username, string password, string server)
    {
        string exe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "VibeSkua.exe");

        ProcessStartInfo start = new()
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        };

        start.FileName = exe;
        if (exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) || exe.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "VibeSkua.dll"));

        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(username);
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add(password);
        start.ArgumentList.Add("-s");
        start.ArgumentList.Add(server);
        return start;
    }

    private List<ClientWorker> ReadWorkers()
    {
        List<ClientWorker> rows = new();
        try
        {
            if (!Directory.Exists(_workerDir))
                return rows;

            foreach (string file in Directory.GetFiles(_workerDir, "*" + WorkerFileSuffix))
            {
                try
                {
                    string name = Path.GetFileName(file);
                    int dot = name.IndexOf('.');
                    if (dot <= 0 || !int.TryParse(name[..dot], out int pid) || pid <= 0)
                        continue;
                    if (!int.TryParse(File.ReadAllText(file).Trim(), out int port) || port <= 0)
                        continue;
                    if (!IsAlive(pid))
                        continue;
                    rows.Add(new ClientWorker(pid, port));
                }
                catch
                {
                    // A child may write or delete its file while this reads it.
                }
            }
        }
        catch
        {
            // A temp folder this user cannot read is a valid empty answer too.
        }

        rows.Sort((left, right) => left.Pid.CompareTo(right.Pid));
        return rows;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
