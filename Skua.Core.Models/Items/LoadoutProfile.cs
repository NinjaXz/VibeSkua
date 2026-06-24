namespace Skua.Core.Models.Items;

public class LoadoutProfile
{
    public string Name { get; set; } = "New Loadout";
    
    public string Class { get; set; } = string.Empty;
    public string ClassEnhancement { get; set; } = "Luck";
    public int ClassPatternID { get; set; }
    public int ClassProcID { get; set; }

    public string Armor { get; set; } = string.Empty;

    public string Weapon { get; set; } = string.Empty;
    public string WeaponEnhancement { get; set; } = "Luck";
    public int WeaponPatternID { get; set; }
    public int WeaponProcID { get; set; }

    public string Helm { get; set; } = string.Empty;
    public string HelmEnhancement { get; set; } = "Luck";
    public int HelmPatternID { get; set; }
    public int HelmProcID { get; set; }

    public string Cape { get; set; } = string.Empty;
    public string CapeEnhancement { get; set; } = "Luck";
    public int CapePatternID { get; set; }
    public int CapeProcID { get; set; }

    public string Pet { get; set; } = string.Empty;
    public string Amulet { get; set; } = string.Empty;
}
