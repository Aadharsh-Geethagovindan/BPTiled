using UnityEngine;

public class BoardRenderer : MonoBehaviour
{
    [Header("Tile Visuals")]
    [SerializeField] private GameObject tilePrefab;
    [SerializeField] private Material   tileMaterial; // assign Sprites/Default; overrides prefab material

    [Header("Map Data (optional — leave empty for placeholder colors)")]
    [SerializeField] private MapData mapData;

    [Header("Essence Overlay")]
    [SerializeField] [Range(0f, 1f)] private float essenceOverlayOpacity = 0.45f;

    [Header("Essence Colors")]
    [SerializeField] private Color arcaneColor    = new Color(0.6f, 0.3f, 0.9f);
    [SerializeField] private Color elementalColor = new Color(0.3f, 0.8f, 0.3f);
    [SerializeField] private Color forceColor     = new Color(0.3f, 0.5f, 0.9f);
    [SerializeField] private Color corruptColor   = new Color(0.5f, 0.1f, 0.1f);

    [Header("Placeholder Colors (used when no MapData is assigned)")]
    [SerializeField] private Color neutralColor        = new Color(0.7f, 0.7f, 0.7f);
    [SerializeField] private Color normalCostTint      = Color.white;
    [SerializeField] private Color lightDifficultTint  = new Color(0.85f, 0.85f, 0.70f);
    [SerializeField] private Color heavyDifficultTint  = new Color(0.70f, 0.65f, 0.50f);
    [SerializeField] private Color extremeTint         = new Color(0.50f, 0.45f, 0.35f);

    private Board _board;

    public void Initialize(Board board)
    {
        _board = board;

        if (mapData != null)
            mapData.ApplyToBoard(board);

        RenderBoard();
    }

    private void RenderBoard()
    {
        for (int x = 0; x < _board.Width; x++)
        {
            for (int y = 0; y < _board.Height; y++)
            {
                var tile     = _board.GetTile(x, y);
                var worldPos = _board.GridToWorld(new Vector2Int(x, y));

                var tileObj = Instantiate(tilePrefab, worldPos, Quaternion.identity, transform);
                tileObj.name      = $"Tile_{x}_{y}";
                tile.VisualObject = tileObj;

                ApplyTileVisual(tile);
            }
        }
    }

    public void ApplyTileVisual(Tile tile)
    {
        if (tile.VisualObject == null) return;

        var baseRenderer = tile.VisualObject.GetComponent<SpriteRenderer>();
        if (baseRenderer == null) return;

        if (tileMaterial != null)
            baseRenderer.material = tileMaterial;

        if (mapData != null
            && tile.GridPosition.x < mapData.width
            && tile.GridPosition.y < mapData.height)
        {
            // Apply the exact sprite painted in the Map Maker
            var data = mapData.GetTileData(tile.GridPosition.x, tile.GridPosition.y);
            baseRenderer.sprite = data.sprite;
            baseRenderer.color  = Color.white;

            // Scale the tile object so the sprite fits exactly one grid tile,
            // regardless of the sprite's pixel size or PPU setting
            FitSpriteToTile(tile.VisualObject, data.sprite);

            // Layer a semi-transparent essence color on top
            ApplyEssenceOverlay(tile, baseRenderer);
            return;
        }

        // ── Placeholder mode (no MapData) ──────────────────────────────────
        Color baseColor = tile.EssenceAffinity switch
        {
            EssenceType.Arcane    => arcaneColor,
            EssenceType.Elemental => elementalColor,
            EssenceType.Force     => forceColor,
            EssenceType.Corrupt   => corruptColor,
            _                     => neutralColor
        };

        Color costTint = tile.MovementCost switch
        {
            <= 1.0f => normalCostTint,
            <= 1.5f => lightDifficultTint,
            <= 2.0f => heavyDifficultTint,
            _       => extremeTint
        };

        baseRenderer.color = baseColor * costTint;
    }

    private void FitSpriteToTile(GameObject tileObj, Sprite sprite)
    {
        if (sprite == null) return;

        Vector2 spriteWorldSize = sprite.bounds.size;
        if (spriteWorldSize.x <= 0 || spriteWorldSize.y <= 0) return;

        float scaleX = _board.TileSize / spriteWorldSize.x;
        float scaleY = _board.TileSize / spriteWorldSize.y;
        tileObj.transform.localScale = new Vector3(scaleX, scaleY, 1f);
    }

    private void ApplyEssenceOverlay(Tile tile, SpriteRenderer baseRenderer)
    {
        const string overlayName = "EssenceOverlay";

        // Find or create the overlay child
        var overlayTransform = tile.VisualObject.transform.Find(overlayName);
        GameObject overlayObj;

        if (overlayTransform == null)
        {
            overlayObj = new GameObject(overlayName);
            overlayObj.transform.SetParent(tile.VisualObject.transform, false);
            overlayObj.transform.localPosition = Vector3.zero;

            var sr = overlayObj.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = baseRenderer.sortingLayerID;
            sr.sortingOrder   = baseRenderer.sortingOrder + 1;
            if (tileMaterial != null) sr.material = tileMaterial;
        }
        else
        {
            overlayObj = overlayTransform.gameObject;
        }

        var overlaySr = overlayObj.GetComponent<SpriteRenderer>();

        // Always sync sprite to the base in case it changed
        overlaySr.sprite = baseRenderer.sprite;

        if (tile.EssenceAffinity == EssenceType.None)
        {
            overlayObj.SetActive(false);
            return;
        }

        overlayObj.SetActive(true);

        Color essenceColor = tile.EssenceAffinity switch
        {
            EssenceType.Arcane    => arcaneColor,
            EssenceType.Elemental => elementalColor,
            EssenceType.Force     => forceColor,
            EssenceType.Corrupt   => corruptColor,
            _                     => Color.clear
        };

        essenceColor.a    = essenceOverlayOpacity;
        overlaySr.color   = essenceColor;
    }

    public void RefreshTile(Vector2Int pos)
    {
        var tile = _board.GetTile(pos);
        if (tile != null)
            ApplyTileVisual(tile);
    }
}
