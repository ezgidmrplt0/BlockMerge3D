using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  PIECE DEFINITION MIGRATION TESTS  —  Kanonik İmza Doğrulama
//  BlockMerge3D  •  PieceGeometryUtils için basit doğrulama yardımcıları
//  (LevelSolverTests.cs ile aynı stil: MenuItem + Debug.Log, NUnit yok)
// ═══════════════════════════════════════════════════════════════════

public struct PieceTestResult
{
    public string name;
    public bool passed;
    public string message;
}

public class PieceDefinitionMigrationTests
{
    [MenuItem("BlockMerge3D/Test Piece Migration")]
    public static void RunTests()
    {
        Debug.Log("═══════════════════════════════════════");
        Debug.Log("   PIECE DEFINITION MIGRATION TEST SUITE");
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
    /// öğesi (Console'a log) hem de Piece Library penceresinin "Testleri Çalıştır" butonu
    /// (sonucu doğrudan GUI'de gösterir) tarafından kullanılır.
    /// </summary>
    public static List<PieceTestResult> RunAll()
    {
        var results = new List<PieceTestResult>();

        // Referans şekiller
        var lShape = new List<Vector3Int> { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0), new Vector3Int(2, 0, 1) };
        var tShape = new List<Vector3Int> { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0), new Vector3Int(1, 0, 1) };
        // Düz (tek katmanlı) S/Z tetromino — klasik 2D Tetris'te ayna görüntüsü sayılırlar.
        var sShape = new List<Vector3Int> { new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1) };
        var zShape = new List<Vector3Int> { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 1), new Vector3Int(2, 0, 1) };
        // Gerçek 3D kiral tetrakup (çapraz eksenlerde ilerleyen "merdiven") ve z-ekseninde
        // aynalanmış hali — bu, hiçbir 3D rotasyonla birbirine denk gelmeyen bir çift.
        var chiralA = new List<Vector3Int> { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 1, 0), new Vector3Int(1, 1, 1) };
        var chiralB = chiralA.Select(c => new Vector3Int(c.x, c.y, -c.z)).ToList();
        var square2x2 = new List<Vector3Int> { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1) };

        // ── Test 1: Ötelenmiş kopya → aynı imza ──────────────────────
        var lTranslated = lShape.Select(c => c + new Vector3Int(5, 3, 1)).ToList();
        string sigL = PieceGeometryUtils.ComputeCanonicalSignature(lShape);
        string sigLTranslated = PieceGeometryUtils.ComputeCanonicalSignature(lTranslated);
        results.Add(new PieceTestResult
        {
            name = "Ötelenmiş kopya",
            passed = sigL == sigLTranslated,
            message = sigL == sigLTranslated ? "Aynı imzayı üretti" : $"FARKLI imza: {sigL} vs {sigLTranslated}"
        });

        // ── Test 2: 90° Y-rotasyonlu kopya → aynı imza ───────────────
        var lRotatedY = PieceGeometryUtils.RotateAndNormalize(lShape, Quaternion.Euler(0f, 90f, 0f));
        string sigLRotated = PieceGeometryUtils.ComputeCanonicalSignature(lRotatedY);
        results.Add(new PieceTestResult
        {
            name = "90° Y-rotasyonlu kopya",
            passed = sigL == sigLRotated,
            message = sigL == sigLRotated ? "Aynı imzayı üretti" : $"FARKLI imza: {sigL} vs {sigLRotated}"
        });

        // ── Test 3: Farklı iki tetromino (L vs T) → farklı imza ──────
        string sigT = PieceGeometryUtils.ComputeCanonicalSignature(tShape);
        results.Add(new PieceTestResult
        {
            name = "L vs T (farklı şekiller)",
            passed = sigL != sigT,
            message = sigL != sigT ? "Farklı imza aldı" : "AYNI imzayı aldı (yanlış birleştirme)"
        });

        // ── Test 4: Düz S/Z ayna çifti → AYNI imza (beklenen davranış) ─
        // Tek katmanlı bir parça, sahnede DraggablePiece.RotateAroundX ile iki kez 90°
        // çevrilerek (180°) ters çevrilebilir — bu, 2D'de "ayna" gibi görünen S/Z farkını
        // gerçek 3D rotasyonla ortadan kaldırır. Yani bu iki şeklin AYNI kanonik imzayı
        // alması bir hata değil, motorun rotasyon serbestliğinin doğru bir sonucudur.
        string sigS = PieceGeometryUtils.ComputeCanonicalSignature(sShape);
        string sigZ = PieceGeometryUtils.ComputeCanonicalSignature(zShape);
        results.Add(new PieceTestResult
        {
            name = "Düz S/Z ayna çifti",
            passed = sigS == sigZ,
            message = sigS == sigZ
                ? "Aynı imzayı aldı (beklenen — 3D'de çevrilebilir)"
                : $"FARKLI imza aldı, beklenmiyordu: {sigS} vs {sigZ}"
        });

        // ── Test 5: Gerçek 3D kiral çift → farklı imza ────────────────
        string sigChiralA = PieceGeometryUtils.ComputeCanonicalSignature(chiralA);
        string sigChiralB = PieceGeometryUtils.ComputeCanonicalSignature(chiralB);
        results.Add(new PieceTestResult
        {
            name = "Gerçek 3D kiral çift",
            passed = sigChiralA != sigChiralB,
            message = sigChiralA != sigChiralB ? "Farklı imza aldı (aşırı birleştirme yok)" : "AYNI imzayı aldı (aşırı birleştirme hatası)"
        });

        // ── Test 6: ComputeDistinctRotations — simetri sayımı ─────────
        int squareRotCount = PieceGeometryUtils.ComputeDistinctRotations(square2x2).Count;
        int lRotCount = PieceGeometryUtils.ComputeDistinctRotations(lShape).Count;
        bool test6Passed = squareRotCount == 3 && lRotCount == 24;
        results.Add(new PieceTestResult
        {
            name = "Simetri sayımı (kare=3, L=24)",
            passed = test6Passed,
            message = $"kare={squareRotCount} (beklenen 3), L={lRotCount} (beklenen 24)"
        });

        return results;
    }
}
