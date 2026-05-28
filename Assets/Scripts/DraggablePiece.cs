using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[RequireComponent(typeof(CubeShapeDataHolder))]
public class DraggablePiece : MonoBehaviour
{
    public Vector3 HomePosition { get; set; }

    /// <summary>
    /// Sürükleme iptal olunca (geçersiz bırakma) PieceCardUI tarafından atanır.
    /// </summary>
    [HideInInspector] public System.Action onDragCancelled;

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
    public Quaternion InitialRotation { get; set; } = Quaternion.identity;

    private bool secondTouchConsumed;
    private bool isSnapped;

    // Smooth transition from card slot variables
    private float dragLerpProgress = 1f;
    private Vector3 dragStartPos;
    private float dragStartScale = 1f;

    private static DraggablePiece activeDrag;
    public static bool IsDragging => activeDrag != null;

    public bool IsBeingDragged => isDragging;
    public bool IsPlaced       => isPlaced;

    public static void RequestRotateY() { if (activeDrag != null) activeDrag.RotateAroundY(); }
    public static void RequestRotateX() { if (activeDrag != null) activeDrag.RotateAroundX(); }

    /// <summary>
    /// PieceCardUI tarafından çağrılır — Physics.Raycast olmadan drag başlatır.
    /// Parça önizleme alanında dönüyor olabilir; rotasyon sıfırlanır.
    /// </summary>
    public void BeginDragFromCard(Ray ray)
    {
        if (isDragging || isPlaced) return;
        if (grid == null) grid = GridManager.Instance;
        if (grid == null) return;

        // Orijinal spawn rotasyonunu koru!
        currentRotation = InitialRotation;
        visualCells     = RotateCellsNoShift(holder.occupiedCells, currentRotation);
        currentCells    = RotateCellsNoShift(holder.occupiedCells, currentRotation);

        dragStartPos = transform.position;
        dragStartScale = transform.localScale.x;
        dragLerpProgress = 0f;

        isDragging          = true;
        activeDrag          = this;
        secondTouchConsumed = false;

        // Sürükleme düzlemini hazırla
        dragPlane = new Plane(-mainCam.transform.forward, grid.Origin);

        dragOffset3D         = Vector3.zero; // kart sürüklemesinde offset yok
        transform.localScale = Vector3.one * dragStartScale;
        UpdateChildPositions();

        if (CameraOrbit.Instance != null)
            CameraOrbit.Instance.IsLocked = true;

        Debug.Log($"[BM3D Debug] BeginDragFromCard tetiklendi. {gameObject.name}. currentRotation: {currentRotation.eulerAngles}, visualCells: {string.Join(", ", visualCells)}");
    }

    private void Awake()
    {
        holder          = GetComponent<CubeShapeDataHolder>();
        mainCam         = Camera.main;
        currentRotation = InitialRotation;
    }

    /// <summary>
    /// PieceCardUI tarafından parça ilk doğduğunda veya karta atandığında çağrılır.
    /// Çocuk objeleri (blokları) anında doğru hücre pozisyonlarına çeker ki UI sınır (bounds) hesaplamaları kusursuz olsun.
    /// </summary>
    public void InitializeForCard()
    {
        if (grid == null) grid = GridManager.Instance;
        if (holder == null) holder = GetComponent<CubeShapeDataHolder>();
        if (mainCam == null) mainCam = Camera.main;

        currentRotation = InitialRotation;
        visualCells  = RotateCellsNoShift(holder.occupiedCells, currentRotation);
        currentCells = RotateCellsNoShift(holder.occupiedCells, currentRotation);

        UpdateChildPositions();
    }

    private void Start()
    {
        if (grid == null) InitializeForCard();
        if (HomePosition == Vector3.zero) HomePosition = transform.position;

        Debug.Log($"[BM3D Debug] Start tetiklendi. {gameObject.name}. currentRotation: {currentRotation.eulerAngles}, visualCells: {string.Join(", ", visualCells)}");
    }

    private void OnDestroy()
    {
        if (activeDrag == this) activeDrag = null;
    }

