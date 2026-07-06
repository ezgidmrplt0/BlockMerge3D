using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  PIECE DESIGNER  —  Level Parçalama Editörü
//  BlockMerge3D  •  BlockMerge3D / 🧩 Piece Designer
//
//  İş akışı:
//    1. LevelData seç  →  Ana şekil 2D grid'e yüklenir
//    2. Aktif parçayı sol panelden seç
//    3. Hücreleri fareyle boya  →  Sol tık ata, Sağ tık çıkar
//    4. "Parçaları Kaydet" ile LevelData güncellenir
// ═══════════════════════════════════════════════════════════════════
public class PieceDesignerWindow : EditorWindow
{
    // ── Sabitler ─────────────────────────────────────────────────
    private const string LEVELS_PATH = "Assets/Levels";
    private const float  MIN_CELL_PX = 18f;
    private const float  MAX_CELL_PX = 66f;

    private static readonly Color COL_BG           = new Color(0.10f, 0.10f, 0.13f);
    private static readonly Color COL_GRID          = new Color(0.20f, 0.20f, 0.26f);
    private static readonly Color COL_SHAPE_EMPTY   = new Color(0.22f, 0.24f, 0.30f); // şekil içi, atanmamış
    private static readonly Color COL_SHAPE_BORDER  = new Color(0.34f, 0.36f, 0.44f);
    private static readonly Color COL_UNASSIGNED    = new Color(0.28f, 0.30f, 0.38f);
    private static readonly Color COL_HOVER_ASSIGN  = new Color(1.00f, 1.00f, 1.00f, 0.18f);
    private static readonly Color COL_HOVER_REMOVE  = new Color(1.00f, 0.28f, 0.20f, 0.45f);
    private static readonly Color COL_GHOST         = new Color(0.24f, 0.26f, 0.32f, 0.28f);
    private static readonly Color COL_HEADER        = new Color(0.95f, 0.65f, 0.25f);
    private static readonly Color COL_HEADER_DARK   = new Color(0.75f, 0.48f, 0.15f);

    private static readonly Color[] PIECE_COLORS = new Color[]
    {
        new Color(0.95f, 0.30f, 0.30f),
        new Color(0.28f, 0.65f, 0.95f),
        new Color(0.25f, 0.88f, 0.42f),
        new Color(0.95f, 0.80f, 0.15f),
        new Color(0.72f, 0.22f, 0.92f),
        new Color(0.95f, 0.55f, 0.15f),
        new Color(0.20f, 0.88f, 0.88f),
        new Color(0.95f, 0.40f, 0.70f),
    };

    // ── Yüklü Level ───────────────────────────────────────────────
    private LevelData    loadedLevel;
    private string       loadedLevelPath = "";
    private string       levelName       = "";

    // Ana şeklin hücreleri (read-only — sadece bunlar boyanabilir)
    private HashSet<Vector3Int> shapeCells = new HashSet<Vector3Int>();
    private Vector3Int          shapeGridSize;
    private float               shapeCellSize = 1f;
    private float               shapeSpacing  = 0.1f;
    private GameObject          shapeCubePrefab;

    // ── Parça Ataması ─────────────────────────────────────────────
    private List<HashSet<Vector3Int>> pieceCells = new List<HashSet<Vector3Int>>();
    private int pieceCount  = 2;
    private int activePiece = 0;

    // ── UI Durumu ─────────────────────────────────────────────────
    private int      activeLayer     = 0;
    private bool     eraseMode       = false;
    private float    cellPx          = 34f;
    private bool     showGhostLayers = true;
    private Vector2  leftScroll, rightScroll;
    private Vector2? hoverCell;
    private bool     levelLoaded     = false;
    private bool     isLeftMouseDown  = false;
    private bool     isRightMouseDown = false;

    // ── Stil ─────────────────────────────────────────────────────
    private GUIStyle styleHeader, styleBox, styleWarn;
    private bool     stylesBuilt;

    // Ertelenmiş level yükleme (GUILayout state bozulmasını önler)
    private LevelData pendingLevelToLoad = null;

    public System.Action onRepaintRequested;

    new public void Repaint()
    {
        base.Repaint();
        if (onRepaintRequested != null)
            onRepaintRequested();
    }

