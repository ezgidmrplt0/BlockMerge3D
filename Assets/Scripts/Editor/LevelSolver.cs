using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL SOLVER  —  Seviye Çözülebilirlik ve Zorluk Analizi
//  BlockMerge3D  •  Backtracking ile geometrik + renk çözülebilirlik
// ═══════════════════════════════════════════════════════════════════

public class LevelSolver
{
    // ── Zorluk Skorlama Ağırlıkları ───────────────────────────────
    public float weightMoveCount = 0.3f;
    public float weightFrozenRatio = 0.2f;
    public float weightPieceCount = 0.25f;
    public float weightGridVolume = 0.25f;

    // ── Arama Limitleri ───────────────────────────────────────────
    public int maxSearchTimeMs = 5000;      // 5 saniye
    public int maxStatesExplored = 100000;  // 100k durum

    private System.Diagnostics.Stopwatch stopwatch;
    private int statesExplored;
    private bool searchTimedOut;

    // ── Seviye Verisi ─────────────────────────────────────────────
    private Vector3Int gridSize;
    private HashSet<Vector3Int> targetCells;
    private HashSet<Vector3Int> prefilledCells;
    private Dictionary<Vector3Int, int> cellMatIndex;
    private HashSet<Vector3Int> frozenCells;
    private List<PieceData> pieces;

    // ── Çözüm Durumu ──────────────────────────────────────────────
    private HashSet<Vector3Int> currentOccupied;
    private Dictionary<Vector3Int, int> currentMatIndex;
    private List<PlacementStep> currentSolution;
    private SolverResult bestResult;

    public SolverResult Solve(LevelData levelData)
    {
        if (levelData == null || levelData.mainShapePrefab == null)
            return new SolverResult { isSolvable = false, failureReason = "LevelData veya mainShapePrefab null" };

        return SolveFromPrefabs(levelData.mainShapePrefab, levelData.complementaryPieces);
    }

    public SolverResult SolveFromPrefabs(GameObject mainShape, List<GameObject> piecePrefabs)
    {
        // ── 1. Veriyi Hazırla ─────────────────────────────────────
        var holder = mainShape.GetComponent<CubeShapeDataHolder>();
        if (holder == null)
            return new SolverResult { isSolvable = false, failureReason = "mainShape üzerinde CubeShapeDataHolder yok" };

        InitializeFromHolder(holder);

        if (piecePrefabs == null || piecePrefabs.Count == 0)
            return new SolverResult { isSolvable = false, failureReason = "Hiç parça yok" };

        // Parçaları yükle
        pieces = new List<PieceData>();
        for (int i = 0; i < piecePrefabs.Count; i++)
        {
            var ph = piecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (ph != null && ph.occupiedCells.Count > 0)
            {
                var cellsCopy = new List<Vector3Int>(ph.occupiedCells);
                pieces.Add(new PieceData
                {
                    index = i,
                    cells = cellsCopy,
                    used = false,
                    shapeKey = ComputeShapeKey(cellsCopy)
                });
            }
        }

        if (pieces.Count == 0)
            return new SolverResult { isSolvable = false, failureReason = "Geçerli parça yok" };

        // ── 2. Hızlı Ön Kontroller ────────────────────────────────
        
        // Toplam hücre sayısı kontrolü
        int totalPieceCells = pieces.Sum(p => p.cells.Count);
        int emptyTargetCells = targetCells.Count(c => !prefilledCells.Contains(c));

        if (totalPieceCells < emptyTargetCells)
        {
            return new SolverResult
            {
                isSolvable = false,
                failureReason = $"Yetersiz hücre: parçalar={totalPieceCells}, hedef boşluk={emptyTargetCells}"
            };
        }

        // Buz (frozen) hücre YOKSA hacim tam eşit olmak zorunda (fazlalık her zaman şüphelidir).
        // Buz VARSA fazlalık meşru olabilir: bir parça buza değip patladığında (bkz.
        // ResolveFrozenCellsInSolver) o hücreler yeniden doldurulmalı — bu "buz vergisi" ham hedef
        // hacminden FAZLA parça gerektirir. Sıkı eşitlik burada yanlış pozitif ("Fazla hücre") üretip
        // aslında çözülebilir, sadece buz vergisi ödeyen seviyeleri gereksiz yere reddediyordu.
        if (totalPieceCells > emptyTargetCells && frozenCells.Count == 0)
        {
            return new SolverResult
            {
                isSolvable = false,
                failureReason = $"Fazla hücre: parçalar={totalPieceCells}, hedef boşluk={emptyTargetCells}"
            };
        }

        // ── 3. Backtracking Arama Başlat ─────────────────────────
        stopwatch = System.Diagnostics.Stopwatch.StartNew();
        statesExplored = 0;
        searchTimedOut = false;

        currentOccupied = new HashSet<Vector3Int>(prefilledCells);
        currentMatIndex = new Dictionary<Vector3Int, int>(cellMatIndex);
        currentSolution = new List<PlacementStep>();
        bestResult = new SolverResult { isSolvable = false };

        bool solved = BacktrackingSolve(0);

        stopwatch.Stop();

        if (solved)
        {
            bestResult.isSolvable = true;
            bestResult.minMoveCount = currentSolution.Count;
            bestResult.solutionSteps = new List<PlacementStep>(currentSolution);
            bestResult.difficultyScore = CalculateDifficulty(bestResult.minMoveCount);
            bestResult.difficultyLabel = GetDifficultyLabel(bestResult.difficultyScore);
            bestResult.failureReason = "";
        }
        else if (searchTimedOut)
        {
            bestResult.failureReason = $"Arama limiti aşıldı ({maxSearchTimeMs}ms veya {maxStatesExplored} durum) - kesin çözülemez değil";
        }
        else
        {
            bestResult.failureReason = bestResult.failureReason ?? "Çözüm bulunamadı (renk/geometri kısıtları)";
        }

        return bestResult;
    }

