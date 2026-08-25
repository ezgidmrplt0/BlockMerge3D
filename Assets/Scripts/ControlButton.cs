using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

[DefaultExecutionOrder(100)]
public class ControlButton : MonoBehaviour
{
    public static ControlButton Instance { get; private set; }

    [Header("Joker UI")]
    [Tooltip("Undo ikonu (buton içindeki görsel)")]
    public Image jokerIcon;

    [Header("Joker Sayaç Rozeti")]
    [Tooltip("Kalan joker hakkını gösteren rozet")]
    public bool showUseBadge = true;
    [Tooltip("Rozet çapı (piksel)")]
    public float badgeSize = 56f;
    [Tooltip("Rozetin buton merkezine göre kayması")]
    public Vector2 badgeOffset = new Vector2(28f, 28f);

    private Button button;
    private Image  bgImage;
    private RectTransform rt;
    private Canvas parentCanvas;
    private Color  bgColorNormal;
    private static readonly Color BgColorSpent = new Color(0.45f, 0.45f, 0.45f, 1f);

    private RectTransform badgeRoot;
    private Image         badgeRing;
    private Image         badgeFill;
    private TMPro.TextMeshProUGUI badgeText;
    private static Sprite circleSprite;

    private static readonly Color BadgeAvailBg = Color.white;
    private static readonly Color BadgeAvailFg = new Color(0.91f, 0.21f, 0.17f);
    private static readonly Color BadgeUsedBg  = new Color(0.93f, 0.94f, 0.92f);
    private static readonly Color BadgeUsedFg  = new Color(0.50f, 0.52f, 0.45f);
    private static readonly Color BadgeAdFg    = new Color(0.13f, 0.68f, 0.30f);

    private bool jokerSpent;
    private bool adRefillUsedThisLevel;
    private GameObject adConfirmPanel;
    private GameObject adDim;

    public static bool AdPanelOpen { get; private set; }

    private static float adInputBlockUntil;
    public static bool AdInputBlocked => AdPanelOpen || Time.unscaledTime < adInputBlockUntil;

    private void Awake()
    {
        Instance = this;
        button = GetComponent<Button>();
        bgImage = GetComponent<Image>();
        rt = GetComponent<RectTransform>();
        if (bgImage != null) bgColorNormal = bgImage.color;
        if (button != null)
            button.onClick.AddListener(OnClick);
    }

    private void Start()
    {
        var canvas = GameObject.Find("UICanvas");
        var ov = canvas != null ? canvas.transform.Find("AdsOverlay") : null;
        if (ov != null && ov.gameObject.activeSelf) ov.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnClick()
    {
        if (adConfirmPanel != null && adConfirmPanel.activeSelf) return;
        if (GameManager.Instance != null && GameManager.Instance.IsLevelOver) return;
        if (GridManager.Instance != null && GridManager.Instance.IsExplodingLayer) return;

        bool tutorial = TutorialOverlay.Instance != null && TutorialOverlay.Instance.IsRunning;
        if (tutorial && TutorialOverlay.Instance.CurrentStep != TutorialStepType.UseJoker)
            return;

        if (LevelManager.Instance == null || LevelManager.Instance.CanUseJoker)
        {
            DoJoker();
        }
        else if (!tutorial && jokerSpent && !adRefillUsedThisLevel)
        {
            ShowAdConfirm();
        }
    }

    private void DoJoker()
    {
        AudioManager.Instance?.PlayButtonClickSound();

        // Basma animasyonu
        if (rt != null)
        {
            rt.DOKill();
            rt.DOScale(0.85f, 0.08f).SetEase(Ease.OutQuad)
                .OnComplete(() => rt.DOScale(1f, 0.18f).SetEase(Ease.OutBack));
        }

        jokerSpent = true;
        SetSpentVisual(true);
        RefreshBadge();
        LevelManager.Instance?.UndoLastPlace();
    }

    public void ResetJoker()
    {
        if (rt != null)
        {
            rt.DOKill();
            rt.localScale = Vector3.one;
        }

        jokerSpent = false;
        SetSpentVisual(false);
        RefreshBadge();
    }

    private void SetSpentVisual(bool spent)
    {
        if (bgImage != null)
            bgImage.DOColor(spent ? BgColorSpent : bgColorNormal, 0.25f);
        if (jokerIcon != null)
            jokerIcon.DOColor(spent ? new Color(1f, 1f, 1f, 0.4f) : Color.white, 0.25f);
    }

    public void ResetForNewLevel()
    {
        adRefillUsedThisLevel = false;
        ResetJoker();
    }

    // ─── Rozet ──────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!showUseBadge) return;
        if (badgeRoot == null && !EnsureBadge()) return;
        ApplyBadgeSize();
    }

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

