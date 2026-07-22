using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

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

    [Header("Döndürme (Next Piece kutusu gibi)")]
    public bool  autoRotate = false;
    public float autoRotateSpeed = 30f;

    // ── Runtime durum ─────────────────────────────────────────────────────────
    private GameObject     piece3D;
    private DraggablePiece draggable;
    private int            slotIndex;
    private bool           initialized;
    private bool           isDraggingOut;
    private float          autoRotateAngle;

    private Vector3        localVisualCenter = Vector3.zero;
    private float          localVisualRadius = 1f;

    public bool HasPiece => piece3D != null;
    public DraggablePiece Draggable => draggable;

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
        isDraggingOut = false;

        // Her kart için RenderTexture önizleme kamerasini aç
        if (previewCam != null)
        {
            previewCam.gameObject.SetActive(true);
            previewCam.enabled = true;

            // Perfect isometric orthographic rendering!
            previewCam.orthographic = true;
            previewCam.orthographicSize = 2f;

            var rt = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);
            rt.antiAliasing        = 2;
            rt.Create();
            previewCam.targetTexture   = rt;
            previewCam.clearFlags      = CameraClearFlags.SolidColor;
            previewCam.backgroundColor = Color.clear;

            if (previewImage != null)
            {
                previewImage.texture = rt;
                previewImage.color   = Color.white;
                previewImage.enabled = true;

                // Create a real-time silhouette drop shadow of the 3D piece
                var oldShadow = previewImage.transform.parent.Find("PreviewImageShadow");
                if (oldShadow != null) Destroy(oldShadow.gameObject);

                GameObject shadowGO = new GameObject("PreviewImageShadow", typeof(RectTransform), typeof(RawImage));
                shadowGO.transform.SetParent(previewImage.transform.parent, false);
                shadowGO.transform.SetSiblingIndex(previewImage.transform.GetSiblingIndex()); // Place it behind previewImage
                
                var shadowRT = shadowGO.GetComponent<RectTransform>();
                shadowRT.anchorMin = previewImage.rectTransform.anchorMin;
                shadowRT.anchorMax = previewImage.rectTransform.anchorMax;
                shadowRT.pivot = previewImage.rectTransform.pivot;
                shadowRT.sizeDelta = previewImage.rectTransform.sizeDelta;
                // Offset to the right and down (consistent with card shadow and top-left key light)
                shadowRT.anchoredPosition = new Vector2(10f, -10f);
                shadowRT.localScale = Vector3.one;

                var shadowImg = shadowGO.GetComponent<RawImage>();
                shadowImg.texture = rt;
                shadowImg.color = new Color(0f, 0f, 0f, 0.38f); // Distinct drop shadow matching light source
                shadowImg.enabled = true;
            }

            // Clean up any old lights
            var oldKey = previewCam.transform.Find("PreviewKeyLight");
            if (oldKey != null) Destroy(oldKey.gameObject);
            var oldFill = previewCam.transform.Find("PreviewFillLight");
            if (oldFill != null) Destroy(oldFill.gameObject);

            // Add professional studio point lights with soft shadows and balanced intensity
            var keyLight = new GameObject("PreviewKeyLight", typeof(Light));
            keyLight.transform.SetParent(previewCam.transform, false);
            keyLight.transform.localPosition = new Vector3(-4f, 4f, 1f);
            var kL = keyLight.GetComponent<Light>();
            kL.type = LightType.Point;
            kL.range = 25f;
            kL.intensity = 3.5f;
            kL.shadows = LightShadows.Soft;
            kL.shadowStrength = 0.85f;
            kL.color = new Color(1f, 0.98f, 0.95f); // Warm key light

            var fillLight = new GameObject("PreviewFillLight", typeof(Light));
            fillLight.transform.SetParent(previewCam.transform, false);
            fillLight.transform.localPosition = new Vector3(4f, -4f, 1f);
            var fL = fillLight.GetComponent<Light>();
            fL.type = LightType.Point;
            fL.range = 25f;
            fL.intensity = 1.2f;
            fL.color = new Color(0.85f, 0.9f, 1f); // Cool fill light
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
        {
            PlaceInPreview();
            if (previewImage != null)
            {
                previewImage.rectTransform.localScale = Vector3.zero;
                previewImage.rectTransform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
            }
        }

        UpdateVisuals();
    }

    public GameObject ExtractPiece()
    {
        GameObject extracted = piece3D;
        piece3D = null;
        draggable = null;
        isDraggingOut = false;
        UpdateVisuals();
        return extracted;
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
            if (autoRotate) autoRotateAngle += Time.deltaTime * autoRotateSpeed;
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

        // SpeciesVisual (hayvan modeli) üzerindeki FaceCamera, her karede kendi rotasyonunu
        // Camera.main'e göre ezer — bu da kartın kendi (previewCam tabanlı) üstten-görünüm
        // rotasyon numarasıyla çakışıp hayvanın kutu içinde yanlış/kaymış görünmesine yol açar.
        // Kart önizlemesindeyken devre dışı bırakıyoruz; doğru yön aşağıda previewCam'e göre elle
        // ayarlanıyor, parça tekrar sürüklenince FaceCamera geri açılır.
        foreach (var fc in piece3D.GetComponentsInChildren<FaceCamera>(true))
            fc.enabled = false;

        piece3D.transform.position   = Vector3.zero;
        piece3D.transform.localScale = Vector3.one;

        // ÖNEMLİ SIRALAMA: kök rotasyonunu (kartın "tam tepeden" görünüm numarası) ve hayvanın
        // previewCam'e dönük yüzünü, sınır (bounds) hesaplamasından ÖNCE, son/kalıcı haliyle
        // uyguluyoruz. Bunlar bounds hesabından SONRA değişirse (eskiden olduğu gibi), o anda
        // merkezlenen içerik yeniden kayar — tam olarak "pozisyonlar kaydı" hatasına yol açan buydu.
        piece3D.transform.rotation = ComputeCardRootRotation();
        FaceSpeciesVisualsToPreviewCam();

        // Yerel mesh sınırlarını hesaplıyoruz (World-space bounds ilk karede hatalı/stale olduğu için)
        Bounds localBounds = CalculateLocalBounds(piece3D);
        localVisualCenter = localBounds.center;
        localVisualRadius = localBounds.extents.magnitude;

        if (localVisualRadius < 0.001f) localVisualRadius = 1f;

        UpdatePiecePositionAndRotation();
    }

    /// <summary>Kartın kök rotasyonu — previewCam'e göre "tam tepeden" görünüm efekti verir.</summary>
    private Quaternion ComputeCardRootRotation()
    {
        if (previewCam == null) return Quaternion.identity;

        float elev = 90f; // Katman görünümü gibi tam tepeden
        float azim = 0f;  // Dümdüz, yatay rotasyon yok
        Quaternion baseIso = Quaternion.Euler(elev, azim, 0f);
        Quaternion rot = previewCam.transform.rotation * Quaternion.Inverse(baseIso);
        if (autoRotate) rot = rot * Quaternion.Euler(0f, autoRotateAngle, 0f);
        return rot;
    }

    /// <summary>SpeciesVisual çocuklarının previewCam'e dönük (dik, kamerayı gören) durmasını sağlar.</summary>
    private void FaceSpeciesVisualsToPreviewCam()
    {
        if (piece3D == null || previewCam == null) return;

        Vector3 camForward = previewCam.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.001f) return;
        camForward.Normalize();
        Quaternion faceRot = Quaternion.LookRotation(camForward, Vector3.up);

        foreach (var fc in piece3D.GetComponentsInChildren<FaceCamera>(true))
            fc.transform.rotation = faceRot;
    }

    private void UpdatePiecePositionAndRotation()
    {
        if (piece3D == null) return;

        // --- RenderTexture modu: parçayı previewCam önünde ortala ---
        if (previewCam != null && previewCam.isActiveAndEnabled)
        {
            float depth   = 3.5f;
            float viewH;
            if (previewCam.orthographic)
            {
                viewH = 2f * previewCam.orthographicSize;
            }
            else
            {
                float halfFov = previewCam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                viewH = 2f * depth * Mathf.Tan(halfFov);
            }
            // Görseli daha büyük ve belirgin yapmak için ölçek oranını 0.80f'ten 1.00f'e çıkardık (0.2 arttırıldı)
            float scale   = (viewH * 1.00f * 0.5f) / Mathf.Max(localVisualRadius, 0.001f);

            Vector3 center = previewCam.transform.position + previewCam.transform.forward * depth;

            Quaternion targetRot = ComputeCardRootRotation();

            piece3D.transform.rotation   = targetRot;
            piece3D.transform.position   = center - (targetRot * localVisualCenter * scale);
            piece3D.transform.localScale = Vector3.one * scale;

            if (draggable != null) draggable.HomePosition = piece3D.transform.position;
            return;
        }

        // --- Fallback: Camera.main tabanlı orijinal konumlandırma ---
        if (Camera.main == null) return;

        float depth2 = 5f;

        RectTransform rect = GetComponent<RectTransform>();
        Vector2 lc = new Vector2(rect.rect.width * (0.5f - rect.pivot.x), rect.rect.height * (0.5f - rect.pivot.y));
        Vector3 cardScreenPos = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(lc));
        cardScreenPos.z = depth2;
        Vector3 targetWorldPos = Camera.main.ScreenToWorldPoint(cardScreenPos);

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float screenH  = Vector3.Distance(corners[0], corners[1]);
        float worldH   = Vector3.Distance(
            Camera.main.ScreenToWorldPoint(new Vector3(0f, 0f,     depth2)),
            Camera.main.ScreenToWorldPoint(new Vector3(0f, screenH, depth2)));

        float scale2 = (worldH * 0.80f * 0.5f) / Mathf.Max(localVisualRadius, 0.001f);

        float elev2 = 90f; // Katman görünümü gibi tam tepeden
        float azim2 = 0f;  // Dümdüz, yatay rotasyon yok
        Quaternion baseIso2   = Quaternion.Euler(elev2, azim2, 0f);
        Quaternion targetRot2 = Camera.main.transform.rotation * Quaternion.Inverse(baseIso2);

        piece3D.transform.rotation   = targetRot2;
        piece3D.transform.position   = targetWorldPos - (targetRot2 * localVisualCenter * scale2);
        piece3D.transform.localScale = Vector3.one * scale2;

        if (draggable != null) draggable.HomePosition = piece3D.transform.position;
    }

    private static Bounds CalculateLocalBounds(GameObject obj)
    {
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;
        
        var renderers = obj.GetComponentsInChildren<Renderer>();
        Transform parentTransform = obj.transform;

        foreach (var r in renderers)
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
            {
                Bounds rendererLocalBounds;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    rendererLocalBounds = mf.sharedMesh.bounds;
                }
                else
                {
                    // Fallback
                    rendererLocalBounds = r.bounds;
                    Vector3 localCenter = obj.transform.InverseTransformPoint(r.bounds.center);
                    Vector3 localSize = obj.transform.InverseTransformVector(r.bounds.size);
                    Bounds tempB = new Bounds(localCenter, localSize);
                    if (!hasBounds)
                    {
                        bounds = tempB;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(tempB);
                    }
                    continue;
                }

                // Ebeveyn uzayına dönüşüm matrisini yerel TRS (TRS = Translation, Rotation, Scale)
                // matrislerini çarparak elde ediyoruz. Dünya koordinatlarına çıkmadığımız için
                // Unity'nin ilk karedeki 'stale/gecikmeli' transform eşitleme hatasından etkilenmez.
                Matrix4x4 localMatrix = GetLocalToParentMatrix(r.transform, parentTransform);
                
                Vector3 center = rendererLocalBounds.center;
                Vector3 extents = rendererLocalBounds.extents;
                Vector3[] corners = new Vector3[8]
                {
                    center + new Vector3(-extents.x, -extents.y, -extents.z),
                    center + new Vector3(extents.x, -extents.y, -extents.z),
                    center + new Vector3(-extents.x, extents.y, -extents.z),
                    center + new Vector3(extents.x, extents.y, -extents.z),
                    center + new Vector3(-extents.x, -extents.y, extents.z),
                    center + new Vector3(extents.x, -extents.y, extents.z),
                    center + new Vector3(-extents.x, extents.y, extents.z),
                    center + new Vector3(extents.x, extents.y, extents.z)
                };

                foreach (var corner in corners)
                {
                    Vector3 parentLocalCorner = localMatrix.MultiplyPoint3x4(corner);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(parentLocalCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(parentLocalCorner);
                    }
                }
            }
        }

        if (!hasBounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.one);
        }
        return bounds;
    }

    private static Matrix4x4 GetLocalToParentMatrix(Transform child, Transform parent)
    {
        Matrix4x4 mat = Matrix4x4.identity;
        Transform curr = child;
        while (curr != null && curr != parent)
        {
            Matrix4x4 localMat = Matrix4x4.TRS(curr.localPosition, curr.localRotation, curr.localScale);
            mat = localMat * mat;
            curr = curr.parent;
        }
        return mat;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pointer olayları
    // ─────────────────────────────────────────────────────────────────────────

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!HasPiece || draggable == null) return;
        if (DraggablePiece.IsDragging)      return;

        // Tutorial check
        if (TutorialOverlay.Instance != null && TutorialOverlay.Instance.IsRunning)
        {
            if (TutorialOverlay.Instance.CurrentStep != TutorialStepType.DragPieceToBoard)
                return; // Blokla

            // Sadece hedeflenen (ilk geçerli) kartın sürüklenmesine izin ver
            if (slotIndex != TutorialOverlay.Instance.GetTargetCardIndex())
                return; // Blokla
        }

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
        bool usingRT = previewCam != null && previewCam.isActiveAndEnabled;
        if (previewImage != null) previewImage.enabled = usingRT && HasPiece;
        if (emptyOverlay != null) emptyOverlay.SetActive(!HasPiece);

        // Sync shadow RawImage visibility
        if (previewImage != null && previewImage.transform.parent != null)
        {
            var shadowTrans = previewImage.transform.parent.Find("PreviewImageShadow");
            if (shadowTrans != null)
            {
                var shadowImg = shadowTrans.GetComponent<RawImage>();
                if (shadowImg != null)
                {
                    shadowImg.enabled = usingRT && HasPiece;
                }
            }
        }

        // Kart arka planı her zaman tam opak — RenderTexture üzerinde parça görünür
        var cardBg = GetComponent<Image>();
        if (cardBg != null)
        {
            var col = cardBg.color;
            col.a = 1.0f;
            cardBg.color = col;
        }
    }

    private void OnDestroy()
    {
        // RenderTexture'ı belleğe serbest bırak
        if (previewCam != null && previewCam.targetTexture != null)
        {
            var rt = previewCam.targetTexture;
            previewCam.targetTexture = null;
            rt.Release();
            Destroy(rt);
        }
    }
}