    // Parçanın normalize edilmiş (orijine kaydırılmış, sıralanmış) hücre setinden benzersiz bir
    // şekil imzası üretir. İki parçanın shapeKey'i eşitse, geometrik olarak birebir aynı şekildir.
    private static string ComputeShapeKey(List<Vector3Int> cells)
    {
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        foreach (var c in cells)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.z < minZ) minZ = c.z;
        }

        var normalized = cells
            .Select(c => new Vector3Int(c.x - minX, c.y - minY, c.z - minZ))
            .OrderBy(c => c.x).ThenBy(c => c.y).ThenBy(c => c.z)
            .ToList();

        return string.Join("|", normalized.Select(c => $"{c.x},{c.y},{c.z}"));
    }

    private void InitializeFromHolder(CubeShapeDataHolder holder)
    {
        gridSize = holder.gridSize;
        targetCells = new HashSet<Vector3Int>(holder.occupiedCells);
        prefilledCells = new HashSet<Vector3Int>(holder.prefilledCells ?? new List<Vector3Int>());
        frozenCells = new HashSet<Vector3Int>(holder.frozenCells ?? new List<Vector3Int>());

        // Material indekslerini hazırla
        cellMatIndex = new Dictionary<Vector3Int, int>();
        if (holder.prefilledCells != null && holder.prefilledMaterialIndices != null)
        {
            for (int i = 0; i < holder.prefilledCells.Count && i < holder.prefilledMaterialIndices.Count; i++)
            {
                cellMatIndex[holder.prefilledCells[i]] = holder.prefilledMaterialIndices[i];
            }
        }
    }

    private bool BacktrackingSolve(int pieceIdx)
    {
        // Limit kontrolleri
        statesExplored++;
        if (stopwatch.ElapsedMilliseconds > maxSearchTimeMs || statesExplored > maxStatesExplored)
        {
            searchTimedOut = true;
            return false;
        }

        // Tüm hedef hücreler dolu mu?
        if (targetCells.All(c => currentOccupied.Contains(c)))
        {
            if (AllLayersValid())
                return true;
        }

        // Tüm parçalar kullanıldı ama hala boşluk var
        if (pieceIdx >= pieces.Count)
            return false;

        // ERKEN BUDAMA: Herhangi bir katmanda çözülemez renk çakışması varsa dur
        if (HasIrrecoverableColorConflict())
        {
            return false;
        }

        // Collapse-aware: oyuncu gerçek oyunda HER ZAMAN sadece en alttaki tamamlanmamış
        // katmana yerleştirme yapabilir (bkz. GridManager.ActiveLayerY / CanPlace). Arama
        // uzayını buna göre kısıtlıyoruz — üst katmana "erken" yerleştirme denemesi yapılmaz.
        int activeLayer = GetLowestIncompleteLayer();

        // Kullanılmamış bir parça seç. Aynı şekle sahip (örn. Tetromino modunda birçok özdeş
        // parça) birden fazla parça varsa, bu dalda sadece BİRİNİ deneriz — diğerleri tamamen
        // aynı alt-ağacı tekrar keşfeder ve arama uzayını (N! kadar) gereksiz yere patlatır.
        // (Bir ara bu budamanın hatalı olduğu şüphelenilmişti — CreateMediumLevel testi aynı
        // şekilden 2 parça gerektiğinde "çözülemez" çıkıyordu. Kök sebep izole edildiğinde asıl
        // suçlunun o testteki BAĞIMSIZ bir gridSize Y/Z karışıklığı olduğu anlaşıldı — bkz. git
        // geçmişi/LevelSolverTests.cs. Bu budama doğru ve güvenli: aynı şekle sahip iki parçadan
        // BİRİNİN tüm rotasyon/offset kombinasyonlarıyla dene­nip başarısız olması, DİĞERİNİN de
        // (aynı geometri, aynı arama durumu) başarısız olacağı anlamına gelir — sadece BU dal için,
        // her recursion seviyesinde taze bir HashSet ile çalıştığı için farklı katman/derinliklerde
        // aynı şeklin ayrı kopyaları yine denenir.)
        var triedShapesAtThisLevel = new HashSet<string>();
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].used) continue;
            if (pieces[i].shapeKey != null && !triedShapesAtThisLevel.Add(pieces[i].shapeKey)) continue;

            pieces[i].used = true;

            // Tüm rotasyonları dene (0, 90, 180, 270 derece Y ekseni, sonra X ekseni)
            foreach (var rotation in GetAllRotations())
            {
                var rotatedCells = RotateCells(pieces[i].cells, rotation);

                // Sadece aktif (en alttaki tamamlanmamış) katmandaki olası pozisyonları dene
                foreach (var offset in GetPossibleOffsets(rotatedCells, activeLayer))
                {
                    if (TryPlacePiece(pieces[i].index, rotatedCells, offset, rotation, activeLayer, out int materialIdx))
                    {
                        // Özyinelemeli arama
                        if (BacktrackingSolve(pieceIdx + 1))
                            return true;

                        // Geri al
                        UndoPlacement(rotatedCells, offset);
                    }
                }
            }

            pieces[i].used = false;
        }

        return false;
    }

    // GridManager.TryFindFirstIncompleteLayer (GridManager.cs) ile aynı mantık: en alttaki
    // dolu-olmayan katmanı döndürür. Bir katman "dolu ama tek renk değilse" de tamamlanmamış
    // sayılır — TryPlacePiece'in katman-içi renk kısıtı bunun pratikte oluşmasını zaten
    // engelliyor, ama savunmacı olarak burada da kontrol ediyoruz.
    private int GetLowestIncompleteLayer()
    {
        int minY = targetCells.Min(c => c.y);
        int maxY = targetCells.Max(c => c.y);

        for (int y = minY; y <= maxY; y++)
        {
            var cellsInLayer = targetCells.Where(c => c.y == y).ToList();
            if (cellsInLayer.Count == 0) continue;

            bool allFilled = cellsInLayer.All(c => currentOccupied.Contains(c));
            if (!allFilled) return y;

            var matsInLayer = cellsInLayer
                .Where(c => currentMatIndex.ContainsKey(c))
                .Select(c => currentMatIndex[c])
                .Distinct()
                .ToList();
            if (matsInLayer.Count > 1) return y;
        }

        return maxY + 1; // tüm katmanlar tamamlandı
    }

    private bool TryPlacePiece(int pieceIndex, List<Vector3Int> cells, Vector3Int offset, Quaternion rotation, int activeLayerY, out int materialIdx)
    {
        materialIdx = -1;
        List<Vector3Int> worldCells = new List<Vector3Int>();

        // 1. Geometrik validasyon
        foreach (var cell in cells)
        {
            Vector3Int worldCell = cell + offset;

            // Parçaların sadece tek bir katmanda olması zorunluluğu (katman-katman oynanış için)
            if (worldCell.y != offset.y + cells[0].y)
                return false;

            // Collapse-aware: gerçek oyunda sadece aktif (en alttaki tamamlanmamış) katmana
            // yerleştirme yapılabilir. GetPossibleOffsets zaten sadece bu katmandaki offset'leri
            // üretiyor — bu, o garantiyi doğrulayan savunmacı bir kontrol.
            if (worldCell.y != activeLayerY)
                return false;

            // Grid sınırları
            if (worldCell.x < 0 || worldCell.x >= gridSize.x ||
                worldCell.y < 0 || worldCell.y >= gridSize.y ||
                worldCell.z < 0 || worldCell.z >= gridSize.z)
                return false;

            // Zaten dolu
            if (currentOccupied.Contains(worldCell))
                return false;

            // Zaten buz var
            if (frozenCells.Contains(worldCell))
                return false;

            // Hedef hücre değil
            if (!targetCells.Contains(worldCell))
                return false;

            worldCells.Add(worldCell);
        }

        // 2. Renk çözülebilirliği kontrolü - optimize edilmiş
        var layersAffected = worldCells.Select(c => c.y).Distinct().ToList();
        materialIdx = pieceIndex % 8;

        foreach (int y in layersAffected)
        {
            var newCellsInLayer = worldCells.Where(c => c.y == y).ToList();
            var occupiedInLayer = currentOccupied.Where(c => c.y == y).ToList();
            var matsInLayer = occupiedInLayer
                .Where(c => currentMatIndex.ContainsKey(c))
                .Select(c => currentMatIndex[c])
                .Distinct()
                .ToList();

            // ERKEN BUDAMA: Katmanda zaten karışık renkler varsa red
            if (matsInLayer.Count > 1)
            {
                return false;
            }

            // Katmanda mevcut renk varsa, yeni parça aynı renkte olmalı
            if (matsInLayer.Count == 1)
            {
                materialIdx = matsInLayer[0];
            }

            // Katman boyutu kontrolü
            int totalInLayer = targetCells.Count(c => c.y == y);
            int currentOccupiedInLayer = occupiedInLayer.Count;
            int afterPlacement = currentOccupiedInLayer + newCellsInLayer.Count;

            // Katman dolacaksa, tüm hücreler aynı renkte olmalı
            if (afterPlacement == totalInLayer)
            {
                if (matsInLayer.Count > 1)
                {
                    return false;
                }
            }
            
            // ERKEN BUDAMA 2: Katman yarı doluyken bile renk çakışması kontrolü
            // Eğer bu parça farklı renkte ise ve katman zaten %30+ doluysa, riskli
            if (matsInLayer.Count == 1 && matsInLayer[0] != materialIdx)
            {
                // Bu parça mevcut renge uymuyorsa red et
                return false;
            }
            
            // ERKEN BUDAMA 3: Katman %50+ doluyken, kalan hücre sayısı ile 
            // yerleştirilebilecek parça sayısını kontrol et
            float fillRatio = (float)currentOccupiedInLayer / totalInLayer;
            if (fillRatio > 0.5f)
            {
                int remainingCells = totalInLayer - afterPlacement;
                // Eğer kalan hücre sayısı çok az ve karışık renk riski varsa budama yap
                if (remainingCells > 0 && remainingCells < 3 && matsInLayer.Count == 0)
                {
                    // İlk rengi belirlerken dikkatli ol - küçük boşluklarda sorun çıkabilir
                }
            }
        }

        // 3. Yerleştir
        var step = new PlacementStep
        {
            pieceIndex = pieceIndex,
            offset = offset,
            rotation = rotation,
            cells = new List<Vector3Int>(worldCells),
            materialIndex = materialIdx
        };

        currentSolution.Add(step);

        foreach (var worldCell in worldCells)
        {
            currentOccupied.Add(worldCell);
            currentMatIndex[worldCell] = materialIdx;
        }

        // Simulate ice thawing and block explosion in the active layer (which is offset.y + cells[0].y)
        ResolveFrozenCellsInSolver(step, offset.y + cells[0].y);

        return true;
    }

    private void UndoPlacement(List<Vector3Int> cells, Vector3Int offset)
    {
        if (currentSolution.Count == 0) return;

        var lastStep = currentSolution[currentSolution.Count - 1];
        currentSolution.RemoveAt(currentSolution.Count - 1);

        // 1. Restore thawed cells back to frozen
        foreach (var cell in lastStep.thawedCells)
        {
            frozenCells.Add(cell);
        }

        // 2. Restore exploded cells back to occupied
        foreach (var item in lastStep.explodedCells)
        {
            currentOccupied.Add(item.cell);
            currentMatIndex[item.cell] = item.materialIndex;
        }

        // 3. Remove placed piece's cells
        foreach (var cell in lastStep.cells)
        {
            currentOccupied.Remove(cell);
            currentMatIndex.Remove(cell);
        }
    }

    private void ResolveFrozenCellsInSolver(PlacementStep step, int activeLayerY)
    {
        if (frozenCells.Count == 0) return;

        var cellsInLayer = targetCells.Where(c => c.y == activeLayerY).ToList();
        var occupiedInLayer = cellsInLayer.Where(c => currentOccupied.Contains(c)).ToList();

        var groups = new List<List<Vector3Int>>();
        var visited = new HashSet<Vector3Int>();

        var horizontalNeighbors = new Vector3Int[]
        {
            Vector3Int.right,
            Vector3Int.left,
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        foreach (var cell in occupiedInLayer)
        {
            if (visited.Contains(cell)) continue;
            if (!currentMatIndex.TryGetValue(cell, out int matIdx)) continue;

            var group = new List<Vector3Int>();
            var queue = new Queue<Vector3Int>();

            queue.Enqueue(cell);
            visited.Add(cell);

            while (queue.Count > 0)
            {
                var curr = queue.Dequeue();
                group.Add(curr);

                foreach (var offset in horizontalNeighbors)
                {
                    Vector3Int neighbor = curr + offset;
                    if (neighbor.y == activeLayerY && 
                        occupiedInLayer.Contains(neighbor) && 
                        !visited.Contains(neighbor))
                    {
                        if (currentMatIndex.TryGetValue(neighbor, out int nMatIdx) && nMatIdx == matIdx)
                        {
                            visited.Add(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            if (group.Count >= 2)
            {
                groups.Add(group);
            }
        }

        var cellsToThaw = new HashSet<Vector3Int>();
        // GridManager.CheckAndResolveFrozenCells (gerçek oyun) ile birebir eşleşmesi gereken kısım:
        // buza komşu olan grup sadece buzu ERİTMEKLE kalmıyor, GRUBUN KENDİSİ DE PATLIYOR
        // (bkz. GridManager.AnimateExplodeAndThaw → occupiedCells.Remove). Önceden burada sadece
        // thaw simüle ediliyordu; bu, solver'ın "9 hamlede çözülür" dediği bir seviyenin gerçek
        // oyunda patlayan hücreleri yeniden dolduracak parça kalmadığı için OYNANAMAZ çıkmasına
        // yol açan gerçek bir hataydı (bkz. PlacementStep.explodedCells — alan zaten vardı ve
        // UndoPlacement onu geri yüklüyordu, ama hiçbir yerde doldurulmuyordu).
        var cellsToExplode = new HashSet<Vector3Int>();

        foreach (var group in groups)
        {
            bool touchesFrozen = false;
            foreach (var cell in group)
            {
                foreach (var offset in horizontalNeighbors)
                {
                    Vector3Int neighbor = cell + offset;
                    if (neighbor.y == activeLayerY && frozenCells.Contains(neighbor))
                    {
                        cellsToThaw.Add(neighbor);
                        touchesFrozen = true;
                    }
                }
            }

            if (touchesFrozen)
            {
                foreach (var cell in group) cellsToExplode.Add(cell);
            }
        }

        foreach (var cell in cellsToThaw)
        {
            frozenCells.Remove(cell);
            step.thawedCells.Add(cell);
        }

        foreach (var cell in cellsToExplode)
        {
            if (!currentOccupied.Contains(cell)) continue;

            int matIdx = currentMatIndex.TryGetValue(cell, out int mi) ? mi : -1;
            step.explodedCells.Add(new ExplodedCellInfo { cell = cell, materialIndex = matIdx });
            currentOccupied.Remove(cell);
            currentMatIndex.Remove(cell);
        }
    }

    private bool AllLayersValid()
    {
        int minY = targetCells.Min(c => c.y);
        int maxY = targetCells.Max(c => c.y);

        for (int y = minY; y <= maxY; y++)
        {
            if (!IsLayerCompleteAndValid(y))
            {
                bestResult.failureReason = $"Katman Y={y} geçersiz (farklı renkler veya eksik hücreler)";
                return false;
            }
        }

        return true;
    }

    private bool IsLayerCompleteAndValid(int y)
    {
        var cellsInLayer = targetCells.Where(c => c.y == y).ToList();
        if (cellsInLayer.Count == 0) return true;

        // Tüm hücreler dolu mu?
        foreach (var cell in cellsInLayer)
        {
            if (!currentOccupied.Contains(cell))
                return false;
        }

        // Hepsi aynı materyalde mi?
        var materials = cellsInLayer
            .Where(c => currentMatIndex.ContainsKey(c))
            .Select(c => currentMatIndex[c])
            .Distinct()
            .ToList();

        return materials.Count == 1 && materials[0] >= 0;
    }

    private List<Quaternion> GetAllRotations()
    {
        // Y ekseni rotasyonları (0, 90, 180, 270)
        return new List<Quaternion>
        {
            Quaternion.Euler(0, 0, 0),
            Quaternion.Euler(0, 90, 0),
            Quaternion.Euler(0, 180, 0),
            Quaternion.Euler(0, 270, 0),
        };
    }

    private List<Vector3Int> RotateCells(List<Vector3Int> cells, Quaternion rotation)
    {
        var rotated = new List<Vector3Int>();
        foreach (var cell in cells)
        {
            Vector3 v = rotation * new Vector3(cell.x, cell.y, cell.z);
            rotated.Add(new Vector3Int(
                Mathf.RoundToInt(v.x),
                Mathf.RoundToInt(v.y),
                Mathf.RoundToInt(v.z)
            ));
        }

        // Normalize et (min koordinat 0'a çek)
        int minX = rotated.Min(c => c.x);
        int minY = rotated.Min(c => c.y);
        int minZ = rotated.Min(c => c.z);

        return rotated.Select(c => new Vector3Int(c.x - minX, c.y - minY, c.z - minZ)).ToList();
    }

    // Collapse-aware: bir parçanın döndürülmüş hücreleri (RotateCells ile normalize edildiği
    // için) her zaman TEK bir ortak Y değerini paylaşır — bu yüzden geçerli tek offset.y değeri
    // "activeLayerY - cells[0].y" olur. Eskiden burada Y ekseni de taranıyordu (O(W·H·D)); artık
    // sadece aktif katmanın X/Z düzlemi taranıyor (O(W·D)) — hem gerçek oyunla birebir eşleşiyor
    // hem de arama uzayını katman sayısı kadar küçültüyor.
    private IEnumerable<Vector3Int> GetPossibleOffsets(List<Vector3Int> cells, int activeLayerY)
    {
        int offsetY = activeLayerY - cells[0].y;

        for (int x = 0; x < gridSize.x; x++)
        {
            for (int z = 0; z < gridSize.z; z++)
            {
                // Bu offset ile parça grid içinde kalıyor mu?
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
                if (fits)
                    yield return new Vector3Int(x, offsetY, z);
            }
        }
    }

    private float CalculateDifficulty(int moveCount)
    {
        int totalCells = targetCells.Count;
        int frozenCount = frozenCells.Count;
        float frozenRatio = totalCells > 0 ? (float)frozenCount / totalCells : 0f;
        int pieceCount = pieces.Count;
        int gridVolume = gridSize.x * gridSize.y * gridSize.z;

        // Normalize edilmiş değerler
        float normMoves = Mathf.Clamp01(moveCount / 20f);           // 20 hamle = 1.0
        float normFrozen = Mathf.Clamp01(frozenRatio / 0.5f);      // %50 buz = 1.0
        float normPieces = Mathf.Clamp01(pieceCount / 15f);        // 15 parça = 1.0
        float normVolume = Mathf.Clamp01(gridVolume / 200f);       // 200 hücre = 1.0

        float score = weightMoveCount * normMoves +
                     weightFrozenRatio * normFrozen +
                     weightPieceCount * normPieces +
                     weightGridVolume * normVolume;

        return Mathf.Clamp01(score);
    }

    private string GetDifficultyLabel(float score)
    {
        if (score < 0.33f) return "kolay";
        if (score < 0.66f) return "orta";
        return "zor";
    }

    private bool HasIrrecoverableColorConflict()
    {
        // Her katmanı kontrol et: karışık renkler varsa çözülemez
        int minY = targetCells.Any() ? targetCells.Min(c => c.y) : 0;
        int maxY = targetCells.Any() ? targetCells.Max(c => c.y) : 0;

        for (int y = minY; y <= maxY; y++)
        {
            var cellsInLayer = targetCells.Where(c => c.y == y).ToList();
            if (cellsInLayer.Count == 0) continue;

            var occupiedInLayer = cellsInLayer.Where(c => currentOccupied.Contains(c)).ToList();
            var matsInLayer = occupiedInLayer
                .Where(c => currentMatIndex.ContainsKey(c))
                .Select(c => currentMatIndex[c])
                .Distinct()
                .ToList();

            // Birden fazla farklı renk varsa çözülemez
            if (matsInLayer.Count > 1)
            {
                return true;
            }
        }

        return false;
    }
}

// ═══════════════════════════════════════════════════════════════════
//  DATA STRUCTURES
// ═══════════════════════════════════════════════════════════════════

[System.Serializable]
public class SolverResult
{
    public bool isSolvable;
    public int minMoveCount;
    // Collapse-aware arama (LevelSolver.BacktrackingSolve) sadece her an aktif olan (en alttaki
    // tamamlanmamış) katmana yerleştirme yaptığı için, bu liste artık gerçek oyunda oynanacak
    // sırayla birebir eşleşir: adımların cells[0].y değeri baştan sona hiç azalmaz (monoton artan).
    public List<PlacementStep> solutionSteps;
    public float difficultyScore;
    public string difficultyLabel;
    public string failureReason;
}

[System.Serializable]
public struct ExplodedCellInfo
{
    public Vector3Int cell;
    public int materialIndex;
}

[System.Serializable]
public class PlacementStep
{
    public int pieceIndex;
    public Vector3Int offset;
    public Quaternion rotation;
    public List<Vector3Int> cells;
    public int materialIndex;

    public List<Vector3Int> thawedCells = new List<Vector3Int>();
    public List<ExplodedCellInfo> explodedCells = new List<ExplodedCellInfo>();
}

public class PieceData
{
    public int index;
    public List<Vector3Int> cells;
    public bool used;
    public string shapeKey;
}
