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

    private List<Vector3Int> currentCells; // Board-aligned cell coordinates (used for snapping/placement)
    private List<Vector3Int> visualCells;  // Piece-aligned cell coordinates (used for visual child positions)
    private Quaternion currentRotation = Quaternion.identity;

    private bool isDragging;
    private bool isPlaced;
    private Vector3Int placedOffset;
    private Vector3 dragOffset3D;
    private Plane dragPlane;

    [HideInInspector] public float slotScale = 0.6f;

    private bool secondTouchConsumed;
    private bool isSnapped;

    private static DraggablePiece activeDrag;
    public static bool IsDragging => activeDrag != null;

    public bool IsBeingDragged => isDragging;
    public bool IsPlaced       => isPlaced;

    public static void RequestRotateY() { if (activeDrag != null) activeDrag.RotateAroundY(); }
    public static void RequestRotateX() { if (activeDrag != null) activeDrag.RotateAroundX(); }

    private void Awake()
    {
        holder          = GetComponent<CubeShapeDataHolder>();
        mainCam         = Camera.main;
        currentRotation = transform.rotation;
        
        visualCells  = RotateCellsNoShift(holder.occupiedCells, currentRotation);
        currentCells = RotateCellsNoShift(holder.occupiedCells, currentRotation);
    }

    private void Start()
    {
        grid = GridManager.Instance;
        if (grid == null) { Debug.LogError("GridManager bulunamadı!"); return; }
        if (HomePosition == Vector3.zero) HomePosition = transform.position;
        
        // Cocuk objeleri dogru pozisyona cek
        UpdateChildPositions();
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

        // Her kosulda (sürüklenirken veya slottayken) rotasyonunu tamamen sabitliyoruz
        if (!isPlaced)
        {
            transform.rotation = Quaternion.identity;
            UpdateBoardCells();
        }
    }

    private void LateUpdate()
    {
        // Slottayken parcanin pozisyonunu sabitliyoruz ki tahta donerken etkilenmesin
        if (!isDragging && !isPlaced)
        {
            if (HomePosition != Vector3.zero)
            {
                transform.position = HomePosition;
            }
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

        // Rotasyon her kosulda sabit kalacak
        transform.rotation = Quaternion.identity;
        UpdateBoardCells();

        Ray mouseRay = mainCam.ScreenPointToRay(Input.mousePosition);
        if (dragPlane.Raycast(mouseRay, out float dist))
            transform.position = mouseRay.GetPoint(dist) + dragOffset3D;

        Ray snapRay = mainCam.ScreenPointToRay(mainCam.WorldToScreenPoint(PieceWorldCenter()));
        if (grid.TryFindSnapOffset(currentCells, snapRay, grid.Step, out Vector3Int snapOff))
        {
            transform.position = grid.OffsetToRoot(snapOff);
            isSnapped = true;
        }
        else
        {
            isSnapped = false;
        }
    }

    private void EndDrag()
    {
        isDragging = false;
        activeDrag = null;
        if (CameraOrbit.Instance != null) CameraOrbit.Instance.IsLocked = false;

        Vector3Int offset = grid.RootToOffset(transform.position);

        if (isSnapped && grid.TryPlace(currentCells, offset))
        {
            var children = new List<Transform>();
            foreach (Transform t in transform) children.Add(t);
            for (int i = 0; i < currentCells.Count && i < children.Count; i++)
            {
                var child = children[i];
                if (CameraOrbit.Instance != null && CameraOrbit.Instance.pivot != null)
                {
                    child.SetParent(CameraOrbit.Instance.pivot, true);
                }
                else
                {
                    child.SetParent(null);
                }
                foreach (var col in child.GetComponents<Collider>()) col.enabled = false;

                var rend  = child.GetComponentInChildren<Renderer>();
                Color col2 = rend != null ? GridManager.GetMaterialColor(rend.sharedMaterial ?? rend.material) : Color.white;
                grid.RegisterCell(currentCells[i] + offset, child.gameObject, col2);
            }

            placedOffset       = offset;
            isPlaced           = true;
            transform.position = grid.OffsetToRoot(offset);
            transform.localScale = Vector3.one;
            GameManager.Instance?.CheckWin();
            LevelManager.Instance?.OnPiecePlaced(this);
            var (cleared, bonusLines) = grid.CheckAndClearLines();
            if (cleared > 0) GameManager.Instance?.OnLinesCleared(cleared, bonusLines);
        }
        else
        {
            transform.position   = HomePosition;
            transform.localScale = Vector3.one * slotScale;
        }
    }

    private Vector3 PieceWorldCenter()
    {
        if (visualCells == null || visualCells.Count == 0) return transform.position;
        float step = grid.Step;

        int minX = visualCells.Min(c => c.x), maxX = visualCells.Max(c => c.x);
        int minY = visualCells.Min(c => c.y), maxY = visualCells.Max(c => c.y);
        int minZ = visualCells.Min(c => c.z), maxZ = visualCells.Max(c => c.z);

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
        visualCells = RotateCellsNoShift(holder.occupiedCells, currentRotation);
        UpdateChildPositions();
        UpdateBoardCells();
    }

    public void UpdateBoardCells()
    {
        if (holder == null || grid == null) return;

        Transform boardTrans = LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null
            ? LevelManager.Instance.ActiveMainPiece.transform
            : null;

        if (boardTrans != null)
        {
            Quaternion targetBoardRotation = Quaternion.identity;
            if (CameraOrbit.Instance != null)
            {
                // Eger CameraOrbit varsa, hedef acisini aliyoruz (interpolasyondan bagimsiz olarak hep 90'in katlaridir)
                targetBoardRotation = Quaternion.Euler(0f, CameraOrbit.Instance.TargetYaw, 0f);
            }
            else
            {
                targetBoardRotation = boardTrans.rotation;
            }

            // Kupun dunya rotasyonunun tersi ile kendi dunya rotasyonumuz ve ic rotasyonumuz carpildiginda
            // parcanin kup referans sistemindeki goreli rotasyonunu elde ederiz
            Quaternion boardRelativeRotation = Quaternion.Inverse(targetBoardRotation) * transform.rotation * currentRotation;
            currentCells = RotateCellsNoShift(holder.occupiedCells, boardRelativeRotation);
        }
        else
        {
            currentCells = RotateCellsNoShift(holder.occupiedCells, currentRotation);
        }
    }

    private List<Vector3Int> RotateCellsNoShift(List<Vector3Int> cells, Quaternion q)
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
        return result;
    }

    private void UpdateChildPositions()
    {
        var children = new List<Transform>();
        foreach (Transform t in transform) children.Add(t);
        if (children.Count != visualCells.Count) return;
        float half = grid.CellSize * 0.5f;
        for (int i = 0; i < children.Count; i++)
        {
            var c = visualCells[i];
            children[i].localPosition = new Vector3(
                c.x * grid.Step + half,
                c.y * grid.Step + half,
                c.z * grid.Step + half);
        }
    }
}
