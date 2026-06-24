using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;

namespace Skua.Core.Skills;

public interface IClassRoutine
{
    int[] Sequence { get; }
    bool? ShouldUse(int skillIndex, IScriptPlayer p, IScriptSelfAuras s, IScriptTargetAuras t);
}

public class DefaultRoutine : IClassRoutine
{
    public int[] Sequence { get; }
    public DefaultRoutine(int[] sequence) { Sequence = sequence; }
    public virtual bool? ShouldUse(int skillIndex, IScriptPlayer p, IScriptSelfAuras s, IScriptTargetAuras t) => true;
}

public class MasterSkillProvider : ISkillProvider
{
    private IScriptPlayer P;
    private IScriptSelfAuras S;
    private IScriptTargetAuras T;
    private IScriptCombat C;
    private string N = "";
    private int X = 0;
    private IClassRoutine _activeRoutine;

    public bool ResetOnTarget { get; set; } = true;
    public int SkillCount => 4;

    public void Init(IScriptPlayer p, IScriptSelfAuras s, IScriptTargetAuras t, IScriptCombat c, IFlashUtil f)
    {
        P = p; S = s; T = t; C = c;
        Configure();
    }

    private void Configure()
    {
        N = IScriptInterface.Instance.Player.CurrentClass?.Name?.ToLower() ?? "";
        _activeRoutine = ClassRoutineCache.GetRoutine(N);
        X = 0;
    }

    public void OnTargetReset() { if (ResetOnTarget) X = 0; }
    public void Stop() { C.CancelAutoAttack(); C.CancelTarget(); X = 0; }
    public void OnPlayerDeath() { X = 0; }
    public void Save(string file) { }
    public void Load(string file) { }

    public (int, int) GetNextSkill()
    {
        var b = IScriptInterface.Instance;
        if (_activeRoutine == null || _activeRoutine.Sequence == null || _activeRoutine.Sequence.Length == 0)
            return (0, 0);

        // Aggressive Priority Loop: Scan from highest priority to lowest EVERY tick
        for (int i = 0; i < _activeRoutine.Sequence.Length; i++)
        {
            int k = _activeRoutine.Sequence[i];
            
            if (b.Skills.CanUseSkill(k))
            {
                if (_activeRoutine.ShouldUse(k, P, S, T) == true)
                {
                    return (k, k);
                }
            }
        }
        
        // If all class skills are on cooldown/denied, return Auto-Attack to prevent thread-sleeping
        return (0, 0);
    }

    public bool? ShouldUseSkill(int skillIndex, bool canUse)
    {
        return _activeRoutine?.ShouldUse(skillIndex, P, S, T) ?? true;
    }
}

