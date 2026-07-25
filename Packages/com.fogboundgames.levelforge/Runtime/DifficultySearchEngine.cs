using System;
using System.Diagnostics;

namespace LevelForge
{
    /// <summary>
    /// The generic closed-loop core of LevelForge: generate a candidate, evaluate it, check whether
    /// its difficulty matches the requested tier (within the current attempt's tolerance), optionally
    /// stochastically re-validate it, and either accept it or mutate parameters and try again - up to
    /// a bounded budget. Unlike a "generate N candidates, keep whichever scored closest" strategy,
    /// a failed run reports failure rather than silently returning an off-target candidate; see
    /// <see cref="SearchResult{TCandidate}.success"/>.
    ///
    /// This class has ZERO knowledge of any specific game's rules - all of that lives behind
    /// <see cref="IDifficultyEvaluator{TCandidate}"/> and <see cref="IParameterSpace{TParams}"/>,
    /// which each adapter implements. See ADAPTER_GUIDE.md.
    /// </summary>
    public class DifficultySearchEngine
    {
        public SearchResult<TCandidate> Run<TParams, TCandidate>(
            TParams initialParams,
            DifficultyTier tier,
            Func<TParams, TCandidate> generate,
            IDifficultyEvaluator<TCandidate> evaluator,
            IParameterSpace<TParams> paramSpace,
            SearchBudget budget,
            IStochasticReevaluator<TCandidate> stochasticCheck = null,
            int stochasticTrials = 10,
            float stochasticRequiredPassRate = 1f,
            int? randomSeed = null)
        {
            if (tier == null) throw new ArgumentNullException(nameof(tier));
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            if (paramSpace == null) throw new ArgumentNullException(nameof(paramSpace));
            budget = budget ?? new SearchBudget();

            var rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
            var result = new SearchResult<TCandidate>();
            var stopwatch = Stopwatch.StartNew();
            TParams currentParams = initialParams;

            int attempt = 0;
            for (; attempt < budget.maxAttempts; attempt++)
            {
                if (stopwatch.ElapsedMilliseconds > budget.maxTotalTimeMs)
                {
                    result.failureSummary = $"Zaman bütçesi aşıldı ({budget.maxTotalTimeMs}ms), {attempt} deneme tamamlanmıştı.";
                    break;
                }

                TCandidate candidate;
                try
                {
                    candidate = generate(currentParams);
                }
                catch (Exception ex)
                {
                    result.allAttempts.Add(new AttemptDiagnostic
                    {
                        attemptIndex = attempt,
                        wasValid = false,
                        reason = FailureReasonCode.Unknown,
                        message = $"Üretim sırasında istisna: {ex.Message}"
                    });
                    currentParams = paramSpace.Mutate(currentParams, new MutationHint
                    {
                        direction = MutationDirection.Invalid,
                        reason = FailureReasonCode.Unknown,
                        attemptIndex = attempt
                    }, rng);
                    continue;
                }

                var eval = evaluator.Evaluate(candidate);
                float toleranceMultiplier = budget.GetToleranceMultiplier(attempt);
                var diagnostic = new AttemptDiagnostic
                {
                    attemptIndex = attempt,
                    wasValid = eval.isValid,
                    difficultyScore = eval.difficultyScore,
                    reason = eval.reason,
                    message = eval.diagnosticMessage,
                    toleranceMultiplierUsed = toleranceMultiplier
                };

                if (!eval.isValid)
                {
                    result.allAttempts.Add(diagnostic);
                    currentParams = paramSpace.Mutate(currentParams, new MutationHint
                    {
                        direction = MutationDirection.Invalid,
                        reason = eval.reason,
                        lastEvaluation = eval,
                        attemptIndex = attempt
                    }, rng);
                    continue;
                }

                bool scoreOk = tier.IsScoreWithinTolerance(eval.difficultyScore, toleranceMultiplier);
                bool metricsOk = tier.MetricsWithinRange(eval.metrics);

                if (!scoreOk || !metricsOk)
                {
                    result.allAttempts.Add(diagnostic);
                    var direction = eval.difficultyScore > tier.targetScore ? MutationDirection.TooHard : MutationDirection.TooEasy;
                    currentParams = paramSpace.Mutate(currentParams, new MutationHint
                    {
                        direction = direction,
                        reason = FailureReasonCode.None,
                        lastEvaluation = eval,
                        attemptIndex = attempt
                    }, rng);
                    continue;
                }

                // Difficulty matches the target - optionally confirm it holds under randomized replay.
                float passRate = 1f;
                if (stochasticCheck != null)
                {
                    passRate = MonteCarloRevalidator.Run(candidate, stochasticCheck, stochasticTrials, rng).PassRate;
                }
                diagnostic.stochasticPassRate = passRate;

                if (passRate < stochasticRequiredPassRate)
                {
                    diagnostic.wasValid = false;
                    diagnostic.reason = FailureReasonCode.ConstraintViolation;
                    diagnostic.message = $"Zorluk hedefi tutturuldu ama stokastik doğrulamadan geçemedi (geçiş oranı %{passRate * 100f:F0}, gereken %{stochasticRequiredPassRate * 100f:F0})";
                    result.allAttempts.Add(diagnostic);
                    currentParams = paramSpace.Mutate(currentParams, new MutationHint
                    {
                        direction = MutationDirection.Invalid,
                        reason = FailureReasonCode.ConstraintViolation,
                        lastEvaluation = eval,
                        attemptIndex = attempt
                    }, rng);
                    continue;
                }

                result.allAttempts.Add(diagnostic);
                result.success = true;
                result.best = candidate;
                result.attemptsUsed = attempt + 1;
                return result;
            }

            result.attemptsUsed = result.allAttempts.Count;
            if (string.IsNullOrEmpty(result.failureSummary))
            {
                var closest = FindClosestValidAttempt(result, tier.targetScore);
                result.failureSummary = closest != null
                    ? $"{result.attemptsUsed} deneme yapıldı, hedefe (skor {tier.targetScore:F2}) toleransla ulaşılamadı. " +
                      $"En yakın deneme: skor={closest.difficultyScore:F2}, sebep={closest.reason}, {closest.message}"
                    : $"{result.attemptsUsed} deneme yapıldı, hiçbir aday geçerli/çözülebilir bulunamadı.";
            }
            return result;
        }

        private static AttemptDiagnostic FindClosestValidAttempt<TCandidate>(SearchResult<TCandidate> result, float targetScore)
        {
            AttemptDiagnostic best = null;
            float bestDistance = float.MaxValue;
            foreach (var attempt in result.allAttempts)
            {
                if (!attempt.wasValid) continue;
                float distance = Math.Abs(attempt.difficultyScore - targetScore);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = attempt;
                }
            }
            return best ?? (result.allAttempts.Count > 0 ? result.allAttempts[result.allAttempts.Count - 1] : null);
        }
    }
}
