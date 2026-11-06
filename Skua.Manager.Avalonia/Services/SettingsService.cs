using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Services;

namespace Skua.Manager.Avalonia.Services;

public class SettingsService : ISettingsService
{
    private readonly UnifiedSettingsService _unifiedService;

    public SettingsService()
    {
        _unifiedService = new UnifiedSettingsService();
        _unifiedService.Initialize(AppRole.Manager);
    }

    public T? Get<T>(string key) => _unifiedService.Get<T>(key);

    public T Get<T>(string key, T defaultValue) => _unifiedService.Get(key, defaultValue);

    public void Set<T>(string key, T value) => _unifiedService.Set(key, value);

    public void Initialize(AppRole role) => _unifiedService.Initialize(role);

    public SharedSettings GetShared() => _unifiedService.GetShared();

    public ClientSettings GetClient() => _unifiedService.GetClient();

    public ManagerSettings GetManager() => _unifiedService.GetManager();

    public void SetApplicationVersion() => _unifiedService.SetApplicationVersion();

    public void ReloadSettings() => _unifiedService.ReloadSettings();

    public bool TryReloadSettings() => _unifiedService.TryReloadSettings();

    public bool TryUpdate<T>(string key, T defaultValue, System.Func<T, T> change, out T result) =>
        _unifiedService.TryUpdate(key, defaultValue, change, out result);
}
