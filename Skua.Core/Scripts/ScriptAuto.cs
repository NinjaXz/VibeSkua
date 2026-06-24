using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Skills;
using System.Diagnostics;

namespace Skua.Core.Scripts;

public partial class ScriptAuto : ObservableObject, IScriptAuto
{
    public ScriptAuto(
        ILogService logger,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptDrop> drops,
        Lazy<IScriptSkill> skills,
        Lazy<IScriptBoost> boosts,
        Lazy<IScriptOption> options,
        Lazy<IScriptMonster> monsters,
        Lazy<IScriptKill> kill,
        Lazy<IScriptWait> wait,
        Lazy<IScriptCombat> lazyCombat,
        Lazy<IScriptMap> lazyMap)
    {
        _logger = logger;
        _lazyPlayer = player;
        _lazyDrops = drops;
        _lazySkills = skills;
        _lazyBoosts = boosts;
        _lazyOptions = options;
        _lazyMonsters = monsters;
        _lazyKill = kill;
        _lazyWait = wait;
        _lazyCombat = lazyCombat;
        _lazyMap = lazyMap;
        _lastHuntTick = Environment.TickCount;
    }

    private readonly ILogService _logger;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptDrop> _lazyDrops;
    private readonly Lazy<IScriptSkill> _lazySkills;
    private readonly Lazy<IScriptBoost> _lazyBoosts;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptKill> _lazyKill;
    private readonly Lazy<IScriptMonster> _lazyMonsters;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptCombat> _lazyCombat;
    private readonly Lazy<IScriptMap> _lazyMap;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptDrop Drops => _lazyDrops.Value;
    private IScriptSkill Skills => _lazySkills.Value;
    private IScriptBoost Boosts => _lazyBoosts.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptKill Kill => _lazyKill.Value;
    private IScriptMonster Monsters => _lazyMonsters.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IScriptCombat Combat => _lazyCombat.Value;
    private IScriptMap Map => _lazyMap.Value;

    [ObservableProperty]
    private bool _isRunning;

    private Task? _autoTask;
    private CancellationTokenSource? _ctsAuto;

    public void StartAutoAttack(string? className = null, ClassUseMode classUseMode = ClassUseMode.Base, int[]? manualMapIDs = null)
    {
        _ctsAuto = new CancellationTokenSource();
        _DoActionAuto(hunt: false, className, classUseMode, manualMapIDs);
    }

    public void StartAutoHunt(string? className = null, ClassUseMode classUseMode = ClassUseMode.Base, int[]? manualMapIDs = null)
    {
        _ctsAuto = new CancellationTokenSource();
        _DoActionAuto(hunt: true, className, classUseMode, manualMapIDs);
    }

    public void Stop()
    {
        if (_ctsAuto is null)
        {
            IsRunning = false;
            return;
        }

        _ctsAuto?.Cancel();
        // Removed blocking Wait() calls to prevent UI thread deadlock
    }

    public async ValueTask StopAsync()
    {
        if (_ctsAuto is null)
        {
            IsRunning = false;
            return;
        }

        _ctsAuto?.Cancel();
        await Wait.ForTrueAsync(() => _ctsAuto is null && (_autoTask?.IsCompleted ?? true), 40);
        IsRunning = false;
    }

