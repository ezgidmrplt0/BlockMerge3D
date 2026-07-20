using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// Dekoratif arcade butonuna dokunma geri bildirimi kazandırır: basılı tutulunca kapak
// (ControlButtonFbx) içeri çöker, bırakılınca yaylanarak geri döner. Şu an gameplay
// fonksiyonu yok — joker mantığı sonradan eklenecek.
// Rozet, butonun ekran konumunu izliyor. Buton ise ScreenAnchoredProp (sıra 0) ile
// her LateUpdate'te yeniden konumlanıyor. Rozeti buton yeniden konumlandıktan SONRA
// güncellemek için bu scripti daha yüksek execution order'a alıyoruz — aksi halde
// kamera katmana zoom yaparken rozet bir kare geriden gelip titriyor/oynuyordu.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Collider))]
public class ControlButton : MonoBehaviour
{
    [Tooltip("Basma animasyonu uygulanacak buton kapağı (ControlButtonFbx child'ı)")]
    public Transform buttonCap;

    [Header("Joker UI Settings")]
    [Tooltip("Joker kullanıldığında yok olacak olan şimşek/joker görsel ikonu")]
    public GameObject jokerIcon;

    [Header("Movement Settings")]
    [Tooltip("Buton kapağının basıldığındaki yerel kayma miktarı ve yönü (Ebeveyn koordinat sisteminde). Z ekseni derinliktir; eksi değerler içeri basılmayı, artı değerler dışarı çıkmayı temsil eder (Örn: X:0, Y:0, Z:-0.003).")]
    public Vector3 pressOffset = new Vector3(0, 0, -0.003f);
    public float pressDuration = 0.08f;
    public float releaseDuration = 0.18f;

    [Header("Tactile Click Settings")]
    [Tooltip("Basma anında buton kapağının kendi Y ekseninde (yükseklik) ne kadar ezileceği (örn. 0.90)")]
    public float pressSquashY = 0.9f;
    [Tooltip("Basma anında buton kapağının kendi X ve Z eksenlerinde (genişlik) ne kadar esneyeceği (örn. 1.05)")]
    public float pressStretchXZ = 1.05f;

    [Header("Easing Settings")]
    public Ease pressEase = Ease.OutQuad;
    public Ease releaseEase = Ease.OutBack;

    [Header("Joker Sayaç Rozeti")]
    [Tooltip("Kalan joker hakkını gösteren rozet (buton sağ-üstünde 1 / 0). Kapatılırsa rozet çıkmaz.")]
    public bool showUseBadge = true;
    [Tooltip("Rozetin buton merkezine göre PİKSEL kayması (sağ-üst için +X sağ, +Y yukarı).")]
    public Vector2 badgeOffset = new Vector2(80f, 72f);
    [Tooltip("Rozet çapı (piksel).")]
    public float badgeSize = 100f;

    private Camera mainCam;
    private Vector3 restLocalPosition;
    private Vector3 originalScale;
    private bool isPressed;
    private Renderer badgeAnchorRenderer;

    // ─── Joker sayaç rozeti (runtime UI) ────────────────────────────────────
    private RectTransform badgeRoot;
    private Image         badgeRing;   // dış renkli halka
    private Image         badgeFill;   // iç beyaz daire
    private TMPro.TextMeshProUGUI badgeText;
    private Canvas        badgeCanvas;
    private static Sprite circleSprite;

    private static readonly Color BadgeAvailBg = Color.white;
    private static readonly Color BadgeAvailFg = new Color(0.91f, 0.21f, 0.17f); // kırmızı
    private static readonly Color BadgeUsedBg  = new Color(0.93f, 0.94f, 0.92f);
    private static readonly Color BadgeUsedFg  = new Color(0.50f, 0.52f, 0.45f); // gri
    private static readonly Color BadgeAdFg    = new Color(0.13f, 0.68f, 0.30f); // reklam daveti (yeşil)

    // ─── Joker durumu / reklamla yenileme ───────────────────────────────────
    private bool jokerSpent;              // joker bu seviyede kullanıldı (rozet 0 ya da "+1")
    private bool adRefillUsedThisLevel;   // seviye başına 1 reklam-yenileme sınırı
    private GameObject    adConfirmPanel;  // açık onay diyaloğu (null = kapalı)
    // Removed watchBtnRect, cancelBtnRect

