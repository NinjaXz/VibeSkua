using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Skua.Core.Models.Auras;

/// <summary>The color of one effect pill.</summary>
public enum EffectKind
{
    /// <summary>Grey: nothing to classify.</summary>
    Neutral,
    /// <summary>Gold: a stat goes up in your favor.</summary>
    Buff,
    /// <summary>Green: heal over time.</summary>
    HealOverTime,
    /// <summary>Red: damage over time.</summary>
    DamageOverTime,
    /// <summary>Blue-violet: a stat goes down against you, stun, heal block.</summary>
    Debuff,
    /// <summary>Orange: a scroll's effect.</summary>
    Scroll
}

/// <summary>One clause of an aura's effect text, with its color.</summary>
public sealed record EffectPill(string Text, EffectKind Kind);

/// <summary>Which filter chip an aura belongs to.</summary>
public enum AuraCategory
{
    Buff,
    Debuff,
    Scroll,
    Potion
}

/// <summary>Priority groups, ranked in the order the user chose (default: this order).</summary>
public enum AuraPriorityGroup
{
    Enrage,
    Scrolls,
    Potions,
    Debuffs,
    Other
}

/// <summary>Filter chips on an aura panel.</summary>
public enum AuraFilter
{
    All,
    Buffs,
    Debuffs,
    ScrollsAndPotions
}

/// <summary>
/// Colors, categories, priority and pin matching for the Aura Tracker. MVS's aura overlay follows the
/// same rules, so both apps show an aura the same way.
/// </summary>
public static partial class AuraClassifier
{
    public static readonly IReadOnlyList<AuraPriorityGroup> DefaultOrder =
        new[] { AuraPriorityGroup.Enrage, AuraPriorityGroup.Scrolls, AuraPriorityGroup.Potions, AuraPriorityGroup.Debuffs, AuraPriorityGroup.Other };

