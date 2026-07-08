package skua.api {
import flash.events.MouseEvent;
import flash.utils.getQualifiedClassName;

import skua.Main;

public class Player {
    private static const DROP_PARSE_REGEX:RegExp = /(.*)\s+x\s*(\d*)/;

    public function Player() {
        super();
    }

    public static function walkTo(xPos:int, yPos:int, walkSpeed:int):void {
        walkSpeed = (walkSpeed == 8 ? Main.instance.game.world.WALKSPEED : walkSpeed);
        Main.instance.game.world.myAvatar.pMC.walkTo(xPos, yPos, walkSpeed);
        Main.instance.game.world.moveRequest({
            'mc': Main.instance.game.world.myAvatar.pMC,
            'tx': xPos,
            'ty': yPos,
            'sp': walkSpeed
        });
    }

    public static function untargetSelf():void {
        var target:* = Main.instance.game.world.myAvatar.target;
        if (target && target == Main.instance.game.world.myAvatar) {
            Main.instance.game.world.cancelTarget();
        }
    }

    public static function attackPlayer(name:String):String {
        var player:* = Main.instance.game.world.getAvatarByUserName(name.toLowerCase());
        if (player != null && player.pMC != null) {
            Main.instance.game.world.setTarget(player);
            Main.instance.game.world.approachTarget();
            return true.toString();
        }
        return false.toString();
    }

    public static function getAvatar(id:int):String {
        return JSON.stringify(Main.instance.game.world.avatars[id].objData);
    }

    public static function isLoggedIn():String {
        return (Main.instance.game != null && Main.instance.game.sfc != null && Main.instance.game.sfc.isConnected).toString();
    }

    public static function isKicked():String {
        return (Main.instance.game.mcLogin != null && Main.instance.game.mcLogin.warning.visible).toString();
    }

    public function equipLoadout(setName:String, changeColors:Boolean = false): void
    {
        if(!Main.instance.game.world.coolDown("equipLoadout") || setName == null || setName == "")
        {
            return;
        }
        Main.instance.game.sfc.sendXtMessage("zm","equipLoadout",["cmd",setName,!changeColors],"str",Main.instance.game.world.curRoom);
    }

    public function onNewSet() : void
    {
        var curItem:* = undefined;
        var itemsArray:Array = ["he","ba","ar","co","Weapon","pe","am","mi"];
        for each(curItem in itemsArray)
        {
            if(Main.instance.game.world.myAvatar.objData.eqp[curItem] != null)
            {
                Main.instance.game.world.myAvatar.loadMovieAtES(curItem,Main.instance.game.world.myAvatar.objData.eqp[curItem].sFile,Main.instance.game.world.myAvatar.objData.eqp[curItem].sLink);
            }
            else
            {
                Main.instance.game.world.myAvatar.unloadMovieAtES(curItem);
            }
        }
    }

    public static function getLoadouts():String {
        var loadouts:Object = Main.instance.game.world.objInfo["customs"].loadouts;
        return JSON.stringify(loadouts);
    }

    public static function Gender():String {
        return '"' + Main.instance.game.world.myAvatar.objData.strGender.toUpperCase() + '"';
    }

    private static function parseDrop(name:*):* {
        var ret:* = {};
        var lowercaseName:String = name.toLowerCase().trim();
        ret.name = lowercaseName;
        ret.count = 1;
        var result:Object = DROP_PARSE_REGEX.exec(lowercaseName);
        if (result == null) {
            return ret;
        } else {
            ret.name = result[1];
            ret.count = int(result[2]);
            return ret;
        }
    }

    public static function rejectExcept(whitelist:String):void {
        var pickup:Array = whitelist.split(',');
        if (Main.instance.game.litePreference.data.bCustomDrops) {
            var source:* = Main.instance.game.cDropsUI.mcDraggable ? Main.instance.game.cDropsUI.mcDraggable.menu : Main.instance.game.cDropsUI;
            for (var i:int = source.numChildren - 1; i >= 0; i--) {
                var child:* = source.getChildAt(i);
                if (child.itemObj) {
                    var itemName:String = child.itemObj.sName.toLowerCase();
                    if (pickup.indexOf(itemName) == -1) {
                        child.btNo.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                    }
                }
            }
        } else {
            var children:int = Main.instance.game.ui.dropStack.numChildren;
            for (i = children - 1; i >= 0; i--) {
                child = Main.instance.game.ui.dropStack.getChildAt(i);
                var type:String = getQualifiedClassName(child);
                if (type.indexOf('DFrame2MC') != -1) {
                    var drop:* = parseDrop(child.cnt.strName.text);
                    var name:* = drop.name;
                    if (pickup.indexOf(name) == -1) {
                        child.cnt.nbtn.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                    }
                }
            }
        }
    }
}
}
