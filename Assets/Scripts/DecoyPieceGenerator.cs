using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Zorluk seviyesine göre tahtaya yerleştirilebilir ancak katman çözümünün ana parçası olmayan
/// alakasız (tuzak / distraction) parçaları seçer ve üretir.
/// </summary>
public static class DecoyPieceGenerator
{
    public class DecoyItem
    {
        public GameObject prefab;
        public int prefabIndex;
    }

    /// <summary>
    /// Seviye numarasına göre kaç adet alakasız (decoy) parça üretileceğini belirler.
    /// Level 1-3: 0 (Saf Çözüm - Öğretici seviyeleri)
    /// Level 4-6: 1 Alakasız Parça
    /// Level 7+: 2 Alakasız Parça
    /// </summary>
    public static int GetDecoyCountForLevel(int levelNumber)
    {
        if (levelNumber <= 3) return 0;
        if (levelNumber <= 6) return 1;
        return 2;
    }

    /// <summary>
    /// Katman çözümü dışında kalan ve tahtaya yerleştirilebilir (CanPlace == true) alakasız parçaları seçer.
    /// </summary>
    public static List<DecoyItem> GenerateDecoyPieces(
        int levelNumber,
        List<LayerSolutionSolver.SolutionItem> solutionPieces,
        List<GameObject> allPiecePrefabs,
        GridManager gridManager,
        List<int> availableIndices)
    {
        List<DecoyItem> result = new List<DecoyItem>();
        int requiredCount = GetDecoyCountForLevel(levelNumber);
        if (requiredCount <= 0 || allPiecePrefabs == null || allPiecePrefabs.Count == 0)
            return result;

        HashSet<int> solutionPrefabIndices = new HashSet<int>(solutionPieces.Select(s => s.prefabIndex));

        // Çözüm parçaları haricinde kalan ve yerleştirilebilir parçaları filtrele
        List<int> candidateIndices = new List<int>();
        foreach (int idx in availableIndices)
        {
            if (solutionPrefabIndices.Contains(idx)) continue; // Çözüm parçası olmasın
            if (idx < 0 || idx >= allPiecePrefabs.Count || allPiecePrefabs[idx] == null) continue;

            // Bu parçanın tahtaya yerleştirilebilir olduğunu doğrula
            if (gridManager != null)
            {
                var holder = allPiecePrefabs[idx].GetComponent<CubeShapeDataHolder>();
                if (holder != null && holder.occupiedCells.Count > 0)
                {
                    // Sığabilirlik testi
                    bool canPlaceSomewhere = gridManager.GetPossibleOffsets(holder.occupiedCells).Count > 0;

                    if (canPlaceSomewhere)
                    {
                        candidateIndices.Add(idx);
                    }
                }
            }
            else
            {
                candidateIndices.Add(idx);
            }
        }

        // Sadece tahtaya gerçekten sığabilen decoy parçalarını kullan (sığmayan parçaları asla ekleme)

        // Karıştır ve istenen sayı kadar ekle
        candidateIndices = candidateIndices.OrderBy(_ => Random.value).ToList();
        for (int i = 0; i < Mathf.Min(requiredCount, candidateIndices.Count); i++)
        {
            int selectedIdx = candidateIndices[i];
            result.Add(new DecoyItem
            {
                prefab = allPiecePrefabs[selectedIdx],
                prefabIndex = selectedIdx
            });
        }

        return result;
    }
}
