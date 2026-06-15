namespace Skua.Core.Skills;

using System;
using System.Linq;
using Skua.Core.Interfaces;

public class MasterSkillProvider : ISkillProvider
{
    private IScriptPlayer P;
    private IScriptSelfAuras S;
    private IScriptTargetAuras T;
    private IScriptCombat C;
    private string N = "";
    private int[] R = new int[] { 1, 2, 3, 4 };
    private int X = 0;
    public bool ResetOnTarget { get; set; } = true;
    public int SkillCount => 4;

    public void Init(IScriptPlayer p, IScriptSelfAuras s, IScriptTargetAuras t, IScriptCombat c, IFlashUtil f)
    {
        P = p; S = s; T = t; C = c;
        N = IScriptInterface.Instance.Player.CurrentClass?.Name?.ToLower() ?? "";
        Configure();
    }

    private void Configure()
    {
        if (new[]{"void highlord","void tracker","hobo highlord","frostval barbarian","dragonlord","shadowflame dragonlord","legendary hero","dark legendary hero","beastmaster","not a mod","simple class","infinity knight","interstellar knight"}.Any(N.Contains)) R = new[]{3,1,2,4};
        else if (new[]{"legion revenant","archfiend","verus doomknight","abyssal angel","abyssal angel's shadow","shadow ripper","witch"}.Any(N.Contains)) R = new[]{3,4,1,2};
        else if (new[]{"archpaladin","cryomancer","dark cryomancer","sakura cryomancer","necromancer","grim necromancer","nu metal necro","dark metal necro","doom metal necro","heavy metal necro","neo metal necro","acolyte","healer","battle healer","oracle","drakkar knight","draco knight"}.Any(N.Contains)) R = new[]{1,3,2,4};
        else if (new[]{"lightcaster","lightmage","lord of order","mindbreaker","psionic mindbreaker","troubador of love","bard","battle bard"}.Any(N.Contains)) R = new[]{1,3,4,2};
        else if (new[]{"stonecrusher","infinity titan","blood titan","frostblood titan","mechajouster"}.Any(N.Contains)) R = new[]{2,3,4,1};
        else if (new[]{"chaos avenger","chaos champion prime","chaos shaper","chaos slayer berserker","chaos slayer cleric","chaos slayer mystic","chaos slayer thief"}.Any(N.Contains)) R = new[]{4,2,3,1};
        else if (new[]{"dragon of time","archmage","shaman","evolved shaman","scarlet sorceress","blood sorceress","troll spellsmith","legion doomknight","classic legion doomknight","timeless dark caster","infinite legion dark caster","dark harbinger","exalted harbinger","soul cleaver","exalted soul cleaver","classic soul cleaver","dark caster","arcane dark caster","mystical dark caster","immortal dark caster","evolved dark caster","lich","king's echo"}.Any(N.Contains)) R = new[]{4,2,1,3};
        else if (new[]{"blaze binder","pyromancer","firelord summoner","flame dragon warrior","scion of flames","mage","sorcerer","battlemage","dark battlemage","royal battlemage","battlemage of love","pink romancer","pinkomancer","shadow rocker"}.Any(N.Contains)) R = new[]{1,4,3,2};
        else if (new[]{"vampire lord","royal vampire lord","enchanted vampire lord","vampire","lycan","alpha doommega","alpha omega","blood ancient","elemental dracomancer","grunge rocker","heavy metal rockstar"}.Any(N.Contains)) R = new[]{4,3,1,2};
        else if (new[]{"yami no ronin","arachnomancer","assassin","ninja","classic ninja","ninja warrior","chunin","imperial chunin","dragon shinobi","dragon soul shinobi","shadow dragon shinobi"}.Any(N.Contains)) R = new[]{2,1,4,3};
        else if (new[]{"chronoassassin","thief of hours","doomknight","doomknight overlord","classic doomknight","deathknight","deathknight lord","shadowstalker of time","shadowwalker of time","shadowweaver of time","timekeeper","timekiller","chronomancer","chronomancer prime","corrupted chronomancer","underworld chronomancer","overworld chronomancer","timeless chronomancer","immortal chronomancer","eternal chronomancer","continuum chronomancer","empyrean chronomancer","quantum chronomancer","obsidian paladin chronomancer","phantasm chronomancer","phantom chronomancer","paladin chronomancer","chrono chaorruptor","chrono commandant","chrono dataknight","chrono dragonknight","chrono shadowhunter","chrono shadowslayer","chronocommander","chronocorruptor"}.Any(N.Contains)) R = new[]{4,1,2,3};
        else if (new[]{"glacial berserker","glaceran warlord","dark glaceran warlord","savage glaceran warlord","glacial warlord","berserker","beta berserker","brutal berserker","dark chaos berserker"}.Any(N.Contains)) R = new[]{2,1,3,4};
        else if (new[]{"eternal inversionist","the collector","dark lord","daimon"}.Any(N.Contains)) R = new[]{2,3,1,4};
        else if (new[]{"rogue","pirate","alpha pirate","classic pirate","classic alpha pirate","barber","classic barber","leprechaun","undead leperchaun","unlucky leperchaun","great thief","naval commander","heroic naval commander","highseas commander","legendary naval commander","renegade"}.Any(N.Contains)) R = new[]{1,2,3,4};
        else if (new[]{"warrior","mercenary","beast warrior","dragonslayer","dragonslayer general","paladin","classic paladin","legion paladin","silver paladin","paladin highlord","paladinslayer","undeadslayer","defender","classic defender","guardian","classic guardian","drakel warlord","warlord","warriorscythe general","master martial artist","martial artist"}.Any(N.Contains)) R = new[]{1,2,3,4};
        else if (new[]{"rustbucket","enforcer","protosartorium","clawsuit","evolved clawsuit","prismatic clawsuit","star captain","starlord","skycharged grenadier","skyguard grenadier"}.Any(N.Contains)) R = new[]{1,2,3,4};
        else R = new[]{1,2,3,4};
    }

