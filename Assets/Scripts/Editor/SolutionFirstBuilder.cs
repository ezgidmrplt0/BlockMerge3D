using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  SOLUTION FIRST BUILDER  —  Kütüphaneden Parça Seçip Geri İzlemeli Yerleştirme
//  BlockMerge3D  •  LevelSolver.cs'in tersi: "bu parçalarla çözülür mü?" değil,
//  "kütüphaneden hangi parçalarla bu şekli TAM olarak doldurabilirim?" sorusuna
//  collapse-aware (katman katman, alttan üste) backtracking ile cevap arar.
// ═══════════════════════════════════════════════════════════════════

public static class SolutionFirstBuilder
{
    private class PieceCandidate
    {
        public PieceDefinition definition;
        public int usedCount;
        public List<List<Vector3Int>> rotationVariants; // TryBuild'de BİR kez hesaplanan normalize dönüşler
    }

    private static HashSet<Vector3Int> targetCells;
    private static HashSet<Vector3Int> currentOccupied;
    private static Vector3Int gridSize;
    private static List<PieceCandidate> pool;
    private static List<List<Vector3Int>> placedPieces;

    private static System.Diagnostics.Stopwatch stopwatch;
    private static int statesExplored;
    private static int maxStatesExplored;
    private static int maxSearchTimeMs;

    /// <summary>
    /// cellsToFill'i (bir seviyenin doldurulması gereken hücreleri — prefilled hariç)
    /// candidatePool'daki parçalarla tam olarak döşemeye çalışır. Başarılı olursa
    /// resultPieces, her biri bir parçanın DÜNYA koordinatlarındaki hücrelerini içeren
    /// listeler halinde döner (AILevelDesignerWindow.pieceSplitList ile aynı format).
    ///
    /// Renk VE buz erimesi simüle EDİLMEZ — bunlar burada değil, gerçek LevelSolver ile
    /// (AILevelDesignerWindow.TestCurrentPiecesWithSolver üzerinden) sonradan doğrulanır.
    /// Bu bilinçli bir katmanlaşma: builder saf bir geometrik döşeme problemi çözer, asıl
    /// "sıralama gerçekten oynanabilir mi" sorusunu (buz erimesi dahil) LevelSolver
    /// cevaplar. İkisi uyuşmazsa (ör. geometrik olarak döşenebiliyor ama buz sırası
    /// çalışmıyor), bu deneme AILevelDesignerWindow'un çok-denemeli döngüsünde başarısız
    /// bir strateji olarak sayılıp bir sonraki rastgele havuzla tekrar denenir.
    /// </summary>
    public static bool TryBuild(
        HashSet<Vector3Int> cellsToFill,
        Vector3Int boardGridSize,
        List<PieceDefinition> candidatePool,
        int maxStates,
        int maxTimeMs,
        out List<List<Vector3Int>> resultPieces)
    {
        resultPieces = null;
        if (cellsToFill == null || cellsToFill.Count == 0) return false;
        if (candidatePool == null || candidatePool.Count == 0) return false;

        targetCells = cellsToFill;
        currentOccupied = new HashSet<Vector3Int>();
        gridSize = boardGridSize;
        // Havuz BİR kez kuruluyor: dönüş varyantları önceden hesaplanıyor ve sıralama (büyük
        // parçalar önce, eşitlikte spawnWeight, sonra rastgele tie-break) burada bir kez yapılıyor.
        // Eskiden bu sıralama HER backtracking düğümünde (OrderBy + düğüm başına yeni Random.value)
        // yapılıyor, ayrıca RotateAndNormalize her düğümde yeniden çağrılıyordu → on binlerce
        // düğümde on binlerce gereksiz allocation. Aramanın yavaşlığının / editörü dondurmanın
        // başlıca sebeplerindendi.
        pool = candidatePool
            .Where(d => d != null && d.cells != null && d.cells.Count > 0)
            .Select(d => new PieceCandidate
            {
                definition = d,
                usedCount = 0,
                rotationVariants = BuildRotationVariants(d)
            })
            .Where(p => p.rotationVariants.Count > 0)
            .OrderByDescending(p => p.definition.volume)
            .ThenByDescending(p => p.definition.spawnWeight)
            .ThenBy(_ => Random.value)
            .ToList();
        placedPieces = new List<List<Vector3Int>>();

        if (pool.Count == 0) return false;

        stopwatch = System.Diagnostics.Stopwatch.StartNew();
        statesExplored = 0;
        maxStatesExplored = maxStates;
        maxSearchTimeMs = maxTimeMs;

        bool solved = BacktrackingBuild();
        if (solved) resultPieces = placedPieces;
        return solved;
    }

    private static bool BacktrackingBuild()
    {
        statesExplored++;
        if (stopwatch.ElapsedMilliseconds > maxSearchTimeMs || statesExplored > maxStatesExplored)
            return false;

        if (targetCells.All(c => currentOccupied.Contains(c)))
            return true; // tamamen dolduruldu

        int activeLayer = GetLowestIncompleteLayer();

        // Havuz TryBuild'de BİR kez sıralandı (büyük parçalar önce — böylece arama büyük/ilginç
        // parçalara öncelik verir, tek-hücrelik Filler'lara sadece büyükler sığmayınca düşülür)
        // ve dönüş varyantları önceden hesaplandı. Burada yalnızca maxCopies filtresini uygulayıp
        // doğrudan geziyoruz; düğüm başına OrderBy/RotateAndNormalize allocation'ı YOK.
        foreach (var candidate in pool)
        {
            var def = candidate.definition;
            if (def.maxCopiesPerLevel >= 0 && candidate.usedCount >= def.maxCopiesPerLevel) continue;

            foreach (var rotatedCells in candidate.rotationVariants)
            {
                foreach (var offset in GetPossibleOffsets(rotatedCells, activeLayer))
                {
                    if (TryPlace(rotatedCells, offset, activeLayer))
                    {
                        candidate.usedCount++;

                        if (BacktrackingBuild())
                            return true;

                        Undo(rotatedCells, offset);
                        candidate.usedCount--;
                    }
                }
            }
        }

        return false;
    }

