// [SERVER/HOTSEAT ONLY] Executes debug/testing commands typed into the in-scene DebugConsole.
// Called from BattleController.RequestDebugCommand, which handles the online/hotseat routing —
// this class only ever runs on whichever peer is authoritative.
//
// To add a new command: add a case below (lowercase). Prefer routing through the same methods
// real gameplay would use (Fighter.IncreaseCharge, TurnManager.ForceGameOver, etc.) rather than
// poking fields directly, so the command exercises the real path instead of a shortcut that could
// behave differently.
public static class DebugCommands
{
    public static string Execute(string rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand)) return "Empty command.";

        var parts   = rawCommand.Trim().ToLowerInvariant().Split(' ');
        var command = parts[0];

        switch (command)
        {
            case "player1wins":
                if (TurnManager.Instance == null) return "TurnManager.Instance is null — command did nothing.";
                TurnManager.Instance.ForceGameOver(1);
                return "Forced Team 1 win.";

            case "player2wins":
                if (TurnManager.Instance == null) return "TurnManager.Instance is null — command did nothing.";
                TurnManager.Instance.ForceGameOver(2);
                return "Forced Team 2 win.";

            case "chargeallsigs":
                foreach (var fighter in FighterManager.Instance.AllFighters)
                    fighter.IncreaseCharge(fighter.SigChargeReq);
                return "All fighters' signature charge maxed.";

            default:
                return $"Unknown command: '{command}'";
        }
    }
}
