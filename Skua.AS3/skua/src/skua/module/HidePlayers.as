package skua.module {
public class HidePlayers extends Module {
    private var frameTick:int = 0;

    public function HidePlayers() {
        super("HidePlayers");
    }

    override public function onToggle(game:*):void {
        var avatars:* = game.world.avatars;
        for (var id:* in avatars) {
            var avatar:* = avatars[id];
            if (!avatar.isMyAvatar && avatar.pMC) {
                avatar.pMC.mcChar.visible = !enabled;
                avatar.pMC.pname.visible = !enabled;
                avatar.pMC.shadow.visible = !enabled;
                if (avatar.petMC) {
                    avatar.petMC.visible = !enabled;
                }
            }
        }
    }

    override public function onFrame(game:*):void {
        frameTick++;
        if (frameTick >= 15) {
            frameTick = 0;
            onToggle(game);
        }
    }
}
}