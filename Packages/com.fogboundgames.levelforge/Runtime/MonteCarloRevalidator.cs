using System;

namespace LevelForge
{
    public static class MonteCarloRevalidator
    {
        public struct Result
        {
            public int trials;
            public int passes;
            public float PassRate => trials == 0 ? 1f : (float)passes / trials;
        }

        public static Result Run<TCandidate>(TCandidate candidate, IStochasticReevaluator<TCandidate> reevaluator, int trials, Random rng)
        {
            int passes = 0;
            for (int i = 0; i < trials; i++)
            {
                if (reevaluator.ReplayOnce(candidate, rng)) passes++;
            }
            return new Result { trials = trials, passes = passes };
        }
    }
}
