using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  PIECE DEFINITION MIGRATION  —  Parça Kütüphanesi Tarayıcı & Editörü
//  BlockMerge3D  •  Assets/Pieces ve Assets/Levels altındaki tüm
//  CubeShapeDataHolder tabanlı prefab'ları tarar, kanonik (rotasyon
//  bağımsız) imzaya göre gruplayıp PieceDefinition assetleri üretir.
// ═══════════════════════════════════════════════════════════════════

public class PieceDefinitionMigrationWindow : EditorWindow
{
    private const string SCAN_ROOT_PIECES = "Assets/Pieces";
    private const string SCAN_ROOT_LEVELS = "Assets/Levels";
    private const string OUTPUT_FOLDER    = "Assets/PieceDefinitions";

    private static readonly Color COL_HEADER = new Color(0.35f, 0.78f, 1.00f);
    private static readonly Color COL_WARN   = new Color(0.95f, 0.70f, 0.20f);

    private struct PieceCandidate
    {
        public string path;
        public GameObject prefab;
        public CubeShapeDataHolder holder;
    }

    private List<string> lastReportLines = new List<string>();
    private List<string> lastDuplicateWarnings = new List<string>();
    private List<PieceDefinition> allDefinitions = new List<PieceDefinition>();
    private List<PieceTestResult> lastPieceTestResults = new List<PieceTestResult>();
    private List<PieceTestResult> lastSolverTestResults = new List<PieceTestResult>();

    private PieceDefinition selectedDefinition;
    private PieceDefinition editingDefinition;
    private int selectedRotationIndex;

    private Vector2 reportScroll;
    private Vector2 listScroll;

    // Özel Parça Tasarımcısı Alanları
    private bool showCustomPieceCreator = true;
    private int customGridWidth = 4;
    private int customGridHeight = 4;
    private bool[,] customGridCells = new bool[5, 5];
    private string customPieceName = "Özel_Parça_1";
    private PieceRotationMode customRotationMode = PieceRotationMode.FixedAsDrawn;
    private float customSpawnWeight = 1.0f;
    private int customMaxCopies = -1;
    private string customTags = "";

    private GUIStyle styleHeader, styleBox;
    private bool stylesBuilt;

    public System.Action onRepaintRequested;

    new public void Repaint()
    {
        base.Repaint();
        onRepaintRequested?.Invoke();
    }

    // [MenuItem("BlockMerge3D/🧬 Parça Kütüphanesi (Piece Library)")]
    public static void ShowWindow()
    {
        var w = GetWindow<PieceDefinitionMigrationWindow>("Parça Kütüphanesi");
        w.minSize = new Vector2(640, 480);
    }

    public void OnEnable()
    {
        RefreshDefinitionList();
    }

    private void BuildStyles()
    {
        if (stylesBuilt) return;
        styleHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        styleHeader.normal.textColor = COL_HEADER;
        styleBox = new GUIStyle(GUI.skin.box);
        stylesBuilt = true;
    }

