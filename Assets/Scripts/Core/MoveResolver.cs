using System.Collections.Generic;
using UnityEngine;

// [SERVER] Validates and executes fighter movement.
// Called only from BattleController.RequestMove.
public static class MoveResolver
{
    // Per-tile pause during a stepped move (see ProgressiveResolver) — the placeholder timing a
    // real walk-cycle animation will eventually drive instead of a flat lerp.
    public const int StepDurationMs = 150;

    // Pure — no mutation. Validates the destination is in range and returns the ordered tile
    // path to walk through to get there (excluding the start tile), or null if unreachable.
    // Caller (BattleController.RequestMove) steps through this one tile at a time via
    // ProgressiveResolver so movement resolves progressively instead of teleporting.
    public static List<Vector2Int> GetPath(Fighter fighter, Vector2Int destination, Board board)
    {
        var destTile = board.GetTile(destination);
        if (destTile == null || !destTile.IsPassable || destTile.IsOccupied)
        {
            Debug.LogWarning($"[MoveResolver] Destination {destination} is invalid.");
            return null;
        }

        var pathfinder = new Pathfinder(board);

        // Confirm destination is within movement range using remaining points.
        var reachable = pathfinder.GetReachableTiles(fighter.GridPosition, fighter.RemainingMovePoints, fighter);
        if (!reachable.ContainsKey(destination))
        {
            Debug.LogWarning($"[MoveResolver] {destination} is out of range for {fighter.FighterName}.");
            return null;
        }

        return pathfinder.FindPath(fighter.GridPosition, destination, fighter);
    }

    // One tile's worth of a move: occupancy swap, position/cost update, and the tile-effect
    // trigger for the tile just entered. [SERVER] Called once per path tile by
    // BattleController.RequestMove via ProgressiveResolver.RunSteps.
    public static void StepTile(Fighter fighter, Vector2Int nextTile, Board board)
    {
        var toTile = board.GetTile(nextTile);
        float cost = fighter.GetEffectiveTerrainCost(toTile);

        var fromTile = board.GetTile(fighter.GridPosition);
        if (fromTile != null)
            fromTile.OccupyingCharacter = null;

        toTile.OccupyingCharacter = fighter.gameObject;
        fighter.SetGridPosition(nextTile);
        fighter.SubtractMovePoints(cost);
        fighter.AddTilesMoved(1);

        TileEffectManager.Instance?.HandleFighterEntered(fighter, nextTile);

        BattleLogger.Log($"{fighter.FighterName} moved to {nextTile}. ({fighter.RemainingMovePoints} move remaining)", LogCategory.Movement);
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
