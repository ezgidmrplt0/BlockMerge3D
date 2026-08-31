using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class LevelLayerTagMigrationWindow : EditorWindow
{
    private const string LEVELS_PATH = "Assets/Levels";

    private static readonly Color COL_HEADER = new Color(0.35f, 0.78f, 1.00f);
    private static readonly Color COL_WARN   = new Color(0.95f, 0.70f, 0.20f);

    private List<string> lastReportLines = new List<string>();
    private List<string> lastWarnings = new List<string>();

    private GUIStyle styleHeader, styleBox;
    private bool stylesBuilt;

    private struct PieceEntry
    {
        public GameObject prefab;
        public CubeShapeDataHolder holder;
    }

    [MenuItem("BlockMerge3D/Katman Etiketlerini Migrate Et")]
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
        GUILayout.Label("KATMAN ETIKETI MIGRATION", styleHeader);
        EditorGUILayout.HelpBox(
            "Assets/Levels altindaki her LevelData icin, complementaryPieces'daki her parcanin " +
            "hangi katman (dunya Y'si) icin cozuldugunu tureterek CubeShapeDataHolder.originLayerY " +
            "alanina yazar. Oncelikle SolutionFirstBuilder dener, basarisiz olursa brute-force " +
            "fitting ile her parcanin sigdigi katmani bulur.",
            MessageType.Info);

        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
        if (GUILayout.Button("Tum Seviyeleri Migrate Et", GUILayout.Height(32)))
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
            GUILayout.Label($"  {lastWarnings.Count} uyari:", EditorStyles.miniBoldLabel);
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
        int fallbackCount = 0;

        var guids = AssetDatabase.FindAssets("t:LevelData", new[] { LEVELS_PATH });
        foreach (var guid in guids)
        {
            string ldPath = AssetDatabase.GUIDToAssetPath(guid);
            var ld = AssetDatabase.LoadAssetAtPath<LevelData>(ldPath);
            if (ld == null) continue;

            levelCount++;

            if (!MigrateLevel(ld, out int taggedInLevel, out string failReason, out bool usedFallback))
            {
                failedCount++;
                lastWarnings.Add($"'{ld.levelName}': {failReason}");
                continue;
            }

            migratedCount++;
            taggedPieceCount += taggedInLevel;
            if (usedFallback) fallbackCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        lastReportLines = new List<string>
        {
            $"Taranan seviye: {levelCount}",
            $"Basariyla migrate edilen: {migratedCount} (solver: {migratedCount - fallbackCount}, fallback: {fallbackCount})",
            $"Etiketlenen parca: {taggedPieceCount}",
            $"Basarisiz/atlanan: {failedCount}",
        };

        Debug.Log($"[LevelLayerTagMigration] {migratedCount}/{levelCount} seviye migrate edildi, {taggedPieceCount} parca etiketlendi, {failedCount} basarisiz, {fallbackCount} fallback.");
    }

    private bool MigrateLevel(LevelData ld, out int taggedCount, out string failReason, out bool usedFallback)
    {
        taggedCount = 0;
        failReason = null;
        usedFallback = false;

        if (ld.mainShapePrefab == null) { failReason = "mainShapePrefab yok"; return false; }
        var mainHolder = ld.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
        if (mainHolder == null) { failReason = "ana sekilde CubeShapeDataHolder yok"; return false; }

        var pieceEntries = new List<PieceEntry>();
        foreach (var p in ld.complementaryPieces ?? new List<GameObject>())
        {
            if (p == null) continue;
            var h = p.GetComponent<CubeShapeDataHolder>();
            if (h != null && h.occupiedCells != null && h.occupiedCells.Count > 0)
                pieceEntries.Add(new PieceEntry { prefab = p, holder = h });
        }

        if (pieceEntries.Count == 0) { failReason = "hic gecerli complementary piece yok"; return false; }

        var cellsToFill = new HashSet<Vector3Int>(mainHolder.occupiedCells);
        cellsToFill.ExceptWith(mainHolder.prefilledCells ?? new List<Vector3Int>());
        cellsToFill.ExceptWith(mainHolder.frozenCells ?? new List<Vector3Int>());

        if (cellsToFill.Count == 0) { failReason = "doldurulacak hedef hucre yok"; return false; }

        // 1. Yol: SolutionFirstBuilder ile tam cozum
        if (TrySolverMigration(ld, pieceEntries, cellsToFill, mainHolder.gridSize, out taggedCount))
            return true;

        // 2. Yol: Brute-force fitting fallback
        taggedCount = FallbackTagByFitting(ld, pieceEntries, cellsToFill);
        if (taggedCount > 0)
        {
            usedFallback = true;
            return true;
        }

        failReason = "solver ve fallback ikisi de basarisiz";
        return false;
    }

    private bool TrySolverMigration(LevelData ld, List<PieceEntry> pieceEntries, HashSet<Vector3Int> cellsToFill, Vector3Int gridSize, out int taggedCount)
    {
        taggedCount = 0;
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
                cellsToFill, gridSize, tempDefs, stateLimit, timeLimitMs, out var resultPieces);

            if (!solved) return false;

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
                    lastWarnings.Add($"'{ld.levelName}' > '{entry.prefab.name}': eslesen katman grubu bulunamadi.");
                    continue;
                }

                var worldCells = q.Dequeue();
                int layerY = worldCells.Min(c => c.y);
                WritePieceOriginLayer(entry.prefab, layerY, ref taggedCount);
            }

            return true;
        }
        finally
        {
            foreach (var def in tempDefs)
                Object.DestroyImmediate(def);
        }
    }

    /// <summary>
    /// Solver basarisiz oldugunda: her parcanin hucre seklini grid uzerinde tum
    /// olasi offsetlerde deneyip sigdigi yerin min Y'sini originLayerY olarak yazar.
    /// </summary>
    private int FallbackTagByFitting(LevelData ld, List<PieceEntry> pieceEntries, HashSet<Vector3Int> cellsToFill)
    {
        int tagged = 0;

        int minX = cellsToFill.Min(c => c.x);
        int maxX = cellsToFill.Max(c => c.x);
        int minY = cellsToFill.Min(c => c.y);
        int maxY = cellsToFill.Max(c => c.y);
        int minZ = cellsToFill.Min(c => c.z);
        int maxZ = cellsToFill.Max(c => c.z);

        Quaternion[] rotations = new Quaternion[]
        {
            Quaternion.identity,
            Quaternion.Euler(0, 90, 0),
            Quaternion.Euler(0, 180, 0),
            Quaternion.Euler(0, 270, 0)
        };

        foreach (var entry in pieceEntries)
        {
            var baseCells = entry.holder.occupiedCells;
            int bestLayerY = -1;
            int bestScore = -1;

            foreach (var rot in rotations)
            {
                var cells = RotateCells(baseCells, rot);

                int pcMinX = cells.Min(c => c.x);
                int pcMinY = cells.Min(c => c.y);
                int pcMinZ = cells.Min(c => c.z);

                for (int oy = minY - pcMinY; oy <= maxY; oy++)
                {
                    for (int ox = minX - pcMinX; ox <= maxX; ox++)
                    {
                        for (int oz = minZ - pcMinZ; oz <= maxZ; oz++)
                        {
                            Vector3Int offset = new Vector3Int(ox, oy, oz);
                            bool allFit = true;
                            int placedMinY = int.MaxValue;

                            foreach (var c in cells)
                            {
                                Vector3Int world = c + offset;
                                if (!cellsToFill.Contains(world))
                                {
                                    allFit = false;
                                    break;
                                }
                                if (world.y < placedMinY) placedMinY = world.y;
                            }

                            if (!allFit) continue;

                            int score = cells.Count * 100 - placedMinY;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestLayerY = placedMinY;
                            }
                        }
                    }
                }
            }

            if (bestLayerY >= 0)
            {
                WritePieceOriginLayer(entry.prefab, bestLayerY, ref tagged);
            }
            else
            {
                lastWarnings.Add($"'{ld.levelName}' > '{entry.prefab.name}': fallback da sigdiramadi.");
            }
        }

        return tagged;
    }

    private static List<Vector3Int> RotateCells(List<Vector3Int> cells, Quaternion rot)
    {
        if (rot == Quaternion.identity) return cells;
        var result = new List<Vector3Int>(cells.Count);
        foreach (var c in cells)
        {
            Vector3 rotated = rot * new Vector3(c.x, c.y, c.z);
            result.Add(new Vector3Int(
                Mathf.RoundToInt(rotated.x),
                Mathf.RoundToInt(rotated.y),
                Mathf.RoundToInt(rotated.z)));
        }
        return result;
    }

    private void WritePieceOriginLayer(GameObject prefab, int layerY, ref int taggedCount)
    {
        string piecePath = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(piecePath)) return;

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
}
