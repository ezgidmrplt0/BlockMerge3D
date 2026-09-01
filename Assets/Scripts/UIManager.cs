using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Top Bar")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI targetScoreText;
    public TextMeshProUGUI timerText;
    public Image           scoreProgressBar; // YENİ: İlerleme barı görseli (Fill Type: Horizontal)
    public Image           timerRadialRing;  // YENİ: Dairesel süre göstergesi (Fill Type: Radial360)
    public GameObject      badgeIcon;        // YENİ: Rozet görseli (Kaybolmasını engellemek için)

    [Header("Win Panel")]
    public CanvasGroup    winOverlay;
    public RectTransform  winCard;
    public TextMeshProUGUI winFinalScoreText;

    [Header("Lose Panel")]
    public CanvasGroup    loseOverlay;
    public RectTransform  loseCard;
    public TextMeshProUGUI loseFinalScoreText;

    [Header("Settings Panel")]
    // Düz (flat) yapı: "Settings" paneli container; çark/titreşim/ses/retry butonları hepsi bunun
    // DOĞRUDAN çocuğu (kardeş, hiçbiri diğerinin çocuğu değil). Başlangıçta sadece çark görünür;
    // çarka basınca diğer 3 buton açılır/kapanır. Titreşim/ses açık-kapalı durumu SPRITE-SWAP ile.
    public GameObject settingsPanel;   // "Settings" container
    public Button     gearBtn;         // çark — her zaman görünür, aç/kapa toggle'ı
    public Button     vibrationBtn;    // titreşim
    public Button     audioBtn;        // ses
    public Button     retryBtn;        // yeniden başlat
    [Space(4)]
    // Titreşim: buton = telefon; çizgiler AYRI child image'lardır. Açık→çizgi child'ları görünür,
    // kapalı→SADECE çizgi child'ları gizli (telefon kalır).
    // Ses: ikon hep kalır; kapalıyken AudioBtn'in child'ı olan kırmızı çapraz çizgi (audioOffSlash) görünür.
    public GameObject audioOffSlash;
    private bool settingsOpen;

    private int   displayedScore;
    private Tween scoreTween;
    private Tween timerPulseTween;
    private bool  timerPulsing;
    private int   currentTargetScore = 100;    // YENİ: Hedef puan hafızası

    private void Awake() { Instance = this; }

    private void Start()
    {
        if (timerText && scoreText)
        {
            timerText.color = scoreText.color;
        }
        if (badgeIcon)
        {
            badgeIcon.SetActive(true);
        }
        SetupSettingsPanel();
    }

    public void UpdateUIAesthetics(Color[] palette)
    {
        if (palette == null || palette.Length < 3) return;

        Color mainColor = palette[0];      // Canlı Magenta / Pembe
        Color secondaryColor = palette[1]; // Turkuaz / Aqua
        Color accentColor = palette[2];    // Pastel Sarı / Krem

        if (scoreText) scoreText.color = mainColor;
        if (targetScoreText) targetScoreText.color = secondaryColor;
        if (timerText && !timerPulsing) timerText.color = mainColor;
        
        if (scoreProgressBar) scoreProgressBar.color = mainColor;
        if (timerRadialRing) timerRadialRing.color = accentColor;
        
        if (winFinalScoreText) winFinalScoreText.color = mainColor;
        if (loseFinalScoreText) loseFinalScoreText.color = secondaryColor;
    }

    // ─── Level Start ──────────────────────────────────────────────────────────

    public bool IsLosePanelActive => loseOverlay != null && loseOverlay.gameObject.activeSelf;

    public void OnLevelStart(int targetScore, float timeLimit)
    {
        if (!IsWinPanelActive && !IsLosePanelActive)
        {
            HidePanelsImmediate();
        }
        RestoreGameplayUI();   // yalnızca RetryBtn; kartları LayerPanelController yönetir
        displayedScore = 0;
        currentTargetScore = targetScore > 0 ? targetScore : 100;
        if (scoreText)
        {
            int levelNum = GameManager.Instance != null ? GameManager.Instance.CurrentLevelNumber : 1;
            scoreText.text = $"LEVEL {levelNum}";
        }
        if (scoreProgressBar) scoreProgressBar.fillAmount = 0f;
        if (timerRadialRing) timerRadialRing.fillAmount = 1f;
        SetTargetScore(targetScore);
        if (timerText && scoreText)
        {
            timerText.color = scoreText.color;
        }
        if (badgeIcon)
        {
            badgeIcon.SetActive(true);
        }
        UpdateTimer(timeLimit, timeLimit);
    }

    // ─── Score ────────────────────────────────────────────────────────────────

    public void AnimateScore(int newTotal)
    {
        scoreTween?.Kill();
        int from = displayedScore;
        scoreTween = DOTween.To(
            ()  => from,
            x   => { from = x; displayedScore = x; },
            newTotal, 0.4f
        ).SetEase(Ease.OutCubic);

        // YENİ: İlerleme barını doldur
        if (scoreProgressBar)
        {
            float fillTarget = Mathf.Clamp01((float)newTotal / currentTargetScore);
            scoreProgressBar.DOFillAmount(fillTarget, 0.4f).SetEase(Ease.OutCubic);
        }
    }

    public void SetTargetScore(int target)
    {
        if (targetScoreText)
            targetScoreText.text = target > 0 ? $"/ {target}" : "";
    }

    private GameObject activeComboPopup;

    public void ShowComboPopup(int comboCount, int linesCount)
    {
        var canvas = GameObject.Find("UICanvas");
        if (canvas == null) return;

        if (activeComboPopup != null)
        {
            Destroy(activeComboPopup);
        }

        string msg;
        if (linesCount >= 3) msg = "TRIPLE BLAST!";
        else if (linesCount == 2) msg = "DOUBLE BLAST!";
        else if (comboCount > 1) msg = $"COMBO x{comboCount}!";
        else
        {
            string[] praises = new string[] { "NICE!", "GREAT!", "GOOD!", "AMAZING!", "EXCELLENT!", "SUPER!" };
            msg = praises[Random.Range(0, praises.Length)];
        }

        GameObject popGO = new GameObject("ComboPopup", typeof(RectTransform), typeof(CanvasGroup));
        activeComboPopup = popGO;
        popGO.transform.SetParent(canvas.transform, false);

        var rt = popGO.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0f, 180f);
        rt.sizeDelta = new Vector2(1000f, 150f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        var cg = popGO.GetComponent<CanvasGroup>();

        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(popGO.transform, false);
        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;

        var tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.enableWordWrapping = false;

        // Oyunun ana fontunu kullan (PlayFloatingPraise stili)
        TMP_FontAsset defaultGameFont = null;
        if (scoreText != null) defaultGameFont = scoreText.font;
        else if (timerText != null) defaultGameFont = timerText.font;
        else if (winFinalScoreText != null) defaultGameFont = winFinalScoreText.font;
        else if (loseFinalScoreText != null) defaultGameFont = loseFinalScoreText.font;

        if (defaultGameFont != null) tmp.font = defaultGameFont;
        else if (TMPro.TMP_Settings.defaultFontAsset != null) tmp.font = TMPro.TMP_Settings.defaultFontAsset;

        tmp.text = msg;
        tmp.fontSize = 58f;
        tmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
        tmp.alignment = TextAlignmentOptions.Center;

        // Canlı ve parlak renk paleti
        if (linesCount >= 3)
            tmp.color = new Color(1f, 0.2f, 0.6f);    // Canlı Magenta / Pembe
        else if (linesCount == 2 || comboCount >= 2)
            tmp.color = new Color(1f, 0.85f, 0.1f);   // Altın Sarısı
        else
            tmp.color = new Color(0.12f, 0.8f, 1f);   // Turkuaz / Mavi

        // TMPro Outline (Siyah kontur)
        tmp.outlineColor = new Color32(20, 20, 20, 255);
        tmp.outlineWidth = 0.25f;

        // Animasyon sekansı (PlayFloatingPraise ile aynı ekrana fırlama & süzülme)
        popGO.transform.localScale = Vector3.zero;
        Sequence seq = DOTween.Sequence();
        seq.SetUpdate(true);

        seq.Append(popGO.transform.DOScale(1.3f, 0.20f).SetEase(Ease.OutQuad));
        seq.Append(popGO.transform.DOScale(1.0f, 0.15f).SetEase(Ease.OutQuad));
        seq.Join(rt.DOAnchorPosY(220f, 0.35f).SetEase(Ease.OutQuad));

        seq.AppendInterval(0.35f);
        seq.Append(rt.DOAnchorPosY(280f, 0.45f).SetEase(Ease.OutCubic));
        seq.Join(cg.DOFade(0f, 0.45f).SetEase(Ease.InQuad));
        seq.Join(popGO.transform.DOScale(0.7f, 0.45f).SetEase(Ease.InQuad));

        seq.OnComplete(() =>
        {
            if (popGO != null) Destroy(popGO);
        });
    }

    public void UpdateTowerFloorProgress(int currentFloor, int totalFloors, float floorProgress)
    {
        if (scoreText != null)
        {
            int levelNum = GameManager.Instance != null ? GameManager.Instance.CurrentLevelNumber : 1;
            scoreText.text = $"LEVEL {levelNum}";
        }

        if (scoreProgressBar != null)
        {
            scoreProgressBar.DOFillAmount(Mathf.Clamp01(floorProgress), 0.3f).SetEase(Ease.OutCubic);
        }
    }

    [Header("Layer Damage Bar")]
    public GameObject      layerDamageBarContainer;
    public Image           layerDamageBarFill;
    public TextMeshProUGUI layerDamageBarText;

    private void OnValidate()
    {
        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EnsureLayerDamageBar();
        }
        #endif
    }

    public void UpdateLayerDamageBar(int currentFloor, int totalFloors, float floorProgress, int currentPoints = 0, int targetPoints = 0)
    {
        if (scoreText != null)
        {
            int levelNum = GameManager.Instance != null ? GameManager.Instance.CurrentLevelNumber : 1;
            scoreText.text = $"LEVEL {levelNum}";
        }

        EnsureLayerDamageBar();

        if (layerDamageBarFill != null)
        {
            float fill = Mathf.Clamp01(floorProgress);
            layerDamageBarFill.DOKill();
            layerDamageBarFill.DOFillAmount(fill, 0.32f).SetEase(Ease.OutCubic);

            // Doluluk arttıkça renk: Elektrik Mavisi -> Canlı Altın/Turuncu
            Color barColor = Color.Lerp(new Color(0.15f, 0.78f, 1f), new Color(1f, 0.65f, 0.1f), fill);
            layerDamageBarFill.DOColor(barColor, 0.25f);
        }

        if (layerDamageBarText != null)
        {
            if (targetPoints > 0)
            {
                layerDamageBarText.text = $"DAMAGE: {currentPoints} / {targetPoints}  ({Mathf.RoundToInt(floorProgress * 100)}%)";
            }
            else
            {
                layerDamageBarText.text = $"DAMAGE: {Mathf.RoundToInt(floorProgress * 100)}%";
            }
        }

        if (layerDamageBarContainer != null)
        {
            layerDamageBarContainer.transform.DOKill();
            layerDamageBarContainer.transform.DOPunchScale(Vector3.one * 0.07f, 0.2f, 5, 0.5f);
        }
    }

    private void EnsureLayerDamageBar()
    {
        if (layerDamageBarContainer != null) return;

        var canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        Transform existing = canvas.transform.Find("LayerDamageBarRoot");
        if (existing != null)
        {
            layerDamageBarContainer = existing.gameObject;
            layerDamageBarFill = existing.Find("Fill")?.GetComponent<Image>();
            layerDamageBarText = existing.Find("Text")?.GetComponent<TextMeshProUGUI>();
            return;
        }

        // Ana Container
        layerDamageBarContainer = new GameObject("LayerDamageBarRoot", typeof(RectTransform), typeof(Image));
        layerDamageBarContainer.transform.SetParent(canvas.transform, false);

        var rt = layerDamageBarContainer.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -68f);
        rt.sizeDelta = new Vector2(250f, 22f);

        var bg = layerDamageBarContainer.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.12f, 0.18f, 0.9f);

        var outline = layerDamageBarContainer.AddComponent<Outline>();
        outline.effectColor = new Color(0.2f, 0.6f, 0.9f, 0.7f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        // Fill Image
        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(layerDamageBarContainer.transform, false);
        var fillRT = fillGo.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = new Vector2(-4f, -4f);

        layerDamageBarFill = fillGo.GetComponent<Image>();
        layerDamageBarFill.type = Image.Type.Filled;
        layerDamageBarFill.fillMethod = Image.FillMethod.Horizontal;
        layerDamageBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        layerDamageBarFill.color = new Color(0.15f, 0.78f, 1f);
        layerDamageBarFill.fillAmount = 0f;

        // Text
        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(layerDamageBarContainer.transform, false);
        var textRT = textGo.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.sizeDelta = Vector2.zero;

        layerDamageBarText = textGo.GetComponent<TextMeshProUGUI>();
        layerDamageBarText.fontSize = 11;
        layerDamageBarText.fontStyle = FontStyles.Bold;
        layerDamageBarText.alignment = TextAlignmentOptions.Center;
        layerDamageBarText.color = Color.white;
        layerDamageBarText.text = "DAMAGE: 0%";
    }

    // ─── Timer ────────────────────────────────────────────────────────────────

    public void UpdateTimer(float remaining, float total)
    {
        if (!timerText) return;
        remaining = Mathf.Max(0f, remaining);
        int mins  = Mathf.FloorToInt(remaining / 60f);
        int secs  = Mathf.FloorToInt(remaining % 60f);
        timerText.text = $"{mins:00}:{secs:00}";

        // YENİ: Dairesel süre halkasını güncelle
        if (timerRadialRing && total > 0f)
        {
            timerRadialRing.fillAmount = Mathf.Clamp01(remaining / total);
            // Kalan süre azaldıkça rengi kademeli olarak kırmızıya çek
            timerRadialRing.color = Color.Lerp(new Color(1f, 0.3f, 0.3f), Color.white, remaining / total);
        }

        bool lowTime = remaining > 0f && remaining <= 10f;

        if (lowTime && !timerPulsing)
        {
            timerPulsing = true;
            timerText.color = new Color(1f, 0.3f, 0.3f);
            timerPulseTween = timerText.rectTransform
                .DOScale(1.2f, 0.45f).SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }
        else if (!lowTime && timerPulsing)
        {
            StopTimerPulse();
        }
    }

    private void StopTimerPulse()
    {
        timerPulsing = false;
        timerPulseTween?.Kill();
        if (timerText)
        {
            timerText.rectTransform.localScale = Vector3.one;
            timerText.color = scoreText ? scoreText.color : Color.white;
        }
    }

    // ─── Win / Lose ───────────────────────────────────────────────────────────

    private GameObject activeSunburst;

    /// <summary>
    /// Seviye bitiş paneli (kazanma/kaybetme) açılırken oynanış arayüzünü gizler,
    /// yeni seviye başlarken geri getirir.
    ///
    /// Panel açıkken parça kartları, sıradaki parça önizlemesi ve sağ üstteki yeniden
    /// başlat butonu ekranda kalmaya devam ediyordu — panelin altında/yanında görünüp
    /// hem dağınık duruyor hem de bitmiş bir seviyede hâlâ oynanabilirmiş izlenimi
    /// veriyordu.
    ///
    /// Katman butonları burada YÖNETİLMEZ: onları zaten
    /// LayerPanelController.SetButtonsVisible çağrıları kapatıp açıyor.
    /// </summary>
    private void HideGameplayUI()
    {
        SetUIObjectsActive(false, "BottomPiecePanel", "RetryBtn", "HoldSlotPanel", "ControlButton");

        // Sıradaki parça paneli çalışma zamanında üretiliyor; isimle aramak yerine
        // LevelManager'ın tuttuğu referans kullanılır (bkz. oradaki not).
        var preview = LevelManager.Instance != null ? LevelManager.Instance.NextPiecePreviewPanel : null;
        if (preview != null) preview.SetActive(false);

        var hold = LevelManager.Instance != null ? LevelManager.Instance.HoldSlotPanel : null;
        if (hold != null) hold.SetActive(false);

        if (ControlButton.Instance != null)
        {
            ControlButton.Instance.gameObject.SetActive(false);
        }

        TowerMiniPreview.Instance?.SetVisible(false);

        if (layerDamageBarContainer != null) layerDamageBarContainer.SetActive(false);
    }

    /// <summary>
    private void RestoreGameplayUI()
    {
        SetUIObjectsActive(true, "BottomPiecePanel", "RetryBtn", "HoldSlotPanel", "ControlButton");

        var preview = LevelManager.Instance != null ? LevelManager.Instance.NextPiecePreviewPanel : null;
        if (preview != null) preview.SetActive(true);

        var hold = LevelManager.Instance != null ? LevelManager.Instance.HoldSlotPanel : null;
        if (hold != null) hold.SetActive(true);

        if (ControlButton.Instance != null)
        {
            ControlButton.Instance.gameObject.SetActive(true);
        }

        TowerMiniPreview.Instance?.SetVisible(true);

        if (layerDamageBarContainer != null) layerDamageBarContainer.SetActive(true);

        LayerPanelController.Instance?.SetBottomPanelVisible(true);
    }

    private void SetUIObjectsActive(bool active, params string[] names)
    {
        var canvas = GameObject.Find("UICanvas");
        if (canvas == null) return;

        foreach (var n in names)
        {
            var t = canvas.transform.Find(n);
            if (t != null) t.gameObject.SetActive(active);
        }
    }

    // ─── Reklam paneli için gameplay gizle/geri getir (win/lose ile aynı davranış) ──
    // İptal edilebilir bir modal olduğu için (WATCH/CANCEL), gizlediğimiz nesneleri
    // hatırlayıp panel kapanınca AYNEN geri açıyoruz.
    private readonly System.Collections.Generic.List<GameObject> adHiddenObjects
        = new System.Collections.Generic.List<GameObject>();

    /// <summary>Reklam onay paneli açılırken çağrılır: parça kartları, sıradaki-parça
    /// önizlemesi, katman butonları ve retry butonu gizlenir (bkz. ControlButton).</summary>
    public void HideGameplayForAd()
    {
        // NOT: Katman/back butonlarına DOKUNMUYORUZ. SetButtonsVisible panel/kamera
        // durumuyla ilişkili (katmandayken back gösterilir); reklam sonrası onu geri
        // açmak durumu bozup oyuncuyu "katman dışı" görünümüne atıyordu. Tam-ekran dim
        // zaten butonları görsel olarak kapatıyor, durumu bozmadan.
        adHiddenObjects.Clear();
        var canvas = GameObject.Find("UICanvas");
        if (canvas != null)
        {
            foreach (var n in new[] { "BottomPiecePanel", "RetryBtn" })
            {
                var t = canvas.transform.Find(n);
                if (t != null && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                    adHiddenObjects.Add(t.gameObject);
                }
            }
        }

        var preview = LevelManager.Instance != null ? LevelManager.Instance.NextPiecePreviewPanel : null;
        if (preview != null && preview.activeSelf)
        {
            preview.SetActive(false);
            adHiddenObjects.Add(preview);
        }
    }

    /// <summary>Reklam paneli kapanınca çağrılır: gizlenen gameplay nesnelerini geri açar.</summary>
    public void RestoreGameplayForAd()
    {
        // SetButtonsVisible çağırmıyoruz (bkz. HideGameplayForAd notu) — katman/back
        // butonları reklam boyunca durumlarını korudu, oyuncu bulunduğu yerde kalır.
        foreach (var go in adHiddenObjects)
            if (go != null) go.SetActive(true);
        adHiddenObjects.Clear();
    }
    private Vector2    trophyStartPos;
    private bool       trophyPosSaved = false;

    public bool IsWinPanelActive => winOverlay != null && winOverlay.gameObject.activeSelf;

    public void ShowWinPanel(int finalScore)
    {
        Debug.Log($"[UIManager] ShowWinPanel tetiklendi! Skor: {finalScore}");
        AudioManager.Instance?.PlayWinSound();
        StopTimerPulse();
        LayerPanelController.Instance?.SetButtonsVisible(false);
        HideGameplayUI();
        if (winFinalScoreText) winFinalScoreText.text = $"SKOR: {finalScore}";

        if (winOverlay == null)
        {
            Debug.LogError("[UIManager] winOverlay atanmamış!");
            return;
        }

        winOverlay.gameObject.SetActive(true);
        winOverlay.alpha   = 0f;

        if (winCard == null)
        {
            Debug.LogError("[UIManager] winCard atanmamış!");
            return;
        }
        winCard.localScale = Vector3.zero;

        SetupWinAnimations();

        var seq = DOTween.Sequence();
        seq.Append(winOverlay.DOFade(1f, 0.22f));
        seq.Join(winCard.DOScale(1f, 0.42f).SetEase(Ease.OutBack));
        seq.AppendCallback(PlayConfettiEffect);
        seq.AppendInterval(0.08f);
        seq.Append(winCard.DOPunchScale(Vector3.one * 0.07f, 0.3f, 5, 0.5f));
    }

    public void AnimateWinPanelExit(System.Action onComplete = null)
    {
        if (winOverlay == null || winCard == null)
        {
            onComplete?.Invoke();
            return;
        }

        // 1. Önce arka planda yeni bölüm yüklendi & 3D tahta sahnede yaylanarak belirir (Elastic In)
        Transform boardTr = LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null
            ? LevelManager.Instance.ActiveMainPiece.transform
            : null;

        if (boardTr != null)
        {
            boardTr.localScale = Vector3.one * 0.8f;
            boardTr.DOScale(Vector3.one, 0.45f).SetEase(Ease.OutBack);
        }

        // 2. Eşzamanlı olarak öndeki Zafer paneli kartı & karartma eriyerek kaybolur (Fade Out)
        Sequence exitSeq = DOTween.Sequence();
        exitSeq.Append(winCard.DOScale(0.8f, 0.25f).SetEase(Ease.InBack));
        exitSeq.Join(winOverlay.DOFade(0f, 0.25f).SetEase(Ease.InQuad));

        exitSeq.OnComplete(() =>
        {
            winOverlay.gameObject.SetActive(false);
            if (activeSunburst != null) Destroy(activeSunburst);

            // 3. Üst Arayüz (HUD) süzülerek kendi konumuna yerleşir
            AnimateTopBarSlideDown();
            RestoreGameplayUI();

            onComplete?.Invoke();
        });
    }

    private void AnimateTopBarSlideDown()
    {
        var topBar = GameObject.Find("TopBar") ?? GameObject.Find("Header") ?? (scoreText != null ? scoreText.transform.parent?.gameObject : null);
        if (topBar != null && topBar.TryGetComponent<RectTransform>(out var rt))
        {
            Vector2 origPos = rt.anchoredPosition;
            rt.anchoredPosition = origPos + new Vector2(0f, 150f);
            rt.DOAnchorPosY(origPos.y, 0.4f).SetEase(Ease.OutBack);
        }
    }

    private void SetupWinAnimations()
    {
        Debug.Log("[UIManager] SetupWinAnimations başlatılıyor...");
        
        if (winOverlay == null)
        {
            Debug.LogError("[UIManager] winOverlay null olduğu için SetupWinAnimations iptal edildi!");
            return;
        }

        // Cihazdaki gerçek hiyerarşiyi loglayalım
        Debug.Log($"[UIManager] winOverlay hiyerarşisi (Ebeveyn: {winOverlay.name}):");
        foreach (Transform c in winOverlay.transform)
        {
            Debug.Log($"   -> {c.name}");
            foreach (Transform cc in c)
            {
                Debug.Log($"      -> {c.name}/{cc.name}");
            }
        }

        // 1. Elementleri bul (Hiyerarşide elemanlar winOverlay altında düz sıralandığı için winOverlay.transform kullanıyoruz)
        Transform nextBtn = winOverlay.transform.Find("NextBtn");
        if (nextBtn == null) nextBtn = FindChildRecursive(winOverlay.transform, "NextBtn");

        Transform trophy = winOverlay.transform.Find("Image");
        if (trophy == null) trophy = FindChildRecursive(winOverlay.transform, "Image");

        Transform titleText = winOverlay.transform.Find("Title");
        if (titleText == null) titleText = FindChildRecursive(winOverlay.transform, "Title");

        Debug.Log($"[UIManager] Element Arama Sonuçları -> nextBtn: {(nextBtn != null ? nextBtn.name : "BULUNAMADI")}, trophy: {(trophy != null ? trophy.name : "BULUNAMADI")}, titleText: {(titleText != null ? titleText.name : "BULUNAMADI")}");

        // 2. Sonraki Seviye Butonu Tıklama ve Nabız Animasyonu (Button Juice & CTA Pulsing)
        if (nextBtn != null && nextBtn.TryGetComponent<Button>(out var nBtn))
        {
            nBtn.interactable = true;
            nBtn.onClick.RemoveAllListeners();
            nBtn.onClick.AddListener(() =>
            {
                nBtn.interactable = false;
                nextBtn.DOKill();
                Sequence pressSeq = DOTween.Sequence();
                pressSeq.Append(nextBtn.DOScale(0.9f, 0.06f).SetEase(Ease.OutQuad));
                pressSeq.Append(nextBtn.DOScale(1.0f, 0.18f).SetEase(Ease.OutBack));

                GameManager.Instance?.NextLevel();
            });
        }

        // 3. Kupa İkonu Salınım Animasyonu (Trophy Floating)
        if (trophy != null)
        {
            Debug.Log("[UIManager] Trophy salınım animasyonu başlatılıyor ve kupa boyutu büyütülüyor.");
            trophy.DOKill();
            trophy.localScale = new Vector3(2.5f, 2.5f, 1f); // Kupayı 2.5 kat büyüterek daha göze çarpar hale getiriyoruz
            RectTransform trophyRect = trophy.GetComponent<RectTransform>();
            if (trophyRect != null)
            {
                if (!trophyPosSaved)
                {
                    trophyStartPos = trophyRect.anchoredPosition;
                    trophyPosSaved = true;
                }
                else
                {
                    trophyRect.anchoredPosition = trophyStartPos;
                }

                // Dikeyde hafif salınım (floating)
                trophyRect.DOAnchorPosY(trophyStartPos.y + 15f, 1.25f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
        }

        // 4. "YOU WIN" Yazısı Bouncy Giriş ve Nefes Alma Animasyonu (Title Pop & Breathe)
        if (titleText != null)
        {
            Debug.Log("[UIManager] Title (YOU WIN!) animasyonları başlatılıyor.");
            titleText.DOKill();
            titleText.localScale = Vector3.zero;

            // Girişte yaylanarak büyüme (Pop-in)
            titleText.DOScale(1f, 0.45f)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    // Giriş tamamlandıktan sonra sürekli nefes alma (breathing) efekti
                    titleText.DOScale(1.08f, 0.9f)
                        .SetEase(Ease.InOutSine)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetUpdate(true);
                });
        }

        // 5. Kupa Arkasına Dönün Işık Hüzmesi (Procedural Sunburst)
        if (trophy != null)
        {
            CreateSunburst(trophy);
        }
    }

    private void CreateSunburst(Transform targetTrophy)
    {
        Debug.Log($"[UIManager] Kupa arkasına Sunburst oluşturuluyor: {targetTrophy.name}");
        if (activeSunburst != null)
        {
            Destroy(activeSunburst);
        }

        if (targetTrophy == null) return;

        // Işık hüzmesi ebeveyn nesnesini kupa ile aynı yere oluştur
        activeSunburst = new GameObject("SunburstEffect");
        activeSunburst.transform.SetParent(targetTrophy.parent, false);
        
        // Kupanın hemen arkasında renderlanması için sibling index ayarı
        int trophyIndex = targetTrophy.GetSiblingIndex();
        activeSunburst.transform.SetSiblingIndex(Mathf.Max(0, trophyIndex));

        RectTransform sunburstRect = activeSunburst.AddComponent<RectTransform>();
        RectTransform trophyRect = targetTrophy.GetComponent<RectTransform>();
        
        if (trophyRect != null)
        {
            sunburstRect.anchorMin = trophyRect.anchorMin;
            sunburstRect.anchorMax = trophyRect.anchorMax;
            sunburstRect.pivot = new Vector2(0.5f, 0.5f);
            sunburstRect.anchoredPosition = trophyRect.anchoredPosition;
        }
        else
        {
            sunburstRect.anchoredPosition = Vector2.zero;
        }
        
        // Kupa boyutuna göre ışık hüzmesini genişlet
        sunburstRect.sizeDelta = new Vector2(500f, 500f);

        int rayCount = 16;
        for (int i = 0; i < rayCount; i++)
        {
            GameObject ray = new GameObject($"Ray_{i}");
            ray.transform.SetParent(activeSunburst.transform, false);

            RectTransform rayRect = ray.AddComponent<RectTransform>();
            
            // Premium görünüm için bir kalın bir ince ışık kolları
            float w = (i % 2 == 0) ? 32f : 18f;
            float h = (i % 2 == 0) ? 270f : 220f;
            rayRect.sizeDelta = new Vector2(w, h);
            rayRect.pivot = new Vector2(0.5f, 0f); // Merkezden dışarı doğru uzaması için pivot alt-ortada
            rayRect.anchoredPosition = Vector2.zero;

            float angle = i * (360f / rayCount);
            rayRect.localRotation = Quaternion.Euler(0f, 0f, angle);

            Image img = ray.AddComponent<Image>();
            // Hafif yarı şeffaf altın sarısı ışık rengi
            img.color = new Color(1f, 0.88f, 0.35f, 0.08f);
        }

        // Kendi etrafında sürekli yumuşakça dönme
        activeSunburst.transform.DORotate(new Vector3(0f, 0f, 360f), 18f, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Incremental)
            .SetEase(Ease.Linear)
            .SetUpdate(true);
    }

    private Transform FindChildRecursive(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform found = FindChildRecursive(child, childName);
            if (found != null) return found;
        }
        return null;
    }

    // Konfeti havuzu: eskiden her win'de 70 yeni GameObject+Image yaratılıp Destroy ediliyordu.
    // Art arda birkaç level kazanınca (konfeti tamamen bitmeden yeni win tetiklenirse) obje/tween
    // sayısı katlanarak büyüyordu (DOTween "Max Tweens reached" 200→1250 gibi) — CreateShatterEffect
    // ile aynı sorunun UI tarafındaki hali. Aynı havuzlama deseni burada da uygulanıyor.
    private Queue<GameObject> confettiPool = new Queue<GameObject>();

    private GameObject GetConfettiPiece()
    {
        GameObject confetti;
        if (confettiPool.Count > 0)
        {
            confetti = confettiPool.Dequeue();
        }
        else
        {
            confetti = new GameObject("ConfettiPiece", typeof(RectTransform), typeof(Image));
        }
        confetti.transform.SetParent(winOverlay.transform, false);
        confetti.transform.SetAsLastSibling();
        confetti.SetActive(true);
        return confetti;
    }

    private void ReturnConfettiPiece(GameObject confetti)
    {
        if (confetti == null) return;
        confetti.GetComponent<RectTransform>().DOKill();
        confetti.GetComponent<Image>().DOKill();
        confetti.SetActive(false);
        confettiPool.Enqueue(confetti);
    }

    private void PlayConfettiEffect()
    {
        Debug.Log("[UIManager] Konfeti efekti başlıyor!");
        if (winOverlay == null) return;

        RectTransform rectTransform = winOverlay.GetComponent<RectTransform>();
        float width = rectTransform != null ? rectTransform.rect.width : Screen.width;
        float height = rectTransform != null ? rectTransform.rect.height : Screen.height;

        int count = 70; // 70 adet konfeti fırlatalım
        for (int i = 0; i < count; i++)
        {
            bool isLeft = (i % 2 == 0);
            float delay = Random.Range(0f, 0.6f); // 0.6 saniye içine yayarak fırlat

            DOVirtual.DelayedCall(delay, () => {
                if (winOverlay != null && winOverlay.gameObject.activeSelf)
                {
                    SpawnConfettiPiece(isLeft, width, height);
                }
            });
        }
    }

    private void SpawnConfettiPiece(bool isLeft, float width, float height)
    {
        GameObject confetti = GetConfettiPiece();

        RectTransform rect = confetti.GetComponent<RectTransform>();
        Image img = confetti.GetComponent<Image>();
        img.color = Color.white; // önceki kullanımdan kalan fade/alpha sıfırlanır (aşağıda tekrar boyanacak)
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        // Rastgele ince/kalın şerit boyutları
        float w = Random.Range(8f, 16f);
        float h = Random.Range(16f, 28f);
        rect.sizeDelta = new Vector2(w, h);

        // Ekranın alt ortasına göre hizala
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        // Sol veya sağ köşelerden başlat
        float startX = isLeft ? (-width / 2f + 40f) : (width / 2f - 40f);
        float startY = -40f; 
        rect.anchoredPosition = new Vector2(startX, startY);

        // Canlı ve şık renk paleti
        Color[] palette = new Color[]
        {
            new Color(1f, 0.17f, 0.46f),  // Canlı Pembe
            new Color(0.12f, 0.73f, 1f),  // Turkuaz / Açık Mavi
            new Color(1f, 0.84f, 0f),     // Altın Sarısı
            new Color(0.18f, 0.8f, 0.44f), // Yeşil
            new Color(1f, 0.4f, 0.1f),    // Turuncu
            new Color(0.6f, 0.2f, 1f)     // Mor
        };
        img.color = palette[Random.Range(0, palette.Length)];

        // Parabolik hareket yolları
        float peakY = height * Random.Range(0.5f, 0.85f);
        float endY = -120f;
        float endX = isLeft 
            ? Random.Range(-width * 0.2f, width * 0.45f)
            : Random.Range(-width * 0.45f, width * 0.2f);

        float durationUp = Random.Range(0.5f, 0.8f);
        float durationDown = Random.Range(1.1f, 1.6f);

        // 3D dönüş efekti verelim (çok daha gerçekçi hissettirir)
        Vector3 randomRot = new Vector3(Random.Range(-360f, 360f), Random.Range(-360f, 360f), Random.Range(-360f, 360f));
        rect.DORotate(randomRot, durationUp + durationDown, RotateMode.FastBeyond360).SetEase(Ease.OutQuad);

        // Yatay sürüklenme
        rect.DOAnchorPosX(endX, durationUp + durationDown).SetEase(Ease.OutQuad);

        // Dikey yükseliş ve düşüş (yerçekimi etkisi)
        Sequence ySeq = DOTween.Sequence();
        ySeq.Append(rect.DOAnchorPosY(peakY, durationUp).SetEase(Ease.OutQuad));
        ySeq.Append(rect.DOAnchorPosY(endY, durationDown).SetEase(Ease.InQuad));

        // Düşerken küçülme ve sönme efekti
        ySeq.Insert(durationUp + durationDown * 0.4f, rect.DOScale(Vector3.zero, durationDown * 0.6f).SetEase(Ease.InQuad));
        ySeq.Insert(durationUp + durationDown * 0.4f, img.DOFade(0f, durationDown * 0.6f).SetEase(Ease.InQuad));

        ySeq.OnComplete(() => {
            ReturnConfettiPiece(confetti);
        });
    }

    public void ShowLosePanel(int finalScore,
                             GameManager.LoseReason reason = GameManager.LoseReason.TimeUp)
    {
        Debug.Log($"[UIManager] ShowLosePanel tetiklendi! Skor: {finalScore}  sebep: {reason}");
        AudioManager.Instance?.PlayLoseSound();
        StopTimerPulse();
        LayerPanelController.Instance?.SetButtonsVisible(false);
        HideGameplayUI();
        if (loseFinalScoreText) loseFinalScoreText.text = $"SKOR: {finalScore}";

        // Başlık kayıp sebebine göre: süre dolduysa "TIME'S UP", hamle kalmadıysa
        // "LEVEL FAILED". Başlık sahnedeki LoseOverlay/Title objesi.
        if (loseOverlay != null)
        {
            var titleTr = loseOverlay.transform.Find("Title");
            if (titleTr == null) titleTr = FindChildRecursive(loseOverlay.transform, "Title");
            if (titleTr != null)
            {
                var titleText = titleTr.GetComponent<TextMeshProUGUI>();
                if (titleText != null)
                    titleText.text = reason == GameManager.LoseReason.NoMoves
                        ? "LEVEL FAILED"
                        : "TIME'S UP";
            }
        }

        if (loseOverlay == null)
        {
            Debug.LogError("[UIManager] loseOverlay atanmamış!");
            return;
        }

        loseOverlay.gameObject.SetActive(true);
        loseOverlay.alpha   = 0f;

        if (loseCard == null)
        {
            Debug.LogError("[UIManager] loseCard atanmamış!");
            return;
        }
        loseCard.localScale = Vector3.zero;

        SetupLoseAnimations();

        var seq = DOTween.Sequence();
        seq.Append(loseOverlay.DOFade(1f, 0.22f));
        seq.Join(loseCard.DOScale(1f, 0.42f).SetEase(Ease.OutBack));
    }

    public void AnimateLosePanelExit(System.Action onComplete = null)
    {
        if (loseOverlay == null || loseCard == null)
        {
            onComplete?.Invoke();
            return;
        }

        Transform boardTr = LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null
            ? LevelManager.Instance.ActiveMainPiece.transform
            : null;

        if (boardTr != null)
        {
            boardTr.localScale = Vector3.one * 0.8f;
            boardTr.DOScale(Vector3.one, 0.45f).SetEase(Ease.OutBack);
        }

        Sequence exitSeq = DOTween.Sequence();
        exitSeq.Append(loseCard.DOScale(0.8f, 0.25f).SetEase(Ease.InBack));
        exitSeq.Join(loseOverlay.DOFade(0f, 0.25f).SetEase(Ease.InQuad));

        exitSeq.OnComplete(() =>
        {
            loseOverlay.gameObject.SetActive(false);

            AnimateTopBarSlideDown();
            RestoreGameplayUI();

            onComplete?.Invoke();
        });
    }

    private void SetupLoseAnimations()
    {
        Debug.Log("[UIManager] SetupLoseAnimations başlatılıyor...");
        if (loseOverlay == null) return;

        // 1. Elementleri bul (Hiyerarşide düz sıralandığı için loseOverlay.transform kullanıyoruz)
        Transform retryBtn = loseOverlay.transform.Find("RetryBtn");
        if (retryBtn == null) retryBtn = FindChildRecursive(loseOverlay.transform, "RetryBtn");

        Transform hourglass = loseOverlay.transform.Find("Image");
        if (hourglass == null) hourglass = FindChildRecursive(loseOverlay.transform, "Image");

        Transform titleText = loseOverlay.transform.Find("Title");
        if (titleText == null) titleText = FindChildRecursive(loseOverlay.transform, "Title");

        Debug.Log($"[UIManager] Lose Element Arama Sonuçları -> retryBtn: {(retryBtn != null ? retryBtn.name : "BULUNAMADI")}, hourglass: {(hourglass != null ? hourglass.name : "BULUNAMADI")}, titleText: {(titleText != null ? titleText.name : "BULUNAMADI")}");

        // 2. Yeniden Dene Butonu Tıklama ve Nabız Animasyonu (Button Juice & CTA)
        if (retryBtn != null && retryBtn.TryGetComponent<Button>(out var rBtn))
        {
            rBtn.interactable = true;
            rBtn.onClick.RemoveAllListeners();
            rBtn.onClick.AddListener(() =>
            {
                rBtn.interactable = false;
                retryBtn.DOKill();
                Sequence pressSeq = DOTween.Sequence();
                pressSeq.Append(retryBtn.DOScale(0.9f, 0.06f).SetEase(Ease.OutQuad));
                pressSeq.Append(retryBtn.DOScale(1.0f, 0.18f).SetEase(Ease.OutBack));

                GameManager.Instance?.RetryLevel();
            });
        }

        // 3. Kum Saati Sallanma Animasyonu (Hourglass Swaying)
        // Pençeden sallanıyormuş gibi hissettirmesi için sağa-sola döner
        if (hourglass != null)
        {
            hourglass.DOKill();
            hourglass.localRotation = Quaternion.Euler(0f, 0f, -8f);
            hourglass.DOLocalRotate(new Vector3(0f, 0f, 8f), 1.25f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        // 4. "TIME'S UP" Yazısı Pop-in ve Nefes Alma Animasyonu (Title Pop & Breathe)
        if (titleText != null)
        {
            titleText.DOKill();
            titleText.localScale = Vector3.zero;

            titleText.DOScale(1f, 0.45f)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    titleText.DOScale(1.08f, 0.9f)
                        .SetEase(Ease.InOutSine)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetUpdate(true);
                });
        }
    }

    private void HidePanelsImmediate()
    {
        CloseSettingsImmediate();
        if (winOverlay)  { winOverlay.alpha  = 0f; winOverlay.gameObject.SetActive(false); }
        if (loseOverlay) { loseOverlay.alpha = 0f; loseOverlay.gameObject.SetActive(false); }

        // Win Panel Temizliği
        if (winOverlay != null)
        {
            Transform nextBtn = winOverlay.transform.Find("NextBtn");
            if (nextBtn == null) nextBtn = FindChildRecursive(winOverlay.transform, "NextBtn");
            if (nextBtn != null) nextBtn.DOKill();
            
            Transform trophy = winOverlay.transform.Find("Image");
            if (trophy == null) trophy = FindChildRecursive(winOverlay.transform, "Image");
            if (trophy != null)
            {
                trophy.DOKill();
                trophy.localScale = Vector3.one;
                if (trophyPosSaved)
                {
                    RectTransform trophyRect = trophy.GetComponent<RectTransform>();
                    if (trophyRect != null) trophyRect.anchoredPosition = trophyStartPos;
                }
            }

            Transform titleText = winOverlay.transform.Find("Title");
            if (titleText == null) titleText = FindChildRecursive(winOverlay.transform, "Title");
            if (titleText != null)
            {
                titleText.DOKill();
                titleText.localScale = Vector3.one;
            }
        }

        // Lose Panel Temizliği
        if (loseOverlay != null)
        {
            Transform retryBtn = loseOverlay.transform.Find("RetryBtn");
            if (retryBtn == null) retryBtn = FindChildRecursive(loseOverlay.transform, "RetryBtn");
            if (retryBtn != null) retryBtn.DOKill();

            Transform hourglass = loseOverlay.transform.Find("Image");
            if (hourglass == null) hourglass = FindChildRecursive(loseOverlay.transform, "Image");
            if (hourglass != null)
            {
                hourglass.DOKill();
                hourglass.localRotation = Quaternion.identity;
            }

            Transform titleText = loseOverlay.transform.Find("Title");
            if (titleText == null) titleText = FindChildRecursive(loseOverlay.transform, "Title");
            if (titleText != null)
            {
                titleText.DOKill();
                titleText.localScale = Vector3.one;
            }
        }
        
        if (activeSunburst != null)
        {
            Destroy(activeSunburst);
            activeSunburst = null;
        }
    }

    private void OnDestroy()
    {
        scoreTween?.Kill();
        timerPulseTween?.Kill();
        
        // Win Panel Animasyonlarını Durdur
        if (winOverlay != null)
        {
            Transform nextBtn = winOverlay.transform.Find("NextBtn");
            if (nextBtn == null) nextBtn = FindChildRecursive(winOverlay.transform, "NextBtn");
            if (nextBtn != null) nextBtn.DOKill();
            
            Transform trophy = winOverlay.transform.Find("Image");
            if (trophy == null) trophy = FindChildRecursive(winOverlay.transform, "Image");
            if (trophy != null) trophy.DOKill();

            Transform titleText = winOverlay.transform.Find("Title");
            if (titleText == null) titleText = FindChildRecursive(winOverlay.transform, "Title");
            if (titleText != null) titleText.DOKill();
        }

        // Lose Panel Animasyonlarını Durdur
        if (loseOverlay != null)
        {
            Transform retryBtn = loseOverlay.transform.Find("RetryBtn");
            if (retryBtn == null) retryBtn = FindChildRecursive(loseOverlay.transform, "RetryBtn");
            if (retryBtn != null) retryBtn.DOKill();

            Transform hourglass = loseOverlay.transform.Find("Image");
            if (hourglass == null) hourglass = FindChildRecursive(loseOverlay.transform, "Image");
            if (hourglass != null) hourglass.DOKill();

            Transform titleText = loseOverlay.transform.Find("Title");
            if (titleText == null) titleText = FindChildRecursive(loseOverlay.transform, "Title");
            if (titleText != null) titleText.DOKill();
        }
        
        if (activeSunburst != null)
        {
            Destroy(activeSunburst);
        }
    }

    /// <summary>
    /// Katman başarıyla tamamlandığında ekranın dış bölgelerinden (sol, sağ veya üst boş alanlar)
    /// oyuncunun görüş alanına fırlatılan tebrik yazıları oluşturur ("GREAT!", "AMAZING!" vb.).
    /// </summary>
    public void PlayFloatingPraise(Vector3 worldPos)
    {
        GameObject praiseGO = new GameObject("FloatingPraise");
        praiseGO.transform.SetParent(transform, false);

        RectTransform rect = praiseGO.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1000f, 150f); // Harflerin/Kelimelerin sığması için geniş bir alan veriyoruz
        rect.pivot = new Vector2(0.5f, 0.5f);      // Tam merkezleme için pivot ayarı

        // Oyuncunun çizdiği kırmızı alanlar: Küplerin etrafındaki boş dış alanlar (Sol, Sağ veya Üst)
        float startX = 0f;
        float startY = 0f;
        float endX = 0f;
        float endY = 0f;

        int zone = Random.Range(0, 3);
        if (zone == 0) // Sol Boşluk
        {
            startX = Random.Range(-260f, -180f);
            startY = Random.Range(-80f, 150f);
            endX = startX - 30f;
            endY = startY + 80f;
        }
        else if (zone == 1) // Sağ Boşluk
        {
            startX = Random.Range(180f, 260f);
            startY = Random.Range(-80f, 150f);
            endX = startX + 30f;
            endY = startY + 80f;
        }
        else // Üst Boşluk (Level/Timer altı, küplerin üstü)
        {
            startX = Random.Range(-120f, 120f);
            startY = Random.Range(280f, 360f);
            endX = startX;
            endY = startY + 80f;
        }

        rect.anchoredPosition = new Vector2(startX, startY);
        rect.localScale = Vector3.zero;

        TextMeshProUGUI tmp = praiseGO.AddComponent<TextMeshProUGUI>();
        tmp.enableWordWrapping = false;            // Asla alt alta kayıp bölünmemesi için kelime kaydırmayı kapatıyoruz

        // Diğer UI elemanlarıyla aynı fontu kullanmak için sahnedeki mevcut bir yazıdan fontu kopyalayalım
        TMP_FontAsset defaultGameFont = null;
        if (scoreText != null) defaultGameFont = scoreText.font;
        else if (timerText != null) defaultGameFont = timerText.font;
        else if (winFinalScoreText != null) defaultGameFont = winFinalScoreText.font;
        else if (loseFinalScoreText != null) defaultGameFont = loseFinalScoreText.font;

        if (defaultGameFont != null)
        {
            tmp.font = defaultGameFont;
        }
        else
        {
            var font = TMPro.TMP_Settings.defaultFontAsset;
            if (font != null) tmp.font = font;
        }

        // Rastgele tebrik kelimesi seç
        string[] praises = new string[] { "NICE!", "GREAT!", "AMAZING!", "EXCELLENT!", "PERFECT!", "AWESOME!", "SWEET!" };
        tmp.text = praises[Random.Range(0, praises.Length)];
        tmp.fontSize = 58; // Ekran boyutlarına daha uyumlu olması için boyut düşürüldü
        tmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
        tmp.alignment = TextAlignmentOptions.Center;

        // Canlı ve parlak renk paleti
        Color[] colors = new Color[] {
            new Color(1f, 0.2f, 0.6f),    // Magenta / Pembe
            new Color(1f, 0.85f, 0.1f),   // Altın Sarısı
            new Color(0.12f, 0.8f, 1f),   // Turkuaz / Mavi
            new Color(0.2f, 0.95f, 0.4f),  // Canlı Yeşil
            new Color(0.7f, 0.3f, 1f)     // Canlı Mor
        };
        tmp.color = colors[Random.Range(0, colors.Length)];

        // TMPro Outline (Arka plandan ayrışması için)
        tmp.outlineColor = new Color32(20, 20, 20, 255);
        tmp.outlineWidth = 0.25f;

        // Animasyon Sekansı
        Sequence seq = DOTween.Sequence();
        seq.SetUpdate(true); // Oyun duraklatılmış olsa bile çalışır
        
        // Ekrana fırlatılıyormuş hissi: 
        // 0.22 saniyede 0'dan 1.4 katına fırlayıp, biraz dışarı/yukarı hareket eder (titremeyi azaltmak için yumuşatıldı)
        seq.Append(rect.DOScale(1.4f, 0.22f).SetEase(Ease.OutQuad));
        seq.Join(rect.DOAnchorPos(new Vector2((startX + endX) * 0.5f, (startY + endY) * 0.5f), 0.22f).SetEase(Ease.OutQuad));
        
        // Sonra normal boyuta (1.0 katına) yaylanarak oturur
        seq.Append(rect.DOScale(1.0f, 0.15f).SetEase(Ease.OutQuad));
        seq.Join(rect.DOAnchorPos(new Vector2(endX, endY), 0.15f).SetEase(Ease.OutQuad));
        
        // Ekranın üstünde hafifçe yükselip süzülerek kaybolma
        seq.AppendInterval(0.35f);
        seq.Append(rect.DOAnchorPos(new Vector2(endX, endY + 60f), 0.5f).SetEase(Ease.OutCubic));
        seq.Join(tmp.DOFade(0f, 0.5f).SetEase(Ease.InQuad));
        seq.Join(rect.DOScale(0.6f, 0.5f).SetEase(Ease.InQuad));

        seq.OnComplete(() => {
            if (praiseGO != null) Destroy(praiseGO);
        });
    }

    // ─── Settings Panel Logic ──────────────────────────────────────────────────

    private void SetupSettingsPanel()
    {
        // Düz yapı: hepsi "Settings" panelinin DOĞRUDAN çocuğu (GearBtn/VibrationBtn/AudioBtn/RetryBtn).
        // İnspector'da elle atanmamışsa isimle bulunur.
        if (settingsPanel == null)
        {
            var canvas = GameObject.Find("UICanvas");
            var p = canvas != null ? canvas.transform.Find("Settings") : null;
            if (p != null) settingsPanel = p.gameObject;
        }
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            var t = settingsPanel.transform;
            if (gearBtn == null)      gearBtn      = FindSettingsBtn(t, "GearBtn");
            if (vibrationBtn == null) vibrationBtn = FindSettingsBtn(t, "VibrationBtn");
            if (audioBtn == null)     audioBtn     = FindSettingsBtn(t, "AudioBtn");
            if (retryBtn == null)     retryBtn     = FindSettingsBtn(t, "RetryBtn");
        }

        if (gearBtn != null)
        {
            gearBtn.onClick = new Button.ButtonClickedEvent();
            gearBtn.onClick.AddListener(ToggleSettingsPanel);
        }
        if (vibrationBtn != null)
        {
            vibrationBtn.onClick = new Button.ButtonClickedEvent();
            vibrationBtn.onClick.AddListener(() => { GameManager.Instance?.ToggleVibration(); UpdateSettingsUI(); });
        }
        if (audioBtn != null)
        {
            audioBtn.onClick = new Button.ButtonClickedEvent();
            audioBtn.onClick.AddListener(() => { GameManager.Instance?.ToggleAudio(); UpdateSettingsUI(); });
        }
        if (retryBtn != null)
        {
            retryBtn.onClick = new Button.ButtonClickedEvent();
            retryBtn.onClick.AddListener(() => {
                AudioManager.Instance?.PlayButtonClickSound();
                CloseSettingsImmediate();
                GameManager.Instance?.RetryLevel();
            });
        }

        settingsOpen = false;
        SetActionButtonsVisible(false); // başlangıçta YALNIZCA çark görünür
        UpdateSettingsUI();
    }

    // "Settings" panelinin doğrudan çocuğu olan butonu isimle bulur (yoksa Button ekler).
    private Button FindSettingsBtn(Transform panel, string name)
    {
        var tr = panel.Find(name);
        if (tr == null) return null;
        var b = tr.GetComponent<Button>();
        if (b == null) b = tr.gameObject.AddComponent<Button>();
        return b;
    }

    public void ToggleSettingsPanel()
    {
        AudioManager.Instance?.PlayButtonClickSound();
        settingsOpen = !settingsOpen;
        if (GameManager.Instance != null) GameManager.Instance.IsSettingsOpen = settingsOpen;
        if (settingsOpen) UpdateSettingsUI();

        if (gearBtn != null)
        {
            gearBtn.transform.DOKill();
            gearBtn.transform.DOPunchRotation(new Vector3(0, 0, -45f), 0.25f, 6, 0.5f).SetUpdate(true);
        }

        SetActionButtonsVisible(settingsOpen);
        TowerMiniPreview.Instance?.SetVisible(!settingsOpen);
    }

    // Çark HARİÇ 3 butonu (titreşim/ses/retry) göster/gizle — pop animasyonuyla.
    private void SetActionButtonsVisible(bool visible)
    {
        foreach (var b in new[] { vibrationBtn, audioBtn, retryBtn })
        {
            if (b == null) continue;
            var tr = b.transform;
            tr.DOKill();
            if (visible)
            {
                b.gameObject.SetActive(true);
                tr.localScale = Vector3.zero;
                tr.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);
            }
            else
            {
                tr.DOScale(0f, 0.18f).SetEase(Ease.InBack).SetUpdate(true).OnComplete(() =>
                {
                    b.gameObject.SetActive(false);
                    tr.localScale = Vector3.one;
                });
            }
        }
    }

    private void CloseSettingsImmediate()
    {
        settingsOpen = false;
        if (GameManager.Instance != null) GameManager.Instance.IsSettingsOpen = false;
        SetActionButtonsVisible(false);
        TowerMiniPreview.Instance?.SetVisible(true);
    }

    public void UpdateSettingsUI()
    {
        if (GameManager.Instance == null) return;

        // TİTREŞİM: buton telefon; çizgiler ayrı CHILD image'lar. Açıkken çizgiler görünür,
        // kapalıyken SADECE o child'lar (çizgiler) gizlenir; telefon (butonun kendi image'ı) kalır.
        if (vibrationBtn != null)
        {
            bool vibOn = GameManager.Instance.IsVibrationEnabled;
            foreach (Transform ch in vibrationBtn.transform)
                ch.gameObject.SetActive(vibOn);
        }

        // SES: ses ikonu hep kalır; kapalıyken üstüne kırmızı çapraz çizgi (audioOffSlash child) çıkar.
        if (audioOffSlash != null)
            audioOffSlash.SetActive(!GameManager.Instance.IsAudioEnabled);
    }
}