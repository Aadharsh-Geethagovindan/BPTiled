using UnityEngine;

// [SERVER] Validates and executes fighter movement.
// Called only from BattleController.RequestMove.
public static class MoveResolver
{
    public static bool ExecuteMove(Fighter fighter, Vector2Int destination, Board board)
    {
        var destTile = board.GetTile(destination);
        if (destTile == null || !destTile.IsPassable || destTile.IsOccupied)
        {
            Debug.LogWarning($"[MoveResolver] Destination {destination} is invalid.");
            return false;
        }

        // Confirm destination is within movement range using remaining points
        var pathfinder = new Pathfinder(board);
        var reachable  = pathfinder.GetReachableTiles(fighter.GridPosition, fighter.RemainingMovePoints);
        if (!reachable.ContainsKey(destination))
        {
            Debug.LogWarning($"[MoveResolver] {destination} is out of range for {fighter.FighterName}.");
            return false;
        }

        float moveCost = reachable[destination];

        // Clear previous tile occupancy
        var fromTile = board.GetTile(fighter.GridPosition);
        if (fromTile != null)
            fromTile.OccupyingCharacter = null;

        // Set new tile occupancy and update fighter position
        destTile.OccupyingCharacter = fighter.gameObject;
        fighter.SetGridPosition(destination);
        fighter.SubtractMovePoints(moveCost);

        int tilesMoved = Mathf.RoundToInt(moveCost);
        fighter.AddTilesMoved(tilesMoved);

        TileEffectManager.Instance?.HandleFighterEntered(fighter, destination);

        BattleLogger.Log($"{fighter.FighterName} moved to {destination}. ({fighter.RemainingMovePoints} move remaining)", LogCategory.Movement);
        return true;
    }

    // Teleports a fighter to a destination with no movement cost and no pathfinding check.
    // Used for ability-driven repositioning (e.g. Eye of the Rift).
    public static bool ExecuteReposition(Fighter fighter, Vector2Int destination, Board board)
    {
        var destTile = board.GetTile(destination);
        if (destTile == null || !destTile.IsPassable || destTile.IsOccupied)
        {
            Debug.LogWarning($"[MoveResolver] Reposition destination {destination} is invalid.");
            return false;
        }

        var fromTile = board.GetTile(fighter.GridPosition);
        if (fromTile != null) fromTile.OccupyingCharacter = null;

        destTile.OccupyingCharacter = fighter.gameObject;
        fighter.SetGridPosition(destination);

        TileEffectManager.Instance?.HandleFighterEntered(fighter, destination);
        BattleLogger.Log($"{fighter.FighterName} was repositioned to {destination}.", LogCategory.Movement);
        return true;
    }
}
