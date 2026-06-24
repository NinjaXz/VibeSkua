using System;
using Skua.Core.Models.Items;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Skua.Core.Interfaces;

public interface ILoadoutService
{
    event Action? LoadoutsChanged;
    bool IsLoggedIn { get; }
    List<LoadoutProfile> Loadouts { get; }
    bool SaveLoadout(LoadoutProfile loadout);
    /// <summary>
    /// Deletes a specified loadout profile.
    /// </summary>
    /// <param name="loadout">The loadout profile to delete.</param>
    void DeleteLoadout(LoadoutProfile loadout);
    void Refresh();
    Task<List<string>> EquipLoadoutAsync(LoadoutProfile loadout);
    LoadoutProfile CreateFromCurrentEquipped(string name);
}
