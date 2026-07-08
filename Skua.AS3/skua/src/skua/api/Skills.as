package skua.api {

import skua.Main;

public class Skills {

    public function Skills() {
        super();
    }

    private static function actionTimeCheck(skill:*):Boolean {
        var finalCD:int = 0;
        var currentTime:Number = new Date().getTime();
        var hasteMultiplier:Number = 1 - Math.min(Math.max(Main.instance.game.world.myAvatar.dataLeaf.sta.$tha, -1), 0.5);
        if (currentTime - Main.instance.game.world.GCDTS < Main.instance.game.world.GCD) {
            return false;
        }
        if (skill.OldCD != null) {
            finalCD = Math.round(skill.OldCD * hasteMultiplier);
        } else {
            finalCD = Math.round(skill.cd * hasteMultiplier);
        }
        if (currentTime - skill.ts >= finalCD) {
            delete skill.OldCD;
            return true;
        }
        return false;
    }

    public static function scavengeSpinners():void {
        try {
            var active:* = Main.instance.game.world.actions.active;
            var actBar:* = Main.instance.game.ui.mcInterface.actBar;
            if (active != null && actBar != null) {
                for (var i:int = 0; i < active.length; i++) {
                    var skill:* = active[i];
                    if (skill != null && actionTimeCheck(skill) && skill.isOK && !skill.skillLock && !skill.lock) {
                        var btn:* = actBar["i" + (i + 1)];
                        if (btn != null) {
                            if ("mcMask" in btn && btn.mcMask != null && btn.mcMask.visible) btn.mcMask.visible = false;
                            if (btn.currentFrame != 1) btn.gotoAndStop(1);
                        }
                    }
                }
            }
        } catch (e:*) {}
    }

    public static function canUseSkill(index:int):String {
        scavengeSpinners();
        var skill:* = Main.instance.game.world.actions.active[index];
        return (Main.instance.game.world.myAvatar.target != null && Main.instance.game.world.myAvatar.target.dataLeaf.intHP > 0 && actionTimeCheck(skill) && skill.isOK && !skill.skillLock && !skill.lock).toString();
    }

    public static function useSkill(index:int):String {
        scavengeSpinners();
        var skill:* = Main.instance.game.world.actions.active[index];
        if (skill != null && actionTimeCheck(skill) && skill.isOK && !skill.skillLock && !skill.lock) {
            try {
                var actBar:* = Main.instance.game.ui.mcInterface.actBar;
                if (actBar != null) {
                    var btn:* = actBar["i" + (index + 1)];
                    if (btn != null) {
                        if ("mcMask" in btn && btn.mcMask != null) btn.mcMask.visible = false;
                        btn.gotoAndStop(1);
                    }
                }
            } catch (e:*) {}
            Main.instance.game.world.testAction(skill);
            return true.toString();
        }

        return false.toString();
    }
}
}
