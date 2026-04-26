using System.Collections.Generic;
using UnityEngine;
using System.Linq;

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

    [HideInInspector] public float slotScale = 0.6f;

    private bool secondTouchConsumed;

    private static DraggablePiece activeDrag;
    public static bool IsDragging => activeDrag != null;

    public bool IsBeingDragged => isDragging;
    public bool IsPlaced       => isPlaced;

    public static void RequestRotateY() { if (activeDrag != null) activeDrag.RotateAroundY(); }
    public static void RequestRotateX() { if (activeDrag != null) activeDrag.RotateAroundX(); }

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
    }

    private void OnDestroy()
    {
        if (activeDrag == this) activeDrag = null;
    }

    private void Update()
    {
        if (isDragging)
        {
            HandleDrag();
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.IsLocked = true;
            if (Input.GetMouseButtonUp(0)) EndDrag();
        }
        else if (activeDrag == null && Input.GetMouseButtonDown(0))
        {
            TryBeginDrag();
        }
    }

    private void TryBeginDrag()
    {
        if (isPlaced) return;

        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;
        if (!hit.transform.IsChildOf(transform) && hit.transform != transform) return;

        isDragging          = true;
        activeDrag          = this;
        secondTouchConsumed = false;

        // Origin dunya pozisyonuna geri döndük
        dragPlane = new Plane(-mainCam.transform.forward, grid.Origin);
        Ray initRay = mainCam.ScreenPointToRay(Input.mousePosition);
        dragOffset3D = dragPlane.Raycast(initRay, out float initDist)
            ? transform.position - initRay.GetPoint(initDist)
            : Vector3.zero;

        transform.localScale = Vector3.one;
        if (CameraOrbit.Instance != null) CameraOrbit.Instance.IsLocked = true;
    }

    private void HandleDrag()
    {
        if (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Space)) 
            RotateAroundY();
            
        if (Input.GetKeyDown(KeyCode.E)) RotateAroundX();

        if (Input.touchCount >= 2)
        {
            if (!secondTouchConsumed) { RotateAroundY(); secondTouchConsumed = true; }
        }
        else secondTouchConsumed = false;

        Ray mouseRay = mainCam.ScreenPointToRay(Input.mousePosition);
        if (dragPlane.Raycast(mouseRay, out float dist))
            transform.position = mouseRay.GetPoint(dist) + dragOffset3D;

        Ray snapRay = mainCam.ScreenPointToRay(mainCam.WorldToScreenPoint(PieceWorldCenter()));
        if (grid.TryFindSnapOffset(currentCells, snapRay, grid.Step, out Vector3Int snapOff))
            transform.position = grid.OffsetToRoot(snapOff);
    }

    private void EndDrag()
    {
        isDragging = false;
        activeDrag = null;
        if (CameraOrbit.Instance != null) CameraOrbit.Instance.IsLocked = false;

        Vector3Int offset = grid.RootToOffset(transform.position);

        if (grid.TryPlace(currentCells, offset))
        {
            placedOffset       = offset;
            isPlaced           = true;
            transform.position = grid.OffsetToRoot(offset);
            transform.localScale = Vector3.one;
            GameManager.Instance?.CheckWin();
            LevelManager.Instance?.OnPiecePlaced(this);
        }
        else
        {
            transform.position   = HomePosition;
            transform.localScale = Vector3.one * slotScale;
        }
    }

    private Vector3 PieceWorldCenter()
    {
        if (currentCells == null || currentCells.Count == 0) return transform.position;
        float step = grid.Step;
        float half = grid.CellSize * 0.5f;

        int minX = currentCells.Min(c => c.x), maxX = currentCells.Max(c => c.x);
        int minY = currentCells.Min(c => c.y), maxY = currentCells.Max(c => c.y);
        int minZ = currentCells.Min(c => c.z), maxZ = currentCells.Max(c => c.z);

        Vector3 localCenter = new Vector3(
            (minX + maxX + 1) * 0.5f,
            (minY + maxY + 1) * 0.5f,
            (minZ + maxZ + 1) * 0.5f) * step;

        return transform.position + (transform.rotation * localCenter);
    }

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
}
