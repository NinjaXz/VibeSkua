package skua.module {
import flash.events.Event;
import flash.utils.getTimer;

import skua.Main;

public class Modules {
    private static var _modules:* = {};

    public static function getModule(name:String):Module {
        return _modules[name];
    }

    public static function registerModule(m:Module):void {
        _modules[m.name] = m;
    }

    public static function enable(name:String):void {
        var module:Module = getModule(name);
        if (module != null) {
            var toggle:Boolean = !module.enabled;
            module.enabled = true;
            if (toggle) {
                module.onToggle(Main.instance.getGame());
            }
        }
    }

    public static function disable(name:String):void {
        var module:Module = getModule(name);
        if (module != null) {
            var toggle:Boolean = module.enabled;
            module.enabled = false;
            if (toggle) {
                module.onToggle(Main.instance.getGame());
            }
        }
    }

    private static const FRAME_UPDATE_INTERVAL:int = 100;
    private static var _lastFrameUpdate:int = 0;

    public static function handleFrame(e:Event):void {
        var now:int = getTimer();
        if (now - _lastFrameUpdate < FRAME_UPDATE_INTERVAL) {
            return;
        }
        _lastFrameUpdate = now;

        var game:* = Main.instance.getGame();
        if (game == null) {
            return;
        }

        for (var name:String in _modules) {
            var module:Module = _modules[name];
            if (module.enabled) {
                module.onFrame(game);
            }
        }
    }

    public static function init():void {
        registerModule(new QuestRequirementWiki());
        registerModule(new QuestItemRates());
        registerModule(new HidePlayers());
        registerModule(new DisableCollisions());
        registerModule(new DisableFX());
    }
}
}