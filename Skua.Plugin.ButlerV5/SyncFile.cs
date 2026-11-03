using System.Diagnostics;
using System.IO;
using System.Text;

namespace ButlerV5;

/// <summary>
/// State written by every account running the plugin. One file per account:
/// %APPDATA%\Skua\character_locations\{username}_butlerv5
/// </summary>
public class SyncData
{
    public string Username = "";
    public int Pid;
    public string Server = "";
    /// <summary>Actually equipped class, not the configured follow class type.</summary>
    public string ClassName = "";
    public string Map = "";
    public string Room = "1";
    public string Cell = "Enter";
    public string Pad = "Spawn";
    public bool LoggedIn;
    public bool Attacking;
    /// <summary>True when this account's AQW "Enable Goto" preference is off.</summary>
    public bool OffGoto;
    public List<string> Followers = new();

    /// <summary>Who this account is currently following ("" = nobody).</summary>
    public string Following = "";

    /// <summary>Whether a script is running on this account (busy - can't be summoned).</summary>
    public bool ScriptOn;
    public string ScriptName = "";

    /// <summary>Butler → master: a summon this account declined; the master auto-releases it.</summary>
    public string Declined = "";

    /// <summary>Whether this account is mid-park (walking home) - not summonable yet.</summary>
    public bool Parking;

    /// <summary>External player name for the latest one-shot send-all goto command.</summary>
    public string SendAllTarget = "";

    /// <summary>Unique ID so each recipient executes a broadcast goto command only once.</summary>
    public string SendAllCommand = "";

    /// <summary>Recipients selected when the broadcast goto command was issued.</summary>
    public List<string> SendAllRecipients = new();

    /// <summary>UTC ticks when the broadcast goto command was issued.</summary>
    public long SendAllIssuedUtcTicks;

    /// <summary>File last write time (UTC), populated on read to guard against PID recycling.</summary>
    public DateTime LastModified { get; set; } = DateTime.MinValue;

    public string MapWithRoom => string.IsNullOrEmpty(Room) ? Map : $"{Map}-{Room}";

    public string Serialize()
    {
        StringBuilder sb = new();
        sb.Append("username=").Append(Username).Append('\n');
        sb.Append("pid=").Append(Pid).Append('\n');
        sb.Append("server=").Append(Server).Append('\n');
        sb.Append("class=").Append(ClassName).Append('\n');
        sb.Append("map=").Append(Map).Append('\n');
        sb.Append("room=").Append(Room).Append('\n');
        sb.Append("cell=").Append(Cell).Append('\n');
        sb.Append("pad=").Append(Pad).Append('\n');
        sb.Append("loggedin=").Append(LoggedIn ? '1' : '0').Append('\n');
        sb.Append("attacking=").Append(Attacking ? '1' : '0').Append('\n');
        sb.Append("offgoto=").Append(OffGoto ? '1' : '0').Append('\n');
        sb.Append("followers=").Append(string.Join(",", Followers)).Append('\n');
        sb.Append("following=").Append(Following).Append('\n');
        sb.Append("script=").Append(ScriptOn ? '1' : '0').Append('\n');
        sb.Append("scriptname=").Append(ScriptName).Append('\n');
        sb.Append("declined=").Append(Declined).Append('\n');
        sb.Append("parking=").Append(Parking ? '1' : '0').Append('\n');
        sb.Append("sendalltarget=").Append(SendAllTarget).Append('\n');
        sb.Append("sendallcommand=").Append(SendAllCommand).Append('\n');
        sb.Append("sendallrecipients=").Append(string.Join(",", SendAllRecipients)).Append('\n');
        sb.Append("sendallissued=").Append(SendAllIssuedUtcTicks).Append('\n');
        return sb.ToString();
    }

