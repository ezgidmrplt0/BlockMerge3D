using System.Collections.Generic;

namespace LevelForge
{
    /// <summary>
    /// Result of evaluating a single generated candidate against a project's own validity/solvability
    /// rules. This is the ONLY place a game-specific evaluator needs to translate its own result type
    /// (e.g. a solver's SolverResult) into something the generic <see cref="DifficultySearchEngine"/>
    /// can reason about.
    /// </summary>
    public struct EvaluationResult
    {
        /// <summary>Candidate is usable (e.g. solvable) at all - independent of whether its difficulty matches the target.</summary>
        public bool isValid;

        /// <summary>
        /// True when invalidity was PROVEN (e.g. a geometric impossibility), false when it is merely
        /// unresolved (e.g. a search budget/timeout was hit and validity is unknown). Mirrors the
        /// "timedOut" distinction that matters for a lot of backtracking-based solvers - a search
        /// budget hit is not proof of impossibility and callers may want to treat it differently.
        /// </summary>
        public bool isDefinitivelyInvalid;

        /// <summary>Normalized 0..1 difficulty score, comparable against a <see cref="DifficultyTier"/>.targetScore.</summary>
        public float difficultyScore;

        public FailureReasonCode reason;
        public string diagnosticMessage;

        /// <summary>
        /// Arbitrary named metrics (e.g. "moveCount", "pieceCount") checked against a tier's
        /// configured metric ranges, in addition to the single scalar difficultyScore.
        /// </summary>
        public Dictionary<string, float> metrics;

        public static EvaluationResult Valid(float difficultyScore, Dictionary<string, float> metrics = null)
        {
            return new EvaluationResult
            {
                isValid = true,
                isDefinitivelyInvalid = false,
                difficultyScore = difficultyScore,
                reason = FailureReasonCode.None,
                diagnosticMessage = null,
                metrics = metrics
            };
        }

        public static EvaluationResult Invalid(FailureReasonCode reason, string diagnosticMessage, bool definitive = true, Dictionary<string, float> metrics = null)
        {
            return new EvaluationResult
            {
                isValid = false,
                isDefinitivelyInvalid = definitive,
                difficultyScore = 0f,
                reason = reason,
                diagnosticMessage = diagnosticMessage,
                metrics = metrics
            };
        }
    }
}