    // Bir tanımın izin verilen tüm dönüşlerini BİR kez hesaplayıp normalize eder. Aynı normalize
    // hücre setini veren dönüşler (simetrik parçalar) elenir → gereksiz dallanma da azalır.
    private static List<List<Vector3Int>> BuildRotationVariants(PieceDefinition def)
    {
        var rotations = (def.allowedRotations != null && def.allowedRotations.Count > 0)
            ? def.allowedRotations
            : new List<Vector3Int> { Vector3Int.zero };

        var variants = new List<List<Vector3Int>>(rotations.Count);
        var seen = new HashSet<string>();
        foreach (var rotEuler in rotations)
        {
            var rotated = PieceGeometryUtils.RotateAndNormalize(
                def.cells, Quaternion.Euler(rotEuler.x, rotEuler.y, rotEuler.z));
            if (rotated == null || rotated.Count == 0) continue;

            // Tiler bir parçayı YALNIZCA tek bir katmana yerleştirir (bkz. TryPlace: tüm hücreler
            // activeLayerY'de olmalı). Çok-katmanlı (3D) varyantlar ASLA yerleşemez. Bunları eleyerek:
            //  (a) kütüphaneye yanlışlıkla girmiş çok-katmanlı "tüm-level" artefaktları (Level6-12,
            //      AI_Level_1 vb. — hiçbir dönüşte tek-katman olmayan 7-75 hücrelik 3D şekiller)
            //      TAMAMEN havuz dışı kalır → SampleEligiblePool onları örnekleyip havuzu bunlarla
            //      doldurup gerçek parça bırakmama ("InsufficientContent") sorunu ortadan kalkar;
            //  (b) yerleşemeyecek varyantlarda boşa dallanma/offset taraması yapılmaz (hız).
            if (!IsSingleLayer(rotated)) continue;

            string key = string.Join(";", rotated
                .OrderBy(c => c.x).ThenBy(c => c.y).ThenBy(c => c.z)
                .Select(c => $"{c.x},{c.y},{c.z}"));
            if (seen.Add(key)) variants.Add(rotated);
        }
        return variants;
    }

    private static bool IsSingleLayer(List<Vector3Int> cells)
    {
        int y = cells[0].y;
        for (int i = 1; i < cells.Count; i++)
            if (cells[i].y != y) return false;
        return true;
    }

    // Üretim her zaman alttan üste ilerler (oynanış sırası GridManager'da ayrı — bkz.
    // GridManager.TryFindNextRequiredLayer, artık üstten alta) / LevelSolver.GetLowestIncompleteLayer
    // ile aynı mantık: en alttaki dolu-olmayan katmanı döndürür.
    private static int GetLowestIncompleteLayer()
    {
        int minY = targetCells.Min(c => c.y);
        int maxY = targetCells.Max(c => c.y);

        for (int y = minY; y <= maxY; y++)
        {
            bool hasAny = false;
            bool allFilled = true;
            foreach (var c in targetCells)
            {
                if (c.y != y) continue;
                hasAny = true;
                if (!currentOccupied.Contains(c)) { allFilled = false; break; }
            }
            if (!hasAny) continue;
            if (!allFilled) return y;
        }

        return maxY + 1; // tüm katmanlar tamamlandı
    }

    // LevelSolver.GetPossibleOffsets ile aynı: parçanın hücreleri (RotateAndNormalize ile
    // normalize edildiği için) ortak bir Y paylaşır, tek geçerli offset.y bu yüzden sabit —
    // sadece aktif katmanın X/Z düzlemi taranır.
    private static IEnumerable<Vector3Int> GetPossibleOffsets(List<Vector3Int> cells, int activeLayerY)
    {
        int offsetY = activeLayerY - cells[0].y;

        for (int x = 0; x < gridSize.x; x++)
        {
            for (int z = 0; z < gridSize.z; z++)
            {
                bool fits = true;
                foreach (var cell in cells)
                {
                    Vector3Int worldCell = new Vector3Int(x + cell.x, offsetY + cell.y, z + cell.z);
                    if (worldCell.x < 0 || worldCell.x >= gridSize.x ||
                        worldCell.y < 0 || worldCell.y >= gridSize.y ||
                        worldCell.z < 0 || worldCell.z >= gridSize.z)
                    {
                        fits = false;
                        break;
                    }
                }
                if (fits) yield return new Vector3Int(x, offsetY, z);
            }
        }
    }

    private static bool TryPlace(List<Vector3Int> cells, Vector3Int offset, int activeLayerY)
    {
        var worldCells = new List<Vector3Int>(cells.Count);
        foreach (var c in cells)
        {
            var world = c + offset;

            // Parçaların tek katmanda olması zorunluluğu (bkz. LevelSolver.TryPlacePiece) —
            // savunmacı kontrol, GetPossibleOffsets zaten sadece bunu üretiyor.
            if (world.y != activeLayerY) return false;
            if (!targetCells.Contains(world)) return false;
            if (currentOccupied.Contains(world)) return false;

            worldCells.Add(world);
        }

        foreach (var w in worldCells) currentOccupied.Add(w);
        placedPieces.Add(worldCells);
        return true;
    }

    private static void Undo(List<Vector3Int> cells, Vector3Int offset)
    {
        placedPieces.RemoveAt(placedPieces.Count - 1);
        foreach (var c in cells) currentOccupied.Remove(c + offset);
    }
}
