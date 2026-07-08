package skua.api {

import skua.Main;

public class Combat {

    public function Combat() {
        super();
    }

    public static function magnetize():void {
        var target:* = Main.instance.game.world.myAvatar.target;
        if (target) {
            target.pMC.x = Main.instance.game.world.myAvatar.pMC.x;
            target.pMC.y = Main.instance.game.world.myAvatar.pMC.y;
        }
    }

    public static function infiniteRange():void {
        var active:Array = Main.instance.game.world.actions.active;
        if (active == null) return;
        for (var i:int = 0; i < active.length && i < 6; i++) {
            if (active[i] != null && ("range" in active[i] || active[i].hasOwnProperty("range"))) {
                active[i].range = 20000;
            }
        }
    }
}
}
