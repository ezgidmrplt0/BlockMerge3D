using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL SERIES GENERATOR  —  Claude tarafından tasarlanmış 15 seviyelik
//  öğretici ilerleme (Level1 → Level15). Her seviye dolu bir kutu hedef
//  şekildir; parçalar PieceTemplateLibrary'deki Tetris şablonlarından
//  (I,O,T,S,Z,J,L) + tek hücrelik Filler'dan okunur.
//
//  Level6'dan itibaren taban HER ZAMAN SİMETRİK (NxN) — Level1-5 küçük/
//  dikdörtgen kalıyor (zaten çalışıyorlardı), büyüme oradan sonra hem
//  taban (N: 2→3→4→5→6→7) hem katman (Y) ekleyerek sürüyor.
//
//  Tek sayılı N (3x3, 5x5, 7x7) için matematiksel bir sorun var: tetromino
//  parçaların her biri 4 hücre, yani NxN alanı 4'e bölünmüyorsa (N tek
//  olduğunda N² her zaman tek sayıdır) pürüzsüz tetromino-only döşeme
//  İMKANSIZ. Çözüm: tek hücrelik "Filler" parçası. Örn. 3x3 (9 hücre) —
//  merkeze (1,1) 1 Filler koy, geri kalan 8 hücrelik halka TAM OLARAK
//  2 J parçasıyla (biri 0°, biri 180° döndürülmüş) doldurulabiliyor —
//  bunu elle inşa edip doğruladım. Aynı fikir 5x5, 7x7'de de kullanıldı.
//  ─────────────────────────────────────────────────────────────────
//  Level1   2x1x2  (4 hücre)    — O                  — ilk dokunuş
//  Level2   4x1x2  (8 hücre)    — O                  — ufak büyüme
//  Level3   4x1x3  (12 hücre)   — I                  — yeni parça: I
//  Level4   4x1x4  (16 hücre)   — O + I              — pekiştirme
//  Level5   4x1x4  (16 hücre)   — T                  — yeni parça: T (pinwheel)
//  Level6   2x2x2  (8 hücre)    — O                  — SİMETRİK küpe geçiş
//  Level7   3x2x3  (18 hücre)   — Filler + J         — yeni: Filler, tek sayılı kare
//  Level8   3x3x3  (27 hücre)   — Filler + J         — gerçek 3x3x3, 3 katman
//  Level9   4x2x4  (32 hücre)   — T                  — 4x4 pinwheel x2 katman
//  Level10  4x3x4  (48 hücre)   — T + J + L          — yeni parça: L, 3 katman
//  Level11  5x2x5  (50 hücre)   — Filler+O+I+T+J     — 5x5, büyük karma
//  Level12  5x3x5  (75 hücre)   — + S + Z            — yeni parça: S,Z + BUZ başlıyor
//  Level13  6x2x6  (72 hücre)   — O                  — 6x6 saf nefes molası
//  Level14  6x3x6  (108 hücre)  — O+T+J+L+S+Z        — büyük karma + buz
//  Level15  7x2x7  (98 hücre)   — Filler + tüm 7     — final, en çok buz
//
//  Ana şeklin occupiedCells'i her zaman basit dolu bir kutudur; her
//  seviyenin parça çoklu-kümesi (multiset) toplam hücre sayısıyla TAM
//  eşleşecek şekilde el ile tasarlandı (LevelSolver.SolveFromPrefabs
//  ön-kontrolü bunu zorunlu kılıyor). O/I/T/J/L döşemeleri (2x2 blok,
//  4x4 pinwheel, 4x2 ikili blok, 3x3-halka+Filler) elle doğrulanmış,
//  kesin çalışır. S+Z içeren seviyelerde (12, 14, 15) nihai doğrulama
//  LevelSolver'a bırakıldı — üretim sırasında otomatik çalışıp sonucu
//  raporda gösteriyor zaten.
// ═══════════════════════════════════════════════════════════════════

