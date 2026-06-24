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
}
