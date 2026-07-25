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

        GameObject mainShape = BuildTempMainShape(candidate);
        List<GameObject> pieces = BuildTempPieces(candidate);

        try
        {
            if (pieces.Count == 0)
            {
                return EvaluationResult.Invalid(FailureReasonCode.InsufficientContent, "Geçerli parça oluşturulamadı.", definitive: true);
            }

            int gridVolume = candidate.gridSize.x * candidate.gridSize.y * candidate.gridSize.z;
            int stateLimit = gridVolume < 50 ? 50000 : gridVolume < 100 ? 75000 : 100000;
            int timeoutMs = gridVolume < 50 ? 2000 : gridVolume < 100 ? 3000 : 5000;

            var solver = new LevelSolver { maxSearchTimeMs = timeoutMs, maxStatesExplored = stateLimit };
            SolverResult result = solver.SolveFromPrefabs(mainShape, pieces);
            candidate.lastSolverResult = result;

            if (result.isSolvable)
            {
                var metrics = new Dictionary<string, float>
                {
                    { "moveCount", result.minMoveCount },
                    { "pieceCount", candidate.pieceSplitList.Count }
                };
                return EvaluationResult.Valid(result.difficultyScore, metrics);
            }

            return EvaluationResult.Invalid(MapFailureReason(result), result.failureReason, definitive: !result.timedOut);
        }
        finally
        {
            if (mainShape != null) Object.DestroyImmediate(mainShape);
            foreach (var p in pieces) if (p != null) Object.DestroyImmediate(p);
        }
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
