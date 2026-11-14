"""Adds short effect lines ("Dodge -35%, Damage -25%") to Skua.Core/Resources/auradata.json.

An aura display could show the whole skill text an aura was named in. This reads the same AQW Wiki
skill texts already in auradata.json and keeps only what each aura does: the stat changes, DoT/HoT,
stuns, heal blocks and taunts that follow "applies <Aura>" (or "<Aura>: ..." table lines), how long it
lasts and its stack limit. Nothing is invented: an aura whose effect can't be read gets no "fx" entry
and the Aura Tracker falls back to the sentence that names it.

Run:  python -I tools/build_aura_effects.py          (writes data["fx"][class][aura])
      python -I tools/build_aura_effects.py --check  (prints a sample, writes nothing)
"""
import json
import re
import sys
from pathlib import Path

DATA = Path(__file__).resolve().parent.parent / "Skua.Core" / "Resources" / "auradata.json"

UP = r"(?:increas\w*|boost\w*|rais\w*|improv\w*|enhanc\w*|amplif\w*|strengthen\w*|heighten\w*|grant\w*|gain\w*|maxing\s+out)"
DOWN = r"(?:reduc\w*|decreas\w*|lower\w*|weaken\w*|drain\w*|siphon\w*|cut\w*|shred\w*)"

APPLY = re.compile(
    r"(?i:\b(?:appl(?:y|ies|ying)|inflict\w*\s+(?:\w+\s+){0,2}with|grant(?:s|ing)|summon(?:s|ing)?"
    r"|remain(?:s|ing)?\s+in|leav(?:es|ing)\s+(?:them|it|you)\s+(?:in\s+)?|puts?\s+(?:\w+\s+){0,2}in(?:to)?))\s+"
    r"(?:(?i:the)\s+)?(?P<name>[A-Z][\w'\u2019\-]*(?:\s+(?:of|the|[A-Z][\w'\u2019\-]*))*)"
)
NUM_SEC = re.compile(r"\bfor\s+(?:up\s+to\s+)?(\d+(?:\.\d+)?)\s*(?:seconds?|secs?|s)\b", re.I)
STACKS = re.compile(
    r"stack(?:s|ing)?\s+(?:up\s+)?to\s+(\d+)|up\s+to\s+(\d+)\s+(?:times|stacks)|max(?:imum)?\s+(?:of\s+)?(\d+)\s+stacks", re.I
)
MORE = re.compile(r"\s*,?\s*(?:and\s+)?(?P<stats>[A-Za-z][A-Za-z '/&\-]{1,50}?)\s+by\s+(?P<pct>\d+(?:\.\d+)?%)", re.I)
CHANGE = re.compile(
    rf"(?P<dir>{UP}|{DOWN})\s+(?P<stats>[A-Za-z][A-Za-z ,'/&\-]{{1,90}}?)\s+by\s+(?P<pct>\d+(?:\.\d+)?%)", re.I
)
FLAGS = [
    (re.compile(r"\bcannot\s+heal|can'?t\s+heal|prevent\w*\s+(?:it\s+|them\s+)?(?:from\s+)?heal|heal(?:ing)?\s+block", re.I), "Cannot heal"),
    (re.compile(r"\bstun(?:s|ned|ning)?\b", re.I), "Stun"),
    (re.compile(r"\btaunt\w*|forc\w+\s+\w+\s+to\s+attack", re.I), "Taunt"),
    (re.compile(r"damage\s+over\s+time|\bDoT\b(?!\s+(?:damage|power))", re.I), "DoT"),
    (re.compile(r"heal\w*\s+(?:\w+\s+)?over\s+time|\bHoT\b(?!\s+(?:power|healing))", re.I), "HoT"),
]
OWNER = re.compile(
    r"(?i)^(?:their|your|its|the|all|and|target'?s?|caster'?s?|enemy|enemies'?|allies'?|you\s+and\s+your\s+allies'?|"
    r"the\s+target'?s?|the\s+caster'?s?|own)\s+"
)
SKIP_STAT = re.compile(
    r"(?i)^(?:you|your|you and your allies'?)$|^(?:it|them|they|this|that|which|while)\b|\b(?:for|while|when|if|then|seconds?)\b"
)
SKIP_NAME = re.compile(
    r"(?:Moderate|Minor|Light|Heavy|Massive|Strong|Damage|DoT|HoT|It|This|That|Your|Their|Its|All|Up|Each|Every|One|Two|Three|An?|Spell|Attack)\b"
)
ALIASES = {
    "crit": "Crit Chance", "critical chance": "Crit Chance", "crit chance": "Crit Chance", "critical hit chance": "Crit Chance",
    "critical strike chance": "Crit Chance", "crit damage": "Crit Damage", "critical damage": "Crit Damage",
    "critical hit damage": "Crit Damage", "hit chance": "Hit Chance", "dodge": "Dodge", "dodge chance": "Dodge",
    "haste": "Haste", "damage": "Damage", "outgoing damage": "Damage", "damage dealt": "Damage",
    "all outgoing damage": "Damage", "overall damage": "Damage", "incoming damage": "Damage taken",
    "damage taken": "Damage taken", "all incoming damage": "Damage taken", "mana consumption": "Mana cost",
    "mana costs": "Mana cost", "mana cost": "Mana cost", "dot damage": "DoT damage", "hot healing": "HoT healing",
}


