using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Ekranın altındaki parça slot kartını yönetir.
/// Atanan 3D parçayı ana kamera önünde fiziksel olarak konumlandırarak,
/// kartın üzerinde gerçek bir 3D obje olarak görünmesini sağlar.
/// Karta basılınca sürüklemeyi başlatır.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PieceCardUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("UI Referansları")]
    public RawImage    previewImage;    // Artık kullanılmıyor (şeffaf/gizli tutulur)
    public GameObject  emptyOverlay;   // Kart boşken gösterilecek "?" veya boş görsel

    [Header("Preview Kamera (Kullanılmıyor, pasif)")]
    public Camera previewCam;

    // ── Runtime durum ─────────────────────────────────────────────────────────
    private GameObject     piece3D;
    private DraggablePiece draggable;
    private int            slotIndex;
    private bool           initialized;
    private bool           isDraggingOut;

    private Vector3        localVisualCenter = Vector3.zero;
    private float          localVisualRadius = 1f;

    public bool HasPiece => piece3D != null;

    private void Awake()
    {
        var arf = GetComponent<AspectRatioFitter>();
        if (arf == null) arf = gameObject.AddComponent<AspectRatioFitter>();
        arf.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        arf.aspectRatio = 1f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Başlatma
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LevelManager tarafından LoadLevel öncesinde çağrılır.
    /// </summary>
    public void Init(int index)
    {
        if (initialized) return;
        initialized = true;

        slotIndex = index;
        isDraggingOut = false; // Sürükleme durumunu sıfırla

        // Eski render texture ve preview kamerasını devre dışı bırak
        if (previewImage != null)
        {
            previewImage.enabled = false;
        }

        if (previewCam != null)
        {
            previewCam.enabled = false;
            previewCam.gameObject.SetActive(false);
        }

        UpdateVisuals();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Parça yönetimi
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Bu karta 3D parçayı ata ve önizleme alanına yerleştir.</summary>
    public void AssignPiece(GameObject piece)
    {
        isDraggingOut = false; // Yeni parça atandığında sürükleme durumunu kesinlikle sıfırla!
        
        // GÜVENLİK KONTROLÜ: Eğer kartta zaten aktif bir parça varsa ve yenisi atanıyorsa, çakışmayı önlemek için eskiyi yok et!
        if (piece3D != null && piece3D != piece)
        {
            Destroy(piece3D);
        }

        piece3D   = piece;
        draggable = piece != null ? piece.GetComponent<DraggablePiece>() : null;

        if (piece3D != null)
            PlaceInPreview();

        UpdateVisuals();
    }

    /// <summary>Parça başarıyla yerleştirildi → kartı boşalt.</summary>
    public void ClearPiece()
    {
        isDraggingOut = false; // Kart boşaltıldığında sürükleme durumunu sıfırla

        // GÜVENLİK KONTROLÜ: Eğer parça yerleştirilmeden kart temizleniyorsa (örneğin seviye sıfırlanırken veya iptal durumunda), eski parçayı yok et!
        if (piece3D != null)
        {
            if (draggable == null || !draggable.IsPlaced)
            {
                Destroy(piece3D);
            }
        }

        piece3D   = null;
        draggable = null;
        UpdateVisuals();
    }

    /// <summary>Sürükleme iptal oldu → parçayı önizleme pozisyonuna geri al.</summary>
    public void ReturnToPreview()
    {
        isDraggingOut = false;
        if (piece3D != null)
            PlaceInPreview();
        UpdateVisuals();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pozisyonlama ve Hizalama (LateUpdate ile titremeyi önler)
    // ─────────────────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (HasPiece && !isDraggingOut && piece3D != null)
        {
            UpdatePiecePositionAndRotation();
        }
    }

    private void PlaceInPreview()
    {
        if (piece3D == null || Camera.main == null) return;

        // Parça ilk doğduğunda çocuk blok pozisyonlarının sıfır kalmasını önlemek için anında hücre konumlarını hesaplıyoruz
        if (draggable != null)
        {
            draggable.InitializeForCard();
        }

        // Boyut ölçümü yapabilmek için geçici olarak varsayılan değerlere çekiyoruz
        piece3D.transform.position   = Vector3.zero;
        piece3D.transform.rotation   = Quaternion.identity;
        piece3D.transform.localScale = Vector3.one;

        // Parçanın renderers sınırlarını hesaplayıp lokal merkezini ve lokal yarıçapını buluyoruz
        var renderers = piece3D.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            localVisualCenter = piece3D.transform.InverseTransformPoint(b.center);
            localVisualRadius = b.extents.magnitude;
            if (localVisualRadius < 0.001f) localVisualRadius = 1f;
        }
        else
        {
            localVisualCenter = Vector3.zero;
            localVisualRadius = 1f;
        }

        UpdatePiecePositionAndRotation();
    }

    private void UpdatePiecePositionAndRotation()
    {
        if (piece3D == null || Camera.main == null) return;

        float depth = 5f; // Kamera önündeki derinlik mesafesi

        // Kartın pivot kaymalarını hesaba katarak tam merkez noktasını buluyoruz
        RectTransform rect = GetComponent<RectTransform>();
        Vector2 localCenter = new Vector2(rect.rect.width * (0.5f - rect.pivot.x), rect.rect.height * (0.5f - rect.pivot.y));
        Vector3 centerWorldPos = rect.TransformPoint(localCenter);
        
        // Merkez noktasını ekran koordinatına çeviriyoruz
        Vector3 cardScreenPos = RectTransformUtility.WorldToScreenPoint(null, centerWorldPos);
        cardScreenPos.z = depth;

        Vector3 targetWorldPos = Camera.main.ScreenToWorldPoint(cardScreenPos);

        // Dinamik olarak kartın o anki kamera açısına göre dünya ölçeğindeki yüksekliğini hesaplıyoruz
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float cardScreenHeight = Vector3.Distance(corners[0], corners[1]);

        Vector3 bottomWorld = Camera.main.ScreenToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 topWorld = Camera.main.ScreenToWorldPoint(new Vector3(0f, cardScreenHeight, depth));
        float cardWorldHeight = Vector3.Distance(bottomWorld, topWorld);

        // Parça kart yüksekliğinin %65'ini kaplayacak şekilde dinamik ölçeklenir
        float targetDiameter = cardWorldHeight * 0.65f;
        float targetRadius = targetDiameter * 0.5f;
        float scale = targetRadius / localVisualRadius;

        // Kameranın başlangıçtaki izometrik bakış açısını temel alarak parçayı mükemmel şekilde hizalıyoruz.
        // Böylece kart içindeki parça, ana tahtadaki bloklarla birebir aynı 3D açıya ve gölgelendirmeye sahip olur.
        float elev = 28f;
        float azim = 25f;
        if (CameraOrbit.Instance != null)
        {
            elev = CameraOrbit.Instance.startElevation;
            azim = CameraOrbit.Instance.startAzimuth;
        }
        Quaternion baseIsoRotation = Quaternion.Euler(elev, azim, 0f);
        Quaternion targetRotation = Camera.main.transform.rotation * Quaternion.Inverse(baseIsoRotation);

        piece3D.transform.rotation = targetRotation;
        piece3D.transform.position = targetWorldPos - (targetRotation * localVisualCenter * scale);
        piece3D.transform.localScale = Vector3.one * scale;

        // DraggablePiece'in HomePosition'ını güncel tut ki LateUpdate çakışması olmasın
        if (draggable != null)
        {
            draggable.HomePosition = piece3D.transform.position;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pointer olayları
    // ─────────────────────────────────────────────────────────────────────────

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!HasPiece || draggable == null) return;
        if (DraggablePiece.IsDragging)      return;

        isDraggingOut = true;

        // 3D parça için sürüklemeyi başlat
        Ray ray = Camera.main.ScreenPointToRay(eventData.position);
        draggable.BeginDragFromCard(ray);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isDraggingOut = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Yardımcılar
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateVisuals()
    {
        if (previewImage != null) previewImage.enabled = false;
        if (emptyOverlay != null) emptyOverlay.SetActive(!HasPiece);

        // Kartta parça varken beyaz arka planı yarı şeffaf yapar, böylece 3D obje öne çıkar
        var cardBg = GetComponent<Image>();
        if (cardBg != null)
        {
            var col = cardBg.color;
            col.a = HasPiece ? 0.15f : 1.0f;
            cardBg.color = col;
        }
    }

    private void OnDestroy()
    {
        if (previewImage != null && previewImage.material != null && previewImage.material.shader.name == "UI/GlowOutline")
        {
            Destroy(previewImage.material);
        }
    }
}
