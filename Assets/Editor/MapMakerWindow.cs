using UnityEngine;
using UnityEditor;
using System.IO;

public class MapMakerWindow : EditorWindow
{
    // ── State ──────────────────────────────────────────────────────────────
    private MapData _map;
    private Sprite  _selectedSprite;
    private TerrainTier _selectedTier = TerrainTier.Easy;

    private Vector2 _paletteScroll;
    private Vector2 _gridScroll;

    // Pending resize values (separate from map.width/height until confirmed)
    private int _pendingWidth  = 10;
    private int _pendingHeight = 8;

    // Hover info for status bar
    private int _hoverX = -1;
    private int _hoverY = -1;

    // ── Layout constants ───────────────────────────────────────────────────
    private const float PaletteWidth      = 180f;
    private const float TileDisplaySize   = 64f;
    private const float PaletteSpriteSize = 52f;
    private const float StatusBarHeight   = 20f;

    private static readonly Color[] TierColors =
    {
        new Color(0.25f, 0.60f, 0.25f, 1f), // Easy       – green
        new Color(0.75f, 0.75f, 0.15f, 1f), // Medium     – yellow
        new Color(0.80f, 0.38f, 0.08f, 1f), // Hard       – orange
        new Color(0.18f, 0.18f, 0.18f, 1f), // Impassable – dark grey
    };

    private static readonly string[] TierLabels = { "Easy", "Medium", "Hard", "Impassable" };

    // ── Menu entry ─────────────────────────────────────────────────────────
    [MenuItem("Breakpoint/Map Maker")]
    public static void Open() => GetWindow<MapMakerWindow>("Map Maker");

    // ── OnGUI ──────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        DrawToolbar();