    public void OnGUI()
    {
        BuildStyles();

        EditorGUILayout.Space(6);
        GUILayout.Label("🧬 PARÇA KÜTÜPHANESİ — MIGRATION & DÜZENLEME", styleHeader);
        EditorGUILayout.HelpBox(
            "Assets/Pieces ve Assets/Levels altındaki tüm CubeShapeDataHolder tabanlı prefab'ları tarar, " +
            "rotasyon bağımsız kanonik şekle göre gruplar ve her benzersiz şekil için bir PieceDefinition " +
            "asseti üretir/günceller. Aynı şeklin birden fazla prefab'ı varsa (mükerrer) uyarı verir.",
            MessageType.Info);

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
        if (GUILayout.Button("🔍  Tara ve Migrate Et", GUILayout.Height(32)))
        {
            ScanAndMigrate();
        }
        GUI.backgroundColor = new Color(0.55f, 0.35f, 0.85f);
        if (GUILayout.Button("🧪  Parça İmza Testleri", GUILayout.Height(32)))
        {
            lastPieceTestResults = PieceDefinitionMigrationTests.RunAll();
        }
        GUI.backgroundColor = new Color(0.20f, 0.60f, 0.55f);
        if (GUILayout.Button("🧪  Solver Testleri (Collapse-Aware)", GUILayout.Height(32)))
        {
            lastSolverTestResults = LevelSolverTests.RunAll();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        DrawTestResultsPanel("Parça İmza Testleri", lastPieceTestResults);
        DrawTestResultsPanel("Solver Testleri (Collapse-Aware)", lastSolverTestResults);

        if (lastReportLines.Count > 0)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(styleBox);
            foreach (var line in lastReportLines)
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        if (lastDuplicateWarnings.Count > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(styleBox);
            var warnStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            warnStyle.normal.textColor = COL_WARN;
            GUILayout.Label($"⚠ {lastDuplicateWarnings.Count} mükerrer grup:", EditorStyles.miniBoldLabel);
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MaxHeight(100));
            foreach (var w in lastDuplicateWarnings)
                EditorGUILayout.LabelField(w, warnStyle);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        DrawCustomPieceCreator();

        if (selectedDefinition != null)
        {
            DrawSelectedPreviewPanel();
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"KAYITLI PARÇA TANIMLARI ({allDefinitions.Count})", styleHeader);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Listeyi Yenile", GUILayout.Width(120)))
            RefreshDefinitionList();
        if (GUILayout.Button("Tümünü Kaydet", GUILayout.Width(120)))
        {
            AssetDatabase.SaveAssets();
        }
        EditorGUILayout.EndHorizontal();

        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        foreach (var def in allDefinitions)
        {
            if (def == null) continue;
            DrawDefinitionRow(def);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawTestResultsPanel(string title, List<PieceTestResult> results)
    {
        if (results == null || results.Count == 0) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(styleBox);

        int testsPassed = results.Count(r => r.passed);
        bool allTestsPassed = testsPassed == results.Count;
        var summaryStyle = new GUIStyle(EditorStyles.boldLabel);
        summaryStyle.normal.textColor = allTestsPassed ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.9f, 0.35f, 0.3f);
        GUILayout.Label($"{title}: {testsPassed}/{results.Count} geçti", summaryStyle);

        foreach (var r in results)
        {
            var rowStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            rowStyle.normal.textColor = r.passed ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.95f, 0.4f, 0.35f);
            GUILayout.Label((r.passed ? "✅ " : "❌ ") + $"{r.name}: {r.message}", rowStyle);
        }

        EditorGUILayout.EndVertical();
    }

    private static readonly string[] ROTATION_MODE_LABELS = new string[]
    {
        "📌 Olduğu Gibi (Sabit / 0°)",
        "🔄 Yatay Düzlemde 2D Dönüş (Y-Rotasyonu)",
        "🎲 Tüm 3D Rotasyonlar (Takla Dahil)"
    };

    private void DrawDefinitionRow(PieceDefinition def)
    {
        EditorGUILayout.BeginVertical(styleBox);

        EditorGUILayout.BeginHorizontal();
        DrawCellsPreview(def.cells, 9f, new Color(0.35f, 0.78f, 1.00f));
        GUILayout.Space(6);

        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(def.displayName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"hacim: {def.volume}   rotasyon: {def.allowedRotations.Count}   kaynak: {def.sourceAssetPaths.Count}", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("👁 Önizle", GUILayout.Width(75)))
        {
            selectedDefinition = def;
            selectedRotationIndex = 0;
        }
        GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
        if (GUILayout.Button("✏ Düzenle", GUILayout.Width(80)))
        {
            LoadDefinitionForEditing(def);
        }
        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
        if (GUILayout.Button("🗑 Sil", GUILayout.Width(60)))
        {
            DeleteDefinitionFromLibrary(def);
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("Hesapla", GUILayout.Width(70)))
        {
            def.RecomputeDerived();
            EditorUtility.SetDirty(def);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        def.rotationMode = (PieceRotationMode)EditorGUILayout.Popup("Rotasyon Modu", (int)def.rotationMode, ROTATION_MODE_LABELS);
        def.spawnWeight = EditorGUILayout.FloatField("Spawn Ağırlığı", def.spawnWeight);
        def.maxCopiesPerLevel = EditorGUILayout.IntField("Seviye Başına Maks. Kopya (-1=sınırsız)", def.maxCopiesPerLevel);

        string tagsJoined = string.Join(", ", def.difficultyTags);
        string newTagsJoined = EditorGUILayout.TextField("Zorluk Etiketleri (virgülle ayır)", tagsJoined);
        if (newTagsJoined != tagsJoined)
        {
            def.difficultyTags = newTagsJoined
                .Split(',')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();
        }
        if (EditorGUI.EndChangeCheck())
        {
            def.RecomputeDerived();
            EditorUtility.SetDirty(def);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    private void DrawSelectedPreviewPanel()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical(styleBox);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"🔎 {selectedDefinition.displayName} — Rotasyon Önizleme", styleHeader);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Kapat", GUILayout.Width(70)))
        {
            selectedDefinition = null;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }
        EditorGUILayout.EndHorizontal();

        var rotations = selectedDefinition.allowedRotations;
        if (rotations == null || rotations.Count == 0)
        {
            EditorGUILayout.HelpBox("Bu parça için rotasyon verisi yok — önce 'Yeniden Hesapla' çalıştırın.", MessageType.Warning);
        }
        else
        {
            selectedRotationIndex = Mathf.Clamp(selectedRotationIndex, 0, rotations.Count - 1);
            Vector3Int rotEuler = rotations[selectedRotationIndex];

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("◀", GUILayout.Width(30)))
                selectedRotationIndex = (selectedRotationIndex - 1 + rotations.Count) % rotations.Count;
            GUILayout.Label(
                $"Rotasyon {selectedRotationIndex + 1}/{rotations.Count}   ({rotEuler.x}°, {rotEuler.y}°, {rotEuler.z}°)",
                EditorStyles.centeredGreyMiniLabel, GUILayout.Width(240));
            if (GUILayout.Button("▶", GUILayout.Width(30)))
                selectedRotationIndex = (selectedRotationIndex + 1) % rotations.Count;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            var rotatedCells = PieceGeometryUtils.RotateAndNormalize(
                selectedDefinition.cells,
                Quaternion.Euler(rotEuler.x, rotEuler.y, rotEuler.z));

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawCellsPreview(rotatedCells, 26f, new Color(0.95f, 0.55f, 0.15f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Hücreleri XZ düzlemine izdüşürüp küçük bir kare ızgara olarak çizer — çok katmanlı
    /// parçalarda aynı (x,z) üzerindeki farklı Y hücreleri tek karede üst üste gösterilir
    /// (küçük önizleme için yeterli, ayrıntılı katman ayrımı bu araçta hedeflenmiyor).
    /// </summary>
    private void DrawCellsPreview(List<Vector3Int> cells, float blockSize, Color color)
    {
        if (cells == null || cells.Count == 0)
        {
            GUILayout.Label("(boş)", EditorStyles.miniLabel, GUILayout.Width(blockSize * 3), GUILayout.Height(blockSize * 3));
            return;
        }

        var footprint = new HashSet<Vector2Int>();
        foreach (var c in cells) footprint.Add(new Vector2Int(c.x, c.z));

        int minX = footprint.Min(p => p.x), maxX = footprint.Max(p => p.x);
        int minZ = footprint.Min(p => p.y), maxZ = footprint.Max(p => p.y);
        int w = maxX - minX + 1;
        int h = maxZ - minZ + 1;

        float pw = w * blockSize;
        float ph = h * blockSize;

        Rect area = GUILayoutUtility.GetRect(pw, ph, GUILayout.Width(pw), GUILayout.Height(ph));
        if (Event.current.type != EventType.Repaint) return;

        EditorGUI.DrawRect(area, new Color(0.10f, 0.10f, 0.13f));
        foreach (var p in footprint)
        {
            Rect r = new Rect(
                area.x + (p.x - minX) * blockSize + 0.5f,
                area.y + (p.y - minZ) * blockSize + 0.5f,
                blockSize - 1f,
                blockSize - 1f);
            EditorGUI.DrawRect(r, color);
        }
    }

    private void RefreshDefinitionList()
    {
        allDefinitions.Clear();
        if (!AssetDatabase.IsValidFolder(OUTPUT_FOLDER)) return;

        var guids = AssetDatabase.FindAssets("t:PieceDefinition", new[] { OUTPUT_FOLDER });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var def = AssetDatabase.LoadAssetAtPath<PieceDefinition>(path);
            if (def != null) allDefinitions.Add(def);
        }
        allDefinitions = allDefinitions.OrderBy(d => d.displayName).ToList();
    }

    // internal: LevelCreationWizardWindow, kütüphane boşsa 2. adımdan doğrudan çağırıyor.
    internal void ScanAndMigrate()
    {
        if (!AssetDatabase.IsValidFolder(OUTPUT_FOLDER))
            AssetDatabase.CreateFolder("Assets", "PieceDefinitions");

        // 1. Assets/Pieces ve Assets/Levels altındaki tüm CubeShapeDataHolder taşıyan prefab'ları topla.
        var guids = AssetDatabase.FindAssets("t:GameObject", new[] { SCAN_ROOT_PIECES, SCAN_ROOT_LEVELS });
        var candidates = new List<PieceCandidate>();

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            var holder = prefab.GetComponent<CubeShapeDataHolder>();
            if (holder == null || holder.occupiedCells == null || holder.occupiedCells.Count == 0) continue;

            candidates.Add(new PieceCandidate { path = path, prefab = prefab, holder = holder });
        }

        // 2. Kanonik (rotasyon bağımsız) imzaya göre grupla.
        var groups = new Dictionary<string, List<PieceCandidate>>();
        foreach (var c in candidates)
        {
            string sig = PieceGeometryUtils.ComputeCanonicalSignature(c.holder.occupiedCells);
            if (!groups.TryGetValue(sig, out var list))
            {
                list = new List<PieceCandidate>();
                groups[sig] = list;
            }
            list.Add(c);
        }

        // 3. Var olan PieceDefinition'ları imzalarına göre indeksle (idempotent çalışma için).
        var existingBySignature = new Dictionary<string, PieceDefinition>();
        if (AssetDatabase.IsValidFolder(OUTPUT_FOLDER))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:PieceDefinition", new[] { OUTPUT_FOLDER }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var pd = AssetDatabase.LoadAssetAtPath<PieceDefinition>(path);
                if (pd != null && !string.IsNullOrEmpty(pd.canonicalSignature))
                    existingBySignature[pd.canonicalSignature] = pd;
            }
        }

        int createdCount = 0, updatedCount = 0, duplicateGroupCount = 0;
        lastDuplicateWarnings.Clear();

        foreach (var kvp in groups)
        {
            string signature = kvp.Key;
            var members = kvp.Value;
            var first = members[0];

            bool isNew = !existingBySignature.TryGetValue(signature, out var def);
            if (isNew)
            {
                def = ScriptableObject.CreateInstance<PieceDefinition>();
                def.cells = new List<Vector3Int>(first.holder.occupiedCells);
                def.cellSize = first.holder.cellSize;
                def.spacing = first.holder.spacing;
                def.displayName = !string.IsNullOrEmpty(first.holder.shapeName) ? first.holder.shapeName : first.prefab.name;
                def.RecomputeDerived();
            }

            def.representativePrefab = first.prefab;
            def.sourceAssetPaths = members.Select(m => m.path).ToList();

            if (isNew)
            {
                string safeName = SanitizeFileName(def.displayName);
                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{OUTPUT_FOLDER}/{safeName}_{def.id}.asset");
                AssetDatabase.CreateAsset(def, assetPath);
                createdCount++;
            }
            else
            {
                EditorUtility.SetDirty(def);
                updatedCount++;
            }

            if (members.Count > 1)
            {
                duplicateGroupCount++;
                string warning = $"Mükerrer şekil ({members.Count} prefab aynı kanonik imzaya sahip): " +
                                  string.Join(", ", members.Select(m => m.path));
                Debug.LogWarning(warning);
                lastDuplicateWarnings.Add(warning);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        lastReportLines = new List<string>
        {
            $"Taranan prefab: {candidates.Count}",
            $"Benzersiz şekil: {groups.Count}",
            $"Yeni oluşturulan: {createdCount}",
            $"Güncellenen: {updatedCount}",
            $"Mükerrer grup sayısı: {duplicateGroupCount}",
        };

        RefreshDefinitionList();
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Piece";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    private void LoadDefinitionForEditing(PieceDefinition def)
    {
        if (def == null) return;
        editingDefinition = def;
        showCustomPieceCreator = true;
        customPieceName = def.displayName;
        customRotationMode = def.rotationMode;
        customSpawnWeight = def.spawnWeight;
        customMaxCopies = def.maxCopiesPerLevel;
        customTags = string.Join(", ", def.difficultyTags);

        customGridCells = new bool[5, 5];
        if (def.cells != null)
        {
            foreach (var cell in def.cells)
            {
                if (cell.x >= 0 && cell.x < 5 && cell.z >= 0 && cell.z < 5)
                {
                    customGridCells[cell.x, cell.z] = true;
                }
            }
        }
        GUI.FocusControl(null);
    }

    private void DeleteDefinitionFromLibrary(PieceDefinition def)
    {
        if (def == null) return;
        string path = AssetDatabase.GetAssetPath(def);
        bool confirmed = EditorUtility.DisplayDialog(
            "Parçayı Kütüphaneden Sil",
            $"'{def.displayName}' parçası kütüphaneden silinecek.\n\nDosya: {path}\n\nBu işlem geri alınamaz. Emin misiniz?",
            "Evet, Sil", "İptal");

        if (confirmed)
        {
            if (selectedDefinition == def) selectedDefinition = null;
            if (editingDefinition == def) editingDefinition = null;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshDefinitionList();
        }
    }

    private void DrawCustomPieceCreator()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical(styleBox);

        EditorGUILayout.BeginHorizontal();
        string foldoutTitle = editingDefinition != null
            ? $"✏️ DÜZENLENİYOR: {editingDefinition.displayName} (Yeniden çizebilir veya ayarları değiştirebilirsiniz)"
            : "🎨 YENİ ÖZEL PARÇA TASARLA VE KÜTÜPHANEYE EKLE";
        showCustomPieceCreator = EditorGUILayout.Foldout(showCustomPieceCreator, foldoutTitle, true, EditorStyles.foldoutHeader);
        
        if (editingDefinition != null)
        {
            if (GUILayout.Button("İptal / Yeniye Dön", GUILayout.Width(130)))
            {
                editingDefinition = null;
                customPieceName = "Özel_Parça_1";
                customRotationMode = PieceRotationMode.FixedAsDrawn;
                customGridCells = new bool[5, 5];
            }
        }
        EditorGUILayout.EndHorizontal();

        if (showCustomPieceCreator)
        {
            EditorGUILayout.HelpBox(
                "İstediğiniz şekli aşağıdaki ızgara üzerinde tıklayarak çizebilir, isimlendirebilir ve kütüphaneye " +
                "ekleyebilirsiniz. Eklenen parçalar seviye/katman oluşturulurken katmanı tam kaplayacak şekilde otomatik referans alınır.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            // Sol taraf: Çizim Izgarası
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            EditorGUILayout.LabelField("Şekil Çizim Alanı (Hücrelere Tıklayın):", EditorStyles.boldLabel);

            int activeCount = 0;
            for (int z = 0; z < customGridHeight; z++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int x = 0; x < customGridWidth; x++)
                {
                    bool active = customGridCells[x, z];
                    if (active) activeCount++;

                    Color prevBG = GUI.backgroundColor;
                    GUI.backgroundColor = active ? new Color(0.35f, 0.78f, 1.00f) : new Color(0.25f, 0.25f, 0.28f);
                    
                    if (GUILayout.Button(active ? "█" : "·", GUILayout.Width(36), GUILayout.Height(36)))
                    {
                        customGridCells[x, z] = !active;
                    }
                    GUI.backgroundColor = prevBG;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Hacim: {activeCount} Blok", EditorStyles.boldLabel);

            // Hazır Şekil / Temizleme Butonları
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Temizle", GUILayout.Width(65)))
            {
                customGridCells = new bool[5, 5];
            }
            if (GUILayout.Button("L-Şekli", GUILayout.Width(65)))
            {
                customGridCells = new bool[5, 5];
                customGridCells[0, 0] = true; customGridCells[0, 1] = true; customGridCells[0, 2] = true; customGridCells[1, 0] = true;
            }
            if (GUILayout.Button("T-Şekli", GUILayout.Width(65)))
            {
                customGridCells = new bool[5, 5];
                customGridCells[0, 1] = true; customGridCells[1, 1] = true; customGridCells[2, 1] = true; customGridCells[1, 0] = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("I-Şekli", GUILayout.Width(65)))
            {
                customGridCells = new bool[5, 5];
                customGridCells[0, 0] = true; customGridCells[0, 1] = true; customGridCells[0, 2] = true; customGridCells[0, 3] = true;
            }
            if (GUILayout.Button("O-Kare", GUILayout.Width(65)))
            {
                customGridCells = new bool[5, 5];
                customGridCells[0, 0] = true; customGridCells[0, 1] = true; customGridCells[1, 0] = true; customGridCells[1, 1] = true;
            }
            if (GUILayout.Button("Z-Şekli", GUILayout.Width(65)))
            {
                customGridCells = new bool[5, 5];
                customGridCells[0, 0] = true; customGridCells[0, 1] = true; customGridCells[1, 1] = true; customGridCells[1, 2] = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            GUILayout.Space(16);

            // Sağ taraf: Meta-veri ve Kayıt Ayarları
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Parça Özellikleri:", EditorStyles.boldLabel);

            customPieceName = EditorGUILayout.TextField("Parça Adı", customPieceName);
            customRotationMode = (PieceRotationMode)EditorGUILayout.Popup("Rotasyon Modu", (int)customRotationMode, ROTATION_MODE_LABELS);
            customSpawnWeight = EditorGUILayout.FloatField("Spawn Ağırlığı", customSpawnWeight);
            customMaxCopies = EditorGUILayout.IntField("Katman Başı Maks. Kopya (-1=sınırsız)", customMaxCopies);
            customTags = EditorGUILayout.TextField("Zorluk Etiketleri (opsiyonel)", customTags);

            EditorGUILayout.Space(8);

            GUI.backgroundColor = editingDefinition != null ? new Color(0.25f, 0.65f, 0.95f) : new Color(0.20f, 0.75f, 0.40f);
            string btnText = editingDefinition != null ? "💾  Değişiklikleri Kaydet & Güncelle" : "➕  Parça Kütüphanesine Kaydet ve Aktif Et";
            if (GUILayout.Button(btnText, GUILayout.Height(38)))
            {
                SaveCustomPieceToLibrary();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private void SaveCustomPieceToLibrary()
    {
        var rawCells = new List<Vector3Int>();
        for (int x = 0; x < 5; x++)
        {
            for (int z = 0; z < 5; z++)
            {
                if (customGridCells[x, z])
                {
                    rawCells.Add(new Vector3Int(x, 0, z));
                }
            }
        }

        if (rawCells.Count == 0)
        {
            EditorUtility.DisplayDialog("Uyarı", "Lütfen önce ızgara üzerinde en az 1 kare seçerek bir şekil çizin.", "Tamam");
            return;
        }

        // Orijine normalize et (en küçük koordinatı 0'a çek)
        int minX = rawCells.Min(c => c.x);
        int minZ = rawCells.Min(c => c.z);
        var normCells = rawCells.Select(c => new Vector3Int(c.x - minX, 0, c.z - minZ)).ToList();

        if (editingDefinition != null)
        {
            // Var olan parçayı güncelle
            editingDefinition.displayName = string.IsNullOrEmpty(customPieceName) ? "Özel Parça" : customPieceName;
            editingDefinition.cells = normCells;
            editingDefinition.rotationMode = customRotationMode;
            editingDefinition.spawnWeight = Mathf.Max(0.1f, customSpawnWeight);
            editingDefinition.maxCopiesPerLevel = customMaxCopies;

            if (!string.IsNullOrEmpty(customTags))
            {
                editingDefinition.difficultyTags = customTags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
            }
            else
            {
                editingDefinition.difficultyTags.Clear();
            }

            editingDefinition.RecomputeDerived();
            EditorUtility.SetDirty(editingDefinition);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string updatedName = editingDefinition.displayName;
            editingDefinition = null;

            RefreshDefinitionList();

            EditorUtility.DisplayDialog(
                "Güncellendi!",
                $"✅ '{updatedName}' parçası kütüphanede başarıyla güncellendi!\n\n" +
                "Level ve katman oluşturulurken güncellenmiş parça referans alınacaktır.",
                "Harika!");
        }
        else
        {
            // Yeni parça oluştur
            if (!AssetDatabase.IsValidFolder(OUTPUT_FOLDER))
                AssetDatabase.CreateFolder("Assets", "PieceDefinitions");

            string cleanName = string.IsNullOrEmpty(customPieceName) ? "Ozel_Parca" : customPieceName.Replace(" ", "_");
            string assetPath = $"{OUTPUT_FOLDER}/{cleanName}_PD_{System.Guid.NewGuid().ToString().Substring(0, 8)}.asset";

            var def = ScriptableObject.CreateInstance<PieceDefinition>();
            def.displayName = string.IsNullOrEmpty(customPieceName) ? "Özel Parça" : customPieceName;
            def.cells = normCells;
            def.rotationMode = customRotationMode;
            def.spawnWeight = Mathf.Max(0.1f, customSpawnWeight);
            def.maxCopiesPerLevel = customMaxCopies;

            if (!string.IsNullOrEmpty(customTags))
            {
                def.difficultyTags = customTags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();
            }

            def.RecomputeDerived();

            AssetDatabase.CreateAsset(def, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RefreshDefinitionList();
            selectedDefinition = def;

            EditorUtility.DisplayDialog(
                "Başarılı!",
                $"✅ '{def.displayName}' parçası başarıyla Kütüphaneye eklendi!\n\n" +
                $"Konum: {assetPath}\n\n" +
                "Seviye ve katman oluşturulurken bu parça doğrudan referans alınacaktır.",
                "Harika!");
        }
    }
}
