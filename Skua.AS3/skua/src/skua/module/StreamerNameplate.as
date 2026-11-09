package skua.module {
import flash.display.Sprite;
import flash.text.TextField;
import flash.text.TextFormat;

public class StreamerNameplate extends Module {
    private var _nameplate:*;
    private var _nameField:*;
    private var _overlay:Sprite;

    public function StreamerNameplate() {
        super("StreamerNameplate");
    }

    override public function onToggle(game:*):void {
        if (enabled) {
            applyOverlay(game);
        } else {
            removeOverlay();
        }
    }

    override public function onFrame(game:*):void {
        applyOverlay(game);
    }

    private function applyOverlay(game:*):void {
        if (game == null || game.world == null || game.world.myAvatar == null || game.world.myAvatar.pMC == null) {
            return;
        }

        var nameplate:* = game.world.myAvatar.pMC.pname;
        if (nameplate == null || nameplate.ti == null) {
            return;
        }

        var nameField:* = nameplate.ti;
        if (_overlay == null || _nameplate != nameplate || _nameField != nameField || _overlay.parent != nameplate) {
            removeOverlay();

            _nameplate = nameplate;
            _nameField = nameField;

            var fieldWidth:Number = Math.max(60, Number(nameField.width));
            var fieldHeight:Number = Math.max(16, Number(nameField.height));

            _overlay = new Sprite();
            _overlay.name = "skuaStreamerNameplateOverlay";
            _overlay.x = Number(nameField.x);
            _overlay.y = Number(nameField.y);
            _overlay.graphics.beginFill(0x202020, 1);
            _overlay.graphics.drawRoundRect(0, 0, fieldWidth, fieldHeight, 6, 6);
            _overlay.graphics.endFill();

            var label:TextField = new TextField();
            var format:TextFormat = new TextFormat("_sans", 12, 0xFFFFFF, true);
            format.align = "center";
            label.defaultTextFormat = format;
            label.width = fieldWidth;
            label.height = fieldHeight;
            label.text = "VibeSkuaUser";
            label.setTextFormat(format);
            label.selectable = false;
            label.mouseEnabled = false;
            label.multiline = false;
            label.wordWrap = false;
            _overlay.addChild(label);

            nameplate.addChild(_overlay);
        }

        nameField.visible = false;

        // Keep the replacement above any children AQW adds during avatar refreshes.
        if (_overlay.parent == nameplate && nameplate.numChildren > 0 && nameplate.getChildIndex(_overlay) != nameplate.numChildren - 1) {
            nameplate.setChildIndex(_overlay, nameplate.numChildren - 1);
        }
    }

    private function removeOverlay():void {
        if (_nameField != null) {
            _nameField.visible = true;
        }

        if (_overlay != null && _overlay.parent != null) {
            _overlay.parent.removeChild(_overlay);
        }

        _overlay = null;
        _nameField = null;
        _nameplate = null;
    }
}
}