public static class ClassRoutineCache
{
    private static readonly Dictionary<string, int[]> Sequences = new()
    {
        { "void highlord", new[]{3,1,2,4} }, { "void tracker", new[]{3,1,2,4} }, { "hobo highlord", new[]{3,1,2,4} }, { "frostval barbarian", new[]{3,1,2,4} }, { "dragonlord", new[]{3,1,2,4} }, { "shadowflame dragonlord", new[]{3,1,2,4} }, { "legendary hero", new[]{3,1,2,4} }, { "dark legendary hero", new[]{3,1,2,4} }, { "beastmaster", new[]{3,1,2,4} }, { "not a mod", new[]{3,1,2,4} }, { "simple class", new[]{3,1,2,4} }, { "infinity knight", new[]{3,1,2,4} }, { "interstellar knight", new[]{3,1,2,4} },
        { "legion revenant", new[]{3,4,1,2} }, { "archfiend", new[]{3,4,1,2} }, { "verus doomknight", new[]{3,4,1,2} }, { "abyssal angel", new[]{3,4,1,2} }, { "abyssal angel's shadow", new[]{3,4,1,2} }, { "shadow ripper", new[]{3,4,1,2} }, { "witch", new[]{3,4,1,2} },
        { "archpaladin", new[]{1,3,2,4} }, { "cryomancer", new[]{1,3,2,4} }, { "dark cryomancer", new[]{1,3,2,4} }, { "sakura cryomancer", new[]{1,3,2,4} }, { "necromancer", new[]{1,3,2,4} }, { "grim necromancer", new[]{1,3,2,4} }, { "nu metal necro", new[]{1,3,2,4} }, { "dark metal necro", new[]{1,3,2,4} }, { "doom metal necro", new[]{1,3,2,4} }, { "heavy metal necro", new[]{1,3,2,4} }, { "neo metal necro", new[]{1,3,2,4} }, { "acolyte", new[]{1,3,2,4} }, { "healer", new[]{1,3,2,4} }, { "battle healer", new[]{1,3,2,4} }, { "oracle", new[]{1,3,2,4} }, { "drakkar knight", new[]{1,3,2,4} }, { "draco knight", new[]{1,3,2,4} },
        { "lightcaster", new[]{1,3,4,2} }, { "lightmage", new[]{1,3,4,2} }, { "lord of order", new[]{1,3,4,2} }, { "mindbreaker", new[]{1,3,4,2} }, { "psionic mindbreaker", new[]{1,3,4,2} }, { "troubador of love", new[]{1,3,4,2} }, { "bard", new[]{1,3,4,2} }, { "battle bard", new[]{1,3,4,2} },
        { "stonecrusher", new[]{2,3,4,1} }, { "infinity titan", new[]{2,3,4,1} }, { "blood titan", new[]{2,3,4,1} }, { "frostblood titan", new[]{2,3,4,1} }, { "mechajouster", new[]{2,3,4,1} },
        { "chaos avenger", new[]{4,2,3,1} }, { "chaos champion prime", new[]{4,2,3,1} }, { "chaos shaper", new[]{4,2,3,1} }, { "chaos slayer berserker", new[]{4,2,3,1} }, { "chaos slayer cleric", new[]{4,2,3,1} }, { "chaos slayer mystic", new[]{4,2,3,1} }, { "chaos slayer thief", new[]{4,2,3,1} },
        { "dragon of time", new[]{4,2,1,3} }, { "archmage", new[]{4,2,1,3} }, { "shaman", new[]{4,2,1,3} }, { "evolved shaman", new[]{4,2,1,3} }, { "scarlet sorceress", new[]{4,2,1,3} }, { "blood sorceress", new[]{4,2,1,3} }, { "troll spellsmith", new[]{4,2,1,3} }, { "legion doomknight", new[]{4,2,1,3} }, { "classic legion doomknight", new[]{4,2,1,3} }, { "timeless dark caster", new[]{4,2,1,3} }, { "infinite legion dark caster", new[]{4,2,1,3} }, { "dark harbinger", new[]{4,2,1,3} }, { "exalted harbinger", new[]{4,2,1,3} }, { "soul cleaver", new[]{4,2,1,3} }, { "exalted soul cleaver", new[]{4,2,1,3} }, { "classic soul cleaver", new[]{4,2,1,3} }, { "dark caster", new[]{4,2,1,3} }, { "arcane dark caster", new[]{4,2,1,3} }, { "mystical dark caster", new[]{4,2,1,3} }, { "immortal dark caster", new[]{4,2,1,3} }, { "evolved dark caster", new[]{4,2,1,3} }, { "lich", new[]{4,2,1,3} }, { "king's echo", new[]{4,2,1,3} },
        { "blaze binder", new[]{1,4,3,2} }, { "pyromancer", new[]{1,4,3,2} }, { "firelord summoner", new[]{1,4,3,2} }, { "flame dragon warrior", new[]{1,4,3,2} }, { "scion of flames", new[]{1,4,3,2} }, { "mage", new[]{1,4,3,2} }, { "sorcerer", new[]{1,4,3,2} }, { "battlemage", new[]{1,4,3,2} }, { "dark battlemage", new[]{1,4,3,2} }, { "royal battlemage", new[]{1,4,3,2} }, { "battlemage of love", new[]{1,4,3,2} }, { "pink romancer", new[]{1,4,3,2} }, { "pinkomancer", new[]{1,4,3,2} }, { "shadow rocker", new[]{1,4,3,2} },
        { "vampire lord", new[]{4,3,1,2} }, { "royal vampire lord", new[]{4,3,1,2} }, { "enchanted vampire lord", new[]{4,3,1,2} }, { "vampire", new[]{4,3,1,2} }, { "lycan", new[]{4,3,1,2} }, { "alpha doommega", new[]{4,3,1,2} }, { "alpha omega", new[]{4,3,1,2} }, { "blood ancient", new[]{4,3,1,2} }, { "elemental dracomancer", new[]{4,3,1,2} }, { "grunge rocker", new[]{4,3,1,2} }, { "heavy metal rockstar", new[]{4,3,1,2} },
        { "yami no ronin", new[]{2,1,4,3} }, { "arachnomancer", new[]{2,1,4,3} }, { "assassin", new[]{2,1,4,3} }, { "ninja", new[]{2,1,4,3} }, { "classic ninja", new[]{2,1,4,3} }, { "ninja warrior", new[]{2,1,4,3} }, { "chunin", new[]{2,1,4,3} }, { "imperial chunin", new[]{2,1,4,3} }, { "dragon shinobi", new[]{2,1,4,3} }, { "dragon soul shinobi", new[]{2,1,4,3} }, { "shadow dragon shinobi", new[]{2,1,4,3} },
        { "chronoassassin", new[]{4,1,2,3} }, { "thief of hours", new[]{4,1,2,3} }, { "doomknight", new[]{4,1,2,3} }, { "doomknight overlord", new[]{4,1,2,3} }, { "classic doomknight", new[]{4,1,2,3} }, { "deathknight", new[]{4,1,2,3} }, { "deathknight lord", new[]{4,1,2,3} }, { "shadowstalker of time", new[]{4,1,2,3} }, { "shadowwalker of time", new[]{4,1,2,3} }, { "shadowweaver of time", new[]{4,1,2,3} }, { "timekeeper", new[]{4,1,2,3} }, { "timekiller", new[]{4,1,2,3} }, { "chronomancer", new[]{4,1,2,3} }, { "chronomancer prime", new[]{4,1,2,3} }, { "corrupted chronomancer", new[]{4,1,2,3} }, { "underworld chronomancer", new[]{4,1,2,3} }, { "overworld chronomancer", new[]{4,1,2,3} }, { "timeless chronomancer", new[]{4,1,2,3} }, { "immortal chronomancer", new[]{4,1,2,3} }, { "eternal chronomancer", new[]{4,1,2,3} }, { "continuum chronomancer", new[]{4,1,2,3} }, { "empyrean chronomancer", new[]{4,1,2,3} }, { "quantum chronomancer", new[]{4,1,2,3} }, { "obsidian paladin chronomancer", new[]{4,1,2,3} }, { "phantasm chronomancer", new[]{4,1,2,3} }, { "phantom chronomancer", new[]{4,1,2,3} }, { "paladin chronomancer", new[]{4,1,2,3} }, { "chrono chaorruptor", new[]{4,1,2,3} }, { "chrono commandant", new[]{4,1,2,3} }, { "chrono dataknight", new[]{4,1,2,3} }, { "chrono dragonknight", new[]{4,1,2,3} }, { "chrono shadowhunter", new[]{4,1,2,3} }, { "chrono shadowslayer", new[]{4,1,2,3} }, { "chronocommander", new[]{4,1,2,3} }, { "chronocorruptor", new[]{4,1,2,3} }, { "necrotic chronomancer", new[]{4,1,2,3} }, { "nechronomancer", new[]{4,1,2,3} },
        { "glacial berserker", new[]{2,1,3,4} }, { "glaceran warlord", new[]{2,1,3,4} }, { "dark glaceran warlord", new[]{2,1,3,4} }, { "savage glaceran warlord", new[]{2,1,3,4} }, { "glacial warlord", new[]{2,1,3,4} }, { "berserker", new[]{2,1,3,4} }, { "beta berserker", new[]{2,1,3,4} }, { "brutal berserker", new[]{2,1,3,4} }, { "dark chaos berserker", new[]{2,1,3,4} },
        { "eternal inversionist", new[]{2,3,1,4} }, { "the collector", new[]{2,3,1,4} }, { "dark lord", new[]{2,3,1,4} }, { "daimon", new[]{2,3,1,4} },
        { "rogue", new[]{1,2,3,4} }, { "pirate", new[]{1,2,3,4} }, { "alpha pirate", new[]{1,2,3,4} }, { "classic pirate", new[]{1,2,3,4} }, { "classic alpha pirate", new[]{1,2,3,4} }, { "barber", new[]{1,2,3,4} }, { "classic barber", new[]{1,2,3,4} }, { "leprechaun", new[]{1,2,3,4} }, { "undead leperchaun", new[]{1,2,3,4} }, { "unlucky leperchaun", new[]{1,2,3,4} }, { "great thief", new[]{1,2,3,4} }, { "naval commander", new[]{1,2,3,4} }, { "heroic naval commander", new[]{1,2,3,4} }, { "highseas commander", new[]{1,2,3,4} }, { "legendary naval commander", new[]{1,2,3,4} }, { "renegade", new[]{1,2,3,4} },
        { "warrior", new[]{1,2,3,4} }, { "mercenary", new[]{1,2,3,4} }, { "beast warrior", new[]{1,2,3,4} }, { "dragonslayer", new[]{1,2,3,4} }, { "dragonslayer general", new[]{1,2,3,4} }, { "paladin", new[]{1,2,3,4} }, { "classic paladin", new[]{1,2,3,4} }, { "legion paladin", new[]{1,2,3,4} }, { "silver paladin", new[]{1,2,3,4} }, { "paladin highlord", new[]{1,2,3,4} }, { "paladinslayer", new[]{1,2,3,4} }, { "undeadslayer", new[]{1,2,3,4} }, { "defender", new[]{1,2,3,4} }, { "classic defender", new[]{1,2,3,4} }, { "guardian", new[]{1,2,3,4} }, { "classic guardian", new[]{1,2,3,4} }, { "drakel warlord", new[]{1,2,3,4} }, { "warlord", new[]{1,2,3,4} }, { "warriorscythe general", new[]{1,2,3,4} }, { "master martial artist", new[]{1,2,3,4} }, { "martial artist", new[]{1,2,3,4} },
        { "rustbucket", new[]{1,2,3,4} }, { "enforcer", new[]{1,2,3,4} }, { "protosartorium", new[]{1,2,3,4} }, { "clawsuit", new[]{1,2,3,4} }, { "evolved clawsuit", new[]{1,2,3,4} }, { "prismatic clawsuit", new[]{1,2,3,4} }, { "star captain", new[]{1,2,3,4} }, { "starlord", new[]{1,2,3,4} }, { "skycharged grenadier", new[]{1,2,3,4} }, { "skyguard grenadier", new[]{1,2,3,4} }
    };

