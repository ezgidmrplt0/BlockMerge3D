using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class ManualVoxelPieceBuilderWindow : EditorWindow
{
    private const string PIECES_PATH = "Assets/Pieces";
    private const string LEVELS_PATH = "Assets/Levels";
    private const string WORKSPACE_NAME = "_ManualVoxelWorkspace";

    private Vector3Int gridSize = new Vector3Int(6, 6, 6);
    private float cellSize = 1.0f;
    private float spacing = 0.1f;
    
    private string pieceName = "Custom_3D_Piece";
    private bool buildModeActive = false;
    private Color pieceColor = new Color(0.95f, 0.40f, 0.70f); // Default Pink

    private GameObject cubePrefab;
    private GameObject workspaceRoot;
    
    private HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
    private Vector3Int? hoveredGridPos = null;

    private Vector2 scrollPos;
    private GUIStyle headerStyle;
    private GUIStyle boxStyle;

    [MenuItem("BlockMerge3D/🧱 3D Sahne Parça Yapıcı", false, 5)]
    public static void ShowWindow()
    {
        var w = GetWindow<ManualVoxelPieceBuilderWindow>("3D Sahne Yapıcı");
        w.minSize = new Vector2(400, 520);
    }

    public void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        // Default prefab'ı yükle
        string prefabPath = EditorPrefs.GetString("BlockMerge3D_DefaultCubePrefab", "");
        if (!string.IsNullOrEmpty(prefabPath))
        {
            cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }
        
        FindOrCreateWorkspace();
    }

    public void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        CleanWorkspacePreview();
    }

    private void FindOrCreateWorkspace()
    {
        workspaceRoot = GameObject.Find(WORKSPACE_NAME);
        if (workspaceRoot != null)
        {
            occupiedCells.Clear();
            foreach (Transform child in workspaceRoot.transform)
            {
                Vector3 localPos = child.localPosition;
                float step = cellSize + spacing;
                int x = Mathf.RoundToInt(localPos.x / step);
                int y = Mathf.RoundToInt(localPos.y / step);
                int z = Mathf.RoundToInt(localPos.z / step);
                occupiedCells.Add(new Vector3Int(x, y, z));
            }
        }
    }

    private void StartBuildMode()
    {
        if (workspaceRoot == null)
        {
            workspaceRoot = new GameObject(WORKSPACE_NAME);
            workspaceRoot.transform.position = Vector3.zero;
        }
        buildModeActive = true;
        Selection.activeGameObject = workspaceRoot;
        SceneView.RepaintAll();
    }

    private void StopBuildMode()
    {
        buildModeActive = false;
        SceneView.RepaintAll();
    }

    private void ClearWorkspace()
    {
        if (workspaceRoot != null)
        {
            DestroyImmediate(workspaceRoot);
        }
        occupiedCells.Clear();
        buildModeActive = false;
        SceneView.RepaintAll();
    }

    private void CleanWorkspacePreview()
    {
        // Keeps workspace objects in the scene but deselects if desired.
    }

    public void OnGUI()
    {
        BuildStyles();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.BeginVertical(boxStyle);
        GUILayout.Label("🧱 3D SAHNE PARÇA YAPICI", headerStyle);
        EditorGUILayout.HelpBox("Bu araç, Unity Scene View (Sahne Görünümü) üzerinde doğrudan 3D bloklar boyayarak özel parçalar tasarlamanızı sağlar.", MessageType.Info);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("⚙️ ÇALIŞMA ALANI AYARLARI", EditorStyles.boldLabel);
        
        gridSize = EditorGUILayout.Vector3IntField("Grid Boyutu (En x Boy x Derinlik)", gridSize);
        gridSize.x = Mathf.Clamp(gridSize.x, 1, 12);
        gridSize.y = Mathf.Clamp(gridSize.y, 1, 12);
        gridSize.z = Mathf.Clamp(gridSize.z, 1, 12);

        cellSize = EditorGUILayout.FloatField("Hücre Boyutu (Cell Size)", cellSize);
        spacing = EditorGUILayout.FloatField("Boşluk (Spacing)", spacing);
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Küp Prefab (Opsiyonel)", cubePrefab, typeof(GameObject), false);
        pieceColor = EditorGUILayout.ColorField("Küp Rengi", pieceColor);
        pieceName = EditorGUILayout.TextField("Parça Adı", pieceName);
        EditorGUILayout.EndVertical();

        GUILayout.Space(12);

        // Kontrol Butonları
        if (!buildModeActive)
        {
            GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
            if (GUILayout.Button("🛠 İNŞA MODUNU BAŞLAT\n(Sahnede Çizmeye Başla)", GUILayout.Height(50)))
            {
                StartBuildMode();
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f); // Pink/Active
            if (GUILayout.Button("⏸ İNŞA MODUNU DURAKLAT\n(Sahne Çizimini Dondur)", GUILayout.Height(50)))
            {
                StopBuildMode();
            }
            GUI.backgroundColor = Color.white;
        }

        GUILayout.Space(8);

        if (GUILayout.Button("🗑 ÇALIŞMA ALANINI TEMİZLE", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Emin misiniz?", "Tüm sahne çiziminiz temizlenecektir.", "Evet", "Hayır"))
            {
                ClearWorkspace();
            }
        }

        GUILayout.Space(15);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("💾 KAYDETME & AKTARMA", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Aktif Blok Sayısı: {occupiedCells.Count}", EditorStyles.miniBoldLabel);

        EditorGUI.BeginDisabledGroup(occupiedCells.Count == 0);
        
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
        if (GUILayout.Button("💾 PREFAB VE ASSET YAP\n(Parça Olarak Kaydet)", GUILayout.Height(45)))
        {
            SaveScenePieceToAssets();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(6);

        GUI.backgroundColor = new Color(0.95f, 0.75f, 0.2f);
        if (GUILayout.Button("🎓 EĞİTİM KÜTÜPHANESİNE EKLE\n(Yapay Zekaya Öğret)", GUILayout.Height(45)))
        {
            AddScenePieceToAILibrary();
        }
        GUI.backgroundColor = Color.white;

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();

        GUILayout.Space(15);
        
        // Kullanım klavuzu
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("💡 SAHNE İÇİ KONTROLLERİ:", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField("• Sol Tık: Boş grid hücresine küp ekler.", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("• Sağ Tık: Boyanmış olan küpü siler.", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("• Scene View penceresinde grid sınırları tel kafes olarak çizilir.", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("• İnşa modundayken sahnedeki diğer nesnelerin seçimi kilitlenir.", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
    }

    private void BuildStyles()
    {
        if (headerStyle != null) return;

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };
        headerStyle.normal.textColor = new Color(0.35f, 0.78f, 1.00f);

        boxStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 10, 10)
        };
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!buildModeActive) return;

        if (workspaceRoot == null)
        {
            workspaceRoot = GameObject.Find(WORKSPACE_NAME);
            if (workspaceRoot == null) return;
        }

        Selection.activeObject = workspaceRoot;

        float step = cellSize + spacing;
        Vector3 size = new Vector3(gridSize.x * step, gridSize.y * step, gridSize.z * step);
        Vector3 center = size * 0.5f - Vector3.one * (spacing * 0.5f);
        
        Handles.color = new Color(0.35f, 0.78f, 1.00f, 0.4f);
        Handles.DrawWireCube(workspaceRoot.transform.position + center, size);

        Handles.color = new Color(0.35f, 0.78f, 1.00f, 0.1f);
        for (int x = 0; x < gridSize.x; x++)
        {
            for (int z = 0; z < gridSize.z; z++)
            {
                Vector3 p = workspaceRoot.transform.position + new Vector3(x * step + cellSize * 0.5f, 0, z * step + cellSize * 0.5f);
                Handles.DrawWireDisc(p, Vector3.up, cellSize * 0.4f);
            }
        }

        Event e = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

        Plane groundPlane = new Plane(workspaceRoot.transform.up, workspaceRoot.transform.position);
        float enter;
        
        Vector3Int? tempHover = null;
        bool hasHit = false;

        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 1000f))
        {
            if (hit.transform.parent == workspaceRoot.transform)
            {
                Vector3 newPosLocal = workspaceRoot.transform.InverseTransformPoint(hit.point + hit.normal * (cellSize * 0.5f));
                int hx = Mathf.RoundToInt(newPosLocal.x / step);
                int hy = Mathf.RoundToInt(newPosLocal.y / step);
                int hz = Mathf.RoundToInt(newPosLocal.z / step);
                
                if (hx >= 0 && hx < gridSize.x && hy >= 0 && hy < gridSize.y && hz >= 0 && hz < gridSize.z)
                {
                    tempHover = new Vector3Int(hx, hy, hz);
                    hasHit = true;
                }
            }
        }

        if (!hasHit && groundPlane.Raycast(ray, out enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            Vector3 localHitPoint = workspaceRoot.transform.InverseTransformPoint(hitPoint);
            
            int hx = Mathf.RoundToInt(localHitPoint.x / step);
            int hy = 0; 
            int hz = Mathf.RoundToInt(localHitPoint.z / step);

            if (hx >= 0 && hx < gridSize.x && hy >= 0 && hy < gridSize.y && hz >= 0 && hz < gridSize.z)
            {
                tempHover = new Vector3Int(hx, hy, hz);
                hasHit = true;
            }
        }

        hoveredGridPos = tempHover;

        if (hasHit && hoveredGridPos.HasValue)
        {
            Vector3 localPos = (Vector3)hoveredGridPos.Value * step + Vector3.one * (cellSize * 0.5f);
            Vector3 worldPos = workspaceRoot.transform.TransformPoint(localPos);

            bool cellOccupied = occupiedCells.Contains(hoveredGridPos.Value);
            Handles.color = cellOccupied ? Color.red : Color.green;
            Handles.DrawWireCube(worldPos, Vector3.one * cellSize);
            
            sceneView.Repaint();

            if (e.type == EventType.MouseDown)
            {
                if (e.button == 0) // Left click: Add
                {
                    if (!cellOccupied)
                    {
                        AddBlock(hoveredGridPos.Value);
                        e.Use();
                    }
                }
                else if (e.button == 1) // Right click: Remove
                {
                    if (cellOccupied)
                    {
                        RemoveBlock(hoveredGridPos.Value);
                        e.Use();
                    }
                }
            }
        }

        int controlID = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlID);
    }

    private void AddBlock(Vector3Int pos)
    {
        if (workspaceRoot == null) return;
        
        float step = cellSize + spacing;
        Vector3 localPos = (Vector3)pos * step + Vector3.one * (cellSize * 0.5f);

        GameObject cube;
        if (cubePrefab != null)
        {
            cube = Instantiate(cubePrefab, workspaceRoot.transform);
        }
        else
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(workspaceRoot.transform);
        }

        cube.transform.localPosition = localPos;
        cube.transform.localScale = Vector3.one * cellSize;
        cube.name = $"Block_{pos.x}_{pos.y}_{pos.z}";
        
        var renderer = cube.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = new Material(Shader.Find("Standard"));
            renderer.sharedMaterial.color = pieceColor;
        }

        if (cube.GetComponent<Collider>() == null)
        {
            cube.AddComponent<BoxCollider>();
        }

        occupiedCells.Add(pos);
        Undo.RegisterCreatedObjectUndo(cube, "Add Cube Block");
        Repaint();
    }

    private void RemoveBlock(Vector3Int pos)
    {
        if (workspaceRoot == null) return;

        string targetName = $"Block_{pos.x}_{pos.y}_{pos.z}";
        Transform target = workspaceRoot.transform.Find(targetName);
        if (target != null)
        {
            Undo.DestroyObjectImmediate(target.gameObject);
            occupiedCells.Remove(pos);
            Repaint();
        }
    }

    private void SaveScenePieceToAssets()
    {
        if (occupiedCells.Count == 0) return;

        List<Vector3Int> cellsList = occupiedCells.ToList();
        int minX = cellsList.Min(c => c.x);
        int minY = cellsList.Min(c => c.y);
        int minZ = cellsList.Min(c => c.z);

        List<Vector3Int> normalizedCells = cellsList
            .Select(c => new Vector3Int(c.x - minX, c.y - minY, c.z - minZ))
            .ToList();

        string folderPath = PIECES_PATH;
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets", "Pieces");
        }

        string name = string.IsNullOrEmpty(pieceName) ? "Custom_3D_Piece" : pieceName;
        string prefabPath = $"{folderPath}/{name}.prefab";
        string assetPath = $"{folderPath}/{name}.asset";

        // 1. ScriptableObject
        CubeShapeData data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(assetPath);
        bool isNew = data == null;
        if (isNew) data = CreateInstance<CubeShapeData>();

        data.shapeName = name;
        data.gridSize = new Vector3Int(
            normalizedCells.Max(c => c.x) + 1,
            normalizedCells.Max(c => c.y) + 1,
            normalizedCells.Max(c => c.z) + 1
        );
        data.cellSize = cellSize;
        data.spacing = spacing;
        data.occupiedCells = new List<Vector3Int>(normalizedCells);

        if (isNew) AssetDatabase.CreateAsset(data, assetPath);
        else EditorUtility.SetDirty(data);

        // 2. Prefab
        GameObject pRoot = new GameObject(name);
        var ph = pRoot.AddComponent<CubeShapeDataHolder>();
        ph.shapeName = name;
        ph.gridSize = data.gridSize;
        ph.cellSize = cellSize;
        ph.spacing = spacing;
        ph.occupiedCells = new List<Vector3Int>(normalizedCells);

        float step = cellSize + spacing;
        foreach (var cell in normalizedCells)
        {
            GameObject cube = cubePrefab != null
                ? Instantiate(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(pRoot.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
            cube.transform.localScale = Vector3.one * cellSize;
            cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            
            var r = cube.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = new Material(Shader.Find("Standard"));
                r.sharedMaterial.color = pieceColor;
            }
        }

        PrefabUtility.SaveAsPrefabAsset(pRoot, prefabPath);
        DestroyImmediate(pRoot);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Başarılı! 💾",
            $"✅ 3D Parça Prefab ve ScriptableObject olarak kaydedildi!\n\n" +
            $"• Konum: {prefabPath}", "Tamam");
    }

    private void AddScenePieceToAILibrary()
    {
        if (occupiedCells.Count == 0) return;

        List<Vector3Int> cellsList = occupiedCells.ToList();
        int minX = cellsList.Min(c => c.x);
        int minY = cellsList.Min(c => c.y);
        int minZ = cellsList.Min(c => c.z);

        List<Vector3Int> normalizedCells = cellsList
            .Select(c => new Vector3Int(c.x - minX, c.y - minY, c.z - minZ))
            .ToList();

        string name = string.IsNullOrEmpty(pieceName) ? "Custom_3D_Piece" : pieceName;

        string jsonPath = $"{LEVELS_PATH}/ai_pieces_manual_library.json";
        AIPieceListWrapper wrapper = new AIPieceListWrapper();
        
        if (File.Exists(jsonPath))
        {
            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                wrapper = JsonUtility.FromJson<AIPieceListWrapper>(jsonContent);
                if (wrapper == null) wrapper = new AIPieceListWrapper();
            }
            catch {}
        }

        if (wrapper.names.Contains(name))
        {
            name = $"{name}_{wrapper.pieces.Count + 1}";
        }

        wrapper.names.Add(name);
        
        AIPieceDatasetEntry entry = new AIPieceDatasetEntry
        {
            pieceIndex = wrapper.pieces.Count,
            generationMode = "3D_Scene_Builder",
            prompt = name,
            cubeCount = normalizedCells.Count,
            cells = normalizedCells.Select(c => new SerializableCell(c)).ToList()
        };
        wrapper.pieces.Add(entry);

        string saveJson = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(jsonPath, saveJson);

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Eğitime Eklendi! 🎓",
            $"✅ '{name}' başarıyla AI Seviye Tasarımcısı manuel kütüphanesine ve eğitim havuzuna eklendi!", "Harika!");
    }
}
