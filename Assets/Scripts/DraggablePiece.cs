using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(CubeShapeDataHolder))]
public class DraggablePiece : MonoBehaviour
{
    public Vector3 HomePosition { get; set; }

    private CubeShapeDataHolder holder;
    private GridManager grid;
    private Camera mainCam;

    // Mevcut hücreler (rotasyon uygulanmış)
    private List<Vector3Int> currentCells;
    private int rotationStep = 0; // 0–3, her biri 90° Y dönüşü

    private bool isDragging;
    private bool isPlaced;
    private Vector3Int placedOffset;
    private int dragYLevel;

    // Ghost (yerleştirme önizlemesi)
    private GameObject ghost;
    private List<Transform> ghostCubes = new List<Transform>();
    private Material ghostValidMat;
    private Material ghostInvalidMat;

    // Aynı anda yalnızca bir parça sürüklenebilir
    private static DraggablePiece activeDrag;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        holder     = GetComponent<CubeShapeDataHolder>();
        mainCam    = Camera.main;
        currentCells = new List<Vector3Int>(holder.occupiedCells);
    }

    private void Start()
    {
        grid = GridManager.Instance;
        if (grid == null) { Debug.LogError("GridManager bulunamadı!"); return; }

        if (HomePosition == Vector3.zero) HomePosition = transform.position;
        BuildGhost();
    }

    private void OnDestroy()
    {
        if (ghost          != null) Destroy(ghost);
        if (ghostValidMat  != null) Destroy(ghostValidMat);
        if (ghostInvalidMat != null) Destroy(ghostInvalidMat);
        if (activeDrag == this) activeDrag = null;
    }

    private void Update()
    {
        if (isDragging)
        {
            HandleDrag();
            if (Input.GetMouseButtonUp(0)) EndDrag();
        }
        else if (activeDrag == null && Input.GetMouseButtonDown(0))
        {
            TryBeginDrag();
        }
    }

    // ─── Drag ─────────────────────────────────────────────────────────────────

    private void TryBeginDrag()
    {
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;
        if (!hit.transform.IsChildOf(transform) && hit.transform != transform) return;

        if (isPlaced)
        {
            grid.Remove(currentCells, placedOffset);
            isPlaced = false;
        }

        isDragging  = true;
        activeDrag  = this;
        // Başlangıç Y: parça şu an hangi katmanda?
        dragYLevel = Mathf.RoundToInt((transform.position.y - grid.Origin.y) / grid.Step);
        dragYLevel = Mathf.Max(0, dragYLevel);
        ghost.SetActive(true);
    }

    private void HandleDrag()
    {
        // Scroll → Y katmanı değiştir
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0.01f)       dragYLevel++;
        else if (scroll < -0.01f) dragYLevel = Mathf.Max(0, dragYLevel - 1);

        // R → Y ekseninde 90° döndür
        if (Input.GetKeyDown(KeyCode.R)) RotateY();

        // Mouse → XZ düzleminde takip et (dragYLevel yüksekliğinde)
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        float planeY = grid.Origin.y + dragYLevel * grid.Step;
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (plane.Raycast(ray, out float dist))
        {
            Vector3 hit = ray.GetPoint(dist);
            transform.position = new Vector3(hit.x, planeY, hit.z);
        }

        RefreshGhost();
    }

    private void EndDrag()
    {
        isDragging = false;
        activeDrag = null;
        ghost.SetActive(false);

        Vector3Int offset = grid.RootToOffset(transform.position);
        offset.y = dragYLevel;

        if (grid.TryPlace(currentCells, offset))
        {
            placedOffset       = offset;
            isPlaced           = true;
            transform.position = grid.OffsetToRoot(offset);
            GameManager.Instance?.CheckWin();
        }
        else
        {
            // Geçersiz → eve dön
            transform.position = HomePosition;
        }
    }

    // ─── Rotasyon ─────────────────────────────────────────────────────────────

    private void RotateY()
    {
        rotationStep = (rotationStep + 1) % 4;

        if (rotationStep == 0)
        {
            // 4 tam tur → orijinale dön
            currentCells = new List<Vector3Int>(holder.occupiedCells);
        }
        else
        {
            currentCells = ApplyRotationY(currentCells);
        }

        UpdateChildPositions();
    }

    // (x, y, z) → (-z, y, x) — Unity sol-el sistemi, Y etrafında 90° CCW
    private static List<Vector3Int> ApplyRotationY(List<Vector3Int> cells)
    {
        var result = new List<Vector3Int>(cells.Count);
        foreach (var c in cells)
            result.Add(new Vector3Int(-c.z, c.y, c.x));

        int minX = result.Min(c => c.x);
        int minZ = result.Min(c => c.z);
        for (int i = 0; i < result.Count; i++)
            result[i] -= new Vector3Int(minX, 0, minZ);

        return result;
    }

    // Child transform'larını currentCells'e göre yeniden konumlandır
    private void UpdateChildPositions()
    {
        var children = new List<Transform>();
        foreach (Transform t in transform) children.Add(t);
        if (children.Count != currentCells.Count) return;

        for (int i = 0; i < children.Count; i++)
            children[i].localPosition =
                (Vector3)currentCells[i] * grid.Step + Vector3.one * (grid.CellSize * 0.5f);
    }

    // ─── Ghost ────────────────────────────────────────────────────────────────

    private void BuildGhost()
    {
        ghost = new GameObject("Ghost_" + name);
        ghost.SetActive(false);

        ghostValidMat   = MakeTransparentMat(new Color(0.2f, 1f,   0.3f, 0.4f));
        ghostInvalidMat = MakeTransparentMat(new Color(1f,   0.2f, 0.2f, 0.4f));

        for (int i = 0; i < currentCells.Count; i++)
            AddGhostCube();
    }

    private void AddGhostCube()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(cube.GetComponent<Collider>());
        cube.transform.SetParent(ghost.transform, false);
        cube.transform.localScale = Vector3.one * grid.CellSize * 0.88f;
        ghostCubes.Add(cube.transform);
    }

    private void RefreshGhost()
    {
        // Rotasyon sonrası hücre sayısı değişmediyse bu bloğa girmez,
        // ama güvenli olması için sync ediyoruz
        while (ghostCubes.Count < currentCells.Count) AddGhostCube();
        while (ghostCubes.Count > currentCells.Count)
        {
            Destroy(ghostCubes[ghostCubes.Count - 1].gameObject);
            ghostCubes.RemoveAt(ghostCubes.Count - 1);
        }

        Vector3Int offset = grid.RootToOffset(transform.position);
        offset.y = dragYLevel;
        bool valid = grid.CanPlace(currentCells, offset);
        Material mat = valid ? ghostValidMat : ghostInvalidMat;

        for (int i = 0; i < ghostCubes.Count; i++)
        {
            ghostCubes[i].position = grid.CellToWorld(currentCells[i] + offset);
            ghostCubes[i].GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    // ─── Materyal ─────────────────────────────────────────────────────────────

    private static Material MakeTransparentMat(Color color)
    {
        var mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;
        return mat;
    }
}
