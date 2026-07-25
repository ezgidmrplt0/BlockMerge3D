using System.Collections.Generic;
using UnityEngine;

// Bir arama denemesinin ürettiği tüm veri: şekil + o denemeye özgü engeller + parça bölünmesi.
// LevelForge.DifficultySearchEngine bu tipin içeriğini hiç bilmez, sadece opak bir TCandidate
// olarak taşır — Evaluate/Mutate/ReplayOnce üçlüsü bunu somutlaştırır.
public class BlockMerge3DCandidate
{
    public Vector3Int gridSize;
    public float cellSize;
    public float spacing;

    public List<Vector3Int> occupiedCells;
    public List<Vector3Int> prefilledCells;
    public List<int> prefilledMaterialIndices;
    public List<Vector3Int> frozenCells;
    public List<int> frozenHitCounts;

    public List<List<Vector3Int>> pieceSplitList;

    /// <summary>BlockMerge3DDifficultyEvaluator.Evaluate tarafından doldurulur — export ve UI
    /// tarafının (DrawSolverResultSection) ihtiyaç duyduğu tam solver çıktısını taşır.</summary>
    public SolverResult lastSolverResult;
}
