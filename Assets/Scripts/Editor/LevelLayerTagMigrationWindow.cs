using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL LAYER TAG MIGRATION  —  Mevcut Seviyelerin Parçalarını Katmanla Etiketleme
//  BlockMerge3D  •  Sıralı katman mekaniği (bkz. Docs/SiraliKatmanMekanigi_Tasarim.md)
//  runtime'da bir parçanın hangi katman için tasarlandığını bilmek zorunda
//  (CubeShapeDataHolder.originLayerY). Bu bilgi bake sırasında normalize edilirken
//  atıldığı için (bkz. AILevelDesignerWindow.ExportProceduralLevelCore), mevcut
//  seviyelerde hiçbir yerde saklı değil — SolutionFirstBuilder'ı seviyenin KENDİ
//  (sabit, zaten doğru yönde) parçalarıyla yeniden çalıştırıp hangi parçanın hangi
//  katmana denk düştüğünü geri çıkarıyoruz. Tek seferlik, elle tetiklenen bir araç.
// ═══════════════════════════════════════════════════════════════════

public class LevelLayerTagMigrationWindow : EditorWindow
{
    private const string LEVELS_PATH = "Assets/Levels";

    private static readonly Color COL_HEADER = new Color(0.35f, 0.78f, 1.00f);
    private static readonly Color COL_WARN   = new Color(0.95f, 0.70f, 0.20f);

    private List<string> lastReportLines = new List<string>();
    private List<string> lastWarnings = new List<string>();

    private GUIStyle styleHeader, styleBox;
    private bool stylesBuilt;

    [MenuItem("BlockMerge3D/🔧 Katman Etiketlerini Migrate Et")]
    public static void ShowWindow()
    {
        var w = GetWindow<LevelLayerTagMigrationWindow>("Katman Etiketi Migration");
        w.minSize = new Vector2(560, 420);
    }

    private void BuildStyles()
    {
        if (stylesBuilt) return;
        styleHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        styleHeader.normal.textColor = COL_HEADER;
        styleBox = new GUIStyle(GUI.skin.box);
        stylesBuilt = true;
    }

    private void OnGUI()
    {
        BuildStyles();

        EditorGUILayout.Space(6);
        GUILayout.Label("🔧 KATMAN ETİKETİ MIGRATION", styleHeader);
        EditorGUILayout.HelpBox(
            "Assets/Levels altındaki her LevelData için, complementaryPieces'daki her parçanın " +
            "hangi katman (dünya Y'si) için çözüldüğünü SolutionFirstBuilder ile yeniden türetir " +
            "ve CubeShapeDataHolder.originLayerY alanına yazar. Seviyenin kendi (sabit, zaten doğru " +
            "yönde) parçaları dışında hiçbir şey değiştirilmez/eklenmez. Tek seferlik bir araçtır.",
            MessageType.Info);

        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
        if (GUILayout.Button("🔧  Tüm Seviyeleri Migrate Et", GUILayout.Height(32)))
        {
            RunMigration();
        }
        GUI.backgroundColor = Color.white;

        if (lastReportLines.Count > 0)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(styleBox);
            foreach (var line in lastReportLines)
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        if (lastWarnings.Count > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(styleBox);
            var warnStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            warnStyle.normal.textColor = COL_WARN;
            GUILayout.Label($"⚠ {lastWarnings.Count} uyarı — manuel kontrol gerekebilir:", EditorStyles.miniBoldLabel);
            foreach (var w in lastWarnings)
                EditorGUILayout.LabelField(w, warnStyle);
            EditorGUILayout.EndVertical();
        }
    }

