using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

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

    public void OnLevelStart(int targetScore, float timeLimit)
    {
        HidePanelsImmediate();
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
    private Vector2    trophyStartPos;
    private bool       trophyPosSaved = false;

    public void ShowWinPanel(int finalScore)
    {
        Debug.Log($"[UIManager] ShowWinPanel tetiklendi! Skor: {finalScore}");
        StopTimerPulse();
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

        // 2. Sonraki Seviye Butonu Nabız Animasyonu (CTA Pulsing)
        if (nextBtn != null)
        {
            Debug.Log("[UIManager] NextBtn nabız animasyonu başlatılıyor.");
            nextBtn.DOKill();
            nextBtn.localScale = Vector3.one;
            nextBtn.DOScale(1.06f, 0.75f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
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
        GameObject confetti = new GameObject("ConfettiPiece");
        confetti.transform.SetParent(winOverlay.transform, false);

        RectTransform rect = confetti.AddComponent<RectTransform>();
        Image img = confetti.AddComponent<Image>();

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
            if (confetti != null) Destroy(confetti);
        });
    }

    public void ShowLosePanel(int finalScore)
    {
        Debug.Log($"[UIManager] ShowLosePanel tetiklendi! Skor: {finalScore}");
        StopTimerPulse();
        if (loseFinalScoreText) loseFinalScoreText.text = $"SKOR: {finalScore}";

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
        loseCard.localScale = new Vector3(1f, 0f, 1f);

        SetupLoseAnimations();

        var seq = DOTween.Sequence();
        seq.Append(loseOverlay.DOFade(1f, 0.22f));
        seq.Join(loseCard.DOScaleY(1f, 0.32f).SetEase(Ease.OutBack));
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

        // 2. Yeniden Dene Butonu Nabız Animasyonu (CTA Pulsing)
        if (retryBtn != null)
        {
            retryBtn.DOKill();
            retryBtn.localScale = Vector3.one;
            retryBtn.DOScale(1.06f, 0.75f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
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
        tmp.fontSize = 86; // Ekranın boş alanlarında çıkacağı için boyutu daha çarpıcı yapıyoruz
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
        // 0.22 saniyede 0'dan 2.4 katına fırlayıp, biraz dışarı/yukarı hareket eder
        seq.Append(rect.DOScale(2.4f, 0.22f).SetEase(Ease.OutQuad));
        seq.Join(rect.DOAnchorPos(new Vector2((startX + endX) * 0.5f, (startY + endY) * 0.5f), 0.22f).SetEase(Ease.OutQuad));
        
        // Sonra normal boyutunun biraz üzerine (1.2 katına) yaylanarak oturur
        seq.Append(rect.DOScale(1.2f, 0.15f).SetEase(Ease.OutQuad));
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
}