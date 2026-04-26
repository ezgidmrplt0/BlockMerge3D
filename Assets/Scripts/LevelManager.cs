using UnityEngine;
using System.Collections.Generic;

public class LevelManager : MonoBehaviour
{
    [Header("Configuration")]
    public Transform spawnPoint;
    public LevelData currentLevel;

    [Header("Piece Spawn")]
    [Range(0f, 0.5f)] public float pieceRowViewportY  = 0.12f; // ekranın kaç %'inde (alttan)
    [Range(0f, 0.5f)] public float pieceRowMarginX    = 0.12f; // sol/sağ kenar boşluğu

    private GameObject activeMainPiece;
    private List<GameObject> activePieces = new List<GameObject>();
    private GridManager gridManager;
    private Material ghostTargetMat;

    private void Awake()
    {
        // GridManager aynı GameObject'te veya bulunamıyorsa yeni eklenir
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
        if (level.mainShapePrefab != null)
        {
            Vector3 shapePos = ComputeMainShapePosition();
            activeMainPiece = Instantiate(level.mainShapePrefab, shapePos, Quaternion.identity);
            activeMainPiece.name = "Main_Shape";

            var holder = activeMainPiece.GetComponent<CubeShapeDataHolder>();
            if (holder != null)
            {
                gridManager.Initialize(
                    holder.occupiedCells,
                    holder.cellSize,
                    holder.spacing,
                    activeMainPiece.transform.position);

                // Ana şekli yarı-saydam hedef görünümüne çevir
                ApplyTargetGhost(activeMainPiece);
            }
        }

        // ── Tamamlayıcı parçalar ─────────────────────────────────────────────
        int count = level.complementaryPieces.Count;
        for (int i = 0; i < count; i++)
        {
            if (level.complementaryPieces[i] == null) continue;

            Vector3 pos = ComputeSpawnPosition(i, count);
            GameObject piece = Instantiate(level.complementaryPieces[i], pos, Quaternion.identity);
            piece.name = $"Piece_{i + 1}";

            var drag = piece.AddComponent<DraggablePiece>();
            drag.HomePosition = pos;

            activePieces.Add(piece);
        }
    }

    public void ClearCurrentLevel()
    {
        if (activeMainPiece != null)
        {
            Destroy(activeMainPiece);
            activeMainPiece = null;
        }
        foreach (var p in activePieces) if (p != null) Destroy(p);
        activePieces.Clear();

        if (ghostTargetMat != null) { Destroy(ghostTargetMat); ghostTargetMat = null; }
    }

    // Main shape'i ekranın ortasına yerleştirir (spawnPoint'in kamera uzaklığını korur)
    private Vector3 ComputeMainShapePosition()
    {
        var cam = Camera.main;
        if (cam == null) return spawnPoint.position;
        float depth = Vector3.Distance(cam.transform.position, spawnPoint.position);
        return cam.ViewportToWorldPoint(new Vector3(0.5f, 0.58f, depth));
    }

    // Parçaları ekranın altında yatay sıra hâlinde konumlandırır (viewport koordinatları)
    private Vector3 ComputeSpawnPosition(int index, int total)
    {
        var cam = Camera.main;
        if (cam == null) return spawnPoint.position + Vector3.right * index * 2f;

        // Main shape ile aynı kamera uzaklığı
        float depth = Vector3.Distance(cam.transform.position, spawnPoint.position);

        float vx = total == 1
            ? 0.5f
            : Mathf.Lerp(pieceRowMarginX, 1f - pieceRowMarginX, (float)index / (total - 1));

        return cam.ViewportToWorldPoint(new Vector3(vx, pieceRowViewportY, depth));
    }

    // Ana şeklin tüm Renderer'larını yarı-saydam mavi ghost materyalle değiştirir
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

        // Ana şekle tıklanamaz (Collider'lar devre dışı)
        foreach (var col in shape.GetComponentsInChildren<Collider>())
            col.enabled = false;
    }
}
