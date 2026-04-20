using System.Collections.Generic;
using System.Linq;

// Ported from original Breakpoint project.
// Validates team rarity compositions for draft restrictions.
public static class RestrictionEngine
{
    // Rarity string → numeric rank (higher = rarer)
    private static readonly Dictionary<string, int> RarityRank = new()
    {
        { "L",  4 },
        { "UR", 3 },
        { "R",  2 },
        { "UC", 1 },
        { "C",  0 },
    };

    // Each pattern is a set of max-rank slots a team of 3 must fit into.
    //   {4, 2, 1} = one L, one ≤R, one ≤UC
    //   {3, 3, 1} = two UR, one ≤UC
    //   {3, 2, 2} = one UR, two R
    private static readonly List<int[]> Patterns = new()
    {
        new[] { 4, 2, 1 },
        new[] { 3, 3, 1 },
        new[] { 3, 2, 2 },
    };

    /// Returns true if this set of rarities forms a valid team.
    public static bool IsValidTeam(List<string> rarities)
    {
        var picks = rarities.Select(r => RarityRank[r]).OrderByDescending(x => x).ToList();
        foreach (var pattern in Patterns)
        {
            var slots = pattern.OrderBy(x => x).ToList();
            if (BestFitAssign(picks, slots)) return true;
        }
        return false;
    }

    /// Returns the set of rarity ranks that can still be legally picked given current picks.
    /// Use this to grey out cards the active team can no longer select.
    public static HashSet<int> AllowedNextRanks(List<string> currentPicks)
    {
        var picks   = currentPicks.Select(r => RarityRank[r]).OrderByDescending(x => x).ToList();
        var allowed = new HashSet<int>();

        foreach (var pattern in Patterns)
        {
            var slots = pattern.OrderBy(x => x).ToList();
            if (!BestFitAssign(picks, slots)) continue;

            foreach (var cap in slots)
                for (int r = 0; r <= cap; r++)
                    allowed.Add(r);
        }
        return allowed;
    }

    /// Returns the numeric rank for a rarity string. Returns -1 for unknown rarities.
    public static int GetRank(string rarity)
        => RarityRank.TryGetValue(rarity, out int r) ? r : -1;

    // Greedy best-fit: assigns each pick (desc) to the smallest slot that can hold it.
    // Mutates slotsAsc — caller passes a copy.
    private static bool BestFitAssign(List<int> picksDesc, List<int> slotsAsc)
    {
        foreach (var pick in picksDesc)
        {
            int idx = slotsAsc.FindIndex(s => s >= pick);
            if (idx < 0) return false;
            slotsAsc.RemoveAt(idx);
        }
        return true;
    }
}
