using Skua.Core.Models.Auras;

namespace Skua.Core.Interfaces.Services;

/// <summary>
/// Reads the auras on you and on your target for the Aura Tracker: who applied each one, what it gives,
/// stacks against the known limit and time left.
/// </summary>
public interface IAuraTrackerService
{
    /// <summary>
    /// Reads the auras on <paramref name="subject"/> from the game. Safe to call from any thread.
    /// </summary>
    AuraTrackerResult Read(SubjectType subject);

    /// <summary>
    /// Draws an aura icon from the game's art. Returns the edge size and ARGB pixels (size * size * 4 bytes),
    /// or null when the game has no such icon.
    /// </summary>
    (int Size, byte[] Argb)? GetIcon(string iconLink, int size);

    /// <summary>Every aura name the wiki data knows, for searching pins.</summary>
    IReadOnlyList<string> KnownAuraNames { get; }
}
