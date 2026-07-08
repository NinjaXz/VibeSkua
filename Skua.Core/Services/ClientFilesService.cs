using Skua.Core.Interfaces;
using Skua.Core.Models;

namespace Skua.Core.Services;

public class ClientFilesService : IClientFilesService
{
    public void CreateDirectories()
    {
        if (!Directory.Exists(ClientFileSources.SkuaDIR))
        {
            Directory.CreateDirectory(ClientFileSources.SkuaDIR);

            if (!Directory.Exists(ClientFileSources.SkuaOptionsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaOptionsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaScriptsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaScriptsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaPluginsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaPluginsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaThemesDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaThemesDIR);
        }
        else
        {
            if (!Directory.Exists(ClientFileSources.SkuaOptionsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaOptionsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaScriptsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaScriptsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaPluginsDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaPluginsDIR);
            if (!Directory.Exists(ClientFileSources.SkuaThemesDIR))
                Directory.CreateDirectory(ClientFileSources.SkuaThemesDIR);
        }
    }

    public void CreateFiles()
    {
        if (!File.Exists(ClientFileSources.SkuaAdvancedSkillsFile))
        {
            string rootAdvancedSkillsFile = Path.Combine(AppContext.BaseDirectory, "AdvancedSkills.json");
            if (File.Exists(rootAdvancedSkillsFile))
                File.Copy(rootAdvancedSkillsFile, ClientFileSources.SkuaAdvancedSkillsFile);
            else
                File.Create(ClientFileSources.SkuaAdvancedSkillsFile);
        }

        if (!File.Exists(ClientFileSources.SkuaQuestsFile))
        {
            string rootQuestsFile = Path.Combine(AppContext.BaseDirectory, "QuestData.json");
            if (File.Exists(rootQuestsFile))
                File.Copy(rootQuestsFile, ClientFileSources.SkuaQuestsFile);
            else
                File.Create(ClientFileSources.SkuaQuestsFile);
        }

        string scriptsQuestFile = Path.Combine(ClientFileSources.SkuaScriptsDIR, "QuestData.json");
        if (!File.Exists(scriptsQuestFile) && File.Exists(ClientFileSources.SkuaQuestsFile))
        {
            try { File.Copy(ClientFileSources.SkuaQuestsFile, scriptsQuestFile, true); } catch { }
        }

        if (!File.Exists(ClientFileSources.SkuaJunkItemsFile))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClientFileSources.SkuaJunkItemsFile)!);
                File.WriteAllText(ClientFileSources.SkuaJunkItemsFile, "[]");
            }
            catch
            {
                // ignored
            }
        }

        string appPluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (Directory.Exists(appPluginsDir))
        {
            try
            {
                foreach (string file in Directory.GetFiles(appPluginsDir, "*.dll"))
                {
                    string destFile = Path.Combine(ClientFileSources.SkuaPluginsDIR, Path.GetFileName(file));
                    File.Copy(file, destFile, true); 
                }
            }
            catch { }
        }
    }
}
