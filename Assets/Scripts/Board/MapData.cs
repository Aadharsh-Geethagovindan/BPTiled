using UnityEngine;

public enum TerrainTier
{
    Easy,       // MovementCost = 1.0f
    Medium,     // MovementCost = 1.5f
    Hard,       // MovementCost = 3.0f
    Impassable  // IsPassable = false
}

[System.Serializable]
public struct TileMapData
{
    public TerrainTier tier;
    public Sprite sprite;
}

[CreateAssetMenu(fileName = "NewMapData", menuName = "Breakpoint/Map Data")]
public class MapData : ScriptableObject
{
    public string mapName;
    public int width = 10;
    public int height = 8;
    public MapTheme theme;
    public TileMapData[] tiles;

    public int GetIndex(int x, int y) => x + y * width;

    public TileMapData GetTileData(int x, int y) => tiles[GetIndex(x, y)];

    /// <summary>
    /// Applies movement costs and passability from this MapData to the board's tile data.
    /// Call this after board.Initialize() and after terrain generator has placed essence zones.
    /// </summary>
    public void ApplyToBoard(Board board)
    {
        if (tiles == null) return;

        for (int x = 0; x < width && x < board.Width; x++)
        {
            for (int y = 0; y < height && y < board.Height; y++)
            {
                var tile = board.GetTile(x, y);
                if (tile == null) continue;

                var data = GetTileData(x, y);
                switch (data.tier)
                {
                    case TerrainTier.Easy:
                        tile.MovementCost = 1.0f;
                        tile.IsPassable = true;
                        break;
                    case TerrainTier.Medium:
                        tile.MovementCost = 1.5f;
                        tile.IsPassable = true;
                        break;
                    case TerrainTier.Hard:
                        tile.MovementCost = 3.0f;
                        tile.IsPassable = true;
                        break;
                    case TerrainTier.Impassable:
                        tile.MovementCost = 999f;
                        tile.IsPassable = false;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Resizes the tile array, preserving existing tile data where dimensions overlap.
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        var newTiles = new TileMapData[newWidth * newHeight];

        for (int x = 0; x < newWidth; x++)
            for (int y = 0; y < newHeight; y++)
                if (x < width && y < height && tiles != null)
                    newTiles[x + y * newWidth] = tiles[x + y * width];

        tiles = newTiles;
        width = newWidth;
        height = newHeight;
    }
}
