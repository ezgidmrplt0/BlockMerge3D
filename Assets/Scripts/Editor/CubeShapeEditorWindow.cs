using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class CubeShapeEditorWindow : EditorWindow
{
    private enum GridOrigin { BottomLeft, Center }
    private enum ToolMode  { Add, Remove }
    private enum ShapeType { MainShape, Piece }

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
    private Vector3Int gridSize  = new Vector3Int(5, 5, 5);
    private float      cellSize  = 1.0f;
    private float      spacing   = 0.1f;
    private int        ySlice    = -1;
    private GridOrigin gridOrigin  = GridOrigin.BottomLeft;
    private ToolMode   currentTool = ToolMode.Add;
    private string     shapeName   = "NewCubeShape";
    private bool       useOrthographic = true;
    private ShapeType  shapeType = ShapeType.MainShape;

    // --- Piece Assignment ---
    private bool                     pieceAssignmentMode = false;
    private Dictionary<Vector3Int,int> cellPieceMap = new Dictionary<Vector3Int,int>();
    private int    activePiece = 0;
    private int    pieceCount  = 2;
    private string levelName   = "NewLevel";

    private GameObject          cubePrefab;
    private GameObject          currentShapeObject;
    private CubeShapeDataHolder dataHolder;

    private Vector3Int? hoveredCell   = null;
    private Vector3Int? placementCell = null;
    private bool        isEditing     = false;
    private bool        inPreviewBlock = false;

    // --- UI ---
    private GUIStyle headerStyle;
    private GUIStyle sidebarStyle;
    private GUIStyle toolButtonStyle;
    private Vector2  sidebarScroll;
    private Vector2  libraryScroll;

    // --- Preview ---
    private PreviewRenderUtility previewUtility;
    private Vector2 previewDir      = new Vector2(135, -30);
    private float   previewDistance = 15f;
    private Mesh    cubeMesh;
    private Material cubeMaterial;
    private Material gizmoMaterial;
    private Material hoverMaterial;
    private Material hoveredMaterial;
    private Material removeMaterial;
    private Material unassignedMaterial;
    private Material[] pieceMaterials;

    private const string GENERATED_PATH    = "Assets/Shapes";
    private const string PIECES_PATH       = "Assets/Pieces";
    private const string LEVELS_PATH       = "Assets/Levels";
    private const string EDITOR_OBJECT_NAME = "Shape_Editor_Object";

    [MenuItem("BlockMerge3D/Cube Shape Builder Pro")]
    public static void ShowWindow()
    {
        var w = GetWindow<CubeShapeEditorWindow>("Cube Shape Builder");
        w.minSize = new Vector2(900, 600);
    }

    // ─── Enable / Disable ─────────────────────────────────────────────────────

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        previewUtility = new PreviewRenderUtility();
        previewUtility.camera.fieldOfView  = 30f;
        previewUtility.camera.farClipPlane = 1000;
        previewUtility.camera.nearClipPlane = 0.1f;
        cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        BuildMaterials();
        FindExistingEditorObject();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        if (previewUtility != null)
        {
            if (inPreviewBlock) { try { previewUtility.EndPreview(); } catch { } inPreviewBlock = false; }
            previewUtility.Cleanup();
        }
        DestroyMaterials();
    }

    private void BuildMaterials()
    {
        cubeMaterial       = Mat(0.62f, 0.66f, 0.72f);
        gizmoMaterial      = Mat(0, 0, 0);
        hoverMaterial      = Mat(0.2f, 0.9f, 1f);
        hoveredMaterial    = Mat(1f, 0.9f, 0.1f);
        removeMaterial     = Mat(1f, 0.2f, 0.1f);
        unassignedMaterial = Mat(0.32f, 0.32f, 0.36f);
        pieceMaterials = new Material[PIECE_COLORS.Length];
        for (int i = 0; i < PIECE_COLORS.Length; i++)
            pieceMaterials[i] = Mat(PIECE_COLORS[i]);
    }

    private void DestroyMaterials()
    {
        void D(Material m) { if (m) DestroyImmediate(m); }
        D(cubeMaterial); D(gizmoMaterial); D(hoverMaterial);
        D(hoveredMaterial); D(removeMaterial); D(unassignedMaterial);
        if (pieceMaterials != null) foreach (var m in pieceMaterials) D(m);
    }

    private void EnsureMaterials()
    {
        if (!cubeMaterial)       BuildMaterials();
        else if (!gizmoMaterial) BuildMaterials();
    }

    private static Material Mat(float r, float g, float b)
    {
        var m = new Material(Shader.Find("Unlit/Color"));
        m.color = new Color(r, g, b);
        return m;
    }
    private static Material Mat(Color c) { var m = new Material(Shader.Find("Unlit/Color")); m.color = c; return m; }

    private void FindExistingEditorObject()
    {
        currentShapeObject = GameObject.Find(EDITOR_OBJECT_NAME);
        if (currentShapeObject == null) return;
        dataHolder = currentShapeObject.GetComponent<CubeShapeDataHolder>();
        if (dataHolder == null) return;
        gridSize  = dataHolder.gridSize;
        cellSize  = dataHolder.cellSize;
        spacing   = dataHolder.spacing;
        shapeName = dataHolder.shapeName;
        isEditing = true;
    }

    private void FocusCamera()
        => previewDistance = Mathf.Max(gridSize.x, gridSize.y, gridSize.z) * 2.5f;

    private void InitializeStyles()
    {
        if (headerStyle != null) return;
        headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        headerStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);
        sidebarStyle    = new GUIStyle(GUI.skin.box);
        toolButtonStyle = new GUIStyle(GUI.skin.button) { fixedHeight = 35, fontSize = 12 };
    }

    // ─── OnGUI ────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        InitializeStyles();
        EnsureMaterials();
        EditorGUILayout.BeginHorizontal();
        try { DrawLeftSidebar(); DrawCenterPanel(); DrawRightSidebar(); }
        finally { EditorGUILayout.EndHorizontal(); }
        DrawStatusBar();
    }

    // ─── Left Sidebar ─────────────────────────────────────────────────────────

    private void DrawLeftSidebar()
    {
        EditorGUILayout.BeginVertical(sidebarStyle, GUILayout.Width(260), GUILayout.ExpandHeight(true));
        sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);

        // Shape Settings
        EditorGUILayout.LabelField("SHAPE SETTINGS", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        shapeName = EditorGUILayout.TextField("Name", shapeName);
        shapeType = (ShapeType)EditorGUILayout.EnumPopup("Shape Type", shapeType);
        gridSize  = EditorGUILayout.Vector3IntField("Grid Size", gridSize);
        cellSize  = EditorGUILayout.FloatField("Cell Size", cellSize);
        EditorGUI.BeginChangeCheck();
        spacing = EditorGUILayout.Slider("Gap (Spacing)", spacing, 0f, 0.5f);
        if (EditorGUI.EndChangeCheck() && dataHolder != null) dataHolder.spacing = spacing;
        gridOrigin = (GridOrigin)EditorGUILayout.EnumPopup("Origin", gridOrigin);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);
        EditorGUILayout.EndVertical();

        // Tools
        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("TOOLS", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUI.BeginDisabledGroup(pieceAssignmentMode);
        GUI.backgroundColor = currentTool == ToolMode.Add ? Color.green : Color.white;
        if (GUILayout.Button("ADD CUBE MODE", toolButtonStyle)) currentTool = ToolMode.Add;
        GUI.backgroundColor = currentTool == ToolMode.Remove ? Color.red : Color.white;
        if (GUILayout.Button("REMOVE CUBE MODE", toolButtonStyle)) currentTool = ToolMode.Remove;
        GUI.backgroundColor = Color.white;
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("FILL ALL"))  FillAll();
        if (GUILayout.Button("CLEAR ALL")) ClearShape();
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();

        // View Options
        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("VIEW OPTIONS", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        ySlice = EditorGUILayout.IntSlider("Y-Slice Filter", ySlice, -1, gridSize.y - 1);
        useOrthographic = EditorGUILayout.Toggle("Orthographic", useOrthographic);
        if (GUILayout.Button("Focus Viewport")) FocusCamera();
        EditorGUILayout.EndVertical();

        // Piece Assignment (only for MainShape)
        if (isEditing && shapeType == ShapeType.MainShape)
        {
            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("PIECE ASSIGNMENT", headerStyle);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            pieceAssignmentMode = EditorGUILayout.Toggle("Enable", pieceAssignmentMode);

            if (pieceAssignmentMode)
            {
                EditorGUILayout.LabelField("Level Adı:", EditorStyles.miniLabel);
                levelName = EditorGUILayout.TextField(levelName);
                EditorGUILayout.Space(4);
                DrawPieceList();

                EditorGUILayout.Space(6);
                int total    = dataHolder?.occupiedCells.Count ?? 0;
                int assigned = cellPieceMap.Values.Count(v => v >= 0);
                bool allAssigned        = assigned == total && total > 0;
                bool allPiecesHaveCells = Enumerable.Range(0, pieceCount).All(i => cellPieceMap.Values.Any(v => v == i));

                if (!allAssigned)
                    EditorGUILayout.HelpBox($"{total - assigned} küp atanmadı.", MessageType.Warning);
                else if (!allPiecesHaveCells)
                    EditorGUILayout.HelpBox("Boş parça var.", MessageType.Warning);

                EditorGUI.BeginDisabledGroup(!allAssigned || !allPiecesHaveCells);
                GUI.backgroundColor = new Color(0.4f, 1f, 0.5f, 0.9f);
                if (GUILayout.Button("EXPORT LEVEL", GUILayout.Height(36))) ExportLevel();
                GUI.backgroundColor = Color.white;
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndVertical();
        }

        // Save button
        EditorGUILayout.Space(25);
        GUI.backgroundColor = shapeType == ShapeType.Piece
            ? new Color(0.5f, 1f, 0.4f, 0.9f)
            : new Color(0.4f, 0.8f, 1f, 0.8f);
        string saveLabel = shapeType == ShapeType.Piece ? "SAVE AS PIECE" : "SAVE AS PREFAB";
        if (GUILayout.Button(saveLabel, GUILayout.Height(45))) SaveAsPrefab();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawPieceList()
    {
        for (int i = 0; i < pieceCount; i++)
        {
            int count = cellPieceMap.Values.Count(v => v == i);
            Color c   = PIECE_COLORS[i % PIECE_COLORS.Length];
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = activePiece == i ? c : c * 0.55f;
            var btn = new GUIStyle(GUI.skin.button)
            {
                fontStyle = activePiece == i ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft
            };
            if (GUILayout.Button($"  Parça {i + 1}  [{count} küp]", btn)) activePiece = i;
            GUI.backgroundColor = Color.white;
            if (i == pieceCount - 1 && pieceCount > 2)
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    foreach (var k in cellPieceMap.Keys.Where(k => cellPieceMap[k] == i).ToList())
                        cellPieceMap[k] = -1;
                    pieceCount--;
                    if (activePiece >= pieceCount) activePiece = pieceCount - 1;
                }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space(2);
        if (pieceCount < PIECE_COLORS.Length)
        {
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f, 0.7f);
            if (GUILayout.Button("+ Parça Ekle")) pieceCount++;
            GUI.backgroundColor = Color.white;
        }
    }

    // ─── Center Panel ─────────────────────────────────────────────────────────

    private void DrawCenterPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        try
        {
            Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            HandleViewportInput(rect);
            Draw3DPreview(rect);

            GUILayout.Space(-75);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(260));
            EditorGUILayout.LabelField("VIEWPORT STATS", headerStyle);
            int cubeCount = dataHolder?.occupiedCells.Count ?? 0;
            EditorGUILayout.LabelField($"Cubes: {cubeCount}", EditorStyles.miniLabel);
            if (pieceAssignmentMode)
            {
                int asgn = cellPieceMap.Values.Count(v => v >= 0);
                EditorGUILayout.LabelField($"Assigned: {asgn}/{cubeCount}", EditorStyles.miniLabel);
            }
            EditorGUILayout.LabelField($"View: {(useOrthographic ? "Iso" : "Persp")}", EditorStyles.miniLabel);
            string hoverInfo = hoveredCell.HasValue
                ? $"Hover: {hoveredCell.Value}"
                : (placementCell.HasValue ? $"Place: {placementCell.Value}" : "—");
            EditorGUILayout.LabelField(hoverInfo, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);

            if (!isEditing)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.4f, 1f, 0.4f, 0.7f);
                if (GUILayout.Button("START NEW DESIGN", GUILayout.Width(350), GUILayout.Height(70))) CreateNewShape();
                GUI.backgroundColor = Color.white;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
            }
        }
        finally { EditorGUILayout.EndVertical(); }
    }

    // ─── Viewport Input ────────────────────────────────────────────────────────

    private void HandleViewportInput(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDrag && e.button == 1) { previewDir += e.delta * 0.5f; e.Use(); Repaint(); }
        if (e.type == EventType.ScrollWheel) { previewDistance = Mathf.Clamp(previewDistance + e.delta.y * 0.5f, 2f, 100f); e.Use(); Repaint(); }

        Vector2 normPos = new Vector2(
            (e.mousePosition.x - rect.x) / rect.width,
            1f - (e.mousePosition.y - rect.y) / rect.height);
        Ray ray = previewUtility.camera.ViewportPointToRay(normPos);

        var prevHover = hoveredCell;
        var prevPlace = placementCell;
        UpdateHoverStateLogic(ray, true);
        if (hoveredCell != prevHover || placementCell != prevPlace) Repaint();

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            // Piece assignment click
            if (pieceAssignmentMode && hoveredCell.HasValue)
            {
                cellPieceMap[hoveredCell.Value] = e.shift ? -1 : activePiece;
                Repaint(); e.Use(); return;
            }

            if (e.shift || currentTool == ToolMode.Remove)
            { if (hoveredCell.HasValue) RemoveCube(hoveredCell.Value); }
            else if (currentTool == ToolMode.Add)
            { if (placementCell.HasValue) AddCube(placementCell.Value); }
            e.Use();
        }
    }

    private void UpdateHoverStateLogic(Ray ray, bool isViewport)
    {
        hoveredCell   = null;
        placementCell = null;
        if (dataHolder == null) return;

        float step     = cellSize + spacing;
        Vector3 origin = isViewport ? -(Vector3)gridSize * step * 0.5f : GetOriginOffset();
        float bestDist = float.MaxValue;

        foreach (var cell in dataHolder.occupiedCells)
        {
            Vector3 center = origin + (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
            if (!new Bounds(center, Vector3.one * cellSize).IntersectRay(ray, out float d) || d >= bestDist) continue;
            bestDist    = d;
            hoveredCell = cell;

            Vector3 hitLocal = ray.GetPoint(d) - center;
            float ax = Mathf.Abs(hitLocal.x), ay = Mathf.Abs(hitLocal.y), az = Mathf.Abs(hitLocal.z);
            Vector3Int normal = Vector3Int.zero;
            if      (ax >= ay && ax >= az) normal.x = (int)Mathf.Sign(hitLocal.x);
            else if (ay >= az)             normal.y = (int)Mathf.Sign(hitLocal.y);
            else                           normal.z = (int)Mathf.Sign(hitLocal.z);

            Vector3Int neighbor = cell + normal;
            placementCell = IsWithinGrid(neighbor) ? neighbor : (Vector3Int?)null;
        }

        if (hoveredCell.HasValue) return;

        float targetY = (ySlice == -1) ? 0 : (isViewport ? (ySlice - gridSize.y * 0.5f) : ySlice) * step;
        Plane ground  = new Plane(Vector3.up, isViewport ? new Vector3(0, targetY, 0) : origin + Vector3.up * targetY);
        if (!ground.Raycast(ray, out float enter)) return;

        Vector3 localPos    = (ray.GetPoint(enter) - origin) / step;
        Vector3Int floorCell = new Vector3Int(Mathf.FloorToInt(localPos.x), ySlice == -1 ? 0 : ySlice, Mathf.FloorToInt(localPos.z));
        if (!IsWithinGrid(floorCell)) return;
        if (dataHolder.occupiedCells.Contains(floorCell)) hoveredCell = floorCell;
        else placementCell = floorCell;
    }

    // ─── 3D Preview ───────────────────────────────────────────────────────────

    private void Draw3DPreview(Rect rect)
    {
        if (previewUtility == null) return;
        if (inPreviewBlock) { try { previewUtility.EndPreview(); } catch { } inPreviewBlock = false; }

        previewUtility.BeginPreview(rect, GUIStyle.none);
        inPreviewBlock = true;
        try
        {
            Quaternion camRot = Quaternion.Euler(previewDir.y, previewDir.x, 0);
            previewUtility.camera.orthographic      = useOrthographic;
            if (useOrthographic) previewUtility.camera.orthographicSize = previewDistance * 0.4f;
            previewUtility.camera.transform.position = camRot * (Vector3.back * previewDistance);
            previewUtility.camera.transform.rotation = camRot;
            previewUtility.camera.backgroundColor   = new Color(0.12f, 0.12f, 0.12f, 1f);
            previewUtility.camera.clearFlags        = CameraClearFlags.Color;
            previewUtility.lights[0].intensity      = 0f;
            previewUtility.lights[1].intensity      = 0f;

            DrawPreviewGridHUD();

            if (dataHolder != null)
            {
                float step      = cellSize + spacing;
                Vector3 offset  = -(Vector3)gridSize * step * 0.5f;
                float cubeScale = cellSize * 0.82f;

                foreach (var cell in dataHolder.occupiedCells)
                {
                    Vector3 pos = offset + (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);

                    Material mat;
                    if (pieceAssignmentMode)
                    {
                        int p = cellPieceMap.TryGetValue(cell, out int pv) ? pv : -1;
                        mat = p >= 0 ? pieceMaterials[p % PIECE_COLORS.Length] : unassignedMaterial;
                    }
                    else mat = cubeMaterial;

                    previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * cubeScale), mat, 0);
                    DrawCubeEdges(pos, cubeScale);
                }

                if (hoveredCell.HasValue)
                {
                    Vector3 pos = offset + (Vector3)hoveredCell.Value * step + Vector3.one * (cellSize * 0.5f);
                    Material hm = (pieceAssignmentMode || !e_isRemoveActive()) ? hoveredMaterial : removeMaterial;
                    previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * (cubeScale + 0.06f)), hm, 0);
                }

                if (placementCell.HasValue && currentTool == ToolMode.Add && !pieceAssignmentMode)
                {
                    Vector3 pos = offset + (Vector3)placementCell.Value * step + Vector3.one * (cellSize * 0.5f);
                    previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * cubeScale), hoverMaterial, 0);
                }
            }

            DrawAxisGizmoHUD(camRot);
            previewUtility.camera.Render();
            Texture result = previewUtility.EndPreview();
            inPreviewBlock = false;
            GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
            DrawOrientationOverlay(rect, camRot);
        }
        catch (System.Exception ex)
        {
            if (inPreviewBlock) { previewUtility.EndPreview(); inPreviewBlock = false; }
            Debug.LogException(ex);
        }
    }

    private bool e_isRemoveActive() => currentTool == ToolMode.Remove || (Event.current != null && Event.current.shift);

    private void DrawCubeEdges(Vector3 center, float size)
    {
        float h = size * 0.5f;
        float t = Mathf.Max(0.012f, cellSize * 0.04f);
        gizmoMaterial.color = new Color(0.08f, 0.08f, 0.1f);
        DrawEdge(center + new Vector3(0,  -h, -h), new Vector3(size, t, t));
        DrawEdge(center + new Vector3(0,  -h,  h), new Vector3(size, t, t));
        DrawEdge(center + new Vector3(-h, -h,  0), new Vector3(t, t, size));
        DrawEdge(center + new Vector3( h, -h,  0), new Vector3(t, t, size));
        DrawEdge(center + new Vector3(0,   h, -h), new Vector3(size, t, t));
        DrawEdge(center + new Vector3(0,   h,  h), new Vector3(size, t, t));
        DrawEdge(center + new Vector3(-h,  h,  0), new Vector3(t, t, size));
        DrawEdge(center + new Vector3( h,  h,  0), new Vector3(t, t, size));
        DrawEdge(center + new Vector3(-h, 0, -h), new Vector3(t, size, t));
        DrawEdge(center + new Vector3( h, 0, -h), new Vector3(t, size, t));
        DrawEdge(center + new Vector3(-h, 0,  h), new Vector3(t, size, t));
        DrawEdge(center + new Vector3( h, 0,  h), new Vector3(t, size, t));
    }

    private void DrawEdge(Vector3 pos, Vector3 scale)
        => previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, scale), gizmoMaterial, 0);

    private void DrawAxisGizmoHUD(Quaternion camRot)
    {
        float s = 0.15f;
        Vector3 pos = previewUtility.camera.transform.position
            + previewUtility.camera.transform.forward * 2f
            + previewUtility.camera.transform.right   * 1.3f
            + previewUtility.camera.transform.up      * 0.9f;
        DrawGizmoLine(Vector3.right,   Color.red,                 camRot, pos, s);
        DrawGizmoLine(Vector3.up,      Color.green,               camRot, pos, s);
        DrawGizmoLine(Vector3.forward, new Color(0.4f, 0.4f, 1f), camRot, pos, s);
    }

    private void DrawGizmoLine(Vector3 dir, Color col, Quaternion camRot, Vector3 pos, float s)
    {
        gizmoMaterial.color = col;
        previewUtility.DrawMesh(cubeMesh,
            Matrix4x4.TRS(pos + (camRot * dir) * s * 0.5f, camRot * Quaternion.LookRotation(dir), new Vector3(0.015f, 0.015f, s)),
            gizmoMaterial, 0);
    }

    private void DrawPreviewGridHUD()
    {
        float step   = cellSize + spacing;
        Vector3 off  = -(Vector3)gridSize * step * 0.5f;
        gizmoMaterial.color = new Color(1, 1, 1, 0.07f);
        for (int x = 0; x <= gridSize.x; x++)
            previewUtility.DrawMesh(cubeMesh,
                Matrix4x4.TRS(off + new Vector3(x * step, 0, gridSize.z * 0.5f * step), Quaternion.identity, new Vector3(0.01f, 0.01f, gridSize.z * step)),
                gizmoMaterial, 0);
        for (int z = 0; z <= gridSize.z; z++)
            previewUtility.DrawMesh(cubeMesh,
                Matrix4x4.TRS(off + new Vector3(gridSize.x * 0.5f * step, 0, z * step), Quaternion.identity, new Vector3(gridSize.x * step, 0.01f, 0.01f)),
                gizmoMaterial, 0);
    }

    private void DrawOrientationOverlay(Rect r, Quaternion rot)
    {
        Vector3 f = rot * Vector3.forward;
        string label;
        if      (Vector3.Dot(f, Vector3.forward) > 0.8f) label = "Front";
        else if (Vector3.Dot(f, Vector3.back)    > 0.8f) label = "Back";
        else if (Vector3.Dot(f, Vector3.up)      > 0.8f) label = "Bottom";
        else if (Vector3.Dot(f, Vector3.down)    > 0.8f) label = "Top";
        else if (Vector3.Dot(f, Vector3.left)    > 0.8f) label = "Right";
        else if (Vector3.Dot(f, Vector3.right)   > 0.8f) label = "Left";
        else label = useOrthographic ? "Isometric" : "Perspective";
        GUI.Label(new Rect(r.x + r.width - 100, r.y + r.height - 30, 90, 20), label,
            new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1,1,1,0.5f) } });
    }

    // ─── Right Sidebar ─────────────────────────────────────────────────────────

    private void DrawRightSidebar()
    {
        EditorGUILayout.BeginVertical(sidebarStyle, GUILayout.Width(210), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("SHAPE LIBRARY", headerStyle);
        libraryScroll = EditorGUILayout.BeginScrollView(libraryScroll);

        EditorGUILayout.LabelField("Main Shapes", EditorStyles.miniBoldLabel);
        DrawLibrarySection(GENERATED_PATH);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Pieces", EditorStyles.miniBoldLabel);

        if (pieceAssignmentMode && isEditing && shapeType == ShapeType.MainShape)
            DrawCurrentPiecePreviews();
        else
            DrawLibrarySection(PIECES_PATH);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawLibrarySection(string folder)
    {
        if (!Directory.Exists(folder)) { EditorGUILayout.LabelField("—", EditorStyles.centeredGreyMiniLabel); return; }
        bool any = false;
        foreach (var p in Directory.GetFiles(folder, "*.asset"))
        {
            if (GUILayout.Button(Path.GetFileNameWithoutExtension(p), EditorStyles.miniButton))
                LoadShapeFromLibrary(p.Replace('\\', '/'));
            any = true;
        }
        if (!any) EditorGUILayout.LabelField("—", EditorStyles.centeredGreyMiniLabel);
    }

    private void DrawCurrentPiecePreviews()
    {
        for (int i = 0; i < pieceCount; i++)
        {
            var cells = cellPieceMap.Where(kv => kv.Value == i).Select(kv => kv.Key).ToList();
            Color pc = PIECE_COLORS[i % PIECE_COLORS.Length];

            var labelStyle = new GUIStyle(EditorStyles.miniBoldLabel);
            labelStyle.normal.textColor = pc;
            EditorGUILayout.LabelField($"Parça {i + 1}  [{cells.Count} küp]", labelStyle);

            if (cells.Count == 0)
            {
                EditorGUILayout.LabelField("boş", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.Space(4);
                continue;
            }

            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(54), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                DrawPieceGrid2D(r, cells, pc);

            EditorGUILayout.Space(6);
        }
    }

    private void DrawPieceGrid2D(Rect rect, List<Vector3Int> cells, Color pieceColor)
    {
        int minX = cells.Min(c => c.x), maxX = cells.Max(c => c.x);
        int minZ = cells.Min(c => c.z), maxZ = cells.Max(c => c.z);
        int w = maxX - minX + 1;
        int d = maxZ - minZ + 1;

        float pad    = 3f;
        float cellPx = Mathf.Min((rect.width - pad * 2) / w, (rect.height - pad * 2) / d, 20f);
        float totalW = cellPx * w;
        float totalH = cellPx * d;
        float ox = rect.x + (rect.width  - totalW) * 0.5f;
        float oy = rect.y + (rect.height - totalH) * 0.5f;

        EditorGUI.DrawRect(rect, new Color(0.09f, 0.09f, 0.12f));

        for (int x = 0; x <= w; x++)
            EditorGUI.DrawRect(new Rect(ox + x * cellPx, oy, 1, totalH), new Color(0.22f, 0.22f, 0.25f));
        for (int z = 0; z <= d; z++)
            EditorGUI.DrawRect(new Rect(ox, oy + z * cellPx, totalW, 1), new Color(0.22f, 0.22f, 0.25f));

        int minY   = cells.Min(c => c.y);
        int yRange = Mathf.Max(1, cells.Max(c => c.y) - minY);

        foreach (var grp in cells.GroupBy(c => (c.x - minX, c.z - minZ)))
        {
            int   topY       = grp.Max(c => c.y);
            float brightness = 0.55f + 0.45f * (float)(topY - minY) / yRange;
            Color col        = new Color(pieceColor.r * brightness, pieceColor.g * brightness, pieceColor.b * brightness);
            float cx = ox + grp.Key.Item1 * cellPx + 1.5f;
            float cy = oy + grp.Key.Item2 * cellPx + 1.5f;
            EditorGUI.DrawRect(new Rect(cx, cy, cellPx - 3, cellPx - 3), col);
        }
    }

    // ─── Status Bar ────────────────────────────────────────────────────────────

    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(25));
        string modeStr = pieceAssignmentMode ? $"PIECE ASSIGN — Parça {activePiece + 1}" : $"EDITING — {currentTool}";
        EditorGUILayout.LabelField($"Status: {(isEditing ? modeStr : "IDLE")}  |  Type: {shapeType}", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("BlockMerge 3D Builder Pro v3.0", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ─── Scene GUI ─────────────────────────────────────────────────────────────

    private void OnSceneGUI(SceneView sv)
    {
        if (!isEditing || currentShapeObject == null) return;
        DrawSceneGrid();
        HandleSceneInteraction();
        sv.Repaint();
    }

    private void HandleSceneInteraction()
    {
        Event e = Event.current;
        Ray r   = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        UpdateHoverStateLogic(r, false);
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if (e.type != EventType.MouseDown) return;

        if (pieceAssignmentMode && e.button == 0 && hoveredCell.HasValue)
        {
            cellPieceMap[hoveredCell.Value] = e.shift ? -1 : activePiece;
            e.Use(); return;
        }

        if (e.button == 0 && !e.shift)
        {
            if (currentTool == ToolMode.Add    && placementCell.HasValue) AddCube(placementCell.Value);
            else if (currentTool == ToolMode.Remove && hoveredCell.HasValue)  RemoveCube(hoveredCell.Value);
            e.Use();
        }
        else if (e.button == 1 || (e.button == 0 && e.shift))
        {
            if (hoveredCell.HasValue) RemoveCube(hoveredCell.Value);
            e.Use();
        }
    }

    private void DrawSceneGrid()
    {
        float s   = cellSize + spacing;
        Vector3 o = GetOriginOffset();
        Handles.color = new Color(1, 1, 1, 0.1f);
        for (int x = 0; x <= gridSize.x; x++)
            for (int y = 0; y <= gridSize.y; y++)
                if (ySlice == -1 || y == ySlice || y == ySlice + 1)
                    Handles.DrawLine(o + new Vector3(x * s, y * s, 0), o + new Vector3(x * s, y * s, gridSize.z * s));

        if (!pieceAssignmentMode && placementCell.HasValue && currentTool == ToolMode.Add)
        {
            Handles.color = new Color(0.2f, 0.9f, 1f, 0.5f);
            Handles.CubeHandleCap(0, o + (Vector3)placementCell.Value * s + Vector3.one * (cellSize * 0.5f), Quaternion.identity, cellSize, EventType.Repaint);
        }
        if (!pieceAssignmentMode && hoveredCell.HasValue && (currentTool == ToolMode.Remove || Event.current.shift))
        {
            Handles.color = new Color(1f, 0.2f, 0.1f, 0.7f);
            Handles.CubeHandleCap(0, o + (Vector3)hoveredCell.Value * s + Vector3.one * (cellSize * 0.5f), Quaternion.identity, cellSize * 1.05f, EventType.Repaint);
        }
    }

    // ─── Shape Ops ─────────────────────────────────────────────────────────────

    private void AddCube(Vector3Int c)
    {
        if (dataHolder.occupiedCells.Contains(c)) return;
        Undo.RegisterCompleteObjectUndo(dataHolder, "Add Cube");
        GameObject cube = cubePrefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(currentShapeObject.transform);
        cube.transform.position   = GetOriginOffset() + (Vector3)c * (cellSize + spacing) + Vector3.one * (cellSize * 0.5f);
        cube.transform.localScale = Vector3.one * cellSize;
        cube.name = $"Cube_{c.x}_{c.y}_{c.z}";
        dataHolder.occupiedCells.Add(c);
        cellPieceMap[c] = -1;
        Undo.RegisterCreatedObjectUndo(cube, "Add Cube");
    }

    private void RemoveCube(Vector3Int c)
    {
        if (!dataHolder.occupiedCells.Contains(c)) return;
        Undo.RegisterCompleteObjectUndo(dataHolder, "Remove Cube");
        Transform t = currentShapeObject.transform.Find($"Cube_{c.x}_{c.y}_{c.z}");
        if (t != null) { Undo.DestroyObjectImmediate(t.gameObject); dataHolder.occupiedCells.Remove(c); cellPieceMap.Remove(c); }
    }

    private void FillAll()
    {
        if (currentShapeObject == null) return;
        for (int x = 0; x < gridSize.x; x++)
            for (int y = 0; y < gridSize.y; y++)
                for (int z = 0; z < gridSize.z; z++)
                    AddCube(new Vector3Int(x, y, z));
    }

    private void ClearShape()
    {
        if (currentShapeObject == null) return;
        for (int i = currentShapeObject.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(currentShapeObject.transform.GetChild(i).gameObject);
        dataHolder.occupiedCells.Clear();
        cellPieceMap.Clear();
    }

    private void CreateNewShape()
    {
        if (currentShapeObject != null) DestroyImmediate(currentShapeObject);
        currentShapeObject          = new GameObject(EDITOR_OBJECT_NAME);
        dataHolder                  = currentShapeObject.AddComponent<CubeShapeDataHolder>();
        dataHolder.gridSize         = gridSize;
        dataHolder.cellSize         = cellSize;
        dataHolder.spacing          = spacing;
        dataHolder.shapeName        = shapeName;
        cellPieceMap.Clear();
        pieceAssignmentMode = false;
        isEditing           = true;
        Selection.activeGameObject  = currentShapeObject;
        FocusCamera();
    }

    // ─── Library ───────────────────────────────────────────────────────────────

    private void LoadShapeFromLibrary(string assetPath)
    {
        CubeShapeData data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(assetPath);
        if (data == null) return;

        shapeType  = assetPath.Contains(PIECES_PATH) ? ShapeType.Piece : ShapeType.MainShape;
        shapeName  = data.shapeName;
        gridSize   = data.gridSize;
        cellSize   = data.cellSize;
        spacing    = data.spacing;
        cubePrefab = data.cubePrefab;

        CreateNewShape();
        foreach (var cell in data.occupiedCells) AddCube(cell);

        if (shapeType == ShapeType.MainShape)
            TryLoadPieceAssignments(data);
    }

    private void TryLoadPieceAssignments(CubeShapeData shapeData)
    {
        string[] guids = AssetDatabase.FindAssets("t:LevelData");
        foreach (var guid in guids)
        {
            var ld = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
            if (ld?.mainShapePrefab == null) continue;
            var holder = ld.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
            if (holder == null || holder.shapeName != shapeData.shapeName) continue;

            levelName  = ld.levelName;
            pieceCount = Mathf.Max(2, ld.complementaryPieces.Count);

            for (int i = 0; i < ld.complementaryPieces.Count; i++)
            {
                var ph = ld.complementaryPieces[i]?.GetComponent<CubeShapeDataHolder>();
                if (ph == null) continue;
                foreach (var cell in ph.occupiedCells)
                    if (cellPieceMap.ContainsKey(cell))
                        cellPieceMap[cell] = i;
            }
            pieceAssignmentMode = true;
            Repaint();
            break;
        }
    }

    // ─── Save ──────────────────────────────────────────────────────────────────

    private void SaveAsPrefab()
    {
        if (currentShapeObject == null || dataHolder.occupiedCells.Count == 0) return;
        string folder = shapeType == ShapeType.Piece ? PIECES_PATH : GENERATED_PATH;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        string path = EditorUtility.SaveFilePanelInProject(
            shapeType == ShapeType.Piece ? "Save Piece" : "Save Prefab", shapeName, "prefab", "", folder);
        if (string.IsNullOrEmpty(path)) return;

        List<Vector3Int> cells = new List<Vector3Int>(dataHolder.occupiedCells);
        if (shapeType == ShapeType.Piece && cells.Count > 0)
        {
            int minX = cells.Min(c => c.x), minY = cells.Min(c => c.y), minZ = cells.Min(c => c.z);
            var shift = new Vector3Int(minX, minY, minZ);
            for (int i = 0; i < cells.Count; i++) cells[i] -= shift;
        }

        CubeShapeData asset  = ScriptableObject.CreateInstance<CubeShapeData>();
        asset.shapeName      = shapeName;
        asset.gridSize       = gridSize;
        asset.cellSize       = cellSize;
        asset.spacing        = spacing;
        asset.occupiedCells  = cells;
        asset.cubePrefab     = cubePrefab;
        AssetDatabase.CreateAsset(asset, Path.ChangeExtension(path, ".asset"));

        float step     = cellSize + spacing;
        GameObject root = new GameObject(Path.GetFileNameWithoutExtension(path));
        var h           = root.AddComponent<CubeShapeDataHolder>();
        h.shapeName     = shapeName; h.gridSize = gridSize; h.cellSize = cellSize;
        h.spacing       = spacing;   h.occupiedCells = cells;
        foreach (var cell in cells)
        {
            GameObject cube = cubePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
            cube.transform.localScale    = Vector3.one * cellSize;
            cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
        }
        PrefabUtility.SaveAsPrefabAsset(root, path);
        DestroyImmediate(root);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Kaydedildi!",
            $"{(shapeType == ShapeType.Piece ? "Piece" : "Main Shape")} → {path}", "OK");
    }

    // ─── Export Level ──────────────────────────────────────────────────────────

    private void ExportLevel()
    {
        if (dataHolder == null || dataHolder.occupiedCells.Count == 0) return;
        if (string.IsNullOrEmpty(levelName)) levelName = shapeName + "_Level";
        string levelDir = $"{LEVELS_PATH}/{levelName}";
        if (!Directory.Exists(levelDir)) Directory.CreateDirectory(levelDir);

        float step = cellSize + spacing;
        var piecePrefabs = new List<GameObject>();

        for (int i = 0; i < pieceCount; i++)
        {
            List<Vector3Int> cells = cellPieceMap.Where(kv => kv.Value == i).Select(kv => kv.Key).ToList();
            GameObject pRoot   = new GameObject($"{levelName}_Piece_{i + 1}");
            var ph             = pRoot.AddComponent<CubeShapeDataHolder>();
            ph.shapeName       = $"{levelName}_Piece_{i + 1}";
            ph.gridSize        = gridSize; ph.cellSize = cellSize; ph.spacing = spacing;
            ph.occupiedCells   = new List<Vector3Int>(cells);
            foreach (var cell in cells)
            {
                GameObject cube = cubePrefab != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(pRoot.transform);
                cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
                cube.transform.localScale    = Vector3.one * cellSize;
                cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            }
            piecePrefabs.Add(PrefabUtility.SaveAsPrefabAsset(pRoot, $"{levelDir}/{levelName}_Piece_{i + 1}.prefab"));
            DestroyImmediate(pRoot);
        }

        GameObject fullRoot  = new GameObject($"{levelName}_FullShape");
        var fh               = fullRoot.AddComponent<CubeShapeDataHolder>();
        fh.shapeName         = levelName; fh.gridSize = gridSize; fh.cellSize = cellSize;
        fh.spacing           = spacing;  fh.occupiedCells = new List<Vector3Int>(dataHolder.occupiedCells);
        foreach (var cell in dataHolder.occupiedCells)
        {
            GameObject cube = cubePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(fullRoot.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
            cube.transform.localScale    = Vector3.one * cellSize;
            cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
        }
        GameObject savedFull = PrefabUtility.SaveAsPrefabAsset(fullRoot, $"{levelDir}/{levelName}_FullShape.prefab");
        DestroyImmediate(fullRoot);

        LevelData ld              = ScriptableObject.CreateInstance<LevelData>();
        ld.levelName              = levelName;
        ld.mainShapePrefab        = savedFull;
        ld.complementaryPieces    = piecePrefabs;
        AssetDatabase.CreateAsset(ld, $"{levelDir}/{levelName}_LevelData.asset");
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Export Tamamlandı!",
            $"{pieceCount} parça  •  1 tam şekil  •  1 LevelData\n\n{levelDir}/", "Tamam");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private bool IsWithinGrid(Vector3Int c)
        => c.x >= 0 && c.x < gridSize.x
        && c.y >= 0 && c.y < gridSize.y
        && c.z >= 0 && c.z < gridSize.z;

    private Vector3 GetOriginOffset()
    {
        if (currentShapeObject == null) return Vector3.zero;
        return gridOrigin == GridOrigin.Center
            ? currentShapeObject.transform.position - (Vector3)gridSize * cellSize * 0.5f
            : currentShapeObject.transform.position;
    }
}
