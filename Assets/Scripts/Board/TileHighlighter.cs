using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TileHighlighter : MonoBehaviour
{
    [Header("Hover / Click")]
    [SerializeField] private Color hoverColor         = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color clickColor         = new Color(1f, 0.85f, 0.2f, 0.55f);
    [SerializeField] private float clickFlashDuration = 0.18f;

    [Header("Movement Range Overlay")]
    [SerializeField] private Sprite moveRangeOverlaySprite; // assign a distinct move-range texture
    [SerializeField] private Sprite moveHoverOverlaySprite; // shown on the hovered move-target tile

    [Header("Ability Range Overlay")]
    [SerializeField] private Sprite rangeOverlaySprite;     // blueprint grid texture

    [Header("Shape Preview Overlays")]
    [SerializeField] private Sprite shapeOverlaySprite;     // orange shape texture
    [SerializeField] private Sprite anchorOverlaySprite;    // red anchor texture

    [Header("Multi-Select Overlay")]
    // Tints the anchor sprite (not a separate texture) — a solid-ish hue shift reads clearly as
    // "the anchor icon, but a different color" since it's recoloring a purpose-built shape rather
    // than translucently blending over a tile's own (often busy) terrain sprite.
    [SerializeField] private Color multiSelectColor = new Color(0.25f, 0.85f, 1f, 0.9f);

    // Sorting order offsets relative to base tile sprite
    private const int MoveRangeOrder    = 2;
    private const int MoveHoverOrder    = 3;
    private const int RangeOrder        = 2; // ability range — won't coexist with move range
    private const int ShapeOrder        = 3;
    private const int AnchorOrder       = 4;
    private const int MultiSelectOrder  = 4; // never coexists with Shape/Anchor — MultiTargeting doesn't use ShowShape
    private const int HoverOrder        = 5;

    private const string MoveRangeChildName    = "MoveRangeOverlay";
    private const string MoveHoverChildName    = "MoveHoverOverlay";
    private const string RangeChildName        = "RangeOverlay";
    private const string ShapeChildName        = "ShapeOverlay";
    private const string AnchorChildName       = "AnchorOverlay";
    private const string MultiSelectChildName  = "MultiSelectOverlay";
    private const string HoverChildName        = "TileHighlight";

    // Cached in Initialize() (SortingLayer.NameToID can't run in a static/instance field
    // initializer — Unity throws, since that runs during MonoBehaviour construction). See
    // GetOrCreateOverlay for why every overlay targets this layer explicitly instead of
    // inheriting the tile's own (topmost layer in this project's Sorting Layers stack, so
    // anything on it renders above every fighter regardless of Order-in-Layer).
    private int _charactersLayerId;

    private Board _board;

    // Hover / click state
    private Vector2Int _hoveredPos  = new Vector2Int(-1, -1);
    private Vector2Int _flashingPos = new Vector2Int(-1, -1);
    private Coroutine  _flashCoroutine;

    // Track which tiles currently have overlays so we can clear them
    private readonly List<Vector2Int> _moveRangeTiles = new List<Vector2Int>();
    private readonly List<Vector2Int> _rangeTiles     = new List<Vector2Int>();
    private readonly List<Vector2Int> _shapeTiles     = new List<Vector2Int>();
    private readonly List<Vector2Int> _multiSelectTiles = new List<Vector2Int>();
    private Vector2Int                _anchorTile     = new Vector2Int(-1, -1);
    private Vector2Int                _moveHoveredPos = new Vector2Int(-1, -1);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public void Initialize(Board board)
    {
        _board = board;
        _charactersLayerId = SortingLayer.NameToID("Characters");

        InputHandler.OnTileHovered += HandleHover;
        InputHandler.OnTileClicked += HandleClick;
        InputHandler.OnBoardExited += HandleBoardExit;

        Debug.Log("[TileHighlighter] Initialized and subscribed to InputHandler events");
    }

    private void OnDestroy()
    {
        InputHandler.OnTileHovered -= HandleHover;
        InputHandler.OnTileClicked -= HandleClick;
        InputHandler.OnBoardExited -= HandleBoardExit;
    }

    // ── Hover / Click (existing) ───────────────────────────────────────────

    private void HandleHover(Vector2Int pos)
    {
        if (_hoveredPos != pos && IsValidPos(_hoveredPos) && _hoveredPos != _flashingPos)
            SetColorOverlay(_hoveredPos, HoverChildName, HoverOrder, Color.clear);

        _hoveredPos = pos;

        if (pos != _flashingPos)
            SetColorOverlay(pos, HoverChildName, HoverOrder, hoverColor);
    }

    private void HandleClick(Vector2Int pos)
    {
        if (_flashCoroutine != null)
        {
            StopCoroutine(_flashCoroutine);

            if (_flashingPos != pos && IsValidPos(_flashingPos))
                SetColorOverlay(_flashingPos, HoverChildName, HoverOrder,
                    _flashingPos == _hoveredPos ? hoverColor : Color.clear);
        }

        _flashingPos    = pos;
        _flashCoroutine = StartCoroutine(FlashRoutine(pos));
    }

    private void HandleBoardExit()
    {
        if (IsValidPos(_hoveredPos) && _hoveredPos != _flashingPos)
            SetColorOverlay(_hoveredPos, HoverChildName, HoverOrder, Color.clear);

        _hoveredPos = new Vector2Int(-1, -1);
    }

    private IEnumerator FlashRoutine(Vector2Int pos)
    {
        SetColorOverlay(pos, HoverChildName, HoverOrder, clickColor);
        yield return new WaitForSeconds(clickFlashDuration);
        SetColorOverlay(pos, HoverChildName, HoverOrder, pos == _hoveredPos ? hoverColor : Color.clear);
        _flashingPos    = new Vector2Int(-1, -1);
        _flashCoroutine = null;
    }

    // ── Movement range overlay (public) ────────────────────────────────────

    public void ShowMoveRange(IEnumerable<Vector2Int> tiles)
    {
        ClearMoveRange();
        foreach (var pos in tiles)
        {
            SetSpriteOverlay(pos, MoveRangeChildName, MoveRangeOrder, moveRangeOverlaySprite);
            _moveRangeTiles.Add(pos);
        }
    }

    public void ClearMoveRange()
    {
        foreach (var pos in _moveRangeTiles)
            HideOverlay(pos, MoveRangeChildName);
        _moveRangeTiles.Clear();
    }

    // ── Move hover overlay (public) ────────────────────────────────────────

    public void ShowMoveHover(Vector2Int pos)
    {
        if (_moveHoveredPos == pos) return;
        ClearMoveHover();
        _moveHoveredPos = pos;
        SetSpriteOverlay(pos, MoveHoverChildName, MoveHoverOrder, moveHoverOverlaySprite);
    }

    public void ClearMoveHover()
    {
        if (_moveHoveredPos.x >= 0)
            HideOverlay(_moveHoveredPos, MoveHoverChildName);
        _moveHoveredPos = new Vector2Int(-1, -1);
    }

    // ── Ability range overlay (public) ─────────────────────────────────────

    public void ShowRange(List<Vector2Int> tiles)
    {
        ClearRange();
        foreach (var pos in tiles)
        {
            SetSpriteOverlay(pos, RangeChildName, RangeOrder, rangeOverlaySprite);
            _rangeTiles.Add(pos);
        }
    }

    public void ClearRange()
    {
        foreach (var pos in _rangeTiles)
            HideOverlay(pos, RangeChildName);
        _rangeTiles.Clear();
    }

    // ── Shape preview overlay (public) ────────────────────────────────────

    public void ShowShape(List<Vector2Int> shapeTiles, Vector2Int anchor)
    {
        ClearShape();

        foreach (var pos in shapeTiles)
        {
            // The whole shape preview renders above characters, not just the anchor — unlike
            // Range/MoveRange (a broad "you could click anywhere here" zone), this is the precise
            // "these are the tiles that will actually be hit" preview, which needs to read clearly
            // over whichever fighters are standing in it. Some shapes (Ring) don't always include
            // the anchor as one of the affected tiles at all, so this can't be anchor-only anyway.
            bool isAnchor = pos == anchor;
            var sprite = isAnchor ? anchorOverlaySprite : shapeOverlaySprite;
            var name   = isAnchor ? AnchorChildName     : ShapeChildName;
            var order  = isAnchor ? AnchorOrder         : ShapeOrder;
            SetSpriteOverlay(pos, name, order, sprite, aboveCharacters: true);
            _shapeTiles.Add(pos);
        }

        _anchorTile = anchor;
    }

    public void ClearShape()
    {
        foreach (var pos in _shapeTiles)
        {
            HideOverlay(pos, ShapeChildName);
            HideOverlay(pos, AnchorChildName);
        }
        _shapeTiles.Clear();
        _anchorTile = new Vector2Int(-1, -1);
    }

    // ── Multi-select overlay (public) ──────────────────────────────────────

    public void ShowMultiSelect(IEnumerable<Vector2Int> tiles)
    {
        ClearMultiSelect();
        foreach (var pos in tiles)
        {
            // Reuses the anchor tile's own sprite, hue-shifted, rather than a flat tint over the
            // ground tile underneath — same visual language as "you're targeting this tile",
            // recolored to read as "already picked" instead of "currently hovered". A translucent
            // tint of the tile's own (often busy/terrain) sprite was hard to make out over a
            // fighter; the anchor icon is a purpose-built shape, so it reads clearly recolored.
            SetSpriteOverlay(pos, MultiSelectChildName, MultiSelectOrder, anchorOverlaySprite,
                              aboveCharacters: true, tint: multiSelectColor);
            _multiSelectTiles.Add(pos);
        }
    }

    public void ClearMultiSelect()
    {
        foreach (var pos in _multiSelectTiles)
            HideOverlay(pos, MultiSelectChildName);
        _multiSelectTiles.Clear();
    }

    // ── Overlay primitives ─────────────────────────────────────────────────

    /// Places (or updates) a sprite-based overlay child on the tile's VisualObject.
    /// aboveCharacters: true renders it on the Characters sorting layer (above every fighter) —
    /// reserved for "you're pointing at/have picked this specific tile" indicators (anchor,
    /// shape preview, multi-select picks). Range/MoveRange are the only zone markers left at
    /// ground level (false, the default), inheriting the tile's own layer, same as before.
    /// tint: null (default) draws the sprite as-authored; a color re-tints it (e.g. multi-select
    /// reusing the anchor sprite in a different hue instead of its own dedicated texture).
    private void SetSpriteOverlay(Vector2Int pos, string childName, int sortingOrderOffset, Sprite sprite, bool aboveCharacters = false, Color? tint = null)
    {
        if (sprite == null) return;

        var tile = _board.GetTile(pos);
        if (tile?.VisualObject == null) return;

        var sr = GetOrCreateOverlay(tile.VisualObject, childName, sortingOrderOffset, aboveCharacters);
        sr.sprite = sprite;
        sr.color  = tint ?? Color.white;
        sr.gameObject.SetActive(true);
    }

    /// Places (or updates) a color-tinted copy-of-base-sprite overlay (used for hover/click).
    /// See SetSpriteOverlay for what aboveCharacters means.
    private void SetColorOverlay(Vector2Int pos, string childName, int sortingOrderOffset, Color color, bool aboveCharacters = false)
    {
        var tile = _board.GetTile(pos);
        if (tile == null) { Debug.LogWarning($"[TileHighlighter] GetTile null for {pos}"); return; }
        if (tile.VisualObject == null) { Debug.LogWarning($"[TileHighlighter] No VisualObject at {pos}"); return; }

        if (color.a <= 0f)
        {
            HideOverlay(pos, childName);
            return;
        }

        var baseSr = tile.VisualObject.GetComponent<SpriteRenderer>();
        var sr     = GetOrCreateOverlay(tile.VisualObject, childName, sortingOrderOffset, aboveCharacters);
        sr.sprite = baseSr.sprite;
        sr.color  = color;
        sr.gameObject.SetActive(true);
    }

    private void HideOverlay(Vector2Int pos, string childName)
    {
        var tile = _board.GetTile(pos);
        if (tile?.VisualObject == null) return;

        var child = tile.VisualObject.transform.Find(childName);
        if (child != null)
            child.gameObject.SetActive(false);
    }

    // aboveCharacters: false (default/most overlays) inherits the tile's own ground-level
    // sorting layer, same as always — move range, ability range, and the shape preview are zone
    // markers meant to sit under fighters. true is reserved for "this specific tile" indicators
    // (the anchor tile, multi-select picks) — those explicitly target the Characters layer
    // instead, since the project's Sorting Layers stack is Default < Board < Particles <
    // Characters, so nothing on the tile's own (Board) layer can render above a fighter no
    // matter what Order-in-Layer value is used; Sorting Layer beats Order-in-Layer.
    private SpriteRenderer GetOrCreateOverlay(GameObject visualObject, string childName, int sortingOrderOffset, bool aboveCharacters = false)
    {
        var existing = visualObject.transform.Find(childName);

        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            return existing.GetComponent<SpriteRenderer>();
        }

        var obj = new GameObject(childName);
        obj.transform.SetParent(visualObject.transform, false);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localScale    = Vector3.one;

        var sr            = obj.AddComponent<SpriteRenderer>();
        var baseSr        = visualObject.GetComponent<SpriteRenderer>();

        if (aboveCharacters)
        {
            sr.sortingLayerID = _charactersLayerId;
            sr.sortingOrder   = sortingOrderOffset;
        }
        else
        {
            sr.sortingLayerID = baseSr.sortingLayerID;
            sr.sortingOrder   = baseSr.sortingOrder + sortingOrderOffset;
        }

        return sr;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool IsValidPos(Vector2Int pos) => pos.x >= 0 && pos.y >= 0;
}
