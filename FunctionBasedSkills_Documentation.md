# The VibeSkua Scripter's Guide to Function-Based Skills

Welcome! If you are reading this, you are ready to move past the limitations of the traditional `AdvancedSkills.json` builder and start writing hyper-optimized, native C# combat logic for your bots.

This documentation contains absolutely everything a scripter needs to know to build, optimize, and run function-based skills in VibeSkua.

---

## 1. What is Function-Based Combat?
Traditionally, VibeSkua parses JSON strings to figure out what skills to use. This is heavy on RAM and limits your logic. 

**Function-Based Combat** allows you to completely bypass the JSON system. Instead, you write a raw C# class that directly hooks into VibeSkua's combat engine. This gives you zero RAM overhead and infinite logical control (like checking exact millisecond durations on auras, writing complex AND/OR statements, or pulling math directly from the player's stats).

---

## 2. The Required Boilerplate
Every function-based skillset is simply a C# class that implements the `ISkillProvider` interface. 
Here is the blank template you should use to start every new class:

```csharp
using Skua.Core.Interfaces;
using System;
using System.IO;

public class MyClassSkillProvider : ISkillProvider
{
    // VibeSkua API References
    private IScriptPlayer _player;
    private IScriptSelfAuras _self;
    private IScriptTargetAuras _target;
    private IScriptCombat _combat;
    
    // Priority Array (The order in which skills are checked)
    private int[] _priority = new int[] { 1, 2, 3, 4 };
    private int _ptr = 0;
    
    public bool ResetOnTarget { get; set; } = true;
    public int SkillCount => 4;

    // Initializes the API references when the script starts
    public void Init(IScriptPlayer player, IScriptSelfAuras self, IScriptTargetAuras target, IScriptCombat combat, IFlashUtil flash)
    {
        _player = player;
        _self = self;
        _target = target;
        _combat = combat;
    }

    // Required cleanup methods
    public void OnTargetReset() { if (ResetOnTarget) _ptr = 0; }
    public void Stop() { _combat.CancelAutoAttack(); _combat.CancelTarget(); _ptr = 0; }
    public void OnPlayerDeath() { _ptr = 0; }
    public void Save(string file) { }
    public void Load(string file) { }

    // Logic goes below...
```

---

## 3. The `GetNextSkill` Loop (Optimizing Ticks)

VibeSkua's background combat thread will repeatedly call `GetNextSkill()` to ask your script what to do next.

**CRITICAL RULE:** Do not simply return a skill and hope it works! If you return a skill that is on cooldown, VibeSkua will reject it and wait 100ms before asking you again. This results in "wasted ticks" and a very slow bot.

Instead, use this **internal loop**. In a single microsecond, it scans your priority list, skips anything on cooldown, asks your logic if the skill is safe to use, and immediately returns the perfect skill:

```csharp
    public (int, int) GetNextSkill()
    {
        var bot = IScriptInterface.Instance;
        int startIndex = _ptr;
        
        do
        {
            int skill = _priority[_ptr];
            _ptr = (_ptr + 1) % _priority.Length;

            // Instantly skip skills that are on cooldown!
            if (bot.Skills.CanUseSkill(skill))
            {
                // Ask our custom logic below if we should actually use the skill
                if (ShouldUseSkill(skill, true) == true)
                {
                    return (skill, skill);
                }
            }
        } while (_ptr != startIndex);

        // If no skills are ready, do nothing this tick
        return (-1, -1);
    }
```

---

## 4. Writing the Logic (`ShouldUseSkill`)

This is where you write your actual combat decisions using raw C# `if` statements.

### A. The 0-Based Indexing Rule
VibeSkua's internal skill array starts at `0`. You MUST use these numbers when referring to skills:
* `0` = Auto Attack (Slot 1)
* `1` = First Class Ability (Slot 2)
* `2` = Second Class Ability (Slot 3)
* `3` = Third Class Ability (Slot 4)
* `4` = Ultimate/Nuke (Slot 5)

### B. Reading Player Data
You can use mathematical calculations directly inside the method. For example, grabbing an accurate HP percentage:
```csharp
double hpPercent = _player.MaxHealth > 0 ? (_player.Health / (double)_player.MaxHealth) : 0;
```

### C. Reading Auras and Durations
`GetAura("Aura Name")` returns an aura object. You can check if it exists (`!= null`) and exactly how many seconds it has left (`RemainingTime`).

### D. Putting It All Together (Example)
Here is how you combine AND (`&&`) and OR (`||`) operators to create an incredibly smart skillset:

```csharp
    public bool? ShouldUseSkill(int skillIndex, bool canUse)
    {
        if (!_player.Alive || !_player.HasTarget) return false;

        double hpPercent = _player.MaxHealth > 0 ? (_player.Health / (double)_player.MaxHealth) : 0;

        // Skill 1 (Slot 2) - Defensive Heal
        if (skillIndex == 1)
        {
            var myBuff = _self.GetAura("Defensive Stance");
            
            // Logic: Cast ONLY if HP is below 50% AND the aura has less than 2 seconds left!
            if (hpPercent <= 0.50 && (myBuff == null || myBuff.RemainingTime <= 2))
            {
                return true; 
            }
            return false;
        }

        // Skill 4 (Slot 5) - The Nuke
        if (skillIndex == 4)
        {
            // Logic: Cast ONLY if HP is totally safe (> 80%) so we don't accidentally die during the animation
            if (hpPercent > 0.80)
            {
                return true;
            }
            return false;
        }

        // Default: If it's any other skill (like 2 or 3), just cast it on cooldown
        return true;
    }
}
```

---

## 6. Compiling & Running

Once you've written your C# class, save it (e.g., `MyCustomSkills.cs`). 
Inside your main bot script, you inject it into the combat loop using `StartCompiled`:

```csharp
public void ScriptMain(IScriptInterface bot)
{
    // 1. Enable the engine bypass toggle (Disables AdvancedSkills.json RAM usage)
    bot.Options.UseFunctionBasedSkills = true;
    
    // 2. Point VibeSkua to your file. It will compile the C# into a DLL instantly at runtime!
    string skillFile = Path.Combine(AppContext.BaseDirectory, "MyCustomClass.cs");
    bot.Skills.StartCompiled(skillFile);
    
    // 3. Keep the script alive or run a hunt loop!
    bot.Hunt.Monster("Blood Titan");
}
```
