using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Models.Auras;

namespace Skua.Core.Services;

/// <summary>
/// Aura Tracker data. The game side is skua.swf's GetAuraTracker: AQW keeps one aura record per aura
/// name per caster (World.updateAuraData matches nam, casterType and casterId), so every player's Decay
/// or taunt on a boss shows separately. What an aura gives comes from Resources/auradata.json (AQW Wiki
/// class, forge enhancement and scroll pages, CC BY-SA 3.0): first the short effect line of the
/// CASTER's class (tools/build_aura_effects.py), then forge/scroll procs, then the server's own
/// description, then the class skill sentence that names it.
/// </summary>
public partial class AuraTrackerService : IAuraTrackerService
{
    private readonly Lazy<IFlashUtil> _lazyFlash;
    private IFlashUtil Flash => _lazyFlash.Value;

    private readonly Lazy<AuraData> _data = new(AuraData.Load);
    private readonly ConcurrentDictionary<string, int> _mostSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WikiInfo?> _wikiCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly SpecialEffect[] Specials =
    {
        new(DecayRegex(), NoHealEvidenceRegex(), AuraTag.NoHeal, "NO HEAL",
            "Healing is blocked or turned into damage while this lasts. Hold healing skills and potions until it ends."),
        new(TauntRegex(), TauntEvidenceRegex(), AuraTag.Taunt, "TAUNT",
            "Forced to attack whoever taunted it (Scroll of Enrage: 6 seconds). Taunts don't work on players."),
        new(RecklessRegex(), RecklessRegex(), AuraTag.Reckless, "RECKLESS",
            "Attacks recklessly: all its outgoing damage is 15% lower (Scroll of Enrage: 10 seconds)."),
        new(EnrageRegex(), EnrageRegex(), AuraTag.Enrage, "ENRAGE", "Enraged: expect bigger hits until it ends.")
    };

    public AuraTrackerService(Lazy<IFlashUtil> lazyFlash)
    {
        _lazyFlash = lazyFlash;
    }

    public AuraTrackerResult Read(SubjectType subject)
    {
        AuraTrackerSnapshot? snap = null;
        try
        {
            string? json = Flash.Call("GetAuraTracker", subject.ToString());
            if (!string.IsNullOrWhiteSpace(json))
                snap = JsonConvert.DeserializeObject<AuraTrackerSnapshot>(json);
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }

        if (snap is null)
            return new AuraTrackerResult { Subject = subject };

        AuraData data = _data.Value;
        List<AuraTrackerEntry> entries = new();
        foreach (AuraTrackerRecord rec in snap.Auras)
        {
            if (string.IsNullOrWhiteSpace(rec.Name))
                continue;
            double? left = rec.Persist ? null : rec.Remaining;
            if (left is < -1)
                continue;
            entries.Add(Decorate(rec, subject, left, data));
        }

        static int Rank(AuraTrackerEntry e) => e.Tag != AuraTag.None ? 0 : e.IsFull ? 1 : e.ShowsStacks ? 2 : 3;
        entries.Sort((x, y) => Rank(x) != Rank(y) ? Rank(x).CompareTo(Rank(y)) : x.Sequence.CompareTo(y.Sequence));

        return new AuraTrackerResult
        {
            Subject = subject,
            Target = snap.Target,
            HasTarget = subject == SubjectType.Self || snap.Target is not null,
            Auras = entries
        };
    }

    public IReadOnlyList<string> KnownAuraNames => _data.Value.KnownNames;

