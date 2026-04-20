using System.Collections.Generic;
using UnityEngine;

public enum TerrainGenerationMode
{
    Flat,
    Procedural
}

public class TerrainGenerator : MonoBehaviour
{
    [Header("Generation Settings")]
    [SerializeField] private TerrainGenerationMode mode = TerrainGenerationMode.Procedural;
    [SerializeField] private int seed = 0;
    [SerializeField] private bool useRandomSeed = true;

    [Header("Zone Size Settings")]
    [SerializeField] private int dominantAnchorMin = 6;
    [SerializeField] private int dominantAnchorMax = 8;
    [SerializeField] private int dominantSatelliteMin = 3;
    [SerializeField] private int dominantSatelliteMax = 5;
    [SerializeField] private int minorZoneMin = 3;
    [SerializeField] private int minorZoneMax = 5;

    [Header("Placement Constraints")]
    [SerializeField] private int minAnchorSeparation = 5;
    [SerializeField] private int minSatelliteFromAnchor = 3;

    private Board _board;
    private System.Random _rng;
    public int LastSeed { get; private set; }

    // Store zone origins for external use (capture point placement later)
    public Vector2Int Dominant1AnchorOrigin { get; private set; }
    public Vector2Int Dominant2AnchorOrigin { get; private set; }
    public Vector2Int MinorZoneOrigin { get; private set; }
    public EssenceType Dominant1Type { get; private set; }
    public EssenceType Dominant2Type { get; private set; }
    public EssenceType MinorType { get; private set; }

    public void Initialize(Board board)
    {
        _board = board;
    }

    /// Forces a specific seed, overriding useRandomSeed. Call before Generate().
    public void SetSeed(int forcedSeed)
    {
        seed          = forcedSeed;
        useRandomSeed = false;
    }

    public void Generate()
    {
        if (useRandomSeed)
            seed = Random.Range(0, int.MaxValue);

        LastSeed = seed;
        _rng = new System.Random(seed);

        Debug.Log($"[TerrainGenerator] Generating with seed {seed}");

        if (mode == TerrainGenerationMode.Flat)
        {
            GenerateFlat();
            return;
        }

        GenerateProcedural();
    }

    private void GenerateFlat()
    {
        for (int x = 0; x < _board.Width; x++)
            for (int y = 0; y < _board.Height; y++)
            {
                var tile = _board.GetTile(x, y);
                tile.EssenceAffinity = EssenceType.None;
                tile.MovementCost = 1f;
            }
    }

    private void GenerateProcedural()
    {
        GenerateFlat();

        // Pick 3 essence types
        var availableTypes = new List<EssenceType>
        {
            EssenceType.Arcane,
            EssenceType.Elemental,
            EssenceType.Force,
            EssenceType.Corrupt
        };
        Shuffle(availableTypes);

        Dominant1Type = availableTypes[0];
        Dominant2Type = availableTypes[1];
        MinorType = availableTypes[2];

        Debug.Log($"[TerrainGenerator] Dominant1: {Dominant1Type} | Dominant2: {Dominant2Type} | Minor: {MinorType}");

        // Pre-calculate all origins before placing any tiles
        int topMinY = _board.Height / 2;
        int topMaxY = _board.Height - 2;
        int botMinY = 1;
        int botMaxY = (_board.Height / 2) - 1;
        int centerMinY = botMaxY;
        int centerMaxY = topMinY;

        // Dominant 1 anchor in top half
        Vector2Int d1Anchor = GetOriginInRegion(1, _board.Width - 2, topMinY, topMaxY);

        // Dominant 2 anchor in bottom half, minimum separation from d1
        Vector2Int d2Anchor = GetOriginWithMinDistance(
            1, _board.Width - 2, botMinY, botMaxY,
            d1Anchor, minAnchorSeparation);

        
        // Minor zone in center rows
        Vector2Int minorOrigin = GetOriginInRegion(
            1, _board.Width - 2, centerMinY, centerMaxY);

        // Store for external use
        Dominant1AnchorOrigin = d1Anchor;
        Dominant2AnchorOrigin = d2Anchor;
        MinorZoneOrigin = minorOrigin;

        // Place anchor zones first
        PlaceContiguousZone(Dominant1Type, _rng.Next(dominantAnchorMin, dominantAnchorMax + 1), d1Anchor); //NEW - place before satellite origin calc
        PlaceContiguousZone(Dominant2Type, _rng.Next(dominantAnchorMin, dominantAnchorMax + 1), d2Anchor); //NEW

        // Now calculate satellite origins away from placed tiles
        Vector2Int d1Satellite = GetOriginAwayFromEssence( //NEW
            1, _board.Width - 2, 1, _board.Height - 2,
            Dominant1Type, minSatelliteFromAnchor);

        Vector2Int d2Satellite = GetOriginAwayFromEssence( //NEW
            1, _board.Width - 2, 1, _board.Height - 2,
            Dominant2Type, minSatelliteFromAnchor);

        // Place satellites and minor
        PlaceContiguousZone(Dominant1Type, _rng.Next(dominantSatelliteMin, dominantSatelliteMax + 1), d1Satellite);
        PlaceContiguousZone(Dominant2Type, _rng.Next(dominantSatelliteMin, dominantSatelliteMax + 1), d2Satellite);
        PlaceContiguousZone(MinorType, _rng.Next(minorZoneMin, minorZoneMax + 1), minorOrigin);

        // Movement costs are now authored per-map in MapData and applied by BoardRenderer.
        // ApplyMovementCosts() has been intentionally removed.
    }

