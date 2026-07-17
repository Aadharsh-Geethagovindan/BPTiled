public enum GameMode { Hotseat, Online }

// Persistent data passed between scenes. Set before scene loads; never reset mid-session.
public static class MatchSetup
{
    public static GameMode Mode = GameMode.Hotseat;

    public static string[] Team1Fighters   = new string[3];
    public static string[] Team2Fighters   = new string[3];
    public static int      FirstActingTeam = 1;
    public static int      MapSeed         = 0;
    public static int      LocalTeamId     = 0; // 0 = hotseat (all teams), 1 or 2 = networked

    public static bool IsReady =>
        Team1Fighters != null && Team1Fighters.Length == 3 &&
        Team2Fighters != null && Team2Fighters.Length == 3 &&
        System.Array.TrueForAll(Team1Fighters, f => !string.IsNullOrEmpty(f)) &&
        System.Array.TrueForAll(Team2Fighters, f => !string.IsNullOrEmpty(f));
}