    public (int Size, byte[] Argb)? GetIcon(string iconLink, int size)
    {
        if (string.IsNullOrWhiteSpace(iconLink))
            return null;
        try
        {
            string? raw = Flash.Call("GetAuraIcon", iconLink, size);
            if (string.IsNullOrEmpty(raw))
                return null;
            int colon = raw.IndexOf(':');
            if (colon <= 0 || !int.TryParse(raw[..colon], out int edge) || edge <= 0)
                return null;
            byte[] px = Convert.FromBase64String(raw[(colon + 1)..]);
            return px.Length >= edge * edge * 4 ? (edge, px) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private AuraTrackerEntry Decorate(AuraTrackerRecord rec, SubjectType subject, double? left, AuraData data)
    {
        AuraCaster? first = rec.Casters.FirstOrDefault();
        bool mine = first is { IsMe: true, IsMonster: false };
        // A caster's own class decides what its aura does: a party buff from someone else's Lord of Order
        // is read from Lord of Order, not from your class.
        string casterClass = first is { IsMonster: false } ? first.ClassName : string.Empty;

        WikiInfo? wiki = casterClass.Length > 0 ? WikiFor(rec.Name, casterClass, data) : null;
        EffectLine? fx = casterClass.Length > 0 ? data.Effect(casterClass, rec.Name) : null;
        // Forge procs are yours; a monster's "Decay" is not Dauntless's.
        ProcInfo? proc = mine || first is not { IsMonster: true } ? data.Proc(rec.Name, subject) : null;

        int max = LimitIn(StripTags(rec.Description));
        if (max == 0 && wiki is not null) max = wiki.Max;
        if (max == 0 && fx?.Max is > 1) max = fx.Max.Value;
        if (max == 0 && proc is not null) max = proc.Max;
        bool noCap = max == 0 && wiki is { NoCap: true };

        int most = _mostSeen.AddOrUpdate(rec.Name, rec.Stacks, (_, v) => Math.Max(v, rec.Stacks));
        bool stacks = max > 1 || most > 1;
        bool full = max > 1 && rec.Stacks >= max;

        SpecialEffect? sp = Specials.FirstOrDefault(s => s.Pattern.IsMatch(rec.Name));
        // When the caster's class describes the aura, its text decides: Chaos Avenger's "Focus" forces the
        // target to attack you (a taunt), King's Echo's "Focus" is a buff, Legion Revenant's "Decay" is
        // described only as a DoT. The tag stays only when that text says so.
        if (sp is not null && wiki is not null && !sp.Evidence.IsMatch(wiki.Text + " " + (fx?.Text ?? string.Empty)))
            sp = null;
        string tagText = sp?.Text ?? string.Empty, tagNote = sp?.Note ?? string.Empty;
        AuraTag tag = sp?.Tag ?? AuraTag.None;
        if (tag == AuraTag.None && wiki is null && proc is not null && proc.Source.StartsWith("Scroll of", StringComparison.OrdinalIgnoreCase))
        {
            tag = AuraTag.Scroll;
            tagText = "SCROLL";
            tagNote = proc.Source + ": " + proc.Text;
        }

        string effect, source;
        string className = casterClass.Length > 0 ? " (" + casterClass + ")" : string.Empty;
        // The caster's own class first (its short effect line, then its sentence), then the server's own
        // description, then a forge or scroll proc of that name.
        if (fx is not null)
        {
            effect = fx.Text;
            source = fx.From + className;
        }
        else if (!string.IsNullOrWhiteSpace(rec.Description))
        {
            effect = Regex.Replace(StripTags(rec.Description), @"\s+", " ").Trim();
            source = string.Empty;
        }
        else if (wiki is not null)
        {
            effect = wiki.Text;
            source = wiki.From + className;
        }
        else if (proc is not null)
        {
            effect = proc.Text.Replace("; ", ", ");
            source = proc.Source;
        }
        else
        {
            effect = tagNote;
            source = string.Empty;
        }

        bool scrollSource = tag == AuraTag.Scroll
            || (wiki is null && proc is not null && proc.Source.StartsWith("Scroll of", StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<EffectPill> pills = AuraClassifier.Pills(effect, scrollSource || tag is AuraTag.Taunt or AuraTag.Reckless);
        AuraCategory category = AuraClassifier.Categorize(rec.Name, tag, scrollSource, pills,
            subject == SubjectType.Self && first is { IsMonster: true });

        string byLine = string.Empty;
        if (first is not null && !(subject == SubjectType.Self && rec.Casters.Count == 1 && mine))
            byLine = "by " + first.DisplayName + (rec.Casters.Count > 1 ? " +" + (rec.Casters.Count - 1) : string.Empty);

        return new AuraTrackerEntry
        {
            Subject = subject,
            Name = rec.Name,
            Icon = rec.Icon,
            Stacks = rec.Stacks,
            MaxStacks = max,
            MostSeen = most,
            NoCap = noCap,
            ShowsStacks = stacks,
            IsFull = full,
            Duration = rec.Duration,
            Remaining = left,
            Effect = effect,
            EffectSource = source,
            Tag = tag,
            TagText = tagText,
            TagNote = tagNote,
            Casters = rec.Casters,
            ByLine = byLine,
            Sequence = rec.Sequence,
            Pills = pills,
            Category = category,
            Group = AuraClassifier.GroupOf(tag, category)
        };
    }

    private WikiInfo? WikiFor(string aura, string cls, AuraData data)
    {
        string key = cls + "|" + aura;
        if (_wikiCache.TryGetValue(key, out WikiInfo? cached))
            return cached;
        WikiInfo? info = data.Wiki(aura, cls);
        _wikiCache[key] = info;
        return info;
    }

    internal static string StripTags(string? text) => string.IsNullOrEmpty(text) ? string.Empty : TagRegex().Replace(text, " ");

    internal static int LimitIn(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        foreach (Regex r in new[] { Limit1Regex(), Limit2Regex(), Limit3Regex(), Limit4Regex() })
        {
            Match m = r.Match(text);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > 1)
                return n;
        }
        return 0;
    }

    [GeneratedRegex(@"decay|heal(ing)?\s*block|no\s*heal|anti-?heal|cauteri[sz]e", RegexOptions.IgnoreCase)]
    private static partial Regex DecayRegex();
    [GeneratedRegex(@"^focus$|taunt", RegexOptions.IgnoreCase)]
    private static partial Regex TauntRegex();
    [GeneratedRegex(@"reckless", RegexOptions.IgnoreCase)]
    private static partial Regex RecklessRegex();
    [GeneratedRegex(@"cannot\s+heal|can'?t\s+heal|prevent\w*\s+(?:\w+\s+){0,2}heal|heal(?:ing)?\s+(?:is\s+)?block|no\s+heal|turn\w*\s+heal", RegexOptions.IgnoreCase)]
    private static partial Regex NoHealEvidenceRegex();
    [GeneratedRegex(@"taunt|forc\w+\s+(?:\w+\s+){0,3}to\s+attack", RegexOptions.IgnoreCase)]
    private static partial Regex TauntEvidenceRegex();
    [GeneratedRegex(@"enrage", RegexOptions.IgnoreCase)]
    private static partial Regex EnrageRegex();
    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagRegex();
    [GeneratedRegex(@"up\s+to\s+(\d+)\s*(?:times|stacks)", RegexOptions.IgnoreCase)]
    private static partial Regex Limit1Regex();
    [GeneratedRegex(@"max(?:imum)?(?:\s+of)?\s+(\d+)\s+stacks?", RegexOptions.IgnoreCase)]
    private static partial Regex Limit2Regex();
    [GeneratedRegex(@"(\d+)\s+stacks?\s+max", RegexOptions.IgnoreCase)]
    private static partial Regex Limit3Regex();
    [GeneratedRegex(@"stacks?\s+up\s+to\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Limit4Regex();

    private sealed record SpecialEffect(Regex Pattern, Regex Evidence, AuraTag Tag, string Text, string Note);

    internal sealed record WikiInfo(string Text, string From, int Max, bool NoCap);

    internal sealed record EffectLine(string Text, string From, double? Duration, int? Max);

    internal sealed record ProcInfo(string Text, int Max, double Duration, string On, string Source);

    /// <summary>
    /// Resources/auradata.json: "classes" (class -> skills with name and wiki text), "fx" (class -> aura ->
    /// short effect line) and "auras" (forge enhancement and scroll procs).
    /// </summary>
    internal sealed partial class AuraData
    {
        private readonly Dictionary<string, List<(string Name, string Text)>> _classes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, EffectLine>> _fx = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ProcInfo>> _procs = new(StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<string>? _known;

        /// <summary>Every aura name in the wiki data (class effects and forge/scroll procs), for pin search.</summary>
        public IReadOnlyList<string> KnownNames => _known ??= _fx.Values.SelectMany(d => d.Keys)
            .Concat(_procs.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        public static AuraData Load()
        {
            AuraData data = new();
            try
            {
                Assembly asm = typeof(AuraTrackerService).Assembly;
                string? name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("auradata.json", StringComparison.OrdinalIgnoreCase));
                if (name is null)
                    return data;
                using Stream? stream = asm.GetManifestResourceStream(name);
                if (stream is null)
                    return data;
                using StreamReader reader = new(stream);
                JObject root = JObject.Parse(reader.ReadToEnd());

                if (root["classes"] is JObject classes)
                {
                    foreach (JProperty cls in classes.Properties())
                    {
                        List<(string, string)> skills = new();
                        if (cls.Value is JArray arr)
                        {
                            foreach (JToken s in arr)
                                skills.Add(((string?)s["n"] ?? string.Empty, (string?)s["d"] ?? string.Empty));
                        }
                        data._classes[cls.Name] = skills;
                    }
                }
                if (root["fx"] is JObject fx)
                {
                    foreach (JProperty cls in fx.Properties())
                    {
                        Dictionary<string, EffectLine> lines = new(StringComparer.OrdinalIgnoreCase);
                        if (cls.Value is JObject auras)
                        {
                            foreach (JProperty a in auras.Properties())
                            {
                                lines[a.Name] = new EffectLine((string?)a.Value["e"] ?? string.Empty, (string?)a.Value["from"] ?? string.Empty,
                                    (double?)a.Value["dur"], (int?)a.Value["max"]);
                            }
                        }
                        data._fx[cls.Name] = lines;
                    }
                }
                if (root["auras"] is JObject procs)
                {
                    foreach (JProperty a in procs.Properties())
                    {
                        List<ProcInfo> list = new();
                        IEnumerable<JToken> items = a.Value is JArray arr ? arr : new[] { a.Value };
                        foreach (JToken p in items)
                        {
                            list.Add(new ProcInfo((string?)p["t"] ?? string.Empty, (int?)p["max"] ?? 0, (double?)p["dur"] ?? 0,
                                (string?)p["on"] ?? "you", (string?)p["src"] ?? string.Empty));
                        }
                        data._procs[NormQuotes(a.Name)] = list;
                    }
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
            return data;
        }

        private static string NormQuotes(string s) => s.Replace('‘', '\'').Replace('’', '\'');

        internal static string NormClass(string n) =>
            WhitespaceRegex().Replace(ParenRegex().Replace(n ?? string.Empty, string.Empty), " ").Trim().ToLowerInvariant();

        private static string BaseName(string aura) =>
            ThePrefixRegex().Replace(ReversedRegex().Replace(RankPrefixRegex().Replace(aura, string.Empty), string.Empty), string.Empty).Trim();

        public EffectLine? Effect(string cls, string aura)
        {
            if (!_fx.TryGetValue(NormClass(cls), out Dictionary<string, EffectLine>? lines))
                return null;
            return lines.TryGetValue(BaseName(aura), out EffectLine? line) ? line : null;
        }

        public ProcInfo? Proc(string aura, SubjectType subject)
        {
            if (!_procs.TryGetValue(NormQuotes(aura), out List<ProcInfo>? list) || list.Count == 0)
                return null;
            string on = subject == SubjectType.Target ? "target" : "you";
            return list.FirstOrDefault(p => p.On == on) ?? list[0];
        }

        /// <summary>
        /// The class's own text for an aura: a table line in a passive ("Magician: 22% INT"), or else the
        /// skill sentences that name it, the one that applies it first.
        /// </summary>
        public WikiInfo? Wiki(string aura, string cls)
        {
            if (!_classes.TryGetValue(NormClass(cls), out List<(string Name, string Text)>? skills))
                return null;
            string withoutRank = RankPrefixRegex().Replace(aura, string.Empty);
            bool reversed = ReversedTestRegex().IsMatch(withoutRank);
            string core = ThePrefixRegex().Replace(ReversedRegex().Replace(withoutRank, string.Empty), string.Empty).Trim();
            Regex coreLine = new("^(?:the\\s+)?" + Regex.Escape(core) + "\\s*:\\s*(.+)$", RegexOptions.IgnoreCase);
            string text = string.Empty, from = string.Empty;
            foreach ((string name, string body) in skills)
            {
                if (ReversedTestRegex().IsMatch(name) != reversed)
                    continue;
                foreach (string line in body.Split('\n'))
                {
                    Match m = coreLine.Match(line.Trim());
                    if (m.Success)
                    {
                        text = m.Groups[1].Value;
                        from = name;
                        break;
                    }
                }
                if (text.Length > 0)
                    break;
            }

            string all = string.Empty;
            if (core.Length > 2)
            {
                Regex nameRe = new("(?<![\\w])" + Regex.Escape(withoutRank) + "(?![\\w])", RegexOptions.IgnoreCase);
                List<(string Skill, string Sentence)> hits = new();
                foreach ((string name, string body) in skills)
                {
                    foreach (string sent in SentenceSplitRegex().Split(body.Replace('\n', ' ')))
                    {
                        if (nameRe.IsMatch(sent))
                            hits.Add((name, sent.Trim()));
                    }
                }
                all = string.Join(" ", hits.Select(h => h.Sentence));
                // The sentence that applies the aura says what it does; others only use it.
                hits = hits.OrderBy(h => SaysRegex().IsMatch(h.Sentence) ? 0 : 1).ToList();
                if (text.Length == 0 && hits.Count > 0)
                {
                    text = string.Join(" ", hits.Take(2).Select(h => h.Sentence));
                    from = hits[0].Skill;
                }
            }
            if (text.Length == 0)
                return null;
            string basis = all.Length > 0 ? all : text;
            int max = LimitIn(basis);
            bool noCap = max == 0 && PerStackRegex().IsMatch(basis);
            return new WikiInfo(text, from, max, noCap);
        }

        [GeneratedRegex(@"\([^)]*\)")]
        private static partial Regex ParenRegex();
        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();
        [GeneratedRegex(@"^[IVXLC0-9]+\s*-\s*")]
        private static partial Regex RankPrefixRegex();
        [GeneratedRegex(@"\s*\(reversed\)\s*", RegexOptions.IgnoreCase)]
        private static partial Regex ReversedRegex();
        [GeneratedRegex(@"reversed", RegexOptions.IgnoreCase)]
        private static partial Regex ReversedTestRegex();
        [GeneratedRegex(@"^the\s+", RegexOptions.IgnoreCase)]
        private static partial Regex ThePrefixRegex();
        [GeneratedRegex(@"(?<=[.!?])\s+")]
        private static partial Regex SentenceSplitRegex();
        [GeneratedRegex(@"\b(appl|grant|summon|gain|increas|reduc|decreas|restor|heal)", RegexOptions.IgnoreCase)]
        private static partial Regex SaysRegex();
        [GeneratedRegex(@"\b(each|per)\s+stack\b", RegexOptions.IgnoreCase)]
        private static partial Regex PerStackRegex();
    }
}
