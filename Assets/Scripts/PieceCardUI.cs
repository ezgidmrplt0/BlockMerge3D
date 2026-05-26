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

        // RenderTexture oluştur
        rt = new RenderTexture(256, 256, 16, RenderTextureFormat.Default);
        rt.antiAliasing = 2;
        rt.Create();

        if (previewImage != null)
            previewImage.texture = rt;

        // Preview kamerasını yapılandır
        if (previewCam != null)
        {
            previewCam.targetTexture   = rt;
            previewCam.clearFlags      = CameraClearFlags.SolidColor;
            previewCam.backgroundColor = new Color(0.09f, 0.10f, 0.13f, 1f);
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
            piece3D.transform.Rotate(0f, 55f * Time.deltaTime, 0f, Space.Self);
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
        piece3D.transform.rotation   = Quaternion.Euler(15f, 30f, 0f);
        piece3D.transform.localScale = Vector3.one;

        FitCameraTopiece();
    }

    private void FitCameraTopiece()
    {
        if (previewCam == null || piece3D == null) return;

        var renderers = piece3D.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { RepositionPreviewCam(); return; }

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        // Kamerayı parçanın boyutuna göre otomatik uzaklaştır
        float radius  = b.extents.magnitude;
        float halfFov = previewCam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float dist    = (radius / Mathf.Tan(halfFov)) * 1.5f;

        Vector3 center = b.center;
        previewCam.transform.position = center + new Vector3(0f, dist * 0.4f, -dist);
        previewCam.transform.LookAt(center);
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
    }
}