    private void OnDisable()
    {
        if (grid != null) grid.ClearSnappedPreviewCells();
        if (grid != null) grid.ClearOccludingCells();
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

        // Sürükleme sırasında rotasyonu ekran hizalı (Quaternion.identity) olarak sabitliyoruz
        if (isDragging)
        {
            transform.rotation = Quaternion.identity;
            UpdateBoardCells();
            UpdateChildPositions();
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
        dragLerpProgress    = 1f; // Karttan olmadığı için animasyon yok

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
        {
            Vector3 targetDragPos = mouseRay.GetPoint(dist) + dragOffset3D;

            if (dragLerpProgress < 1f)
            {
                dragLerpProgress += Time.deltaTime * 7.5f; // ~130ms geçiş süresi
                if (dragLerpProgress > 1f) dragLerpProgress = 1f;

                transform.position = Vector3.Lerp(dragStartPos, targetDragPos, dragLerpProgress);
                transform.localScale = Vector3.Lerp(Vector3.one * dragStartScale, Vector3.one, dragLerpProgress);
            }
            else
            {
                transform.position = targetDragPos;
                transform.localScale = Vector3.one;
            }
        }

        // Geçiş esnasında snapping yapılmaz
        bool canSnap = dragLerpProgress >= 1f;
        Ray snapRay = mainCam.ScreenPointToRay(mainCam.WorldToScreenPoint(PieceWorldCenter()));
        bool wasSnapped = isSnapped;
        
        // Surukleme sirasinda HERZAMAN parcadan once gelen katmanlari gizle
        // (Snap olmadan da calisir - parcanin o anki pozisyonuna gore dinamik)
        // grid.UpdateOccludingCells(mainCam.transform.position, PieceWorldCenter()); // eski

        if (canSnap && grid.TryFindSnapOffset(currentCells, snapRay, grid.Step, out Vector3Int snapOff))
        {
            transform.position = grid.OffsetToRoot(snapOff);
            isSnapped = true;
            if (!wasSnapped)
            {
                Debug.Log($"[BM3D Debug] Grid Snap aktifle\u015fti! {gameObject.name}. snapOffset: {snapOff}, currentRotation: {currentRotation.eulerAngles}, currentCells: {string.Join(", ", currentCells)}");
            }

            // Snaplendigi yerdeki rehber grid hucrelerinin gorunurlugunu gecici olarak kapat
            var snappedBoardCells = new List<Vector3Int>();
            foreach (var c in currentCells) snappedBoardCells.Add(c + snapOff);
            grid.UpdateSnappedPreviewCells(snappedBoardCells);

            // Ilk mekanik: kamera-isini yaklasimi ile snap konumundaki hucreler gizlenir
            grid.UpdateOccludingCells(mainCam.transform.position, currentCells, snapOff);
        }
        else
        {
            isSnapped = false;
            if (wasSnapped)
            {
                Debug.Log($"[BM3D Debug] Grid Snap kayboldu (serbest surekleme). {gameObject.name}");
            }

            // Snap kaybolduysa gecici olarak gizlenmis grid hucrelerini geri goster
            grid.ClearSnappedPreviewCells();
            grid.ClearOccludingCells();
        }
    }

    private void EndDrag()
    {
        isDragging = false;
        activeDrag = null;
        if (CameraOrbit.Instance != null) CameraOrbit.Instance.IsLocked = false;

        // Sürükleme bittiği için snapten ve X-Ray occlusion'dan kaynaklı gizlenen gridleri geri açıyoruz
        if (grid != null) grid.ClearSnappedPreviewCells();
        if (grid != null) grid.ClearOccludingCells();

        Vector3Int offset = grid.RootToOffset(transform.position);

        Debug.Log($"[BM3D Debug] EndDrag tetiklendi. {gameObject.name}. isSnapped: {isSnapped}, offset: {offset}, currentRotation: {currentRotation.eulerAngles}, targetBoardRotation: {(LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null ? LevelManager.Instance.ActiveMainPiece.transform.rotation.eulerAngles.ToString() : "N/A")}");

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

                // TAM KONUMLANDIRMA VE HİZALAMA:
                Vector3 targetWorldPos = grid.CellToWorld(currentCells[i] + offset);
                Quaternion targetWorldRot = (LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null)
                    ? LevelManager.Instance.ActiveMainPiece.transform.rotation
                    : Quaternion.identity;

                child.position = targetWorldPos;
                child.rotation = targetWorldRot * currentRotation; // Kendi iç rotasyonunu (dikeylik vb.) koru!

                Debug.Log($"[BM3D Debug] Placed Child [{i}] of {gameObject.name}. worldPos: {child.position}, worldRot: {child.rotation.eulerAngles}, boardCell: {currentCells[i] + offset}");

                var rend  = child.GetComponentInChildren<Renderer>();
                Color col2 = rend != null ? GridManager.GetMaterialColor(rend.sharedMaterial ?? rend.material) : Color.white;
                grid.RegisterCell(currentCells[i] + offset, child.gameObject, col2);
            }

            placedOffset       = offset;
            isPlaced           = true;
            transform.position = grid.OffsetToRoot(offset);
            transform.localScale = Vector3.one;

            // 1. Çizgileri kontrol et; merge animasyonu bittikten sonra kazanma bildir
            //    onComplete lambda: merge bitti → GameManager'a sinyal ver (win panel açılır)
            var (cleared, bonusLines) = grid.CheckAndClearLines(onComplete: () =>
            {
                GameManager.Instance?.OnMergeAnimationComplete();
            });

            // 2. Puan ve kazanma kontrolü (merge animasyonu başladıysa win panel ertelenir)
            if (cleared > 0) GameManager.Instance?.OnLinesCleared(cleared, bonusLines);
            else              GameManager.Instance?.CheckWin();


            // 3. LevelManager'a parça yerleşimini bildir (böylece Game Over kontrolü temizlenmiş tahta üzerinden yapılır)
            LevelManager.Instance?.OnPiecePlaced(this);
        }
        else
        {
            // Kart sisteminde geri dönüş PieceCardUI.ReturnToPreview() üzerinden yönetilir
            onDragCancelled?.Invoke();
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
        Debug.Log($"[BM3D Debug] RotateAroundY tetiklendi. {gameObject.name}. currentRotation: {currentRotation.eulerAngles}, visualCells: {string.Join(", ", visualCells)}");
    }
 
    private void RotateAroundX()
    {
        currentRotation = Quaternion.Euler(90f, 0f, 0f) * currentRotation;
        RebuildCells();
        Debug.Log($"[BM3D Debug] RotateAroundX tetiklendi. {gameObject.name}. currentRotation: {currentRotation.eulerAngles}, visualCells: {string.Join(", ", visualCells)}");
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

        if (isDragging && isSnapped)
        {
            // SNAP DURUMUNDA: Blokları tahtadaki hücrelerin gerçek dunya pozisyonlarına eşitle!
            Vector3Int snapOff = grid.RootToOffset(transform.position);
            for (int i = 0; i < children.Count; i++)
            {
                Vector3 worldCellPos = grid.CellToWorld(currentCells[i] + snapOff);
                children[i].localPosition = transform.InverseTransformPoint(worldCellPos);
            }
        }
        else
        {
            // SÜRÜKLEME VEYA SLOT DURUMUNDA: Ekrana paralel / düz konumlandır
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
}