    private bool CanOfferAdRefill() => jokerSpent && !adRefillUsedThisLevel;

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
        if (invite)
            seq.Append(badgeRoot.DOScale(1.12f, 0.6f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo));
    }

    private void ApplyBadgeSize()
    {
        if (badgeRoot == null) return;
        badgeRoot.sizeDelta = new Vector2(badgeSize, badgeSize);
        if (badgeRing != null) badgeRing.rectTransform.sizeDelta = new Vector2(badgeSize, badgeSize);
        if (badgeFill != null) badgeFill.rectTransform.sizeDelta = new Vector2(badgeSize * 0.86f, badgeSize * 0.86f);
        if (badgeText != null) badgeText.fontSize = badgeSize * 0.62f;
    }

    private bool EnsureBadge()
    {
        if (badgeRoot != null) return true;

        parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null) parentCanvas = FindObjectOfType<Canvas>();
        if (parentCanvas == null) return false;

        var rootGo = new GameObject("JokerUseBadge", typeof(RectTransform));
        rootGo.transform.SetParent(rt != null ? rt : parentCanvas.transform as RectTransform, false);
        badgeRoot = rootGo.GetComponent<RectTransform>();
        badgeRoot.anchorMin = badgeRoot.anchorMax = new Vector2(1f, 1f);
        badgeRoot.pivot = new Vector2(0.5f, 0.5f);
        badgeRoot.anchoredPosition = badgeOffset;
        badgeRoot.sizeDelta = new Vector2(badgeSize, badgeSize);

        badgeRing = MakeCircle(badgeRoot, badgeSize, "Ring");
        badgeFill = MakeCircle(badgeRoot, badgeSize * 0.86f, "Fill");

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
        RefreshBadge();
        return true;
    }

    // ─── Reklam akışı ───────────────────────────────────────────────────────

    private void StartAdRefill()
    {
        AudioManager.Instance?.PlayButtonClickSound();
        if (adConfirmPanel != null)
        {
            var cg = adConfirmPanel.GetComponent<CanvasGroup>();
            if (cg != null) cg.interactable = false;
        }
        RewardedAds.Show(OnAdRewarded, OnAdFailed);
    }

    private void OnAdRewarded()
    {
        adRefillUsedThisLevel = true;
        if (LevelManager.Instance != null)
            LevelManager.Instance.RestoreJoker();
        else { jokerSpent = false; RefreshBadge(); }
        CloseAdConfirm();
    }

    private void OnAdFailed()
    {
        Debug.LogWarning("[Joker] Ödüllü reklam gösterilemedi / ödül alınamadı.");
        RefreshBadge();
        CloseAdConfirm();
    }

    private void ShowAdConfirm()
    {
        if (adConfirmPanel != null && adConfirmPanel.activeSelf) return;

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
            Debug.LogError("[ControlButton] AdsOverlay bulunamadı!");
            return;
        }

        adConfirmPanel = adsOverlayTr.gameObject;
        var cg = adConfirmPanel.GetComponent<CanvasGroup>();
        if (cg == null) cg = adConfirmPanel.AddComponent<CanvasGroup>();

        adConfirmPanel.SetActive(true);
        AdPanelOpen = true;
        EnsureAdDim(adsOverlayTr);
        UIManager.Instance?.HideGameplayForAd();

        var watchBtn = FindChildButton(adsOverlayTr, "WatchBtn");
        var cancelBtn = FindChildButton(adsOverlayTr, "CancelBtn");

        if (watchBtn != null)
        {
            watchBtn.onClick.RemoveAllListeners();
            watchBtn.onClick.AddListener(() => StartAdRefill());
        }
        if (cancelBtn != null)
        {
            cancelBtn.onClick.RemoveAllListeners();
            cancelBtn.onClick.AddListener(() => CloseAdConfirm());
        }

        cg.interactable = true;
        cg.blocksRaycasts = true;
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

        var icon = adsOverlayTr.Find("Image");
        if (icon != null)
        {
            icon.DOKill();
            icon.localRotation = Quaternion.Euler(0f, 0f, -8f);
            icon.DOLocalRotate(new Vector3(0f, 0f, 8f), 1.2f).SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo).SetLink(adConfirmPanel).SetUpdate(true);
        }

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

        AdPanelOpen = false;
        UIManager.Instance?.RestoreGameplayForAd();
        if (adDim != null) adDim.SetActive(false);
        adInputBlockUntil = Time.unscaledTime + 0.5f;

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

    private void EnsureAdDim(Transform adsOverlay)
    {
        var parent = adsOverlay.parent;
        if (parent == null) return;

        if (adDim == null)
        {
            adDim = new GameObject("AdDim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            adDim.transform.SetParent(parent, false);
            var img = adDim.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.66f);
            img.raycastTarget = true;
            var drt = (RectTransform)adDim.transform;
            drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
            drt.offsetMin = drt.offsetMax = Vector2.zero;
        }
        adDim.SetActive(true);
        adDim.transform.SetAsLastSibling();
        adsOverlay.SetAsLastSibling();
    }

    private static Button FindChildButton(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t.GetComponent<Button>();
        var card = parent.Find("AdsCard");
        if (card != null)
        {
            t = card.Find(name);
            if (t != null) return t.GetComponent<Button>();
        }
        return null;
    }

    private Image MakeCircle(RectTransform parent, float size, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = GetCircleSprite();
        img.raycastTarget = false;
        var crt = go.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(size, size);
        return img;
    }

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
            float a = Mathf.Clamp01((rad - d) / 1.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        circleSprite = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), 100f);
        return circleSprite;
    }
}
