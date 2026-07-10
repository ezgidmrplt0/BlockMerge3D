using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL SOLVER TESTS  —  Test ve Doğrulama Yardımcıları
//  BlockMerge3D  •  Solver'ı test etmek için basit seviye oluşturucular
// ═══════════════════════════════════════════════════════════════════

public class LevelSolverTests
{
    /// <summary>
    /// Basit çözülebilir bir seviye oluştur: 2x2x2 grid, 2 parça
    /// </summary>
    public static (GameObject mainShape, List<GameObject> pieces) CreateSimpleSolvableLevel()
    {
        // Ana şekil: 2x2x2 küp (8 hücre)
        GameObject main = new GameObject("TestMain_Solvable");
        var holder = main.AddComponent<CubeShapeDataHolder>();
        holder.gridSize = new Vector3Int(2, 2, 2);
        holder.cellSize = 1f;
        holder.spacing = 0.1f;
        holder.occupiedCells = new List<Vector3Int>
        {
            // Tüm hücreleri ekle
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1),
            new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0),
            new Vector3Int(0, 1, 1), new Vector3Int(1, 1, 1)
        };
        holder.prefilledCells = new List<Vector3Int>();
        holder.prefilledMaterialIndices = new List<int>();
        holder.frozenCells = new List<Vector3Int>();

        // Parça 1: Alt katman (4 hücre)
        GameObject piece1 = new GameObject("TestPiece_1");
        var p1holder = piece1.AddComponent<CubeShapeDataHolder>();
        p1holder.gridSize = holder.gridSize;
        p1holder.cellSize = holder.cellSize;
        p1holder.spacing = holder.spacing;
        p1holder.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1)
        };

        // Parça 2: Üst katman (4 hücre)
        GameObject piece2 = new GameObject("TestPiece_2");
        var p2holder = piece2.AddComponent<CubeShapeDataHolder>();
        p2holder.gridSize = holder.gridSize;
        p2holder.cellSize = holder.cellSize;
        p2holder.spacing = holder.spacing;
        p2holder.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1)
        };

        return (main, new List<GameObject> { piece1, piece2 });
    }

    /// <summary>
    /// Çözülemez seviye: yetersiz hücre
    /// </summary>
    public static (GameObject mainShape, List<GameObject> pieces) CreateUnsolvableLevel_InsufficientCells()
    {
        // Ana şekil: 3x3x1 (9 hücre)
        GameObject main = new GameObject("TestMain_Unsolvable");
        var holder = main.AddComponent<CubeShapeDataHolder>();
        holder.gridSize = new Vector3Int(3, 3, 1);
        holder.cellSize = 1f;
        holder.spacing = 0.1f;
        holder.occupiedCells = new List<Vector3Int>();
        
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                holder.occupiedCells.Add(new Vector3Int(x, 0, z));

        holder.prefilledCells = new List<Vector3Int>();
        holder.prefilledMaterialIndices = new List<int>();
        holder.frozenCells = new List<Vector3Int>();

        // Sadece 1 parça: 4 hücre (9 > 4 = yetersiz)
        GameObject piece1 = new GameObject("TestPiece_Small");
        var p1holder = piece1.AddComponent<CubeShapeDataHolder>();
        p1holder.gridSize = holder.gridSize;
        p1holder.cellSize = holder.cellSize;
        p1holder.spacing = holder.spacing;
        p1holder.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1)
        };

        return (main, new List<GameObject> { piece1 });
    }

    /// <summary>
    /// Çözülebilir orta zorluk seviye: 3x3x2, prefilled ve frozen içerir
    /// </summary>
    public static (GameObject mainShape, List<GameObject> pieces) CreateMediumLevel()
    {
        // Ana şekil: 3x3x2 (18 hücre)
        GameObject main = new GameObject("TestMain_Medium");
        var holder = main.AddComponent<CubeShapeDataHolder>();
        holder.gridSize = new Vector3Int(3, 3, 2);
        holder.cellSize = 1f;
        holder.spacing = 0.1f;
        holder.occupiedCells = new List<Vector3Int>();

        // Tüm hücreleri ekle
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                for (int z = 0; z < 3; z++)
                    holder.occupiedCells.Add(new Vector3Int(x, y, z));

        // 2 prefilled hücre (alt katman merkez)
        holder.prefilledCells = new List<Vector3Int>
        {
            new Vector3Int(1, 0, 1), // Merkez
            new Vector3Int(1, 0, 0)  // Ön
        };
        holder.prefilledMaterialIndices = new List<int> { 0, 0 }; // Aynı renk

        // 1 frozen hücre
        holder.frozenCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0) // Köşe
        };

        // 4 parça oluştur (toplam 16 hücre, 2 prefilled var, 18-2=16)
        List<GameObject> pieces = new List<GameObject>();

        // Parça 1: L şekli (4 hücre)
        GameObject p1 = new GameObject("Piece_L");
        var p1h = p1.AddComponent<CubeShapeDataHolder>();
        p1h.gridSize = holder.gridSize;
        p1h.cellSize = holder.cellSize;
        p1h.spacing = holder.spacing;
        p1h.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(2, 0, 0), new Vector3Int(2, 0, 1)
        };
        pieces.Add(p1);

        // Parça 2: Kare (4 hücre)
        GameObject p2 = new GameObject("Piece_Square");
        var p2h = p2.AddComponent<CubeShapeDataHolder>();
        p2h.gridSize = holder.gridSize;
        p2h.cellSize = holder.cellSize;
        p2h.spacing = holder.spacing;
        p2h.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1)
        };
        pieces.Add(p2);

        // Parça 3: Çizgi (4 hücre)
        GameObject p3 = new GameObject("Piece_Line");
        var p3h = p3.AddComponent<CubeShapeDataHolder>();
        p3h.gridSize = holder.gridSize;
        p3h.cellSize = holder.cellSize;
        p3h.spacing = holder.spacing;
        p3h.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(2, 0, 0), new Vector3Int(0, 1, 0)
        };
        pieces.Add(p3);

        // Parça 4: T şekli (4 hücre)
        GameObject p4 = new GameObject("Piece_T");
        var p4h = p4.AddComponent<CubeShapeDataHolder>();
        p4h.gridSize = holder.gridSize;
        p4h.cellSize = holder.cellSize;
        p4h.spacing = holder.spacing;
        p4h.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(2, 0, 0), new Vector3Int(1, 0, 1)
        };
        pieces.Add(p4);

        return (main, pieces);
    }

    /// <summary>
    /// Test suite'i çalıştır
    /// </summary>
    [MenuItem("BlockMerge3D/Test Solver")]
    public static void RunTests()
    {
        var solver = new LevelSolver
        {
            maxSearchTimeMs = 10000,  // 10 saniye test için
            maxStatesExplored = 200000
        };

        Debug.Log("═══════════════════════════════════════");
        Debug.Log("   LEVEL SOLVER TEST SUITE");
        Debug.Log("═══════════════════════════════════════");

        // Test 1: Basit çözülebilir
        Debug.Log("\n[TEST 1] Basit Çözülebilir Seviye");
        var test1 = CreateSimpleSolvableLevel();
        var result1 = solver.SolveFromPrefabs(test1.mainShape, test1.pieces);
        Debug.Log(result1.isSolvable
            ? $"✅ BAŞARILI: Çözülebilir ({result1.minMoveCount} hamle, {result1.difficultyLabel})"
            : $"❌ BAŞARISIZ: {result1.failureReason}");
        Object.DestroyImmediate(test1.mainShape);
        foreach (var p in test1.pieces) Object.DestroyImmediate(p);

        // Test 2: Yetersiz hücre
        Debug.Log("\n[TEST 2] Yetersiz Hücre (Çözülemez)");
        var test2 = CreateUnsolvableLevel_InsufficientCells();
        var result2 = solver.SolveFromPrefabs(test2.mainShape, test2.pieces);
        Debug.Log(!result2.isSolvable
            ? $"✅ BAŞARILI: Doğru tespit edildi - {result2.failureReason}"
            : $"❌ BAŞARISIZ: Çözülemez olması gerekirdi");
        Object.DestroyImmediate(test2.mainShape);
        foreach (var p in test2.pieces) Object.DestroyImmediate(p);

        // Test 3: Orta zorluk
        Debug.Log("\n[TEST 3] Orta Zorluk Seviye");
        var test3 = CreateMediumLevel();
        var result3 = solver.SolveFromPrefabs(test3.mainShape, test3.pieces);
        Debug.Log(result3.isSolvable
            ? $"✅ Çözülebilir: {result3.minMoveCount} hamle, Zorluk: {result3.difficultyLabel} ({result3.difficultyScore:F2})"
            : $"⚠️ Çözülemedi: {result3.failureReason}");
        Object.DestroyImmediate(test3.mainShape);
        foreach (var p in test3.pieces) Object.DestroyImmediate(p);

        Debug.Log("\n═══════════════════════════════════════");
        Debug.Log("   TEST SUITE TAMAMLANDI");
        Debug.Log("═══════════════════════════════════════\n");
    }
}
