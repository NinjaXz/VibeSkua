using System;
using System.Reflection;

class Program {
    static void Main() {
        var asm = Assembly.LoadFrom(@"C:\Users\Ninja1\Desktop\VibeSkua\Skua.Manager\bin\Release\net10.0-windows\Velopack.dll");
        var um = asm.GetType("Velopack.UpdateManager");
        var vp = asm.GetType("Velopack.VelopackApp");
        var sh = asm.GetType("Velopack.Shortcuts.ShortcutManager") ?? asm.GetType("Velopack.ShortcutManager");
        Console.WriteLine("UpdateManager Methods:");
        foreach (var m in um.GetMethods()) {
            if (m.Name.Contains("Shortcut")) Console.WriteLine(m.Name);
        }
        Console.WriteLine("VelopackApp Methods:");
        foreach (var m in vp.GetMethods()) {
            if (m.Name.Contains("Shortcut")) Console.WriteLine(m.Name);
        }
        if (sh != null) {
            Console.WriteLine("ShortcutManager Methods:");
            foreach (var m in sh.GetMethods()) {
                Console.WriteLine(m.Name);
            }
        }
    }
}
