// Persistent data passed from CharacterSelect scene to Battle scene.
// Set by CharSelectManager before scene load; read by BattleController on startup.
public static class MatchSetup
{
    public static string[] Team1Fighters   = new string[3];
    public static string[] Team2Fighters   = new string[3];
    public static int      FirstActingTeam = 1;   // team that acts first in Round 1
    public static int      MapSeed         = 0;   // seed used by TerrainGenerator

    public static bool IsReady =>
        Team1Fighters != null && Team1Fighters.Length == 3 &&
        Team2Fighters != null && Team2Fighters.Length == 3;
}
