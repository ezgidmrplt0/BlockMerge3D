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
    public int   maxVisiblePieces = 3;
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
        InitializePremiumPalette();
    }

    private void InitializePremiumPalette()
    {
        // Koyu toprak tonları — açık arka plan üzerinde belirgin, cozy
        Color[] premiumColors = new Color[]
        {
            new Color(0.65f, 0.28f, 0.18f), // Koyu Terracotta
            new Color(0.32f, 0.52f, 0.30f), // Koyu Adaçayı Yeşili
            new Color(0.25f, 0.42f, 0.62f), // Koyu Toz Mavisi
            new Color(0.75f, 0.55f, 0.15f), // Koyu Bal Sarısı
            new Color(0.68f, 0.35f, 0.38f), // Koyu Gül / Kiremit Pembe
            new Color(0.48f, 0.37f, 0.62f), // Koyu Lavanta
            new Color(0.55f, 0.38f, 0.22f)  // Kahverengi / Walnut
        };

        // Try to find a template material to clone
        Material template = null;
        if (pieceMaterials != null && pieceMaterials.Length > 0)
        {
            // Try to find a material that doesn't use a shader with "PembeKup" in its name
            template = pieceMaterials.FirstOrDefault(m => m != null && m.shader != null && !m.shader.name.Contains("PembeKup"));
        }

        // If that failed, search within prefabs for a non-PembeKup material
        if (template == null && allPiecePrefabs != null)
        {
            foreach (var prefab in allPiecePrefabs)
            {
                if (prefab != null)
                {
                    var r = prefab.GetComponentInChildren<Renderer>();
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null && !r.sharedMaterial.shader.name.Contains("PembeKup"))
                    {
                        template = r.sharedMaterial;
                        break;
                    }
                }
            }
        }

        // If template is still null or uses the hardcoded PembeKup shader, let's create a fresh customizable material
        if (template == null || (template.shader != null && template.shader.name.Contains("PembeKup")))
        {
            Shader standardShader = Shader.Find("Universal Render Pipeline/Lit");
            if (standardShader == null) standardShader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (standardShader == null) standardShader = Shader.Find("Standard");

            if (standardShader != null)
            {
                template = new Material(standardShader);
            }
            else if (pieceMaterials != null && pieceMaterials.Length > 0)
            {
                template = pieceMaterials.FirstOrDefault(m => m != null);
            }
        }

        if (template != null)
        {
            List<Material> generatedMats = new List<Material>();
            foreach (var col in premiumColors)
            {
                Material m = new Material(template);
                m.name = $"PremiumMaterial_{col.r:F2}_{col.g:F2}_{col.b:F2}";
                
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", col);

                // Cozy mat/saten görünüm: yüksek smoothness, sıfır metallic, çok düşük emission
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.55f);
                if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic",   0.0f);

                // Çok hafif emission — ışıltı değil, derinlik hissi
                if (m.HasProperty("_EmissionColor"))
                {
                    m.SetColor("_EmissionColor", col * 0.06f);
                    m.EnableKeyword("_EMISSION");
                }
                
                generatedMats.Add(m);
            }
            pieceMaterials = generatedMats.ToArray();
            Debug.Log($"[BM3D] Programmatically generated {pieceMaterials.Length} premium colors for pieceMaterials.");
        }
        else
        {
            Debug.LogError("[BM3D] Failed to find or create a template material for premium colors!");
        }
    }

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

        // Kart UI'larını başlat (idempotent)
        for (int i = 0; i < pieceCards.Count; i++)
            pieceCards[i]?.Init(i);

        for (int i = 0; i < maxVisiblePieces; i++)
            SpawnRandomPiece();
        FitCameraToScene();
    }

    [Header("Color Palette Settings (Material-Based)")]
    [Tooltip("Sürüklenen oyun parçaları için kullanılacak Material (Malzeme) paleti")]
    public Material[] pieceMaterials;

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
        
        bool shouldBeSmart = (Random.value < smartSpawnProbability);

        if (forceSmall)
        {
            int smallIdx = FindSmallestPieceIndex();
            activeIsSmart.Add(true);
            SpawnPieceAtIndex(smallIdx, GetRandom90DegreeRotation(), GetDominantMaterialOnGrid(), targetCard);
        }
        else if (shouldBeSmart)
        {
            int index = FindBestPieceIndex(out Quaternion rot, out Color? recCol, out bool foundMerge);
            activeIsSmart.Add(foundMerge); 
            Material recMat = null;
            if (recCol.HasValue && pieceMaterials != null)
            {
                recMat = pieceMaterials.FirstOrDefault(m => m != null && GridManager.ColorsApproxEqual(GridManager.GetMaterialColor(m), recCol.Value));
            }
            SpawnPieceAtIndex(index, rot, recMat, targetCard);
        }
        else
        {
            int index = validIndices[Random.Range(0, validIndices.Count)];
            activeIsSmart.Add(false);
            SpawnPieceAtIndex(index, GetRandom90DegreeRotation(), null, targetCard);
        }

        // Parca uretildikten sonra hala hamle var mi bak
        CheckGameOver();
    }

    private Quaternion GetRandom90DegreeRotation()
    {
        float rx = Random.Range(0, 4) * 90f;
        float ry = Random.Range(0, 4) * 90f;
        float rz = Random.Range(0, 4) * 90f;
        return Quaternion.Euler(rx, ry, rz);
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
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostTargetMaterial;
            r.sharedMaterials = mats;
        }

        foreach (var col in shape.GetComponentsInChildren<Collider>())
            col.enabled = false;
    }
}
