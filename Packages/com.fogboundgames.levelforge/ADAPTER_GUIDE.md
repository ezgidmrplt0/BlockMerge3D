# LevelForge — Adapter Guide

LevelForge is **not** a level generator. It is the closed-loop *search* around one:

> generate a candidate → evaluate it → is its difficulty within tolerance of the target? → if not, mutate parameters and try again → give up cleanly after a bounded budget.

It has no idea what a "level" is in your game — no grid, no pieces, no ice, no match-3 tiles. That
knowledge lives entirely in the four things **you** write per project ("the adapter"). This is
deliberate: it's what makes the same engine usable in an unrelated genre — a match-3 board and a
voxel puzzle share nothing except "some way to make one, some way to tell if it's good, some way to
try again differently."

## What you provide

| # | You write | Wraps |
|---|---|---|
| 1 | `TParams` | Your own struct/class of generation knobs (grid size, fill %, obstacle %, whatever your generator takes). |
| 2 | `TCandidate` | Your own generated-level representation (whatever your generator produces and your validator consumes). |
| 3 | `Func<TParams, TCandidate>` | A plain function — usually your existing generator, unchanged. |
| 4 | `IDifficultyEvaluator<TCandidate>` | Wraps your existing solver/validator. Returns `EvaluationResult` (valid?, 0–1 score, `FailureReasonCode`, optional named metrics). |
| 5 | `IParameterSpace<TParams>` | Given a `MutationHint` (too easy / too hard / invalid + why), returns a nudged `TParams` for the next attempt. |
| 6 | (optional) `IStochasticReevaluator<TCandidate>` | Only if your validity depends on something randomized at runtime that your evaluator approximated deterministically (see "Ice example" below). |
| 7 | One `DifficultyTier` asset per named difficulty | `targetScore` + `scoreTolerance`, optionally `metricRanges` for named metrics like move count. |

## Minimal usage

```csharp
var engine = new LevelForge.DifficultySearchEngine();
var result = engine.Run(
    initialParams: myParams,
    tier: myMediumTierAsset,
    generate: p => MyGenerator.Generate(p),
    evaluator: new MyDifficultyEvaluator(),
    paramSpace: new MyParameterSpace(),
    budget: new LevelForge.SearchBudget { maxAttempts = 25, maxTotalTimeMs = 20000 });

if (result.success)
{
    Export(result.best);
}
else
{
    Debug.LogWarning(result.failureSummary); // never silently export the closest-but-off-target candidate
}
```

`result.allAttempts` holds a full diagnostic trail (score, reason, message per attempt) if you want
to show the user *why* generation failed, not just that it did.

## The "ice example" — why `IStochasticReevaluator` exists

BlockMerge3D's solver simulates a color-dependent mechanic (ice melting when touched by matching
colors) using a **deterministic** proxy color during search, because the real game assigns piece
color **randomly** at spawn time. A candidate the solver proves solvable under its proxy coloring
could, in rare cases, be unwinnable under the real random coloring. `BlockMerge3DIceRevalidator`
replays the already-found solution's step order with N randomized colorings and requires a high
pass rate before the engine accepts the candidate. If your game has an analogous "the evaluator's
model is an approximation of something randomized at runtime" gap, this is the hook for it — if it
doesn't, skip it entirely (it's an optional parameter).

## Reference implementation

`Assets/Scripts/Editor/LevelForgeAdapter/` in this repository (BlockMerge3D) is a complete, working
adapter: `BlockMerge3DGenerationParams`, `BlockMerge3DCandidate`, `BlockMerge3DDifficultyEvaluator`,
`BlockMerge3DParameterSpace`, `BlockMerge3DIceRevalidator`, and four `DifficultyTier` assets. Copy
its shape for a new project rather than starting from a blank file — the interfaces are small, but
seeing a real evaluator's `FailureReasonCode` mapping and a real parameter-mutation strategy is the
fastest way to see how the pieces fit.

## What does *not* travel with this package

- Grid/voxel/ice/layer rules — genre-specific, stay in the adapter.
- The generator itself — you already have one; LevelForge only calls it.
- UI chrome for your specific tool — `LevelForge.EditorTools.LevelForgeGUIStyles` offers a few
  generic section-card/stat-tile IMGUI helpers if useful, but your window's layout is yours.

If your next project is a completely different genre, expect to write a new evaluator + parameter
space + tier assets (a few hundred lines, most of it "wrap my existing validator"). What you get for
free is the retry loop itself, the tolerance-relaxation schedule, the structured failure reporting,
and the Monte Carlo hook — the part that's easy to get subtly wrong and easy to under-invest in when
writing it from scratch under deadline.
