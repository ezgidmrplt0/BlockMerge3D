using System.Collections.Generic;
using UnityEngine;

namespace LevelForge
{
    /// <summary>
    /// A named difficulty target ("Easy", "Hard", ...). Projects create one asset per tier and
    /// fill in the numeric target/tolerance that their own <see cref="IDifficultyEvaluator{TCandidate}"/>
    /// scores are comparable against. The scalar targetScore/tolerance drive the main accept/reject
    /// decision; metricRanges are an optional secondary check against named metrics an evaluator
    /// reports (e.g. "moveCount" must fall in [4,10] for a "Medium" tier even if the scalar score
    /// alone would pass).
    /// </summary>
    [CreateAssetMenu(fileName = "NewDifficultyTier", menuName = "LevelForge/Difficulty Tier")]
    public class DifficultyTier : ScriptableObject
    {
        public string tierName;

        [Range(0f, 1f)]
        public float targetScore = 0.5f;

        [Tooltip("Half-width of the acceptance band around targetScore, before any per-attempt tolerance relaxation from SearchBudget is applied.")]
        [Range(0f, 1f)]
        public float scoreTolerance = 0.1f;

        [System.Serializable]
        public struct MetricRange
        {
            public string metricName;
            public float min;
            public float max;
        }

        [Tooltip("Optional extra constraints on named metrics an evaluator reports (e.g. moveCount, pieceCount). Leave empty to rely on targetScore/scoreTolerance alone.")]
        public List<MetricRange> metricRanges = new List<MetricRange>();

        public bool IsScoreWithinTolerance(float score, float toleranceMultiplier = 1f)
        {
            return Mathf.Abs(score - targetScore) <= scoreTolerance * Mathf.Max(0.0001f, toleranceMultiplier);
        }

        /// <summary>
        /// True if metrics satisfies every configured range (missing metrics are treated as
        /// "not constrained" rather than a failure, since not every evaluator reports every metric).
        /// </summary>
        public bool MetricsWithinRange(Dictionary<string, float> metrics)
        {
            if (metricRanges == null || metricRanges.Count == 0) return true;
            if (metrics == null) return true;

            foreach (var range in metricRanges)
            {
                if (metrics.TryGetValue(range.metricName, out float value))
                {
                    if (value < range.min || value > range.max) return false;
                }
            }
            return true;
        }
    }
}
