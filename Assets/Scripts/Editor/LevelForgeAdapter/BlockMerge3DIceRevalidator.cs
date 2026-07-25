using UnityEngine;
using LevelForge;

/// <summary>
/// LevelForge.IStochasticReevaluator implementasyonu — bkz. LevelSolver.ReplayWithRandomizedColors
/// ve ADAPTER_GUIDE.md "Ice example" bölümü. Buzsuz seviyelerde daima true döner (renk hiçbir
/// etkiye sahip değil), buzlu seviyelerde solver'ın deterministik vekil-renk varsayımının gerçek
/// (rastgele) renk atamasıyla kırılıp kırılmadığını tek seferlik replay ile test eder.
/// </summary>
public class BlockMerge3DIceRevalidator : IStochasticReevaluator<BlockMerge3DCandidate>
{
    // LevelManager.pieceMaterials / AILevelDesignerWindow.PIECE_COLORS ile aynı büyüklükte —
    // gerçek oyunda parçaya atanabilecek renk sayısı. Bu iki palet arasında derleme-zamanı bir
    // bağ yok (bkz. AILevelDesignerWindow.cs:111 notu); burada sabit tutulması, kütüphanenin
    // gerçek proje paletiyle senkron kalmasını GEREKTİRİR (gelecekte config'e taşınabilir).
    public const int IcePaletteSize = 8;

    public bool ReplayOnce(BlockMerge3DCandidate candidate, System.Random rng)
    {
        if (candidate.frozenCells == null || candidate.frozenCells.Count == 0) return true;
        if (candidate.lastSolverResult == null || !candidate.lastSolverResult.isSolvable || candidate.lastSolverResult.solutionSteps == null)
            return false;

        GameObject mainShape = BlockMerge3DDifficultyEvaluator.BuildTempMainShape(candidate);
        try
        {
            var holder = mainShape.GetComponent<CubeShapeDataHolder>();
            var solver = new LevelSolver();
            float passRate = solver.ReplayWithRandomizedColors(candidate.lastSolverResult.solutionSteps, holder, 1, IcePaletteSize, rng);
            return passRate >= 1f;
        }
        finally
        {
            Object.DestroyImmediate(mainShape);
        }
    }
}
