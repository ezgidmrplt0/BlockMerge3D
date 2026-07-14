using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

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
    /// Çözülebilir orta zorluk seviye: 3x3x2, prefilled içerir (buz yok — buz/patlama mekaniği
    /// ayrıca CreateIceExplosionLevel'de izole ve minimal bir senaryoyla test ediliyor).
    /// </summary>
    public static (GameObject mainShape, List<GameObject> pieces) CreateMediumLevel()
    {
        // Ana şekil: 3x3x2 (18 hücre) — DÜZELTİLDİ: gridSize eskiden (3,3,2) idi ama hücreler
        // x,z ekseninde 3'er, y ekseninde 2'şer dolduruluyor (aşağıdaki döngüye bak); yani Y/Z
        // bileşenleri yer değiştirmişti. gridSize.z=2 sınırı yüzünden z=2'deki hücrelere HİÇBİR
        // parça yerleştirilemiyordu (TryPlacePiece'in sınır kontrolü reddediyordu) — bu, bu
        // testin ice/dedup düzeltmelerinden bağımsız, kendi başına çözülemez kılan gizli bir hataydı.
        GameObject main = new GameObject("TestMain_Medium");
        var holder = main.AddComponent<CubeShapeDataHolder>();
        holder.gridSize = new Vector3Int(3, 2, 3);
        holder.cellSize = 1f;
        holder.spacing = 0.1f;
        holder.occupiedCells = new List<Vector3Int>();

        // Tüm hücreleri ekle
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                for (int z = 0; z < 3; z++)
                    holder.occupiedCells.Add(new Vector3Int(x, y, z));

        // 2 prefilled hücre — her katmana 1 tane. Parçalar her zaman 4 hücrelik bloklar halinde
        // TEK bir katmana yerleştiği için, bir katmanın "doldurulması gereken" hücre sayısı
        // (9 - o katmandaki prefilled sayısı) 4'ün katı OLMAK ZORUNDA, yoksa hiçbir parça
        // kombinasyonu o katmanı tam dolduramaz. 9-1=8 (2 parça) her iki katman için de tutarlı.
        // KÖŞEYE yerleştirildi (merkeze DEĞİL): 3x3'lük bir katmanda 2x2'lik Kare parçasının
        // olası 4 konumunun HEPSİ merkez hücreyi kapsar — merkez dolu/engelliyken Square hiçbir
        // rotasyon/konumda yerleştirilemezdi. Köşede bu sorun yok.
        holder.prefilledCells = new List<Vector3Int>
        {
            new Vector3Int(2, 0, 2), // Köşe (alt katman, Y=0)
            new Vector3Int(2, 1, 2)  // Köşe (üst katman, Y=1)
        };
        holder.prefilledMaterialIndices = new List<int> { 0, 0 }; // Kozmetik, oynanışı etkilemiyor
        holder.frozenCells = new List<Vector3Int>();

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

        // Parça 3 ve 4: L ve Kare'nin BİRER kopyası daha — DÜZELTİLDİ: eski "Line" ve "T"
        // parçaları, katman 1'i (köşesi eksik 3x3) hiçbir rotasyonla döşeyemiyordu (elle
        // doğrulandı: L+Kare çifti dışındaki HİÇBİR ikili — Line+T, T+Kare, Line+Kare — köşesi
        // eksik bir 3x3'ü kalan boşluğun şekli tetromino-uyumsuz veya bağlantısız kaldığı için
        // döşeyemiyor). L+Kare çiftinin kendisi tek bir katmanı doğru döşediği için (kalan 4 hücre
        // birebir Kare'nin şekli), aynı çifti İKİNCİ katman için de tekrar kullanıyoruz.
        GameObject p3 = new GameObject("Piece_L2");
        var p3h = p3.AddComponent<CubeShapeDataHolder>();
        p3h.gridSize = holder.gridSize;
        p3h.cellSize = holder.cellSize;
        p3h.spacing = holder.spacing;
        p3h.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(2, 0, 0), new Vector3Int(2, 0, 1)
        };
        pieces.Add(p3);

        GameObject p4 = new GameObject("Piece_Square2");
        var p4h = p4.AddComponent<CubeShapeDataHolder>();
        p4h.gridSize = holder.gridSize;
        p4h.cellSize = holder.cellSize;
        p4h.spacing = holder.spacing;
        p4h.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1)
        };
        pieces.Add(p4);

        return (main, pieces);
    }

    /// <summary>
    /// Minimal, elle doğrulanmış buz/erime + GRUP patlaması senaryosu: 3x1x2 tek katman.
    /// (0,0,0) ve (1,0,1): prefilled, materialIndex=0 (tasarımcı tarafından SABİT).
    /// (1,0,0): boş, buza değecek hücre. (2,0,0): frozen.
    /// [2026-07-14, eski mantığa dönüş] Piece0 (index 0, proxyColor=0) (1,0,0)'a yerleşince
    /// buza (2,0,0) değer. (1,0,0)'dan başlayan flood-fill aynı renkli (matIndex=0) bağlantılı
    /// komşuları toplar: (0,0,0) ve (1,0,1) ikisi de prefilled matIndex=0, ikisi de (1,0,0)'a
    /// komşu → grup = {(1,0,0),(0,0,0),(1,0,1)} = 3 ÜYELİ (eski sabit-2 kuralından farklı
    /// olarak grup büyüklüğü keyfi). Grup ≥2 olduğu için TAMAMI buzla birlikte yok olur, buz
    /// da (2,0,0) olarak normal boş hücreye döner. Sonuçta 4 hücrenin de (0,0,0)/(1,0,0)/
    /// (1,0,1)/(2,0,0) yeniden boş kaldığı bir durum ortaya çıkar — Piece1..Piece4 (4 "yedek"
    /// tek hücrelik parça, üretim tarafının buz başına genişletilmiş pay eklemesini taklit
    /// eder, bkz. AILevelDesignerWindow) bunları yeniden doldurur. Toplam 5 parça kullanılır
    /// (1 tetikleyici + 4 yeniden-doldurma).
    /// </summary>
    public static (GameObject mainShape, List<GameObject> pieces) CreateIceGroupExplosionLevel()
    {
        GameObject main = new GameObject("TestMain_IceGroupExplosion");
        var holder = main.AddComponent<CubeShapeDataHolder>();
        holder.gridSize = new Vector3Int(3, 1, 2);
        holder.cellSize = 1f;
        holder.spacing = 0.1f;
        holder.occupiedCells = new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0), new Vector3Int(1, 0, 1)
        };
        holder.prefilledCells = new List<Vector3Int> { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 1) };
        holder.prefilledMaterialIndices = new List<int> { 0, 0 };
        holder.frozenCells = new List<Vector3Int> { new Vector3Int(2, 0, 0) };

        List<GameObject> pieces = new List<GameObject>();
        for (int i = 0; i < 5; i++)
        {
            GameObject p = new GameObject("Piece" + i);
            var ph = p.AddComponent<CubeShapeDataHolder>();
            ph.gridSize = holder.gridSize; ph.cellSize = holder.cellSize; ph.spacing = holder.spacing;
            ph.occupiedCells = new List<Vector3Int> { new Vector3Int(0, 0, 0) };
            pieces.Add(p);
        }

        return (main, pieces);
    }

    /// <summary>
    /// Test suite'i çalıştır (Console'a log basar)
    /// </summary>
    // [MenuItem("BlockMerge3D/Test Solver")]
    public static void RunTests()
    {
        Debug.Log("═══════════════════════════════════════");
        Debug.Log("   LEVEL SOLVER TEST SUITE");
        Debug.Log("═══════════════════════════════════════");

        var results = RunAll();
        foreach (var r in results)
        {
            if (r.passed) Debug.Log($"✅ [{r.name}] {r.message}");
            else Debug.LogError($"❌ [{r.name}] {r.message}");
        }

        int passed = results.Count(r => r.passed);
        Debug.Log("═══════════════════════════════════════");
        Debug.Log(passed == results.Count
            ? $"   TÜM TESTLER BAŞARILI ({passed}/{results.Count})"
            : $"   {results.Count - passed} TEST BAŞARISIZ ({passed}/{results.Count} geçti)");
        Debug.Log("═══════════════════════════════════════");
    }

    /// <summary>
    /// Testleri çalıştırır ve yapılandırılmış sonuç listesi döndürür — hem yukarıdaki menü
    /// öğesi (Console'a log) hem de Piece Library penceresinin "Solver Testleri" butonu
    /// (sonucu doğrudan GUI'de gösterir) tarafından kullanılır.
    /// </summary>
    public static List<PieceTestResult> RunAll()
    {
        var results = new List<PieceTestResult>();
        var solver = new LevelSolver
        {
            maxSearchTimeMs = 10000,  // 10 saniye test için
            maxStatesExplored = 200000
        };

        // ── Test 1: Basit çözülebilir seviye ─────────────────────────
        var test1 = CreateSimpleSolvableLevel();
        var result1 = solver.SolveFromPrefabs(test1.mainShape, test1.pieces);
        results.Add(new PieceTestResult
        {
            name = "Basit çözülebilir seviye",
            passed = result1.isSolvable,
            message = result1.isSolvable
                ? $"Çözülebilir ({result1.minMoveCount} hamle, {result1.difficultyLabel})"
                : $"BAŞARISIZ: {result1.failureReason}"
        });
        if (result1.isSolvable) results.Add(CheckBottomUpOrder("Basit seviye — hamle sırası alttan üste", result1));
        Object.DestroyImmediate(test1.mainShape);
        foreach (var p in test1.pieces) Object.DestroyImmediate(p);

        // ── Test 2: Yetersiz hücre (çözülemez olmalı) ────────────────
        var test2 = CreateUnsolvableLevel_InsufficientCells();
        var result2 = solver.SolveFromPrefabs(test2.mainShape, test2.pieces);
        results.Add(new PieceTestResult
        {
            name = "Yetersiz hücre (çözülemez tespiti)",
            passed = !result2.isSolvable,
            message = !result2.isSolvable
                ? $"Doğru tespit edildi: {result2.failureReason}"
                : "BAŞARISIZ: çözülemez olması gerekirdi"
        });
        Object.DestroyImmediate(test2.mainShape);
        foreach (var p in test2.pieces) Object.DestroyImmediate(p);

        // ── Test 3: Orta zorluk seviye (prefilled içerir) ────────────
        var test3 = CreateMediumLevel();
        var result3 = solver.SolveFromPrefabs(test3.mainShape, test3.pieces);
        results.Add(new PieceTestResult
        {
            name = "Orta zorluk seviye",
            passed = result3.isSolvable,
            message = result3.isSolvable
                ? $"Çözülebilir: {result3.minMoveCount} hamle, Zorluk: {result3.difficultyLabel} ({result3.difficultyScore:F2})"
                : $"Çözülemedi: {result3.failureReason}"
        });
        if (result3.isSolvable) results.Add(CheckBottomUpOrder("Orta seviye — hamle sırası alttan üste", result3));
        Object.DestroyImmediate(test3.mainShape);
        foreach (var p in test3.pieces) Object.DestroyImmediate(p);

        // ── Test 4: Buz = buza değen hücreden başlayan bağlantılı aynı-renk GRUBU (≥2) TAMAMEN patlar ────
        var test4 = CreateIceGroupExplosionLevel();
        var result4 = solver.SolveFromPrefabs(test4.mainShape, test4.pieces);
        bool thawedSomewhere = result4.isSolvable && result4.solutionSteps.Any(s => s.thawedCells.Count > 0);
        int totalDestroyed = result4.isSolvable ? result4.solutionSteps.Sum(s => s.destroyedCells.Count) : 0;
        results.Add(new PieceTestResult
        {
            name = "Buz = buza değen hücreden başlayan bağlantılı aynı-renk grubu TAMAMEN patlar",
            passed = result4.isSolvable && thawedSomewhere && totalDestroyed == 3 && result4.minMoveCount == 5,
            message = !result4.isSolvable
                ? $"BAŞARISIZ: {result4.failureReason}"
                : !thawedSomewhere
                    ? "BAŞARISIZ: çözüldü ama hiçbir adımda erime tetiklenmedi"
                    : totalDestroyed != 3
                        ? $"BAŞARISIZ: {totalDestroyed} hücre yok oldu, 3 bekleniyordu (bağlantılı grup: dokunan hücre + 2 prefilled)"
                        : result4.minMoveCount != 5
                            ? $"BAŞARISIZ: {result4.minMoveCount} hamle bekleniyordu 5 (1 tetikleyici + 4 yeniden-doldurma)"
                            : $"Çözülebilir: {result4.minMoveCount} hamle, grup patlaması (3 üyeli) doğru simüle edildi"
        });
        Object.DestroyImmediate(test4.mainShape);
        foreach (var p in test4.pieces) Object.DestroyImmediate(p);

        return results;
    }

    /// <summary>
    /// Collapse-aware solver'ın temel garantisi: bir çözümün adımları (solutionSteps), gerçek
    /// oyunda oynanacak sırayla birebir eşleşmeli — yani her adımın katmanı (cells[0].y),
    /// bir öncekinden asla küçük olmamalı (monoton artan/sabit).
    /// </summary>
    private static PieceTestResult CheckBottomUpOrder(string name, SolverResult result)
    {
        var steps = result.solutionSteps;
        for (int i = 1; i < steps.Count; i++)
        {
            int prevY = steps[i - 1].cells[0].y;
            int curY = steps[i].cells[0].y;
            if (curY < prevY)
            {
                return new PieceTestResult
                {
                    name = name,
                    passed = false,
                    message = $"BAŞARISIZ: adım {i} katmanı ({curY}) önceki adımdan ({prevY}) düşük — alttan üste sıralama bozuk"
                };
            }
        }

        return new PieceTestResult
        {
            name = name,
            passed = true,
            message = $"{steps.Count} adımın tamamı katman sırasına göre monoton (alttan üste)"
        };
    }
}