def norm_stat(s: str) -> str:
    s = re.sub(r"\s+", " ", s.strip(" ,.'")).strip()
    while True:
        t = OWNER.sub("", s)
        if t == s:
            break
        s = t
    s = re.sub(r"(?i)^(?:the\s+)?(?:amount\s+of\s+)?(.+?)\s+(?:they|you|your\s+opponents|opponents|enemies|the\s+target|targets?)\s+takes?$",
               r"\1 taken", s)
    key = s.lower()
    if key in ALIASES:
        return ALIASES[key]
    return " ".join(w if w.isupper() or w in ("DoT", "HoT") else w[:1].upper() + w[1:] for w in s.split())


def split_stats(stats: str):
    out = []
    for p in re.split(r"\s*,\s*(?:and\s+)?|\s+and\s+|\s*&\s*|\s*/\s*", stats):
        p = p.strip()
        if not p or len(p) > 40 or SKIP_STAT.search(p):
            continue
        n = norm_stat(p)
        if n and not SKIP_STAT.search(n):
            out.append(n)
    return out


def effects_of(clause: str):
    """Stat changes and flags in one clause, in reading order, no duplicates."""
    out = []

    def add(x):
        if x and x not in out:
            out.append(x)

    for m in CHANGE.finditer(clause):
        pct = m.group("pct")
        # "increasing outgoing damage and reducing incoming damage by 50%": each verb keeps its own sign.
        sign = "+" if re.fullmatch(UP, m.group("dir"), re.I) else "-"
        for part in re.split(rf"\b({UP}|{DOWN})\b", m.group("stats"), flags=re.I):
            if not part or not part.strip():
                continue
            if re.fullmatch(UP, part.strip(), re.I):
                sign = "+"
            elif re.fullmatch(DOWN, part.strip(), re.I):
                sign = "-"
            else:
                for stat in split_stats(part):
                    add(f"{stat} {sign}{pct}")
        # "reducing Dodge by 35%, damage by 25%, and Crit Chance by 15%": the verb carries on.
        pos = m.end()
        while True:
            c = MORE.match(clause, pos)
            if not c or re.search(rf"\b(?:{UP}|{DOWN})\b", c.group("stats"), re.I):
                break
            for stat in split_stats(c.group("stats")):
                add(f"{stat} {sign}{c.group('pct')}")
            pos = c.end()
    for rx, label in FLAGS:
        if rx.search(clause):
            add(label)
    return out


def sentences(text: str):
    for line in text.replace("\r", "").split("\n"):
        line = line.strip(" |")
        for s in re.split(r"(?<=[.!?])\s+(?=[A-Z])", line):
            if s.strip():
                yield s.strip()


def clean_name(n: str) -> str:
    n = re.sub(r"(?:\s+(?:the|of))+$", "", n.strip())
    n = re.sub(r"\s+(?:Aura|Effect|Buff|Debuff|Status)$", "", n)
    return n.strip()


def build_class(skills):
    fx = {}

    def put(name, effects, dur, src, stacks):
        name = clean_name(name)
        if len(name) < 3 or not effects or SKIP_NAME.match(name):
            return
        cur = fx.get(name)
        if cur and len(cur["e"].split(", ")) >= len(effects):
            # Keep the reading with the most effects; fill in what it lacks.
            if not cur.get("dur") and dur:
                cur["dur"] = dur
            if not cur.get("max") and stacks:
                cur["max"] = stacks
            return
        rec = {"e": ", ".join(effects), "from": src}
        if dur or (cur and cur.get("dur")):
            rec["dur"] = dur or cur["dur"]
        if stacks or (cur and cur.get("max")):
            rec["max"] = stacks or cur["max"]
        fx[name] = rec

    for s in skills:
        src = s.get("n", "")
        text = str(s.get("d", ""))
        # Table lines inside passives: "Magician: 22% INT".
        for line in text.split("\n"):
            m = re.match(r"\s*([A-Z][\w'\u2019 \-]{2,40}):\s*(.+)$", line)
            if m and not re.match(r"(?i)notes?$", m.group(1)):
                val = m.group(2).strip().rstrip(".")
                if len(val) <= 80 and re.search(r"\d", val):
                    eff = effects_of(val)
                    chance = re.search(r"(\d+%)\s+chance", val, re.I)
                    if eff and chance:
                        eff.append(chance.group(1) + " chance")
                    put(m.group(1), eff or [val], None, src, None)
        for sent in sentences(text):
            for m in APPLY.finditer(sent):
                rest = sent[m.end():]
                nxt = APPLY.search(rest)            # the clause ends where the next aura is applied
                clause = rest[: nxt.start()] if nxt else rest
                dm = NUM_SEC.search(clause)
                sm = STACKS.search(clause)
                put(m.group("name"), effects_of(clause), float(dm.group(1)) if dm else None, src,
                    int(next(g for g in sm.groups() if g)) if sm else None)
    return fx


def main():
    data = json.loads(DATA.read_text(encoding="utf-8"))
    fx_all = {cls: fx for cls, skills in data["classes"].items() if (fx := build_class(skills))}
    total = sum(len(v) for v in fx_all.values())
    if "--check" in sys.argv:
        for cls in sys.argv[2:] or ("archmage", "void highlord", "lord of order", "legion revenant", "abyssal angel",
                                     "verus doomknight", "chaos avenger"):
            print("==", cls)
            for k, v in fx_all.get(cls, {}).items():
                print(f"   {k}: {v}")
        print(f"{total} aura effects in {len(fx_all)} classes")
        return
    data["fx"] = fx_all
    DATA.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"wrote {total} aura effects in {len(fx_all)} classes")


if __name__ == "__main__":
    main()
