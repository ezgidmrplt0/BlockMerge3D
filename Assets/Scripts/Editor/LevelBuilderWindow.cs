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
    private enum DrawMode { Shape, Prefilled, Erase }

    // ── Sabitler ─────────────────────────────────────────────────
    private const string LEVELS_PATH = "Assets/Levels";
    private const string SHAPES_PATH = "Assets/Shapes";
    private const float  MIN_CELL_PX = 18f;
    private const float  MAX_CELL_PX = 60f;

    private static readonly Color COL_BG         = new Color(0.10f, 0.10f, 0.13f);
    private static readonly Color COL_GRID        = new Color(0.22f, 0.22f, 0.28f);
    private static readonly Color COL_OCCUPIED    = new Color(0.40f, 0.44f, 0.52f);
    private static readonly Color COL_PREFILLED   = new Color(0.85f, 0.70f, 0.20f);
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

    // ── UI Durumu ─────────────────────────────────────────────────
    private int      activeLayer      = 0;
    private DrawMode drawMode         = DrawMode.Shape;
    private int      activePrefilledColor = 0;
    private float    cellPx           = 32f;
    private bool     showGhostLayers  = true;
    private Vector2  leftScroll, rightScroll;
    private Vector2? hoverCell;

    private GameObject cubePrefab;

    // ── Stil ─────────────────────────────────────────────────────
    private GUIStyle styleHeader, styleBox, styleModeBtn;
    private bool     stylesBuilt;

    // ─────────────────────────────────────────────────────────────
    [MenuItem("BlockMerge3D/🗂  Level Builder")]
    public static void Open()
    {
        var w = GetWindow<LevelBuilderWindow>("Level Builder");
        w.minSize = new Vector2(900, 560);
    }

    private void OnGUI()
    {
        BuildStyles();
        DrawToolbar();
        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel();
        DrawCenterGrid();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();
        DrawStatusBar();
    }

    // ── Toolbar ───────────────────────────────────────────────────
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(30));

        GUILayout.Label("Level:", EditorStyles.toolbarButton, GUILayout.Width(42));
        levelName = EditorGUILayout.TextField(levelName, EditorStyles.toolbarTextField, GUILayout.Width(140));

        GUILayout.Space(10);

        GUILayout.Label("Katman:", EditorStyles.toolbarButton, GUILayout.Width(52));
        GUI.enabled = activeLayer > 0;
        if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(22))) { activeLayer--; Repaint(); }
        GUI.enabled = true;

        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 0.85f);
        GUILayout.Label($"  Y = {activeLayer}  ", EditorStyles.toolbarButton, GUILayout.Width(56));
        GUI.backgroundColor = Color.white;

        GUI.enabled = activeLayer < gridSize.y - 1;
        if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(22))) { activeLayer++; Repaint(); }
        GUI.enabled = true;

        GUILayout.Space(10);
        showGhostLayers = GUILayout.Toggle(showGhostLayers, "Diğer Katmanlar", EditorStyles.toolbarButton);

        GUILayout.FlexibleSpace();

        GUILayout.Label("Zoom:", EditorStyles.toolbarButton, GUILayout.Width(42));
        cellPx = EditorGUILayout.Slider(cellPx, MIN_CELL_PX, MAX_CELL_PX, GUILayout.Width(110));

        GUILayout.Space(10);

        GUI.backgroundColor = new Color(0.35f, 1f, 0.45f, 0.9f);
        GUI.enabled = occupiedCells.Count > 0;
        if (GUILayout.Button("⬆  Export Level", EditorStyles.toolbarButton, GUILayout.Width(120))) ExportLevel();
        GUI.enabled = true;
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
    }

    // ── Sol Panel ─────────────────────────────────────────────────
    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(280), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        // Level Ayarları
        GUILayout.Label("LEVEL AYARLARI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
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
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // Katman Listesi
        GUILayout.Label("KATMANLAR", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        for (int y = 0; y < gridSize.y; y++)
        {
            bool isActive = (y == activeLayer);
            int  count    = occupiedCells.Count(c => c.y == y);
            GUI.backgroundColor = isActive ? new Color(0.3f, 0.7f, 1f, 0.8f) : Color.white;
            if (GUILayout.Button($"Y={y}  ({count} küp)", isActive ? EditorStyles.boldLabel : EditorStyles.label))
                { activeLayer = y; Repaint(); }
            GUI.backgroundColor = Color.white;
            Rect mini = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(22), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) DrawMiniLayer(mini, y);
            GUILayout.Space(2);
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
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ── Merkez Grid ───────────────────────────────────────────────
    private void DrawCenterGrid()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // Mod butonları
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        DrawModeBtn("✏  Şekil Çiz",   DrawMode.Shape);
        DrawModeBtn("⬛  Prefilled",   DrawMode.Prefilled);
        DrawModeBtn("✕  Sil",          DrawMode.Erase);

        if (drawMode == DrawMode.Prefilled)
        {
            GUILayout.Space(8);
            GUILayout.Label("Renk:", EditorStyles.miniLabel);
            for (int i = 0; i < PREFILL_COLORS.Length; i++)
            {
                GUI.backgroundColor = PREFILL_COLORS[i];
                var s = new GUIStyle(GUI.skin.button) { fixedWidth = 22, fixedHeight = 22 };
                if (GUILayout.Button("", s)) activePrefilledColor = i;
                if (i == activePrefilledColor)
                {
                    Rect sel = GUILayoutUtility.GetLastRect();
                    EditorGUI.DrawRect(new Rect(sel.x, sel.yMax - 3, sel.width, 3), Color.white);
                }
                GUI.backgroundColor = Color.white;
            }
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label($"Grid {gridSize.x}×{gridSize.z}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        HandleGridInput(area);
        DrawGrid2D(area);

        EditorGUILayout.EndVertical();
    }

    private void DrawModeBtn(string label, DrawMode mode)
    {
        GUI.backgroundColor = drawMode == mode ? new Color(0.3f, 0.8f, 1f) : Color.white;
        if (GUILayout.Button(label, styleModeBtn, GUILayout.Height(26))) drawMode = mode;
        GUI.backgroundColor = Color.white;
    }

    private void DrawGrid2D(Rect area)
    {
        if (Event.current.type != EventType.Repaint) return;
        int W = gridSize.x, D = gridSize.z;
        float tw = cellPx * W, th = cellPx * D;
        float ox = area.x + (area.width  - tw) * 0.5f;
        float oy = area.y + (area.height - th) * 0.5f;

        EditorGUI.DrawRect(area, COL_BG);

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

        // Grid çizgileri
        for (int x = 0; x <= W; x++)
            EditorGUI.DrawRect(new Rect(ox + x * cellPx, oy, 1, th), COL_GRID);
        for (int z = 0; z <= D; z++)
            EditorGUI.DrawRect(new Rect(ox, oy + z * cellPx, tw, 1), COL_GRID);

        // Hücreler
        foreach (var cell in occupiedCells)
        {
            if (cell.y != activeLayer) continue;
            bool isPf = prefilledCells.Contains(cell);
            Color col = isPf ? COL_PREFILLED : COL_OCCUPIED;
            if (isPf)
            {
                int idx = prefilledCells.IndexOf(cell);
                if (idx >= 0 && idx < prefilledMatIdx.Count)
                    col = PREFILL_COLORS[prefilledMatIdx[idx] % PREFILL_COLORS.Length];
            }
            EditorGUI.DrawRect(new Rect(ox + cell.x * cellPx + 1.5f, oy + cell.z * cellPx + 1.5f, cellPx - 3, cellPx - 3), col);
        }

        // Hover
        if (hoverCell.HasValue)
        {
            int hx = Mathf.RoundToInt(hoverCell.Value.x);
            int hz = Mathf.RoundToInt(hoverCell.Value.y);
            if (hx >= 0 && hx < W && hz >= 0 && hz < D)
            {
                bool exists = occupiedCells.Contains(new Vector3Int(hx, activeLayer, hz));
                Color hc = (drawMode == DrawMode.Erase && exists) ? COL_HOVER_ERASE : COL_HOVER_ADD;
                EditorGUI.DrawRect(new Rect(ox + hx * cellPx + 1, oy + hz * cellPx + 1, cellPx - 2, cellPx - 2), hc);
            }
        }

        // Eksen
        var lbl = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = new Color(1,1,1,0.35f) } };
        for (int x = 0; x < W; x++) GUI.Label(new Rect(ox + x * cellPx, oy - 14, cellPx, 14), x.ToString(), lbl);
        for (int z = 0; z < D; z++) GUI.Label(new Rect(ox - 18, oy + z * cellPx, 18, cellPx), z.ToString(), lbl);
        GUI.Label(new Rect(ox - 18, oy - 14, 18, 14), "Z\\X", lbl);
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

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && inside && hoverCell.HasValue)
        {
            ApplyBrush(new Vector3Int(Mathf.RoundToInt(hoverCell.Value.x), activeLayer, Mathf.RoundToInt(hoverCell.Value.y)));
            e.Use(); Repaint();
        }
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 1 && inside && hoverCell.HasValue)
        {
            EraseCell(new Vector3Int(Mathf.RoundToInt(hoverCell.Value.x), activeLayer, Mathf.RoundToInt(hoverCell.Value.y)));
            e.Use(); Repaint();
        }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) Repaint();
    }

    private void ApplyBrush(Vector3Int c)
    {
        switch (drawMode)
        {
            case DrawMode.Shape:
                occupiedCells.Add(c);
                // Eğer prefilled listesindeyse çıkar (normal şekle döndür)
                RemoveFromPrefilled(c);
                break;
            case DrawMode.Erase:
                EraseCell(c);
                break;
            case DrawMode.Prefilled:
                occupiedCells.Add(c);
                int existing = prefilledCells.IndexOf(c);
                if (existing >= 0) prefilledMatIdx[existing] = activePrefilledColor;
                else { prefilledCells.Add(c); prefilledMatIdx.Add(activePrefilledColor); }
                break;
        }
    }

    private void EraseCell(Vector3Int c)
    {
        occupiedCells.Remove(c);
        RemoveFromPrefilled(c);
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
            Color col = isPf ? COL_PREFILLED : COL_OCCUPIED;
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
                if (GUILayout.Button(dname, EditorStyles.miniButton)) TryLoadLevel(dname);
                EditorGUILayout.EndHorizontal();
            }
        }
        else GUILayout.Label("Henüz level yok.", EditorStyles.centeredGreyMiniLabel);

        GUILayout.Space(12);
        GUILayout.Label("ŞEKILDEN YÜKLE", styleHeader);
        if (Directory.Exists(SHAPES_PATH))
        {
            foreach (var f in Directory.GetFiles(SHAPES_PATH, "*.asset"))
                if (GUILayout.Button(Path.GetFileNameWithoutExtension(f), EditorStyles.miniButton))
                    TryLoadShapeAsBase(f.Replace('\\', '/'));
        }
        else GUILayout.Label("Henüz şekil yok.", EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ── Status Bar ────────────────────────────────────────────────
    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(22));
        EditorGUILayout.LabelField(
            $"Y={activeLayer}  •  {occupiedCells.Count} küp  •  {prefilledCells.Count} prefilled  •  Mod: {ModeLabel()}",
            EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("BlockMerge3D  •  Level Builder", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private string ModeLabel() => drawMode switch
    {
        DrawMode.Shape     => "Şekil Çiz",
        DrawMode.Prefilled => "Prefilled",
        DrawMode.Erase     => "Sil",
        _                  => "?"
    };

    // ── Fill / Clear ──────────────────────────────────────────────
    private void FillLayer(int y) { for (int x = 0; x < gridSize.x; x++) for (int z = 0; z < gridSize.z; z++) occupiedCells.Add(new Vector3Int(x, y, z)); Repaint(); }
    private void ClearLayer(int y) { var rem = occupiedCells.Where(c => c.y == y).ToList(); foreach (var c in rem) EraseCell(c); Repaint(); }
    private void FillAll()  { for (int y = 0; y < gridSize.y; y++) FillLayer(y); }
    private void ClearAll() { occupiedCells.Clear(); prefilledCells.Clear(); prefilledMatIdx.Clear(); Repaint(); }

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
            }
        }
        activeLayer = 0; Repaint();
    }

    // ── Export Level ──────────────────────────────────────────────
    private void ExportLevel()
    {
        if (occupiedCells.Count == 0) { EditorUtility.DisplayDialog("Hata", "Hiç küp yok!", "Tamam"); return; }
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

        EditorUtility.DisplayDialog("Export Tamamlandı!",
            $"✅  FullShape prefab\n✅  LevelData asset\n\n{levelDir}/\n\nParçaları atamak için\n🧩 Piece Designer'ı kullan.", "Tamam");
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
