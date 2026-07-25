using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LevelForge;

/// <summary>
/// LevelForge.IDifficultyEvaluator implementasyonu — BlockMerge3D'nin gerçek doğrulayıcısı olan
/// LevelSolver'ı sarar. TEK merkezi yer: LevelSolver.SolverResult.failureReason (serbest metin)
/// buradan FailureReasonCode'a çevrilir — eskiden AutoAdjustAndRegenerate içinde dağınık
/// ".Contains(...)" çağrılarıyla yapılan (ve LevelSolver mesajı değişirse sessizce bozulan) işin
/// yerini alır.
/// </summary>
public class BlockMerge3DDifficultyEvaluator : IDifficultyEvaluator<BlockMerge3DCandidate>
{
    public EvaluationResult Evaluate(BlockMerge3DCandidate candidate)
    {
        if (candidate.pieceSplitList == null || candidate.pieceSplitList.Count == 0)
        {
            return EvaluationResult.Invalid(FailureReasonCode.InsufficientContent,
                "Kütüphaneden hiç parça döşenemedi (SolutionFirstBuilder başarısız).", definitive: true);
        }

        int totalCells  = candidate.occupiedCells != null ? candidate.occupiedCells.Count : 0;
        int frozenCount = candidate.frozenCells != null ? candidate.frozenCells.Count : 0;
        int pieceCount  = candidate.pieceSplitList.Count;

        // ── Zorluk skoru: HEURİSTİK (çözmeden) ───────────────────────────────────────────
        // Eskiden her aday için exhaustive LevelSolver çalıştırılıp minMoveCount üzerinden skor
        // hesaplanıyordu. Bu üsteldi: karmaşık adaylarda saniyelerce sürüp bütçeye takılıyor,
        // iyi adayları "skorlanamadı" diye eleyip üretimi patlatıyordu (özellikle "Uzman"da hiçbir
        // aday tutmuyordu). LevelSolver.EstimateDifficulty aynı skoru ANINDA, ort. 0.037 hatayla
        // üretir (27 gerçek seviyede kalibre) — çünkü solver skoru zaten çoğunlukla yapısal
        // (parça sayısı, buz oranı, hacim) ve minMoveCount ≈ parça sayısı.
        float score = LevelSolver.EstimateDifficulty(pieceCount, frozenCount, totalCells, candidate.gridSize);

        // ── Çözülebilirlik kapısı: yalnızca BUZ varsa ────────────────────────────────────
        // SolutionFirstBuilder zaten geometrik döşemeyi garanti ediyor → BUZSUZ bir level yapıca
        // çözülebilir (parçalar katman katman sığıyor; renk/tür yerleşimi engellemez, sadece buz
        // engeller). Bu yüzden solver'ı yalnızca buz varken ve KISA bütçeyle, erime sırasının
        // uygulanabilirliğini doğrulamak için çalıştırıyoruz. Timeout → KABUL (elemek üretimi
        // patlatan davranıştı); yalnızca KESİN çözülemez buz sırası reddedilir.
        if (frozenCount > 0)
        {
            GameObject mainShape = BuildTempMainShape(candidate);
            List<GameObject> pieces = BuildTempPieces(candidate);
            try
            {
                if (pieces.Count > 0)
                {
                    int gridVolume = candidate.gridSize.x * candidate.gridSize.y * candidate.gridSize.z;
                    int stateLimit = gridVolume < 50 ? 40000 : 60000;
                    int timeoutMs  = gridVolume < 50 ? 1000 : 1500;

                    var solver = new LevelSolver { maxSearchTimeMs = timeoutMs, maxStatesExplored = stateLimit };
                    SolverResult result = solver.SolveFromPrefabs(mainShape, pieces);
                    candidate.lastSolverResult = result;

                    if (!result.isSolvable && !result.timedOut)
                        return EvaluationResult.Invalid(MapFailureReason(result), result.failureReason, definitive: true);
                }
            }
            finally
            {
                if (mainShape != null) Object.DestroyImmediate(mainShape);
                foreach (var p in pieces) if (p != null) Object.DestroyImmediate(p);
            }
        }

        var metrics = new Dictionary<string, float>
        {
            { "pieceCount", pieceCount },
            { "frozenCount", frozenCount }
        };
        return EvaluationResult.Valid(score, metrics);
    }

    // LevelSolver'ın serbest metin failureReason'ını TEK bu noktada yapılandırılmış bir koda
    // çevirir (bkz. LevelSolver.cs:98-121 için "Yetersiz hücre"/"Fazla hücre" mesajları,
    // ":159" için "Arama limiti aşıldı" mesajı). "renk"/"katman" gibi artık üretilmeyen eski
    // mesaj kalıpları (bkz. AILevelDesignerWindow eski AutoAdjustAndRegenerate) bilinçli olarak
    // burada YOK — 2026-07-13 redesign'da o kısıt tamamen kaldırıldı.
    private static FailureReasonCode MapFailureReason(SolverResult result)
    {
        if (result.timedOut) return FailureReasonCode.SearchBudgetExceeded;
        string reason = (result.failureReason ?? string.Empty).ToLowerInvariant();
        if (reason.Contains("yetersiz")) return FailureReasonCode.InsufficientContent;
        if (reason.Contains("fazla")) return FailureReasonCode.ExcessContent;
        if (reason.Contains("katman")) return FailureReasonCode.StructurallyUnsolvable; // "Katman Y=.. geçersiz"
        return FailureReasonCode.StructurallyUnsolvable;
    }

    public static GameObject BuildTempMainShape(BlockMerge3DCandidate candidate)
    {
        var root = new GameObject("LevelForge_TempMainShape");
        var holder = root.AddComponent<CubeShapeDataHolder>();
        holder.gridSize = candidate.gridSize;
        holder.cellSize = candidate.cellSize;
        holder.spacing = candidate.spacing;
        holder.occupiedCells = new List<Vector3Int>(candidate.occupiedCells);
        holder.prefilledCells = new List<Vector3Int>(candidate.prefilledCells);
        holder.prefilledMaterialIndices = new List<int>(candidate.prefilledMaterialIndices);
        holder.frozenCells = new List<Vector3Int>(candidate.frozenCells);
        holder.frozenHitCounts = new List<int>(candidate.frozenHitCounts);
        return root;
    }

    public static List<GameObject> BuildTempPieces(BlockMerge3DCandidate candidate)
    {
        var pieces = new List<GameObject>();
        for (int i = 0; i < candidate.pieceSplitList.Count; i++)
        {
            var cells = candidate.pieceSplitList[i];
            if (cells == null || cells.Count == 0) continue;

            int minX = cells.Min(c => c.x), minY = cells.Min(c => c.y), minZ = cells.Min(c => c.z);
            var shift = new Vector3Int(minX, minY, minZ);
            var normCells = cells.Select(c => c - shift).ToList();

            var piece = new GameObject($"LevelForge_TempPiece_{i}");
            var holder = piece.AddComponent<CubeShapeDataHolder>();
            holder.gridSize = candidate.gridSize;
            holder.cellSize = candidate.cellSize;
            holder.spacing = candidate.spacing;
            holder.occupiedCells = normCells;
            pieces.Add(piece);
        }
        return pieces;
    }
}
