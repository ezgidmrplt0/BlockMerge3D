using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  PIECE DESIGNER  —  Bağımsız Parça Oluşturucu & Editörü
//  BlockMerge3D  •  Level Bağımsız Voxel Çizim Tuvali
// ═══════════════════════════════════════════════════════════════════
public class PieceDesignerWindow : EditorWindow
{
    // ── Sabitler ─────────────────────────────────────────────────
    private const string PIECES_PATH = "Assets/Pieces";
    private const string AI_LIBRARY_PATH = "Assets/Levels/ai_pieces_manual_library.json";
    private const float  MIN_CELL_PX = 18f;
    private const float  MAX_CELL_PX = 66f;

    private static readonly Color COL_BG           = new Color(0.047f, 0.047f, 0.07f);
    private static readonly Color COL_GRID          = new Color(0.30f, 0.32f, 0.40f);
    private static readonly Color COL_UNASSIGNED    = new Color(0.07f, 0.07f, 0.10f);
    private static readonly Color COL_HOVER_ASSIGN  = new Color(1.00f, 1.00f, 1.00f, 0.18f);
    private static readonly Color COL_HOVER_REMOVE  = new Color(1.00f, 0.28f, 0.20f, 0.45f);
    private static readonly Color COL_GHOST         = new Color(0.24f, 0.26f, 0.32f, 0.28f);
    private static readonly Color COL_HEADER        = new Color(0.35f, 0.78f, 1.00f);
    private static readonly Color COL_PIECE         = new Color(0.20f, 0.44f, 0.70f); // #3371b2 (blue)

    // ── Editör Durumu ─────────────────────────────────────────────
    private string               pieceName    = "Yeni_Parca";
    private Vector3Int           gridSize     = new Vector3Int(5, 1, 5);
    private HashSet<Vector3Int>  paintedCells = new HashSet<Vector3Int>();

    private float               cellSize      = 1f;
    private float               spacing       = 0.1f;
    private GameObject          cubePrefab;

    // ── UI Durumu ─────────────────────────────────────────────────
    private int      activeLayer     = 0;
    private bool     eraseMode       = false;
    private float    cellPx          = 34f;
    private bool     showGhostLayers = true;
    private Vector2  leftScroll, rightScroll;
    private Vector2? hoverCell;
    private bool     isLeftMouseDown  = false;
    private bool     isRightMouseDown = false;

    // ── Kayıtlı Parçalar Listesi ───────────────────────────────────
    private List<string> savedPieceNames = new List<string>();

    // ── Stil ─────────────────────────────────────────────────────
    private GUIStyle styleHeader, styleBox, styleWarn;
    private bool     stylesBuilt;

    public System.Action onRepaintRequested;

    new public void Repaint()
    {
        base.Repaint();
        if (onRepaintRequested != null)
            onRepaintRequested();
    }

    public void OnEnable()
    {
        RefreshSavedPiecesList();
        BuildStyles();
        
        // Default cube prefab bul
        if (cubePrefab == null)
        {
            string defaultPath = EditorPrefs.GetString("BlockMerge3D_DefaultCubePrefab", "");
            if (!string.IsNullOrEmpty(defaultPath))
            {
                cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(defaultPath);
            }
        }
    }

    // ── OnGUI ══════════════════════════════════════════════════
    public void OnGUI()
    {
        BuildStyles();
        DrawToolbar();

        EditorGUILayout.BeginHorizontal();
        DrawCenterGrid();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();

        DrawStatusBar();
    }

