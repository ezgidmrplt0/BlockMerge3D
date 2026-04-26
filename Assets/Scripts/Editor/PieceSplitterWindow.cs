using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class PieceSplitterWindow : EditorWindow
{
    private static readonly Color[] PIECE_COLORS =
    {
        new Color(1f,    0.38f, 0.38f),
        new Color(0.38f, 0.78f, 0.42f),
        new Color(0.38f, 0.58f, 1f),
        new Color(1f,    0.85f, 0.25f),
        new Color(0.85f, 0.38f, 0.9f),
        new Color(0.3f,  0.88f, 0.88f),
        new Color(1f,    0.58f, 0.25f),
        new Color(0.75f, 0.75f, 0.8f),
    };

    // --- State ---
    private CubeShapeData sourceShape;
    private Dictionary<Vector3Int, int> cellPieceMap = new Dictionary<Vector3Int, int>();
    private int activePiece = 0;
    private int pieceCount = 2;
    private string levelName = "NewLevel";
    private GameObject cubePrefab;

    // --- UI ---
    private GUIStyle headerStyle;
    private GUIStyle sidebarStyle;
    private Vector2 leftScroll;
    private Vector2 rightScroll;

    // --- 3D Preview ---
    private PreviewRenderUtility previewUtility;
    private Vector2 previewDir = new Vector2(135, -30);
    private float previewDistance = 15f;
    private Mesh cubeMesh;
    private Material[] pieceMaterials;
    private Material unassignedMaterial;
    private Material hoveredMaterial;
    private Material gizmoMaterial;
    private Material ghostSliceMaterial;
    private bool useOrthographic = true;

    // --- Dilim Filtresi ---
    private int xSlice = -1; // -1 = tümü göster
    private int ySlice = -1;
    private int zSlice = -1;

    private Vector3Int? hoveredCell = null;

    private const string SHAPES_PATH = "Assets/Shapes";
    private const string LEVELS_PATH = "Assets/Levels";

    [MenuItem("BlockMerge3D/Piece Splitter")]
    public static void ShowWindow()
    {
        var w = GetWindow<PieceSplitterWindow>("Piece Splitter");
        w.minSize = new Vector2(960, 600);
    }

    private void OnEnable()
    {
        previewUtility = new PreviewRenderUtility();
        previewUtility.camera.fieldOfView = 30f;
        previewUtility.camera.farClipPlane = 1000f;
        previewUtility.camera.nearClipPlane = 0.1f;

        cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        unassignedMaterial = new Material(Shader.Find("Unlit/Color"));
        unassignedMaterial.color = new Color(0.32f, 0.32f, 0.36f);

        hoveredMaterial = new Material(Shader.Find("Unlit/Color"));
        hoveredMaterial.color = Color.white;

        gizmoMaterial = new Material(Shader.Find("Unlit/Color"));

        ghostSliceMaterial = new Material(Shader.Find("Unlit/Color"));
        ghostSliceMaterial.color = new Color(0.14f, 0.14f, 0.17f);

        pieceMaterials = new Material[PIECE_COLORS.Length];
        for (int i = 0; i < PIECE_COLORS.Length; i++)
        {
            pieceMaterials[i] = new Material(Shader.Find("Unlit/Color"));
            pieceMaterials[i].color = PIECE_COLORS[i];
        }
    }

    private void OnDisable()
    {
        if (previewUtility != null) previewUtility.Cleanup();
        if (unassignedMaterial != null) DestroyImmediate(unassignedMaterial);
        if (hoveredMaterial != null) DestroyImmediate(hoveredMaterial);
        if (gizmoMaterial != null) DestroyImmediate(gizmoMaterial);
        if (ghostSliceMaterial != null) DestroyImmediate(ghostSliceMaterial);
        if (pieceMaterials != null)
            foreach (var m in pieceMaterials)
                if (m != null) DestroyImmediate(m);
    }

    private void InitStyles()
    {
        if (headerStyle != null) return;
        headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        headerStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);
        sidebarStyle = new GUIStyle(GUI.skin.box);
    }

    private void OnGUI()
    {
        InitStyles();
        EditorGUILayout.BeginHorizontal();
        DrawLeftSidebar();
        DrawCenterPanel();
        DrawRightSidebar();
        EditorGUILayout.EndHorizontal();
        DrawStatusBar();
    }

    // ─── Left Sidebar ────────────────────────────────────────────────────────

    private void DrawLeftSidebar()
    {
        EditorGUILayout.BeginVertical(sidebarStyle, GUILayout.Width(230), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        EditorGUILayout.LabelField("KAYNAK ŞEKIL", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUI.BeginChangeCheck();
        sourceShape = (CubeShapeData)EditorGUILayout.ObjectField("Shape Asset", sourceShape, typeof(CubeShapeData), false);
        if (EditorGUI.EndChangeCheck() && sourceShape != null) LoadShape(sourceShape);

        if (Directory.Exists(SHAPES_PATH))
        {
            EditorGUILayout.LabelField("Hızlı Yükle:", EditorStyles.miniLabel);
            foreach (var p in Directory.GetFiles(SHAPES_PATH, "*.asset"))
            {
                string ap = p.Replace('\\', '/');
                if (GUILayout.Button(Path.GetFileNameWithoutExtension(p), EditorStyles.miniButton))
                {
                    var data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(ap);
                    if (data != null) { sourceShape = data; LoadShape(data); }
                }
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("PARÇALAR", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawPieceList();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("GÖRÜNÜM", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        useOrthographic = EditorGUILayout.Toggle("Orthographic", useOrthographic);
        if (GUILayout.Button("Odakla")) FocusCamera();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Dilim Filtresi  (-1 = tümü)", EditorStyles.miniLabel);
        EditorGUI.BeginDisabledGroup(sourceShape == null);
        int maxX = sourceShape != null ? Mathf.Max(0, sourceShape.gridSize.x - 1) : 0;
        int maxY = sourceShape != null ? Mathf.Max(0, sourceShape.gridSize.y - 1) : 0;
        int maxZ = sourceShape != null ? Mathf.Max(0, sourceShape.gridSize.z - 1) : 0;
        xSlice = Mathf.Clamp(xSlice, -1, maxX);
        ySlice = Mathf.Clamp(ySlice, -1, maxY);
        zSlice = Mathf.Clamp(zSlice, -1, maxZ);
        EditorGUI.BeginChangeCheck();
        xSlice = EditorGUILayout.IntSlider("X", xSlice, -1, maxX);
        ySlice = EditorGUILayout.IntSlider("Y", ySlice, -1, maxY);
        zSlice = EditorGUILayout.IntSlider("Z", zSlice, -1, maxZ);
        if (EditorGUI.EndChangeCheck()) Repaint();
        if (xSlice != -1 || ySlice != -1 || zSlice != -1)
            if (GUILayout.Button("Sıfırla", EditorStyles.miniButton))
                { xSlice = -1; ySlice = -1; zSlice = -1; Repaint(); }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawPieceList()
    {
        for (int i = 0; i < pieceCount; i++)
        {
            int count = cellPieceMap.Values.Count(v => v == i);
            Color pieceColor = PIECE_COLORS[i % PIECE_COLORS.Length];

            EditorGUILayout.BeginHorizontal();
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = (activePiece == i) ? pieceColor : pieceColor * 0.55f;

            GUIStyle btn = new GUIStyle(GUI.skin.button)
            {
                fontStyle = activePiece == i ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft
            };
            if (GUILayout.Button($"  Parça {i + 1}   [{count} küp]", btn))
                activePiece = i;

            GUI.backgroundColor = prevBg;

            // Sadece son parça silinebilir, minimum 2 parça
            if (i == pieceCount - 1 && pieceCount > 2)
            {
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    foreach (var key in cellPieceMap.Keys.Where(k => cellPieceMap[k] == i).ToList())
                        cellPieceMap[key] = -1;
                    pieceCount--;
                    if (activePiece >= pieceCount) activePiece = pieceCount - 1;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(4);
        if (pieceCount < PIECE_COLORS.Length)
        {
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f, 0.7f);
            if (GUILayout.Button("+ Parça Ekle")) pieceCount++;
            GUI.backgroundColor = Color.white;
        }
    }

    // ─── Center Panel (3D Preview) ────────────────────────────────────────────

    private void DrawCenterPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        HandleViewportInput(rect);
        Draw3DPreview(rect);

        GUILayout.Space(-60);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(10);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(340));
        int total = sourceShape != null ? sourceShape.occupiedCells.Count : 0;
        int assigned = cellPieceMap.Values.Count(v => v >= 0);
        EditorGUILayout.LabelField($"Toplam: {total}  |  Atanmış: {assigned}  |  Kalan: {total - assigned}", EditorStyles.miniLabel);
        if (hoveredCell.HasValue)
        {
            int hp = cellPieceMap.TryGetValue(hoveredCell.Value, out int hv) ? hv : -1;
            string pLabel = hp >= 0 ? $"Parça {hp + 1}" : "Atanmamış";
            EditorGUILayout.LabelField($"Hücre {hoveredCell.Value}  →  {pLabel}", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);

        if (sourceShape == null)
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Sol panelden bir şekil asset'i yükle", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.EndVertical();
    }

    // ─── Right Sidebar ────────────────────────────────────────────────────────

    private void DrawRightSidebar()
    {
        EditorGUILayout.BeginVertical(sidebarStyle, GUILayout.Width(290), GUILayout.ExpandHeight(true));
        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        EditorGUILayout.LabelField("EXPORT", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        levelName = EditorGUILayout.TextField("Level Adı", levelName);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);
        EditorGUILayout.Space(5);

        if (sourceShape != null)
        {
            int total    = sourceShape.occupiedCells.Count;
            int assigned = cellPieceMap.Values.Count(v => v >= 0);
            bool allAssigned     = assigned == total;
            bool allPiecesHaveCells = Enumerable.Range(0, pieceCount).All(i => cellPieceMap.Values.Any(v => v == i));

            if (!allAssigned)
                EditorGUILayout.HelpBox($"{total - assigned} küp henüz atanmadı.", MessageType.Warning);
            else if (!allPiecesHaveCells)
                EditorGUILayout.HelpBox("Boş parça var — her parçada en az 1 küp olmalı.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Hazır. Export edebilirsin.", MessageType.Info);

            EditorGUI.BeginDisabledGroup(!allAssigned || !allPiecesHaveCells);
            GUI.backgroundColor = new Color(0.4f, 1f, 0.5f, 0.9f);
            if (GUILayout.Button("EXPORT LEVEL", GUILayout.Height(45))) ExportLevel();
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("PARÇA ÖZETİ", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        for (int i = 0; i < pieceCount; i++)
        {
            int count = cellPieceMap.Values.Count(v => v == i);
            Color c = PIECE_COLORS[i % PIECE_COLORS.Length];
            EditorGUILayout.LabelField(
                $"Parça {i + 1}: {count} küp",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = c } });
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("NASIL KULLANILIR", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("1. Sol panelden şekil yükle",        EditorStyles.miniLabel);
        EditorGUILayout.LabelField("2. Aktif parçayı seç",               EditorStyles.miniLabel);
        EditorGUILayout.LabelField("3. Küpe sol tıkla → parçaya atar",   EditorStyles.miniLabel);
        EditorGUILayout.LabelField("   Shift+tıkla → atamayı kaldırır", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("4. Tüm küpler atandıktan sonra",     EditorStyles.miniLabel);
        EditorGUILayout.LabelField("   'Export Level' düğmesine bas",    EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawStatusBar()
    {
        Color ac = PIECE_COLORS[activePiece % PIECE_COLORS.Length];
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(25));
        EditorGUILayout.LabelField(
            $"Aktif: Parça {activePiece + 1}",
            new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ac } });
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("BlockMerge 3D — Piece Splitter", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ─── Shape / Camera ───────────────────────────────────────────────────────

    private void LoadShape(CubeShapeData data)
    {
        cellPieceMap.Clear();
        foreach (var cell in data.occupiedCells)
            cellPieceMap[cell] = -1;
        FocusCamera();
        Repaint();
    }

    private void FocusCamera()
    {
        if (sourceShape == null) return;
        previewDistance = Mathf.Max(sourceShape.gridSize.x, sourceShape.gridSize.y, sourceShape.gridSize.z) * 2.5f;
    }

    // ─── Viewport Input ───────────────────────────────────────────────────────

    private void HandleViewportInput(Rect rect)
    {
        if (sourceShape == null) return;
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDrag && e.button == 1) { previewDir += e.delta * 0.5f; e.Use(); Repaint(); }
        if (e.type == EventType.ScrollWheel) { previewDistance = Mathf.Clamp(previewDistance + e.delta.y * 0.5f, 2f, 100f); e.Use(); Repaint(); }

        Vector2 norm = new Vector2(
            (e.mousePosition.x - rect.x) / rect.width,
            1f - (e.mousePosition.y - rect.y) / rect.height);
        Ray ray = previewUtility.camera.ViewportPointToRay(norm);

        var prev = hoveredCell;
        UpdateHoverState(ray);
        if (hoveredCell != prev) Repaint();

        if (e.type == EventType.MouseDown && hoveredCell.HasValue)
        {
            if (e.button == 0 && !e.shift) { cellPieceMap[hoveredCell.Value] = activePiece; Repaint(); }
            else if (e.button == 1 || (e.button == 0 && e.shift)) { cellPieceMap[hoveredCell.Value] = -1; Repaint(); }
            e.Use();
        }
    }

    private void UpdateHoverState(Ray ray)
    {
        hoveredCell = null;
        float step = sourceShape.cellSize + sourceShape.spacing;
        Vector3 origin = -(Vector3)sourceShape.gridSize * step * 0.5f;
        float best = float.MaxValue;

        foreach (var cell in sourceShape.occupiedCells)
        {
            if (!PassesSlice(cell)) continue;
            Vector3 center = origin + (Vector3)cell * step + Vector3.one * (sourceShape.cellSize * 0.5f);
            if (new Bounds(center, Vector3.one * sourceShape.cellSize).IntersectRay(ray, out float d) && d < best)
            {
                best = d;
                hoveredCell = cell;
            }
        }
    }

    private bool PassesSlice(Vector3Int cell)
    {
        if (xSlice >= 0 && cell.x != xSlice) return false;
        if (ySlice >= 0 && cell.y != ySlice) return false;
        if (zSlice >= 0 && cell.z != zSlice) return false;
        return true;
    }

    // ─── 3D Preview ───────────────────────────────────────────────────────────

    private void Draw3DPreview(Rect rect)
    {
        if (previewUtility == null) return;
        previewUtility.BeginPreview(rect, GUIStyle.none);

        previewUtility.camera.orthographic = useOrthographic;
        if (useOrthographic) previewUtility.camera.orthographicSize = previewDistance * 0.4f;
        Quaternion camRot = Quaternion.Euler(previewDir.y, previewDir.x, 0);
        previewUtility.camera.transform.position = camRot * (Vector3.back * previewDistance);
        previewUtility.camera.transform.rotation = camRot;
        previewUtility.camera.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        previewUtility.camera.clearFlags = CameraClearFlags.Color;
        previewUtility.lights[0].intensity = 0f;
        previewUtility.lights[1].intensity = 0f;

        DrawGridLines();

        if (sourceShape != null)
        {
            float cs   = sourceShape.cellSize;
            float step = cs + sourceShape.spacing;
            float size = cs * 0.82f;
            Vector3 origin = -(Vector3)sourceShape.gridSize * step * 0.5f;

            foreach (var cell in sourceShape.occupiedCells)
            {
                Vector3 pos = origin + (Vector3)cell * step + Vector3.one * (cs * 0.5f);
                bool inSlice = PassesSlice(cell);
                bool isHovered = inSlice && hoveredCell.HasValue && hoveredCell.Value == cell;

                Material mat;
                float drawSize;
                if (!inSlice)
                {
                    mat = ghostSliceMaterial;
                    drawSize = size * 0.35f;
                }
                else if (isHovered)
                {
                    mat = hoveredMaterial;
                    drawSize = size;
                }
                else
                {
                    int p = cellPieceMap.TryGetValue(cell, out int v) ? v : -1;
                    mat = p >= 0 ? pieceMaterials[p % pieceMaterials.Length] : unassignedMaterial;
                    drawSize = size;
                }

                previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * drawSize), mat, 0);
                if (inSlice) DrawCubeEdges(pos, drawSize, cs);
            }
        }

        DrawAxisGizmo(camRot);
        previewUtility.camera.Render();
        GUI.DrawTexture(rect, previewUtility.EndPreview(), ScaleMode.StretchToFill, false);
        DrawOrientationOverlay(rect, camRot);
    }

    private void DrawGridLines()
    {
        if (sourceShape == null) return;
        float step = sourceShape.cellSize + sourceShape.spacing;
        Vector3 off = -(Vector3)sourceShape.gridSize * step * 0.5f;
        gizmoMaterial.color = new Color(1, 1, 1, 0.06f);
        for (int x = 0; x <= sourceShape.gridSize.x; x++)
            DrawEdgeMesh(off + new Vector3(x * step, 0, sourceShape.gridSize.z * 0.5f * step),
                new Vector3(0.01f, 0.01f, sourceShape.gridSize.z * step));
        for (int z = 0; z <= sourceShape.gridSize.z; z++)
            DrawEdgeMesh(off + new Vector3(sourceShape.gridSize.x * 0.5f * step, 0, z * step),
                new Vector3(sourceShape.gridSize.x * step, 0.01f, 0.01f));
    }

    private void DrawCubeEdges(Vector3 c, float size, float cs)
    {
        float h = size * 0.5f;
        float t = Mathf.Max(0.012f, cs * 0.04f);
        gizmoMaterial.color = new Color(0.08f, 0.08f, 0.1f);
        DrawEdgeMesh(c + new Vector3(0,  -h, -h), new Vector3(size, t, t));
        DrawEdgeMesh(c + new Vector3(0,  -h,  h), new Vector3(size, t, t));
        DrawEdgeMesh(c + new Vector3(-h, -h,  0), new Vector3(t, t, size));
        DrawEdgeMesh(c + new Vector3( h, -h,  0), new Vector3(t, t, size));
        DrawEdgeMesh(c + new Vector3(0,   h, -h), new Vector3(size, t, t));
        DrawEdgeMesh(c + new Vector3(0,   h,  h), new Vector3(size, t, t));
        DrawEdgeMesh(c + new Vector3(-h,  h,  0), new Vector3(t, t, size));
        DrawEdgeMesh(c + new Vector3( h,  h,  0), new Vector3(t, t, size));
        DrawEdgeMesh(c + new Vector3(-h, 0, -h), new Vector3(t, size, t));
        DrawEdgeMesh(c + new Vector3( h, 0, -h), new Vector3(t, size, t));
        DrawEdgeMesh(c + new Vector3(-h, 0,  h), new Vector3(t, size, t));
        DrawEdgeMesh(c + new Vector3( h, 0,  h), new Vector3(t, size, t));
    }

    private void DrawEdgeMesh(Vector3 pos, Vector3 scale)
    {
        previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, scale), gizmoMaterial, 0);
    }

    private void DrawAxisGizmo(Quaternion camRot)
    {
        float s = 0.15f;
        Vector3 pos = previewUtility.camera.transform.position
            + previewUtility.camera.transform.forward * 2f
            + previewUtility.camera.transform.right * 1.3f
            + previewUtility.camera.transform.up * 0.9f;
        DrawAxisLine(Vector3.right,   Color.red,                   camRot, pos, s);
        DrawAxisLine(Vector3.up,      Color.green,                  camRot, pos, s);
        DrawAxisLine(Vector3.forward, new Color(0.4f, 0.4f, 1f),   camRot, pos, s);
    }

    private void DrawAxisLine(Vector3 dir, Color col, Quaternion camRot, Vector3 pos, float s)
    {
        gizmoMaterial.color = col;
        previewUtility.DrawMesh(cubeMesh,
            Matrix4x4.TRS(pos + (camRot * dir) * s * 0.5f, camRot * Quaternion.LookRotation(dir), new Vector3(0.015f, 0.015f, s)),
            gizmoMaterial, 0);
    }

    private void DrawOrientationOverlay(Rect r, Quaternion rot)
    {
        Vector3 f = rot * Vector3.forward;
        string label;
        if (Vector3.Dot(f, Vector3.forward) > 0.8f)      label = "Front";
        else if (Vector3.Dot(f, Vector3.back)    > 0.8f) label = "Back";
        else if (Vector3.Dot(f, Vector3.up)      > 0.8f) label = "Bottom";
        else if (Vector3.Dot(f, Vector3.down)    > 0.8f) label = "Top";
        else if (Vector3.Dot(f, Vector3.left)    > 0.8f) label = "Right";
        else if (Vector3.Dot(f, Vector3.right)   > 0.8f) label = "Left";
        else label = useOrthographic ? "Isometric" : "Perspective";

        GUI.Label(new Rect(r.x + r.width - 100, r.y + r.height - 30, 90, 20), label,
            new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1, 1, 1, 0.5f) }
            });
    }

    // ─── Export ───────────────────────────────────────────────────────────────

    private void ExportLevel()
    {
        string levelDir = $"{LEVELS_PATH}/{levelName}";
        if (!Directory.Exists(levelDir)) Directory.CreateDirectory(levelDir);

        float cs   = sourceShape.cellSize;
        float sp   = sourceShape.spacing;
        float step = cs + sp;

        // Her parça için prefab
        List<GameObject> piecePrefabs = new List<GameObject>();
        for (int i = 0; i < pieceCount; i++)
        {
            List<Vector3Int> cells = cellPieceMap
                .Where(kv => kv.Value == i)
                .Select(kv => kv.Key)
                .ToList();

            GameObject root = new GameObject($"{levelName}_Piece_{i + 1}");
            CubeShapeDataHolder holder = root.AddComponent<CubeShapeDataHolder>();
            holder.shapeName     = $"{levelName}_Piece_{i + 1}";
            holder.gridSize      = sourceShape.gridSize;
            holder.cellSize      = cs;
            holder.spacing       = sp;
            holder.occupiedCells = new List<Vector3Int>(cells);

            foreach (var cell in cells)
            {
                GameObject cube = cubePrefab != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform);
                cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cs * 0.5f);
                cube.transform.localScale    = Vector3.one * cs;
                cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            }

            string path = $"{levelDir}/{levelName}_Piece_{i + 1}.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            piecePrefabs.Add(saved);
        }

        // Tam şekil prefab
        GameObject fullRoot = new GameObject($"{levelName}_FullShape");
        CubeShapeDataHolder fullHolder = fullRoot.AddComponent<CubeShapeDataHolder>();
        fullHolder.shapeName     = levelName;
        fullHolder.gridSize      = sourceShape.gridSize;
        fullHolder.cellSize      = cs;
        fullHolder.spacing       = sp;
        fullHolder.occupiedCells = new List<Vector3Int>(sourceShape.occupiedCells);
        foreach (var cell in sourceShape.occupiedCells)
        {
            GameObject cube = cubePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(fullRoot.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cs * 0.5f);
            cube.transform.localScale    = Vector3.one * cs;
            cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
        }
        string fullPath = $"{levelDir}/{levelName}_FullShape.prefab";
        GameObject savedFull = PrefabUtility.SaveAsPrefabAsset(fullRoot, fullPath);
        DestroyImmediate(fullRoot);

        // LevelData asset
        LevelData levelData = ScriptableObject.CreateInstance<LevelData>();
        levelData.levelName          = levelName;
        levelData.mainShapePrefab    = savedFull;
        levelData.complementaryPieces = piecePrefabs;
        AssetDatabase.CreateAsset(levelData, $"{levelDir}/{levelName}_LevelData.asset");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Export Tamamlandı!",
            $"{pieceCount} parça prefab\n1 tam şekil prefab\n1 LevelData asset\n\n{levelDir}/", "Tamam");
    }
}
