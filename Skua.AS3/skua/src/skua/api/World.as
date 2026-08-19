package skua.api {
import flash.display.DisplayObject;
import flash.events.TimerEvent;
import flash.utils.Timer;

import skua.Main;

public class World {
    private static var _fxStore:Object = {};
    private static var _fxLastOpt:Boolean = false;

    private static var _lastGoodFrame:String = "";
    private static var _lastGoodPad:String = "";

    private static const PAD_NAMES_REGEX:RegExp =
        /(Spawn|Center|Left|Right|Up|Down|Top|Bottom)/;

    public function World() {
        super();
    }

    public static function jumpCorrectRoom(
        cell:String,
        pad:String,
        autoCorrect:Boolean = true,
        clientOnly:Boolean = false
    ):void {
        var world:* = Main.instance.game.world;

        if (!autoCorrect) {
            world.moveToCell(cell, pad, clientOnly);
            return;
        }

        var users:Vector.<String> = world.areaUsers.concat();

        var myIndex:int =
            users.indexOf(Main.instance.game.sfc.myUserName);

        if (myIndex != -1)
            users.splice(myIndex, 1);

        if (users.length <= 1) {
            world.moveToCell(cell, pad, clientOnly);
        } else {
            var uoTree:* = world.uoTree;
            var usersCell:String = world.strFrame;
            var usersPad:String = "Left";

            for (var i:int = 0; i < users.length; i++) {
                var userObj:* = uoTree[users[i]];

                if (userObj == null)
                    continue;

                usersCell = userObj.strFrame;
                usersPad = userObj.strPad;

                if (cell == usersCell && pad != usersPad)
                    break;
            }

            world.moveToCell(cell, usersPad, clientOnly);
        }

        var jumpTimer:Timer = new Timer(50, 1);
        jumpTimer.addEventListener(TimerEvent.TIMER, jumpTimerEvent);
        jumpTimer.start();

        function jumpTimerEvent(e:TimerEvent):void {
            jumpCorrectPad(cell, clientOnly);

            jumpTimer.stop();
            jumpTimer.removeEventListener(
                TimerEvent.TIMER,
                jumpTimerEvent
            );
        }
    }

    public static function jumpCorrectPad(
        cell:String,
        clientOnly:Boolean = false
    ):void {
        var cellPad:String = "Left";
        var padArr:Array = getCellPads();
        var world:* = Main.instance.game.world;

        if (padArr.indexOf(cellPad) >= 0) {
            if (world.strPad == cellPad)
                return;

            world.moveToCell(cell, cellPad, clientOnly);
        } else if (padArr.length > 0) {
            cellPad = padArr[0];

            if (world.strPad == cellPad)
                return;

            world.moveToCell(cell, cellPad, clientOnly);
        }
    }

    public static function getCellPads():Array {
        var cellPads:Array = [];
        var cellPadsCnt:int =
            Main.instance.game.world.map.numChildren;

        for (var i:int = 0; i < cellPadsCnt; ++i) {
            var child:DisplayObject =
                Main.instance.game.world.map.getChildAt(i);

            if (PAD_NAMES_REGEX.test(child.name))
                cellPads.push(child.name);
        }

        return cellPads;
    }

    public static function disableDeathAd(enable:Boolean):void {
        Main.instance.game.userPreference.data.bDeathAd = !enable;
    }

    public static function skipCutscenes():void {
        var game:* = Main.instance.game;
        var world:* = game.world;
        var extSWF:* = game.mcExtSWF;

        /*
         * No cutscene is active.
         *
         * Remember the current location as the last known good
         * location and leave the player completely alone.
         */
        if (extSWF.numChildren <= 0) {
            if (world.strFrame != "") {
                _lastGoodFrame = world.strFrame;
                _lastGoodPad = world.strPad;
            }

            return;
        }

        /*
         * A cutscene is active.
         *
         * Remove the cutscene overlay.
         */
        while (extSWF.numChildren > 0)
            extSWF.removeChildAt(0);

        game.showInterface();

        /*
         * Return to the last known good location.
         *
         * If we haven't recorded one yet, fall back to Enter/Spawn.
         */
        var targetFrame:String = _lastGoodFrame != ""
            ? _lastGoodFrame
            : "Enter";

        var targetPad:String = _lastGoodPad != ""
            ? _lastGoodPad
            : "Spawn";

        /*
         * Let AQW finish removing the cutscene before jumping.
         */
        var jumpTimer:Timer = new Timer(50, 1);
        jumpTimer.addEventListener(TimerEvent.TIMER, performJump);
        jumpTimer.start();

        function performJump(e:TimerEvent):void {
            jumpTimer.stop();
            jumpTimer.removeEventListener(
                TimerEvent.TIMER,
                performJump
            );

            jumpCorrectRoom(
                targetFrame,
                targetPad,
                true,
                false
            );

            /*
             * Allow the room/pad correction to finish before
             * refreshing the display.
             */
            var refreshTimer:Timer = new Timer(250, 1);
            refreshTimer.addEventListener(
                TimerEvent.TIMER,
                performRefresh
            );
            refreshTimer.start();

            function performRefresh(e:TimerEvent):void {
                refreshTimer.stop();
                refreshTimer.removeEventListener(
                    TimerEvent.TIMER,
                    performRefresh
                );

                refreshWorld();
            }
        }
    }

    private static function refreshWorld():void {
        /*
         * AQW can leave the world display black after a cutscene.
         *
         * Do the same visibility transition as toggling Lag Killer,
         * which forces Flash to redraw the world.
         */
        killLag(true);

        var refreshTimer:Timer = new Timer(100, 1);
        refreshTimer.addEventListener(
            TimerEvent.TIMER,
            restoreWorld
        );
        refreshTimer.start();

        function restoreWorld(e:TimerEvent):void {
            refreshTimer.stop();
            refreshTimer.removeEventListener(
                TimerEvent.TIMER,
                restoreWorld
            );

            killLag(false);
        }
    }

    public static function hidePlayers(enabled:Boolean):void {
        var world:* = Main.instance.game.world;
        var currentFrame:String = world.strFrame;

        for each (var avatar:* in world.avatars) {
            if (avatar != null &&
                avatar.pnm != null &&
                !avatar.isMyAvatar) {

                if (enabled) {
                    avatar.hideMC();
                } else if (avatar.strFrame == currentFrame) {
                    avatar.showMC();
                }
            }
        }
    }

    public static function disableFX(enabled:Boolean):void {
        if (!_fxLastOpt && enabled)
            _fxStore = {};

        _fxLastOpt = enabled;

        for each (var avatar:* in Main.instance.game.world.avatars) {
            if (enabled) {
                if (avatar.pMC.spFX != null)
                    _fxStore[avatar.uid] = avatar.rootClass.spFX;

                avatar.rootClass.spFX = null;
            } else {
                avatar.rootClass.spFX = _fxStore[avatar.uid];
            }
        }
    }

    public static function killLag(enable:Boolean):void {
        Main.instance.game.world.visible = !enable;

        if (Main.instance.customBGLagKiller)
            Main.instance.customBGLagKiller.visible = enable;
    }
}
}