    private void RunMigration()
    {
        lastReportLines.Clear();
        lastWarnings.Clear();

        int levelCount = 0, migratedCount = 0, taggedPieceCount = 0, failedCount = 0;

        var guids = AssetDatabase.FindAssets("t:LevelData", new[] { LEVELS_PATH });
        foreach (var guid in guids)
        {
            string ldPath = AssetDatabase.GUIDToAssetPath(guid);
            var ld = AssetDatabase.LoadAssetAtPath<LevelData>(ldPath);
            if (ld == null) continue;

            levelCount++;

            if (!MigrateLevel(ld, out int taggedInLevel, out string failReason))
            {
                failedCount++;
                lastWarnings.Add($"'{ld.levelName}' migrate edilemedi: {failReason}");
                continue;
            }

            migratedCount++;
            taggedPieceCount += taggedInLevel;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        lastReportLines = new List<string>
        {
            $"Taranan seviye: {levelCount}",
            $"Başarıyla migrate edilen: {migratedCount}",
            $"Etiketlenen parça: {taggedPieceCount}",
            $"Başarısız/atlanan: {failedCount}",
        };

        Debug.Log($"[LevelLayerTagMigration] {migratedCount}/{levelCount} seviye migrate edildi, {taggedPieceCount} parça etiketlendi, {failedCount} başarısız.");
    }

    /// <summary>
    /// Tek bir seviyeyi migrate eder: ld.complementaryPieces'daki her parçanın normalize edilmiş
    /// (öteleme çıkarılmış) hücrelerinden, rotasyon araması YAPMADAN (parçalar zaten çözümdeki
    /// yönde), sabit bir aday havuzu kurar ve SolutionFirstBuilder ile ana şeklin hedef
    /// hücrelerini (prefilled/frozen hariç) tam olarak döşemeyi dener. Başarılı olursa, sonuçtaki
    /// her parça grubunu (kanonik imzaya göre) orijinal parçayla eşleştirip originLayerY'yi yazar.
    /// </summary>
    private bool MigrateLevel(LevelData ld, out int taggedCount, out string failReason)
    {
        taggedCount = 0;
        failReason = null;

        if (ld.mainShapePrefab == null) { failReason = "mainShapePrefab yok"; return false; }
        var mainHolder = ld.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
        if (mainHolder == null) { failReason = "ana şekilde CubeShapeDataHolder yok"; return false; }

        var pieceEntries = (ld.complementaryPieces ?? new List<GameObject>())
            .Where(p => p != null)
            .Select(p => new { prefab = p, holder = p.GetComponent<CubeShapeDataHolder>() })
            .Where(e => e.holder != null && e.holder.occupiedCells != null && e.holder.occupiedCells.Count > 0)
            .ToList();

        if (pieceEntries.Count == 0) { failReason = "hiç geçerli complementary piece yok"; return false; }

        var cellsToFill = new HashSet<Vector3Int>(mainHolder.occupiedCells);
        cellsToFill.ExceptWith(mainHolder.prefilledCells ?? new List<Vector3Int>());
        cellsToFill.ExceptWith(mainHolder.frozenCells ?? new List<Vector3Int>());

        if (cellsToFill.Count == 0) { failReason = "doldurulacak hedef hücre yok"; return false; }

        // Her gerçek parça için, sabit (rotasyonsuz) bir aday tanım — parça zaten çözümdeki
        // yönde olduğu için tekrar rotasyon aramaya gerek yok, sadece X/Z/katman konumu aranıyor.
        var tempDefs = new List<PieceDefinition>(pieceEntries.Count);
        try
        {
            foreach (var entry in pieceEntries)
            {
                var def = ScriptableObject.CreateInstance<PieceDefinition>();
                def.cells = new List<Vector3Int>(entry.holder.occupiedCells);
                def.volume = def.cells.Count;
                def.spawnWeight = 1f;
                def.maxCopiesPerLevel = 1;
                def.allowedRotations = new List<Vector3Int> { Vector3Int.zero };
                tempDefs.Add(def);
            }

            int volume = cellsToFill.Count;
            int stateLimit  = volume < 50 ? 30000 : volume < 100 ? 80000 : 150000;
            int timeLimitMs = volume < 50 ? 2000  : volume < 100 ? 4000  : 8000;

            bool solved = SolutionFirstBuilder.TryBuild(
                cellsToFill, mainHolder.gridSize, tempDefs, stateLimit, timeLimitMs, out var resultPieces);

            if (!solved)
            {
                failReason = "SolutionFirstBuilder seviyenin KENDİ parçalarıyla çözüm bulamadı (zaman aşımı olabilir)";
                return false;
            }

            // Sonuç parça gruplarını kanonik (rotasyon bağımsız) imzaya göre kovalara ayır.
            var resultBuckets = new Dictionary<string, Queue<List<Vector3Int>>>();
            foreach (var worldCells in resultPieces)
            {
                var normalized = PieceGeometryUtils.NormalizeCells(worldCells);
                string sig = PieceGeometryUtils.ComputeCanonicalSignature(normalized);
                if (!resultBuckets.TryGetValue(sig, out var q))
                {
                    q = new Queue<List<Vector3Int>>();
                    resultBuckets[sig] = q;
                }
                q.Enqueue(worldCells);
            }

            foreach (var entry in pieceEntries)
            {
                string sig = PieceGeometryUtils.ComputeCanonicalSignature(entry.holder.occupiedCells);
                if (!resultBuckets.TryGetValue(sig, out var q) || q.Count == 0)
                {
                    lastWarnings.Add($"'{ld.levelName}' → '{entry.prefab.name}': eşleşen katman grubu bulunamadı, originLayerY değiştirilmedi.");
                    continue;
                }

                var worldCells = q.Dequeue();
                int layerY = worldCells.Min(c => c.y);

                string piecePath = AssetDatabase.GetAssetPath(entry.prefab);
                if (string.IsNullOrEmpty(piecePath)) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(piecePath);
                try
                {
                    var rootHolder = root.GetComponent<CubeShapeDataHolder>();
                    if (rootHolder != null)
                    {
                        rootHolder.originLayerY = layerY;
                        PrefabUtility.SaveAsPrefabAsset(root, piecePath);
                        taggedCount++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return true;
        }
        finally
        {
            foreach (var def in tempDefs)
                Object.DestroyImmediate(def);
        }
    }
}
