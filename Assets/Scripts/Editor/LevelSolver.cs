using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL SOLVER  —  Seviye Çözülebilirlik ve Zorluk Analizi
//  BlockMerge3D  •  Backtracking ile saf geometrik çözülebilirlik (renk kısıtı yok — TEK istisna:
//  buz erimesi hâlâ kısmen renk-bağımlı, best-effort simüle edilir, bkz. currentMatIndex notu)
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
    private HashSet<Vector3Int> frozenCells;
    private List<PieceData> pieces;

    // ── Çözüm Durumu ──────────────────────────────────────────────
    private HashSet<Vector3Int> currentOccupied;
    // Ekip kararıyla buz erimesi kısmen renk-bağımlı kaldı (bkz. ResolveFrozenCellsInSolver notu):
    // buza değen hücreden başlayan bağlantılı aynı renk grubu ≥2 üyeliyse buz erir VE grubun
    // tamamı buzla birlikte yok olur. Gerçek oyunda parça rengi spawn anında RASTGELE atandığı
    // için bu, solver'ın KESİN garanti VEREMEYECEĞİ tek mekanik — burada pieceIndex tabanlı
    // basit bir vekil (proxy) renk kullanıyoruz, sadece best-effort bir simülasyon, gerçek
    // oyundaki rastgele atamayı temsil etme iddiası yok.
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

        // [2026-07-14, eski mantığa dönüş] Buza değen hücreden başlayan BAĞLANTILI aynı renk
        // grubu (≥2 üyeli) artık TAMAMEN buzla birlikte yok oluyor (bkz. ResolveFrozenCellsInSolver)
        // — grup büyüklüğü keyfi olabileceği için "buz başına en fazla N hücre" gibi sabit bir üst
        // sınır artık YOK. Buz içeren seviyelerde parça hacmi hedeften ne kadar fazla olursa olsun
        // erken reddedilmiyor, arama (zaman/durum limitleriyle sınırlı) karar versin. Buzsuz
        // seviyelerde hiçbir hücre kaybolmadığı için hacim hâlâ TAM eşit olmalı — bu kesin bir
        // kural, gevşetilmiyor.
        if (frozenCells.Count == 0 && totalPieceCells > emptyTargetCells)
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
        // Prefilled hücrelerin rengi tasarımcı tarafından BELİRLİ (rastgele değil) — bu yüzden
        // buz-erime kontrolü için başlangıçta biliniyor sayılır (parçaların proxy rengi gibi
        // "best-effort" değil, gerçek oyunla birebir tutarlı).
        currentMatIndex = new Dictionary<Vector3Int, int>();
        if (holder.prefilledCells != null && holder.prefilledMaterialIndices != null)
        {
            for (int i = 0; i < holder.prefilledCells.Count && i < holder.prefilledMaterialIndices.Count; i++)
            {
                currentMatIndex[holder.prefilledCells[i]] = holder.prefilledMaterialIndices[i];
            }
        }
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
            bestResult.timedOut = true;
            bestResult.failureReason = $"Arama limiti aşıldı ({maxSearchTimeMs}ms veya {maxStatesExplored} durum) - kesin çözülemez değil";
        }
        else
        {
            bestResult.failureReason = bestResult.failureReason ?? "Çözüm bulunamadı (geometri kısıtları)";
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
        // [2026-07-14] BU GÜVENLİK VARSAYIMI buz varken artık GEÇERSİZ: ResolveFrozenCellsInSolver
        // proxyColor = pieceIndex % 8 kullanıyor, yani aynı şekle sahip iki parça artık farklı
        // pieceIndex'e sahip oldukları için farklı proxy renklere sahip olabilir ve erime/yok-olma
        // sonucunu FARKLI etkileyebilir — artık "aynı geometri, aynı sonuç" garantisi yok. Bu
        // yüzden buz içeren seviyelerde dedup'ı devre dışı bırakıyoruz (daha pahalı ama doğru arama);
        // buzsuz seviyelerde performans için hâlâ aktif.
        var triedShapesAtThisLevel = new HashSet<string>();
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].used) continue;
            if (frozenCells.Count == 0 && pieces[i].shapeKey != null && !triedShapesAtThisLevel.Add(pieces[i].shapeKey)) continue;

            pieces[i].used = true;

            // Tüm rotasyonları dene (0, 90, 180, 270 derece Y ekseni, sonra X ekseni)
            foreach (var rotation in GetAllRotations())
            {
                var rotatedCells = RotateCells(pieces[i].cells, rotation);

                // Sadece aktif (en alttaki tamamlanmamış) katmandaki olası pozisyonları dene
                foreach (var offset in GetPossibleOffsets(rotatedCells, activeLayer))
                {
                    if (TryPlacePiece(pieces[i].index, rotatedCells, offset, rotation, activeLayer))
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
    // dolu-olmayan katmanı döndürür.
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
        }

        return maxY + 1; // tüm katmanlar tamamlandı
    }

    private bool TryPlacePiece(int pieceIndex, List<Vector3Int> cells, Vector3Int offset, Quaternion rotation, int activeLayerY)
    {
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

        // 2. Yerleştir (renksiz sistemde katman içi renk uzlaşması diye bir şey yok, sadece
        // geometri — bkz. dosya başındaki not).
        var step = new PlacementStep
        {
            pieceIndex = pieceIndex,
            offset = offset,
            rotation = rotation,
            cells = new List<Vector3Int>(worldCells)
        };

        currentSolution.Add(step);

        // pieceIndex tabanlı vekil (proxy) renk — sadece buz-erime simülasyonu için (bkz. alan
        // yorumu). Gerçek oyunda parçanın rengi spawn anında rastgele atanır, bu SADECE solver'ın
        // best-effort bir çözüm bulabilmesi için deterministik bir yer tutucu.
        int proxyColor = pieceIndex % 8;
        foreach (var worldCell in worldCells)
        {
            currentOccupied.Add(worldCell);
            currentMatIndex[worldCell] = proxyColor;
        }

        // Buza komşu hücreler varsa erimeyi dene (bkz. GridManager.CheckAndResolveFrozenCells ile
        // birebir eşleşmesi gereken kural: buz hücresinin ≥2 komşusu aynı renkteyse erir).
        ResolveFrozenCellsInSolver(step);

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

        // 2. Restore cells that were destroyed as a side effect of this step's thaw (bkz.
        // ResolveFrozenCellsInSolver) — bu, adımın kendi yerleştirdiği bir hücreyi de kapsıyorsa
        // (tetikleyici parça kendi yerleştirdiği hücreyse), aşağıdaki 3. adım onu tekrar
        // kaldırarak doğru "hiç yerleştirilmemiş" durumuna döndürür.
        foreach (var info in lastStep.destroyedCells)
        {
            currentOccupied.Add(info.cell);
            currentMatIndex[info.cell] = info.matIndex;
        }

        // 3. Remove placed piece's cells
        foreach (var cell in lastStep.cells)
        {
            currentOccupied.Remove(cell);
            currentMatIndex.Remove(cell);
        }
    }

    // GridManager.CheckAndResolveFrozenCells (gerçek oyun) ile birebir eşleşmesi gereken kural
    // [2026-07-14, eski mantığa dönüş]: buza değen hücreden başlayarak BAĞLANTILI aynı renkteki
    // TÜM grup (flood-fill) hesaplanır — grup ≥2 üyeliyse grubun TAMAMI buzla birlikte yok olur
    // (bkz. PlacementStep.destroyedCells). Grup büyüklüğü artık sabit-2 değil, keyfi olabilir.
    // Renk burada pieceIndex tabanlı bir vekil olduğu için bu KESİN bir garanti değil,
    // best-effort — bkz. currentMatIndex alanının üstündeki not.
    private void ResolveFrozenCellsInSolver(PlacementStep step)
    {
        if (frozenCells.Count == 0) return;

        var horizontalNeighbors = new Vector3Int[]
        {
            Vector3Int.right,
            Vector3Int.left,
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        var touchingCells = new HashSet<Vector3Int>();
        var candidateFrozenCells = new HashSet<Vector3Int>();
        foreach (var cell in step.cells)
        {
            foreach (var offset in horizontalNeighbors)
            {
                Vector3Int neighbor = cell + offset;
                if (frozenCells.Contains(neighbor))
                {
                    touchingCells.Add(cell);
                    candidateFrozenCells.Add(neighbor);
                }
            }
        }

        var cellsToThaw = new HashSet<Vector3Int>();
        var cellsToDestroy = new HashSet<Vector3Int>();

        foreach (var touchCell in touchingCells)
        {
            if (!currentMatIndex.TryGetValue(touchCell, out int touchMatIdx)) continue;

            var group = FloodFillSameColorInSolver(touchCell, touchMatIdx, horizontalNeighbors);
            if (group.Count < 2) continue;

            cellsToDestroy.UnionWith(group);
            foreach (var offset in horizontalNeighbors)
            {
                Vector3Int neighbor = touchCell + offset;
                if (frozenCells.Contains(neighbor)) cellsToThaw.Add(neighbor);
            }
        }

        foreach (var cell in cellsToThaw)
        {
            frozenCells.Remove(cell);
            step.thawedCells.Add(cell);
        }

        foreach (var cell in cellsToDestroy)
        {
            if (currentMatIndex.TryGetValue(cell, out int prevMatIdx))
            {
                step.destroyedCells.Add(new DestroyedCellInfo { cell = cell, matIndex = prevMatIdx });
            }
            currentOccupied.Remove(cell);
            currentMatIndex.Remove(cell);
        }
    }

    // GridManager.FloodFillSameColor ile birebir eşleşmesi gereken mantık: start hücresinden
    // başlayarak, currentOccupied içinde aynı proxy/prefilled matIndex'e sahip ve yatay
    // komşuluk üzerinden birbirine bağlı tüm hücreleri (start dahil) döndürür.
    private HashSet<Vector3Int> FloodFillSameColorInSolver(Vector3Int start, int matIdx, Vector3Int[] horizontalNeighbors)
    {
        var visited = new HashSet<Vector3Int> { start };
        var stack = new Stack<Vector3Int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            foreach (var offset in horizontalNeighbors)
            {
                Vector3Int neighbor = cur + offset;
                if (visited.Contains(neighbor)) continue;
                if (!currentMatIndex.TryGetValue(neighbor, out int neighborMatIdx)) continue;
                if (neighborMatIdx != matIdx) continue;

                visited.Add(neighbor);
                stack.Push(neighbor);
            }
        }

        return visited;
    }

    private bool AllLayersValid()
    {
        int minY = targetCells.Min(c => c.y);
        int maxY = targetCells.Max(c => c.y);

        for (int y = minY; y <= maxY; y++)
        {
            if (!IsLayerCompleteAndValid(y))
            {
                bestResult.failureReason = $"Katman Y={y} geçersiz (eksik hücreler)";
                return false;
            }
        }

        return true;
    }

    private bool IsLayerCompleteAndValid(int y)
    {
        var cellsInLayer = targetCells.Where(c => c.y == y).ToList();
        if (cellsInLayer.Count == 0) return true;

        foreach (var cell in cellsInLayer)
        {
            if (!currentOccupied.Contains(cell))
                return false;
        }

        return true;
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
    // true ⇔ isSolvable=false yalnızca arama limiti (süre/durum sayısı) aşıldığı için — yani
    // "çözülemez" KANITLANMADI, sadece bu bütçede bulunamadı. UI bunu gerçek/kanıtlanmış
    // çözülemezlikten ayırt etmek için kullanır (bkz. AILevelDesignerWindow.DrawSolverResultSection).
    public bool timedOut;
}

[System.Serializable]
public class PlacementStep
{
    public int pieceIndex;
    public Vector3Int offset;
    public Quaternion rotation;
    public List<Vector3Int> cells;

    public List<Vector3Int> thawedCells = new List<Vector3Int>();

    // [2026-07-14] Buz erimesini tetikleyen komşu hücreler de anında yok olur (bkz.
    // ResolveFrozenCellsInSolver) — undo sırasında geri getirilebilmeleri için eski
    // matIndex değerleriyle birlikte saklanır.
    public List<DestroyedCellInfo> destroyedCells = new List<DestroyedCellInfo>();
}

[System.Serializable]
public struct DestroyedCellInfo
{
    public Vector3Int cell;
    public int matIndex;
}

public class PieceData
{
    public int index;
    public List<Vector3Int> cells;
    public bool used;
    public string shapeKey;
}