    // [MenuItem("BlockMerge3D/🧩  Piece Designer")]
    // public static void Open()
    // {
    //     var w = GetWindow<PieceDesignerWindow>("Piece Designer");
    //     w.minSize = new Vector2(860, 540);
    // }

    // ══ OnGUI ══════════════════════════════════════════════════
    public void OnGUI()
    {
        // Handle keyboard shortcuts for switching layers
        Event e = Event.current;
        if (levelLoaded && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.PageUp)
            {
                if (activeLayer < shapeGridSize.y - 1)
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
                if (EditorUtility.DisplayDialog("Katmanı Temizle", $"Y={activeLayer} katmanındaki tüm parça atamalarını temizlemek istiyor musunuz?", "Evet", "Hayır"))
                {
                    ClearLayer(activeLayer);
                    e.Use();
                }
            }
        }

        // Layout pass başlamadan önce bekleyen level yükleme varsa işle
        if (pendingLevelToLoad != null && Event.current.type == EventType.Layout)
        {
            var toLoad = pendingLevelToLoad;
            pendingLevelToLoad = null;
            LoadLevel(toLoad);
            return; // Bu frame'i iptal et, bir sonraki frame temiz gelir
        }

        BuildStyles();
        DrawToolbar();
        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel();
        if (levelLoaded) DrawCenterGrid();
        else             DrawEmptyCenter();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();
        DrawStatusBar();
    }

    // ── Toolbar ───────────────────────────────────────────────────
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(30));

        // LevelData seçici
        GUILayout.Label("Level:", EditorStyles.toolbarButton, GUILayout.Width(42));
        EditorGUI.BeginChangeCheck();
        var newLevel = (LevelData)EditorGUILayout.ObjectField(loadedLevel, typeof(LevelData), false, GUILayout.Width(180));
        if (EditorGUI.EndChangeCheck() && newLevel != null && newLevel != loadedLevel)
            pendingLevelToLoad = newLevel; // Ertelenmiş yükleme
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        if (levelLoaded)
        {
            // Katman
            GUILayout.Label("Katman:", EditorStyles.toolbarButton, GUILayout.Width(52));
            GUI.enabled = activeLayer > 0;
            if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(22))) { activeLayer--; Repaint(); }
            GUI.enabled = true;

            GUI.backgroundColor = new Color(1f, 0.65f, 0.25f, 0.85f);
            GUILayout.Label($"  Y = {activeLayer}  ", EditorStyles.toolbarButton, GUILayout.Width(56));
            GUI.backgroundColor = Color.white;

            GUI.enabled = activeLayer < shapeGridSize.y - 1;
            if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(22))) { activeLayer++; Repaint(); }
            GUI.enabled = true;

            GUILayout.Space(8);

            // Mod
            GUI.backgroundColor = eraseMode ? new Color(1f, 0.38f, 0.28f) : new Color(0.28f, 0.92f, 0.52f);
            if (GUILayout.Button(eraseMode ? "✕  Çıkar" : "✏  Ata", EditorStyles.toolbarButton, GUILayout.Width(80)))
                eraseMode = !eraseMode;
            GUI.backgroundColor = Color.white;

            showGhostLayers = GUILayout.Toggle(showGhostLayers, "Diğer Katmanlar", EditorStyles.toolbarButton);
        }

        GUILayout.FlexibleSpace();

        if (levelLoaded)
        {
            GUILayout.Label("Zoom:", EditorStyles.toolbarButton, GUILayout.Width(42));
            cellPx = EditorGUILayout.Slider(cellPx, MIN_CELL_PX, MAX_CELL_PX, GUILayout.Width(110));
            GUILayout.Space(8);

            bool canSave = pieceCells.Count > 0 && pieceCells.All(s => s.Count > 0);
            GUI.backgroundColor = canSave ? new Color(0.95f, 0.65f, 0.25f, 0.9f) : Color.white;
            GUI.enabled = canSave;
            if (GUILayout.Button("💾  Parçaları Kaydet", EditorStyles.toolbarButton, GUILayout.Width(150)))
                SavePieces();
            GUI.enabled = true;
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.EndHorizontal();
    }

    // ── Sol Panel ─────────────────────────────────────────────────
    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(270), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        if (!levelLoaded)
        {
            GUILayout.Space(20);
            EditorGUILayout.HelpBox("Yukarıdan bir LevelData seç ya da sağ panelden tıkla.", MessageType.Info);
        }
        else
        {
            // Parça Listesi
            GUILayout.Label("PARÇALAR", styleHeader);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EnsurePieceLists();
            for (int i = 0; i < pieceCount; i++)
            {
                Color pc  = PIECE_COLORS[i % PIECE_COLORS.Length];
                int   cnt = pieceCells[i].Count;
                bool  isActive = (activePiece == i);

                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = isActive ? pc : pc * 0.50f;
                var btn = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal };
                if (GUILayout.Button($"  Parça {i + 1}  [{cnt} küp]", btn))
                {
                    activePiece = i;
                    eraseMode   = false;
                }
                GUI.backgroundColor = Color.white;
                if (pieceCount > 2 && GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    if (pieceCells.Count > i) pieceCells.RemoveAt(i);
                    pieceCount--;
                    activePiece = Mathf.Clamp(activePiece, 0, pieceCount - 1);
                    break;
                }
                EditorGUILayout.EndHorizontal();

                // Mini 2D önizleme (tüm katmanlar süper üst üste)
                Rect mini = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(36), GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint) DrawMiniPiece(mini, i);
                GUILayout.Space(2);
            }

            GUI.backgroundColor = new Color(1f, 0.65f, 0.25f, 0.7f);
            if (GUILayout.Button("+ Parça Ekle")) { pieceCount++; EnsurePieceLists(); activePiece = pieceCount - 1; eraseMode = false; }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            // Katman listesi
            GUILayout.Label("KATMANLAR", styleHeader);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            for (int y = 0; y < shapeGridSize.y; y++)
            {
                bool isActive = (y == activeLayer);
                int  inLayer  = shapeCells.Count(c => c.y == y);
                int  assigned = pieceCells.Sum(s => s.Count(c => c.y == y));

                GUI.backgroundColor = isActive ? new Color(1f, 0.65f, 0.25f, 0.8f) : Color.white;
                if (GUILayout.Button($"Y={y}  {assigned}/{inLayer} atanmış", isActive ? EditorStyles.boldLabel : EditorStyles.label))
                    { activeLayer = y; Repaint(); }
                GUI.backgroundColor = Color.white;

                Rect mini = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(24), GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint) DrawMiniLayer(mini, y);
                if (Event.current.type == EventType.MouseDown && mini.Contains(Event.current.mousePosition))
                {
                    activeLayer = y;
                    Repaint();
                    Event.current.Use();
                }
                GUILayout.Space(2);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            // Aksiyonlar
            GUILayout.Label("AKSİYONLAR", styleHeader);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button($"Katmanı Ata (P{activePiece + 1})"))   AssignLayerToPiece(activeLayer, activePiece);
            if (GUILayout.Button("Katmanı Temizle")) ClearLayer(activeLayer);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button($"Tümünü Ata (P{activePiece + 1})"))    AssignAllToPiece(activePiece);
            if (GUILayout.Button("Tümünü Temizle")) ClearAll();
            EditorGUILayout.EndHorizontal();

            // Özet: atanmamış hücreler
            int totalAssigned = pieceCells.Sum(s => s.Count);
            int unassigned = shapeCells.Count - totalAssigned;
            if (unassigned > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox($"⚠ {unassigned} küp henüz atanmamış!", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ── Boş Merkez ───────────────────────────────────────────────
    private void DrawEmptyCenter()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginVertical();
        var big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
        big.normal.textColor = new Color(1f, 0.65f, 0.25f, 0.5f);
        GUILayout.Label("🧩", big);
        var sub = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 13 };
        GUILayout.Label("Sağ panelden bir LevelData seç", sub);
        GUILayout.Label("ya da toolbar'daki ObjectField'ı kullan.", sub);
        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();
    }

    // ── Merkez 2D Grid ───────────────────────────────────────────
    private void DrawCenterGrid()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // Başlık şeridi
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        Color pc = PIECE_COLORS[activePiece % PIECE_COLORS.Length];
        GUI.backgroundColor = pc * 0.7f;
        string modeStr = eraseMode ? "✕  Çıkar Modu" : $"✏  Parça {activePiece + 1} Ata";
        GUILayout.Label($"  Y = {activeLayer}   •   {modeStr}",
            new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } });
        GUI.backgroundColor = Color.white;
        GUILayout.FlexibleSpace();
        GUILayout.Label($"Şekil: {shapeGridSize.x}×{shapeGridSize.z}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        HandleGridInput(area);
        DrawGrid2D(area);

        EditorGUILayout.EndVertical();
    }

    private void DrawGrid2D(Rect area)
    {
        if (Event.current.type != EventType.Repaint) return;
        int W = shapeGridSize.x, D = shapeGridSize.z;
        float tw = cellPx * W, th = cellPx * D;
        float ox = area.x + (area.width  - tw) * 0.5f;
        float oy = area.y + (area.height - th) * 0.5f;

        EditorGUI.DrawRect(area, COL_BG);

        // Ghost katmanlar
        if (showGhostLayers)
        {
            foreach (var cell in shapeCells)
            {
                if (cell.y == activeLayer) continue;
                float a = Mathf.Clamp(1f - Mathf.Abs(cell.y - activeLayer) * 0.28f, 0.04f, 0.24f);
                EditorGUI.DrawRect(new Rect(ox + cell.x * cellPx + 1, oy + cell.z * cellPx + 1, cellPx - 2, cellPx - 2),
                    new Color(COL_GHOST.r, COL_GHOST.g, COL_GHOST.b, a));
            }
        }

        // Grid çizgileri (sadece şekil alanı içinde)
        for (int x = 0; x <= W; x++)
            EditorGUI.DrawRect(new Rect(ox + x * cellPx, oy, 1, th), COL_GRID);
        for (int z = 0; z <= D; z++)
            EditorGUI.DrawRect(new Rect(ox, oy + z * cellPx, tw, 1), COL_GRID);

        // Şekil hücreleri
        // Kural: sadece aktif parçanın hücreleri renkli gösterilir.
        // Diğer parçalara atanmış hücreler → base (unassigned) renk.
        // Böylece parçalar birbirini "görsel olarak" silmez.
        Color activePieceColor = PIECE_COLORS[activePiece % PIECE_COLORS.Length];
        foreach (var cell in shapeCells)
        {
            if (cell.y != activeLayer) continue;
            float cx = ox + cell.x * cellPx + 1.5f;
            float cz = oy + cell.z * cellPx + 1.5f;

            bool inActive = activePiece < pieceCells.Count && pieceCells[activePiece].Contains(cell);
            Color fill = inActive ? activePieceColor * 0.82f : COL_UNASSIGNED;
            EditorGUI.DrawRect(new Rect(cx, cz, cellPx - 3, cellPx - 3), fill);

            // Parça numarası sadece aktif parçada göster
            if (cellPx >= 28 && inActive)
            {
                var numStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    fontSize   = Mathf.Clamp(Mathf.RoundToInt(cellPx * 0.38f), 9, 18),
                    fontStyle  = FontStyle.Bold,
                };
                numStyle.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
                GUI.Label(new Rect(cx, cz, cellPx - 3, cellPx - 3), (activePiece + 1).ToString(), numStyle);
            }
        }

        // Hover
        if (hoverCell.HasValue)
        {
            int hx = Mathf.RoundToInt(hoverCell.Value.x);
            int hz = Mathf.RoundToInt(hoverCell.Value.y);
            var hCoord = new Vector3Int(hx, activeLayer, hz);
            if (shapeCells.Contains(hCoord))
            {
                bool assigned = GetPieceIndex(hCoord) >= 0;
                Color hc = (eraseMode && assigned) ? COL_HOVER_REMOVE : COL_HOVER_ASSIGN;
                EditorGUI.DrawRect(new Rect(ox + hx * cellPx + 1, oy + hz * cellPx + 1, cellPx - 2, cellPx - 2), hc);
            }
        }

        // Eksen etiketleri
        var lbl = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = new Color(1,1,1,0.32f) } };
        for (int x = 0; x < W; x++) GUI.Label(new Rect(ox + x * cellPx, oy - 14, cellPx, 14), x.ToString(), lbl);
        for (int z = 0; z < D; z++) GUI.Label(new Rect(ox - 18, oy + z * cellPx, 18, cellPx), z.ToString(), lbl);
        GUI.Label(new Rect(ox - 18, oy - 14, 18, 14), "Z\\X", lbl);
    }

    // ── Grid Input ────────────────────────────────────────────────
    private void HandleGridInput(Rect area)
    {
        Event e = Event.current;
        int W = shapeGridSize.x, D = shapeGridSize.z;
        float tw = cellPx * W, th = cellPx * D;
        float ox = area.x + (area.width  - tw) * 0.5f;
        float oy = area.y + (area.height - th) * 0.5f;

        bool inside = area.Contains(e.mousePosition);

        if (inside)
        {
            int gx = Mathf.FloorToInt((e.mousePosition.x - ox) / cellPx);
            int gz = Mathf.FloorToInt((e.mousePosition.y - oy) / cellPx);
            var candidate = new Vector3Int(gx, activeLayer, gz);
            hoverCell = (gx >= 0 && gx < W && gz >= 0 && gz < D && shapeCells.Contains(candidate))
                ? new Vector2(gx, gz) : (Vector2?)null;
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
            var coord = new Vector3Int(Mathf.RoundToInt(hoverCell.Value.x), activeLayer, Mathf.RoundToInt(hoverCell.Value.y));
            if (shapeCells.Contains(coord))
            {
                if (isLeftMouseDown)
                {
                    if (eraseMode) UnassignCell(coord);
                    else           AssignCell(coord, activePiece);
                    if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) e.Use();
                    Repaint();
                }
                else if (isRightMouseDown)
                {
                    UnassignCell(coord);
                    if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) e.Use();
                    Repaint();
                }
            }
        }
        if (e.type == EventType.ScrollWheel && inside)
        {
            if (e.delta.y < 0)
            {
                if (activeLayer < shapeGridSize.y - 1)
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

    private void AssignCell(Vector3Int c, int piece)
    {
        EnsurePieceLists();
        // Diğer parçalara dokunma — her parça kendi verisini bağımsız tutar.
        // Aynı hücre birden fazla parçada olabilir (görsel olarak aktif parça önceliklidir).
        pieceCells[piece].Add(c);
    }

    private void UnassignCell(Vector3Int c)
    {
        foreach (var s in pieceCells) s.Remove(c);
    }

    private int GetPieceIndex(Vector3Int c)
    {
        for (int i = 0; i < pieceCells.Count; i++)
            if (pieceCells[i].Contains(c)) return i;
        return -1;
    }

    // ── Mini Çizimler ─────────────────────────────────────────────
    private void DrawMiniLayer(Rect rect, int y)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.10f));
        int W = shapeGridSize.x, D = shapeGridSize.z;
        if (W == 0 || D == 0) return;
        float cpx = Mathf.Min((rect.width - 4) / W, (rect.height - 4) / D);
        float ox = rect.x + (rect.width  - cpx * W) * 0.5f;
        float oy = rect.y + (rect.height - cpx * D) * 0.5f;
        foreach (var cell in shapeCells)
        {
            if (cell.y != y) continue;
            int pi = GetPieceIndex(cell);
            Color col = pi >= 0 ? PIECE_COLORS[pi % PIECE_COLORS.Length] : COL_UNASSIGNED;
            EditorGUI.DrawRect(new Rect(ox + cell.x * cpx + 0.5f, oy + cell.z * cpx + 0.5f, cpx - 1, cpx - 1), col);
        }
        if (y == activeLayer) DrawOutline(rect, new Color(1f, 0.65f, 0.25f, 0.8f), 2);
    }

    private void DrawMiniPiece(Rect rect, int pieceIdx)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.10f));
        if (shapeCells.Count == 0) return;
        int W = shapeGridSize.x, D = shapeGridSize.z;
        float cpx = Mathf.Min((rect.width - 4) / W, (rect.height - 4) / D);
        float ox = rect.x + (rect.width  - cpx * W) * 0.5f;
        float oy = rect.y + (rect.height - cpx * D) * 0.5f;
        Color pc = PIECE_COLORS[pieceIdx % PIECE_COLORS.Length] * 0.75f;
        Color bg = new Color(0.18f, 0.19f, 0.23f);
        foreach (var cell in shapeCells)
        {
            bool assigned = pieceIdx < pieceCells.Count && pieceCells[pieceIdx].Contains(cell);
            EditorGUI.DrawRect(new Rect(ox + cell.x * cpx + 0.5f, oy + cell.z * cpx + 0.5f, cpx - 1, cpx - 1), assigned ? pc : bg);
        }
        DrawOutline(rect, PIECE_COLORS[pieceIdx % PIECE_COLORS.Length] * 0.6f, 1.5f);
    }

    // ── Sağ Panel — Level Listesi ─────────────────────────────────
    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(230), GUILayout.ExpandHeight(true));
        GUILayout.Label("LEVELLAR", styleHeader);
        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        if (Directory.Exists(LEVELS_PATH))
        {
            foreach (var dir in Directory.GetDirectories(LEVELS_PATH))
            {
                string dname = Path.GetFileName(dir);
                string ldPath = $"{LEVELS_PATH}/{dname}/{dname}_LevelData.asset";
                bool isLoaded = (levelLoaded && levelName == dname);

                GUI.backgroundColor = isLoaded ? new Color(1f, 0.65f, 0.25f, 0.8f) : Color.white;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(isLoaded ? $"● {dname}" : dname, isLoaded ? EditorStyles.boldLabel : EditorStyles.label))
                {
                    var ld = AssetDatabase.LoadAssetAtPath<LevelData>(ldPath);
                    if (ld != null) pendingLevelToLoad = ld; // Ertelenmiş yükleme
                    else EditorUtility.DisplayDialog("Hata", $"{ldPath} bulunamadı.", "Tamam");
                }
                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = Color.white;
                GUILayout.Space(2);
            }
        }
        else GUILayout.Label("Henüz level yok.\nÖnce Level Builder ile\nbir level oluştur.", EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.EndScrollView();

        // Kayıtlı parça durumu
        if (levelLoaded)
        {
            GUILayout.Space(8);
            GUILayout.Label("KAYITLI PARÇALAR", styleHeader);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            int existing = loadedLevel?.complementaryPieces?.Count ?? 0;
            if (existing == 0)
                GUILayout.Label("Henüz parça yok.", EditorStyles.centeredGreyMiniLabel);
            else
            {
                for (int i = 0; i < existing; i++)
                {
                    var gobj = loadedLevel.complementaryPieces[i];
                    Color pc = PIECE_COLORS[i % PIECE_COLORS.Length];
                    var s = new GUIStyle(EditorStyles.miniLabel);
                    s.normal.textColor = pc;
                    GUILayout.Label($"  Parça {i + 1}: {(gobj != null ? gobj.name : "null")}", s);
                }
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndVertical();
    }

    // ── Status Bar ────────────────────────────────────────────────
    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(22));
        if (levelLoaded)
        {
            int totalAssigned = pieceCells.Sum(s => s.Count);
            int unassigned    = shapeCells.Count - totalAssigned;
            EditorGUILayout.LabelField(
                $"Level: {levelName}  •  Y={activeLayer}  •  {shapeCells.Count} küp  •  {totalAssigned} atanmış  •  {unassigned} atanmamış",
                EditorStyles.miniLabel);
        }
        else EditorGUILayout.LabelField("Parçalamak için bir LevelData yükle.", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("BlockMerge3D  •  Piece Designer", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ── Level Yükle ───────────────────────────────────────────────
    private void LoadLevel(LevelData ld)
    {
        loadedLevel     = ld;
        loadedLevelPath = AssetDatabase.GetAssetPath(ld);
        levelName       = ld.levelName;

        shapeCells.Clear();
        pieceCells.Clear();

        if (ld.mainShapePrefab != null)
        {
            var h = ld.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
            if (h != null)
            {
                shapeGridSize = h.gridSize;
                shapeCellSize = h.cellSize;
                shapeSpacing  = h.spacing;
                foreach (var c in h.occupiedCells) shapeCells.Add(c);
            }

            // FullShape prefabının ilk çocuğundan cube prefabını al
            shapeCubePrefab = null;
            if (ld.mainShapePrefab.transform.childCount > 0)
            {
                var firstChild = ld.mainShapePrefab.transform.GetChild(0).gameObject;
                shapeCubePrefab = PrefabUtility.GetCorrespondingObjectFromSource(firstChild);
            }
        }

        // Mevcut parça atamalarını yükle — Unity null check ile güvenli kontrol
        pieceCount = Mathf.Max(2, ld.complementaryPieces?.Count ?? 2);
        if (ld.complementaryPieces != null)
        {
            for (int i = 0; i < ld.complementaryPieces.Count; i++)
            {
                var set = new HashSet<Vector3Int>();
                GameObject pieceGo = ld.complementaryPieces[i];
                if (pieceGo != null) // Unity'nin sahte-null kontrolü (MissingReference'a karşı)
                {
                    var ph = pieceGo.GetComponent<CubeShapeDataHolder>();
                    if (ph != null)
                        foreach (var c in ph.occupiedCells) set.Add(c);
                }
                pieceCells.Add(set);
            }
        }
        EnsurePieceLists();

        levelLoaded = true;
        activeLayer = 0;
        activePiece = 0;
        eraseMode   = false;
        Repaint();
    }

    // ── Aksiyonlar ────────────────────────────────────────────────
    private void AssignLayerToPiece(int y, int piece)
    {
        EnsurePieceLists();
        foreach (var cell in shapeCells.Where(c => c.y == y).ToList())
            AssignCell(cell, piece);
        Repaint();
    }

    private void AssignAllToPiece(int piece)
    {
        EnsurePieceLists();
        foreach (var cell in shapeCells.ToList())
            AssignCell(cell, piece);
        Repaint();
    }

    private void ClearLayer(int y)
    {
        foreach (var cell in shapeCells.Where(c => c.y == y).ToList())
            UnassignCell(cell);
        Repaint();
    }

    private void ClearAll() { foreach (var s in pieceCells) s.Clear(); Repaint(); }

    private void EnsurePieceLists()
    {
        while (pieceCells.Count < pieceCount) pieceCells.Add(new HashSet<Vector3Int>());
    }

    // ── Parçaları Kaydet ──────────────────────────────────────────
    private void SavePieces()
    {
        if (loadedLevel == null) { EditorUtility.DisplayDialog("Hata", "LevelData yüklü değil!", "Tamam"); return; }

        string levelDir = Path.GetDirectoryName(loadedLevelPath).Replace('\\', '/');
        float  step     = shapeCellSize + shapeSpacing;

        var piecePrefabs = new List<GameObject>();
        EnsurePieceLists();

        for (int i = 0; i < pieceCount; i++)
        {
            var cells = pieceCells[i].ToList();
            if (cells.Count == 0) { EditorUtility.DisplayDialog("Hata", $"Parça {i + 1} boş!", "Tamam"); return; }

            // Normalize: en küçük koordinatı origin'e taşı
            int minX = cells.Min(c => c.x), minY = cells.Min(c => c.y), minZ = cells.Min(c => c.z);
            var shift = new Vector3Int(minX, minY, minZ);
            var normCells = cells.Select(c => c - shift).ToList();

            string pPath = $"{levelDir}/{levelName}_Piece_{i + 1}.prefab";
            GameObject pRoot = new GameObject($"{levelName}_Piece_{i + 1}");
            var ph = pRoot.AddComponent<CubeShapeDataHolder>();
            ph.shapeName     = $"{levelName}_Piece_{i + 1}";
            ph.gridSize      = shapeGridSize;
            ph.cellSize      = shapeCellSize;
            ph.spacing       = shapeSpacing;
            ph.occupiedCells = new List<Vector3Int>(normCells);

            foreach (var cell in normCells)
            {
                GameObject cube = shapeCubePrefab != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(shapeCubePrefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(pRoot.transform);
                cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (shapeCellSize * 0.5f);
                cube.transform.localScale    = Vector3.one * shapeCellSize;
                cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            }
            piecePrefabs.Add(PrefabUtility.SaveAsPrefabAsset(pRoot, pPath));
            DestroyImmediate(pRoot);
        }

        // LevelData'yı güncelle
        loadedLevel.complementaryPieces = piecePrefabs;
        EditorUtility.SetDirty(loadedLevel);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Kaydedildi!",
            $"✅  {pieceCount} parça  →  {levelDir}/\n\nLevelData.complementaryPieces güncellendi.", "Tamam");
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
        styleBox    = new GUIStyle(GUI.skin.box);
        styleWarn   = new GUIStyle(EditorStyles.helpBox);
        stylesBuilt = true;
    }
}
