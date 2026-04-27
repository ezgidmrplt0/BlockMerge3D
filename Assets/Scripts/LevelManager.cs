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
    public List<Transform> pieceSlots = new List<Transform>();

    [Header("Visual Settings")]
    public int   maxVisiblePieces = 3;
    public float pieceSlotScale   = 0.6f;
    [Range(0f, 1f)] public float smartSpawnProbability = 0.5f;
    
    private List<bool> activeIsSmart = new List<bool>();

    private GameObject activeMainPiece;
    private List<GameObject> activePieces = new List<GameObject>();
    private List<GameObject> placedPieces = new List<GameObject>();
    private GridManager gridManager;
    private Material ghostTargetMat;

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

    private void Start() { }

    public void LoadLevel(LevelData level)
    {
        ClearCurrentLevel();
        if (mainCubeLocation == null) { Debug.LogError("LevelManager: MainCubeLocation atanmadı!"); return; }

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
            DisableShadows(activeMainPiece);

            var holder = activeMainPiece.GetComponent<CubeShapeDataHolder>();
            if (holder != null)
            {
                // Dunya pozisyonu ile baslatmaya geri döndük
                gridManager.Initialize(holder.occupiedCells, holder.cellSize, holder.spacing, activeMainPiece.transform.position);
                ApplyTargetGhost(activeMainPiece);
            }
        }

        allPiecePrefabs.Clear();
        allPieceWidths.Clear();
        allPieceHeights.Clear();
        allPieceDepths.Clear();

        foreach (var prefab in level.complementaryPieces)
        {
            if (prefab == null) continue;
            var ph = prefab.GetComponent<CubeShapeDataHolder>();
            allPiecePrefabs.Add(prefab);
            if (ph != null)
            {
                float step = ph.cellSize + ph.spacing;
                int maxX = 0, maxY = 0, maxZ = 0;
                foreach (var c in ph.occupiedCells)
                {
                    if (c.x > maxX) maxX = c.x;
                    if (c.y > maxY) maxY = c.y;
                    if (c.z > maxZ) maxZ = c.z;
                }
                allPieceWidths.Add((maxX + 1) * step);
                allPieceHeights.Add((maxY + 1) * step);
                allPieceDepths.Add((maxZ + 1) * step);
            }
            else
            {
                allPieceWidths.Add(2f);
                allPieceHeights.Add(2f);
                allPieceDepths.Add(2f);
            }
        }

        for (int i = 0; i < maxVisiblePieces; i++)
            SpawnRandomPiece();
        RecomputeHomePositions();
        FitCameraToScene();
    }

    public static readonly Color[] PIECE_PALETTE = new Color[]
    {
        new Color(1.0f, 0.0f, 0.45f),    // Ultra Neon Pink
        new Color(0.0f, 1.0f, 0.25f),    // Electric Lime
        new Color(0.0f, 0.85f, 1.0f),    // Cyber Cyan
        new Color(1.0f, 0.45f, 0.0f),    // Blaze Orange
        new Color(0.6f, 0.0f, 1.0f),     // Deep Purple Neon
        new Color(1.0f, 0.95f, 0.0f),    // Acid Yellow
        new Color(0.0f, 1.0f, 0.65f)     // Mint Glow
    };

    private void SpawnRandomPiece()
    {
        if (allPiecePrefabs.Count == 0) return;

        float fullness = (float)GridManager.Instance.PlacedCells / GridManager.Instance.TotalCells;
        
        // 1. Yerlesebilir parcalari bul
        List<int> validIndices = new List<int>();
        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            var h = allPiecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (h == null) continue;
            
            // Herhangi bir rotasyonda sigiyor mu?
            bool canFitAnywhere = false;
            Quaternion[] rots = { Quaternion.identity, Quaternion.Euler(0,90,0), Quaternion.Euler(90,0,0) };
            foreach(var r in rots)
            {
                if (GridManager.Instance.GetPossibleOffsets(GridManager.RotateCells(h.occupiedCells, r)).Count > 0)
                {
                    canFitAnywhere = true;
                    break;
                }
            }
            if (canFitAnywhere) validIndices.Add(i);
        }

        // Eger hicbir parca sigmiyorsa veya saha cok doluysa, en kucuk parcayi zorla
        bool forceSmall = (validIndices.Count == 0 || fullness > 0.8f);
        
        int smartCount = 0;
        foreach (bool s in activeIsSmart) if (s) smartCount++;

        bool shouldBeSmart = (smartCount == 0) || (Random.value < smartSpawnProbability);

        if (forceSmall)
        {
            int smallIdx = FindSmallestPieceIndex();
            activeIsSmart.Add(true);
            SpawnPieceAtIndex(smallIdx, Quaternion.identity, GetDominantColorOnGrid());
        }
        else if (shouldBeSmart)
        {
            int index = FindBestPieceIndex(out Quaternion rot, out Color? recCol, out bool foundMerge);
            activeIsSmart.Add(foundMerge); 
            SpawnPieceAtIndex(index, rot, recCol);
        }
        else
        {
            int index = validIndices[Random.Range(0, validIndices.Count)];
            activeIsSmart.Add(false);
            SpawnPieceAtIndex(index, Quaternion.identity, null);
        }

        // Parca uretildikten sonra hala hamle var mi bak
        CheckGameOver();
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

    private Color? GetDominantColorOnGrid()
    {
        // Sahada en cok hangi renkten varsa onu dondur (Joker icin)
        // GridManager'dan bir sekilde almaliyiz, simdilik rastgele paletten
        return PIECE_PALETTE[Random.Range(0, PIECE_PALETTE.Length)];
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
            Quaternion.Euler(0, 270, 0),
            Quaternion.Euler(90, 0, 0),
            Quaternion.Euler(270, 0, 0)
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
        int bestIdx = -1;
        Quaternion bestRot = Quaternion.identity;
        Color? bestCol = null;

        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            var h = allPiecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (h == null) continue;
            foreach (var rot in possibleRotations)
            {
                var rotatedCells = GridManager.RotateCells(h.occupiedCells, rot);
                var offsets = gridManager.GetPossibleOffsets(rotatedCells);
                if (offsets.Count == 0) continue;

                foreach (var paletteCol in PIECE_PALETTE)
                {
                    foreach (var off in offsets)
                    {
                        int score = CalculateMatchScore(rotatedCells, off, paletteCol);
                        if (score > bestMatchScore)
                        {
                            bestMatchScore = score;
                            bestIdx = i;
                            bestRot = rot;
                            bestCol = paletteCol;
                        }
                    }
                }
            }
        }

        if (bestIdx != -1)
        {
            rotation = bestRot;
            recommendedColor = bestCol;
            foundMerge = (bestMatchScore > 0);
            return bestIdx;
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

    private void SpawnPieceAtIndex(int index, Quaternion? initialRot = null, Color? forcedColor = null)
    {
        if (index < 0 || index >= allPiecePrefabs.Count) return;

        GameObject piece = Instantiate(allPiecePrefabs[index], new Vector3(0, -100, 0), initialRot ?? Quaternion.identity);
        piece.name = $"Piece_{index + 1}";
        DisableShadows(piece);
        
        // Bu parca icin bir renk sec (zorunlu renk yoksa rastgele)
        Color col = forcedColor ?? PIECE_PALETTE[Random.Range(0, PIECE_PALETTE.Length)];
        ApplyColorToPiece(piece, col);

        var drag = piece.AddComponent<DraggablePiece>();
        drag.slotScale = pieceSlotScale;
        activePieces.Add(piece);
        activePieceDataIndices.Add(index);
    }

    private static void ApplyColorToPiece(GameObject piece, Color color)
    {
        foreach (var r in piece.GetComponentsInChildren<Renderer>())
            r.material.color = color;
    }

    private void RecomputeHomePositions()
    {
        if (activePieces.Count == 0 || pieceSlots.Count == 0) return;

        for (int i = 0; i < activePieces.Count; i++)
        {
            if (i < pieceSlots.Count && pieceSlots[i] != null)
            {
                activePieces[i].transform.SetParent(pieceSlots[i]);

                int di = activePieceDataIndices[i];
                float w = allPieceWidths[di];
                float h = allPieceHeights[di];
                float d = allPieceDepths[di];

                Vector3 localOffset = -new Vector3(w * 0.5f, h * 0.5f, d * 0.5f);

                var drag = activePieces[i].GetComponent<DraggablePiece>();
                if (drag != null)
                {
                    drag.HomePosition = pieceSlots[i].TransformPoint(localOffset);
                    if (!drag.IsBeingDragged && !drag.IsPlaced)
                    {
                        activePieces[i].transform.localPosition = localOffset;
                        activePieces[i].transform.localRotation = Quaternion.identity;
                        activePieces[i].transform.localScale    = Vector3.one * pieceSlotScale;
                    }
                }
            }
        }
    }

    public void OnPiecePlaced(DraggablePiece piece)
    {
        int idx = activePieces.IndexOf(piece.gameObject);
        if (idx < 0) return;

        piece.transform.SetParent(null); 

        placedPieces.Add(activePieces[idx]);
        activePieces.RemoveAt(idx);
        activePieceDataIndices.RemoveAt(idx);
        activeIsSmart.RemoveAt(idx);

        SpawnRandomPiece();
        RecomputeHomePositions();

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
                Quaternion.Euler(0, 90, 0), Quaternion.Euler(0, 180, 0), Quaternion.Euler(0, 270, 0),
                Quaternion.Euler(90, 0, 0), Quaternion.Euler(270, 0, 0)
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
        if (activeMainPiece != null) { Destroy(activeMainPiece); activeMainPiece = null; }
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

        Include(activeMainPiece);
        foreach (var p in activePieces) Include(p);

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
        ghostTargetMat = new Material(Shader.Find("Standard"));
        ghostTargetMat.color = new Color(0.5f, 0.8f, 1f, 0.22f);
        ghostTargetMat.SetFloat("_Mode", 3f);
        ghostTargetMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        ghostTargetMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        ghostTargetMat.SetInt("_ZWrite", 0);
        ghostTargetMat.EnableKeyword("_ALPHABLEND_ON");
        ghostTargetMat.renderQueue = 3000;

        foreach (var r in shape.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostTargetMat;
            r.sharedMaterials = mats;
        }

        foreach (var col in shape.GetComponentsInChildren<Collider>())
            col.enabled = false;
    }
}
