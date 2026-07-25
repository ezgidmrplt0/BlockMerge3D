namespace LevelForge
{
    /// <summary>
    /// Wraps a project's own validity/solvability check (e.g. a puzzle solver, a rule-based
    /// simulator, a heuristic scorer) behind a generic contract the search engine can call.
    /// This is the main piece of "translation" an adapter has to write - see ADAPTER_GUIDE.md.
    /// </summary>
    public interface IDifficultyEvaluator<TCandidate>
    {
        EvaluationResult Evaluate(TCandidate candidate);
    }
}
