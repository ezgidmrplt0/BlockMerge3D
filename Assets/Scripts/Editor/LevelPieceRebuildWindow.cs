using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class LevelPieceRebuildWindow : EditorWindow
{
    private const string LEVELS_PATH = "Assets/Levels";
    private const string PIECE_DEFS_PATH = "Assets/PieceDefinitions";

    private Vector2 scrollPos;
    private List<string> reportLines = new List<string>();
    private List<string> warnings = new List<string>();
    private bool running;

    [MenuItem("BlockMerge3D/Parcalari Yeniden Olustur")]
    public static void ShowWindow()
    {
        var w = GetWindow<LevelPieceRebuildWindow>("Parca Rebuild");
        w.minSize = new Vector2(560, 420);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("PARCA REBUILD", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Tum levellarin complementaryPieces listesini sifirlar ve " +
            "PieceDefinitions kutuphanesinden SolutionFirstBuilder ile " +
            "yeni parcalar olusturur. Her parca originLayerY ile etiketlenir.",
            MessageType.Info);

        EditorGUILayout.Space(6);

        GUI.enabled = !running;
        if (GUILayout.Button("Tum Levellari Rebuild Et", GUILayout.Height(32)))
        {
            RunRebuild();
        }
        GUI.enabled = true;

        if (reportLines.Count > 0 || warnings.Count > 0)
        {
            EditorGUILayout.Space(6);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            if (reportLines.Count > 0)
            {
                foreach (var line in reportLines)
                    EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            }

            if (warnings.Count > 0)
            {
                EditorGUILayout.Space(4);
                var warnStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                warnStyle.normal.textColor = new Color(0.95f, 0.70f, 0.20f);
                EditorGUILayout.LabelField($"{warnings.Count} uyari:", EditorStyles.miniBoldLabel);
                foreach (var w in warnings)
                    EditorGUILayout.LabelField(w, warnStyle);
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private List<PieceDefinition> LoadPieceLibrary()
    {
        var result = new List<PieceDefinition>();
        if (!AssetDatabase.IsValidFolder(PIECE_DEFS_PATH)) return result;

        var guids = AssetDatabase.FindAssets("t:PieceDefinition", new[] { PIECE_DEFS_PATH });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var def = AssetDatabase.LoadAssetAtPath<PieceDefinition>(path);
            if (def == null || def.cells == null || def.cells.Count == 0) continue;
            if (!IsFlattenableToSingleLayer(def)) continue;
            result.Add(def);
        }
        return result;
    }

    private static bool IsFlattenableToSingleLayer(PieceDefinition def)
    {
        var rotations = (def.allowedRotations != null && def.allowedRotations.Count > 0)
            ? def.allowedRotations
            : new List<Vector3Int> { Vector3Int.zero };

        foreach (var rotEuler in rotations)
        {
            var rotated = PieceGeometryUtils.RotateAndNormalize(
                def.cells, Quaternion.Euler(rotEuler.x, rotEuler.y, rotEuler.z));
            if (rotated.Count == 0) continue;
            int minY = rotated[0].y, maxY = rotated[0].y;
            foreach (var c in rotated) { if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y; }
            if (minY == maxY) return true;
        }
        return false;
    }

    private void RunRebuild()
    {
        running = true;
        reportLines.Clear();
        warnings.Clear();

        var library = LoadPieceLibrary();
        if (library.Count == 0)
        {
            warnings.Add("Assets/PieceDefinitions/ altinda hic PieceDefinition yok!");
            running = false;
            return;
        }

        reportLines.Add($"Kutuphane: {library.Count} parca tanimi yuklendi");

        // Oncelik: EditorPrefs'te kayitli prefab, yoksa Untitled1.fbx, yoksa SingleCube
        string cubePrefabPath = EditorPrefs.GetString("BlockMerge3D_DefaultCubePrefab", "");
        GameObject cubePrefab = null;
        if (!string.IsNullOrEmpty(cubePrefabPath))
            cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cubePrefabPath);
        if (cubePrefab == null)
            cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Untitled1.fbx");
        if (cubePrefab == null)
            cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SingleCube.prefab");

        reportLines.Add($"Kup prefab: {(cubePrefab != null ? cubePrefab.name : "BULUNAMADI — varsayilan kup")}");


        var guids = AssetDatabase.FindAssets("t:LevelData", new[] { LEVELS_PATH });
        int total = 0, success = 0, failed = 0, totalPieces = 0;

        foreach (var guid in guids)
        {
            string ldPath = AssetDatabase.GUIDToAssetPath(guid);
            var ld = AssetDatabase.LoadAssetAtPath<LevelData>(ldPath);
            if (ld == null) continue;
            total++;

            if (ld.mainShapePrefab == null)
            {
                warnings.Add($"'{ld.levelName}': mainShapePrefab yok");
                failed++;
                continue;
            }

            var mainHolder = ld.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
            if (mainHolder == null)
            {
                warnings.Add($"'{ld.levelName}': CubeShapeDataHolder yok");
                failed++;
                continue;
            }

            var cellsToFill = new HashSet<Vector3Int>(mainHolder.occupiedCells);
            cellsToFill.ExceptWith(mainHolder.prefilledCells ?? new List<Vector3Int>());

            if (cellsToFill.Count == 0)
            {
                warnings.Add($"'{ld.levelName}': doldurulacak hucre yok");
                failed++;
                continue;
            }

            // Buz bilgisini oku
            var frozenSet = new HashSet<Vector3Int>(mainHolder.frozenCells ?? new List<Vector3Int>());

            int volume = cellsToFill.Count;
            int stateLimit = volume < 50 ? 50000 : volume < 100 ? 150000 : 300000;
            int timeLimitMs = volume < 50 ? 5000 : volume < 100 ? 10000 : 20000;

            // Tüm hücreleri çöz (buz yokmuş gibi)
            SolutionFirstBuilder.PreferVariety = true;

            bool solved = SolutionFirstBuilder.TryBuild(
                cellsToFill, mainHolder.gridSize, library, stateLimit, timeLimitMs,
                out var resultPieces);

            SolutionFirstBuilder.PreferVariety = false;

            if (!solved)
            {
                warnings.Add($"'{ld.levelName}': SolutionFirstBuilder cozum bulamadi (volume={volume})");
                failed++;
                continue;
            }

            // Buz varsa feda parçaları üret
            var sacrificePieces = new List<List<Vector3Int>>();
            if (frozenSet.Count > 0)
            {
                sacrificePieces = GenerateSacrificePieces(
                    frozenSet, cellsToFill, mainHolder, library);
                if (sacrificePieces.Count > 0)
                    reportLines.Add($"'{ld.levelName}': {sacrificePieces.Count} feda parcasi");
            }

            // Birleştir: normal parçalar + feda parçaları
            var allPieces = new List<List<Vector3Int>>(resultPieces);
            int sacrificeStartIdx = allPieces.Count;
            allPieces.AddRange(sacrificePieces);

            string levelDir = Path.GetDirectoryName(ldPath);

            // Eski parca prefab'larini sil
            DeleteOldPiecePrefabs(levelDir, ld.levelName);

            // Y'ye göre sırala (BakePieces de aynı sırayı kullanıyor)
            // Feda parçalarının indekslerini sıralama sonrası da takip et
            var indexed = allPieces.Select((cells, i) => new { cells, origIdx = i }).ToList();
            indexed = indexed
                .OrderBy(x => x.cells.Count > 0 ? x.cells.Min(c => c.y) : 0)
                .ToList();
            allPieces = indexed.Select(x => x.cells).ToList();
            var sacrificeFlags = indexed.Select(x => x.origIdx >= sacrificeStartIdx).ToList();

            // Yeni parcalari olustur
            var piecePrefabs = BakePieces(allPieces, levelDir, ld.levelName, mainHolder, cubePrefab);

            ld.complementaryPieces = piecePrefabs;

            // Çözüm haritasını kaydet
            ld.precomputedSolution = BuildSolutionMap(allPieces, piecePrefabs, sacrificeFlags);

            EditorUtility.SetDirty(ld);

            success++;
            totalPieces += piecePrefabs.Count;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        reportLines.Add($"Taranan: {total}");
        reportLines.Add($"Basarili: {success}");
        reportLines.Add($"Basarisiz: {failed}");
        reportLines.Add($"Toplam olusturulan parca: {totalPieces}");

        Debug.Log($"[PieceRebuild] {success}/{total} level rebuild edildi, {totalPieces} parca olusturuldu, {failed} basarisiz.");
        running = false;
    }

    private List<PiecePlacement> BuildSolutionMap(
        List<List<Vector3Int>> resultPieces,
        List<GameObject> piecePrefabs,
        List<bool> sacrificeFlags = null)
    {
        var solution = new List<PiecePlacement>();
        for (int i = 0; i < piecePrefabs.Count; i++)
        {
            var placement = new PiecePlacement();
            if (i < resultPieces.Count)
                placement.targetWorldCells = new List<Vector3Int>(resultPieces[i]);
            if (sacrificeFlags != null && i < sacrificeFlags.Count)
                placement.isSacrifice = sacrificeFlags[i];
            solution.Add(placement);
        }
        return solution;
    }

    private List<List<Vector3Int>> GenerateSacrificePieces(
        HashSet<Vector3Int> frozenSet,
        HashSet<Vector3Int> allFillable,
        CubeShapeDataHolder mainHolder,
        List<PieceDefinition> library)
    {
        var result = new List<List<Vector3Int>>();
        if (frozenSet.Count == 0) return result;

        Vector3Int[] hNeighbors = {
            Vector3Int.left, Vector3Int.right,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        // Buzlu hücreleri bağlı gruplara ayır
        var visited = new HashSet<Vector3Int>();
        var frozenGroups = new List<List<Vector3Int>>();
        foreach (var fc in frozenSet)
        {
            if (visited.Contains(fc)) continue;
            var group = new List<Vector3Int>();
            var stack = new Stack<Vector3Int>();
            stack.Push(fc);
            visited.Add(fc);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                group.Add(cur);
                foreach (var off in hNeighbors)
                {
                    var n = cur + off;
                    if (frozenSet.Contains(n) && visited.Add(n))
                        stack.Push(n);
                }
            }
            frozenGroups.Add(group);
        }

        // Her frozen-cell hitCount ayrı ayrı hesaplanmalı ama basitlik için
        // her grup için bir feda parçası üret (tek vuruşta erime varsayımı)
        // Çoklu vuruş desteği: gruptaki max hitCount kadar feda parçası üret
        foreach (var group in frozenGroups)
        {
            int hitsNeeded = 1;
            if (mainHolder.frozenHitCounts != null)
            {
                foreach (var fc in group)
                {
                    int idx = (mainHolder.frozenCells != null) ? mainHolder.frozenCells.IndexOf(fc) : -1;
                    if (idx >= 0 && idx < mainHolder.frozenHitCounts.Count)
                        hitsNeeded = Mathf.Max(hitsNeeded, mainHolder.frozenHitCounts[idx]);
                }
            }

            // Buzun yanındaki boş (donmamış) hücreleri bul
            var adjacentFree = new HashSet<Vector3Int>();
            foreach (var fc in group)
            {
                foreach (var off in hNeighbors)
                {
                    var n = fc + off;
                    if (allFillable.Contains(n) && !frozenSet.Contains(n))
                        adjacentFree.Add(n);
                }
            }

            if (adjacentFree.Count < 2) continue;

            for (int hit = 0; hit < hitsNeeded; hit++)
            {
                // 2 hücreli feda parçası bul: bitişik iki boş hücre, en az biri buza komşu
                List<Vector3Int> sacrificeCells = null;
                foreach (var cell in adjacentFree)
                {
                    foreach (var off in hNeighbors)
                    {
                        var pair = cell + off;
                        if (pair != cell && allFillable.Contains(pair) && !frozenSet.Contains(pair))
                        {
                            sacrificeCells = new List<Vector3Int> { cell, pair };
                            break;
                        }
                    }
                    if (sacrificeCells != null) break;
                }

                if (sacrificeCells != null)
                    result.Add(sacrificeCells);
            }
        }

        return result;
    }

    private void DeleteOldPiecePrefabs(string levelDir, string levelName)
    {
        var existingPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { levelDir });
        foreach (var pg in existingPrefabs)
        {
            string pPath = AssetDatabase.GUIDToAssetPath(pg);
            string fileName = Path.GetFileNameWithoutExtension(pPath);
            if (fileName.StartsWith(levelName + "_Piece_"))
            {
                AssetDatabase.DeleteAsset(pPath);
            }
        }
    }

    private List<GameObject> BakePieces(
        List<List<Vector3Int>> resultPieces,
        string levelDir,
        string levelName,
        CubeShapeDataHolder mainHolder,
        GameObject cubePrefab)
    {
        float cellSize = mainHolder.cellSize > 0 ? mainHolder.cellSize : 1f;
        float spacing = mainHolder.spacing;
        float step = cellSize + spacing;

        var canonCache = new Dictionary<string, GameObject>();
        var piecePrefabs = new List<GameObject>();
        int bakedCount = 0;

        foreach (var worldCells in resultPieces)
        {
            if (worldCells.Count == 0) continue;

            int solutionLayerY = worldCells.Min(c => c.y);

            // Normalize: shift to origin
            int minX = worldCells.Min(c => c.x);
            int minY = worldCells.Min(c => c.y);
            int minZ = worldCells.Min(c => c.z);
            var shift = new Vector3Int(minX, minY, minZ);
            var normCells = worldCells.Select(c => c - shift).ToList();

            // Canonical dedup
            var canonSig = PieceGeometryUtils.ComputeCanonicalSignature(normCells);
            string dedupKey = $"{canonSig}@{solutionLayerY}";
            if (canonCache.TryGetValue(dedupKey, out var shared))
            {
                piecePrefabs.Add(shared);
                continue;
            }

            bakedCount++;
            string pPath = $"{levelDir}/{levelName}_Piece_{bakedCount}.prefab";

            GameObject pRoot = new GameObject($"{levelName}_Piece_{bakedCount}");
            var ph = pRoot.AddComponent<CubeShapeDataHolder>();
            ph.shapeName = $"{levelName}_Piece_{bakedCount}";
            ph.gridSize = mainHolder.gridSize;
            ph.cellSize = cellSize;
            ph.spacing = spacing;
            ph.occupiedCells = new List<Vector3Int>(normCells);
            ph.originLayerY = solutionLayerY;

            foreach (var cell in normCells)
            {
                GameObject cube;
                if (cubePrefab != null)
                    cube = (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab);
                else
                    cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

                cube.transform.SetParent(pRoot.transform);
                cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
                cube.transform.localScale = Vector3.one * cellSize;
                cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            }

            GameObject savedPiece = PrefabUtility.SaveAsPrefabAsset(pRoot, pPath);
            DestroyImmediate(pRoot);
            piecePrefabs.Add(savedPiece);
            canonCache[dedupKey] = savedPiece;
        }

        return piecePrefabs;
    }
}
