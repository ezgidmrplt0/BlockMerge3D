using UnityEngine;

namespace LevelForge
{
    /// <summary>
    /// Bounds a <see cref="DifficultySearchEngine"/> run: how many attempts / how much wall-clock
    /// time it may spend, and how much the score-acceptance tolerance is allowed to relax as
    /// attempts are spent without a match. Relaxing tolerance over time trades "closer to a
    /// pathological infinite retry loop" for "closer to the target but not exact" instead of
    /// failing outright - callers should keep the schedule's last value modest (e.g. no more than
    /// ~2x the tier's base tolerance) so a relaxed accept never bleeds into a neighboring tier's
    /// intended range.
    /// </summary>
    [System.Serializable]
    public class SearchBudget
    {
        public int maxAttempts = 20;
        public int maxTotalTimeMs = 15000;

        [Tooltip("Tolerance multiplier applied per attempt index (clamped to the last entry once attempts exceed the array length). Index 0 = first attempt.")]
        public float[] toleranceMultiplierSchedule = { 1f, 1f, 1f, 1f, 1.25f, 1.25f, 1.5f, 1.5f, 2f };

        public float GetToleranceMultiplier(int attemptIndex)
        {
            if (toleranceMultiplierSchedule == null || toleranceMultiplierSchedule.Length == 0) return 1f;
            int idx = Mathf.Clamp(attemptIndex, 0, toleranceMultiplierSchedule.Length - 1);
            return toleranceMultiplierSchedule[idx];
        }
    }
}
