package skua.module {
import flash.display.MovieClip;

public class OptimizePlayers extends Module {
    private var frameTick:int = 0;

    public function OptimizePlayers() {
        super("OptimizePlayers");
        this.enabled = false;
    }

    private function recursiveStop(container:*):void {
        if (container == null || !("numChildren" in container)) return;
        try {
            if (container is MovieClip) {
                MovieClip(container).stop();
            }
            for (var i:int = container.numChildren - 1; i >= 0; i--) {
                var child:* = container.getChildAt(i);
                if (child != null && child is MovieClip) {
                    recursiveStop(child);
                }
            }
        } catch (e:*) {}
    }

    private function recursivePlay(container:*):void {
        if (container == null || !("numChildren" in container)) return;
        try {
            if (container is MovieClip) {
                MovieClip(container).play();
            }
            for (var i:int = container.numChildren - 1; i >= 0; i--) {
                var child:* = container.getChildAt(i);
                if (child != null && child is MovieClip) {
                    recursivePlay(child);
                }
            }
        } catch (e:*) {}
    }

    private function purgeCompletedClips(container:*):void {
        if (container == null || !("numChildren" in container)) return;
        try {
            for (var i:int = container.numChildren - 1; i >= 0; i--) {
                var child:* = container.getChildAt(i);
                if (child != null && child is MovieClip) {
                    var mc:MovieClip = MovieClip(child);
                    if (!("pMC" in mc) && !("objData" in mc) && !("mcChar" in mc)) {
                        if (mc.totalFrames > 1 && mc.currentFrame >= mc.totalFrames) {
                            mc.stop();
                            if (mc.parent) mc.parent.removeChild(mc);
                        }
                    }
                }
            }
        } catch (e:*) {}
    }

    private function clampFloatingText(container:*):void {
        if (container == null || !("numChildren" in container)) return;
        try {
            var floatingClips:Array = [];
            for (var i:int = 0; i < container.numChildren; i++) {
                var child:* = container.getChildAt(i);
                if (child != null && child is MovieClip) {
                    var mc:MovieClip = MovieClip(child);
                    if (!("pMC" in mc) && !("objData" in mc) && !("mcChar" in mc)) {
                        floatingClips.push(mc);
                    }
                }
            }
            while (floatingClips.length > 3) {
                var oldMC:MovieClip = floatingClips.shift();
                oldMC.stop();
                if (oldMC.parent) oldMC.parent.removeChild(oldMC);
            }
        } catch (e:*) {}
    }

    private function stripUIFilters(ui:*):void {
        if (ui == null) return;
        try {
            if (ui.mcPortraitTarget != null && ui.mcPortraitTarget.filters != null && ui.mcPortraitTarget.filters.length > 0) ui.mcPortraitTarget.filters = [];
            if (ui.mcPortrait != null && ui.mcPortrait.filters != null && ui.mcPortrait.filters.length > 0) ui.mcPortrait.filters = [];
            if (ui.mcAuraList != null && ui.mcAuraList.filters != null && ui.mcAuraList.filters.length > 0) ui.mcAuraList.filters = [];
        } catch (e:*) {}
    }

    override public function onToggle(game:*):void {
        if (enabled) {
            try {
                if (game.world.map != null) recursiveStop(game.world.map);
            } catch (e:*) {}
            if (game.ui != null) stripUIFilters(game.ui);
        } else {
            try {
                if (game.world.map != null) recursivePlay(game.world.map);
            } catch (e:*) {}
        }
    }

    override public function onFrame(game:*):void {
        frameTick++;
        if (frameTick >= 15) {
            frameTick = 0;
            if (!enabled) return;

            // A. Drop Stack UI Optimization
            try {
                if (game.ui != null && game.ui.dropStack != null) {
                    for (var d:int = game.ui.dropStack.numChildren - 1; d >= 0; d--) {
                        var dropMC:* = game.ui.dropStack.getChildAt(d);
                        if (dropMC != null && dropMC.filters != null && dropMC.filters.length > 0) {
                            dropMC.filters = [];
                        }
                    }
                }
            } catch (e:*) {}

            // B. Monster Death Fast-Forwarding
            try {
                if (game.world.monsters != null) {
                    for each (var monster:* in game.world.monsters) {
                        if (monster != null && monster.pMC != null && monster.pMC.mcChar is MovieClip) {
                            var monMC:MovieClip = MovieClip(monster.pMC.mcChar);
                            if (monMC.currentLabel == "Die" || monMC.currentLabel == "Death" || (monster.objData && monster.objData.intHP <= 0)) {
                                if (monMC.currentFrame < monMC.totalFrames) {
                                    monMC.gotoAndStop(monMC.totalFrames);
                                }
                            }
                        }
                    }
                }
            } catch (e:*) {}

            // C. Smart Chat & Log Truncation Throttler
            try {
                if (game.chatF != null && game.chatF.ti != null && game.chatF.ti.text != null) {
                    if (game.chatF.ti.text.length > 4000) {
                        game.chatF.ti.text = game.chatF.ti.text.substring(game.chatF.ti.text.length - 2000);
                    }
                }
            } catch (e:*) {}

            // D. Combat Floating Text Clamping
            clampFloatingText(game.world.CHARS);
        }
    }
}
}
