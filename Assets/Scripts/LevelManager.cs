using UnityEngine;
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

        // Sahadaki yerleştirilmiş parçalara ve boşluklara yerleşebilecek (uygun) parçaları bul
        List<int> placeableIndices = new List<int>();
        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            if (IsShapePlaceable(allPiecePrefabs[i]))
            {
                placeableIndices.Add(i);
            }
        }

        int indexToSpawn = -1;

        if (placeableIndices.Count > 0)
        {
            // Sahadaki boşluklara tam oturan/uygun parçalardan rastgele birini seç!
            indexToSpawn = placeableIndices[Random.Range(0, placeableIndices.Count)];
            activeIsSmart.Add(true);
        }
        else
        {
            // Eğer hiçbir parça sığmıyorsa (sıkışmayı önlemek için) en küçük parçayı spawn et
            indexToSpawn = FindSmallestPieceIndex();
            activeIsSmart.Add(true);
        }

        // Aktif katmandaki renk uyumuna göre akıllı materyal/renk seçimi yap!
        Material matchingMaterial = GetDominantMaterialOnActiveLayer();

        // Parçayı spawn et
        SpawnPieceAtIndex(indexToSpawn, GetRandom90DegreeRotation(), matchingMaterial, targetCard);

        // Parca uretildikten sonra hala hamle var mi bak
        CheckGameOver();
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

    private Material GetDominantMaterialOnGrid()
    {
        return GetDominantMaterialOnActiveLayer();
    }

    private Material GetDominantMaterialOnActiveLayer()
    {
        if (gridManager == null) return null;

        var colorCounts = new Dictionary<Color, int>();
        foreach (var c in gridManager.targetCells)
        {
            if (c.y == gridManager.ActiveLayerY)
            {
                if (gridManager.occupiedCells.Contains(c))
                {
                    if (gridManager.GetCellColor(c, out Color col))
                    {
                        Color matchedColor = col;
                        bool found = false;
                        foreach (var key in colorCounts.Keys)
                        {
                            if (GridManager.ColorsApproxEqual(col, key))
                            {
                                matchedColor = key;
                                found = true;
                                break;
                            }
                        }
                        if (found) colorCounts[matchedColor]++;
                        else colorCounts[matchedColor] = 1;
                    }
                }
            }
        }

        // Eğer aktif katmanda hiç blok yoksa, oyuncunun katmana yeni bir renkle başlama esnekliği olsun diye paletten rastgele seçilir.
        Color? dominantColor = null;
        if (colorCounts.Count > 0)
        {
            int maxCount = 0;
            foreach (var kvp in colorCounts)
            {
                if (kvp.Value > maxCount)
                {
                    maxCount = kvp.Value;
                    dominantColor = kvp.Key;
                }
            }
        }

        // Bu renge ait Materyali pieceMaterials içinden bul
        if (dominantColor.HasValue && pieceMaterials != null)
        {
            foreach (var m in pieceMaterials)
            {
                if (m != null)
                {
                    Color mCol = GridManager.GetMaterialColor(m);
                    if (GridManager.ColorsApproxEqual(dominantColor.Value, mCol))
                    {
                        return m;
                    }
                }
            }
        }

        // Dominant renk bulunamadıysa (katman boşsa) paletten rastgele seç
        if (pieceMaterials != null && pieceMaterials.Length > 0)
        {
            var valid = pieceMaterials.Where(m => m != null).ToList();
            if (valid.Count > 0) return valid[Random.Range(0, valid.Count)];
        }

        return null;
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

        // Check if there are frozen cells to thaw/explode
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
                if (lpc != null && CameraOrbit.Instance != null && CameraOrbit.Instance.IsInPanelMode)
                {
                    lpc.ClosePanel(() =>
                    {
                        HandlePostPiecePlaced();
                    });
                }
                else
                {
                    HandlePostPiecePlaced();
                }
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
        if (activePieces.Count == 0) return;

        bool anyMovePossible = false;
        foreach (var pieceGO in activePieces)
        {
            if (pieceGO == null) continue;
            var h = pieceGO.GetComponent<CubeShapeDataHolder>();
            if (h == null) continue;

            Quaternion[] possibleRots = { 
                Quaternion.identity, 
                Quaternion.Euler(0, 90, 0), Quaternion.Euler(0, 180, 0), Quaternion.Euler(0, 270, 0)
            };

            foreach (var rot in possibleRots)
            {
                var rotatedCells = GridManager.RotateCells(h.occupiedCells, rot);
                if (gridManager.GetPossibleOffsets(rotatedCells).Count > 0)
                {
                    anyMovePossible = true;
                    break;
                }
            }
            if (anyMovePossible) break;
        }

        if (!anyMovePossible)
        {
            GameManager.Instance?.GameOver();
        }
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
                // "Prefilled_matIdx_x_y_z" — matIdx'i doğrudan isimden oku
                var parts = cubeName.Split('_');
                int matIdx = -1;
                if (parts.Length >= 2) int.TryParse(parts[1], out matIdx);
                
                Material prefilledMat = null;
                if (pieceMaterials != null && matIdx >= 0 && matIdx < pieceMaterials.Length)
                    prefilledMat = pieceMaterials[matIdx];
                
                if (prefilledMat == null)
                    prefilledMat = pieceMaterials != null && pieceMaterials.Length > 0 ? pieceMaterials[0] : null;
                
                if (prefilledMat != null)
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = prefilledMat;
                    r.sharedMaterials = mats;
                }
                
                // GridManager'a kaydet
                var holder2 = shape.GetComponent<CubeShapeDataHolder>();
                float step2 = holder2 != null ? holder2.cellSize + holder2.spacing : 1f;
                Vector3 lp = shape.transform.InverseTransformPoint(r.transform.position);
                var cell2 = new Vector3Int(
                    Mathf.RoundToInt(lp.x / step2),
                    Mathf.RoundToInt(lp.y / step2),
                    Mathf.RoundToInt(lp.z / step2));
                
                GridManager.Instance.occupiedCells.Add(cell2);
                if (prefilledMat != null)
                    GridManager.Instance.SetCellColor(cell2, GridManager.GetMaterialColor(prefilledMat));
                GridManager.Instance.SetCellMatIndex(cell2, matIdx);
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
                    int matIdx = (holderF?.prefilledMaterialIndices != null && pfListIdx < holderF.prefilledMaterialIndices.Count)
                        ? holderF.prefilledMaterialIndices[pfListIdx] : 0;

                    Material prefilledMat = (pieceMaterials != null && matIdx >= 0 && matIdx < pieceMaterials.Length)
                        ? pieceMaterials[matIdx] : (pieceMaterials?.Length > 0 ? pieceMaterials[0] : null);

                    if (prefilledMat != null)
                    {
                        var mats2 = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats2.Length; i++) mats2[i] = prefilledMat;
                        r.sharedMaterials = mats2;
                    }

                    GridManager.Instance.occupiedCells.Add(cellF);
                    if (prefilledMat != null)
                        GridManager.Instance.SetCellColor(cellF, GridManager.GetMaterialColor(prefilledMat));
                    GridManager.Instance.SetCellMatIndex(cellF, matIdx);
                }
                else // Normal hedef (Ghost)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = ghostTargetMaterial;
                    r.sharedMaterials = mats;
                }
            }
        }

        foreach (var col in shape.GetComponentsInChildren<Collider>())
            col.enabled = true;
    }

    private bool IsShapePlaceable(GameObject shapePrefabOrGO)
    {
        if (shapePrefabOrGO == null) return false;
        var h = shapePrefabOrGO.GetComponent<CubeShapeDataHolder>();
        if (h == null) return false;

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
            var offsets = gridManager.GetPossibleOffsets(rotated);
            if (offsets.Count > 0) return true;
        }

        return false;
    }
}
