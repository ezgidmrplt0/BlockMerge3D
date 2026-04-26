using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class CubeShapeEditorWindow : EditorWindow
{
    private enum GridOrigin { BottomLeft, Center }
    private enum ToolMode { Add, Remove }

    // --- State Variables ---
    private Vector3Int gridSize = new Vector3Int(5, 5, 5);
    private float cellSize = 1.0f;
    private float spacing = 0.1f;
    private int ySlice = -1;
    private GridOrigin gridOrigin = GridOrigin.BottomLeft;
    private ToolMode currentTool = ToolMode.Add;
    private string shapeName = "NewCubeShape";
    private bool useOrthographic = true;

    private GameObject cubePrefab;
    private GameObject currentShapeObject;
    private CubeShapeDataHolder dataHolder;

    private Vector3Int? hoveredCell = null;
    private Vector3Int? placementCell = null;
    private bool isEditing = false;
    private bool inPreviewBlock = false; // Çizim durumunu takip et

    // --- UI Styling ---
    private GUIStyle headerStyle;
    private GUIStyle sidebarStyle;
    private GUIStyle toolButtonStyle;
    private Vector2 sidebarScroll;
    private Vector2 libraryScroll;

    // --- 3D Preview Rendering ---
    private PreviewRenderUtility previewUtility;
    private Vector2 previewDir = new Vector2(135, -30);
    private float previewDistance = 15f;
    private Mesh cubeMesh;
    private Material cubeMaterial;
    private Material gizmoMaterial;
    private Material hoverMaterial;    // placement ghost (cyan)
    private Material hoveredMaterial;  // hovered occupied cube in ADD mode (yellow)
    private Material removeMaterial;   // hovered occupied cube in REMOVE mode (red)

    private const string GENERATED_PATH = "Assets/Shapes";
    private const string EDITOR_OBJECT_NAME = "Shape_Editor_Object";

    [MenuItem("BlockMerge3D/Cube Shape Builder Pro")]
    public static void ShowWindow()
    {
        var window = GetWindow<CubeShapeEditorWindow>("Cube Shape Builder");
        window.minSize = new Vector2(900, 600);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        previewUtility = new PreviewRenderUtility();
        previewUtility.camera.fieldOfView = 30f;
        previewUtility.camera.farClipPlane = 1000;
        previewUtility.camera.nearClipPlane = 0.1f;

        cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        cubeMaterial = new Material(Shader.Find("Unlit/Color"));
        cubeMaterial.color = new Color(0.62f, 0.66f, 0.72f);

        gizmoMaterial = new Material(Shader.Find("Unlit/Color"));

        hoverMaterial = new Material(Shader.Find("Unlit/Color"));
        hoverMaterial.color = new Color(0.2f, 0.9f, 1f, 0.6f);

        hoveredMaterial = new Material(Shader.Find("Unlit/Color"));
        hoveredMaterial.color = new Color(1f, 0.9f, 0.1f, 0.8f);

        removeMaterial = new Material(Shader.Find("Unlit/Color"));
        removeMaterial.color = new Color(1f, 0.2f, 0.1f, 0.85f);

        FindExistingEditorObject();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        if (previewUtility != null)
        {
            // Eğer çizim bloğu açık kalmışsa zorla kapat
            if (inPreviewBlock)
            {
                try { previewUtility.EndPreview(); } catch { }
                inPreviewBlock = false;
            }
            previewUtility.Cleanup();
        }
        
        // Tüm oluşturulan materyalleri yok et
        if (cubeMaterial != null) DestroyImmediate(cubeMaterial);
        if (gizmoMaterial != null) DestroyImmediate(gizmoMaterial);
        if (hoverMaterial != null) DestroyImmediate(hoverMaterial);
        if (hoveredMaterial != null) DestroyImmediate(hoveredMaterial);
        if (removeMaterial != null) DestroyImmediate(removeMaterial);
    }

    private void EnsureMaterials()
    {
        if (cubeMaterial == null) { cubeMaterial = new Material(Shader.Find("Unlit/Color")); cubeMaterial.color = new Color(0.62f, 0.66f, 0.72f); }
        if (gizmoMaterial == null) gizmoMaterial = new Material(Shader.Find("Unlit/Color"));
        if (hoverMaterial == null) { hoverMaterial = new Material(Shader.Find("Unlit/Color")); hoverMaterial.color = new Color(0.2f, 0.9f, 1f, 0.6f); }
        if (hoveredMaterial == null) { hoveredMaterial = new Material(Shader.Find("Unlit/Color")); hoveredMaterial.color = new Color(1f, 0.9f, 0.1f, 0.8f); }
        if (removeMaterial == null) { removeMaterial = new Material(Shader.Find("Unlit/Color")); removeMaterial.color = new Color(1f, 0.2f, 0.1f, 0.85f); }
    }

    private void FindExistingEditorObject()
    {
        currentShapeObject = GameObject.Find(EDITOR_OBJECT_NAME);
        if (currentShapeObject == null) return;
        dataHolder = currentShapeObject.GetComponent<CubeShapeDataHolder>();
        if (dataHolder == null) return;
        gridSize = dataHolder.gridSize;
        cellSize = dataHolder.cellSize;
        spacing = dataHolder.spacing;
        shapeName = dataHolder.shapeName;
        isEditing = true;
    }

    private void FocusCamera()
    {
        previewDistance = Mathf.Max(gridSize.x, gridSize.y, gridSize.z) * 2.5f;
    }

    private void InitializeStyles()
    {
        if (headerStyle != null) return;
        headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        headerStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);
        sidebarStyle = new GUIStyle(GUI.skin.box);
        toolButtonStyle = new GUIStyle(GUI.skin.button) { fixedHeight = 35, fontSize = 12 };
    }

    private void OnGUI()
    {
        InitializeStyles();
        EnsureMaterials(); // Materyallerin geçerli olduğundan emin ol

        EditorGUILayout.BeginHorizontal();
        try
        {
            DrawLeftSidebar();
            DrawCenterPanel();
            DrawRightSidebar();
        }
        finally
        {
            EditorGUILayout.EndHorizontal();
        }
        DrawStatusBar();
    }

    private void DrawLeftSidebar()
    {
        EditorGUILayout.BeginVertical(sidebarStyle, GUILayout.Width(260), GUILayout.ExpandHeight(true));
        sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);

        EditorGUILayout.LabelField("SHAPE SETTINGS", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        shapeName = EditorGUILayout.TextField("Name", shapeName);
        gridSize = EditorGUILayout.Vector3IntField("Grid Size", gridSize);
        cellSize = EditorGUILayout.FloatField("Cell Size", cellSize);

        EditorGUI.BeginChangeCheck();
        spacing = EditorGUILayout.Slider("Gap (Spacing)", spacing, 0f, 0.5f);
        if (EditorGUI.EndChangeCheck() && dataHolder != null)
            dataHolder.spacing = spacing;

        gridOrigin = (GridOrigin)EditorGUILayout.EnumPopup("Origin", gridOrigin);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Cube Prefab", cubePrefab, typeof(GameObject), false);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("TOOLS", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = currentTool == ToolMode.Add ? Color.green : Color.white;
        if (GUILayout.Button("ADD CUBE MODE", toolButtonStyle)) currentTool = ToolMode.Add;
        GUI.backgroundColor = currentTool == ToolMode.Remove ? Color.red : Color.white;
        if (GUILayout.Button("REMOVE CUBE MODE", toolButtonStyle)) currentTool = ToolMode.Remove;
        GUI.backgroundColor = Color.white;
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("FILL ALL")) FillAll();
        if (GUILayout.Button("CLEAR ALL")) ClearShape();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("VIEW OPTIONS", headerStyle);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        ySlice = EditorGUILayout.IntSlider("Y-Slice Filter", ySlice, -1, gridSize.y - 1);
        useOrthographic = EditorGUILayout.Toggle("Orthographic", useOrthographic);
        if (GUILayout.Button("Focus Viewport")) FocusCamera();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(25);
        GUI.backgroundColor = new Color(0.4f, 0.8f, 1f, 0.8f);
        if (GUILayout.Button("SAVE AS PREFAB", GUILayout.Height(45))) SaveAsPrefab();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

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
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(220));
            EditorGUILayout.LabelField("VIEWPORT STATS", headerStyle);
            EditorGUILayout.LabelField($"Cubes: {(dataHolder != null ? dataHolder.occupiedCells.Count : 0)}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"View: {(useOrthographic ? "Iso" : "Persp")}", EditorStyles.miniLabel);
            string hoverInfo = hoveredCell.HasValue ? $"Hover: {hoveredCell.Value}" : (placementCell.HasValue ? $"Place: {placementCell.Value}" : "—");
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
        finally
        {
            EditorGUILayout.EndVertical();
        }
    }

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
            if (e.shift || currentTool == ToolMode.Remove)
            {
                if (hoveredCell.HasValue) RemoveCube(hoveredCell.Value);
            }
            else if (currentTool == ToolMode.Add)
            {
                if (placementCell.HasValue) AddCube(placementCell.Value);
            }
            e.Use();
        }
    }

    private void UpdateHoverStateLogic(Ray ray, bool isViewport)
    {
        hoveredCell = null;
        placementCell = null;
        if (dataHolder == null) return;

        float step = cellSize + spacing;
        Vector3 origin = isViewport ? -(Vector3)gridSize * step * 0.5f : GetOriginOffset();
        float bestDist = float.MaxValue;

        foreach (var cell in dataHolder.occupiedCells)
        {
            Vector3 center = origin + (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
            if (!new Bounds(center, Vector3.one * cellSize).IntersectRay(ray, out float d) || d >= bestDist) continue;

            bestDist = d;
            hoveredCell = cell;

            Vector3 hitLocal = ray.GetPoint(d) - center;
            float ax = Mathf.Abs(hitLocal.x), ay = Mathf.Abs(hitLocal.y), az = Mathf.Abs(hitLocal.z);
            Vector3Int normal = Vector3Int.zero;
            if (ax >= ay && ax >= az) normal.x = (int)Mathf.Sign(hitLocal.x);
            else if (ay >= az) normal.y = (int)Mathf.Sign(hitLocal.y);
            else normal.z = (int)Mathf.Sign(hitLocal.z);

            Vector3Int neighbor = cell + normal;
            placementCell = IsWithinGrid(neighbor) ? neighbor : (Vector3Int?)null;
        }

        if (hoveredCell.HasValue) return;

        float targetY = (ySlice == -1) ? 0 : (isViewport ? (ySlice - gridSize.y * 0.5f) : ySlice) * step;
        Plane ground = new Plane(Vector3.up, isViewport ? new Vector3(0, targetY, 0) : origin + Vector3.up * targetY);
        if (!ground.Raycast(ray, out float enter)) return;

        Vector3 localPos = (ray.GetPoint(enter) - origin) / step;
        Vector3Int floorCell = new Vector3Int(Mathf.FloorToInt(localPos.x), ySlice == -1 ? 0 : ySlice, Mathf.FloorToInt(localPos.z));
        if (!IsWithinGrid(floorCell)) return;
        if (dataHolder.occupiedCells.Contains(floorCell)) hoveredCell = floorCell;
        else placementCell = floorCell;
    }

    private void Draw3DPreview(Rect rect)
    {
        if (previewUtility == null) return;
        
        // Önceki bir hatadan dolayı açık kalmışsa kapat
        if (inPreviewBlock) { try { previewUtility.EndPreview(); } catch { } inPreviewBlock = false; }

        previewUtility.BeginPreview(rect, GUIStyle.none);
        inPreviewBlock = true;

        try
        {
            previewUtility.camera.orthographic = useOrthographic;
            if (useOrthographic) previewUtility.camera.orthographicSize = previewDistance * 0.4f;
            Quaternion camRot = Quaternion.Euler(previewDir.y, previewDir.x, 0);
            previewUtility.camera.transform.position = camRot * (Vector3.back * previewDistance);
            previewUtility.camera.transform.rotation = camRot;
            previewUtility.camera.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
            previewUtility.camera.clearFlags = CameraClearFlags.Color;
            previewUtility.lights[0].intensity = 0f;
            previewUtility.lights[1].intensity = 0f;

            DrawPreviewGridHUD();

            if (dataHolder != null)
            {
                float step = cellSize + spacing;
                Vector3 offset = -(Vector3)gridSize * step * 0.5f;
                float cubeScale = cellSize * 0.82f;

                foreach (var cell in dataHolder.occupiedCells)
                {
                    Vector3 pos = offset + (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
                    previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * cubeScale), cubeMaterial, 0);
                    DrawCubeEdges(pos, cubeScale);
                }

                if (hoveredCell.HasValue)
                {
                    Vector3 pos = offset + (Vector3)hoveredCell.Value * step + Vector3.one * (cellSize * 0.5f);
                    Material hm = (e_isRemoveActive()) ? removeMaterial : hoveredMaterial;
                    previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * (cubeScale + 0.06f)), hm, 0);
                }

                if (placementCell.HasValue && currentTool == ToolMode.Add)
                {
                    Vector3 pos = offset + (Vector3)placementCell.Value * step + Vector3.one * (cellSize * 0.5f);
                    previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * cubeScale), hoverMaterial, 0);
                }
            }

            DrawAxisGizmoHUD(camRot);
            previewUtility.camera.Render();
            
            Texture result = previewUtility.EndPreview();
            inPreviewBlock = false; // Başarıyla kapandı

            GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
            DrawOrientationOverlay(rect, camRot);
        }
        catch (System.Exception ex)
        {
            if (inPreviewBlock)
            {
                previewUtility.EndPreview();
                inPreviewBlock = false;
            }
            Debug.LogException(ex);
        }
    }

    private bool e_isRemoveActive() => currentTool == ToolMode.Remove || (Event.current != null && Event.current.shift);

    private void DrawCubeEdges(Vector3 center, float size)
    {
        float h = size * 0.5f;
        float t = Mathf.Max(0.012f, cellSize * 0.04f);
        gizmoMaterial.color = new Color(0.08f, 0.08f, 0.1f);
        // Bottom ring
        DrawEdge(center + new Vector3(0,  -h, -h), new Vector3(size, t, t));
        DrawEdge(center + new Vector3(0,  -h,  h), new Vector3(size, t, t));
        DrawEdge(center + new Vector3(-h, -h,  0), new Vector3(t, t, size));
        DrawEdge(center + new Vector3( h, -h,  0), new Vector3(t, t, size));
        // Top ring
        DrawEdge(center + new Vector3(0,   h, -h), new Vector3(size, t, t));
        DrawEdge(center + new Vector3(0,   h,  h), new Vector3(size, t, t));
        DrawEdge(center + new Vector3(-h,  h,  0), new Vector3(t, t, size));
        DrawEdge(center + new Vector3( h,  h,  0), new Vector3(t, t, size));
        // Vertical pillars
        DrawEdge(center + new Vector3(-h, 0, -h), new Vector3(t, size, t));
        DrawEdge(center + new Vector3( h, 0, -h), new Vector3(t, size, t));
        DrawEdge(center + new Vector3(-h, 0,  h), new Vector3(t, size, t));
        DrawEdge(center + new Vector3( h, 0,  h), new Vector3(t, size, t));
    }

    private void DrawEdge(Vector3 pos, Vector3 scale)
    {
        previewUtility.DrawMesh(cubeMesh, Matrix4x4.TRS(pos, Quaternion.identity, scale), gizmoMaterial, 0);
    }

    private void DrawAxisGizmoHUD(Quaternion camRot)
    {
        float scale = 0.15f;
        Vector3 pos = previewUtility.camera.transform.position
            + previewUtility.camera.transform.forward * 2f
            + previewUtility.camera.transform.right * 1.3f
            + previewUtility.camera.transform.up * 0.9f;
        DrawGizmoLineHUD(Vector3.right, Color.red, camRot, pos, scale);
        DrawGizmoLineHUD(Vector3.up, Color.green, camRot, pos, scale);
        DrawGizmoLineHUD(Vector3.forward, new Color(0.4f, 0.4f, 1f), camRot, pos, scale);
    }

    private void DrawGizmoLineHUD(Vector3 dir, Color col, Quaternion camRot, Vector3 pos, float s)
    {
        gizmoMaterial.color = col;
        previewUtility.DrawMesh(cubeMesh,
            Matrix4x4.TRS(pos + (camRot * dir) * s * 0.5f, camRot * Quaternion.LookRotation(dir), new Vector3(0.015f, 0.015f, s)),
            gizmoMaterial, 0);
    }

    private void DrawPreviewGridHUD()
    {
        float step = cellSize + spacing;
        Vector3 offset = -(Vector3)gridSize * step * 0.5f;
        gizmoMaterial.color = new Color(1, 1, 1, 0.07f);
        for (int x = 0; x <= gridSize.x; x++)
            previewUtility.DrawMesh(cubeMesh,
                Matrix4x4.TRS(offset + new Vector3(x * step, 0, gridSize.z * 0.5f * step), Quaternion.identity, new Vector3(0.01f, 0.01f, gridSize.z * step)),
                gizmoMaterial, 0);
        for (int z = 0; z <= gridSize.z; z++)
            previewUtility.DrawMesh(cubeMesh,
                Matrix4x4.TRS(offset + new Vector3(gridSize.x * 0.5f * step, 0, z * step), Quaternion.identity, new Vector3(gridSize.x * step, 0.01f, 0.01f)),
                gizmoMaterial, 0);
    }

    private void DrawOrientationOverlay(Rect r, Quaternion rot)
    {
        Vector3 f = rot * Vector3.forward;
        string label;
        if (Vector3.Dot(f, Vector3.forward) > 0.8f) label = "Front";
        else if (Vector3.Dot(f, Vector3.back) > 0.8f) label = "Back";
        else if (Vector3.Dot(f, Vector3.up) > 0.8f) label = "Bottom";
        else if (Vector3.Dot(f, Vector3.down) > 0.8f) label = "Top";
        else if (Vector3.Dot(f, Vector3.left) > 0.8f) label = "Right";
        else if (Vector3.Dot(f, Vector3.right) > 0.8f) label = "Left";
        else label = useOrthographic ? "Isometric" : "Perspective";

        GUI.Label(new Rect(r.x + r.width - 100, r.y + r.height - 30, 90, 20), label,
            new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1, 1, 1, 0.5f) }
            });
    }

    private void DrawRightSidebar()
    {
        EditorGUILayout.BeginVertical(sidebarStyle, GUILayout.Width(200), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("SHAPE LIBRARY", headerStyle);
        libraryScroll = EditorGUILayout.BeginScrollView(libraryScroll);
        if (Directory.Exists(GENERATED_PATH))
        {
            foreach (var p in Directory.GetFiles(GENERATED_PATH, "*.asset"))
            {
                string assetPath = p.Replace('\\', '/');
                if (GUILayout.Button(Path.GetFileNameWithoutExtension(p), EditorStyles.miniButton))
                    LoadShapeFromLibrary(assetPath);
            }
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void LoadShapeFromLibrary(string assetPath)
    {
        CubeShapeData data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(assetPath);
        if (data == null) return;

        shapeName = data.shapeName;
        gridSize = data.gridSize;
        cellSize = data.cellSize;
        spacing = data.spacing;
        cubePrefab = data.cubePrefab;

        CreateNewShape();
        foreach (var cell in data.occupiedCells)
            AddCube(cell);
    }

    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(25));
        EditorGUILayout.LabelField($"Status: {(isEditing ? "EDITING" : "IDLE")}  |  Mode: {currentTool}", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("BlockMerge 3D Builder Pro v2.5", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

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
        Ray r = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        UpdateHoverStateLogic(r, false);
        int id = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(id);

        if (e.type != EventType.MouseDown) return;

        if (e.button == 0 && !e.shift)
        {
            if (currentTool == ToolMode.Add && placementCell.HasValue) AddCube(placementCell.Value);
            else if (currentTool == ToolMode.Remove && hoveredCell.HasValue) RemoveCube(hoveredCell.Value);
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
        float s = cellSize + spacing;
        Vector3 o = GetOriginOffset();
        Handles.color = new Color(1, 1, 1, 0.1f);
        for (int x = 0; x <= gridSize.x; x++)
            for (int y = 0; y <= gridSize.y; y++)
                if (ySlice == -1 || y == ySlice || y == ySlice + 1)
                    Handles.DrawLine(o + new Vector3(x * s, y * s, 0), o + new Vector3(x * s, y * s, gridSize.z * s));

        if (placementCell.HasValue && currentTool == ToolMode.Add)
        {
            Handles.color = new Color(0.2f, 0.9f, 1f, 0.5f);
            Handles.CubeHandleCap(0, o + (Vector3)placementCell.Value * s + Vector3.one * (cellSize * 0.5f), Quaternion.identity, cellSize, EventType.Repaint);
        }
        if (hoveredCell.HasValue && (currentTool == ToolMode.Remove || Event.current.shift))
        {
            Handles.color = new Color(1f, 0.2f, 0.1f, 0.7f);
            Handles.CubeHandleCap(0, o + (Vector3)hoveredCell.Value * s + Vector3.one * (cellSize * 0.5f), Quaternion.identity, cellSize * 1.05f, EventType.Repaint);
        }
    }

    private void AddCube(Vector3Int c)
    {
        if (dataHolder.occupiedCells.Contains(c)) return;
        Undo.RegisterCompleteObjectUndo(dataHolder, "Add Cube");
        GameObject cube = cubePrefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(currentShapeObject.transform);
        float s = cellSize + spacing;
        cube.transform.position = GetOriginOffset() + (Vector3)c * s + Vector3.one * (cellSize * 0.5f);
        cube.transform.localScale = Vector3.one * cellSize;
        cube.name = $"Cube_{c.x}_{c.y}_{c.z}";
        dataHolder.occupiedCells.Add(c);
        Undo.RegisterCreatedObjectUndo(cube, "Add Cube");
    }

    private void RemoveCube(Vector3Int c)
    {
        if (!dataHolder.occupiedCells.Contains(c)) return;
        Undo.RegisterCompleteObjectUndo(dataHolder, "Remove Cube");
        Transform t = currentShapeObject.transform.Find($"Cube_{c.x}_{c.y}_{c.z}");
        if (t != null)
        {
            Undo.DestroyObjectImmediate(t.gameObject);
            dataHolder.occupiedCells.Remove(c);
        }
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
    }

    private void CreateNewShape()
    {
        if (currentShapeObject != null) DestroyImmediate(currentShapeObject);
        currentShapeObject = new GameObject(EDITOR_OBJECT_NAME);
        dataHolder = currentShapeObject.AddComponent<CubeShapeDataHolder>();
        dataHolder.gridSize = gridSize;
        dataHolder.cellSize = cellSize;
        dataHolder.spacing = spacing;
        dataHolder.shapeName = shapeName;
        isEditing = true;
        Selection.activeGameObject = currentShapeObject;
        FocusCamera();
    }

    private void SaveAsPrefab()
    {
        if (currentShapeObject == null || dataHolder.occupiedCells.Count == 0) return;
        if (!Directory.Exists(GENERATED_PATH)) Directory.CreateDirectory(GENERATED_PATH);
        string path = EditorUtility.SaveFilePanelInProject("Save Prefab", shapeName, "prefab", "", GENERATED_PATH);
        if (string.IsNullOrEmpty(path)) return;

        CubeShapeData asset = ScriptableObject.CreateInstance<CubeShapeData>();
        asset.shapeName = shapeName;
        asset.gridSize = gridSize;
        asset.cellSize = cellSize;
        asset.spacing = spacing;
        asset.occupiedCells = new List<Vector3Int>(dataHolder.occupiedCells);
        asset.cubePrefab = cubePrefab;
        AssetDatabase.CreateAsset(asset, Path.ChangeExtension(path, ".asset"));

        GameObject pRoot = Instantiate(currentShapeObject);
        pRoot.name = Path.GetFileNameWithoutExtension(path);
        CubeShapeDataHolder h = pRoot.GetComponent<CubeShapeDataHolder>();
        h.shapeName = shapeName;
        h.gridSize = gridSize;
        h.cellSize = cellSize;
        h.spacing = spacing;
        h.occupiedCells = new List<Vector3Int>(asset.occupiedCells);
        PrefabUtility.SaveAsPrefabAsset(pRoot, path);
        DestroyImmediate(pRoot);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Done!", "Saved.", "OK");
    }

    private bool IsWithinGrid(Vector3Int c)
    {
        return c.x >= 0 && c.x < gridSize.x
            && c.y >= 0 && c.y < gridSize.y
            && c.z >= 0 && c.z < gridSize.z;
    }

    private Vector3 GetOriginOffset()
    {
        if (currentShapeObject == null) return Vector3.zero;
        return gridOrigin == GridOrigin.Center
            ? currentShapeObject.transform.position - (Vector3)gridSize * cellSize * 0.5f
            : currentShapeObject.transform.position;
    }
}