    private void _DoActionAuto(bool hunt, string? className = null, ClassUseMode classUseMode = ClassUseMode.Base, int[]? manualMapIDs = null)
    {
        if (_autoTask is { IsCompleted: false })
            return;

        if (!Player.LoggedIn)
            return;

        CheckDropsandBoosts();
        Options.InfiniteRange = true;
        if (className is not null)
            Skills.StartAdvanced(className, true, classUseMode);
        else
            Skills.StartAdvanced(Player.CurrentClass?.Name ?? "Generic", true);

        _autoTask = Task.Run(async () =>
        {
            try
            {
                if (hunt)
                    await _Hunt(_ctsAuto!.Token, manualMapIDs);
                else
                    await _Attack(_ctsAuto!.Token, manualMapIDs);
            }
            catch (OperationCanceledException) { } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"ScriptAuto task error: {ex}"); }
            finally
            {
                Drops.Stop();
                Skills.Stop();
                Boosts.Stop();
                Options.InfiniteRange = false;
                _ctsAuto?.Dispose();
                _ctsAuto = null;
                IsRunning = false;
            }
        });
        IsRunning = true;
    }

    private string _target = "";
    private int _targetMapID = -1;
    private int[]? _priorityMapIDs = null;

    private async Task _Attack(CancellationToken token, int[]? manualMapIDs = null)
    {
        Trace.WriteLine("Auto attack started.");
        Player.SetSpawnPoint();

        _priorityMapIDs = manualMapIDs;

        if (manualMapIDs?.Length > 0)
        {
            // Use manually specified MapIDs with priority
            _target = $"MapIDs:[{string.Join(", ", manualMapIDs)}]";
            _targetMapID = -1; // Will be determined by priority logic
        }
        else if (Player.HasTarget && !Combat.StopAttacking)
        {
            _target = Player.Target?.Name ?? "*";
            _targetMapID = Player.Target?.MapID ?? -1;
        }
        else if (!Combat.StopAttacking)
        {
            _target = "*";
            _targetMapID = -1;
        }

        _logger.ScriptLog($"[Auto Attack] Attacking {_target}");

        // Fast path flag for wildcard mode (no manual IDs, no specific target, target is "*")
        bool fastWildcard = (_priorityMapIDs is null || _priorityMapIDs.Length == 0) && _targetMapID <= 0 && (string.IsNullOrEmpty(_target) || _target == "*");

        while (!token.IsCancellationRequested)
        {
            if (_priorityMapIDs?.Length > 0)
            {
                // Sequential priority targeting - always prefer earlier indices if alive
                int? currentTarget = null;

                // Find the highest priority (lowest index) alive monster
                for (int i = 0; i < _priorityMapIDs.Length; i++)
                {
                    Monster? monster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _priorityMapIDs[i]);
                    bool alive = monster != null && monster.Alive;

                    if (alive)
                    {
                        currentTarget = _priorityMapIDs[i];
                        break;
                    }
                }

                if (currentTarget.HasValue)
                {
                    Combat.Attack(currentTarget.Value);
                    await Task.Delay(Math.Max(0, 200), token);
                    // Immediately re-evaluate priorities (no extra delay)
                    continue;
                }
                // No valid target found right now, short yield
                await Task.Delay(Math.Max(0, 50), token);
            }
            else if (_targetMapID > 0)
            {
                // Attack specific monster by MapID only
                if (Monsters.Exists(_targetMapID))
                {
                    if (Combat.Attack(_targetMapID))
                        Kill.Monster(_targetMapID, token);
                }
                await Task.Delay(Math.Max(0, Options.ActionDelay), token);
            }
            else if (_target is not "*" and not "")
            {
                // Target is a player (like yourself)
                Combat.AttackPlayer(_target);
                await Task.Delay(Math.Max(0, Options.ActionDelay), token);
            }
            else
            {
                if (fastWildcard)
                {
                    // Fast wildcard: only alive monsters, break after first successful engage
                    foreach (Monster monster in Monsters.MapMonsters)
                    {
                        if (token.IsCancellationRequested)
                            return;
                        if (!monster.Alive)
                            continue;
                        if (!Combat.Attack(monster.MapID))
                            continue;
                        Kill.Monster(monster.MapID, token);
                        break;
                    }
                    continue;
                }

                // Generic wildcard fallback
                List<Monster> monsters = Monsters.CurrentMonsters;
                foreach (Monster monster in monsters)
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (!Combat.Attack(monster.MapID))
                        continue;

                    Kill.Monster(monster.MapID, token);
                }
            }
        }
        Trace.WriteLine("Auto attack stopped.");
    }

    private int _lastHuntTick;

    private async Task _Hunt(CancellationToken token, int[]? manualMapIDs = null)
    {
        Trace.WriteLine("Auto hunt started.");

        _priorityMapIDs = manualMapIDs;

        if (manualMapIDs?.Length > 0)
        {
            // Use manually specified MapIDs with priority
            _target = $"MapIDs:[{string.Join(", ", manualMapIDs)}]";
            _targetMapID = -1; // Will be determined by priority logic
        }
        else if (Player.HasTarget && !Combat.StopAttacking)
        {
            _target = Player.Target?.Name ?? "*";
            _targetMapID = -1;
        }
        else if (!Combat.StopAttacking)
        {
            _target = string.Join('|', Monsters.CurrentMonsters.Select(m => m.Name));
            _targetMapID = -1;
        }

        _logger.ScriptLog($"[Auto Hunt] Hunting for {_target}");

        if (_priorityMapIDs?.Length > 0)
        {
            // Hunt with priority MapIDs
            while (!token.IsCancellationRequested)
            {
                // Sequential priority targeting - always prefer earlier indices if alive
                int? currentTarget = null;

                // Find the highest priority (lowest index) alive monster
                for (int i = 0; i < _priorityMapIDs.Length; i++)
                {
                    Monster? monster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _priorityMapIDs[i]);
                    bool alive = monster != null && monster.Alive;

                    if (alive)
                    {
                        currentTarget = _priorityMapIDs[i];
                        break;
                    }
                }

                if (currentTarget.HasValue)
                {
                    Monster? monster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == currentTarget.Value);
                    if (monster != null)
                    {
                        if (Player.Cell != monster.Cell && !token.IsCancellationRequested)
                        {
                            if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                                await Task.Delay(Math.Max(0, Options.HuntDelay - Environment.TickCount + _lastHuntTick), token);
                            Map.Jump(monster.Cell, "Left");
                            _lastHuntTick = Environment.TickCount;
                        }
                    }

                    if (Monsters.Exists(currentTarget.Value) && !token.IsCancellationRequested)
                    {
                        Monster? targetMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == currentTarget.Value);
                        while (targetMonster?.HP > 0 && !token.IsCancellationRequested)
                        {
                            Combat.Attack(currentTarget.Value);
                            await Task.Delay(Math.Max(0, Options.ActionDelay), token);
                            targetMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == currentTarget.Value);
                        }
                    }
                    await Task.Delay(Math.Max(0, 200), token);
                }
                else
                {
                    // No priority targets available, wait a bit
                    await Task.Delay(Math.Max(0, 500), token);
                }
            }
        }
        else if (_targetMapID > 0)
        {
            while (!token.IsCancellationRequested)
            {
                List<string> cells = Monsters.GetLivingMonsterDataLeafCells(_targetMapID);

                if (cells.Count == 0)
                {
                    await Task.Delay(Math.Max(0, 200), token);
                    continue;
                }

                foreach (string cell in cells)
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (Player.Cell != cell && !token.IsCancellationRequested)
                    {
                        if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                            await Task.Delay(Math.Max(0, Options.HuntDelay - Environment.TickCount + _lastHuntTick), token);
                        Map.Jump(cell, "Left");
                        _lastHuntTick = Environment.TickCount;
                    }

                    if (Monsters.Exists(_targetMapID) && !token.IsCancellationRequested)
                    {
                        Monster? targetMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _targetMapID);
                        while (targetMonster?.HP > 0 && !token.IsCancellationRequested)
                        {
                            Combat.Attack(_targetMapID);
                            await Task.Delay(Math.Max(0, Options.ActionDelay), token);
                            targetMonster = Monsters.MapMonsters.FirstOrDefault(m => m.MapID == _targetMapID);
                        }
                    }

                    await Task.Delay(Math.Max(0, 200), token);
                }
            }
        }
        else if (!string.IsNullOrEmpty(_target) && _target != "*")
        {
            while (!token.IsCancellationRequested)
            {
                List<string> cells = Monsters.GetLivingMonsterDataLeafCells(_target);

                if (cells.Count == 0)
                {
                    await Task.Delay(Math.Max(0, 200), token);
                    continue;
                }

                foreach (string cell in cells)
                {
                    if (token.IsCancellationRequested)
                        break;

                    if (Player.Cell != cell && !token.IsCancellationRequested)
                    {
                        if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                            await Task.Delay(Math.Max(0, Options.HuntDelay - Environment.TickCount + _lastHuntTick), token);
                        Map.Jump(cell, "Left");
                        _lastHuntTick = Environment.TickCount;
                    }

                    if (Monsters.Exists(_target) && !token.IsCancellationRequested)
                    {
                        Monster? targetMonster = Monsters.CurrentMonsters.FirstOrDefault(m => m.Name == _target);
                        while (targetMonster?.HP > 0 && !token.IsCancellationRequested)
                        {
                            Combat.Attack(_target);
                            await Task.Delay(Math.Max(0, Options.ActionDelay), token);
                            targetMonster = Monsters.CurrentMonsters.FirstOrDefault(m => m.Name == _target);
                        }
                    }

                    await Task.Delay(Math.Max(0, 200), token);
                }
            }
        }
        {
            string[] names = _target.Split('|');
            List<string> cells = names.SelectMany(n => Monsters.GetLivingMonsterDataLeafCells(n)).Distinct().ToList();

            while (!token.IsCancellationRequested)
            {
                for (int i = cells.Count - 1; i >= 0; i--)
                {
                    if (Player.Cell != cells[i] && !token.IsCancellationRequested)
                    {
                        if (Environment.TickCount - _lastHuntTick < Options.HuntDelay)
                            await Task.Delay(Math.Max(0, Options.HuntDelay - Environment.TickCount + _lastHuntTick), token);
                        Map.Jump(cells[i], "Left");
                        _lastHuntTick = Environment.TickCount;
                    }

                    foreach (string mon in names)
                    {
                        if (token.IsCancellationRequested)
                            break;

                        if (Monsters.Exists(mon) && !token.IsCancellationRequested)
                        {
                            if (!Combat.Attack(mon))
                            {
                                cells.RemoveAt(i);
                                continue;
                            }
                            await Task.Delay(Math.Max(0, Options.ActionDelay), token);
                            Kill.Monster(mon, token);
                            break;
                        }

                        cells.RemoveAt(i);
                    }
                }
            }
        }
        Trace.WriteLine("Auto hunt stopped.");
    }

    private void CheckDropsandBoosts()
    {
        if (Drops.ToPickupIDs.Any() || Drops.ToPickup.Any())
            Drops.Start();

        if (Boosts.UsingBoosts)
            Boosts.Start();
    }
}
