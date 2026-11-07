using Skua.Core.Models;

namespace Skua.Core.Interfaces;

public interface ISettingsService
{
    void Set<T>(string key, T value);

    T? Get<T>(string key);

    T Get<T>(string key, T defaultValue);

    void Initialize(AppRole role);

    SharedSettings GetShared();

    ClientSettings GetClient();

    ManagerSettings GetManager();

    void SetApplicationVersion();

    void ReloadSettings();

    /// <summary>Re-reads the settings file; false (settings in memory kept) when it can't be read.</summary>
    bool TryReloadSettings();

    /// <summary>
    /// Reloads, changes and saves one setting as one step that no other VibeSkua process can interleave with.
    /// False, and nothing written, when the reload or the save fails. <paramref name="change"/> must return a new
    /// value rather than modify the one it is given.
    /// </summary>
    bool TryUpdate<T>(string key, T defaultValue, System.Func<T, T> change, out T result);
}
