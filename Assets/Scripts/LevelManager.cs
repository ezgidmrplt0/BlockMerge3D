using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

[DefaultExecutionOrder(-10)] // GameManager'dan önce Awake çalışır, Instance hazır olur
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("Level Configuration")]
    public LevelData currentLevel;

    [Header("Scene Locations")]
    public Transform mainCubeLocation;

    [Header("UI Kart Slotları")]
    public List<PieceCardUI> pieceCards = new List<PieceCardUI>();

    [Header("Visual Settings")]
    public int   maxVisiblePieces = 2;
    [Range(0f, 1f)] public float smartSpawnProbability = 0.35f;
    
    private List<bool> activeIsSmart = new List<bool>();

    private GameObject activeMainPiece;
    public GameObject ActiveMainPiece => activeMainPiece;
    private List<GameObject> activePieces = new List<GameObject>();
    private List<GameObject> placedPieces = new List<GameObject>();
    private GridManager gridManager;
    private Material ghostTargetMat;

    // Hangi parçanın hangi kartta olduğunu takip eder
    private Dictionary<GameObject, PieceCardUI> pieceToCard = new Dictionary<GameObject, PieceCardUI>();

    private List<GameObject> allPiecePrefabs = new List<GameObject>();
    private List<float>      allPieceWidths  = new List<float>();
    private List<float>      allPieceHeights = new List<float>();
    private List<float>      allPieceDepths  = new List<float>();
    private List<int>        activePieceDataIndices = new List<int>();
    private List<int>        spawnedPieceIndices = new List<int>();

    [Header("Next Piece Preview Settings")]
    private int nextPieceIndex = -1;
    // "Sıradaki parça" önizlemesinde gösterilen renk, o parça gerçekten spawn edilirken AYNI
    // renk kullanılsın diye önizleme oluşturulduğu anda BİR KEZ seçilip burada saklanır (kozmetik
    // amaçlı — renk artık hiçbir oynanış kararını etkilemiyor, bkz. PickCosmeticPieceColor).
    private Material nextPieceMaterial;

    private GameObject nextPiecePreviewParent;
    private Camera nextPiecePreviewCam;
    private GameObject nextPiecePreview3D;
    private RawImage nextPiecePreviewRawImage;
    private float nextPieceVisualRadius = 1f;
    private Vector3 nextPieceVisualCenter = Vector3.zero;
    private float previewRotationAngle = 0f;

    private void Awake()
    {
        Instance    = this;
        gridManager = GetComponent<GridManager>();
        if (gridManager == null) gridManager = gameObject.AddComponent<GridManager>();
    }

    private void Start()
    {
        // Material assets will be assigned via inspector by the AestheticSetupTool
    }

    public void LoadLevel(LevelData level)
    {
        ClearCurrentLevel();

        // Önceki seviyeden kalan swipe döndürmesini yeni ana şekil pivot'a
        // parent'lanmadan ÖNCE nötrle (aksi halde SetParent eski açıyı telafi
        // eden bir local rotasyon kilitler ve şekil kalıcı olarak yanlış görünür).
        CameraOrbit.Instance?.ResetBoardRotation();

        if (level.mainShapePrefab != null)
        {
            var prefabHolder = level.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
            Vector3 bc = Vector3.zero;
            float step = 1f;

            if (prefabHolder != null)
            {
                step = prefabHolder.cellSize + prefabHolder.spacing;
                bc = BoundsCenter(prefabHolder.occupiedCells, step);
            }

            activeMainPiece = Instantiate(level.mainShapePrefab, mainCubeLocation);
            activeMainPiece.name = "Main_Shape";
            activeMainPiece.transform.localPosition = -bc;

            if (CameraOrbit.Instance != null && CameraOrbit.Instance.pivot != null)
            {
                activeMainPiece.transform.SetParent(CameraOrbit.Instance.pivot, true);
                CameraOrbit.Instance.cube = activeMainPiece.transform;
            }

            DisableShadows(activeMainPiece);

            var holder = activeMainPiece.GetComponent<CubeShapeDataHolder>();
            if (holder != null)
            {
                // Dunya pozisyonu ile baslatmaya geri döndük
                gridManager.Initialize(activeMainPiece, holder.cellSize, holder.spacing, activeMainPiece.transform.position);
                ApplyTargetGhost(activeMainPiece);
            }
        }

        allPiecePrefabs.Clear();
        if (level.complementaryPieces != null && level.complementaryPieces.Count > 0)
        {
            foreach (var p in level.complementaryPieces)
            {
                if (p != null)
                {
                    allPiecePrefabs.Add(p);
                    var holder2 = p.GetComponent<CubeShapeDataHolder>();
                    if (holder2 != null && holder2.occupiedCells.Count > 0)
                    {
                        float st = holder2.cellSize + holder2.spacing;
                        float minX = holder2.occupiedCells.Min(c => c.x) * st;
                        float maxX = holder2.occupiedCells.Max(c => c.x) * st + holder2.cellSize;
                        float minY = holder2.occupiedCells.Min(c => c.y) * st;
                        float maxY = holder2.occupiedCells.Max(c => c.y) * st + holder2.cellSize;
                        float minZ = holder2.occupiedCells.Min(c => c.z) * st;
                        float maxZ = holder2.occupiedCells.Max(c => c.z) * st + holder2.cellSize;
                        allPieceWidths.Add(maxX - minX);
                        allPieceHeights.Add(maxY - minY);
                        allPieceDepths.Add(maxZ - minZ);
                    }
                    else
                    {
                        allPieceWidths.Add(2f);
                        allPieceHeights.Add(2f);
                        allPieceDepths.Add(2f);
                    }
                }
            }
        }

        // Kart UI'larını başlat (idempotent)
        for (int i = 0; i < pieceCards.Count; i++)
            pieceCards[i]?.Init(i);

        InitNextPiecePreviewSystem();

        for (int i = 0; i < maxVisiblePieces; i++)
            SpawnRandomPiece();
        FitCameraToScene();

        var lpc = FindObjectOfType<LayerPanelController>();
        if (lpc != null) lpc.ResetPanel();
    }

    [Header("Color Palette Settings (Material-Based)")]
    [Tooltip("Sürüklenen oyun parçaları için kullanılacak Material (Malzeme) paleti")]
    public Material[] pieceMaterials;
    public Material[] PieceMaterials => pieceMaterials;

    [Tooltip("Yarı saydam hedef kılavuz küpü için kullanılacak Material")]
    public Material ghostTargetMaterial;

    private void SpawnRandomPiece()
    {
        if (allPiecePrefabs.Count == 0) return;

        // Boş bir kart bul
        PieceCardUI targetCard = (pieceCards != null && pieceCards.Count > 0)
            ? pieceCards.FirstOrDefault(c => c != null && !c.HasPiece)
            : null;

        // Kart sistemi varsa ama boş kart yoksa çık
        if (pieceCards != null && pieceCards.Count > 0 && targetCard == null) return;

        if (nextPieceIndex < 0)
        {
            PrepareNextPieceIndex();
        }

        int indexToSpawn = nextPieceIndex;

        // Bu indeksi spawn edilmiş olarak işaretle
        spawnedPieceIndices.Add(indexToSpawn);
        activeIsSmart.Add(false); // Kolaylaştırma yardımı kapalı

        // Bu parçanın (kozmetik) rengi, önizlemesi gösterildiği anda (PrepareNextPieceIndex
        // içinde) zaten kararlaştırılıp nextPieceMaterial'e kaydedilmişti — burada YENİDEN
        // hesaplanmaz, aksi halde önizlemede gösterilen renkle spawn edilen renk tutmayabilir.
        Material matchingMaterial = nextPieceMaterial;

        // Parçayı spawn et
        SpawnPieceAtIndex(indexToSpawn, GetRandom90DegreeRotation(), matchingMaterial, targetCard);

        // Get the new next piece index
        PrepareNextPieceIndex();
    }

    private Quaternion GetRandom90DegreeRotation()
    {
        float ry = Random.Range(0, 4) * 90f;
        return Quaternion.Euler(0, ry, 0);
    }

    private int FindSmallestPieceIndex()
    {
        int bestIdx = 0;
        int minCells = int.MaxValue;
        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            var h = allPiecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (h != null && h.occupiedCells.Count < minCells)
            {
                minCells = h.occupiedCells.Count;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    // Renksiz sisteme geçişle birlikte parça rengi artık tamamen kozmetik — katmanın "baskın
    // rengiyle uyuşma" zorunluluğu yok, bu yüzden paletten düz rastgele seçiyoruz.
    private Material PickCosmeticPieceColor()
    {
        if (pieceMaterials == null || pieceMaterials.Length == 0) return null;
        var valid = pieceMaterials.Where(m => m != null).ToList();
        if (valid.Count == 0) return null;
        return valid[Random.Range(0, valid.Count)];
    }

    private int FindBestPieceIndex(out Quaternion rotation, out Color? recommendedColor, out bool foundMerge)
    {
        rotation = Quaternion.identity;
        recommendedColor = null;
        foundMerge = false;

        if (allPiecePrefabs.Count == 0) return -1;

        Quaternion[] possibleRotations = new Quaternion[]
        {
            Quaternion.identity,
            Quaternion.Euler(0, 90, 0),
            Quaternion.Euler(0, 180, 0),
            Quaternion.Euler(0, 270, 0)
        };

        List<int> placeableIndices = new List<int>();
        var mergeOpportunities = new List<(int index, Quaternion rot, Color col)>();

        // Tum prefablari ve rotasyonlari tara
        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            var h = allPiecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (h == null) continue;

            foreach (var rot in possibleRotations)
            {
                var rotatedCells = GridManager.RotateCells(h.occupiedCells, rot);
                var offsets = gridManager.GetPossibleOffsets(rotatedCells);
                
                if (offsets.Count > 0)
                {
                    if (!placeableIndices.Contains(i)) placeableIndices.Add(i);

                    foreach (var off in offsets)
                    {
                        var mCol = gridManager.GetMergeColor(rotatedCells, off);
                        if (mCol.HasValue)
                        {
                            mergeOpportunities.Add((i, rot, mCol.Value));
                        }
                    }
                }
            }
        }

        // 1. Merge firsati varsa onu kullan
        if (mergeOpportunities.Count > 0)
        {
            var choice = mergeOpportunities[Random.Range(0, mergeOpportunities.Count)];
            rotation = choice.rot;
            recommendedColor = choice.col;
            foundMerge = true;
            return choice.index;
        }

        // 2. Merge bulunamadiysa, "En cok komsu renk eslesmesi" saglayani bul (Progress score)
        int bestMatchScore = -1;
        var bestOptions = new List<(int index, Quaternion rot, Color col)>();

        var paletteColors = new List<Color>();
        if (pieceMaterials != null)
        {
            foreach (var m in pieceMaterials)
            {
                if (m != null) paletteColors.Add(GridManager.GetMaterialColor(m));
            }
        }

        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            var h = allPiecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (h == null) continue;
            foreach (var rot in possibleRotations)
            {
                var rotatedCells = GridManager.RotateCells(h.occupiedCells, rot);
                var offsets = gridManager.GetPossibleOffsets(rotatedCells);
                if (offsets.Count == 0) continue;

                foreach (var paletteCol in paletteColors)
                {
                    foreach (var off in offsets)
                    {
                        int score = CalculateMatchScore(rotatedCells, off, paletteCol);
                        if (score > bestMatchScore)
                        {
                            bestMatchScore = score;
                            bestOptions.Clear();
                            bestOptions.Add((i, rot, paletteCol));
                        }
                        else if (score == bestMatchScore)
                        {
                            bestOptions.Add((i, rot, paletteCol));
                        }
                    }
                }
            }
        }

        if (bestOptions.Count > 0 && bestMatchScore > 0)
        {
            var choice = bestOptions[Random.Range(0, bestOptions.Count)];
            rotation = choice.rot;
            recommendedColor = choice.col;
            foundMerge = true;
            return choice.index;
        }

        if (placeableIndices.Count > 0)
        {
            return placeableIndices[Random.Range(0, placeableIndices.Count)];
        }

        return Random.Range(0, allPiecePrefabs.Count);
    }

    private int CalculateMatchScore(List<Vector3Int> cells, Vector3Int offset, Color color)
    {
        int score = 0;
        foreach (var c in cells)
        {
            Vector3Int pos = c + offset;
            Vector3Int[] neighbors = { Vector3Int.left, Vector3Int.right, Vector3Int.up, Vector3Int.down, new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };
            foreach (var n in neighbors)
            {
                if (gridManager.GetCellColor(pos + n, out Color neighborCol))
                {
                    if (GridManager.ColorsApproxEqual(color, neighborCol)) score++;
                }
            }
        }
        return score;
    }

    private void SpawnPieceAtIndex(int index, Quaternion? initialRot = null, Material forcedMaterial = null, PieceCardUI targetCard = null)
    {
        if (index < 0 || index >= allPiecePrefabs.Count) return;

        GameObject piece = Instantiate(allPiecePrefabs[index], new Vector3(0, -100, 0), initialRot ?? Quaternion.identity);
        piece.name = $"Piece_{index + 1}";
        DisableShadows(piece);
        
        // Bu parca icin bir Material sec (zorunlu material yoksa rastgele)
        Material mat = forcedMaterial;
        if (mat == null && pieceMaterials != null && pieceMaterials.Length > 0)
        {
            var valid = pieceMaterials.Where(m => m != null).ToList();
            if (valid.Count > 0) mat = valid[Random.Range(0, valid.Count)];
        }
        ApplyMaterialToPiece(piece, mat);

        var drag = piece.AddComponent<DraggablePiece>();
        drag.InitialRotation = initialRot ?? Quaternion.identity;
        activePieces.Add(piece);
        activePieceDataIndices.Add(index);

        // Kart sistemine bağla
        if (targetCard != null)
        {
            drag.onDragCancelled = () => targetCard.ReturnToPreview();
            targetCard.AssignPiece(piece);
            pieceToCard[piece] = targetCard;
        }
    }

    private static void ApplyMaterialToPiece(GameObject piece, Material material)
    {
        if (piece == null || material == null) return;
        foreach (var r in piece.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
        }
    }

    private void RecomputeHomePositions() { } // Kart sistemiyle artık kullanılmıyor

    public void OnPiecePlaced(DraggablePiece piece)
    {
        int idx = activePieces.IndexOf(piece.gameObject);
        if (idx < 0) return;

        // İlgili kartı boşalt
        if (pieceToCard.TryGetValue(piece.gameObject, out var card))
        {
            card.ClearPiece();
            pieceToCard.Remove(piece.gameObject);
        }

        piece.transform.SetParent(null);

        placedPieces.Add(activePieces[idx]);
        activePieces.RemoveAt(idx);
        activePieceDataIndices.RemoveAt(idx);
        activeIsSmart.RemoveAt(idx);

        // Calculate placed coordinates for the newly placed piece
        List<Vector3Int> newlyPlacedCells = new List<Vector3Int>();
        Vector3Int boardOffset = gridManager.RootToOffset(piece.transform.position);
        foreach (var cell in piece.CurrentCells)
        {
            newlyPlacedCells.Add(cell + boardOffset);
        }

        // Check if there are frozen cells to thaw
        gridManager.CheckAndResolveFrozenCells(newlyPlacedCells, onComplete: (iceResolved) =>
        {
            var lpc = FindObjectOfType<LayerPanelController>();

            if (gridManager.IsLayerComplete())
            {
                if (lpc != null)
                {
                    lpc.ClosePanel(() => 
                    {
                        gridManager.ExplodeActiveLayer(
                            onLayerComplete: () => {
                                lpc.BuildLayerButtons();
                                HandlePostPiecePlaced();
                            },
                            onLevelComplete: () => {
                                GameManager.Instance?.CheckWin();
                            }
                        );
                    });
                }
                else
                {
                    gridManager.ExplodeActiveLayer(
                        onLayerComplete: () => {
                            HandlePostPiecePlaced();
                        },
                        onLevelComplete: () => {
                            GameManager.Instance?.CheckWin();
                        }
                    );
                }
            }
            else if (iceResolved)
            {
                // Buz kırıldı ama katman henüz tamamlanmadı
                // Panel modda kalıyoruz, sadece piece spawn'ı yapıyoruz
                HandlePostPiecePlaced();
            }
            else
            {
                HandlePostPiecePlaced();
            }
        });
    }

    private void HandlePostPiecePlaced()
    {
        // Tüketilen parçanın (boşalan kartın) yerine hemen yenisini getir
        SpawnRandomPiece();

        CheckGameOver();
    }

    private void CheckGameOver()
    {
        if (gridManager == null) return;

        // Level tamamlanmışsa kayıp değil, kazanma/katman geçişi akışı çalışmalıdır.
        if (gridManager.IsComplete() || gridManager.IsLayerComplete())
        {
            return;
        }

        // Kartlar henüz oluşturulmadıysa veya geçici olarak boşsa Retry açma.
        if (activePieces == null || activePieces.Count == 0)
        {
            return;
        }

        foreach (var pieceGO in activePieces)
        {
            if (pieceGO == null) continue;

            if (IsShapePlaceable(pieceGO))
            {
                return;
            }
        }

        // Buraya yalnızca eldeki parçaların hiçbiri, izin verilen dönüşlerin
        // hiçbirinde aktif katmandaki boş hücrelere yerleşemiyorsa gelir.
        GameManager.Instance?.GameOver();
    }

    private bool CanPlacementThawIce(List<Vector3Int> rotatedCells, Vector3Int offset)
    {
        if (gridManager == null || gridManager.frozenCells.Count == 0) return false;

        Vector3Int[] neighbors = {
            Vector3Int.left, Vector3Int.right,
            Vector3Int.up, Vector3Int.down,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        List<Vector3Int> placedPositions = new List<Vector3Int>();
        foreach (var c in rotatedCells)
        {
            placedPositions.Add(c + offset);
        }

        foreach (var frozenCell in gridManager.frozenCells)
        {
            if (frozenCell.y != gridManager.ActiveLayerY) continue;
            List<Vector3Int> inLayer = new List<Vector3Int>();
            foreach (var cell in placedPositions)
            {
                if (cell.y == frozenCell.y)
                {
                    inLayer.Add(cell);
                }
            }

            if (inLayer.Count < 2) continue;

            bool isAdjacent = false;
            foreach (var cell in inLayer)
            {
                foreach (var nOff in neighbors)
                {
                    if (cell + nOff == frozenCell)
                    {
                        isAdjacent = true;
                        break;
                    }
                }
                if (isAdjacent) break;
            }

            if (isAdjacent)
            {
                return true;
            }
        }

        return false;
    }

    public void ClearCurrentLevel()
    {
        gridManager?.ClearAllCellObjects();
        if (activeMainPiece != null)
        {
            Destroy(activeMainPiece);
            activeMainPiece = null;
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.cube = null;
        }
        foreach (var p in activePieces) if (p != null) Destroy(p);
        activePieces.Clear();
        activePieceDataIndices.Clear();
        foreach (var p in placedPieces) if (p != null) Destroy(p);
        placedPieces.Clear();
        if (ghostTargetMat != null) { Destroy(ghostTargetMat); ghostTargetMat = null; }
        allPiecePrefabs.Clear();
        allPieceWidths.Clear();
        allPieceHeights.Clear();
        allPieceDepths.Clear();

        // Kartları sıfırla
        foreach (var c in pieceCards) c?.ClearPiece();
        pieceToCard.Clear();
        activeIsSmart.Clear();
        spawnedPieceIndices.Clear();

        // NEXT PIECE PREVIEW RESET
        nextPieceIndex = -1;
        nextPieceMaterial = null;
        ClearNextPiecePreview();
    }

    private void FitCameraToScene()
    {
        if (CameraOrbit.Instance == null) return;

        bool first = true;
        Bounds total = new Bounds();

        void Include(GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { total = r.bounds; first = false; }
                else total.Encapsulate(r.bounds);
            }
        }

        // Sadece ana tahta dahil edilir.
        // Aktif parçalar artık x=-10000 civarında olduğundan dahil edilmez.
        Include(activeMainPiece);

        if (!first) CameraOrbit.Instance.FitInView(total);
    }

    private static Vector3 BoundsCenter(List<Vector3Int> cells, float step)
    {
        if (cells.Count == 0) return Vector3.zero;
        int minX = cells.Min(c => c.x), maxX = cells.Max(c => c.x);
        int minY = cells.Min(c => c.y), maxY = cells.Max(c => c.y);
        int minZ = cells.Min(c => c.z), maxZ = cells.Max(c => c.z);
        return new Vector3(
            (minX + maxX + 1) * 0.5f,
            (minY + maxY + 1) * 0.5f,
            (minZ + maxZ + 1) * 0.5f) * step;
    }

    private void DisableShadows(GameObject go)
    {
        if (go == null) return;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    private void ApplyTargetGhost(GameObject shape)
    {
        if (ghostTargetMaterial == null) return;
        
        foreach (var r in shape.GetComponentsInChildren<Renderer>())
        {
            string cubeName = r.gameObject.name;
            
            if (cubeName.StartsWith("Prefilled_"))
            {
                // "Prefilled_matIdx_x_y_z" formatından koordinatları oku
                var parts = cubeName.Split('_');
                
                // GridManager'a kaydet
                var holder = shape.GetComponent<CubeShapeDataHolder>();
                float step = holder != null ? holder.cellSize + holder.spacing : 1f;
                Vector3 lp = shape.transform.InverseTransformPoint(r.transform.position);
                var cell = new Vector3Int(
                    Mathf.RoundToInt(lp.x / step),
                    Mathf.RoundToInt(lp.y / step),
                    Mathf.RoundToInt(lp.z / step));
                
                // prefilledColors'dan doğru rengi al
                Material prefilledMat = null;
                int pfIndex = holder?.prefilledCells != null ? holder.prefilledCells.IndexOf(cell) : -1;
                
                if (pfIndex >= 0 && holder.prefilledColors != null && pfIndex < holder.prefilledColors.Count)
                {
                    Color targetColor = holder.prefilledColors[pfIndex];
                    // pieceMaterials'dan en yakın rengi bul
                    prefilledMat = FindClosestMaterial(targetColor);
                }
                
                if (prefilledMat == null)
                    prefilledMat = pieceMaterials != null && pieceMaterials.Length > 0 ? pieceMaterials[0] : null;
                
                if (prefilledMat != null)
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = prefilledMat;
                    r.sharedMaterials = mats;
                }
                
                GridManager.Instance.occupiedCells.Add(cell);
                if (prefilledMat != null)
                {
                    GridManager.Instance.SetCellColor(cell, GridManager.GetMaterialColor(prefilledMat));
                    // matIdx'i de güncelle
                    int matIdx = FindMaterialIndex(prefilledMat);
                    GridManager.Instance.SetCellMatIndex(cell, matIdx);
                }
            }
            else if (cubeName.StartsWith("Cube_"))
            {
                // Koordinat hesapla
                var holderF = shape.GetComponent<CubeShapeDataHolder>();
                float stepF = holderF != null ? holderF.cellSize + holderF.spacing : 1f;
                Vector3 lpF = shape.transform.InverseTransformPoint(r.transform.position);
                var cellF = new Vector3Int(
                    Mathf.RoundToInt(lpF.x / stepF),
                    Mathf.RoundToInt(lpF.y / stepF),
                    Mathf.RoundToInt(lpF.z / stepF));

                var prefilledList = holderF?.prefilledCells;
                int pfListIdx = prefilledList != null ? prefilledList.IndexOf(cellF) : -1;

                if (pfListIdx >= 0) // Eski format: Cube_ isimli ama prefilledCells'te var
                {
                    // prefilledColors'dan doğru rengi al
                    Material prefilledMat = null;
                    
                    if (holderF?.prefilledColors != null && pfListIdx < holderF.prefilledColors.Count)
                    {
                        Color targetColor = holderF.prefilledColors[pfListIdx];
                        // pieceMaterials'dan en yakın rengi bul
                        prefilledMat = FindClosestMaterial(targetColor);
                    }
                    
                    if (prefilledMat == null)
                        prefilledMat = pieceMaterials?.Length > 0 ? pieceMaterials[0] : null;

                    if (prefilledMat != null)
                    {
                        var mats2 = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats2.Length; i++) mats2[i] = prefilledMat;
                        r.sharedMaterials = mats2;
                    }

                    GridManager.Instance.occupiedCells.Add(cellF);
                    if (prefilledMat != null)
                    {
                        GridManager.Instance.SetCellColor(cellF, GridManager.GetMaterialColor(prefilledMat));
                        int matIdx = FindMaterialIndex(prefilledMat);
                        GridManager.Instance.SetCellMatIndex(cellF, matIdx);
                    }
                }
                else // Normal hedef (Ghost)
                {
                    if (holderF != null && holderF.frozenCells != null && holderF.frozenCells.Contains(cellF))
                    {
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        r.receiveShadows = false;
                    }
                    else
                    {
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        r.receiveShadows = false;
                        var mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = ghostTargetMaterial;
                        r.sharedMaterials = mats;
                    }
                }
            }
        }

        foreach (var col in shape.GetComponentsInChildren<Collider>())
            col.enabled = true;
    }

    // DÜZELTİLDİ: eskiden temizlenmemiş TÜM katmanları tarıyordu ("şu an aktif olmayan ama
    // ileride sığacağı bir katman var" → "yerleştirilebilir" sayılıyordu). Ama GridManager.CanPlace
    // SADECE ActiveLayerY'ye yerleştirmeye izin veriyor — yani oyuncu, geleceğe ait bir katmanda
    // "ilerleyemiyor", katmanlar kesin sırayla (alttan üste) işleniyor. Eski davranış şu sessiz
    // kilitlenmeye yol açıyordu: elindeki TÜM kartlar henüz aktif olmayan bir katmana ait olabilir
    // (SpawnRandomPiece kart seçerken katman gözetmiyordu) — CheckGameOver bunu "hâlâ oynanabilir"
    // sanıp asla "Kaybettin" göstermiyordu, oyun sonsuza dek donuyordu. Artık sadece ActiveLayerY'ye
    // gerçekten sığıp sığmadığına bakıyor — CanPlace ile birebir tutarlı.
    private bool IsShapePlaceable(GameObject shapePrefabOrGO)
    {
        return CanShapeFitActiveLayer(shapePrefabOrGO);
    }

    // PrepareNextPieceIndex (aktif katmana uyan parçaları önceliklendirmek için) ve IsShapePlaceable
    // (gerçek deadlock kontrolü için) tarafından paylaşılan tek doğru kaynak.
    private bool CanShapeFitActiveLayer(GameObject shapePrefabOrGO)
    {
        if (shapePrefabOrGO == null || gridManager == null) return false;
        var h = shapePrefabOrGO.GetComponent<CubeShapeDataHolder>();
        if (h == null) return false;

        int activeLayer = gridManager.ActiveLayerY;

        Quaternion[] rots = {
            Quaternion.identity,
            Quaternion.Euler(0, 90, 0),
            Quaternion.Euler(0, 180, 0),
            Quaternion.Euler(0, 270, 0),
            Quaternion.Euler(90, 0, 0),
            Quaternion.Euler(270, 0, 0)
        };

        foreach (var r in rots)
        {
            var rotated = GridManager.RotateCells(h.occupiedCells, r);
            var offsets = gridManager.GetPossibleOffsetsOnLayer(rotated, activeLayer);
            if (offsets.Count > 0) return true;
        }

        return false;
    }

    private Material FindClosestMaterial(Color targetColor)
    {
        if (pieceMaterials == null || pieceMaterials.Length == 0) return null;
        
        Material closest = null;
        float minDistance = float.MaxValue;
        
        foreach (var mat in pieceMaterials)
        {
            if (mat == null) continue;
            Color matColor = GridManager.GetMaterialColor(mat);
            float distance = ColorDistance(targetColor, matColor);
            
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = mat;
            }
        }
        
        return closest;
    }
    
    private int FindMaterialIndex(Material mat)
    {
        if (pieceMaterials == null || mat == null) return -1;
        
        for (int i = 0; i < pieceMaterials.Length; i++)
        {
            if (pieceMaterials[i] == mat) return i;
        }
        
        return -1;
    }
    
    private float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return dr * dr + dg * dg + db * db;
    }

    private void LateUpdate()
    {
        if (nextPiecePreview3D != null && nextPiecePreviewCam != null)
        {
            // Rotate slowly over time
            previewRotationAngle += Time.deltaTime * 30f;
            UpdateNextPiecePreviewTransform();
        }
    }

    private void OnDestroy()
    {
        ClearNextPiecePreview();
        if (nextPiecePreviewCam != null && nextPiecePreviewCam.targetTexture != null)
        {
            var rt = nextPiecePreviewCam.targetTexture;
            nextPiecePreviewCam.targetTexture = null;
            rt.Release();
            Destroy(rt);
        }
        if (nextPiecePreviewParent != null)
        {
            Destroy(nextPiecePreviewParent);
        }
    }

    private void ClearNextPiecePreview()
    {
        if (nextPiecePreview3D != null)
        {
            Destroy(nextPiecePreview3D);
            nextPiecePreview3D = null;
        }
    }

    private void InitNextPiecePreviewSystem()
    {
        // 1. Find UICanvas
        var canvas = GameObject.Find("UICanvas");
        if (canvas == null) return;

        // Remove old next piece preview if exists (for safety)
        var oldPanel = canvas.transform.Find("NextPiecePreviewPanel");
        if (oldPanel != null) Destroy(oldPanel.gameObject);

        // Get sprites directly from existing cards to guarantee identical design
        Sprite cardSprite = null;
        Sprite insetSprite = null;
        if (pieceCards != null && pieceCards.Count > 0 && pieceCards[0] != null)
        {
            var cardImgComp = pieceCards[0].GetComponent<Image>();
            if (cardImgComp != null) cardSprite = cardImgComp.sprite;

            var overlay = pieceCards[0].emptyOverlay;
            if (overlay != null)
            {
                var overlayImg = overlay.GetComponent<Image>();
                if (overlayImg != null) insetSprite = overlayImg.sprite;
            }
        }
        if (cardSprite == null) cardSprite = GetSpriteFromAtlas("GUI_52");
        if (insetSprite == null) insetSprite = GetSpriteFromAtlas("GUI_53");

        // 2. Create Next Piece Card (Perfect square matching GUI_52 styling)
        GameObject panelGO = new GameObject("NextPiecePreviewPanel", typeof(RectTransform));
        panelGO.transform.SetParent(canvas.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        
        // Position it at the bottom-right corner of the screen (as a smaller 140x140 square card)
        panelRT.anchorMin = new Vector2(1f, 0f);
        panelRT.anchorMax = new Vector2(1f, 0f);
        panelRT.pivot = new Vector2(1f, 0f);
        panelRT.anchoredPosition = new Vector2(-40f, 100f);
        panelRT.sizeDelta = new Vector2(140f, 140f);

        var panelImg = panelGO.AddComponent<Image>();
        panelImg.sprite = cardSprite;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = Color.white;

        // Shadow/Outline matching bottom cards
        var shadow = panelGO.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.4f);
        shadow.effectDistance = new Vector2(3f, -3f);

        // 3. Create Preview Area Inset Background (GUI_53)
        GameObject bgGO = new GameObject("PreviewAreaBg", typeof(RectTransform));
        bgGO.transform.SetParent(panelGO.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0.05f, 0.05f);
        bgRT.anchorMax = new Vector2(0.95f, 0.95f);
        bgRT.sizeDelta = Vector2.zero;

        var bgImg = bgGO.AddComponent<Image>();
        bgImg.sprite = insetSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(1f, 1f, 1f, 0.15f);

        // 4. Create RawImage for RenderTexture
        GameObject rawGO = new GameObject("PreviewRawImage", typeof(RectTransform));
        rawGO.transform.SetParent(bgGO.transform, false);
        var rawRT = rawGO.GetComponent<RectTransform>();
        rawRT.anchorMin = Vector2.zero;
        rawRT.anchorMax = Vector2.one;
        rawRT.sizeDelta = Vector2.zero;

        nextPiecePreviewRawImage = rawGO.AddComponent<RawImage>();
        nextPiecePreviewRawImage.color = Color.white;

        // Create a real-time silhouette drop shadow for the next piece preview
        var oldShadow = bgGO.transform.Find("PreviewImageShadow");
        if (oldShadow != null) Destroy(oldShadow.gameObject);

        GameObject shadowGO = new GameObject("PreviewImageShadow", typeof(RectTransform), typeof(RawImage));
        shadowGO.transform.SetParent(bgGO.transform, false);
        shadowGO.transform.SetSiblingIndex(rawGO.transform.GetSiblingIndex()); // Place it behind the main raw image
        
        var shadowRT = shadowGO.GetComponent<RectTransform>();
        shadowRT.anchorMin = rawRT.anchorMin;
        shadowRT.anchorMax = rawRT.anchorMax;
        shadowRT.pivot = rawRT.pivot;
        shadowRT.sizeDelta = rawRT.sizeDelta;
        // Offset to the left and slightly down
        shadowRT.anchoredPosition = new Vector2(-12f, -8f);
        shadowRT.localScale = Vector3.one;

        var shadowImg = shadowGO.GetComponent<RawImage>();
        shadowImg.color = new Color(0f, 0f, 0f, 0.28f);

        // 5. Create Camera and Camera Root
        if (nextPiecePreviewParent != null) Destroy(nextPiecePreviewParent);
        nextPiecePreviewParent = new GameObject("NextPiecePreviewCameraRoot");
        nextPiecePreviewParent.transform.position = new Vector3(-20000f, 100f, -5000f);

        GameObject camGO = new GameObject("NextPiecePreviewCam");
        camGO.transform.SetParent(nextPiecePreviewParent.transform, false);
        camGO.transform.localPosition = new Vector3(0f, 3.5f, -5.5f);
        
        nextPiecePreviewCam = camGO.AddComponent<Camera>();
        nextPiecePreviewCam.clearFlags = CameraClearFlags.SolidColor;
        nextPiecePreviewCam.backgroundColor = Color.clear;
        nextPiecePreviewCam.orthographic = true;
        nextPiecePreviewCam.orthographicSize = 2f;
        nextPiecePreviewCam.nearClipPlane = 0.1f;
        nextPiecePreviewCam.farClipPlane = 30f;
        nextPiecePreviewCam.depth = -3;

        // Add studio key and fill lights to next piece preview
        var keyLight = new GameObject("NextPreviewKeyLight", typeof(Light));
        keyLight.transform.SetParent(nextPiecePreviewCam.transform, false);
        keyLight.transform.localPosition = new Vector3(-4f, 4f, 1f);
        var kL = keyLight.GetComponent<Light>();
        kL.type = LightType.Point;
        kL.range = 25f;
        kL.intensity = 3.5f;
        kL.shadows = LightShadows.Soft;
        kL.shadowStrength = 0.85f;
        kL.color = new Color(1f, 0.98f, 0.95f);

        var fillLight = new GameObject("NextPreviewFillLight", typeof(Light));
        fillLight.transform.SetParent(nextPiecePreviewCam.transform, false);
        fillLight.transform.localPosition = new Vector3(4f, -4f, 1f);
        var fL = fillLight.GetComponent<Light>();
        fL.type = LightType.Point;
        fL.range = 25f;
        fL.intensity = 1.2f;
        fL.color = new Color(0.85f, 0.9f, 1f);

        // Create RenderTexture
        var rt = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 2;
        rt.Create();
        nextPiecePreviewCam.targetTexture = rt;
        nextPiecePreviewRawImage.texture = rt;
        shadowImg.texture = rt;
    }

    private Sprite GetSpriteFromAtlas(string spriteName)
    {
        if (pieceCards != null && pieceCards.Count > 0)
        {
            foreach (var card in pieceCards)
            {
                if (card != null)
                {
                    var img = card.GetComponent<Image>();
                    if (img != null && img.sprite != null && img.sprite.name == spriteName)
                    {
                        return img.sprite;
                    }
                    var emptyOverlay = card.emptyOverlay;
                    if (emptyOverlay != null)
                    {
                        var eImg = emptyOverlay.GetComponent<Image>();
                        if (eImg != null && eImg.sprite != null && eImg.sprite.name == spriteName)
                        {
                            return eImg.sprite;
                        }
                    }
                }
            }
        }
        return Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == spriteName);
    }

    private void AssignDefaultFontAsset(TextMeshProUGUI tmp)
    {
        var otherText = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>().FirstOrDefault(t => t.font != null);
        if (otherText != null && otherText.font != null)
        {
            tmp.font = otherText.font;
        }
    }

    private void PrepareNextPieceIndex()
    {
        if (allPiecePrefabs.Count == 0)
        {
            nextPieceIndex = -1;
            nextPieceMaterial = null;
            return;
        }

        List<int> availableIndices = new List<int>();
        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            if (!spawnedPieceIndices.Contains(i))
            {
                availableIndices.Add(i);
            }
        }

        if (availableIndices.Count == 0)
        {
            spawnedPieceIndices.Clear();
            for (int i = 0; i < allPiecePrefabs.Count; i++)
            {
                availableIndices.Add(i);
            }
        }

        // DÜZELTİLDİ: eskiden availableIndices içinden TAMAMEN rastgele seçiliyordu — katman
        // gözetmeden. Bu, oyuncuya art arda "henüz aktif olmayan katmana ait" kartlar dağıtıp
        // (bkz. IsShapePlaceable'daki fix notu) sessiz bir kilitlenmeye yol açabiliyordu. Şimdi,
        // mevcut havuzdan aktif katmana GERÇEKTEN sığan parçalar varsa seçim onların arasından
        // yapılıyor — hâlâ rastgele (çeşitlilik korunuyor), ama artık "boşa" bir kart asla
        // dağıtılmıyor. Aktif katmana sığan hiçbir parça kalmadıysa (o katman için gereken tüm
        // parçalar zaten dağıtılmış/yerleştirilmiş demektir) eski tam-rastgele davranışa düşülür.
        List<int> fitsActiveLayer = new List<int>();
        foreach (int i in availableIndices)
        {
            if (CanShapeFitActiveLayer(allPiecePrefabs[i]))
            {
                fitsActiveLayer.Add(i);
            }
        }
        List<int> pickFrom = fitsActiveLayer.Count > 0 ? fitsActiveLayer : availableIndices;

        nextPieceIndex = pickFrom[Random.Range(0, pickFrom.Count)];
        // Bu parçanın (kozmetik) rengi ŞİMDİ, tam bu anda kararlaştırılır — hem önizlemede hem de
        // parça gerçekten spawn edilirken (bkz. SpawnRandomPiece) AYNI renk kullanılacak.
        nextPieceMaterial = PickCosmeticPieceColor();
        UpdateNextPiecePreviewVisuals();
    }

    private void UpdateNextPiecePreviewVisuals()
    {
        ClearNextPiecePreview();

        if (nextPieceIndex < 0 || allPiecePrefabs.Count == 0) return;
        if (nextPiecePreviewCam == null) return;

        var prefab = allPiecePrefabs[nextPieceIndex];
        nextPiecePreview3D = Instantiate(prefab, nextPiecePreviewParent.transform);

        foreach (var col in nextPiecePreview3D.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }
        var drag = nextPiecePreview3D.GetComponent<DraggablePiece>();
        if (drag != null) drag.enabled = false;

        Material matchingMaterial = nextPieceMaterial;
        if (matchingMaterial != null)
        {
            var renderers = nextPiecePreview3D.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (r.gameObject.name.StartsWith("Cube_"))
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = matchingMaterial;
                    r.sharedMaterials = mats;
                }
            }
        }

        nextPiecePreview3D.transform.position = Vector3.zero;
        nextPiecePreview3D.transform.rotation = Quaternion.identity;
        nextPiecePreview3D.transform.localScale = Vector3.one;

        var rends = nextPiecePreview3D.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            nextPieceVisualCenter = nextPiecePreview3D.transform.InverseTransformPoint(b.center);
            nextPieceVisualRadius = b.extents.magnitude;
            if (nextPieceVisualRadius < 0.001f) nextPieceVisualRadius = 1f;
        }
        else
        {
            nextPieceVisualCenter = Vector3.zero;
            nextPieceVisualRadius = 1f;
        }

        UpdateNextPiecePreviewTransform();
    }

    private void UpdateNextPiecePreviewTransform()
    {
        if (nextPiecePreview3D == null || nextPiecePreviewCam == null) return;

        float depth = 3.5f;
        float viewH = 2f * nextPiecePreviewCam.orthographicSize;
        float scale = (viewH * 0.70f * 0.5f) / Mathf.Max(nextPieceVisualRadius, 0.001f);

        Vector3 center = nextPiecePreviewCam.transform.position + nextPiecePreviewCam.transform.forward * depth;

        float elev = 90f;
        float azim = 0f;
        Quaternion baseIso = Quaternion.Euler(elev, azim, 0f);

        Quaternion targetRot = nextPiecePreviewCam.transform.rotation * Quaternion.Inverse(baseIso) * Quaternion.Euler(0f, previewRotationAngle, 0f);

        nextPiecePreview3D.transform.rotation = targetRot;
        nextPiecePreview3D.transform.position = center - (targetRot * nextPieceVisualCenter * scale);
        nextPiecePreview3D.transform.localScale = Vector3.one * scale;
    }
}