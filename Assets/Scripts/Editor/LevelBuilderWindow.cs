using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL BUILDER  —  2D Katman Editörü  (Şekil Çizme + Export)
//  BlockMerge3D  •  BlockMerge3D / 🗂 Level Builder
// ═══════════════════════════════════════════════════════════════════
public class LevelBuilderWindow : EditorWindow
{
    private enum DrawMode { Shape, Prefilled, Ice, Erase, DisableCell }

    // ── Sabitler ─────────────────────────────────────────────────
    private const string LEVELS_PATH = "Assets/Levels";
    private const string SHAPES_PATH = "Assets/Shapes";
    private const float  MIN_CELL_PX = 18f;
    private const float  MAX_CELL_PX = 60f;
    private const string PREF_DEFAULT_CUBE = "BlockMerge3D_DefaultCubePrefab";

    private static readonly Color COL_BG         = new Color(0.08f, 0.08f, 0.11f);
    private static readonly Color COL_GRID        = new Color(0.3f, 0.32f, 0.4f);
    private static readonly Color COL_OCCUPIED    = new Color(0.25f, 0.6f, 0.95f);
    private static readonly Color COL_PREFILLED   = new Color(0.85f, 0.70f, 0.20f);
    private static readonly Color COL_ICE         = new Color(0.92f, 0.95f, 1.00f); // Beyaz-mavi buz rengi ❄️
    private static readonly Color COL_HOVER_ADD   = new Color(0.25f, 0.85f, 0.55f, 0.75f);
    private static readonly Color COL_HOVER_ERASE = new Color(1.00f, 0.28f, 0.20f, 0.75f);
    private static readonly Color COL_GHOST       = new Color(0.28f, 0.30f, 0.36f, 0.35f);
    private static readonly Color COL_HEADER      = new Color(0.35f, 0.78f, 1.00f);

    private static readonly Color[] PREFILL_COLORS = new Color[]
    {
        new Color(0.95f, 0.30f, 0.30f),
        new Color(0.25f, 0.65f, 0.95f),
        new Color(0.25f, 0.85f, 0.40f),
        new Color(0.95f, 0.80f, 0.15f),
        new Color(0.70f, 0.20f, 0.90f),
        new Color(0.95f, 0.55f, 0.15f),
    };

    // ── Level Ayarları ────────────────────────────────────────────
    private string levelName   = "NewLevel";
    private float  levelTime   = 60f;
    private int    levelTarget = 100;

    // ── Grid Durumu ───────────────────────────────────────────────
    private Vector3Int gridSize = new Vector3Int(5, 5, 5);
    private float      cellSize = 1.0f;
    private float      spacing  = 0.1f;

    private HashSet<Vector3Int> occupiedCells   = new HashSet<Vector3Int>();
    private List<Vector3Int>    prefilledCells  = new List<Vector3Int>();
    private List<int>           prefilledMatIdx = new List<int>();
    private List<Vector3Int>    frozenCells     = new List<Vector3Int>();
    private HashSet<Vector3Int> disabledCells   = new HashSet<Vector3Int>();

    // ── UI Durumu ─────────────────────────────────────────────────
    private int      activeLayer      = 0;
    private DrawMode drawMode         = DrawMode.Shape;
    private int      activePrefilledColor = 0;
    private float    cellPx           = 32f;
    private bool     showGhostLayers  = true;
    private Vector2  leftScroll, rightScroll;
    private Vector2? hoverCell;
    private int      clearRowIndex    = 0;
    private int      clearColIndex    = 0;
    private bool     isLeftMouseDown  = false;
    private bool     isRightMouseDown = false;
    private bool     hideEmptyGrid    = false;

    private GameObject cubePrefab;

    // ── Stil ─────────────────────────────────────────────────────
    private GUIStyle styleHeader, styleBox, styleModeBtn;
    private bool     stylesBuilt;

    public System.Action onRepaintRequested;

    new public void Repaint()
    {
        base.Repaint();
        if (onRepaintRequested != null)
            onRepaintRequested();
    }

    // [MenuItem("BlockMerge3D/🗂  Level Builder")]
    // public static void Open()
    // {
    //     var w = GetWindow<LevelBuilderWindow>("Level Builder");
    //     w.minSize = new Vector2(900, 560);
    // }

    private void OnEnable()
    {
        // Global default cube prefab'ı yükle
        string prefabPath = EditorPrefs.GetString(PREF_DEFAULT_CUBE, "");
        if (!string.IsNullOrEmpty(prefabPath))
        {
            cubePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }
    }

