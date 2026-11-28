namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Converts class points into a class rank. The game keeps a class item's accumulated class points
/// in the item quantity, and rank 10 unlocks at 302,500 CP. The same table appears in
/// <c>ScriptBotStats.ClassRankCumulativePoints</c> and in the script library helper
/// <c>CoreBots.CheckClassRank</c>; this is the shared answer for read surfaces.
/// </summary>
public static class ClassRank
{
    /// <summary>Cumulative class points needed to reach each rank, index 0 is rank 1.</summary>
    public static readonly int[] CumulativePoints = [0, 900, 3_600, 10_000, 22_500, 44_100, 78_400, 129_600, 202_500, 302_500];

    /// <summary>Rank for a class point total. Rank 1 covers everything below the first threshold.</summary>
    public static int RankOf(int classPoints)
    {
        if (classPoints < 0)
        {
            return 1;
        }

        int rank = 1;
        for (int i = 1; i < CumulativePoints.Length; i++)
        {
            if (classPoints < CumulativePoints[i])
            {
                break;
            }

            rank = i + 1;
        }

        return rank;
    }

    /// <summary>Class points needed for the next rank, or the rank 10 target once a class is maxed.</summary>
    public static int NextRankPoints(int classPoints)
    {
        foreach (int point in CumulativePoints)
        {
            if (classPoints < point)
            {
                return point;
            }
        }

        return CumulativePoints[CumulativePoints.Length - 1];
    }
}