    private static readonly List<(Func<string, bool> matcher, Func<string, int[], IClassRoutine> factory)> LogicMappings = new()
    {
        (n => n.Contains("void highlord") || n.Contains("void tracker") || n.Contains("hobo highlord"), (n, seq) => new VoidHighlordRoutine(seq)),
        (n => n.Contains("legion revenant"), (n, seq) => new LegionRevenantRoutine(seq)),
        (n => n.Contains("archfiend"), (n, seq) => new ArchfiendRoutine(seq)),
        (n => n.Contains("verus doomknight"), (n, seq) => new VerusDoomKnightRoutine(seq)),
        (n => n.Contains("chaos avenger") || n.Contains("chaos champion prime"), (n, seq) => new ChaosAvengerRoutine(seq)),
        (n => n.Contains("dragon of time"), (n, seq) => new DragonOfTimeRoutine(seq)),
        (n => n.Contains("archmage"), (n, seq) => new ArchmageRoutine(seq)),
        (n => n.Contains("archpaladin"), (n, seq) => new ArchpaladinRoutine(seq)),
        (n => n.Contains("stonecrusher") || n.Contains("infinity titan"), (n, seq) => new StonecrusherRoutine(seq)),
        (n => n.Contains("yami no ronin"), (n, seq) => new YamiNoRoninRoutine(seq)),
        (n => n.Contains("chronoassassin") || n.Contains("thief of hours"), (n, seq) => new ChronoAssassinRoutine(seq)),
        (n => n.Contains("shaman") || n.Contains("evolved shaman"), (n, seq) => new ShamanRoutine(seq)),
        (n => n.Contains("scarlet sorceress") || n.Contains("blood sorceress"), (n, seq) => new ScarletSorceressRoutine(seq)),
        (n => n.Contains("necrotic chronomancer") || n.Contains("nechronomancer"), (n, seq) => new NechronomancerRoutine(seq)),
        (n => n.Contains("glacial berserker") || n.Contains("glaceran warlord") || n.Contains("glacial warlord"), (n, seq) => new GlacialBerserkerRoutine(seq)),
        (n => n.Contains("blood titan") || n.Contains("frostblood titan"), (n, seq) => new BloodTitanRoutine(seq)),
        (n => n.Contains("arachnomancer"), (n, seq) => new ArachnomancerRoutine(seq)),
        (n => n.Contains("vindicator of they") || n.Contains("hollowborn vindicator"), (n, seq) => new VindicatorOfTheyRoutine(seq)),
        (n => n.Contains("necromancer") || n.Contains("pinkomancer"), (n, seq) => new NecromancerRoutine(seq)),
        (n => n.Contains("paladin") || n.Contains("silver paladin"), (n, seq) => new PaladinRoutine(n, seq)),
        (n => n.Contains("chronomancer") || n.Contains("time"), (n, seq) => new ChronomancerRoutine(n, seq)),
        (n => n.Contains("chaos slayer") || n.Contains("dark chaos berserker"), (n, seq) => new ChaosSlayerRoutine(seq)),
        (n => n.Contains("rustbucket") || n.Contains("enforcer") || n.Contains("protosartorium"), (n, seq) => new RustbucketRoutine(seq)),
        (n => n.Contains("naval commander") || n.Contains("highseas commander") || n.Contains("pirate") || n.Contains("alpha pirate"), (n, seq) => new NavalCommanderRoutine(seq)),
        (n => n.Contains("dragon shinobi"), (n, seq) => new DragonShinobiRoutine(seq)),
        (n => n.Contains("great thief"), (n, seq) => new GreatThiefRoutine(seq)),
        (n => n.Contains("lich"), (n, seq) => new LichRoutine(seq)),
        (n => n.Contains("master of moglins"), (n, seq) => new MasterOfMoglinsRoutine(seq))
    };

