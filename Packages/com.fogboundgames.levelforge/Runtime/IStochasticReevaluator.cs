using System;

namespace LevelForge
{
    /// <summary>
    /// Optional second layer of validation for candidates whose acceptance depends on some
    /// randomized runtime detail the deterministic evaluator can't fully account for (e.g. a
    /// puzzle game where piece color is assigned randomly at spawn, but the solver used a fixed
    /// proxy color to find a solution quickly). Implementations replay/re-check the ALREADY-FOUND
    /// candidate under a randomized sample of that runtime detail, cheaply, without re-running a
    /// full search - see ADAPTER_GUIDE.md and BlockMerge3DIceRevalidator for a concrete example.
    /// </summary>
    public interface IStochasticReevaluator<TCandidate>
    {
        /// <summary>Returns true if the candidate still holds up under this one randomized trial.</summary>
        bool ReplayOnce(TCandidate candidate, Random rng);
    }
}
