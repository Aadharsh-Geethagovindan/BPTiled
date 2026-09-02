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
    /// Returns all tiles the player can click as an anchor for the given effect (e.g.
    /// ability.PrimaryEffect for the first click, ability.SecondaryEffect for a follow-up).
    /// </summary>
    public static List<Vector2Int> GetValidTargetTiles(Fighter caster, AbilityEffect effect, Board board)
    {
        var valid = new List<Vector2Int>();
        if (effect == null) return valid;

        Vector2Int origin = caster.GridPosition;

        switch (effect.Shape)
        {
            case AbilityShape.Single:
            case AbilityShape.Ring:
            case AbilityShape.Line:
            case AbilityShape.Cone:
            case AbilityShape.Cross:
            case AbilityShape.Box:
                // Anchor must be in a cardinal direction (up/down/left/right) from the caster,
                // at a distance within [MinRange, Range] — no more whole-board omnidirectional
                // sniping. One tile per (direction, distance) pair, so there's no even/odd
                // ambiguity to resolve here; that only applies to shape *drawing* (Box/Ring's
                // existing biasLeft, toggled with B), not anchor selection.
                foreach (var dir in Cardinals)
                for (int dist = effect.MinRange; dist <= effect.Range; dist++)
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
    /// Returns the full set of tiles hit by the given effect given a chosen anchor tile.
    /// Caller is responsible for only passing a valid anchor from GetValidTargetTiles.
    /// facingOverride, when set, replaces the auto-inferred (anchor - caster) direction for
    /// Line/Cone/Box — needed because a range-0 effect only ever has one valid anchor (the
    /// caster's own tile), so there's nothing for auto-inference to read a direction from; it
    /// silently defaults to Up. See SelectionManager's R-key facing cycle.
    /// </summary>
    public static List<Vector2Int> GetShapeTiles(Fighter caster, AbilityEffect effect, Vector2Int anchor, Board board,
                                                  bool biasLeft = true, Vector2Int? facingOverride = null)
    {
        var tiles = new List<Vector2Int>();
        if (effect == null) return tiles;

        switch (effect.Shape)
        {
            case AbilityShape.Single:
                tiles.Add(anchor);
                break;

            case AbilityShape.Line:
            {
                Vector2Int dir = facingOverride ?? CardinalDirection(anchor - caster.GridPosition);
                for (int i = 0; i < effect.ShapeSize; i++)
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
                Vector2Int dir  = facingOverride ?? CardinalDirection(anchor - caster.GridPosition);
                Vector2Int perp = Perpendicular(dir);
                for (int i = 0; i < effect.ShapeSize; i++)
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
                // Always symmetric in all 4 directions — no facing concept, nothing to override.
                tiles.Add(anchor);
                foreach (var dir in Cardinals)
                for (int i = 1; i <= effect.ShapeSize; i++)
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
                Vector2Int fwd   = facingOverride ?? CardinalDirection(anchor - caster.GridPosition);
                Vector2Int right = ClockwiseRight(fwd);
                int W           = effect.ShapeWidth;
                int leftExtent  = biasLeft ? W / 2 : (W - 1) / 2;
                int rightExtent = biasLeft ? (W - 1) / 2 : W / 2;
                for (int h = 0; h < effect.ShapeHeight; h++)
                for (int w = -leftExtent; w <= rightExtent; w++)
                {
                    var pos = anchor + fwd * h + right * w;
                    if (board.IsInBounds(pos))
                        tiles.Add(pos);
                }
                break;
            }

            case AbilityShape.Ring:
            {
                // Hollow square perimeter with corners cut, NOT a Manhattan-distance diamond.
                // Size 1 is a special case — degenerates to the same shape as Cross size 1
                // (center + 4 orthogonal neighbours), since there's no room for a hollow
                // interior distinct from the ring itself at that size.
                if (effect.ShapeSize <= 1)
                {
                    tiles.Add(anchor);
                    foreach (var dir in Cardinals)
                    {
                        var pos = anchor + dir;
                        if (board.IsInBounds(pos))
                            tiles.Add(pos);
                    }
                    break;
                }

                // General case: perimeter of an (N+2)x(N+2) box centered on anchor, corners cut.
                // Even extents have no true center, so one axis has to be split unevenly.
                // biasLeft (B key) only resolves the COLUMN (X) split — the row (Y) split stays
                // fixed every time. Toggling both together (the original approach) moved the
                // anchor diagonally, which reads as "not centered" in a way that's confusing to
                // aim; letting the player fix only the column keeps the toggle predictable.
                int extent = effect.ShapeSize + 2;
                int nearX  = biasLeft ? extent / 2 : (extent - 1) / 2;
                int farX   = biasLeft ? (extent - 1) / 2 : extent / 2;
                int nearY  = extent / 2;
                int farY   = (extent - 1) / 2;

                for (int dx = -nearX; dx <= farX; dx++)
                for (int dy = -nearY; dy <= farY; dy++)
                {
                    bool onXEdge = dx == -nearX || dx == farX;
                    bool onYEdge = dy == -nearY || dy == farY;
                    if (!onXEdge && !onYEdge) continue; // interior — hollow
                    if (onXEdge && onYEdge) continue;   // corner — cut

                    var pos = anchor + new Vector2Int(dx, dy);
                    if (board.IsInBounds(pos))
                        tiles.Add(pos);
                }
                break;
            }
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