    public void OnGUI()
    {
        // Handle keyboard shortcuts for switching layers
        Event e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.PageUp)
            {
                if (activeLayer < gridSize.y - 1)
                {
                    activeLayer++;
                    Repaint();
                    e.Use();
                }
            }
            else if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.PageDown)
            {
                if (activeLayer > 0)
                {
                    activeLayer--;
                    Repaint();
                    e.Use();
                }
            }
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                if (EditorUtility.DisplayDialog("Katmanı Temizle", $"Y={activeLayer} katmanındaki tüm küpleri temizlemek istiyor musunuz?", "Evet", "Hayır"))
                {
                    ClearLayer(activeLayer);
                    e.Use();
                }
            }
        }

        BuildStyles();
        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel();
        DrawCenterGrid();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();
        DrawStatusBar();
    }

    // ── Sol Panel ─────────────────────────────────────────────────
    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(280), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        // Kaydet & Dışa Aktar Bölümü
        GUILayout.Label("KAYDET VE DIŞA AKTAR", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        GUI.backgroundColor = new Color(0.2f, 0.6f, 1f, 1f); // Doygun mavi
        if (GUILayout.Button("💾  SEVİYEYİ KAYDET", GUILayout.Height(36)))
        {
            ExportLevel(true);
        }
        
        GUILayout.Space(5);
        
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f, 1f); // Doygun yeşil
        if (GUILayout.Button("⬆  SEVİYEYİ DIŞA AKTAR", GUILayout.Height(36)))
        {
            ExportLevel(false);
        }
        
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();

        GUILayout.Space(12);

        // Level Ayarları
        GUILayout.Label("LEVEL AYARLARI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        levelName   = EditorGUILayout.TextField("Seviye Adı", levelName);
        levelTime   = EditorGUILayout.FloatField("Süre (sn)", levelTime);
        levelTarget = EditorGUILayout.IntField("Hedef Puan", levelTarget);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // Grid Boyutu
        GUILayout.Label("GRID BOYUTU", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUI.BeginChangeCheck();
        gridSize = EditorGUILayout.Vector3IntField("W × H × D", gridSize);
        gridSize = new Vector3Int(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y), Mathf.Max(1, gridSize.z));
        if (EditorGUI.EndChangeCheck())
            activeLayer = Mathf.Clamp(activeLayer, 0, gridSize.y - 1);
        cellSize  = EditorGUILayout.FloatField("Cell Size", cellSize);
        spacing   = EditorGUILayout.Slider("Gap", spacing, 0f, 0.5f);
        
        EditorGUI.BeginChangeCheck();
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab (Global)", cubePrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck() && cubePrefab != null)
        {
            // Global default olarak kaydet
            string path = UnityEditor.AssetDatabase.GetAssetPath(cubePrefab);
            EditorPrefs.SetString(PREF_DEFAULT_CUBE, path);
        }
        
        if (cubePrefab != null)
        {
            EditorGUILayout.HelpBox("✓ Bu küp prefabı tüm yeni levellerde otomatik kullanılacak.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("⚠ Küp prefabı seçilmedi! Lütfen bir küp prefabı atayın.", MessageType.Warning);
        }
        
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // Aksiyonlar
        GUILayout.Label("AKSİYONLAR", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Katmanı Doldur"))   FillLayer(activeLayer);
        if (GUILayout.Button("Katmanı Temizle"))  ClearLayer(activeLayer);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Hepsini Doldur"))   FillAll();
        if (GUILayout.Button("Hepsini Temizle"))  ClearAll();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6);
        EditorGUILayout.LabelField("Özel Temizleme (Bu Katman)", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Hazır Küpleri Sil", EditorStyles.miniButton)) ClearPrefilledInLayer(activeLayer);
        if (GUILayout.Button("Şekil Küplerini Sil", EditorStyles.miniButton)) ClearShapesInLayer(activeLayer);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        clearRowIndex = EditorGUILayout.IntField("Satır Sil (Z)", clearRowIndex);
        if (GUILayout.Button("Satır Temizle", EditorStyles.miniButton, GUILayout.Width(95))) ClearRowInLayer(activeLayer, clearRowIndex);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        clearColIndex = EditorGUILayout.IntField("Sütun Sil (X)", clearColIndex);
        if (GUILayout.Button("Sütun Temizle", EditorStyles.miniButton, GUILayout.Width(95))) ClearColumnInLayer(activeLayer, clearColIndex);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        GUILayout.Space(12);

        // Kısayollar ve İpuçları
        GUILayout.Label("💡 KISAYOLLAR & İPUÇLARI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("• Sol Tık:", "Küp/buz/renk yerleştirir", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("• Sağ Tık:", "Hücreyi temizler/siler", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("• Mouse Tekerleği:", "Katmanlar arası geçiş yapar", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("• Yön Tuşları (↑ / ↓):", "Katman değiştirir", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("• Sil / Backspace:", "Aktif katmanı temizler", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ── Merkez Grid ───────────────────────────────────────────────
    private void DrawCenterGrid()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // Mod butonları
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("🖌️ ÇİZİM FIRÇASI SEÇİN", styleHeader);
        EditorGUILayout.BeginHorizontal();
        DrawModeBtn("✏️  Şekil Küpü Çiz",   DrawMode.Shape);
        DrawModeBtn("🎨  Renkli Küp Koy",   DrawMode.Prefilled);
        DrawModeBtn("❄️  Buz Yerleştir",   DrawMode.Ice);
        DrawModeBtn("🚫  Grid Hücresi Sil", DrawMode.DisableCell);
        DrawModeBtn("✕  Silgi (Sil)",       DrawMode.Erase);
        EditorGUILayout.EndHorizontal();

        if (drawMode == DrawMode.Prefilled)
        {
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Küp Rengi Seçin:", EditorStyles.boldLabel, GUILayout.Width(120));
            for (int i = 0; i < PREFILL_COLORS.Length; i++)
            {
                GUI.backgroundColor = PREFILL_COLORS[i];
                var s = new GUIStyle(GUI.skin.button) { fixedWidth = 28, fixedHeight = 28 };
                if (GUILayout.Button("", s)) activePrefilledColor = i;
                if (i == activePrefilledColor)
                {
                    Rect sel = GUILayoutUtility.GetLastRect();
                    DrawOutline(sel, Color.white, 2);
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Space(4);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();

        // Prominent Horizontal Layer Selector Row
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("📍 DÜZENLEME KATMANI (YÜKSEKLİK)", styleHeader);
        
        EditorGUILayout.BeginHorizontal();
        
        GUI.enabled = activeLayer > 0;
        if (GUILayout.Button("◀ BİR ALT KATMAN", GUILayout.Height(30), GUILayout.Width(130)))
        {
            activeLayer--;
            Repaint();
        }
        GUI.enabled = true;
        
        GUILayout.Space(4);
        
        for (int y = 0; y < gridSize.y; y++)
        {
            int shapeCount = occupiedCells.Count(c => c.y == y && !prefilledCells.Contains(c) && !frozenCells.Contains(c));
            int prefilledCount = prefilledCells.Count(c => c.y == y);
            int iceCount = frozenCells.Count(c => c.y == y);
            
            string label = $"KATMAN Y={y}\n";
            List<string> icons = new List<string>();
            if (shapeCount > 0) icons.Add($"{shapeCount}✏️");
            if (prefilledCount > 0) icons.Add($"{prefilledCount}⬛");
            if (iceCount > 0) icons.Add($"{iceCount}❄️");
            
            if (icons.Count > 0)
            {
                label += string.Join(" + ", icons);
            }
            else
            {
                label += "Boş";
            }
            
            bool isActive = (y == activeLayer);
            GUI.backgroundColor = isActive ? new Color(0.2f, 0.75f, 1f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
            
            var btnStyle = new GUIStyle(GUI.skin.button) {
                fontSize = 10,
                fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                fixedHeight = 36
            };
            
            if (GUILayout.Button(label, btnStyle, GUILayout.ExpandWidth(true)))
            {
                activeLayer = y;
                Repaint();
            }
            GUI.backgroundColor = Color.white;
        }
        
        GUILayout.Space(4);
        
        GUI.enabled = activeLayer < gridSize.y - 1;
        if (GUILayout.Button("BİR ÜST KATMAN ▶", GUILayout.Height(30), GUILayout.Width(130)))
        {
            activeLayer++;
            Repaint();
        }
        GUI.enabled = true;
        
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        // Display Toggles and Zoom Panel directly above the Grid
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        showGhostLayers = GUILayout.Toggle(showGhostLayers, "💡 Diğer Katmanları Göster (Ghost)", GUILayout.Height(20));
        GUILayout.Space(10);
        hideEmptyGrid = GUILayout.Toggle(hideEmptyGrid, "👁️ Boş Hücreleri Gizle", GUILayout.Height(20));
        GUILayout.FlexibleSpace();
        GUILayout.Label("Büyüteç (Zoom):", EditorStyles.miniLabel);
        cellPx = EditorGUILayout.Slider(cellPx, MIN_CELL_PX, MAX_CELL_PX, GUILayout.Width(130));
        EditorGUILayout.EndHorizontal();

        Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        HandleGridInput(area);
        DrawGrid2D(area);

        EditorGUILayout.EndVertical();
    }

    private void DrawModeBtn(string label, DrawMode mode)
    {
        Color activeColor = mode switch
        {
            DrawMode.Shape       => new Color(0.3f, 0.75f, 1.0f),  // Sky Blue
            DrawMode.Prefilled   => new Color(1.0f, 0.75f, 0.2f),  // Warm Gold
            DrawMode.Ice         => new Color(0.5f, 0.88f, 1.0f),  // Ice Cyan
            DrawMode.DisableCell => new Color(0.8f, 0.4f, 0.9f),   // Soft purple/magenta
            DrawMode.Erase       => new Color(1.0f, 0.35f, 0.35f), // Coral Red
            _                    => Color.white
        };
        
        bool isActive = (drawMode == mode);
        GUI.backgroundColor = isActive ? activeColor : new Color(0.85f, 0.85f, 0.85f);
        
        var btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
            normal = { textColor = isActive ? Color.white : Color.black }
        };
        
        if (GUILayout.Button(label, btnStyle, GUILayout.Height(32), GUILayout.ExpandWidth(true)))
        {
            drawMode = mode;
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawGrid2D(Rect area)
    {
        if (Event.current.type != EventType.Repaint) return;
        int W = gridSize.x, D = gridSize.z;
        float tw = cellPx * W, th = cellPx * D;
        float ox = area.x + (area.width  - tw) * 0.5f;
        float oy = area.y + (area.height - th) * 0.5f;

        // Draw the background area
        EditorGUI.DrawRect(area, COL_BG);

        for (int x = 0; x < W; x++)
        {
            for (int z = 0; z < D; z++)
            {
                var cell = new Vector3Int(x, activeLayer, z);
                Rect cellRect = new Rect(ox + x * cellPx, oy + z * cellPx, cellPx, cellPx);
                
                // If it is disabled, draw it with a very faint outline ONLY if in DisableCell mode, otherwise skip
                if (disabledCells.Contains(cell))
                {
                    if (drawMode == DrawMode.DisableCell)
                    {
                        DrawOutline(cellRect, new Color(0.8f, 0.4f, 0.9f, 0.15f), 1);
                    }
                    continue;
                }
                
                // If hideEmptyGrid is true and it's not occupied, do NOT draw it!
                if (hideEmptyGrid && !occupiedCells.Contains(cell)) continue;

                DrawOutline(cellRect, COL_GRID, 1);
            }
        }

        // Ghost
        if (showGhostLayers)
        {
            foreach (var cell in occupiedCells)
            {
                if (cell.y == activeLayer) continue;
                float a = Mathf.Clamp(1f - Mathf.Abs(cell.y - activeLayer) * 0.25f, 0.05f, 0.28f);
                EditorGUI.DrawRect(new Rect(ox + cell.x * cellPx + 1, oy + cell.z * cellPx + 1, cellPx - 2, cellPx - 2),
                    new Color(COL_GHOST.r, COL_GHOST.g, COL_GHOST.b, a));
            }
        }

        // Hücreler
        foreach (var cell in occupiedCells)
        {
            if (cell.y != activeLayer) continue;
            bool isPf = prefilledCells.Contains(cell);
            bool isIce = frozenCells.Contains(cell);
            Color col = isPf ? COL_PREFILLED : (isIce ? COL_ICE : COL_OCCUPIED);
            if (isPf)
            {
                int idx = prefilledCells.IndexOf(cell);
                if (idx >= 0 && idx < prefilledMatIdx.Count)
                    col = PREFILL_COLORS[prefilledMatIdx[idx] % PREFILL_COLORS.Length];
            }
            EditorGUI.DrawRect(new Rect(ox + cell.x * cellPx + 1.5f, oy + cell.z * cellPx + 1.5f, cellPx - 3, cellPx - 3), col);

            if (isIce && cellPx >= 18)
            {
                var iceLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(cellPx * 0.50f), 9, 22)
                };
                GUI.Label(new Rect(ox + cell.x * cellPx, oy + cell.z * cellPx, cellPx, cellPx), "❄️", iceLabelStyle);
            }
        }

        // Hover
        if (hoverCell.HasValue)
        {
            int hx = Mathf.RoundToInt(hoverCell.Value.x);
            int hz = Mathf.RoundToInt(hoverCell.Value.y);
            if (hx >= 0 && hx < W && hz >= 0 && hz < D)
            {
                bool exists = occupiedCells.Contains(new Vector3Int(hx, activeLayer, hz));
                Rect hoverRect = new Rect(ox + hx * cellPx + 1.5f, oy + hz * cellPx + 1.5f, cellPx - 3, cellPx - 3);
                
                if (drawMode == DrawMode.Erase)
                {
                    if (exists) EditorGUI.DrawRect(hoverRect, COL_HOVER_ERASE);
                }
                else if (drawMode == DrawMode.Shape)
                {
                    EditorGUI.DrawRect(hoverRect, new Color(COL_OCCUPIED.r, COL_OCCUPIED.g, COL_OCCUPIED.b, 0.7f));
                }
                else if (drawMode == DrawMode.Prefilled)
                {
                    Color pColor = PREFILL_COLORS[activePrefilledColor % PREFILL_COLORS.Length];
                    EditorGUI.DrawRect(hoverRect, new Color(pColor.r, pColor.g, pColor.b, 0.7f));
                }
                else if (drawMode == DrawMode.Ice)
                {
                    EditorGUI.DrawRect(hoverRect, new Color(0.5f, 0.85f, 1.0f, 0.7f));
                    if (cellPx >= 22)
                    {
                        var iceLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = Mathf.Clamp(Mathf.RoundToInt(cellPx * 0.45f), 10, 20),
                            normal = { textColor = new Color(1, 1, 1, 0.6f) }
                        };
                        GUI.Label(hoverRect, "❄️", iceLabelStyle);
                    }
                }
                else if (drawMode == DrawMode.DisableCell)
                {
                    EditorGUI.DrawRect(hoverRect, new Color(0.8f, 0.4f, 0.9f, 0.7f));
                    if (cellPx >= 22)
                    {
                        var lblStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = Mathf.Clamp(Mathf.RoundToInt(cellPx * 0.45f), 10, 20),
                            normal = { textColor = Color.white }
                        };
                        GUI.Label(hoverRect, "🚫", lblStyle);
                    }
                }
            }
        }

        // Eksen
        var lbl = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = new Color(1,1,1,0.35f) } };
        for (int x = 0; x < W; x++) GUI.Label(new Rect(ox + x * cellPx, oy - 14, cellPx, 14), x.ToString(), lbl);
        for (int z = 0; z < D; z++) GUI.Label(new Rect(ox - 18, oy + z * cellPx, 18, cellPx), z.ToString(), lbl);
        GUI.Label(new Rect(ox - 18, oy - 14, 18, 14), "Z\\X", lbl);

        // Big Overlay Info
        string toolName = drawMode switch
        {
            DrawMode.Shape       => "✏️ Şekil Küpü",
            DrawMode.Prefilled   => "⬛ Renkli Küp",
            DrawMode.Ice         => "❄️ Buz Bloğu",
            DrawMode.DisableCell => "🚫 Grid Sil",
            DrawMode.Erase       => "❌ Silgi",
            _                    => ""
        };
        var headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.35f, 0.78f, 1f) }
        };
        GUI.Label(new Rect(area.x + 8, area.y + 6, 400, 20), $"📍 Katman Y = {activeLayer}   •   Fırça: {toolName}", headerStyle);
    }

    // ── Grid Input ────────────────────────────────────────────────
    private void HandleGridInput(Rect area)
    {
        Event e = Event.current;
        int W = gridSize.x, D = gridSize.z;
        float tw = cellPx * W, th = cellPx * D;
        float ox = area.x + (area.width  - tw) * 0.5f;
        float oy = area.y + (area.height - th) * 0.5f;

        bool inside = area.Contains(e.mousePosition);
        if (inside)
        {
            int gx = Mathf.FloorToInt((e.mousePosition.x - ox) / cellPx);
            int gz = Mathf.FloorToInt((e.mousePosition.y - oy) / cellPx);
            hoverCell = (gx >= 0 && gx < W && gz >= 0 && gz < D) ? new Vector2(gx, gz) : (Vector2?)null;
        }
        else hoverCell = null;

        if (e.rawType == EventType.MouseDown)
        {
            if (e.button == 0) { isLeftMouseDown = true; isRightMouseDown = false; }
            if (e.button == 1) { isRightMouseDown = true; isLeftMouseDown = false; }
        }
        else if (e.rawType == EventType.MouseUp)
        {
            if (e.button == 0) isLeftMouseDown = false;
            if (e.button == 1) isRightMouseDown = false;
        }

        if (e.type == EventType.ContextClick && inside)
        {
            e.Use();
        }

        if (inside && hoverCell.HasValue)
        {
            if (isLeftMouseDown)
            {
                ApplyBrush(new Vector3Int(Mathf.RoundToInt(hoverCell.Value.x), activeLayer, Mathf.RoundToInt(hoverCell.Value.y)));
                if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) e.Use();
                Repaint();
            }
            else if (isRightMouseDown)
            {
                EraseCell(new Vector3Int(Mathf.RoundToInt(hoverCell.Value.x), activeLayer, Mathf.RoundToInt(hoverCell.Value.y)));
                if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) e.Use();
                Repaint();
            }
        }
        if (e.type == EventType.ScrollWheel && inside)
        {
            if (e.delta.y < 0)
            {
                if (activeLayer < gridSize.y - 1)
                {
                    activeLayer++;
                    Repaint();
                    e.Use();
                }
            }
            else if (e.delta.y > 0)
            {
                if (activeLayer > 0)
                {
                    activeLayer--;
                    Repaint();
                    e.Use();
                }
            }
        }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) Repaint();
    }

    private void ApplyBrush(Vector3Int c)
    {
        switch (drawMode)
        {
            case DrawMode.Shape:
                disabledCells.Remove(c);
                occupiedCells.Add(c);
                RemoveFromPrefilled(c);
                frozenCells.Remove(c);
                break;
            case DrawMode.Ice:
                disabledCells.Remove(c);
                occupiedCells.Add(c);
                RemoveFromPrefilled(c);
                if (!frozenCells.Contains(c)) frozenCells.Add(c);
                break;
            case DrawMode.Erase:
                EraseCell(c);
                break;
            case DrawMode.Prefilled:
                disabledCells.Remove(c);
                occupiedCells.Add(c);
                frozenCells.Remove(c);
                int existing = prefilledCells.IndexOf(c);
                if (existing >= 0) prefilledMatIdx[existing] = activePrefilledColor;
                else { prefilledCells.Add(c); prefilledMatIdx.Add(activePrefilledColor); }
                break;
            case DrawMode.DisableCell:
                EraseCell(c);
                disabledCells.Add(c);
                break;
        }
    }

    private void EraseCell(Vector3Int c)
    {
        occupiedCells.Remove(c);
        RemoveFromPrefilled(c);
        frozenCells.Remove(c);
        disabledCells.Remove(c);
    }

    private void RemoveFromPrefilled(Vector3Int c)
    {
        int i = prefilledCells.IndexOf(c);
        if (i >= 0) { prefilledCells.RemoveAt(i); prefilledMatIdx.RemoveAt(i); }
    }

    // ── Mini Layer ────────────────────────────────────────────────
    private void DrawMiniLayer(Rect rect, int y)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.10f));
        int W = gridSize.x, D = gridSize.z;
        if (W == 0 || D == 0) return;
        float cpx = Mathf.Min((rect.width - 4) / W, (rect.height - 4) / D);
        float ox = rect.x + (rect.width  - cpx * W) * 0.5f;
        float oy = rect.y + (rect.height - cpx * D) * 0.5f;
        foreach (var cell in occupiedCells)
        {
            if (cell.y != y) continue;
            bool isPf = prefilledCells.Contains(cell);
            bool isIce = frozenCells.Contains(cell);
            Color col = isPf ? COL_PREFILLED : (isIce ? COL_ICE : COL_OCCUPIED);
            EditorGUI.DrawRect(new Rect(ox + cell.x * cpx + 0.5f, oy + cell.z * cpx + 0.5f, cpx - 1, cpx - 1), col);
        }
        if (y == activeLayer) DrawOutline(rect, new Color(0.35f, 0.78f, 1f, 0.8f), 2);
    }

    // ── Sağ Panel ─────────────────────────────────────────────────
    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(230), GUILayout.ExpandHeight(true));
        GUILayout.Label("KAYITLI LEVELLAR", styleHeader);
        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        if (Directory.Exists(LEVELS_PATH))
        {
            foreach (var dir in Directory.GetDirectories(LEVELS_PATH))
            {
                string dname = Path.GetFileName(dir);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(dname, EditorStyles.miniButton, GUILayout.ExpandWidth(true))) TryLoadLevel(dname);
                
                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f, 0.9f); // Yumuşak kırmızı
                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(16)))
                {
                    if (EditorUtility.DisplayDialog("Seviyeyi Sil", $"'{dname}' seviyesini ve içerdiği tüm dosyaları silmek istediğinize emin misiniz?", "Evet", "Hayır"))
                    {
                        string path = $"{LEVELS_PATH}/{dname}";
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                            string metaPath = $"{path}.meta";
                            if (File.Exists(metaPath)) File.Delete(metaPath);
                            AssetDatabase.Refresh();
                        }
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
        }
        else GUILayout.Label("Henüz level yok.", EditorStyles.centeredGreyMiniLabel);

        GUILayout.Space(12);
        GUILayout.Label("ŞEKILDEN YÜKLE", styleHeader);
        if (Directory.Exists(SHAPES_PATH))
        {
            foreach (var f in Directory.GetFiles(SHAPES_PATH, "*.asset"))
            {
                string fname = Path.GetFileNameWithoutExtension(f);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(fname, EditorStyles.miniButton, GUILayout.ExpandWidth(true)))
                    TryLoadShapeAsBase(f.Replace('\\', '/'));
                
                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f, 0.9f); // Yumuşak kırmızı
                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(16)))
                {
                    if (EditorUtility.DisplayDialog("Şekli Sil", $"'{fname}' şekil asset'ini silmek istediğinize emin misiniz?", "Evet", "Hayır"))
                    {
                        string assetPath = f.Replace('\\', '/');
                        AssetDatabase.DeleteAsset(assetPath);
                        AssetDatabase.Refresh();
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
        }
        else GUILayout.Label("Henüz şekil yok.", EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(22));
        EditorGUILayout.LabelField(
            $"Y={activeLayer}  •  {occupiedCells.Count} küp  •  {prefilledCells.Count} prefilled  •  {frozenCells.Count} buz  •  Mod: {ModeLabel()}",
            EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("BlockMerge3D  •  Seviye Tasarımcısı", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private string ModeLabel() => drawMode switch
    {
        DrawMode.Shape     => "Şekil Çiz",
        DrawMode.Prefilled => "Prefilled",
        DrawMode.Ice       => "Buz",
        DrawMode.Erase     => "Sil",
        _                  => "?"
    };

    // ── Fill / Clear ──────────────────────────────────────────────
    private void FillLayer(int y) { for (int x = 0; x < gridSize.x; x++) for (int z = 0; z < gridSize.z; z++) occupiedCells.Add(new Vector3Int(x, y, z)); Repaint(); }
    private void ClearLayer(int y) { var rem = occupiedCells.Where(c => c.y == y).ToList(); foreach (var c in rem) EraseCell(c); Repaint(); }
    private void FillAll()  { for (int y = 0; y < gridSize.y; y++) FillLayer(y); }
    private void ClearAll() { occupiedCells.Clear(); prefilledCells.Clear(); prefilledMatIdx.Clear(); frozenCells.Clear(); disabledCells.Clear(); Repaint(); }

    private void ClearPrefilledInLayer(int y)
    {
        var rem = prefilledCells.Where(c => c.y == y).ToList();
        foreach (var c in rem)
        {
            int idx = prefilledCells.IndexOf(c);
            if (idx >= 0)
            {
                prefilledCells.RemoveAt(idx);
                if (idx < prefilledMatIdx.Count) prefilledMatIdx.RemoveAt(idx);
            }
        }
        Repaint();
    }

    private void ClearShapesInLayer(int y)
    {
        var rem = occupiedCells.Where(c => c.y == y && !prefilledCells.Contains(c)).ToList();
        foreach (var c in rem)
        {
            occupiedCells.Remove(c);
        }
        Repaint();
    }

    private void ClearRowInLayer(int y, int z)
    {
        var rem = occupiedCells.Where(c => c.y == y && c.z == z).ToList();
        foreach (var c in rem) EraseCell(c);
        Repaint();
    }

    private void ClearColumnInLayer(int y, int x)
    {
        var rem = occupiedCells.Where(c => c.y == y && c.x == x).ToList();
        foreach (var c in rem) EraseCell(c);
        Repaint();
    }

    // ── Load ──────────────────────────────────────────────────────
    private void TryLoadShapeAsBase(string assetPath)
    {
        var data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(assetPath);
        if (data == null) return;
        gridSize = data.gridSize; cellSize = data.cellSize; spacing = data.spacing;
        ClearAll();
        foreach (var c in data.occupiedCells) occupiedCells.Add(c);
        for (int i = 0; i < (data.prefilledCells?.Count ?? 0); i++)
        {
            prefilledCells.Add(data.prefilledCells[i]);
            prefilledMatIdx.Add(data.prefilledMaterialIndices != null && i < data.prefilledMaterialIndices.Count ? data.prefilledMaterialIndices[i] : 0);
        }
        
        // Auto-detect disabled cells
        disabledCells.Clear();
        for (int x = 0; x < gridSize.x; x++)
            for (int y = 0; y < gridSize.y; y++)
                for (int z = 0; z < gridSize.z; z++)
                {
                    var cell = new Vector3Int(x, y, z);
                    if (!occupiedCells.Contains(cell)) disabledCells.Add(cell);
                }

        activeLayer = 0; Repaint();
    }

    private void TryLoadLevel(string name)
    {
        var ld = AssetDatabase.LoadAssetAtPath<LevelData>($"{LEVELS_PATH}/{name}/{name}_LevelData.asset");
        if (ld == null) return;
        levelName = ld.levelName; levelTime = ld.timeLimit; levelTarget = ld.targetScore;
        if (ld.mainShapePrefab != null)
        {
            var h = ld.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
            if (h != null)
            {
                gridSize = h.gridSize; cellSize = h.cellSize; spacing = h.spacing;
                ClearAll();
                foreach (var c in h.occupiedCells) occupiedCells.Add(c);
                for (int i = 0; i < (h.prefilledCells?.Count ?? 0); i++)
                {
                    prefilledCells.Add(h.prefilledCells[i]);
                    prefilledMatIdx.Add(h.prefilledMaterialIndices != null && i < h.prefilledMaterialIndices.Count ? h.prefilledMaterialIndices[i] : 0);
                }
                if (h.frozenCells != null)
                {
                    foreach (var c in h.frozenCells) frozenCells.Add(c);
                }
            }
        }

        // Auto-detect disabled cells
        disabledCells.Clear();
        for (int x = 0; x < gridSize.x; x++)
            for (int y = 0; y < gridSize.y; y++)
                for (int z = 0; z < gridSize.z; z++)
                {
                    var cell = new Vector3Int(x, y, z);
                    if (!occupiedCells.Contains(cell)) disabledCells.Add(cell);
                }

        activeLayer = 0; Repaint();
    }

    // ── Export Level ──────────────────────────────────────────────
    private void ExportLevel(bool silent = false)
    {
        if (string.IsNullOrWhiteSpace(levelName)) levelName = "NewLevel";

        string levelDir = $"{LEVELS_PATH}/{levelName}";
        if (!Directory.Exists(levelDir)) Directory.CreateDirectory(levelDir);
        AssetDatabase.Refresh();

        float step = cellSize + spacing;

        // FullShape prefab
        string fullPath = $"{levelDir}/{levelName}_FullShape.prefab";
        GameObject fullRoot = new GameObject($"{levelName}_FullShape");
        var fh = fullRoot.AddComponent<CubeShapeDataHolder>();
        fh.shapeName    = levelName; fh.gridSize = gridSize; fh.cellSize = cellSize; fh.spacing = spacing;
        fh.occupiedCells            = new List<Vector3Int>(occupiedCells);
        fh.prefilledCells           = new List<Vector3Int>(prefilledCells);
        fh.prefilledColors          = prefilledMatIdx.Select(i => PREFILL_COLORS[i % PREFILL_COLORS.Length]).ToList();
        fh.prefilledMaterialIndices = new List<int>(prefilledMatIdx);
        fh.frozenCells              = new List<Vector3Int>(frozenCells);
        foreach (var cell in occupiedCells)
        {
            GameObject cube = cubePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(fullRoot.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
            cube.transform.localScale    = Vector3.one * cellSize;
            cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
        }
        GameObject savedFull = PrefabUtility.SaveAsPrefabAsset(fullRoot, fullPath);
        DestroyImmediate(fullRoot);

        // LevelData
        string ldPath = $"{levelDir}/{levelName}_LevelData.asset";
        LevelData ld = AssetDatabase.LoadAssetAtPath<LevelData>(ldPath);
        bool isNew = ld == null;
        if (isNew) ld = ScriptableObject.CreateInstance<LevelData>();
        ld.levelName        = levelName;
        ld.mainShapePrefab  = savedFull;
        ld.timeLimit        = levelTime;
        ld.targetScore      = levelTarget;
        // complementaryPieces dokunulmaz (Piece Designer'ın işi)
        if (ld.complementaryPieces == null) ld.complementaryPieces = new List<GameObject>();

        if (isNew) AssetDatabase.CreateAsset(ld, ldPath);
        else       EditorUtility.SetDirty(ld);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();

        if (silent)
        {
            this.ShowNotification(new GUIContent("💾 Seviye Kaydedildi!"));
        }
        else
        {
            EditorUtility.DisplayDialog("Export Tamamlandı!",
                $"✅  FullShape prefab\n✅  LevelData asset\n\n{levelDir}/\n\nParçaları atamak için\n🧩 Piece Designer'ı kullan.", "Tamam");
        }
    }

    // ── Yardımcılar ───────────────────────────────────────────────
    private static void DrawOutline(Rect r, Color c, float t)
    {
        EditorGUI.DrawRect(new Rect(r.x,        r.y,        r.width, t),  c);
        EditorGUI.DrawRect(new Rect(r.x,        r.yMax - t, r.width, t),  c);
        EditorGUI.DrawRect(new Rect(r.x,        r.y,        t, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.y,        t, r.height), c);
    }

    private void BuildStyles()
    {
        if (stylesBuilt) return;
        styleHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        styleHeader.normal.textColor = COL_HEADER;
        styleBox     = new GUIStyle(GUI.skin.box);
        styleModeBtn = new GUIStyle(GUI.skin.button) { fontSize = 11 };
        stylesBuilt  = true;
    }
}
