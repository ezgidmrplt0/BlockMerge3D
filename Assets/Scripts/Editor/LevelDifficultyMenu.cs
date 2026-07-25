using System.Text;
using UnityEditor;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  Mevcut (kaydedilmiş) bir seviyenin zorluk skorunu göstermek için
//  Project penceresinde bir LevelData asset'ine SAĞ TIKLA →
//  "BlockMerge3D/Zorluk Skorunu Göster". Birden çok seçili LevelData
//  varsa hepsini skorlar. Skoru LevelSolver hesaplar (0-1, üretim
//  zamanındaki AI tool'un kullandığı AYNI motor — bkz.
//  BlockMerge3DDifficultyEvaluator).
// ═══════════════════════════════════════════════════════════════════
public static class LevelDifficultyMenu
{
    private const string MENU = "Assets/BlockMerge3D/Zorluk Skorunu Göster";

    [MenuItem(MENU, true)]
    private static bool Validate()
    {
        foreach (var o in Selection.objects)
            if (o is LevelData) return true;
        return false;
    }

    [MenuItem(MENU, false)]
    private static void ShowDifficulty()
    {
        var sb = new StringBuilder();
        int count = 0;

        foreach (var o in Selection.objects)
        {
            if (!(o is LevelData ld)) continue;
            count++;

            var holder = ld.mainShapePrefab != null ? ld.mainShapePrefab.GetComponent<CubeShapeDataHolder>() : null;
            if (holder == null)
            {
                sb.AppendLine($"• {ld.levelName}: mainShape/CubeShapeDataHolder yok — skorlanamadı.");
                continue;
            }

            // AI üretimiyle AYNI motor: kalibre heuristik (bkz. LevelSolver.EstimateDifficulty).
            // Exhaustive solver YERİNE — anında, hiç timeout etmez, karmaşık seviyeler dahil
            // TÜM level'lar skorlanır (eski solver zor level'larda "belirsiz" veriyordu).
            int totalCells  = holder.occupiedCells != null ? holder.occupiedCells.Count : 0;
            int frozenCount = holder.frozenCells != null ? holder.frozenCells.Count : 0;
            int pieceCount  = ld.complementaryPieces != null ? ld.complementaryPieces.Count : 0;

            float score = LevelSolver.EstimateDifficulty(pieceCount, frozenCount, totalCells, holder.gridSize);
            string label = score < 0.33f ? "kolay" : score < 0.66f ? "orta" : "zor";
            sb.AppendLine($"• {ld.levelName}: skor {score:F2} ({label}) — {pieceCount} parça, {frozenCount} buz");
        }

        string report = sb.ToString().TrimEnd();
        Debug.Log($"🎯 Seviye Zorluk Skoru ({count} seviye):\n{report}");
        EditorUtility.DisplayDialog("Seviye Zorluk Skoru", report, "Tamam");
    }
}