    public void OnTargetReset() { if (ResetOnTarget) X = 0; }
    public void Stop() { C.CancelAutoAttack(); C.CancelTarget(); X = 0; }
    public void OnPlayerDeath() { X = 0; }
    public void Save(string file) { }
    public void Load(string file) { }

    public (int, int) GetNextSkill()
    {
        var b = IScriptInterface.Instance;
        int s = X;
        do
        {
            int k = R[X];
            X = (X + 1) % R.Length;
            if (b.Skills.CanUseSkill(k)) if (ShouldUse(k) == true) return (k, k);
        } while (X != s);
        return (-1, -1);
    }

    public bool? ShouldUse(int I)
    {
        if (!P.Alive || !P.HasTarget) return false;
        double H = P.MaxHealth > 0 ? (P.Health / (double)P.MaxHealth) : 0;
        double M = P.MaxMana > 0 ? (P.Mana / (double)P.MaxMana) : 0;

        if (N.Contains("void highlord") || N.Contains("void tracker") || N.Contains("hobo highlord"))
        {
            if (I == 1) return (H <= 0.50 && S.GetAura("Unshackled") != null) || (H <= 0.90 && S.GetAura("Unshackled") == null && (S.GetAura("Shackled") == null || S.GetAura("Shackled").RemainingTime <= 2));
            if (I == 3) return (H > 0.90 && S.GetAura("Shackled") != null) || (H > 0.50 && S.GetAura("Shackled") == null && (S.GetAura("Unshackled") == null || S.GetAura("Unshackled").RemainingTime <= 2));
            return true;
        }
        if (N.Contains("legion revenant"))
        {
            if (I == 3) return true;
            if (I == 4) return S.GetAura("Depths of Tartarus") == null;
            return true;
        }
        if (N.Contains("archfiend"))
        {
            if (I == 4) return S.GetAura("Fiend Frenzy") == null;
            return true;
        }
        if (N.Contains("verus doomknight"))
        {
            if (I == 4) return S.GetAura("Unleashed Doom") == null && S.GetAura("Doom")?.Value >= 10;
            if (I == 1) return S.GetAura("Unleashed Doom") != null;
            return true;
        }
        if (N.Contains("chaos avenger") || N.Contains("chaos champion prime"))
        {
            if (I == 4) return S.GetAura("Chaotic Armor") != null;
            if (I == 3) return H <= 0.80;
            return true;
        }
        if (N.Contains("dragon of time"))
        {
            if (I == 4) return H >= 0.85;
            if (I == 2) return H <= 0.60 || S.GetAura("Convergence") != null;
            return true;
        }
        if (N.Contains("archmage"))
        {
            if (I == 4) return S.GetAura("Corporeal Ascension") == null;
            if (I == 2) return M < 0.35;
            return true;
        }
        if (N.Contains("archpaladin"))
        {
            if (I == 4) return T.GetAura("Commandment")?.Value >= 50;
            if (I == 2) return H < 0.70;
            return true;
        }
        if (N.Contains("stonecrusher") || N.Contains("infinity titan"))
        {
            if (I == 4) return S.GetAura("Magnitude") != null;
            return true;
        }
        if (N.Contains("yami no ronin"))
        {
            if (I == 4) return S.GetAura("Yami")?.Value >= 4;
            return true;
        }
        if (N.Contains("chronoassassin") || N.Contains("thief of hours"))
        {
            if (I == 4) return S.GetAura("Temporal Dodge") == null;
            if (I == 2) return M >= 0.3;
            return true;
        }
        if (N.Contains("shaman") || N.Contains("evolved shaman"))
        {
            if (I == 4) return S.GetAura("Elemental Embrace") == null || S.GetAura("Elemental Embrace").RemainingTime <= 2;
            if (I == 2) return H < 0.70;
            return true;
        }
        if (N.Contains("scarlet sorceress") || N.Contains("blood sorceress"))
        {
            if (I == 4) return S.GetAura("Crimson Ritual") == null || S.GetAura("Crimson Ritual").Value < 5 || S.GetAura("Crimson Ritual").RemainingTime <= 2;
            if (I == 3) return H < 0.60;
            return true;
        }
        if (N.Contains("necrotic chronomancer") || N.Contains("nechronomancer"))
        {
            if (I == 4) return H <= 0.50 && T.GetAura("Chaos Rift")?.Value >= 1;
            return true;
        }
        if (N.Contains("glacial berserker") || N.Contains("glaceran warlord") || N.Contains("glacial warlord"))
        {
            if (I == 3) return S.GetAura("Ancestral Presence") != null;
            return true;
        }
        if (N.Contains("blood titan") || N.Contains("frostblood titan"))
        {
            if (I == 4 || I == 3) return S.GetAura("Titan's Bloodline") == null;
            return H >= 0.70 || S.GetAura("Life Drinker") != null;
        }
        if (N.Contains("arachnomancer"))
        {
            if (I == 2) return H < 0.60;
            return true;
        }
        if (N.Contains("vindicator of they") || N.Contains("hollowborn vindicator"))
        {
            if (I == 3) return M > 0.40;
            if (I == 4) return S.GetAura("Massive Strike") != null;
            return true;
        }
        if (N.Contains("necromancer") || N.Contains("pinkomancer"))
        {
            if (I == 1) return S.GetAura("Summon Minion") == null;
            if (I == 4) return H > 0.40;
            return true;
        }
        if (N.Contains("paladin") || N.Contains("silver paladin"))
        {
            if (N.Contains("legion paladin") && I == 2) return S.GetAura("Soul")?.Value >= 10;
            if (I == 1) return H < 0.80;
            if (I == 4) return S.GetAura("Aegis") == null;
            return true;
        }
        if (N.Contains("chronomancer") || N.Contains("time"))
        {
            if (I == 4) return T.GetAura("Temporal Rift")?.Value >= 4;
            if (N.Contains("corrupted chronomancer") || N.Contains("timeless chronomancer"))
            {
                if (I == 2) return H < 0.50; 
            }
            return true;
        }
        if (N.Contains("chaos slayer") || N.Contains("dark chaos berserker"))
        {
            if (I == 4 && T.GetAura("Courageous") != null) return false;
            if (I == 4) return S.GetAura("Chaotic Defense") == null;
            if (I == 2) return S.GetAura("Surge") == null && H < 0.60;
            return true;
        }
        if (N.Contains("rustbucket") || N.Contains("enforcer") || N.Contains("protosartorium"))
        {
            if (I == 3) return S.GetAura("Event Horizon") == null;
            return true;
        }
        if (N.Contains("naval commander") || N.Contains("highseas commander") || N.Contains("pirate") || N.Contains("alpha pirate"))
        {
            if (I == 1) return S.GetAura("Broadside") == null || S.GetAura("Armada") == null;
            return true;
        }
        if (N.Contains("dragon shinobi"))
        {
            if (I == 3) return M < 0.40;
            if (I == 2) return H < 0.60;
            return true;
        }
        if (N.Contains("great thief"))
        {
            if (I == 2) return false;
            return true;
        }
        if (N.Contains("lich"))
        {
            if (I == 1) return S.GetAura("Ex Mortis") != null;
            return true;
        }
        if (N.Contains("master of moglins"))
        {
            if (I == 4) return S.GetAura("Moglin's Will")?.Value >= 3;
            return true;
        }
        return true;
    }

    public bool? ShouldUseSkill(int skillIndex, bool canUse) => ShouldUse(skillIndex);
}
