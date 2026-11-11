using Newtonsoft.Json;

namespace Skua.Core.Models.Auras;

/// <summary>
/// What skua.swf's GetAuraTracker returns for "Self" or "Target".
/// </summary>
public class AuraTrackerSnapshot
{
    [JsonProperty("target")]
    public AuraTrackerTarget? Target { get; set; }

    [JsonProperty("myId")]
    public long? MyId { get; set; }

    [JsonProperty("auras")]
    public List<AuraTrackerRecord> Auras { get; set; } = new();
}

public class AuraTrackerTarget
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("mapId")]
    public long? MapId { get; set; }

    [JsonProperty("isPlayer")]
    public bool IsPlayer { get; set; }
}

/// <summary>
/// One aura on the subject: the game's hud record joined with everyone who cast it.
/// </summary>
public class AuraTrackerRecord
{
    [JsonProperty("nam")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Stacks (all casters together; the game keeps one hud count per aura name).</summary>
    [JsonProperty("n")]
    public int Stacks { get; set; } = 1;

    /// <summary>Duration in seconds, 0 when it lasts until removed.</summary>
    [JsonProperty("dur")]
    public double Duration { get; set; }

    /// <summary>Seconds left, read in the game's own clock.</summary>
    [JsonProperty("remaining")]
    public double Remaining { get; set; }

    [JsonProperty("persist")]
    public bool Persist { get; set; }

    [JsonProperty("icon")]
    public string Icon { get; set; } = string.Empty;

    /// <summary>The server's own description, when it sent one.</summary>
    [JsonProperty("desc")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("seq")]
    public int Sequence { get; set; }

    /// <summary>Everyone who has this aura up on the subject, latest first.</summary>
    [JsonProperty("casters")]
    public List<AuraCaster> Casters { get; set; } = new();
}

public class AuraCaster
{
    /// <summary>"p" for a player, "m" for a monster.</summary>
    [JsonProperty("t")]
    public string Type { get; set; } = string.Empty;

    /// <summary>The player's entID or the monster's MonMapID.</summary>
    [JsonProperty("id")]
    public long Id { get; set; }

    /// <summary>When this caster first applied it (a re-cast keeps the first time), Unix ms.</summary>
    [JsonProperty("ts")]
    public double Timestamp { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The caster's class, for players.</summary>
    [JsonProperty("cls")]
    public string ClassName { get; set; } = string.Empty;

    [JsonProperty("me")]
    public bool IsMe { get; set; }

    [JsonIgnore]
    public bool IsMonster => Type == "m";

    [JsonIgnore]
    public string DisplayName => IsMe ? "You" : !string.IsNullOrWhiteSpace(Name) ? Name : IsMonster ? "a monster" : "a player";
}

/// <summary>Effects worth a highlight.</summary>
public enum AuraTag
{
    None,
    NoHeal,
    Taunt,
    Reckless,
    Enrage,
    Scroll
}

/// <summary>
/// An aura ready to show: what it gives, who cast it, stacks against the known limit and time left.
/// </summary>
public class AuraTrackerEntry
{
    public SubjectType Subject { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public int Stacks { get; init; }
    /// <summary>The stack limit, 0 when unknown.</summary>
    public int MaxStacks { get; init; }
    /// <summary>The most stacks seen this session.</summary>
    public int MostSeen { get; init; }
    public bool NoCap { get; init; }
    public bool ShowsStacks { get; init; }
    public bool IsFull { get; init; }
    public double Duration { get; init; }
    /// <summary>Seconds left, null when it lasts until removed.</summary>
    public double? Remaining { get; init; }
    /// <summary>What the aura gives (one short line when known).</summary>
    public string Effect { get; init; } = string.Empty;
    /// <summary>Where the effect line comes from ("Flight (Abyssal Angel)", "Scroll of Decay").</summary>
    public string EffectSource { get; init; } = string.Empty;
    public AuraTag Tag { get; init; }
    public string TagText { get; init; } = string.Empty;
    public string TagNote { get; init; } = string.Empty;
    public IReadOnlyList<AuraCaster> Casters { get; init; } = Array.Empty<AuraCaster>();
    /// <summary>"by Name +2", empty for your own buffs on yourself.</summary>
    public string ByLine { get; init; } = string.Empty;
    public int Sequence { get; init; }

    /// <summary>The effect line split into colored pills.</summary>
    public IReadOnlyList<EffectPill> Pills { get; init; } = Array.Empty<EffectPill>();

    /// <summary>Which filter chip it belongs to.</summary>
    public AuraCategory Category { get; init; }

    /// <summary>Its priority group.</summary>
    public AuraPriorityGroup Group { get; init; }
}

public class AuraTrackerResult
{
    public SubjectType Subject { get; init; }
    public AuraTrackerTarget? Target { get; init; }
    public bool HasTarget { get; init; }
    public IReadOnlyList<AuraTrackerEntry> Auras { get; init; } = Array.Empty<AuraTrackerEntry>();
}
