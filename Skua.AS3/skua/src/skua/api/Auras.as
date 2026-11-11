package skua.api {

import flash.display.BitmapData;
import flash.display.DisplayObject;
import flash.display.MovieClip;
import flash.events.TimerEvent;
import flash.geom.Matrix;
import flash.geom.Rectangle;
import flash.utils.ByteArray;
import flash.utils.Timer;
import flash.utils.getTimer;

import skua.Main;

public class Auras {
    private static var _cleanupTimer:Timer = null;
    private static var _initTimer:Timer = null;
    private static var _initialized:Boolean = false;

    public function Auras() {
        super();
    }

    public static function initialize():void {
        if (_initialized) {
            return;
        }
        
        _initTimer = new Timer(500);
        _initTimer.addEventListener(TimerEvent.TIMER, checkGameReady);
        _initTimer.start();
    }

    private static function checkGameReady(event:TimerEvent):void {
        try {
            var world:* = Main.instance.game.world;
            if (world && world.uoTree && world.monTree) {
                _initTimer.stop();
                _initTimer.removeEventListener(TimerEvent.TIMER, checkGameReady);
                _initTimer = null;
                _initialized = true;
                Main.instance.external.debug("World load check passed, initializing Auras API.");
                
                startAuraAutoCleanup(5);
            }
        } catch (e:Error) {
        }
    }

    public static function GetAuraSnapshots(subject:String):String {
        var world:* = Main.instance.game.world;
        var leaf:* = subject == "Self"
            ? world.uoTree[Main.instance.game.sfc.myUserName.toLowerCase()]
            : (world.myAvatar.target != null ? world.myAvatar.target.dataLeaf : null);
        var snapshots:Array = [];
        if (leaf == null || leaf.hudAuras == null) {
            return JSON.stringify(snapshots);
        }
        var now:int = getTimer();
        for (var name:String in leaf.hudAuras) {
            var aura:* = leaf.hudAuras[name];
            if (aura == null) {
                continue;
            }
            snapshots.push({
                nam: name,
                n: int(aura.n),
                dur: Number(aura.dur),
                remaining: Number(aura.end) > 0 ? Math.max(0, (Number(aura.end) - now) / 1000) : 0,
                persist: aura.persist == true,
                icon: aura.icon != null ? String(aura.icon) : "",
                desc: world.auraDescs != null && world.auraDescs[name] != null ? String(world.auraDescs[name]) : "",
                seq: int(aura.seq)
            });
        }
        snapshots.sortOn("seq", Array.NUMERIC);
        return JSON.stringify(snapshots);
    }

    public static function getSubjectAuras(subject:String):Array {
        if (subject == 'Self')
        {
            var userObj:* = Main.instance.game.world.uoTree[Main.instance.game.sfc.myUserName.toLowerCase()];
            return rebuildAuraArray(userObj.auras)
        }
        else
        {
            var monID:int = 0;
            if (Main.instance.game.world.myAvatar.target != null) {
                monID = Main.instance.game.world.myAvatar.target.dataLeaf.MonMapID;
            }
            var monObj:* = Main.instance.game.world.monTree[monID];
            return rebuildAuraArray(monObj.auras)
        }
    }

    public static function  rebuildAuraArray(auras:Object):Array {
        var rebuiltAuras:Array = [];
        if (!auras) {
            return rebuiltAuras;
        }
        
        for (var i:int = 0; i < auras.length; i++) {
            var aura:Object = auras[i];
            
            if (!aura) {
                continue;
            }
            
            if (!aura.hasOwnProperty("nam") || !aura.nam) {
                continue;
            }
            
            if (aura.e == 1) {
                continue;
            }
            
            var rebuiltAura:Object = {};
            var hasVal:Boolean = false;
            
            for (var key:String in aura) {
                if (key == "cLeaf") {
                    rebuiltAura[key] = "cycle_";
                } else if (key == "val") {
                    var rawVal:* = aura[key];
                    if (rawVal == null || isNaN(rawVal)) {
                        rebuiltAura[key] = 1;
                    } else {
                        rebuiltAura[key] = rawVal;
                    }
                    hasVal = true;
                } else {
                    rebuiltAura[key] = aura[key];
                }
            }
            if (!hasVal) {
                rebuiltAura.val = 1;
            }
            
            rebuiltAuras.push(rebuiltAura);
        }
        
        return rebuiltAuras;
    }

    public static function rebuilduoTree(playerName:String):Object {
        var plrUser:String = playerName.toLowerCase();
        var userObj:* = Main.instance.game.world.uoTree[plrUser];
        if (!userObj) {
            return {};
        }

        var rebuiltObj:Object = {};
        for (var prop:String in userObj) {
            if (prop == "auras") {
                rebuiltObj[prop] = rebuildAuraArray(userObj.auras);
            } else {
                rebuiltObj[prop] = userObj[prop];
            }
        }

        return rebuiltObj;
    }

    public static function rebuildmonTree(monID:int):Object {
        var monObj:* = Main.instance.game.world.monTree[monID];
        if (!monObj) {
            return {};
        }
        var rebuiltObj:Object = {};
        for (var prop:String in monObj) {
            if (prop == "auras") {
                rebuiltObj[prop] = rebuildAuraArray(monObj.auras);
            } else {
                rebuiltObj[prop] = monObj[prop];
            }
        }
        return rebuiltObj;
    }

    public static function HasAnyActiveAura(subject:String, auraNames:String):String {
        var auraList:Array = auraNames.split(',');
        var auras:Object = null;
        try {
            auras = getSubjectAuras(subject);
        } catch (e:Error) {
            return false.toString();
        }

        var auraCount:int = auras.length;
        var auraListCount:int = auraList.length;
        
        for (var i:int = 0; i < auraCount; i++) {
            var aura:Object = auras[i];
            var auraNameLower:String = aura.nam.toLowerCase();
            for (var j:int = 0; j < auraListCount; j++) {
                if (auraNameLower == auraList[j].toLowerCase().replace(/^\s+|\s+$/g, "")) {
                    return true.toString();
                }
            }
        }
        return false.toString();
    }

    public static function GetAurasValue(subject:String, auraName:String):Number {
        var aura:Object = null;
        var auras:Array = null;
        try {
            auras = getSubjectAuras(subject);
        } catch (e:Error) {
            return 442;
        }

        var lowerAuraName:String = auraName.toLowerCase();
        for (var i:int = 0; i < auras.length; i++) {
            aura = auras[i];
            if (aura.nam.toLowerCase() == lowerAuraName) {
                return aura.val;
            }
        }
        return 444;
    }

    public static function GetPlayerAura(playerName:String):String {
        var plrUser:String = playerName.toLowerCase();
        try {
            var userObj:* = Main.instance.game.world.uoTree[plrUser];
            if (!userObj) {
                return '[]';
            }
            return JSON.stringify(rebuildAuraArray(userObj.auras))
        }
        catch (e:Error) {
        }
        return '[]';
    }

    public static function GetMonsterAuraByName(monsterName:String):String {

        try {
            var monID:int = 0;
            var lowerMonsterName:String = monsterName.toLowerCase();
            for each (var monster:* in Main.instance.game.world.monsters)
            {
                if (monster && monster.objData.strMonName.toLowerCase() == lowerMonsterName) {
                    monID = monster.objData.MonMapID
                }
            }
            var monObj:* = Main.instance.game.world.monTree[monID];
            if (!monObj) {
                return 'Error: Couldn\'t get Monster Object Tree';
            }
            return JSON.stringify(rebuildAuraArray(monObj.auras));
        }
        catch (e:Error) {
        }
        return '[]';
    }

    public static function GetMonsterAuraByID(monID:int):String {
        try {
            var monObj:* = Main.instance.game.world.monTree[monID];
            if (!monObj) {
                return 'Error: Couldn\'t get Monster Object Tree';
            }
            return JSON.stringify(rebuildAuraArray(monObj.auras));
        }
        catch (e:Error) {
        }
        return '[]';
    }

    private static function startAuraAutoCleanup(intervalSeconds:int = 5):void {
        if (_cleanupTimer != null) {
            return;
        }
        
        _cleanupTimer = new Timer(intervalSeconds * 1000);
        _cleanupTimer.addEventListener(TimerEvent.TIMER, onCleanupTimer);
        _cleanupTimer.start();
    }

    public static function stopAuraAutoCleanup():void {
        if (_cleanupTimer == null) {
            return;
        }
        
        _cleanupTimer.stop();
        _cleanupTimer.removeEventListener(TimerEvent.TIMER, onCleanupTimer);
        _cleanupTimer = null;
    }

    private static function onCleanupTimer(event:TimerEvent):void {
        var world:* = Main.instance.game.world;
        if (!world) {
            return;
        }
        
        var cleanedCount:int = 0;
        
        // Clean player auras in uoTree
        for (var playerName:String in world.uoTree) {
            var userObj:* = world.uoTree[playerName];
            if (userObj && userObj.auras is Array) {
                cleanedCount += cleanExpiredAuras(userObj.auras);
            }
        }
        
        // Clean monster auras in monTree
        for (var monID:String in world.monTree) {
            var monObj:* = world.monTree[monID];
            if (monObj && monObj.auras is Array) {
                cleanedCount += cleanExpiredAuras(monObj.auras);
            }
        }
    }

    private static function cleanExpiredAuras(auras:Array):int {
        var removedCount:int = 0;
        for (var i:int = auras.length - 1; i >= 0; i--) {
            var aura:Object = auras[i];
            if (!aura || !aura.hasOwnProperty("nam") || !aura.nam || aura.e == 1) {
                auras.splice(i, 1);
                removedCount++;
            }
        }
        return removedCount;
    }

    private static const B64:String = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    // Aura Tracker snapshot of "Self" or "Target": the hud records (stacks, icon, time left in the game's
    // own clock) joined with dataLeaf.auras, where AQW keeps one record per aura name per caster
    // (World.updateAuraData matches nam, casterType and casterId). A re-cast by the same caster only
    // updates dur/val, so ts is when that caster first applied it. Caster names are resolved here:
    // players from world.avatars[entID], monsters from world.getMonster(MonMapID).
    // 
    public static function GetAuraTracker(subject:String):String {
        var out:Object = {target: null, myId: null, auras: []};
        try {
            var world:* = Main.instance.game.world;
            if (world == null || world.myAvatar == null) {
                return JSON.stringify(out);
            }
            out.myId = world.myAvatar.dataLeaf != null ? world.myAvatar.dataLeaf.entID : null;
            var avatar:* = subject == "Self" ? world.myAvatar : world.myAvatar.target;
            if (avatar == null || avatar.dataLeaf == null) {
                return JSON.stringify(out);
            }
            var leaf:* = avatar.dataLeaf;
            if (subject != "Self") {
                out.target = {
                    name: avatar.objData != null ? String(avatar.objData.strMonName || avatar.objData.strUsername || "") : "",
                    mapId: leaf.MonMapID != null ? leaf.MonMapID : null,
                    isPlayer: leaf.MonMapID == null
                };
            }
            if (leaf.hudAuras == null) {
                return JSON.stringify(out);
            }
            var now:int = getTimer();
            var wall:Number = new Date().getTime();
            var casters:Object = {};
            var latest:Object = {};
            if (leaf.auras is Array) {
                for each (var rec:* in leaf.auras) {
                    if (rec == null || !rec.nam || rec.e == 1) {
                        continue;
                    }
                    var nam:String = String(rec.nam);
                    var ts:Number = Number(rec.ts) || 0;
                    if (latest[nam] == null || ts > Number(latest[nam].ts)) {
                        latest[nam] = rec;
                    }
                    if (rec.casterType != "p" && rec.casterType != "m") {
                        continue;
                    }
                    var list:Array = casters[nam] as Array;
                    if (list == null) {
                        list = [];
                        casters[nam] = list;
                    }
                    var found:Object = null;
                    for each (var c:Object in list) {
                        if (c.t == rec.casterType && c.id == rec.casterId) {
                            found = c;
                            break;
                        }
                    }
                    if (found == null) {
                        list.push(describeCaster(world, String(rec.casterType), rec.casterId, ts));
                    } else if (ts > found.ts) {
                        found.ts = ts;
                    }
                }
            }
            for (var name:String in leaf.hudAuras) {
                var hud:* = leaf.hudAuras[name];
                if (hud == null) {
                    continue;
                }
                var last:* = latest[name];
                var fromHud:Number = Number(hud.end) > 0 ? (Number(hud.end) - now) / 1000 : -1;
                // A stack that falls off moves only the hud end, and a monster's re-cast aura keeps its
                // old hud end: the later of the two readings is the one that holds.
                var fromCast:Number = last != null && Number(last.ts) > 0 && Number(last.dur) > 0
                    ? (Number(last.ts) + Number(last.dur) * 1000 - wall) / 1000 : -1;
                var byCaster:Array = casters[name] as Array;
                if (byCaster == null) {
                    byCaster = [];
                }
                byCaster.sortOn("ts", Array.NUMERIC | Array.DESCENDING);
                out.auras.push({
                    nam: name,
                    n: Math.max(1, int(hud.n) || (last != null ? int(last.val) : 1)),
                    dur: Number(hud.dur) || (last != null ? Number(last.dur) : 0),
                    remaining: Math.max(fromHud, fromCast),
                    persist: hud.persist == true || (fromHud < 0 && fromCast < 0),
                    icon: hud.icon != null ? String(hud.icon) : (last != null && last.icon != null ? String(last.icon) : ""),
                    desc: world.auraDescs != null && world.auraDescs[name] != null ? String(world.auraDescs[name]) : "",
                    seq: int(hud.seq),
                    casters: byCaster
                });
            }
            out.auras.sortOn("seq", Array.NUMERIC);
        } catch (e:Error) {
        }
        return JSON.stringify(out);
    }

    private static function describeCaster(world:*, type:String, id:*, ts:Number):Object {
        var c:Object = {t: type, id: id, ts: ts, name: "", cls: "", me: false};
        try {
            if (type == "p") {
                c.me = world.myAvatar.dataLeaf != null && world.myAvatar.dataLeaf.entID == id;
                var av:* = world.avatars[id];
                if (av == null) {
                    av = world.avatars[String(id)];
                }
                if (av != null) {
                    c.name = av.objData != null && av.objData.strUsername ? String(av.objData.strUsername) : String(av.pnm || "");
                    c.cls = av.objData != null && av.objData.strClassName ? String(av.objData.strClassName) : "";
                }
            } else {
                var mon:* = world.getMonster(int(id));
                if (mon != null && mon.objData != null) {
                    c.name = String(mon.objData.strMonName || "");
                }
            }
        } catch (e:Error) {
        }
        return c;
    }

    // Draws an aura icon from the game's art (the hud record's icon link; "a,b" lists take the last)
    // into a square bitmap and returns "size:base64 ARGB pixels", or "" when it can't be drawn.
    // 
    public static function GetAuraIcon(link:String, size:int):String {
        try {
            var world:* = Main.instance.game.world;
            if (link.indexOf(",") > -1) {
                var parts:Array = link.split(",");
                link = String(parts[parts.length - 1]);
            }
            size = Math.max(8, Math.min(64, size));
            var cls:Class = world.getClass(link) as Class;
            if (cls == null) {
                return "";
            }
            var d:DisplayObject = new cls() as DisplayObject;
            if (d == null) {
                return "";
            }
            if (d is MovieClip) {
                MovieClip(d).gotoAndStop(1);
            }
            var r:Rectangle = d.getBounds(d);
            if (!(r.width > 0 && r.height > 0)) {
                return "";
            }
            var k:Number = size / Math.max(r.width, r.height);
            var m:Matrix = new Matrix(k, 0, 0, k, -r.x * k + (size - r.width * k) / 2, -r.y * k + (size - r.height * k) / 2);
            var bmd:BitmapData = new BitmapData(size, size, true, 0);
            bmd.draw(d, m, null, null, null, true);
            var px:ByteArray = bmd.getPixels(bmd.rect);
            bmd.dispose();
            return size + ":" + toBase64(px);
        } catch (e:Error) {
        }
        return "";
    }

    private static function toBase64(b:ByteArray):String {
        var out:Array = [];
        var n:int = int(b.length);
        var i:int = 0;
        while (i < n) {
            var b0:int = int(b[i]);
            var b1:int = i + 1 < n ? int(b[i + 1]) : 0;
            var b2:int = i + 2 < n ? int(b[i + 2]) : 0;
            out.push(B64.charAt(b0 >> 2), B64.charAt(((b0 & 3) << 4) | (b1 >> 4)),
                i + 1 < n ? B64.charAt(((b1 & 15) << 2) | (b2 >> 6)) : "=",
                i + 2 < n ? B64.charAt(b2 & 63) : "=");
            i += 3;
        }
        return out.join("");
    }
}
}
