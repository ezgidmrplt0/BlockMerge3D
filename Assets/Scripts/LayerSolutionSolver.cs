using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Seviyenin aktif katmanı (layerY) için tam kapatma sağlayan garantili çözüm parçalarını türetir.
/// Seviye tasarımında etiketlenmiş parçalar varsa onları, yoksa dinamik olarak katmanı
/// eksiksiz dolduran çözüm dizisini (SolutionItem listesi) döndürür.
/// </summary>
public static class LayerSolutionSolver
{
    public class SolutionItem
    {
        public GameObject prefab;
        public int prefabIndex;
        public List<Vector3Int> targetWorldCells;
    }

    /// <summary>
    /// Katman hücrelerini (layerCells) allPiecePrefabs listesindeki parçalarla tam olarak döşeyen bir çözüm üretir.
    /// </summary>
    public static List<SolutionItem> SolveLayer(
        int layerY,
        List<Vector3Int> layerCells,
        List<GameObject> allPiecePrefabs,
        GridManager gridManager)
    {
        List<SolutionItem> result = new List<SolutionItem>();
        if (layerCells == null || layerCells.Count == 0 || allPiecePrefabs == null || allPiecePrefabs.Count == 0)
            return result;

        // 1. Kontrol: allPiecePrefabs içinde bu katmana özel etiketli parçalar var mı? (originLayerY == layerY)
        List<int> taggedIndices = new List<int>();
        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            if (allPiecePrefabs[i] == null) continue;
            var holder = allPiecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (holder != null && holder.originLayerY == layerY)
            {
                taggedIndices.Add(i);
            }
        }

        if (taggedIndices.Count > 0)
        {
            foreach (int idx in taggedIndices)
            {
                var prefab = allPiecePrefabs[idx];
                var holder = prefab.GetComponent<CubeShapeDataHolder>();
                List<Vector3Int> cells = holder != null && holder.occupiedCells.Count > 0 
                    ? holder.occupiedCells 
                    : new List<Vector3Int> { Vector3Int.zero };

                result.Add(new SolutionItem
                {
                    prefab = prefab,
                    prefabIndex = idx,
                    targetWorldCells = cells.Select(c => new Vector3Int(c.x, layerY, c.z)).ToList()
                });
            }
            return result;
        }

        // 2. Dinamik Katman Ayrıştırma: Sığabilen parçalardan katmanı tam kaplayacak bir çözüm kümesi oluştur
        HashSet<Vector3Int> remaining = new HashSet<Vector3Int>(layerCells);

        var candidatePrefabs = allPiecePrefabs
            .Select((p, idx) => new { prefab = p, index = idx, holder = p != null ? p.GetComponent<CubeShapeDataHolder>() : null })
            .Where(x => x.holder != null && x.holder.occupiedCells.Count > 0 && x.holder.occupiedCells.Count <= remaining.Count)
            .OrderByDescending(x => x.holder.occupiedCells.Count)
            .ToList();

        foreach (var candidate in candidatePrefabs)
        {
            if (remaining.Count == 0) break;

            List<Vector3Int> shapeCells = candidate.holder.occupiedCells;

            // Katman hücreleri üzerinde bu parçanın sığabileceği ilk konumu ara
            foreach (var cell in remaining.ToList())
            {
                List<Vector3Int> placedWorld = shapeCells.Select(sc => new Vector3Int(cell.x + sc.x, layerY, cell.z + sc.z)).ToList();

                if (placedWorld.All(pw => remaining.Contains(pw)))
                {
                    result.Add(new SolutionItem
                    {
                        prefab = candidate.prefab,
                        prefabIndex = candidate.index,
                        targetWorldCells = placedWorld
                    });

                    foreach (var pw in placedWorld)
                    {
                        remaining.Remove(pw);
                    }
                    break;
                }
            }
        }

        // Geride kalan tekil hücreler varsa 1x1 parçalarla tamamla
        if (remaining.Count > 0)
        {
            var singlePrefabItem = candidatePrefabs.FirstOrDefault(x => x.holder.occupiedCells.Count == 1) ?? candidatePrefabs.LastOrDefault();
            if (singlePrefabItem != null)
            {
                foreach (var remCell in remaining.ToList())
                {
                    result.Add(new SolutionItem
                    {
                        prefab = singlePrefabItem.prefab,
                        prefabIndex = singlePrefabItem.index,
                        targetWorldCells = new List<Vector3Int> { remCell }
                    });
                }
            }
        }

        return result;
    }
}
