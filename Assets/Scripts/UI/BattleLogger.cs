// Categories are granular — filter UI groups them into broader toggles (e.g. Hit+Miss = "Combat").
public enum LogCategory
{
    Ability,   // ability was used
    Hit,       // damage / healing / shielding resolved
    Miss,      // ability missed
    Effect,    // status or instant effect applied
    Movement,  // fighter moved
    Passive,   // passive triggered
    Death,     // fighter died
}

public class LogEntry
{
    public string      Message;
    public LogCategory Category;

    public LogEntry(string message, LogCategory category)
    {
        Message  = message;
        Category = category;
    }
}

// Static logger — any system calls Log(); UI subscribes to OnEntry.
public static class BattleLogger
{
    public static event System.Action<LogEntry> OnEntry;

    public static void Log(string message, LogCategory category)
    {
        OnEntry?.Invoke(new LogEntry(message, category));
        UnityEngine.Debug.Log($"[{category}] {message}");
    }
}