    // ── Toolbar ───────────────────────────────────────────────────
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(30));

        // Parça İsmi
        GUILayout.Label("Parça İsmi:", EditorStyles.toolbarButton, GUILayout.Width(80));
        pieceName = EditorGUILayout.TextField(pieceName, GUILayout.Width(150));
        
        GUILayout.Space(10);

        // Grid Ebatları
        GUILayout.Label("Grid Boyutu:", EditorStyles.toolbarButton, GUILayout.Width(80));
        EditorGUI.BeginChangeCheck();
        int newX = EditorGUILayout.IntField(gridSize.x, GUILayout.Width(35));
        GUILayout.Label("×", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(10));
        int newZ = EditorGUILayout.IntField(gridSize.z, GUILayout.Width(35));
        if (EditorGUI.EndChangeCheck())
        {
            gridSize.x = Mathf.Clamp(newX, 1, 12);
            gridSize.y = 1; // Sabit 2D
            gridSize.z = Mathf.Clamp(newZ, 1, 12);
            activeLayer = 0;
            CleanOutOfBoundsCells();
        }

        GUILayout.Space(15);

        // Çiz/Sil Modu
        GUI.backgroundColor = eraseMode ? new Color(1f, 0.38f, 0.28f) : new Color(0.28f, 0.92f, 0.52f);
        if (GUILayout.Button(eraseMode ? "✕  Sil Modu" : "✏  Çiz Modu", EditorStyles.toolbarButton, GUILayout.Width(90)))
            eraseMode = !eraseMode;
        GUI.backgroundColor = Color.white;

        GUILayout.FlexibleSpace();

        // Zoom
        GUILayout.Label("Zoom:", EditorStyles.toolbarButton, GUILayout.Width(42));
        cellPx = GUILayout.HorizontalSlider(cellPx, MIN_CELL_PX, MAX_CELL_PX, GUILayout.Width(90));

        GUILayout.Space(10);

        // Aksiyonlar
        GUI.backgroundColor = new Color(0.35f, 0.78f, 1f, 0.9f);
        if (GUILayout.Button("💾 Parçayı Kaydet", EditorStyles.toolbarButton, GUILayout.Width(120)))
        {
            SavePiece();
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("🧹 Temizle", EditorStyles.toolbarButton, GUILayout.Width(75)))
        {
            if (EditorUtility.DisplayDialog("Temizle", "Tuvali tamamen temizlemek istiyor musunuz?", "Evet", "Hayır"))
            {
                ClearCanvas();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    // ── Merkez 2D Grid ───────────────────────────────────────────
    private void DrawCenterGrid()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        string modeStr = eraseMode ? "✕  Sil Modu (Sağ Tık / Sürükle)" : "✏  Çiz Modu (Sol Tık / Sürükle)";
        GUILayout.Label($"  {modeStr}",
            new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = COL_HEADER } });
        GUILayout.FlexibleSpace();
        GUILayout.Label($"Boyut: {gridSize.x}×{gridSize.z}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        HandleGridInput(area);
        DrawGrid2D(area);

        EditorGUILayout.EndVertical();
    }

    private void DrawGrid2D(Rect area)
    {
        if (Event.current.type != EventType.Repaint) return;
        int W = gridSize.x, D = gridSize.z;
        float tw = cellPx * W, th = cellPx * D;
        float ox = area.x + (area.width  - tw) * 0.5f;
        float oy = area.y + (area.height - th) * 0.5f;

        EditorGUI.DrawRect(area, COL_BG);

        // Ghost katmanlar (Alttaki/Üstteki çizilmiş küpler)
        if (showGhostLayers)
        {
            foreach (var cell in paintedCells)
            {
                if (cell.y == activeLayer) continue;
                float a = Mathf.Clamp(1f - Mathf.Abs(cell.y - activeLayer) * 0.28f, 0.04f, 0.24f);
                EditorGUI.DrawRect(new Rect(ox + cell.x * cellPx + 1, oy + cell.z * cellPx + 1, cellPx - 2, cellPx - 2),
                    new Color(COL_GHOST.r, COL_GHOST.g, COL_GHOST.b, a));
            }
        }

        // Grid çizgileri
        for (int x = 0; x <= W; x++)
            EditorGUI.DrawRect(new Rect(ox + x * cellPx, oy, 1, th), COL_GRID);
        for (int z = 0; z <= D; z++)
            EditorGUI.DrawRect(new Rect(ox, oy + z * cellPx, tw, 1), COL_GRID);

        // Boyanmış Küpler
        foreach (var cell in paintedCells)
        {
            if (cell.y != activeLayer) continue;
            float cx = ox + cell.x * cellPx + 1.5f;
            float cz = oy + cell.z * cellPx + 1.5f;

            EditorGUI.DrawRect(new Rect(cx, cz, cellPx - 3, cellPx - 3), COL_PIECE);
        }

        // Hover
        if (hoverCell.HasValue)
        {
            int hx = Mathf.RoundToInt(hoverCell.Value.x);
            int hz = Mathf.RoundToInt(hoverCell.Value.y);
            var hCoord = new Vector3Int(hx, activeLayer, hz);
            bool isPainted = paintedCells.Contains(hCoord);
            Color hc = (eraseMode && isPainted) ? COL_HOVER_REMOVE : COL_HOVER_ASSIGN;
            EditorGUI.DrawRect(new Rect(ox + hx * cellPx + 1, oy + hz * cellPx + 1, cellPx - 2, cellPx - 2), hc);
        }

        // Eksen etiketleri
        var lbl = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = new Color(1,1,1,0.32f) } };
        for (int x = 0; x < W; x++) GUI.Label(new Rect(ox + x * cellPx, oy - 14, cellPx, 14), x.ToString(), lbl);
        for (int z = 0; z < D; z++) GUI.Label(new Rect(ox - 18, oy + z * cellPx, 18, cellPx), z.ToString(), lbl);
        GUI.Label(new Rect(ox - 18, oy - 14, 18, 14), "Z\\x", lbl);
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
            var coord = new Vector3Int(Mathf.RoundToInt(hoverCell.Value.x), activeLayer, Mathf.RoundToInt(hoverCell.Value.y));
            if (isLeftMouseDown)
            {
                if (eraseMode) paintedCells.Remove(coord);
                else           paintedCells.Add(coord);
                if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) e.Use();
                Repaint();
            }
            else if (isRightMouseDown)
            {
                paintedCells.Remove(coord);
                if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) e.Use();
                Repaint();
            }
        }

        // Scroll wheel ile katman geçişi
        if (e.type == EventType.ScrollWheel && inside)
        {
            if (e.delta.y < 0 && activeLayer < gridSize.y - 1)
            {
                activeLayer++;
                Repaint();
                e.Use();
            }
            else if (e.delta.y > 0 && activeLayer > 0)
            {
                activeLayer--;
                Repaint();
                e.Use();
            }
        }

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) Repaint();
    }

    // ── Sağ Panel — Parçalar Kütüphanesi ─────────────────────────────
    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(250), GUILayout.ExpandHeight(true));
        
        // Küp Prefabı Seçici
        GUILayout.Label("KÜP PREFABI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUI.BeginChangeCheck();
        cubePrefab = (GameObject)EditorGUILayout.ObjectField(cubePrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck() && cubePrefab != null)
        {
            string path = AssetDatabase.GetAssetPath(cubePrefab);
            EditorPrefs.SetString("BlockMerge3D_DefaultCubePrefab", path);
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(12);

        GUILayout.Label("KAYITLI PARÇALAR", styleHeader);
        EditorGUILayout.LabelField("Assets/Pieces klasöründekiler:", EditorStyles.miniLabel);
        
        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        if (savedPieceNames.Count == 0)
        {
            GUILayout.Space(20);
            GUILayout.Label("Kayıtlı parça yok.\nYeni bir parça çizip kaydedin.", EditorStyles.centeredGreyMiniLabel);
        }
        else
        {
            for (int i = 0; i < savedPieceNames.Count; i++)
            {
                string name = savedPieceNames[i];
                bool isEditing = (pieceName == name);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUI.backgroundColor = isEditing ? COL_HEADER * 0.85f : Color.white;
                
                var btnStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = isEditing ? FontStyle.Bold : FontStyle.Normal };
                if (GUILayout.Button(isEditing ? $"● {name}" : name, btnStyle))
                {
                    LoadPiece(name);
                }
                GUI.backgroundColor = Color.white;

                // Silme butonu
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f, 0.8f);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    DeletePiece(name);
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
        }

        EditorGUILayout.EndScrollView();
        
        // Açıklama Notu
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.HelpBox("💡 Kaydettiğiniz parçalar otomatik olarak AI Seviye Tasarımcısı kütüphanesine (✍️ MANUEL LİSTE) eklenir.", MessageType.Info);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndVertical();
    }

    // ── Status Bar ────────────────────────────────────────────────
    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(22));
        
        int totalCubes = paintedCells.Count;
        string hoverStr = hoverCell.HasValue ? $"X:{hoverCell.Value.x} Z:{hoverCell.Value.y}" : "---";
        EditorGUILayout.LabelField($"Aktif Parça: {pieceName}  •  Küp Sayısı: {totalCubes}  •  İşaretçi: {hoverStr}", EditorStyles.miniLabel);
        
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("BlockMerge3D  •  Voxel Piece Creator", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ── Editör Aksiyonları ─────────────────────────────────────────
    private void ClearCanvas()
    {
        paintedCells.Clear();
        Repaint();
    }

    private void CleanOutOfBoundsCells()
    {
        paintedCells.RemoveWhere(c => c.x >= gridSize.x || c.z >= gridSize.z || c.y >= gridSize.y);
    }

    private void RefreshSavedPiecesList()
    {
        savedPieceNames.Clear();
        if (Directory.Exists(PIECES_PATH))
        {
            var files = Directory.GetFiles(PIECES_PATH, "*.prefab");
            foreach (var f in files)
            {
                savedPieceNames.Add(Path.GetFileNameWithoutExtension(f));
            }
        }
    }

    private void LoadPiece(string name)
    {
        string pPath = $"{PIECES_PATH}/{name}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Hata", $"{name} prefabı yüklenemedi.", "Tamam");
            return;
        }

        var holder = prefab.GetComponent<CubeShapeDataHolder>();
        if (holder == null)
        {
            EditorUtility.DisplayDialog("Hata", $"{name} prefabında CubeShapeDataHolder bulunamadı.", "Tamam");
            return;
        }

        pieceName = name;
        gridSize = new Vector3Int(holder.gridSize.x, 1, holder.gridSize.z);
        activeLayer = 0;
        
        paintedCells.Clear();
        foreach (var cell in holder.occupiedCells)
        {
            paintedCells.Add(new Vector3Int(cell.x, 0, cell.z));
        }
        Repaint();
    }

    private void SavePiece()
    {
        if (string.IsNullOrEmpty(pieceName))
        {
            EditorUtility.DisplayDialog("Hata", "Lütfen geçerli bir parça ismi girin!", "Tamam");
            return;
        }

        if (paintedCells.Count == 0)
        {
            EditorUtility.DisplayDialog("Hata", "Boş parça kaydedilemez! Lütfen grid üzerinde çizim yapın.", "Tamam");
            return;
        }

        if (!Directory.Exists(PIECES_PATH))
        {
            Directory.CreateDirectory(PIECES_PATH);
        }

        // Normalize: en küçük koordinatı origin'e (0,0,0) taşı
        var cells = paintedCells.ToList();
        int minX = cells.Min(c => c.x);
        int minY = cells.Min(c => c.y);
        int minZ = cells.Min(c => c.z);
        var shift = new Vector3Int(minX, minY, minZ);
        var normCells = cells.Select(c => c - shift).ToList();

        // Parçanın gerçek sınırlarını hesapla
        int w = normCells.Max(c => c.x) + 1;
        int h = normCells.Max(c => c.y) + 1;
        int d = normCells.Max(c => c.z) + 1;
        Vector3Int finalSize = new Vector3Int(w, h, d);

        string pPath = $"{PIECES_PATH}/{pieceName}.prefab";
        string aPath = $"{PIECES_PATH}/{pieceName}.asset";

        // 1. ScriptableObject
        CubeShapeData data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(aPath);
        bool isNew = data == null;
        if (isNew) data = CreateInstance<CubeShapeData>();

        data.shapeName = pieceName;
        data.gridSize = finalSize;
        data.cellSize = cellSize;
        data.spacing = spacing;
        data.occupiedCells = new List<Vector3Int>(normCells);

        if (isNew) AssetDatabase.CreateAsset(data, aPath);
        else EditorUtility.SetDirty(data);

        // 2. Prefab
        GameObject pRoot = new GameObject(pieceName);
        var ph = pRoot.AddComponent<CubeShapeDataHolder>();
        ph.shapeName = pieceName;
        ph.gridSize = finalSize;
        ph.cellSize = cellSize;
        ph.spacing = spacing;
        ph.occupiedCells = new List<Vector3Int>(normCells);

        float step = cellSize + spacing;
        foreach (var cell in normCells)
        {
            GameObject cube = cubePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(pRoot.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
            cube.transform.localScale = Vector3.one * cellSize;
            cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";

            var r = cube.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = new Material(Shader.Find("Standard"));
                r.sharedMaterial.color = COL_PIECE;
            }
        }

        PrefabUtility.SaveAsPrefabAsset(pRoot, pPath);
        DestroyImmediate(pRoot);

        // 3. AI Kütüphanesine (JSON) Ekle
        AddPieceToAILibraryJSON(pieceName, normCells);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        RefreshSavedPiecesList();

        EditorUtility.DisplayDialog("Başarılı 💾", 
            $"'{pieceName}' parçası kaydedildi!\n\n" +
            $"• Prefab: {pPath}\n" +
            $"• Asset: {aPath}\n" +
            "AI eğitim kütüphanesi güncellendi.", "Harika");
    }

    private void DeletePiece(string name)
    {
        if (EditorUtility.DisplayDialog("Silmeyi Onayla", 
            $"'{name}' parçasını tamamen silmek istediğinizden emin misiniz?", 
            "Evet, Sil", "İptal"))
        {
            string pPath = $"{PIECES_PATH}/{name}.prefab";
            string aPath = $"{PIECES_PATH}/{name}.asset";

            AssetDatabase.DeleteAsset(pPath);
            AssetDatabase.DeleteAsset(aPath);

            RemovePieceFromAILibraryJSON(name);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            RefreshSavedPiecesList();

            if (pieceName == name)
            {
                pieceName = "Yeni_Parca";
                paintedCells.Clear();
            }
            Repaint();
        }
    }

    private void AddPieceToAILibraryJSON(string name, List<Vector3Int> normalizedCells)
    {
        // Levels klasörünün varlığından emin ol
        if (!Directory.Exists("Assets/Levels"))
        {
            Directory.CreateDirectory("Assets/Levels");
        }

        AIPieceListWrapper wrapper = new AIPieceListWrapper();
        if (File.Exists(AI_LIBRARY_PATH))
        {
            try
            {
                string jsonContent = File.ReadAllText(AI_LIBRARY_PATH);
                wrapper = JsonUtility.FromJson<AIPieceListWrapper>(jsonContent);
                if (wrapper == null) wrapper = new AIPieceListWrapper();
            }
            catch {}
        }

        if (!wrapper.names.Contains(name))
        {
            wrapper.names.Add(name);
        }

        int index = wrapper.pieces.FindIndex(p => p.prompt == name);
        AIPieceDatasetEntry entry = new AIPieceDatasetEntry
        {
            pieceIndex = index >= 0 ? index : wrapper.pieces.Count,
            generationMode = "2D_Grid_PieceDesigner",
            prompt = name,
            cubeCount = normalizedCells.Count,
            cells = normalizedCells.Select(c => new SerializableCell(c)).ToList()
        };

        if (index >= 0)
        {
            wrapper.pieces[index] = entry;
        }
        else
        {
            wrapper.pieces.Add(entry);
        }

        string saveJson = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(AI_LIBRARY_PATH, saveJson);
    }

    private void RemovePieceFromAILibraryJSON(string name)
    {
        if (!File.Exists(AI_LIBRARY_PATH)) return;

        try
        {
            string jsonContent = File.ReadAllText(AI_LIBRARY_PATH);
            AIPieceListWrapper wrapper = JsonUtility.FromJson<AIPieceListWrapper>(jsonContent);
            if (wrapper == null) return;

            if (wrapper.names.Contains(name))
            {
                wrapper.names.Remove(name);
            }

            int index = wrapper.pieces.FindIndex(p => p.prompt == name);
            if (index >= 0)
            {
                wrapper.pieces.RemoveAt(index);
                // Indisleri yeniden düzenle
                for (int i = 0; i < wrapper.pieces.Count; i++)
                {
                    wrapper.pieces[i].pieceIndex = i;
                }
            }

            string saveJson = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(AI_LIBRARY_PATH, saveJson);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("AI Kütüphanesinden silerken hata oluştu: " + ex.Message);
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
        styleBox    = new GUIStyle(GUI.skin.box);
        styleWarn   = new GUIStyle(EditorStyles.helpBox);
        stylesBuilt = true;
    }
}