    public static IClassRoutine GetRoutine(string className)
    {
        int[] seq = Sequences.TryGetValue(className, out var res) ? res : new[] { 1, 2, 3, 4 };
        
        foreach (var mapping in LogicMappings)
        {
            if (mapping.matcher(className))
                return mapping.factory(className, seq);
        }
        return new DefaultRoutine(seq);
    }
}

public class VoidHighlordRoutine : DefaultRoutine {
    public VoidHighlordRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 1) return (H <= 0.50 && S.GetAura("Unshackled") != null) || (H <= 0.90 && S.GetAura("Unshackled") == null && (S.GetAura("Shackled") == null || S.GetAura("Shackled").RemainingTime <= 2));
        if (I == 3) return (H > 0.90 && S.GetAura("Shackled") != null) || (H > 0.50 && S.GetAura("Shackled") == null && (S.GetAura("Unshackled") == null || S.GetAura("Unshackled").RemainingTime <= 2));
        return true;
    }
}
public class LegionRevenantRoutine : DefaultRoutine {
    public LegionRevenantRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 3) return true;
        if (I == 4) return S.GetAura("Depths of Tartarus") == null;
        return true;
    }
}
public class ArchfiendRoutine : DefaultRoutine {
    public ArchfiendRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 4) return S.GetAura("Fiend Frenzy") == null;
        return true;
    }
}
public class VerusDoomKnightRoutine : DefaultRoutine {
    public VerusDoomKnightRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 4) return S.GetAura("Unleashed Doom") == null && S.GetAura("Doom")?.Value >= 10;
        if (I == 1) return S.GetAura("Unleashed Doom") != null;
        return true;
    }
}
public class ChaosAvengerRoutine : DefaultRoutine {
    public ChaosAvengerRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 4) return S.GetAura("Chaotic Armor") != null;
        if (I == 3) return H <= 0.80;
        return true;
    }
}
public class DragonOfTimeRoutine : DefaultRoutine {
    public DragonOfTimeRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 4) return H >= 0.85;
        if (I == 2) return H <= 0.60 || S.GetAura("Convergence") != null;
        return true;
    }
}
public class ArchmageRoutine : DefaultRoutine {
    public ArchmageRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double M = P.MaxMana > 0 ? (P.Mana / (double)P.MaxMana) : 0;
        if (I == 4) return S.GetAura("Corporeal Ascension") == null;
        if (I == 2) return M < 0.35;
        return true;
    }
}
public class ArchpaladinRoutine : DefaultRoutine {
    public ArchpaladinRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 4) return T.GetAura("Commandment")?.Value >= 50;
        if (I == 2) return H < 0.70;
        return true;
    }
}
public class StonecrusherRoutine : DefaultRoutine {
    public StonecrusherRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 4) return S.GetAura("Magnitude") != null;
        return true;
    }
}
public class YamiNoRoninRoutine : DefaultRoutine {
    public YamiNoRoninRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 4) return S.GetAura("Yami")?.Value >= 4;
        return true;
    }
}
public class ChronoAssassinRoutine : DefaultRoutine {
    public ChronoAssassinRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double M = P.MaxMana > 0 ? (P.Mana / (double)P.MaxMana) : 0;
        if (I == 4) return S.GetAura("Temporal Dodge") == null;
        if (I == 2) return M >= 0.3;
        return true;
    }
}
public class ShamanRoutine : DefaultRoutine {
    public ShamanRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 4) return S.GetAura("Elemental Embrace") == null || S.GetAura("Elemental Embrace").RemainingTime <= 2;
        if (I == 2) return H < 0.70;
        return true;
    }
}
public class ScarletSorceressRoutine : DefaultRoutine {
    public ScarletSorceressRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 4) return S.GetAura("Crimson Ritual") == null || S.GetAura("Crimson Ritual").Value < 5 || S.GetAura("Crimson Ritual").RemainingTime <= 2;
        if (I == 3) return H < 0.60;
        return true;
    }
}
public class NechronomancerRoutine : DefaultRoutine {
    public NechronomancerRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 4) return H <= 0.50 && T.GetAura("Chaos Rift")?.Value >= 1;
        return true;
    }
}
public class GlacialBerserkerRoutine : DefaultRoutine {
    public GlacialBerserkerRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 3) return S.GetAura("Ancestral Presence") != null;
        return true;
    }
}
public class BloodTitanRoutine : DefaultRoutine {
    public BloodTitanRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 4 || I == 3) return S.GetAura("Titan's Bloodline") == null;
        return H >= 0.70 || S.GetAura("Life Drinker") != null;
    }
}
public class ArachnomancerRoutine : DefaultRoutine {
    public ArachnomancerRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 2) return H < 0.60;
        return true;
    }
}
public class VindicatorOfTheyRoutine : DefaultRoutine {
    public VindicatorOfTheyRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double M = P.MaxMana > 0 ? (P.Mana / (double)P.MaxMana) : 0;
        if (I == 3) return M > 0.40;
        if (I == 4) return S.GetAura("Massive Strike") != null;
        return true;
    }
}
public class NecromancerRoutine : DefaultRoutine {
    public NecromancerRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 1) return S.GetAura("Summon Minion") == null;
        if (I == 4) return H > 0.40;
        return true;
    }
}
public class PaladinRoutine : DefaultRoutine {
    private readonly string _className;
    public PaladinRoutine(string className, int[] sequence) : base(sequence) { _className = className; }
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (_className.Contains("legion paladin") && I == 2) return S.GetAura("Soul")?.Value >= 10;
        if (I == 1) return H < 0.80;
        if (I == 4) return S.GetAura("Aegis") == null;
        return true;
    }
}
public class ChronomancerRoutine : DefaultRoutine {
    private readonly string _className;
    public ChronomancerRoutine(string className, int[] sequence) : base(sequence) { _className = className; }
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 4) return T.GetAura("Temporal Rift")?.Value >= 4;
        if (_className.Contains("corrupted chronomancer") || _className.Contains("timeless chronomancer")) {
            if (I == 2) return H < 0.50; 
        }
        return true;
    }
}
public class ChaosSlayerRoutine : DefaultRoutine {
    public ChaosSlayerRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        if (I == 4 && T.GetAura("Courageous") != null) return false;
        if (I == 4) return S.GetAura("Chaotic Defense") == null;
        if (I == 2) return S.GetAura("Surge") == null && H < 0.60;
        return true;
    }
}
public class RustbucketRoutine : DefaultRoutine {
    public RustbucketRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 3) return S.GetAura("Event Horizon") == null;
        return true;
    }
}
public class NavalCommanderRoutine : DefaultRoutine {
    public NavalCommanderRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 1) return S.GetAura("Broadside") == null || S.GetAura("Armada") == null;
        return true;
    }
}
public class DragonShinobiRoutine : DefaultRoutine {
    public DragonShinobiRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        double M = P.MaxMana > 0 ? (P.Mana / (double)P.MaxMana) : 0;
        if (I == 3) return M < 0.40;
        if (I == 2) return H < 0.60;
        return true;
    }
}
public class GreatThiefRoutine : DefaultRoutine {
    public GreatThiefRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 2) return false;
        return true;
    }
}
public class LichRoutine : DefaultRoutine {
    public LichRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 1) return S.GetAura("Ex Mortis") != null;
        return true;
    }
}
public class MasterOfMoglinsRoutine : DefaultRoutine {
    public MasterOfMoglinsRoutine(int[] sequence) : base(sequence) {}
    public override bool? ShouldUse(int I, IScriptPlayer P, IScriptSelfAuras S, IScriptTargetAuras T) {
        if (I == 4) return S.GetAura("Moglin's Will")?.Value >= 3;
        return true;
    }
}

