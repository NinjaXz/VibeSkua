using System.Text.RegularExpressions;

namespace Skua.Plugin.Omniscope;

/// <summary>
/// Extracts server shop IDs from decompiled location-map ActionScript.
/// </summary>
internal static class LocationSwfShopIndexer
{
    private static readonly Regex DirectShopIdRegex = new(
        @"(?:sendLoadShopRequest|loadShop|showShop|openShop|loadMergeShop)\s*\(\s*(?<id>\d+)|\.(?:intShop|shopID)\s*=\s*(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ShopComponentRegex = new(
        @"(?<instance>(?:this\.)?\w+)\.strAction\s*=\s*""(?:Item|Merge) Shop""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ComponentIdRegex = new(
        @"(?<instance>(?:this\.)?\w+)\.intID\s*=\s*(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads all positive shop IDs from the ActionScript files exported for one map SWF.
    /// </summary>
    /// <param name="exportDirectory">Directory containing the map's exported ActionScript files.</param>
    /// <returns>Distinct shop IDs in ascending order.</returns>
    public static int[] ReadShopIds(string exportDirectory)
    {
        if (!Directory.Exists(exportDirectory))
            throw new DirectoryNotFoundException($"The exported map source directory was not found: {exportDirectory}");

        HashSet<int> shopIds = [];
        foreach (string sourcePath in Directory.EnumerateFiles(exportDirectory, "*.as", SearchOption.AllDirectories))
        {
            string source;
            try
            {
                source = File.ReadAllText(sourcePath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in DirectShopIdRegex.Matches(source))
                if (int.TryParse(match.Groups["id"].Value, out int shopId) && shopId > 1)
                    shopIds.Add(shopId);

            HashSet<string> shopComponents = ShopComponentRegex.Matches(source)
                .Select(static match => match.Groups["instance"].Value)
                .ToHashSet(StringComparer.Ordinal);
            foreach (Match match in ComponentIdRegex.Matches(source))
                if (shopComponents.Contains(match.Groups["instance"].Value) &&
                    int.TryParse(match.Groups["id"].Value, out int shopId) &&
                    shopId > 1)
                    shopIds.Add(shopId);
        }

        return shopIds.Order().ToArray();
    }
}