        if (_map == null)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox("Create a new map or drag an existing MapData asset into the Map field above.", MessageType.Info);
            return;
        }

        // Main content area
        Rect contentRect = new Rect(0, GetToolbarHeight(), position.width, position.height - GetToolbarHeight() - StatusBarHeight);
        GUILayout.BeginArea(contentRect);
        EditorGUILayout.BeginHorizontal();
        DrawPalette();
        DrawGrid();
        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();

        DrawStatusBar();
    }

    // ── Toolbar ────────────────────────────────────────────────────────────
    private float GetToolbarHeight() => 46f;

    private void DrawToolbar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.toolbar, GUILayout.Height(GetToolbarHeight()));

        // Row 1: map asset field + actions
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        EditorGUILayout.LabelField("Map", GUILayout.Width(32));
        var newMap = (MapData)EditorGUILayout.ObjectField(_map, typeof(MapData), false, GUILayout.Width(200));
        if (newMap != _map)
        {
            _map = newMap;
            if (_map != null)
            {
                _pendingWidth  = _map.width;
                _pendingHeight = _map.height;
            }
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("New Map", EditorStyles.toolbarButton, GUILayout.Width(70)))
            CreateNewMap();

        GUI.enabled = _map != null;
        if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(50)))
            SaveMap();
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        // Row 2: map settings (only when a map is loaded)
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (_map != null)
        {
            EditorGUILayout.LabelField("Name", GUILayout.Width(38));
            _map.mapName = EditorGUILayout.TextField(_map.mapName, GUILayout.Width(130));

            GUILayout.Space(12);
            EditorGUILayout.LabelField("W", GUILayout.Width(14));
            _pendingWidth = EditorGUILayout.IntField(_pendingWidth, GUILayout.Width(36));
            EditorGUILayout.LabelField("H", GUILayout.Width(14));
            _pendingHeight = EditorGUILayout.IntField(_pendingHeight, GUILayout.Width(36));

            if (GUILayout.Button("Resize", EditorStyles.toolbarButton, GUILayout.Width(52)))
                TryResize();

            GUILayout.Space(12);
            EditorGUILayout.LabelField("Theme", GUILayout.Width(42));
            var newTheme = (MapTheme)EditorGUILayout.ObjectField(_map.theme, typeof(MapTheme), false, GUILayout.Width(160));
            if (newTheme != _map.theme)
            {
                _map.theme = newTheme;
                EditorUtility.SetDirty(_map);
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    // ── Palette ────────────────────────────────────────────────────────────
    private void DrawPalette()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(PaletteWidth));
        _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll, GUILayout.Width(PaletteWidth));

        if (_map == null || _map.theme == null)
        {
            EditorGUILayout.HelpBox("Assign a Map Theme to paint sprites.", MessageType.Info);
        }
        else
        {
            var theme = _map.theme;
            DrawTierSection(TerrainTier.Easy,       theme.easyTiles);
            DrawTierSection(TerrainTier.Medium,     theme.mediumTiles);
            DrawTierSection(TerrainTier.Hard,       theme.hardTiles);
            DrawTierSection(TerrainTier.Impassable, theme.impassableTiles);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawTierSection(TerrainTier tier, Sprite[] sprites)
    {
        int tierIndex = (int)tier;

        // Tier header with color swatch
        EditorGUILayout.BeginHorizontal();
        Rect swatchRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12), GUILayout.Height(12));
        EditorGUI.DrawRect(swatchRect, TierColors[tierIndex]);
        EditorGUILayout.LabelField(TierLabels[tierIndex], EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();

        if (sprites == null || sprites.Length == 0)
        {
            EditorGUILayout.LabelField("  (no sprites)", EditorStyles.miniLabel);
            GUILayout.Space(6);
            return;
        }

        // Draw sprites 3-per-row
        const int cols = 3;
        float spacing = 3f;
        float totalSpacing = spacing * (cols - 1);
        float spriteSize = Mathf.Min(PaletteSpriteSize, (PaletteWidth - 16f - totalSpacing) / cols);

        for (int i = 0; i < sprites.Length; i += cols)
        {
            EditorGUILayout.BeginHorizontal();
            for (int c = 0; c < cols; c++)
            {
                int idx = i + c;
                if (idx >= sprites.Length) break;

                var sprite = sprites[idx];
                if (sprite == null) { GUILayout.Space(spriteSize + spacing); continue; }

                Rect btnRect = GUILayoutUtility.GetRect(spriteSize, spriteSize,
                    GUILayout.Width(spriteSize), GUILayout.Height(spriteSize));

                bool isSelected = _selectedSprite == sprite;

                // Selection highlight
                if (isSelected)
                    EditorGUI.DrawRect(Inflate(btnRect, 3f), Color.cyan);

                // Tier color background
                EditorGUI.DrawRect(btnRect, TierColors[tierIndex]);

                // Sprite
                DrawSpriteInRect(btnRect, sprite);

                // Click detection
                if (Event.current.type == EventType.MouseDown && btnRect.Contains(Event.current.mousePosition))
                {
                    _selectedSprite = sprite;
                    _selectedTier   = tier;
                    Event.current.Use();
                    Repaint();
                }

                GUILayout.Space(spacing);
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(spacing);
        }

        GUILayout.Space(8f);
    }

    // ── Grid ───────────────────────────────────────────────────────────────
    private void DrawGrid()
    {
        if (_map == null || _map.tiles == null) return;

        _gridScroll = EditorGUILayout.BeginScrollView(_gridScroll);

        float totalW = _map.width  * TileDisplaySize;
        float totalH = _map.height * TileDisplaySize;
        Rect gridArea = GUILayoutUtility.GetRect(totalW, totalH);

        Event e = Event.current;
        _hoverX = -1;
        _hoverY = -1;

        for (int y = _map.height - 1; y >= 0; y--)
        {
            int screenRow = (_map.height - 1 - y);

            for (int x = 0; x < _map.width; x++)
            {
                Rect tileRect = new Rect(
                    gridArea.x + x * TileDisplaySize,
                    gridArea.y + screenRow * TileDisplaySize,
                    TileDisplaySize - 1f,
                    TileDisplaySize - 1f
                );

                int dataIndex = _map.GetIndex(x, y);
                if (dataIndex < 0 || dataIndex >= _map.tiles.Length) continue;

                ref var tileData = ref _map.tiles[dataIndex];

                // Tier color background
                EditorGUI.DrawRect(tileRect, TierColors[(int)tileData.tier]);

                // Sprite
                if (tileData.sprite != null)
                    DrawSpriteInRect(tileRect, tileData.sprite);

                // Grid border
                DrawBorder(tileRect, new Color(0f, 0f, 0f, 0.35f));

                bool hovered = tileRect.Contains(e.mousePosition);

                if (hovered)
                {
                    _hoverX = x;
                    _hoverY = y;
                    EditorGUI.DrawRect(tileRect, new Color(1f, 1f, 1f, 0.18f));
                    Repaint();

                    // Paint on click or drag
                    if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
                    {
                        if (_selectedSprite != null)
                        {
                            _map.tiles[dataIndex] = new TileMapData
                            {
                                tier   = _selectedTier,
                                sprite = _selectedSprite
                            };
                            EditorUtility.SetDirty(_map);
                            e.Use();
                            Repaint();
                        }
                    }
                }

                // Coordinate label (small, bottom-left of tile)
                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(1f, 1f, 1f, 0.55f) }
                };
                GUI.Label(new Rect(tileRect.x + 2, tileRect.yMax - 14, 40, 14), $"{x},{y}", labelStyle);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    // ── Status bar ─────────────────────────────────────────────────────────
    private void DrawStatusBar()
    {
        Rect barRect = new Rect(0, position.height - StatusBarHeight, position.width, StatusBarHeight);
        EditorGUI.DrawRect(barRect, new Color(0.18f, 0.18f, 0.18f, 1f));

        string status;
        if (_selectedSprite == null)
            status = "No brush selected — pick a sprite from the palette";
        else if (_hoverX >= 0)
            status = $"Tile ({_hoverX}, {_hoverY})  |  Brush: {_selectedSprite.name}  [{TierLabels[(int)_selectedTier]}]";
        else
            status = $"Brush: {_selectedSprite.name}  [{TierLabels[(int)_selectedTier]}]";

        var style = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f, 1f) }
        };
        GUI.Label(new Rect(barRect.x + 8, barRect.y + 2, barRect.width - 16, barRect.height), status, style);
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static void DrawSpriteInRect(Rect rect, Sprite sprite)
    {
        if (sprite == null || sprite.texture == null) return;

        Texture2D tex = sprite.texture;
        Rect texRect  = sprite.textureRect;

        Rect uvRect = new Rect(
            texRect.x      / tex.width,
            texRect.y      / tex.height,
            texRect.width  / tex.width,
            texRect.height / tex.height
        );

        GUI.DrawTextureWithTexCoords(rect, tex, uvRect);
    }

    private static void DrawBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x,        rect.y,    rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x,        rect.yMax, rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x,        rect.y,    1, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax,     rect.y,    1, rect.height), color);
    }

    private static Rect Inflate(Rect rect, float amount) =>
        new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2, rect.height + amount * 2);

    // ── Map management ─────────────────────────────────────────────────────
    private void CreateNewMap()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create New Map", "NewMap", "asset", "Choose where to save the map asset");

        if (string.IsNullOrEmpty(path)) return;

        var map       = CreateInstance<MapData>();
        map.mapName   = Path.GetFileNameWithoutExtension(path);
        map.width     = 10;
        map.height    = 8;
        map.tiles     = new TileMapData[10 * 8];

        AssetDatabase.CreateAsset(map, path);
        AssetDatabase.SaveAssets();

        _map           = map;
        _pendingWidth  = map.width;
        _pendingHeight = map.height;

        Debug.Log($"[MapMaker] Created new map: {map.mapName}");
    }

    private void SaveMap()
    {
        EditorUtility.SetDirty(_map);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MapMaker] Saved: {_map.mapName}");
    }

    private void TryResize()
    {
        int newW = Mathf.Max(1, _pendingWidth);
        int newH = Mathf.Max(1, _pendingHeight);

        if (newW == _map.width && newH == _map.height) return;

        bool confirmed = EditorUtility.DisplayDialog(
            "Resize Map",
            $"Resize from {_map.width}x{_map.height} to {newW}x{newH}?\n\nTiles outside the new bounds will be lost.",
            "Resize", "Cancel");

        if (!confirmed) return;

        _map.Resize(newW, newH);
        EditorUtility.SetDirty(_map);
        Debug.Log($"[MapMaker] Resized to {newW}x{newH}");
    }
}
