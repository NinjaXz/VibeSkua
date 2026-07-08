package skua.api {
import flash.display.MovieClip;
import flash.events.Event;
import flash.events.MouseEvent;
import flash.events.TimerEvent;
import flash.utils.Timer;

import skua.Main;
import skua.module.ModalMC;

public class Server {

    public function Server() {
        super();
    }

    public static function clickServer(serverName:String):String {
        var source:* = Main.instance.game.mcLogin.sl.iList;
        for (var i:int = 0; i < source.numChildren; i++) {
            var child:* = source.getChildAt(i);

            if (child.tName.ti.text.toLowerCase().indexOf(serverName.toLowerCase()) > -1) {
                child.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                return true.toString();
            }
        }
        return false.toString();
    }

    public static function connectToServer(server:String):String {
        var serverData:Object = JSON.parse(server);
        var objLogin:Object = null;

        var connectionServerTimer:Timer = new Timer(500, 50);
        connectionServerTimer.addEventListener(TimerEvent.TIMER, connectingServer);
        connectionServerTimer.start();

        function connectingServer(e:Event):void {
            if (objLogin != null) {
                connectServer(serverData, objLogin);
                connectionServerTimer.stop();
                connectionServerTimer.removeEventListener(TimerEvent.TIMER, connectingServer);
            }
            objLogin = JSON.parse(Main.getGameObjectS("objLogin"));
        }

        return true.toString();
    }

    private static function connectServer(server:Object, objLoginData:Object):* {
        Main.instance.game.showTracking("4");
        if (Main.instance.game.serialCmdMode) {
            return;
        }

        if (server.bOnline == 0) {
            Main.instance.game.MsgBox.notify("Server currently offline!");
        } else if (server.iCount >= server.iMax) {
            Main.instance.game.MsgBox.notify("Server is Full!");
        } else if (server.iChat > 0 && objLoginData.bCCOnly == 1) {
            Main.instance.game.MsgBox.notify("Account Restricted to Moglin Sage Server Only.");
        } else if (server.iChat > 0 && objLoginData.iAge < 13 && objLoginData.iUpgDays < 0) {
            Main.instance.game.MsgBox.notify("Ask your parent to upgrade your account in order to play on chat enabled servers.");
        } else if (server.bUpg == 1 && objLoginData.iUpgDays < 0) {
            showModal("Member Server! Do you want to upgrade your account to access this premium server now?", "white,medium", "dual");
        } else if (Number(server.iMax) % 2 > 0) {
            showModal("Testing Server! Do you want to switch to the testing game client?", "white,medium", "dual");
        } else if (server.iLevel > 0 && objLoginData.iEmailStatus <= 2) {
            showModal("This server requires a confirmed email address.", "red,medium", "mono");
        } else {
            Main.instance.game.objServerInfo = server;
            Main.instance.game.chatF.iChat = server.iChat;
            killLoginModals();
            Main.instance.game.connectTo(server.sIP, server.iPort);
        }
    }

    private static function showModal(message:String, glow:String, buttons:String):void {
        var modal:ModalMC = new ModalMC();
        var params:Object = {
            strBody: message,
            params: {},
            glow: glow,
            btns: buttons
        };
        Main.instance.game.mcLogin.ModalStack.addChild(modal);
        modal.init(params);
    }

    private static function killLoginModals():void {
        var modalStack:MovieClip = Main.instance.game.mcLogin.ModalStack;
        for (var i:int = modalStack.numChildren - 1; i >= 0; i--) {
            var modal:MovieClip = modalStack.getChildAt(i) as MovieClip;
            if (modal && "fClose" in modal) {
                modal.fClose();
            }
        }
    }
}
}