    [GeneratedRegex(@"\bdots?\b|damage\s+(?:\w+\s+){0,2}over time|\bburn(ing|s)?\b|\bbleed(ing|s)?\b|\bpoison(ed|s)?\b|deals? .*\b(every|per second|each second)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotRegex();

    [GeneratedRegex(@"\bhots?\b|heal\w*\s+(?:\w+\s+){0,2}over time|\bregen(eration|erates?)?\b|heals? .*\b(every|per second|each second)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HotRegex();

    [GeneratedRegex(@"\bstun(ned|s)?\b|heal(ing)?\s*block|blocks? heal|heal\w*\s+(?:\w+\s+){0,3}(?:into|to|as)\s+damage|convert\w*\s+heal|no heal|cannot|can't|\bblocked\b|\bsilence|\bfreez|\bslow(ed|s)?\b|\bdecay\b|\bfear(ed)?\b|\bsleep\b|\bblind", RegexOptions.IgnoreCase)]
    private static partial Regex ControlRegex();

    [GeneratedRegex(@"\b(cost|cooldown|consumption|damage taken|incoming damage|damage received|taken)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LowerIsBetterRegex();

    [GeneratedRegex(@"(?<![\w.])([+\-−–])\s*\d")]
    private static partial Regex SignedNumberRegex();

    [GeneratedRegex(@"\b(reduc|decreas|lower)", RegexOptions.IgnoreCase)]
    private static partial Regex DownWordRegex();

    [GeneratedRegex(@"\b(increas|boost|gain|raise|bonus)", RegexOptions.IgnoreCase)]
    private static partial Regex UpWordRegex();

    [GeneratedRegex(@"tonic|elixir|potion|draught|brew|philter|felicitous|tincture|serum", RegexOptions.IgnoreCase)]
    private static partial Regex PotionRegex();

    /// <summary>Splits an effect line into colored pills.</summary>
    public static IReadOnlyList<EffectPill> Pills(string effect, bool scroll)
    {
        if (string.IsNullOrWhiteSpace(effect))
            return Array.Empty<EffectPill>();
        List<EffectPill> pills = new();
        foreach (string raw in Clauses(effect))
        {
            string text = raw.Trim().TrimEnd('.');
            if (text.Length == 0)
                continue;
            EffectKind kind = Classify(text);
            if (scroll && kind is EffectKind.Buff or EffectKind.Debuff or EffectKind.Neutral)
                kind = EffectKind.Scroll;
            pills.Add(new EffectPill(text, kind));
        }
        return pills;
    }

    // ";" always separates effects. A "," only does when every part is a stat on its own
    // ("+15% Crit Chance, +25% Haste"); otherwise it is one sentence and stays one pill.
    private static IEnumerable<string> Clauses(string effect)
    {
        foreach (string part in effect.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] bits = part.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (bits.Length > 1 && bits.All(b => StatItemRegex().IsMatch(b)))
            {
                foreach (string b in bits)
                    yield return b;
            }
            else
            {
                yield return part;
            }
        }
    }

    [GeneratedRegex(@"^\s*[^,]{0,40}\d+(\.\d+)?\s*%?[^,]{0,40}$")]
    private static partial Regex StatItemRegex();

    /// <summary>The color of one effect clause.</summary>
    public static EffectKind Classify(string clause)
    {
        if (DotRegex().IsMatch(clause))
            return EffectKind.DamageOverTime;
        if (HotRegex().IsMatch(clause))
            return EffectKind.HealOverTime;
        if (ControlRegex().IsMatch(clause))
            return EffectKind.Debuff;

        bool? up = null;
        Match m = SignedNumberRegex().Match(clause);
        if (m.Success)
            up = m.Groups[1].Value == "+";
        else if (DownWordRegex().IsMatch(clause))
            up = false;
        else if (UpWordRegex().IsMatch(clause))
            up = true;
        if (up is null)
            return EffectKind.Neutral;
        bool good = LowerIsBetterRegex().IsMatch(clause) ? !up.Value : up.Value;
        return good ? EffectKind.Buff : EffectKind.Debuff;
    }

    public static bool IsPotion(string name) => PotionRegex().IsMatch(name);

    /// <summary>The filter chip an aura belongs to.</summary>
    public static AuraCategory Categorize(string name, AuraTag tag, bool scrollSource, IReadOnlyList<EffectPill> pills, bool byMonsterOnYou)
    {
        if (tag is AuraTag.Scroll or AuraTag.Taunt or AuraTag.Reckless or AuraTag.Enrage || scrollSource)
            return AuraCategory.Scroll;
        if (IsPotion(name))
            return AuraCategory.Potion;
        if (tag == AuraTag.NoHeal || byMonsterOnYou)
            return AuraCategory.Debuff;
        int bad = pills.Count(p => p.Kind is EffectKind.Debuff or EffectKind.DamageOverTime);
        int good = pills.Count(p => p.Kind is EffectKind.Buff or EffectKind.HealOverTime);
        return bad > good ? AuraCategory.Debuff : AuraCategory.Buff;
    }

    /// <summary>The priority group of an aura.</summary>
    public static AuraPriorityGroup GroupOf(AuraTag tag, AuraCategory category) =>
        tag is AuraTag.Taunt or AuraTag.Reckless ? AuraPriorityGroup.Enrage
        : category switch
        {
            AuraCategory.Scroll => AuraPriorityGroup.Scrolls,
            AuraCategory.Potion => AuraPriorityGroup.Potions,
            AuraCategory.Debuff => AuraPriorityGroup.Debuffs,
            _ => AuraPriorityGroup.Other
        };

    public static bool Passes(AuraFilter filter, AuraCategory category) => filter switch
    {
        AuraFilter.Buffs => category == AuraCategory.Buff,
        AuraFilter.Debuffs => category == AuraCategory.Debuff,
        AuraFilter.ScrollsAndPotions => category is AuraCategory.Scroll or AuraCategory.Potion,
        _ => true
    };

    /// <summary>Reads a saved group order ("enrage,scrolls,..."); missing groups are added in the default order.</summary>
    public static List<AuraPriorityGroup> ParseOrder(string? saved)
    {
        List<AuraPriorityGroup> order = new();
        foreach (string part in (saved ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(part, true, out AuraPriorityGroup g) && !order.Contains(g))
                order.Add(g);
        }
        foreach (AuraPriorityGroup g in DefaultOrder)
        {
            if (!order.Contains(g))
                order.Add(g);
        }
        return order;
    }

    public static string FormatOrder(IEnumerable<AuraPriorityGroup> order) =>
        string.Join(",", order.Select(g => g.ToString().ToLowerInvariant()));

    /// <summary>Lowercase, letters/digits/spaces only, single spaces.</summary>
    public static string Normalize(string text)
    {
        StringBuilder sb = new(text.Length);
        bool space = false;
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (space && sb.Length > 0)
                    sb.Append(' ');
                sb.Append(c);
                space = false;
            }
            else
            {
                space = true;
            }
        }
        return sb.ToString();
    }

    /// <summary>Whether a pin puts this aura on top: same name, or the name or effect contains the pin.</summary>
    public static bool PinMatches(string pin, string name, string effect)
    {
        string p = Normalize(pin);
        if (p.Length == 0)
            return false;
        string n = Normalize(name);
        return n == p || n.Contains(p, StringComparison.Ordinal) || Normalize(effect).Contains(p, StringComparison.Ordinal);
    }

    /// <summary>How well <paramref name="query"/> matches <paramref name="name"/>; 0 = not at all.</summary>
    public static int Score(string query, string name)
    {
        string q = Normalize(query), n = Normalize(name);
        if (q.Length == 0 || n.Length == 0)
            return 0;
        if (n == q)
            return 1000;
        if (n.StartsWith(q, StringComparison.Ordinal))
            return 800 - Math.Min(99, n.Length - q.Length);
        string[] words = n.Split(' ');
        if (words.Any(w => w.StartsWith(q, StringComparison.Ordinal)))
            return 700 - Math.Min(99, n.Length - q.Length);
        if (n.Contains(q, StringComparison.Ordinal))
            return 600 - Math.Min(99, n.Length - q.Length);
        int gaps = SubsequenceGaps(q, n);
        if (gaps >= 0)
            return Math.Max(401, 500 - gaps * 5 - Math.Min(50, n.Length - q.Length));
        int dist = words.Append(n).Min(w => Levenshtein(q, w));
        int allowed = Math.Max(1, q.Length / 3);
        return dist <= allowed ? 400 - 50 * dist : 0;
    }

    /// <summary>Known names best matching <paramref name="query"/>, best first.</summary>
    public static IReadOnlyList<string> BestMatches(string query, IEnumerable<string> names, int take = 8) =>
        names.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => (Name: n, Score: Score(query, n)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name.Length)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(x => x.Name)
            .ToList();

    // Letters of q in order inside n; returns how many letters were skipped between them, or -1.
    private static int SubsequenceGaps(string q, string n)
    {
        int i = 0, gaps = 0, last = -1;
        for (int j = 0; j < n.Length && i < q.Length; j++)
        {
            if (n[j] != q[i])
                continue;
            if (last >= 0)
                gaps += j - last - 1;
            last = j;
            i++;
        }
        return i == q.Length ? gaps : -1;
    }

    private static int Levenshtein(string a, string b)
    {
        int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
            prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
