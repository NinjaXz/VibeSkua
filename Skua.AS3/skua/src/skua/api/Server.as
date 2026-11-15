package skua.api {
import flash.display.MovieClip;
import flash.events.Event;
import flash.events.MouseEvent;
import flash.events.TimerEvent;
import flash.system.Security;
import flash.utils.Timer;

import skua.Main;
import skua.module.ModalMC;

public class Server {

    private static var _connectionServerTimer:Timer = null;

    public function Server() {
        super();
    }

    public static function loadPolicyFiles():void {
        try {
            if (Main.instance.game == null) return;

            // 1. Check serialCmd.servers
            if (Main.instance.game.serialCmd != null && Main.instance.game.serialCmd.servers != null) {
                var servers:Array = Main.instance.game.serialCmd.servers as Array;
                if (servers != null) {
                    for (var i:int = 0; i < servers.length; i++) {
                        var s:Object = servers[i];
                        if (s != null && s.sIP != null && s.iPort != null) {
                            var port:int = int(s.iPort);
                            if (port > 0) {
                                Security.loadPolicyFile("xmlsocket://" + String(s.sIP) + ":" + port);
                            }
                        }
                    }
                }
            }

            // 2. Check mcLogin.sl.iList children
            if (Main.instance.game.mcLogin != null && Main.instance.game.mcLogin.sl != null && Main.instance.game.mcLogin.sl.iList != null) {
                var source:* = Main.instance.game.mcLogin.sl.iList;
                for (var j:int = 0; j < source.numChildren; j++) {
                    var child:* = source.getChildAt(j);
                    if (child != null) {
                        var cServer:Object = child.server || child.objServer;
                        if (cServer != null && cServer.sIP != null && cServer.iPort != null) {
                            var cPort:int = int(cServer.iPort);
                            if (cPort > 0) {
                                Security.loadPolicyFile("xmlsocket://" + String(cServer.sIP) + ":" + cPort);
                            }
                        }
                    }
                }
            }
        } catch (e:Error) {
            Main.instance.external.debug("Error in loadPolicyFiles: " + e);
        }
    }

    public static function loadPolicyFile(ip:String, port:int):void {
        try {
            if (ip != null && ip.length > 0 && port > 0) {
                Security.loadPolicyFile("xmlsocket://" + ip + ":" + port);
            }
        } catch (e:Error) {
            Main.instance.external.debug("Error in loadPolicyFile: " + e);
        }
    }

    public static function resetConnectionState():void {
        try {
            if (_connectionServerTimer != null) {
                _connectionServerTimer.stop();
                _connectionServerTimer = null;
            }
            killLoginModals();
            if (Main.instance.game != null) {
                try {
                    if (Main.instance.game.sfc != null && "disconnect" in Main.instance.game.sfc) {
                        Main.instance.game.sfc.disconnect();
                    }
                } catch (e:Error) {}
                try {
                    if ("connectingServer" in Main.instance.game) {
                        Main.instance.game.connectingServer = false;
                    }
                } catch (e:Error) {}
            }
        } catch (e:Error) {
            Main.instance.external.debug("Error in resetConnectionState: " + e);
        }
    }

    public static function clickServer(serverName:String):String {
        killLoginModals();
        loadPolicyFiles();
        var source:* = Main.instance.game.mcLogin.sl.iList;
        for (var i:int = 0; i < source.numChildren; i++) {
            var child:* = source.getChildAt(i);

            if (child.tName.ti.text.toLowerCase().indexOf(serverName.toLowerCase()) > -1) {
                try {
                    var cServer:Object = child.server || child.objServer;
                    if (cServer != null && cServer.sIP != null && cServer.iPort != null) {
                        Security.loadPolicyFile("xmlsocket://" + String(cServer.sIP) + ":" + int(cServer.iPort));
                    }
                } catch (e:Error) {}
                child.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                return true.toString();
            }
        }
        return false.toString();
    }

    public static function connectToServer(server:String):String {
        resetConnectionState();

        var serverData:Object = JSON.parse(server);
        if (serverData != null && serverData.sIP != null && serverData.iPort != null) {
            try {
                Security.loadPolicyFile("xmlsocket://" + String(serverData.sIP) + ":" + int(serverData.iPort));
            } catch (e:Error) {}
        }

        var objLoginStr:String = Main.getGameObjectS("objLogin");
        var objLogin:Object = (objLoginStr != null && objLoginStr.length > 0) ? JSON.parse(objLoginStr) : null;
        if (objLogin != null) {
            connectServer(serverData, objLogin);
            return true.toString();
        }

        _connectionServerTimer = new Timer(100, 50);
        _connectionServerTimer.addEventListener(TimerEvent.TIMER, function(e:Event):void {
            objLoginStr = Main.getGameObjectS("objLogin");
            objLogin = (objLoginStr != null && objLoginStr.length > 0) ? JSON.parse(objLoginStr) : null;
            if (objLogin != null) {
                if (_connectionServerTimer != null) {
                    _connectionServerTimer.stop();
                    _connectionServerTimer = null;
                }
                connectServer(serverData, objLogin);
            }
        });
        _connectionServerTimer.addEventListener(TimerEvent.TIMER_COMPLETE, function(e:Event):void {
            _connectionServerTimer = null;
        });
        _connectionServerTimer.start();

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
            if (server != null && server.sIP != null && server.iPort != null) {
                try {
                    Security.loadPolicyFile("xmlsocket://" + String(server.sIP) + ":" + int(server.iPort));
                } catch (e:Error) {}
            }
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

    public static function killLoginModals():void {
        try {
            if (Main.instance.game == null) return;

            // 1. Clear mcLogin.ModalStack
            try {
                if (Main.instance.game.mcLogin != null && Main.instance.game.mcLogin.ModalStack != null) {
                    cleanModalStack(Main.instance.game.mcLogin.ModalStack);
                }
            } catch (e:Error) {}

            // 2. Clear mcO.ModalStack (root overlay modal stack). mcO is a sealed mcOption that does not
            // declare ModalStack, so reading it unguarded throws ReferenceError #1069.
            try {
                if (Main.instance.game.mcO != null) {
                    if ("ModalStack" in Main.instance.game.mcO && Main.instance.game.mcO.ModalStack != null) {
                        cleanModalStack(Main.instance.game.mcO.ModalStack);
                    }
                    // Disarm invisible blocker hitmask
                    try {
                        if (Main.instance.game.mcO.cnt != null && Main.instance.game.mcO.cnt.hit != null) {
                            Main.instance.game.mcO.cnt.hit.visible = false;
                        }
                    } catch (e:Error) {}
                }
            } catch (e:Error) {}

            // 3. Clear ui.ModalStack if present
            try {
                if (Main.instance.game.ui != null && Main.instance.game.ui.ModalStack != null) {
                    cleanModalStack(Main.instance.game.ui.ModalStack);
                }
            } catch (e:Error) {}

            // 4. Close any MsgBox popup
            try {
                if (Main.instance.game.MsgBox != null && "fClose" in Main.instance.game.MsgBox) {
                    Main.instance.game.MsgBox.fClose();
                }
            } catch (e:Error) {}

            // 5. Hide mcLogin.warning (kick / ban alert frame)
            try {
                if (Main.instance.game.mcLogin != null && Main.instance.game.mcLogin.warning != null) {
                    Main.instance.game.mcLogin.warning.visible = false;
                }
            } catch (e:Error) {}

            // 6. Clear in-game popups (ui.mcPopup)
            try {
                if (Main.instance.game.ui != null && Main.instance.game.ui.mcPopup != null) {
                    cleanModalStack(Main.instance.game.ui.mcPopup);
                }
            } catch (e:Error) {}

            // 7. Reset internal Flash state flags
            try {
                if ("serialCmdMode" in Main.instance.game) Main.instance.game.serialCmdMode = false;
                if ("connectingServer" in Main.instance.game) Main.instance.game.connectingServer = false;
            } catch (e:Error) {}
        } catch (e:Error) {
            Main.instance.external.debug("Error in killLoginModals: " + e);
        }
    }

    private static function cleanModalStack(stack:*):void {
        if (stack == null) return;
        try {
            var count:int = stack.numChildren;
            for (var i:int = count - 1; i >= 0; i--) {
                var child:* = stack.getChildAt(i);
                if (child != null) {
                    try {
                        if ("fClose" in child && typeof(child.fClose) == "function") {
                            child.fClose();
                        }
                    } catch (e:Error) {}
                    try {
                        if (stack.contains(child)) {
                            stack.removeChild(child);
                        }
                    } catch (e:Error) {}
                }
            }
        } catch (e:Error) {}
    }

    public static function getActiveModalMessage():String {
        try {
            if (Main.instance.game == null) return "";

            // 1. Check mcO.ModalStack (overlay); mcO is sealed, so test "in" before reading
            if (Main.instance.game.mcO != null && "ModalStack" in Main.instance.game.mcO && Main.instance.game.mcO.ModalStack != null) {
                var stack:* = Main.instance.game.mcO.ModalStack;
                if (stack.numChildren > 0) {
                    var m:* = stack.getChildAt(0);
                    if (m != null) {
                        if (m.cnt != null && m.cnt.strBody != null && m.cnt.strBody.text != null) return String(m.cnt.strBody.text);
                        if (m.strBody != null) return String(m.strBody);
                    }
                }
            }

            // 2. Check mcLogin.ModalStack
            if (Main.instance.game.mcLogin != null && Main.instance.game.mcLogin.ModalStack != null) {
                var lStack:* = Main.instance.game.mcLogin.ModalStack;
                if (lStack.numChildren > 0) {
                    var lm:* = lStack.getChildAt(0);
                    if (lm != null) {
                        if (lm.cnt != null && lm.cnt.strBody != null && lm.cnt.strBody.text != null) return String(lm.cnt.strBody.text);
                        if (lm.strBody != null) return String(lm.strBody);
                    }
                }
            }

            // 3. Check MsgBox
            if (Main.instance.game.MsgBox != null && Main.instance.game.MsgBox.stage != null) {
                if (Main.instance.game.MsgBox.cnt != null && Main.instance.game.MsgBox.cnt.strBody != null) {
                    return String(Main.instance.game.MsgBox.cnt.strBody.text);
                }
            }

            // 4. Check mcLogin.warning (kick / ban alert message)
            if (Main.instance.game.mcLogin != null && Main.instance.game.mcLogin.warning != null && Main.instance.game.mcLogin.warning.visible) {
                if (Main.instance.game.mcLogin.warning.strMessage != null && Main.instance.game.mcLogin.warning.strMessage.text != null) {
                    return String(Main.instance.game.mcLogin.warning.strMessage.text);
                }
            }
        } catch (e:Error) {}
        return "";
    }
}
}
