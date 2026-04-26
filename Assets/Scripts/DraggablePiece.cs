using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CubeShapeDataHolder))]
public class DraggablePiece : MonoBehaviour
{
    public Vector3 HomePosition { get; set; }

    private CubeShapeDataHolder holder;
    private GridManager grid;
    private Camera mainCam;

    private List<Vector3Int> currentCells;
    private Quaternion currentRotation = Quaternion.identity;

    private bool isDragging;
    private bool isPlaced;
    private Vector3Int placedOffset;
    private Vector3 dragOffset3D;
    private Plane dragPlane;

    // Ghost
    private GameObject ghost;
    private List<Transform> ghostCubes = new List<Transform>();
    private Material ghostValidMat;
    private Material ghostInvalidMat;

    // Multi-touch rotate
    private bool secondTouchConsumed;

    private static DraggablePiece activeDrag;
    public static bool IsDragging => activeDrag != null;

    public bool IsBeingDragged => isDragging;
    public bool IsPlaced       => isPlaced;

    public static void RequestRotateY() { if (activeDrag != null) activeDrag.RotateAroundY(); }
    public static void RequestRotateX() { if (activeDrag != null) activeDrag.RotateAroundX(); }

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        holder       = GetComponent<CubeShapeDataHolder>();
        mainCam      = Camera.main;
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
        if (ghost           != null) Destroy(ghost);
        if (ghostValidMat   != null) Destroy(ghostValidMat);
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

        if (isPlaced) { grid.Remove(currentCells, placedOffset); isPlaced = false; }

        isDragging          = true;
        activeDrag          = this;
        secondTouchConsumed = false;

        // Tüm parçalar grid origin'den geçen aynı referans düzleminde hareket eder
        dragPlane = new Plane(-mainCam.transform.forward, grid.Origin);
        Ray initRay = mainCam.ScreenPointToRay(Input.mousePosition);
        dragOffset3D = dragPlane.Raycast(initRay, out float initDist)
            ? transform.position - initRay.GetPoint(initDist)
            : Vector3.zero;

        ghost.SetActive(true);
    }

    private void HandleDrag()
    {
        if (Input.GetKeyDown(KeyCode.R)) RotateAroundY();
        if (Input.GetKeyDown(KeyCode.E)) RotateAroundX();

        if (Input.touchCount >= 2)
        {
            if (!secondTouchConsumed) { RotateAroundY(); secondTouchConsumed = true; }
        }
        else secondTouchConsumed = false;

        // Serbest hareket
        // Serbest hareket (drag plane boyunca)
        Ray mouseRay = mainCam.ScreenPointToRay(Input.mousePosition);
        if (dragPlane.Raycast(mouseRay, out float dist))
            transform.position = mouseRay.GetPoint(dist) + dragOffset3D;

        // Kamera açısından parça merkezi üzerinden ekran-uzay snap
        Ray snapRay = mainCam.ScreenPointToRay(mainCam.WorldToScreenPoint(PieceWorldCenter()));
        if (grid.TryFindSnapOffset(currentCells, snapRay, grid.Step, out Vector3Int snapOff))
            transform.position = grid.OffsetToRoot(snapOff);

        RefreshGhost();
    }

    private void EndDrag()
    {
        isDragging = false;
        activeDrag = null;
        ghost.SetActive(false);

        Vector3Int offset = grid.RootToOffset(transform.position);

        if (grid.TryPlace(currentCells, offset))
        {
            placedOffset       = offset;
            isPlaced           = true;
            transform.position = grid.OffsetToRoot(offset);
            GameManager.Instance?.CheckWin();
            LevelManager.Instance?.OnPiecePlaced(this);
        }
        else
        {
            transform.position = HomePosition;
        }
    }

    private Vector3 PieceWorldCenter()
    {
        float half = grid.CellSize * 0.5f;
        Vector3 sum = Vector3.zero;
        foreach (var c in currentCells)
            sum += new Vector3(c.x * grid.Step + half, c.y * grid.Step + half, c.z * grid.Step + half);
        return transform.position + sum / currentCells.Count;
    }

    // ─── Rotasyon ─────────────────────────────────────────────────────────────

    private void RotateAroundY()
    {
        currentRotation = Quaternion.Euler(0f, 90f, 0f) * currentRotation;
        RebuildCells();
    }

    private void RotateAroundX()
    {
        currentRotation = Quaternion.Euler(90f, 0f, 0f) * currentRotation;
        RebuildCells();
    }

    private void RebuildCells()
    {
        currentCells = RotateCells(holder.occupiedCells, currentRotation);
        UpdateChildPositions();
        while (ghostCubes.Count < currentCells.Count) AddGhostCube();
        while (ghostCubes.Count > currentCells.Count)
        {
            Destroy(ghostCubes[ghostCubes.Count - 1].gameObject);
            ghostCubes.RemoveAt(ghostCubes.Count - 1);
        }
    }

    private static List<Vector3Int> RotateCells(List<Vector3Int> cells, Quaternion q)
    {
        var result = new List<Vector3Int>(cells.Count);
        foreach (var c in cells)
        {
            Vector3 v = q * new Vector3(c.x, c.y, c.z);
            result.Add(new Vector3Int(
                Mathf.RoundToInt(v.x),
                Mathf.RoundToInt(v.y),
                Mathf.RoundToInt(v.z)));
        }

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        foreach (var c in result)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.z < minZ) minZ = c.z;
        }
        for (int i = 0; i < result.Count; i++)
            result[i] -= new Vector3Int(minX, minY, minZ);

        return result;
    }

    private void UpdateChildPositions()
    {
        var children = new List<Transform>();
        foreach (Transform t in transform) children.Add(t);
        if (children.Count != currentCells.Count) return;
        float half = grid.CellSize * 0.5f;
        for (int i = 0; i < children.Count; i++)
        {
            var c = currentCells[i];
            children[i].localPosition = new Vector3(
                c.x * grid.Step + half,
                c.y * grid.Step + half,
                c.z * grid.Step + half);
        }
    }

    // ─── Ghost ────────────────────────────────────────────────────────────────

    private void BuildGhost()
    {
        ghost = new GameObject("Ghost_" + name);
        ghost.SetActive(false);
        ghostValidMat   = MakeTransparentMat(new Color(0.2f, 1f,   0.3f, 0.4f));
        ghostInvalidMat = MakeTransparentMat(new Color(1f,   0.2f, 0.2f, 0.4f));
        for (int i = 0; i < currentCells.Count; i++) AddGhostCube();
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
        Vector3Int offset = grid.RootToOffset(transform.position);
        bool valid = grid.CanPlace(currentCells, offset);
        Material mat = valid ? ghostValidMat : ghostInvalidMat;

        for (int i = 0; i < ghostCubes.Count; i++)
        {
            ghostCubes[i].position = grid.CellToWorld(currentCells[i] + offset);
            ghostCubes[i].GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

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
