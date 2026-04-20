using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure logic — no MonoBehaviour. Computes valid anchor tiles and full shape tiles
/// given a caster, ability, and board state.
/// </summary>
public static class AbilityTargeting
{
    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all tiles the player can click as an anchor for this ability.
    /// </summary>
    public static List<Vector2Int> GetValidTargetTiles(Fighter caster, Ability ability, Board board)
    {
        var valid = new List<Vector2Int>();
        Vector2Int origin = caster.GridPosition;

        switch (ability.Shape)
        {
            case AbilityShape.Single:
            case AbilityShape.Ring:
                // Any in-bounds tile within [minRange, range] Manhattan distance
                for (int x = 0; x < board.Width; x++)
                for (int y = 0; y < board.Height; y++)
                {
                    var pos = new Vector2Int(x, y);
                    int dist = Manhattan(origin, pos);
                    if (dist >= ability.MinRange && dist <= ability.Range)
                        valid.Add(pos);
                }
                break;

            case AbilityShape.Line:
            case AbilityShape.Cone:
            case AbilityShape.Cross:
            case AbilityShape.Box:
                // Anchor must be in a cardinal direction from the caster
                foreach (var dir in Cardinals)
                for (int dist = ability.MinRange; dist <= ability.Range; dist++)
                {
                    var pos = origin + dir * dist;
                    if (board.IsInBounds(pos))
                        valid.Add(pos);
                }
                break;
        }

        return valid;
    }

    /// <summary>
    /// Returns the full set of tiles hit by the ability given a chosen anchor tile.
    /// Caller is responsible for only passing a valid anchor from GetValidTargetTiles.
    /// </summary>
    public static List<Vector2Int> GetShapeTiles(Fighter caster, Ability ability, Vector2Int anchor, Board board, bool biasLeft = true)
    {
        var tiles = new List<Vector2Int>();

        switch (ability.Shape)
        {
            case AbilityShape.Single:
                tiles.Add(anchor);
                break;

            case AbilityShape.Line:
            {
                Vector2Int dir = CardinalDirection(anchor - caster.GridPosition);
                for (int i = 0; i < ability.ShapeSize; i++)
                {
                    var pos = anchor + dir * i;
                    if (board.IsInBounds(pos))
                        tiles.Add(pos);
                }
                break;
            }

            case AbilityShape.Cone:
            {
                // Anchor = tip (closest row to caster). Each row expands ±1 in the
                // perpendicular axis relative to the previous row.
                // Row i (0-indexed): center = anchor + dir*i, spread = ±i tiles perpendicularly.
                Vector2Int dir  = CardinalDirection(anchor - caster.GridPosition);
                Vector2Int perp = Perpendicular(dir);
                for (int i = 0; i < ability.ShapeSize; i++)
                {
                    Vector2Int rowCenter = anchor + dir * i;
                    for (int j = -i; j <= i; j++)
                    {
                        var pos = rowCenter + perp * j;
                        if (board.IsInBounds(pos))
                            tiles.Add(pos);
                    }
                }
                break;
            }

            case AbilityShape.Cross:
            {
                // Anchor = center. Arms extend ShapeSize tiles in each cardinal direction.
                tiles.Add(anchor);
                foreach (var dir in Cardinals)
                for (int i = 1; i <= ability.ShapeSize; i++)
                {
                    var pos = anchor + dir * i;
                    if (board.IsInBounds(pos))
                        tiles.Add(pos);
                }
                break;
            }

            case AbilityShape.Box:
            {
                // Anchor = center of near row. Box expands symmetrically for odd widths.
                // For even widths, biasLeft determines which side gets the extra tile.
                // Toggle with B key during targeting (see SelectionManager).
                // TODO: add a visible UI indicator so players know B toggles box bias.
                Vector2Int fwd   = CardinalDirection(anchor - caster.GridPosition);
                Vector2Int right = ClockwiseRight(fwd);
                int W           = ability.ShapeWidth;
                int leftExtent  = biasLeft ? W / 2 : (W - 1) / 2;
                int rightExtent = biasLeft ? (W - 1) / 2 : W / 2;
                for (int h = 0; h < ability.ShapeHeight; h++)
                for (int w = -leftExtent; w <= rightExtent; w++)
                {
                    var pos = anchor + fwd * h + right * w;
                    if (board.IsInBounds(pos))
                        tiles.Add(pos);
                }
                break;
            }

            case AbilityShape.Ring:
                // Tiles at exactly ShapeSize Manhattan distance from anchor
                for (int x = 0; x < board.Width; x++)
                for (int y = 0; y < board.Height; y++)
                {
                    var pos = new Vector2Int(x, y);
                    if (Manhattan(anchor, pos) == ability.ShapeSize)
                        tiles.Add(pos);
                }
                break;
        }

        return tiles;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static readonly Vector2Int[] Cardinals =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    /// <summary>
    /// Returns the perpendicular axis unit vector for a cardinal direction.
    /// North/South → East axis (1,0). East/West → North axis (0,1).
    /// Used for Cone spread (±both sides).
    /// </summary>
    private static Vector2Int Perpendicular(Vector2Int dir)
        => new Vector2Int(Mathf.Abs(dir.y), Mathf.Abs(dir.x));

    /// <summary>
    /// Rotates a cardinal direction 90° clockwise.
    /// North→East, East→South, South→West, West→North.
    /// Used for Area box expansion (consistent "rightward" per facing direction).
    /// </summary>
    private static Vector2Int ClockwiseRight(Vector2Int dir)
        => new Vector2Int(dir.y, -dir.x);

    private static int Manhattan(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    /// <summary>
    /// Collapses an arbitrary offset into the nearest cardinal unit vector.
    /// Favours the axis with the larger component on a tie.
    /// </summary>
    private static Vector2Int CardinalDirection(Vector2Int offset)
    {
        if (offset == Vector2Int.zero) return Vector2Int.up;

        if (Mathf.Abs(offset.x) >= Mathf.Abs(offset.y))
            return new Vector2Int(Math.Sign(offset.x), 0);
        else
            return new Vector2Int(0, Math.Sign(offset.y));
    }
}