public static class LevelSeriesGenerator
{
    private class LevelSpec
    {
        public string name;
        public Vector3Int gridSize;
        public float timeLimit;
        public int targetScore;
        public List<(string template, int count)> pieces;
        // Hedef şeklin occupiedCells'i içinden, "buzlu" (ice) olarak işaretlenecek hücreler.
        // Buz bir hücreyi doğrudan parça alamaz hale getirir (GridManager.CanPlace reddeder);
        // yanına aynı renkte bitişik ≥2 hücrelik bir grup gelince erir. Bu yüzden her buz
        // hücresinin en az bir buzsuz komşusu olmalı — grid kenarına/köşeye sıkıştırılmamalı.
        // Buz ekstra parça hacmi gerektirmez (frozenCells zaten occupiedCells'in bir alt kümesi).
        public List<Vector3Int> frozenCells = new List<Vector3Int>();
    }

    private const string LEVELS_ROOT = "Assets/Levels";
    private const string LEVEL_ORDER_PATH = "Assets/LevelOrder.asset";
    private const float CellSize = 1f;
    private const float Spacing = 0.1f;

    private static GameObject cubePrefab;

    private static readonly LevelSpec[] Levels =
    {
        new LevelSpec { name = "Level1", gridSize = new Vector3Int(2, 1, 2), timeLimit = 0, targetScore = 40,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.O, 1) } },

        new LevelSpec { name = "Level2", gridSize = new Vector3Int(4, 1, 2), timeLimit = 30, targetScore = 70,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.O, 2) } },

        new LevelSpec { name = "Level3", gridSize = new Vector3Int(4, 1, 3), timeLimit = 40, targetScore = 100,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.I, 3) } },

        new LevelSpec { name = "Level4", gridSize = new Vector3Int(4, 1, 4), timeLimit = 50, targetScore = 140,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.O, 2), (PieceTemplateLibrary.I, 2) } },

        new LevelSpec { name = "Level5", gridSize = new Vector3Int(4, 1, 4), timeLimit = 55, targetScore = 140,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.T, 4) } },

        // ── Level6'dan itibaren: taban her zaman simetrik (NxN) ──
        new LevelSpec { name = "Level6", gridSize = new Vector3Int(2, 2, 2), timeLimit = 45, targetScore = 70,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.O, 2) } },

        // 3x3 katman (9 hücre) = merkeze 1 Filler + etrafındaki 8 hücrelik halkaya 2 J
        // (biri 0°, biri 180°) — elle inşa edilip doğrulanmış kesin çözüm.
        new LevelSpec { name = "Level7", gridSize = new Vector3Int(3, 2, 3), timeLimit = 70, targetScore = 130,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.Filler, 2), (PieceTemplateLibrary.J, 4) } },

        new LevelSpec { name = "Level8", gridSize = new Vector3Int(3, 3, 3), timeLimit = 90, targetScore = 200,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.Filler, 3), (PieceTemplateLibrary.J, 6) } },

        new LevelSpec { name = "Level9", gridSize = new Vector3Int(4, 2, 4), timeLimit = 80, targetScore = 220,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.T, 8) } },

        new LevelSpec { name = "Level10", gridSize = new Vector3Int(4, 3, 4), timeLimit = 110, targetScore = 320,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.T, 4), (PieceTemplateLibrary.J, 4), (PieceTemplateLibrary.L, 4) } },

        new LevelSpec { name = "Level11", gridSize = new Vector3Int(5, 2, 5), timeLimit = 140, targetScore = 350,
            pieces = new List<(string, int)>
            {
                (PieceTemplateLibrary.Filler, 2), (PieceTemplateLibrary.O, 3), (PieceTemplateLibrary.I, 3),
                (PieceTemplateLibrary.T, 3), (PieceTemplateLibrary.J, 3),
            } },

        // Yeni parça: S ve Z — ve buz burada başlıyor.
        new LevelSpec { name = "Level12", gridSize = new Vector3Int(5, 3, 5), timeLimit = 180, targetScore = 500,
            pieces = new List<(string, int)>
            {
                (PieceTemplateLibrary.Filler, 3), (PieceTemplateLibrary.O, 3), (PieceTemplateLibrary.I, 3), (PieceTemplateLibrary.T, 3),
                (PieceTemplateLibrary.J, 3), (PieceTemplateLibrary.S, 3), (PieceTemplateLibrary.Z, 3),
            },
            frozenCells = new List<Vector3Int> { new Vector3Int(1, 0, 1), new Vector3Int(3, 1, 3), new Vector3Int(1, 2, 2) } },

        // Saf O ile 6x6 nefes molası (6 çift olduğu için Filler'a gerek yok).
        new LevelSpec { name = "Level13", gridSize = new Vector3Int(6, 2, 6), timeLimit = 150, targetScore = 450,
            pieces = new List<(string, int)> { (PieceTemplateLibrary.O, 18) } },

        new LevelSpec { name = "Level14", gridSize = new Vector3Int(6, 3, 6), timeLimit = 230, targetScore = 700,
            pieces = new List<(string, int)>
            {
                (PieceTemplateLibrary.O, 9), (PieceTemplateLibrary.T, 4), (PieceTemplateLibrary.J, 4),
                (PieceTemplateLibrary.L, 4), (PieceTemplateLibrary.S, 3), (PieceTemplateLibrary.Z, 3),
            },
            frozenCells = new List<Vector3Int>
            {
                new Vector3Int(1, 0, 1), new Vector3Int(4, 0, 4), new Vector3Int(1, 1, 3), new Vector3Int(4, 2, 1),
            } },

        // FİNAL: 7x7 taban (tek sayılı — Filler gerekiyor), 2 katman, tüm 7 parça.
        new LevelSpec { name = "Level15", gridSize = new Vector3Int(7, 2, 7), timeLimit = 240, targetScore = 750,
            pieces = new List<(string, int)>
            {
                (PieceTemplateLibrary.Filler, 2), (PieceTemplateLibrary.O, 4), (PieceTemplateLibrary.I, 4), (PieceTemplateLibrary.T, 4),
                (PieceTemplateLibrary.J, 4), (PieceTemplateLibrary.L, 4), (PieceTemplateLibrary.S, 2), (PieceTemplateLibrary.Z, 2),
            },
            frozenCells = new List<Vector3Int>
            {
                new Vector3Int(1, 0, 1), new Vector3Int(5, 0, 1), new Vector3Int(1, 0, 5),
                new Vector3Int(5, 0, 5), new Vector3Int(3, 1, 3), new Vector3Int(1, 1, 5),
            } },
    };

    [MenuItem("BlockMerge3D/15 Levelli Seriyi Oluştur (Claude)")]
    public static void GenerateAll()
    {
        // Önce tüm şablonların kütüphanede mevcut olduğunu doğrula.
        var missing = new List<string>();
        foreach (var spec in Levels)
            foreach (var (template, _) in spec.pieces)
                if (PieceTemplateLibrary.GetCells(template) == null && !missing.Contains(template))
                    missing.Add(template);

        if (missing.Count > 0)
        {
            EditorUtility.DisplayDialog("Şablon Eksik",
                $"Önce \"BlockMerge3D → Tetris Parça Şablonlarını Oluştur (Claude)\" menüsünü çalıştırın.\nEksik: {string.Join(", ", missing)}",
                "Tamam");
            return;
        }

        cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultCubePrefabSetup.DEFAULT_CUBE_PATH);

        var orderedLevelDatas = new List<LevelData>();
        var report = new StringBuilder();

        foreach (var spec in Levels)
        {
            LevelData ld = GenerateLevel(spec, out string solverMsg);
            orderedLevelDatas.Add(ld);
            report.AppendLine($"{spec.name}: {solverMsg}");
        }

        var levelOrder = AssetDatabase.LoadAssetAtPath<LevelOrderData>(LEVEL_ORDER_PATH);
        if (levelOrder == null)
        {
            levelOrder = ScriptableObject.CreateInstance<LevelOrderData>();
            AssetDatabase.CreateAsset(levelOrder, LEVEL_ORDER_PATH);
        }

        // LevelOrder.asset'i tamamen bu seviyelerle değiştir (sırayla Level1..Level15).
        levelOrder.levels = new List<LevelData>(orderedLevelDatas);
        EditorUtility.SetDirty(levelOrder);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string fullReport = report.ToString();
        Debug.Log($"[LevelSeriesGenerator] {Levels.Length} level oluşturuldu ve LevelOrder.asset güncellendi:\n{fullReport}");
        EditorUtility.DisplayDialog($"{Levels.Length} Level Oluşturuldu", fullReport, "Tamam");
    }

    private static LevelData GenerateLevel(LevelSpec spec, out string solverMsg)
    {
        string levelDir = $"{LEVELS_ROOT}/{spec.name}";
        if (!Directory.Exists(levelDir)) Directory.CreateDirectory(levelDir);
        AssetDatabase.Refresh();

        // Ana şekil: her zaman dolu bir kutu (prefilled yok; frozenCells varsa spec'ten gelir).
        var occupiedCells = new List<Vector3Int>();
        for (int y = 0; y < spec.gridSize.y; y++)
            for (int x = 0; x < spec.gridSize.x; x++)
                for (int z = 0; z < spec.gridSize.z; z++)
                    occupiedCells.Add(new Vector3Int(x, y, z));

        GameObject fullShapePrefab = BuildFullShapePrefab(levelDir, spec.name, spec.gridSize, occupiedCells, spec.frozenCells);

        // Parçalar: şablon başına bir prefab, listede N kez referanslanır (oyun aynı şekli
        // tekrar tekrar spawn edip kullanır — bkz LevelManager.SpawnRandomPiece).
        var complementaryPieces = new List<GameObject>();
        foreach (var (template, count) in spec.pieces)
        {
            var cells = PieceTemplateLibrary.GetCells(template);
            GameObject piecePrefab = BuildPiecePrefab(levelDir, $"{spec.name}_Piece_{template}", spec.gridSize, cells);
            for (int i = 0; i < count; i++) complementaryPieces.Add(piecePrefab);
        }

        string ldPath = $"{levelDir}/{spec.name}_LevelData.asset";
        LevelData ld = AssetDatabase.LoadAssetAtPath<LevelData>(ldPath);
        bool isNew = ld == null;
        if (isNew) ld = ScriptableObject.CreateInstance<LevelData>();

        ld.levelName = spec.name;
        ld.mainShapePrefab = fullShapePrefab;
        ld.complementaryPieces = complementaryPieces;
        ld.timeLimit = spec.timeLimit;
        ld.targetScore = spec.targetScore;

        if (isNew) AssetDatabase.CreateAsset(ld, ldPath);
        else EditorUtility.SetDirty(ld);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var solver = new LevelSolver { maxSearchTimeMs = 15000, maxStatesExplored = 500000 };
        var result = solver.SolveFromPrefabs(fullShapePrefab, complementaryPieces);
        solverMsg = result.isSolvable
            ? $"✅ {result.minMoveCount} hamle, zorluk: {result.difficultyLabel} ({result.difficultyScore:F2})"
            : $"⚠️ {result.failureReason}";

        return ld;
    }

    private static GameObject BuildPiecePrefab(string levelDir, string name, Vector3Int gridSize, List<Vector3Int> cells)
    {
        string path = $"{levelDir}/{name}.prefab";
        float step = CellSize + Spacing;

        GameObject root = new GameObject(name);
        var holder = root.AddComponent<CubeShapeDataHolder>();
        holder.shapeName = name;
        holder.gridSize = gridSize;
        holder.cellSize = CellSize;
        holder.spacing = Spacing;
        holder.occupiedCells = new List<Vector3Int>(cells);

        foreach (var cell in cells)
        {
            GameObject cube = cubePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (CellSize * 0.5f);
            cube.transform.localScale = Vector3.one * CellSize;
            cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return saved;
    }

    private static GameObject BuildFullShapePrefab(string levelDir, string name, Vector3Int gridSize, List<Vector3Int> occupiedCells, List<Vector3Int> frozenCells)
    {
        string path = $"{levelDir}/{name}_FullShape.prefab";
        float step = CellSize + Spacing;

        GameObject root = new GameObject($"{name}_FullShape");
        var holder = root.AddComponent<CubeShapeDataHolder>();
        holder.shapeName = name;
        holder.gridSize = gridSize;
        holder.cellSize = CellSize;
        holder.spacing = Spacing;
        holder.occupiedCells = new List<Vector3Int>(occupiedCells);
        holder.prefilledCells = new List<Vector3Int>();
        holder.prefilledColors = new List<Color>();
        holder.prefilledMaterialIndices = new List<int>();
        holder.frozenCells = new List<Vector3Int>(frozenCells ?? new List<Vector3Int>());

        foreach (var cell in occupiedCells)
        {
            GameObject cube = cubePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (CellSize * 0.5f);
            cube.transform.localScale = Vector3.one * CellSize;
            cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return saved;
    }
}
