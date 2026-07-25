using System;

namespace LevelForge
{
    public enum MutationDirection
    {
        /// <summary>Score just needs to be checked against a wider/narrower target - not a hard failure.</summary>
        TooEasy,
        TooHard,
        /// <summary>Candidate was invalid outright (see MutationHint.reason for why).</summary>
        Invalid
    }

    public struct MutationHint
    {
        public MutationDirection direction;
        public FailureReasonCode reason;
        public EvaluationResult lastEvaluation;
        public int attemptIndex;
    }

    /// <summary>
    /// Defines how generation parameters get nudged between attempts when the previous candidate
    /// didn't match the requested difficulty (or was invalid). Replaces ad-hoc, string-matched
    /// "if failureReason.Contains(...)" dispatch with a structured, reusable contract.
    /// </summary>
    public interface IParameterSpace<TParams>
    {
        TParams Mutate(TParams current, MutationHint hint, Random rng);
    }
}