    public static SyncData? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        SyncData data = new();
        bool sawUsername = false;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            string key = line[..eq].Trim().ToLowerInvariant();
            string value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "username": data.Username = value; sawUsername = !string.IsNullOrEmpty(value); break;
                case "pid": _ = int.TryParse(value, out data.Pid); break;
                case "server": data.Server = value; break;
                case "class": data.ClassName = value; break;
                case "map": data.Map = value.ToLowerInvariant(); break;
                case "room": data.Room = value; break;
                case "cell": data.Cell = value; break;
                case "pad": data.Pad = value; break;
                case "loggedin": data.LoggedIn = value == "1"; break;
                case "attacking": data.Attacking = value == "1"; break;
                case "offgoto": data.OffGoto = value == "1"; break;
                case "followers":
                    data.Followers = value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    break;
                case "following": data.Following = value; break;
                case "script": data.ScriptOn = value == "1"; break;
                case "scriptname": data.ScriptName = value; break;
                case "declined": data.Declined = value; break;
                case "parking": data.Parking = value == "1"; break;
                case "sendalltarget": data.SendAllTarget = value; break;
                case "sendallcommand": data.SendAllCommand = value; break;
                case "sendallrecipients":
                    data.SendAllRecipients = value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    break;
                case "sendallissued": _ = long.TryParse(value, out data.SendAllIssuedUtcTicks); break;
            }
        }

        return sawUsername ? data : null;
    }
}

public static class SyncFile
{
    public const string Suffix = "_butlerv5";

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Skua",
        "character_locations");

    public static string PathFor(string username) =>
        Path.Combine(Directory, $"{username.ToLowerInvariant()}{Suffix}");

    /// <summary>
    /// Prefers an atomic temp-file + rename (readers never see a half-written file),
    /// but Windows refuses the rename while another Skua instance has the file open -
    /// so after a few attempts it falls back to a direct shared-mode write. Readers
    /// tolerate the rare partial read (parse fails, next read half a second later wins).
    /// </summary>
    public static bool Write(SyncData data)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            string path = PathFor(data.Username);
            string temp = path + ".tmp";
            string content = data.Serialize();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    File.WriteAllText(temp, content);
                    File.Move(temp, path, overwrite: true);
                    return true;
                }
                // Replacing a file another Skua instance holds open throws
                // UnauthorizedAccessException, not just IOException.
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(30);
                }
            }

            try { File.Delete(temp); } catch { }

            using FileStream fs = new(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using StreamWriter sw = new(fs);
            sw.Write(content);
            return true;
        }
        catch
        {
            // Never let a sync write take the client down.
        }
        return false;
    }

    public static SyncData? Read(string path)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                DateTime lastWrite = File.GetLastWriteTimeUtc(path);
                using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader sr = new(fs);
                SyncData? data = SyncData.Parse(sr.ReadToEnd());
                if (data != null)
                    data.LastModified = lastWrite;
                return data;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public static SyncData? ReadUser(string username) => Read(PathFor(username));

    public static List<SyncData> ReadAll()
    {
        List<SyncData> result = new();
        try
        {
            if (!System.IO.Directory.Exists(Directory))
                return result;

            foreach (string path in System.IO.Directory.EnumerateFiles(Directory, $"*{Suffix}"))
            {
                SyncData? data = Read(path);
                if (data != null)
                    result.Add(data);
            }
        }
        catch
        {
        }
        return result;
    }

    /// <summary>
    /// An account counts as online only if its file says loggedin=1 AND the Skua process that
    /// wrote it is still running. Guards against Windows PID recycling (e.g. if the client
    /// crashed and an unrelated Windows process inherited the PID).
    /// </summary>
    public static bool IsOnline(SyncData data) =>
        data.LoggedIn && data.Pid > 0 && IsProcessAlive(data.Pid, data.LastModified > DateTime.MinValue ? data.LastModified : null);

    public static bool IsProcessAlive(int pid, DateTime? fileWriteTimeUtc = null)
    {
        try
        {
            // MUST dispose: GetProcessById returns an IDisposable Process that opens an
            // OS handle on .HasExited. This runs ~10-20x/sec across the fleet; leaving
            // them for the finalizer piles up handles (a slow handle/memory leak).
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited)
                return false;

            // Verify process name: Windows recycles PIDs after a crash. If another program
            // (e.g. RuntimeBroker, svchost, Discord) inherits the PID, it is NOT Skua.
            string name = process.ProcessName.ToLowerInvariant();
            if (!name.Contains("skua") && !name.Contains("vibeskua") && name != "dotnet")
                return false;

            // If the process started after the sync file was written, the PID was recycled to a new process.
            if (fileWriteTimeUtc.HasValue && fileWriteTimeUtc.Value > DateTime.MinValue)
            {
                try
                {
                    if (process.StartTime.ToUniversalTime() > fileWriteTimeUtc.Value.AddMinutes(1))
                        return false;
                }
                catch
                {
                    // Access denied or unprivileged query
                }
            }

            return true;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch
        {
            return false;
        }
    }
}
