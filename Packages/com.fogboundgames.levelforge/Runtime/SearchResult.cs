using System.Collections.Generic;

namespace LevelForge
{
    /// <summary>Diagnostic record of a single attempt inside a <see cref="DifficultySearchEngine"/> run.</summary>
    public class AttemptDiagnostic
    {
        public int attemptIndex;
        public bool wasValid;
        public float difficultyScore;
        public FailureReasonCode reason;
        public string message;
        public float toleranceMultiplierUsed;
        /// <summary>1 when no stochastic re-check ran or it fully passed; otherwise the observed pass rate.</summary>
        public float stochasticPassRate = 1f;
    }

    /// <summary>
    /// Outcome of a full <see cref="DifficultySearchEngine"/> run. When <see cref="success"/> is
    /// false, callers MUST NOT fall back to "closest attempt" as if it were an accepted result -
    /// that is the exact open-loop behavior this engine exists to replace. Use
    /// <see cref="failureSummary"/> (and <see cref="allAttempts"/> for full detail) to report why.
    /// </summary>
    public class SearchResult<TCandidate>
    {
        public bool success;
        public TCandidate best;
        public int attemptsUsed;
        public List<AttemptDiagnostic> allAttempts = new List<AttemptDiagnostic>();
        public string failureSummary;
    }
}
