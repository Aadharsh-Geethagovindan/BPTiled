using UnityEngine;

[CreateAssetMenu(fileName = "NewMapTheme", menuName = "Breakpoint/Map Theme")]
public class MapTheme : ScriptableObject
{
    public string themeName;

    [Header("Easy Terrain Sprites")]
    public Sprite[] easyTiles;

    [Header("Medium Terrain Sprites")]
    public Sprite[] mediumTiles;

    [Header("Hard Terrain Sprites")]
    public Sprite[] hardTiles;

    [Header("Impassable Terrain Sprites")]
    public Sprite[] impassableTiles;

    public Sprite[] GetTierSprites(TerrainTier tier) => tier switch
    {
        TerrainTier.Easy       => easyTiles,
        TerrainTier.Medium     => mediumTiles,
        TerrainTier.Hard       => hardTiles,
        TerrainTier.Impassable => impassableTiles,
        _                      => easyTiles
    };
}
