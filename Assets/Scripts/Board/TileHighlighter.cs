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

    // Sorting order offsets relative to base tile sprite
    private const int MoveRangeOrder = 2;
    private const int MoveHoverOrder = 3;
    private const int RangeOrder     = 2; // ability range — won't coexist with move range
    private const int ShapeOrder     = 3;
    private const int AnchorOrder    = 4;
    private const int HoverOrder     = 5;

    private const string MoveRangeChildName = "MoveRangeOverlay";
    private const string MoveHoverChildName = "MoveHoverOverlay";
    private const string RangeChildName     = "RangeOverlay";
    private const string ShapeChildName     = "ShapeOverlay";
    private const string AnchorChildName    = "AnchorOverlay";
    private const string HoverChildName     = "TileHighlight";

    private Board _board;

    // Hover / click state
    private Vector2Int _hoveredPos  = new Vector2Int(-1, -1);
    private Vector2Int _flashingPos = new Vector2Int(-1, -1);
    private Coroutine  _flashCoroutine;

    // Track which tiles currently have overlays so we can clear them
    private readonly List<Vector2Int> _moveRangeTiles = new List<Vector2Int>();
    private readonly List<Vector2Int> _rangeTiles     = new List<Vector2Int>();
    private readonly List<Vector2Int> _shapeTiles     = new List<Vector2Int>();
    private Vector2Int                _anchorTile     = new Vector2Int(-1, -1);
    private Vector2Int                _moveHoveredPos = new Vector2Int(-1, -1);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public void Initialize(Board board)
    {
        _board = board;

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
            var sprite = pos == anchor ? anchorOverlaySprite : shapeOverlaySprite;
            var name   = pos == anchor ? AnchorChildName     : ShapeChildName;
            var order  = pos == anchor ? AnchorOrder         : ShapeOrder;
            SetSpriteOverlay(pos, name, order, sprite);
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

    // ── Overlay primitives ─────────────────────────────────────────────────

    /// Places (or updates) a sprite-based overlay child on the tile's VisualObject.
    private void SetSpriteOverlay(Vector2Int pos, string childName, int sortingOrderOffset, Sprite sprite)
    {
        if (sprite == null) return;

        var tile = _board.GetTile(pos);
        if (tile?.VisualObject == null) return;

        var sr = GetOrCreateOverlay(tile.VisualObject, childName, sortingOrderOffset);
        sr.sprite = sprite;
        sr.color  = Color.white;
        sr.gameObject.SetActive(true);
    }

    /// Places (or updates) a color-tinted copy-of-base-sprite overlay (used for hover/click).
    private void SetColorOverlay(Vector2Int pos, string childName, int sortingOrderOffset, Color color)
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
        var sr     = GetOrCreateOverlay(tile.VisualObject, childName, sortingOrderOffset);
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

    private SpriteRenderer GetOrCreateOverlay(GameObject visualObject, string childName, int sortingOrderOffset)
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
        sr.sortingLayerID = baseSr.sortingLayerID;
        sr.sortingOrder   = baseSr.sortingOrder + sortingOrderOffset;

        return sr;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool IsValidPos(Vector2Int pos) => pos.x >= 0 && pos.y >= 0;
}
