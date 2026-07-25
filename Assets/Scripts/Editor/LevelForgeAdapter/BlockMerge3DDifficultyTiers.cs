using System.Collections.Generic;
using UnityEngine;
using LevelForge;

/// <summary>
/// Üretir/önbelleğe alır: AILevelDesignerWindow.DifficultySpecs (Kolay/Orta/Zor/Uzman) →
/// LevelForge.DifficultyTier. Sayısal hedefler (solverTargetScore/idealPieceCount/minMoves/
/// maxMoves) TEK gerçek kaynaktan (DifficultySpecs) okunur, burada sadece LevelForge'un
/// beklediği tipe dönüştürülür — iki tablo birbirinden bağımsız sürüklenemez.
/// </summary>
internal static class BlockMerge3DDifficultyTiers
{
    // Tier'lar arası hedef skor farkı 0.25 (bkz. DifficultySpecs: .15/.40/.65/.85). scoreTolerance
    // burada 0.06 seçildi ki AILevelDesignerWindow.BuildSearchBudget'ın en yüksek tolerans
    // çarpanıyla (1.5x → efektif ±0.09) bile komşu tier'ların bantları ASLA çakışmasın
    // (ör. Kolay üst sınırı .15+.09=.24, Orta alt sınırı .40-.09=.31 — aralarında .07 pay kalır).
    private const float ScoreTolerance = 0.06f;

    private static readonly Dictionary<AILevelDesignerWindow.AILevelDifficulty, DifficultyTier> cache
        = new Dictionary<AILevelDesignerWindow.AILevelDifficulty, DifficultyTier>();

    internal static DifficultyTier GetTier(AILevelDesignerWindow.AILevelDifficulty mode)
    {
        if (cache.TryGetValue(mode, out var existing) && existing != null) return existing;

        var spec = AILevelDesignerWindow.GetDifficultySpec(mode);
        var tier = ScriptableObject.CreateInstance<DifficultyTier>();
        tier.tierName = mode.ToString();
        tier.targetScore = spec.solverTargetScore;
        tier.scoreTolerance = ScoreTolerance;
        tier.metricRanges = new List<DifficultyTier.MetricRange>
        {
            new DifficultyTier.MetricRange { metricName = "moveCount", min = spec.minMoves, max = spec.maxMoves },
            // pieceCount'a skor kadar sıkı bir bant uygulanmaz (eski SelectBestStrategy'de de
            // pieceScore yumuşak bir cezalandırmaydı, sert bir eşik değil) — sadece aşırı sapmayı eler.
            new DifficultyTier.MetricRange { metricName = "pieceCount", min = Mathf.Max(1, spec.idealPieceCount - 4), max = spec.idealPieceCount + 4 }
        };

        cache[mode] = tier;
        return tier;
    }
}