    private Vector2Int GetOriginInRegion(int minX, int maxX, int minY, int maxY)
    {
        return new Vector2Int(
            _rng.Next(minX, maxX + 1),
            _rng.Next(minY, maxY + 1)
        );
    }

    private Vector2Int GetOriginWithMinDistance(
        int minX, int maxX, int minY, int maxY,
        Vector2Int avoidPoint, int minDistance,
        int maxAttempts = 50)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var candidate = GetOriginInRegion(minX, maxX, minY, maxY);
            if (ManhattanDistance(candidate, avoidPoint) >= minDistance)
                return candidate;
        }

        // Fallback — return a point on the opposite side of the board
        Debug.LogWarning("[TerrainGenerator] Could not find origin with min distance, using fallback");
        return new Vector2Int(
            _board.Width - 1 - avoidPoint.x,
            _board.Height - 1 - avoidPoint.y
        );
    }

    private void PlaceContiguousZone(EssenceType essence, int targetSize, Vector2Int origin)
    {
        if (!_board.IsInBounds(origin))
        {
            Debug.LogWarning($"[TerrainGenerator] Origin {origin} out of bounds, skipping zone");
            return;
        }

        var filled = new HashSet<Vector2Int>();
        var frontier = new List<Vector2Int>();

        filled.Add(origin);
        _board.GetTile(origin).EssenceAffinity = essence;
        AddNeighborsToFrontier(origin, filled, frontier);

        int placed = 1;

        while (placed < targetSize && frontier.Count > 0)
        {
            int idx = _rng.Next(frontier.Count);
            Vector2Int candidate = frontier[idx];
            frontier.RemoveAt(idx);

            if (!_board.IsInBounds(candidate)) continue;
            if (filled.Contains(candidate)) continue;
            if (_board.GetTile(candidate).EssenceAffinity != EssenceType.None) continue; 
            filled.Add(candidate);
            _board.GetTile(candidate).EssenceAffinity = essence;
            placed++;

            AddNeighborsToFrontier(candidate, filled, frontier);
        }

        //Debug.Log($"[TerrainGenerator] Placed {placed}/{targetSize} tiles of {essence} at origin {origin}");
    }

    private void AddNeighborsToFrontier(Vector2Int pos, HashSet<Vector2Int> filled, List<Vector2Int> frontier)
    {
        var directions = new List<Vector2Int>
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right,
            
        };

        foreach (var dir in directions)
        {
            var neighbor = pos + dir;
            if (_board.IsInBounds(neighbor) && !filled.Contains(neighbor)
                && !frontier.Contains(neighbor))
                frontier.Add(neighbor);
        }
    }

    private Vector2Int GetOriginAwayFromEssence(
    int minX, int maxX, int minY, int maxY,
    EssenceType essence, int minDistance,
    int maxAttempts = 100)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var candidate = GetOriginInRegion(minX, maxX, minY, maxY);
            bool tooClose = false;

            for (int x = 0; x < _board.Width; x++)
            {
                for (int y = 0; y < _board.Height; y++)
                {
                    if (_board.GetTile(x, y).EssenceAffinity == essence)
                    {
                        if (ManhattanDistance(candidate, new Vector2Int(x, y)) < minDistance)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                }
                if (tooClose) break;
            }

            if (!tooClose) return candidate;
        }

        Debug.LogWarning($"[TerrainGenerator] Could not find satellite origin away from {essence}, using fallback");
        return GetOriginInRegion(minX, maxX, minY, maxY);
    }
    private int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}