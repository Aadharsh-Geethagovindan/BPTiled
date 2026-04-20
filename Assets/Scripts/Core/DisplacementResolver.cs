using UnityEngine;

// Handles knockback and pull displacement after an ability resolves.
// Positive knockback = push away from caster. Negative = pull toward caster.
// Fighters stop at the last valid tile before an impassable tile, occupied tile, or board edge.
// On a pull, the target always stops at least 1 tile away from the caster.
public static class DisplacementResolver
{
    public static void Resolve(Fighter caster, Fighter target, int knockback, Board board)
    {
        if (knockback == 0) return;

        Vector2Int dir    = CardinalDirection(target.GridPosition - caster.GridPosition);
        bool       isPull = knockback < 0;
        int        steps  = Mathf.Abs(knockback);

        if (isPull) dir = -dir; // reverse direction for pull

        Vector2Int dest = target.GridPosition;

        for (int i = 0; i < steps; i++)
        {
            Vector2Int next = dest + dir;

            // Pull: never land on the caster's tile
            if (isPull && next == caster.GridPosition) break;

            var nextTile = board.GetTile(next);

            // Stop if out of bounds, impassable, or occupied by someone else
            if (nextTile == null || !nextTile.IsPassable || nextTile.IsOccupied) break;

            dest = next;
        }

        if (dest == target.GridPosition) return; // nowhere to go

        // Clear old tile, set new tile, update fighter position
        var fromTile = board.GetTile(target.GridPosition);
        if (fromTile != null) fromTile.OccupyingCharacter = null;

        var toTile = board.GetTile(dest);
        if (toTile != null) toTile.OccupyingCharacter = target.gameObject;

        target.SetGridPosition(dest);

        BattleLogger.Log($"{target.FighterName} was {(isPull ? "pulled" : "pushed")} to {dest} by {caster.FighterName}.", LogCategory.Movement);
    }

    // Collapses any vector to the nearest cardinal direction.
    // On a diagonal tie (|x| == |y|), horizontal axis wins.
    private static Vector2Int CardinalDirection(Vector2Int delta)
    {
        if (delta == Vector2Int.zero) return Vector2Int.up; // fallback: same tile

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            return new Vector2Int((int)Mathf.Sign(delta.x), 0);
        else
            return new Vector2Int(0, (int)Mathf.Sign(delta.y));
    }
}
