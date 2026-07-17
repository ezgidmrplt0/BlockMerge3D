using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using DG.Tweening;

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
    public Vector3Int PlacedOffset => placedOffset;
    private Vector3 dragOffset3D;
    private Plane dragPlane;

    [HideInInspector] public float slotScale = 0.6f;
    public Quaternion InitialRotation { get; set; } = Quaternion.identity;

    // Bu parçanın TÜRÜ — LevelManager.SpawnPieceAtIndex'te spawn anında set edilir. Yerleştirme
    // sırasında grid.AddCell'e doğrudan bu geçirilir (bkz. EndDrag) — eskiden olduğu gibi
    // renderer'ın uygulanmış materyalini LevelManager.PieceMaterials'la karşılaştırarak SONRADAN
    // yeniden bulunmaya çalışılmaz (kırılgandı, iki palet slotu aynı materyali paylaşırsa/isim
    // eşleşmesi tesadüfen tutarsa yanlış tür bulunabilirdi).
    public int SpeciesIndex { get; set; } = -1;

    private bool secondTouchConsumed;
    private bool isSnapped;

    // Smooth transition from card slot variables
    private float dragLerpProgress = 1f;
    private Vector3 dragStartPos;
    private float dragStartScale = 1f;
    private Vector3 lastPosition;

    public static DraggablePiece activeDrag;
    public static bool IsDragging => activeDrag != null;

    public bool IsBeingDragged => isDragging;
    public bool IsPlaced       => isPlaced;
    public List<Vector3Int> CurrentCells => currentCells;

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

        // Kart önizlemesinde PieceCardUI tarafından kapatılan FaceCamera'yı geri aç —
        // artık gerçek oyun kamerasına göre yüzünü döndürmesi gerekiyor.
        foreach (var fc in GetComponentsInChildren<FaceCamera>(true))
            fc.enabled = true;

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
        lastPosition        = transform.position;
        if (grid != null) grid.StartVisualFocus(this);

        // Sürükleme düzlemini hazırla
        // Panel modunda aktif katmanın Y düzlemine kilitle
        if (CameraOrbit.Instance != null && CameraOrbit.Instance.IsInPanelMode && GridManager.Instance != null)
        {
            Vector3 layerWorldPos = GridManager.Instance.CellToWorld(
                new Vector3Int(0, GridManager.Instance.ActiveLayerY, 0));
            dragPlane = new Plane(Vector3.up, layerWorldPos);
        }
        else
        {
            dragPlane = new Plane(-mainCam.transform.forward, grid.Origin);
        }
        transform.localScale = Vector3.one * dragStartScale;
        UpdateChildPositions();

        DOTween.Kill(transform);
        transform.DOPunchScale(new Vector3(-0.08f, 0.15f, -0.08f), 0.22f, 8, 0.5f);

        if (CameraOrbit.Instance != null)
            CameraOrbit.Instance.IsLocked = true;

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
        lastPosition        = transform.position;
        if (grid != null) grid.StartVisualFocus(this);

        // Origin dunya pozisyonuna geri döndük
        // Panel modunda aktif katmanın Y düzlemine kilitle
        if (CameraOrbit.Instance != null && CameraOrbit.Instance.IsInPanelMode && GridManager.Instance != null)
        {
            Vector3 layerWorldPos = GridManager.Instance.CellToWorld(
                new Vector3Int(0, GridManager.Instance.ActiveLayerY, 0));
            dragPlane = new Plane(Vector3.up, layerWorldPos);
        }
        else
        {
            dragPlane = new Plane(-mainCam.transform.forward, grid.Origin);
        }
        Ray initRay = mainCam.ScreenPointToRay(Input.mousePosition);
        dragOffset3D = dragPlane.Raycast(initRay, out float initDist)
            ? transform.position - initRay.GetPoint(initDist)
            : Vector3.zero;

        transform.localScale = Vector3.one;
        
        DOTween.Kill(transform);
        transform.DOPunchScale(new Vector3(-0.08f, 0.15f, -0.08f), 0.22f, 8, 0.5f);

        if (CameraOrbit.Instance != null) CameraOrbit.Instance.IsLocked = true;
    }

    private void HandleDrag()
    {
        if (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Space)) 
            RotateAroundY();

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
                
                // Velocity-based Squash & Stretch during active dragging
                Vector3 moveDelta = transform.position - lastPosition;
                if (moveDelta.magnitude > 0.001f && !isSnapped)
                {
                    float speed = moveDelta.magnitude / Time.deltaTime;
                    float factor = Mathf.Clamp01(speed / 15f) * 0.12f;
                    transform.localScale = new Vector3(1f - factor * 0.5f, 1f + factor, 1f - factor * 0.5f);
                }
                else if (!isSnapped && !DOTween.IsTweening(transform))
                {
                    transform.localScale = Vector3.one;
                }
            }
            lastPosition = transform.position;
        }

        // Geçiş esnasında snapping yapılmaz
        bool canSnap = dragLerpProgress >= 1f;
        Ray snapRay = mainCam.ScreenPointToRay(mainCam.WorldToScreenPoint(PieceWorldCenter()));
        bool wasSnapped = isSnapped;

        if (canSnap && grid.TryFindSnapOffset(currentCells, snapRay, grid.Step, out Vector3Int snapOff))
        {
            transform.position = grid.OffsetToRoot(snapOff);
            isSnapped = true;
            if (!wasSnapped)
            {
                // Snap pop animation: squash and stretch click effect!
                DOTween.Kill(transform);
                transform.DOPunchScale(new Vector3(0.08f, -0.1f, 0.08f), 0.15f, 10, 1f);
            }

            // Snaplendigi yerdeki rehber grid hucrelerinin gorunurlugunu gecici olarak kapat
            var snappedBoardCells = new List<Vector3Int>();
            foreach (var c in currentCells) snappedBoardCells.Add(c + snapOff);
            grid.UpdateSnappedPreviewCells(snappedBoardCells);

            // Ilk mekanik: kamera-isini yaklasimi ile snap konumundaki hucreler gizlenir
            grid.UpdateOccludingCells(mainCam.transform.position, currentCells, snapOff);

            // Görsel odak sistemi güncellemesi
            grid.UpdateVisualFocus(this, true, snapOff);

        }
        else
        {
            isSnapped = false;
            if (wasSnapped)
            {
                DOTween.Kill(transform);
                transform.localScale = Vector3.one;
            }

            // Snap kaybolduysa gecici olarak gizlenmis grid hucrelerini geri goster
            grid.ClearSnappedPreviewCells();
            grid.ClearOccludingCells();

            // Görsel odak sistemi güncellemesi (snap yok)
            grid.UpdateVisualFocus(this, false, Vector3Int.zero);

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
        if (grid != null) grid.StopVisualFocus(this);

        Vector3Int offset = grid.RootToOffset(transform.position);


        if (isSnapped && grid.TryPlace(currentCells, offset))
        {
            var children = new List<Transform>();
            foreach (Transform t in transform) children.Add(t);

            // Parçanın "kart atma" yerleşim animasyonu bitene kadar win paneli açılmasın —
            // önceden bu animasyon (~0.3s) devam ederken merge'de çizgi yoksa panel anında açılıyordu.
            GameManager.Instance?.BeginBlockingAnimation();
            int settleRemaining = Mathf.Min(currentCells.Count, children.Count);
            System.Action onChildSettled = () =>
            {
                if (--settleRemaining <= 0) GameManager.Instance?.EndBlockingAnimation();
            };

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

                Vector3 targetWorldPos = grid.CellToWorld(currentCells[i] + offset);
                Quaternion targetWorldRot = (LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null)
                    ? LevelManager.Instance.ActiveMainPiece.transform.rotation
                    : Quaternion.identity;
                Quaternion targetWorldRotFinal = targetWorldRot * currentRotation;

                // Kart atıyormuş gibi hissettiren başlangıç pozisyonu, rotasyonu ve ölçeği
                Vector3 throwOffset = Vector3.up * 1.5f - (mainCam != null ? mainCam.transform.forward * 0.5f : Vector3.zero);
                child.position = targetWorldPos + throwOffset;
                child.rotation = targetWorldRotFinal * Quaternion.Euler(30f, -45f, 15f);
                child.localScale = Vector3.one * 0.6f;

                // DOTween kart atma animasyonu
                float duration = 0.3f;
                child.DOMove(targetWorldPos, duration).SetEase(Ease.OutBack);
                child.DORotateQuaternion(targetWorldRotFinal, duration).SetEase(Ease.OutBack);

                Transform finalChild = child;
                // Ezgi: hücreye tam oturmuş görünmesi için yerleşim sonrası ölçek hücre
                // boyutunun biraz üzerinde (+0.1) tutuluyor.
                child.DOScale(Vector3.one * 1.1f, duration).SetEase(Ease.OutBack).OnComplete(() =>
                {
                    // Yerleştiğinde tatmin edici esneme/sıkışma etkisi
                    finalChild.DOPunchScale(new Vector3(0.08f, -0.15f, 0.08f), 0.32f, 10, 1f).SetEase(Ease.OutQuad);
                    onChildSettled();
                });

                var rend  = child.GetComponentInChildren<Renderer>();
                Material usedMat = rend != null ? (rend.sharedMaterial ?? rend.material) : null;
                Color col2 = GridManager.GetMaterialColor(usedMat); // kozmetik: hâlâ VFX/tint için tutuluyor

                // Tür (eşleşme anahtarı) spawn anında zaten biliniyordu — BumpAnimation'ı devre dışı bırakıyoruz (kendi animasyonumuz var)
                grid.AddCell(currentCells[i] + offset, child.gameObject, col2, SpeciesIndex, animateBump: false);
            }

            placedOffset       = offset;
            isPlaced           = true;
            transform.position = grid.OffsetToRoot(offset);
            transform.localScale = Vector3.one;

            // 1. Çizgileri kontrol et; merge animasyonu bittikten sonra kazanma bildir
            //    onComplete lambda: merge bitti → GameManager'a sinyal ver (win panel açılır)
            GameManager.Instance?.BeginBlockingAnimation();
            var (cleared, bonusLines) = grid.CheckAndClearLines(onComplete: () =>
            {
                GameManager.Instance?.EndBlockingAnimation();
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