    private void Awake()
    {
        mainCam = Camera.main;
        badgeAnchorRenderer = GetComponentInChildren<Renderer>();
        if (buttonCap != null)
        {
            restLocalPosition = buttonCap.localPosition;
            originalScale = buttonCap.localScale;
        }
    }

    private void Update()
    {
        // Reklam onay diyaloğu açıksa tüm girişleri o yönetir.
        if (adConfirmPanel != null && adConfirmPanel.activeSelf)
        {
            return;
        }

        // Seviye bittiyse joker kullanılamaz (bkz. GameManager.IsLevelOver).
        if (GameManager.Instance != null && GameManager.Instance.IsLevelOver) return;
        if (GridManager.Instance != null && GridManager.Instance.IsExplodingLayer) return;

        if (!isPressed)
        {
            if (Input.GetMouseButtonDown(0) && HitsSelf(Input.mousePosition))
            {
                // Öğretici çalışıyorsa sadece UseJoker adımında tıklanabilir; reklam akışı da kapalı.
                bool tutorial = TutorialOverlay.Instance != null && TutorialOverlay.Instance.IsRunning;
                if (tutorial && TutorialOverlay.Instance.CurrentStep != TutorialStepType.UseJoker)
                    return;

                if (LevelManager.Instance == null || LevelManager.Instance.CanUseJoker)
                {
                    Press();
                }
                else if (!tutorial && jokerSpent && !adRefillUsedThisLevel && RewardedAds.IsReady)
                {
                    // Joker tükendi ama reklamla +1 alınabilir → önce onay diyaloğu.
                    ShowAdConfirm();
                }
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            Release();
        }
    }

    private bool HitsSelf(Vector3 screenPos)
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return false;

        Ray ray = mainCam.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out RaycastHit hit) &&
               (hit.transform == transform || hit.transform.IsChildOf(transform));
    }

    private void Press()
    {
        if (buttonCap == null) return;
        isPressed = true;

        // Joker basma sesi (UI butonlarıyla aynı tık sesi).
        AudioManager.Instance?.PlayButtonClickSound();

        buttonCap.DOKill();
        
        // Doğrudan ebeveyn (parent) yerel uzayında hareket ettiriyoruz.
        // Bu sayede Inspector'daki Z değeri doğrudan derinliği (içeri/dışarı) kontrol eder.
        buttonCap.DOLocalMove(restLocalPosition + pressOffset, pressDuration).SetEase(pressEase);
        
        // Ezilme-büzülme (Squash & Stretch) efekti ile tıklama hissini güçlendiriyoruz
        Vector3 targetScale = new Vector3(
            originalScale.x * pressStretchXZ, 
            originalScale.y * pressSquashY, 
            originalScale.z * pressStretchXZ
        );
        buttonCap.DOScale(targetScale, pressDuration).SetEase(pressEase);

        // Şimşek ikonunu animasyonlu bir şekilde küçülterek yok et
        if (jokerIcon != null)
        {
            jokerIcon.transform.DOKill();
            jokerIcon.transform.DOScale(Vector3.zero, 0.25f)
                .SetEase(Ease.InBack)
                .OnComplete(() => jokerIcon.SetActive(false));
        }

        // Joker kullanıldı: rozet 1 → 0 (reklam yenilemesi mümkünse "+1" daveti).
        // Öğretici kullanımında RestoreJoker → ResetJoker çağrılıp tekrar 1'e döner.
        jokerSpent = true;
        RefreshBadge();

        // Joker fonksiyonu: Son yerleştirilen parçayı yok eder
        LevelManager.Instance?.UndoLastPlace();
    }

    private void Release()
    {
        isPressed = false;
        if (buttonCap == null) return;
        
        buttonCap.DOKill();
        
        // Yaylanarak eski konumuna geri dönme
        buttonCap.DOLocalMove(restLocalPosition, releaseDuration).SetEase(releaseEase);
        buttonCap.DOScale(originalScale, releaseDuration).SetEase(releaseEase);
    }

    /// <summary>
    /// LevelManager tarafından yeni seviyeye geçildiğinde veya seviye sıfırlandığında çağrılır.
    /// Jokeri ve şimşek ikonunu sıfırlayıp animasyonlu bir şekilde geri getirir.
    /// </summary>
    public void ResetJoker()
    {
        isPressed = false;
        
        if (jokerIcon != null)
        {
            jokerIcon.transform.DOKill();
            jokerIcon.SetActive(true);
            jokerIcon.transform.localScale = Vector3.zero;
            // Pop-up scale animasyonu ile geri getir
            jokerIcon.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
        }

        if (buttonCap != null)
        {
            buttonCap.DOKill();
            buttonCap.localPosition = restLocalPosition;
            buttonCap.localScale = originalScale;
        }

        // Joker geri geldi: rozet tekrar 1.
        jokerSpent = false;
        RefreshBadge();
    }

    /// <summary>Yeni seviyeye geçişte çağrılır: reklam-yenileme hakkını da sıfırlar.
    /// (ResetJoker yalnızca jokeri geri verir; seviye-başı reklam sınırını KORUR — ki
    /// reklamla yenileme sonrası ResetJoker çağrıldığında sınır tükenmiş kalsın.)</summary>
    public void ResetForNewLevel()
    {
        adRefillUsedThisLevel = false;
        ResetJoker();
    }

    // ─── Joker sayaç rozeti ─────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!showUseBadge) return;
        if (badgeRoot == null && !EnsureBadge()) return;

        // Buton 3D bir sahne nesnesi; ekran konumunu her kare izleyip rozeti
        // sağ-üstüne oturt (konsol/kamera kayarsa rozet takip etsin).
        // Boyutu HER KARE uygula: badgeSize'ı Inspector'dan (oyun çalışırken de)
        // değiştirince rozet anında büyüyüp küçülsün. Eskiden boyut yalnızca
        // EnsureBadge'de bir kez okunuyordu, bu yüzden sonraki değişiklikler işlemiyordu.
        ApplyBadgeSize();

        Vector3 anchorWorld = badgeAnchorRenderer != null
            ? badgeAnchorRenderer.bounds.center
            : (buttonCap != null ? buttonCap.position : transform.position);

        badgeRoot.anchoredPosition = WorldToCanvasLocal(anchorWorld) + badgeOffset;
    }

    /// <summary>Joker durumuna göre rozeti günceller:
    /// kullanılmadı → "1" (kırmızı); kullanıldı + reklamla yenilenebilir → "+1"
    /// (yeşil, davet nabzı); kullanıldı + hak yok → "0" (gri).</summary>
    private void RefreshBadge()
    {
        if (!showUseBadge) return;
        if (badgeRoot == null && !EnsureBadge()) return;

        if (!jokerSpent)
            SetBadgeVisual("1", BadgeAvailBg, BadgeAvailFg, invite: false);
        else if (CanOfferAdRefill())
            SetBadgeVisual("+1", BadgeAvailBg, BadgeAdFg, invite: true);
        else
            SetBadgeVisual("0", BadgeUsedBg, BadgeUsedFg, invite: false);
    }

    private bool CanOfferAdRefill()
    {
        return jokerSpent && !adRefillUsedThisLevel && RewardedAds.IsReady;
    }

    private void SetBadgeVisual(string text, Color bg, Color fg, bool invite)
    {
        badgeText.text  = text;
        badgeFill.color = bg;
        badgeRing.color = fg;
        badgeText.color = fg;

        badgeRoot.DOKill();
        badgeRoot.localScale = Vector3.one * 0.7f;
        var seq = DOTween.Sequence().SetLink(badgeRoot.gameObject);
        seq.Append(badgeRoot.DOScale(1f, 0.35f).SetEase(Ease.OutBack));
        if (invite) // "reklamla +1" — dikkat çeken sürekli nabız
            seq.Append(badgeRoot.DOScale(1.12f, 0.6f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo));
    }

    /// <summary>badgeSize'a göre kök/halka/dolgu/rakam ölçülerini uygular. Her karede
    /// çağrılır ki Inspector'dan canlı boyut değişimi anında görünsün.</summary>
    private void ApplyBadgeSize()
    {
        if (badgeRoot == null) return;
        badgeRoot.sizeDelta   = new Vector2(badgeSize, badgeSize);
        if (badgeRing != null) badgeRing.rectTransform.sizeDelta = new Vector2(badgeSize, badgeSize);
        if (badgeFill != null) badgeFill.rectTransform.sizeDelta = new Vector2(badgeSize * 0.86f, badgeSize * 0.86f);
        if (badgeText != null) badgeText.fontSize = badgeSize * 0.62f;
    }

    private bool EnsureBadge()
    {
        if (badgeRoot != null) return true;

        badgeCanvas = LayerPanelController.Instance != null ? LayerPanelController.Instance.uiCanvas : null;
        if (badgeCanvas == null) badgeCanvas = FindObjectOfType<Canvas>();
        if (badgeCanvas == null) return false;

        var rootGo = new GameObject("JokerUseBadge", typeof(RectTransform));
        rootGo.transform.SetParent(badgeCanvas.transform, false);
        badgeRoot = rootGo.GetComponent<RectTransform>();
        badgeRoot.anchorMin = badgeRoot.anchorMax = new Vector2(0.5f, 0.5f);
        badgeRoot.pivot = new Vector2(0.5f, 0.5f);
        badgeRoot.sizeDelta = new Vector2(badgeSize, badgeSize);

        badgeRing = MakeCircle(badgeRoot, badgeSize, "Ring");
        badgeFill = MakeCircle(badgeRoot, badgeSize * 0.86f, "Fill"); // ~%7 orantılı halka

        var txtGo = new GameObject("Count", typeof(RectTransform), typeof(CanvasRenderer));
        txtGo.transform.SetParent(badgeRoot, false);
        badgeText = txtGo.AddComponent<TMPro.TextMeshProUGUI>();
        badgeText.alignment = TMPro.TextAlignmentOptions.Center;
        badgeText.enableWordWrapping = false;
        badgeText.fontStyle = TMPro.FontStyles.Bold;
        badgeText.fontSize = badgeSize * 0.62f;
        badgeText.raycastTarget = false;
        var ui = UIManager.Instance;
        if (ui != null && ui.scoreText != null) badgeText.font = ui.scoreText.font;
        else if (TMPro.TMP_Settings.defaultFontAsset != null) badgeText.font = TMPro.TMP_Settings.defaultFontAsset;
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        badgeRoot.SetAsLastSibling();

        RefreshBadge(); // başlangıç durumunu (1 / +1 / 0) yansıt
        return true;
    }

    // ─── Reklamla joker yenileme akışı ──────────────────────────────────────

    // Removed HandleAdConfirmInput

    private void StartAdRefill()
    {
        AudioManager.Instance?.PlayButtonClickSound();
        RewardedAds.Show(OnAdRewarded, OnAdFailed);
    }

    private void OnAdRewarded()
    {
        adRefillUsedThisLevel = true;                 // seviye başına 1
        if (LevelManager.Instance != null)
            LevelManager.Instance.RestoreJoker();     // → tüm ControlButton.ResetJoker → rozet "1"
        else { jokerSpent = false; RefreshBadge(); }
    }

    private void OnAdFailed()
    {
        Debug.LogWarning("[Joker] Ödüllü reklam gösterilemedi / ödül alınamadı.");
        RefreshBadge(); // "+1" daveti kalır, tekrar denenebilir
    }

    // Removed RectContainsScreen

    // ─── Reklam onay diyaloğu (runtime UI) ──────────────────────────────────

    private void ShowAdConfirm()
    {
        if (adConfirmPanel != null && adConfirmPanel.activeSelf) return;

        // Try to find the user's AdsOverlay in the scene
        Transform adsOverlayTr = null;
        if (UIManager.Instance != null)
        {
            adsOverlayTr = UIManager.Instance.transform.Find("AdsOverlay");
            if (adsOverlayTr == null && UIManager.Instance.transform.parent != null) 
                adsOverlayTr = UIManager.Instance.transform.parent.Find("AdsOverlay");
        }
        if (adsOverlayTr == null)
        {
            var canvas = GameObject.Find("UICanvas");
            if (canvas != null) adsOverlayTr = canvas.transform.Find("AdsOverlay");
        }

        if (adsOverlayTr == null)
        {
            Debug.LogError("[ControlButton] AdsOverlay bulunamadı! Lütfen sahneye UICanvas altına ekleyin.");
            return;
        }

        adConfirmPanel = adsOverlayTr.gameObject;
        var cg = adConfirmPanel.GetComponent<CanvasGroup>();
        if (cg == null) cg = adConfirmPanel.AddComponent<CanvasGroup>();

        adConfirmPanel.SetActive(true);

        // Butonları bul ve listener ekle
        var watchBtn = adsOverlayTr.Find("WatchBtn")?.GetComponent<UnityEngine.UI.Button>();
        if (watchBtn == null)
        {
            // Belki AdsCard'ın içindedir
            var adsCardInner = adsOverlayTr.Find("AdsCard");
            if (adsCardInner != null) watchBtn = adsCardInner.Find("WatchBtn")?.GetComponent<UnityEngine.UI.Button>();
        }

        var cancelBtn = adsOverlayTr.Find("CancelBtn")?.GetComponent<UnityEngine.UI.Button>();
        if (cancelBtn == null)
        {
            var adsCardInner = adsOverlayTr.Find("AdsCard");
            if (adsCardInner != null) cancelBtn = adsCardInner.Find("CancelBtn")?.GetComponent<UnityEngine.UI.Button>();
        }

        if (watchBtn != null)
        {
            watchBtn.onClick.RemoveAllListeners();
            watchBtn.onClick.AddListener(() => { CloseAdConfirm(); StartAdRefill(); });
        }
        else
        {
            Debug.LogWarning("[ControlButton] AdsOverlay altında WatchBtn (Button) bulunamadı!");
        }
        
        if (cancelBtn != null)
        {
            cancelBtn.onClick.RemoveAllListeners();
            cancelBtn.onClick.AddListener(() => { CloseAdConfirm(); });
        }
        else
        {
            Debug.LogWarning("[ControlButton] AdsOverlay altında CancelBtn (Button) bulunamadı!");
        }

        // Animasyonlar
        cg.alpha = 0f;
        cg.DOKill();
        cg.DOFade(1f, 0.22f).SetLink(adConfirmPanel).SetUpdate(true);

        var adsCard = adsOverlayTr.Find("AdsCard");
        if (adsCard != null)
        {
            adsCard.DOKill();
            adsCard.localScale = new Vector3(1f, 0f, 1f);
            adsCard.DOScaleY(1f, 0.32f).SetEase(Ease.OutBack).SetLink(adConfirmPanel).SetUpdate(true);
        }

        // İkon sallanma animasyonu
        var icon = adsOverlayTr.Find("Image");
        if (icon != null)
        {
            icon.DOKill();
            icon.localRotation = Quaternion.Euler(0f, 0f, -8f);
            icon.DOLocalRotate(new Vector3(0f, 0f, 8f), 1.2f).SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo).SetLink(adConfirmPanel).SetUpdate(true);
        }

        // Title animasyonu
        var title = adsOverlayTr.Find("Title");
        if (title != null)
        {
            title.DOKill();
            title.localScale = Vector3.zero;
            title.DOScale(1f, 0.45f).SetEase(Ease.OutBack).SetLink(adConfirmPanel).SetUpdate(true)
                .OnComplete(() => {
                    title.DOScale(1.06f, 0.9f).SetEase(Ease.InOutSine)
                        .SetLoops(-1, LoopType.Yoyo).SetLink(adConfirmPanel).SetUpdate(true);
                });
        }
        
        // WatchBtn Nabız
        if (watchBtn != null)
        {
            watchBtn.transform.DOKill();
            watchBtn.transform.localScale = Vector3.one;
            watchBtn.transform.DOScale(1.06f, 0.75f).SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo).SetLink(adConfirmPanel).SetUpdate(true);
        }
    }

    private void CloseAdConfirm()
    {
        if (adConfirmPanel == null || !adConfirmPanel.activeSelf) return;
        var cg = adConfirmPanel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.DOKill();
            cg.DOFade(0f, 0.15f).SetLink(adConfirmPanel).SetUpdate(true).OnComplete(() => {
                adConfirmPanel.SetActive(false);
            });
        }
        else
        {
            adConfirmPanel.SetActive(false);
        }
    }

    private Image MakeCircle(RectTransform parent, float size, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = GetCircleSprite();
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        return img;
    }

    /// <summary>Yumuşak kenarlı dolu beyaz daire dokusu (renk Image.color ile verilir).</summary>
    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null) return circleSprite;

        const int R = 64;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float c = (R - 1) * 0.5f, rad = c;
        for (int y = 0; y < R; y++)
        for (int x = 0; x < R; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float a = Mathf.Clamp01((rad - d) / 1.5f); // ~1px yumuşak kenar
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        circleSprite = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), 100f);
        return circleSprite;
    }

    /// <summary>3D buton dünya konumunu, screen-space canvas'ın yerel (anchored)
    /// uzayına çevirir (bkz. TutorialOverlay.WorldObjectToCanvas).</summary>
    private Vector2 WorldToCanvasLocal(Vector3 worldPos)
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null || badgeCanvas == null) return Vector2.zero;

        Vector2 screenPoint = mainCam.WorldToScreenPoint(worldPos);
        var canvasRect = badgeCanvas.transform as RectTransform;
        Camera uiCam = badgeCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : badgeCanvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, uiCam, out Vector2 local);
        return local;
    }
}
