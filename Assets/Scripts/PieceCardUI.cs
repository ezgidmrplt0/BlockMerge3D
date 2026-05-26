using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Ekranın altındaki parça slot kartını yönetir.
/// Atanan 3D parçayı off-screen bir kamera ile RenderTexture'a render eder,
/// dönen önizleme olarak gösterir. Karta basılınca sürüklemeyi başlatır.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PieceCardUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("UI Referansları")]
    public RawImage    previewImage;    // RenderTexture'ı gösterecek UI elemanı
    public GameObject  emptyOverlay;   // Kart boşken gösterilecek "?" veya boş görsel

    [Header("Preview Kamera (CanvasSetup ile atanır)")]
    public Camera previewCam;

    // ── Sabitler ─────────────────────────────────────────────────────────────
    // Her kart x=-10000'den başlayarak 20 birim aralıklarla konumlanır.
    // Ana kameranın far clip'i (1000) bu mesafeye ulaşmaz → katman gerekmez.
    private const float PREVIEW_BASE_X    = -10000f;
    private const float PREVIEW_SPACING   =    20f;
    private const float PREVIEW_CAM_HEIGHT=     3.5f;
    private const float PREVIEW_CAM_DEPTH =    -5.5f;

    // ── Runtime durum ─────────────────────────────────────────────────────────
    private RenderTexture  rt;
    private GameObject     piece3D;
    private DraggablePiece draggable;
    private int            slotIndex;
    private bool           initialized;
    private bool           isDraggingOut;
    private Vector3        previewWorldPos;

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
    /// Birden fazla kez çağrılsa bile sadece ilk çalışmada init yapar.
    /// </summary>
    public void Init(int index)
    {
        if (initialized) return;
        initialized = true;

        slotIndex       = index;
        previewWorldPos = new Vector3(PREVIEW_BASE_X - index * PREVIEW_SPACING, 0f, 0f);

        // Premium 512x512 çözünürlük ve 4x Anti-aliasing kenar yumuşatma desteği ile şeffaf format (ARGB32)
        rt = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        rt.Create();

        if (previewImage != null)
        {
            previewImage.texture = rt;
            
            // Glow outline materyali oluştur ve ata
            var shader = Shader.Find("UI/GlowOutline");
            if (shader != null)
            {
                var glowMat = new Material(shader);
                glowMat.SetColor("_OutlineColor", new Color(1f, 1f, 1f, 1f)); // Tam opak parlak beyaz
                glowMat.SetFloat("_OutlineWidth", 0.035f); // Genişlik (Kalınlaştırıldı)
                glowMat.SetFloat("_GlowPower", 1.2f);       // Işıma gücü (Daha yoğun ve belirgin olması için düşürüldü)
                previewImage.material = glowMat;
            }
        }

        // Preview kamerasını yapılandır (Arka plan şeffaf olacak şekilde)
        if (previewCam != null)
        {
            previewCam.targetTexture   = rt;
            previewCam.clearFlags      = CameraClearFlags.SolidColor;
            previewCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCam.nearClipPlane   = 0.1f;
            previewCam.farClipPlane    = 30f;
            previewCam.fieldOfView     = 38f;
            RepositionPreviewCam();
        }

        UpdateVisuals();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Parça yönetimi
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Bu karta 3D parçayı ata ve önizleme alanına yerleştir.</summary>
    public void AssignPiece(GameObject piece)
    {
        piece3D   = piece;
        draggable = piece != null ? piece.GetComponent<DraggablePiece>() : null;

        if (piece3D != null)
            PlaceInPreview();

        UpdateVisuals();
    }

    /// <summary>Parça başarıyla yerleştirildi → kartı boşalt.</summary>
    public void ClearPiece()
    {
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
    // Döndürme
    // ─────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (HasPiece && !isDraggingOut && piece3D != null)
        {
            // Karttaki parçayı tamamen sabit (Quaternion.identity) tutarak tahtanın dönmesinden etkilenmemesini sağlıyoruz
            piece3D.transform.rotation = Quaternion.identity;

            if (previewCam != null)
            {
                // Kameranın bakış açısını ana kamerayla eşitliyoruz (sabit izometrik bakış)
                previewCam.transform.rotation = Camera.main.transform.rotation;
                FitCameraTopiece();
            }
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

    private void PlaceInPreview()
    {
        if (piece3D == null) return;

        piece3D.transform.position   = previewWorldPos;
        piece3D.transform.rotation   = Quaternion.identity;
        piece3D.transform.localScale = Vector3.one;

        if (previewCam != null)
        {
            previewCam.transform.rotation = Camera.main.transform.rotation;
        }
        FitCameraTopiece();
    }

    private void FitCameraTopiece()
    {
        if (previewCam == null || piece3D == null) return;

        var renderers = piece3D.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { RepositionPreviewCam(); return; }

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        // Kamerayı parçanın boyutuna göre otomatik uzaklaştır (Daha büyük görünmesi için 1.15f çarpanı yapıldı)
        float radius  = b.extents.magnitude;
        float halfFov = previewCam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float dist    = (radius / Mathf.Tan(halfFov)) * 1.15f;

        Vector3 center = b.center;
        // Kamera ana kameranın bakış açısını kullandığı için, bakış yönünün tersine dist kadar uzaklaştırarak ortalıyoruz
        previewCam.transform.position = center - previewCam.transform.forward * dist;
    }

    private void RepositionPreviewCam()
    {
        if (previewCam == null) return;
        previewCam.transform.position = previewWorldPos + new Vector3(0f, PREVIEW_CAM_HEIGHT, PREVIEW_CAM_DEPTH);
        previewCam.transform.LookAt(previewWorldPos + Vector3.up * 0.5f);
    }

    private void UpdateVisuals()
    {
        if (previewImage != null) previewImage.enabled = HasPiece;
        if (emptyOverlay != null) emptyOverlay.SetActive(!HasPiece);
    }

    private void OnDestroy()
    {
        if (previewCam != null) previewCam.targetTexture = null;
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (previewImage != null && previewImage.material != null && previewImage.material.shader.name == "UI/GlowOutline")
        {
            Destroy(previewImage.material);
        }
    }
}
