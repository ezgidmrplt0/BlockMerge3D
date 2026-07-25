namespace LevelForge
{
    /// <summary>
    /// Generic, game-agnostic classification of why a generated candidate was rejected.
    /// Adapters map their own solver/validator failure messages onto these values ONCE,
    /// in a single place, instead of scattering string-matching (e.g. reason.Contains("..."))
    /// across the calling code. See ADAPTER_GUIDE.md.
    /// </summary>
    public enum FailureReasonCode
    {
        None,
        InsufficientContent,
        ExcessContent,
        StructurallyUnsolvable,
        SearchBudgetExceeded,
        ConstraintViolation,
        Unknown
    }
}
