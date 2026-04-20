using UnityEngine;

public enum EssenceType
{
    None,
    Arcane,
    Elemental,
    Force,
    Corrupt,
    True
}

public class Tile
{
    public Vector2Int GridPosition { get; private set; }
    public EssenceType EssenceAffinity { get; set; } = EssenceType.None;
    public float MovementCost { get; set; } = 1f;
    public bool IsPassable { get; set; } = true;
    public bool IsOccupied => OccupyingCharacter != null;
    public GameObject OccupyingCharacter { get; set; } = null;
    public GameObject VisualObject { get; set; }

    public Tile(Vector2Int position)
    {
        GridPosition = position;
    }
}