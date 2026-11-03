using Skua.Core.Models.Items;

namespace Skua.Core.Interfaces;

/// <summary>
/// Represents the inventory.
/// </summary>
/// <remarks>Implementations of this interface provide functionality for storing and equipping script items. This
/// interface extends <see cref="ICanEquip"/>, indicating that it supports equipping operations.</remarks>
public interface IScriptInventory : ICanEquip
{
    /// <summary>Whether the game provides separate bag, misc, class and house pools.</summary>
    bool HasCategories { get; }

    /// <summary>The current misc capacity, including server overrides; zero on older games.</summary>
    int MiscSlots { get; }
    int MiscUsedSlots { get; }
    int MiscFreeSlots { get; }

    /// <summary>Returns bag, misc, class or house. Older games return bag.</summary>
    string GetPool(ItemBase item);

    /// <summary>Checks the destination pool, existing stack and purchase materials.
    /// Quantity is the amount purchased, not the desired final total.</summary>
    bool HasSpaceFor(ItemBase item, int quantity = 1);

    /// <summary>Classes cannot be deposited on games with category inventories.</summary>
    bool CanBank(InventoryItem item);

    /// <summary>Checks the current character's local favorite mark.</summary>
    bool IsFavorited(int itemId);

    /// <summary>Checks whether the specified inventory item is favorited.</summary>
    bool IsFavorited(InventoryItem item) => IsFavorited(item.ID);

    /// <summary>Gets the current inventory items marked as favorites.</summary>
    IEnumerable<InventoryItem> FavoritedItems => Items.Where(item => IsFavorited(item.ID)).ToArray();

    /// <summary>Gets the current inventory items that are not marked as favorites.</summary>
    IEnumerable<InventoryItem> UnfavoritedItems => Items.Where(item => !IsFavorited(item.ID)).ToArray();

    /// <summary>
    /// Transfers eligible inventory items to the bank.
    /// </summary>
    /// <param name="skipFavorited">Whether favorited items should be left in the inventory.</param>
    /// <param name="skipEquipped">Whether equipped items should be left in the inventory.</param>
    /// <returns>The number of items successfully transferred.</returns>
    int BankAllItems(bool skipFavorited = true, bool skipEquipped = true)
    {
        int banked = 0;
        InventoryItem[] items = Items.ToArray();

        foreach (InventoryItem item in items)
        {
            if (!CanBank(item) || (skipEquipped && item.Equipped) || (skipFavorited && IsFavorited(item.ID)))
                continue;

            if (ToBank(item))
                banked++;
        }

        return banked;
    }
}