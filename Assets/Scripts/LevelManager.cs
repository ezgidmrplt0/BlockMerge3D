using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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
    private int  nextPieceIndex  = 0;

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
                gridManager.Initialize(holder.occupiedCells, holder.cellSize, holder.spacing, activeMainPiece.transform.localPosition, mainCubeLocation);
                ApplyTargetGhost(activeMainPiece);
            }
        }

        allPiecePrefabs.Clear();
        allPieceWidths.Clear();
        allPieceHeights.Clear();
        allPieceDepths.Clear();
        nextPieceIndex = 0;

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
        GameObject piece = Instantiate(allPiecePrefabs[index], new Vector3(0, -100, 0), Quaternion.identity);
        piece.name = $"Piece_{index + 1}";
        DisableShadows(piece);
        var drag = piece.AddComponent<DraggablePiece>();
        drag.slotScale = pieceSlotScale;
        activePieces.Add(piece);
        activePieceDataIndices.Add(index);
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

        if (nextPieceIndex < allPiecePrefabs.Count)
        {
            SpawnPieceAtIndex(nextPieceIndex);
            nextPieceIndex++;
        }
        RecomputeHomePositions();
    }

    private void LateUpdate()
    {
        foreach (var p in placedPieces)
        {
            if (p == null) continue;
            p.GetComponent<DraggablePiece>()?.RefreshWorldPosition();
        }
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
        allPieceDepths.Clear();
        nextPieceIndex = 0;
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
