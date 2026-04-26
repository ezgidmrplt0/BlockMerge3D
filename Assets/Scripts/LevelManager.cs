using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("Configuration")]
    public Transform spawnPoint;
    public LevelData currentLevel;

    [Header("Piece Spawn")]
    public float pieceBelowGap = 1.5f;  // ana şeklin altı ile parça satırı arasındaki boşluk
    public float pieceGap      = 0.5f;  // parçalar arasındaki yatay boşluk

    [Tooltip("Aynı anda sahnede görünen parça sayısı")]
    public int maxVisiblePieces = 3;

    private GameObject activeMainPiece;
    private List<GameObject> activePieces = new List<GameObject>();
    private List<GameObject> placedPieces = new List<GameObject>();
    private GridManager gridManager;
    private Material ghostTargetMat;

    // Sıra sistemi
    private List<GameObject> allPiecePrefabs = new List<GameObject>();
    private List<float>      allPieceWidths  = new List<float>();
    private List<float>      allPieceHeights = new List<float>();
    private List<int>        activePieceDataIndices = new List<int>();
    private int  nextPieceIndex  = 0;
    private float cachedPieceTopY = 0f;

    private void Awake()
    {
        Instance    = this;
        gridManager = GetComponent<GridManager>();
        if (gridManager == null) gridManager = gameObject.AddComponent<GridManager>();
    }

    private void Start()
    {
        if (currentLevel != null) LoadLevel(currentLevel);
    }

    public void LoadLevel(LevelData level)
    {
        ClearCurrentLevel();
        if (spawnPoint == null) { Debug.LogError("LevelManager: Spawn Point atanmadı!"); return; }

        // ── Ana hedef şekil ──────────────────────────────────────────────────
        Vector3 gridOrigin = spawnPoint.position;
        if (level.mainShapePrefab != null)
        {
            var prefabHolder = level.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
            Vector3 viewportCenter = ComputeMainShapePosition();

            gridOrigin = viewportCenter;
            if (prefabHolder != null)
            {
                float step = prefabHolder.cellSize + prefabHolder.spacing;
                Vector3 bc = BoundsCenter(prefabHolder.occupiedCells, step);
                // XY ortalanmış, Z = 0
                gridOrigin = new Vector3(
                    viewportCenter.x - bc.x,
                    viewportCenter.y - bc.y,
                    0f);
            }

            activeMainPiece = Instantiate(level.mainShapePrefab, gridOrigin, Quaternion.identity);
            activeMainPiece.name = "Main_Shape";

            var holder = activeMainPiece.GetComponent<CubeShapeDataHolder>();
            if (holder != null)
            {
                gridManager.Initialize(holder.occupiedCells, holder.cellSize, holder.spacing, gridOrigin);
                ApplyTargetGhost(activeMainPiece);
            }
        }

        // ── Tamamlayıcı parçalar ─────────────────────────────────────────────
        float mainMinY = gridOrigin.y;
        cachedPieceTopY = mainMinY - pieceBelowGap;

        allPiecePrefabs.Clear();
        allPieceWidths.Clear();
        allPieceHeights.Clear();
        nextPieceIndex = 0;

        foreach (var prefab in level.complementaryPieces)
        {
            if (prefab == null) continue;
            var ph = prefab.GetComponent<CubeShapeDataHolder>();
            allPiecePrefabs.Add(prefab);
            if (ph != null)
            {
                float step = ph.cellSize + ph.spacing;
                int maxX = 0, maxY = 0;
                foreach (var c in ph.occupiedCells)
                {
                    if (c.x > maxX) maxX = c.x;
                    if (c.y > maxY) maxY = c.y;
                }
                allPieceWidths.Add((maxX + 1) * step);
                allPieceHeights.Add((maxY + 1) * step);
            }
            else
            {
                allPieceWidths.Add(2f);
                allPieceHeights.Add(2f);
            }
        }

        // İlk maxVisiblePieces parçayı spawn et
        int toSpawn = Mathf.Min(maxVisiblePieces, allPiecePrefabs.Count);
        for (int i = 0; i < toSpawn; i++)
        {
            SpawnPieceAtIndex(nextPieceIndex);
            nextPieceIndex++;
        }
        RecomputeHomePositions();
        FitCameraToScene();
    }

    private void SpawnPieceAtIndex(int index)
    {
        // Geçici pozisyonda spawn et; RecomputeHomePositions gerçek yeri atar
        GameObject piece = Instantiate(allPiecePrefabs[index], new Vector3(9999f, 9999f, 0f), Quaternion.identity);
        piece.name = $"Piece_{index + 1}";
        var drag = piece.AddComponent<DraggablePiece>();
        drag.HomePosition = new Vector3(9999f, 9999f, 0f);
        activePieces.Add(piece);
        activePieceDataIndices.Add(index);
    }

    private void RecomputeHomePositions()
    {
        if (activePieces.Count == 0) return;

        float totalWidth = 0f;
        for (int i = 0; i < activePieces.Count; i++)
        {
            int di = activePieceDataIndices[i];
            totalWidth += allPieceWidths[di];
            if (i > 0) totalWidth += pieceGap;
        }

        float curX = spawnPoint.position.x - totalWidth * 0.5f;
        for (int i = 0; i < activePieces.Count; i++)
        {
            int di = activePieceDataIndices[i];
            float w = allPieceWidths[di];
            float h = allPieceHeights[di];
            Vector3 newHome = new Vector3(curX, cachedPieceTopY - h, 0f);

            var drag = activePieces[i].GetComponent<DraggablePiece>();
            if (drag != null)
            {
                drag.HomePosition = newHome;
                if (!drag.IsBeingDragged && !drag.IsPlaced)
                    activePieces[i].transform.position = newHome;
            }
            curX += w + pieceGap;
        }
    }

    public void OnPiecePlaced(DraggablePiece piece)
    {
        int idx = activePieces.IndexOf(piece.gameObject);
        if (idx < 0) return;

        placedPieces.Add(activePieces[idx]);
        activePieces.RemoveAt(idx);
        activePieceDataIndices.RemoveAt(idx);

        if (nextPieceIndex < allPiecePrefabs.Count)
        {
            SpawnPieceAtIndex(nextPieceIndex);
            nextPieceIndex++;
        }
        RecomputeHomePositions();
    }

    public void ClearCurrentLevel()
    {
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
        nextPieceIndex = 0;
    }

    // Ana şekil spawnPoint'te sabit doğar
    private Vector3 ComputeMainShapePosition() => spawnPoint.position;

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

    // Hücre grubunun sınır kutusunun merkezini dünya birimiyle döndürür
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
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostTargetMat;
            r.sharedMaterials = mats;
        }

        foreach (var col in shape.GetComponentsInChildren<Collider>())
            col.enabled = false;
    }
}
