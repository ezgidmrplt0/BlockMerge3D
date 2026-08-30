using UnityEngine;

[DefaultExecutionOrder(10)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Scoring")]
    public int pointsPerCell = 10;

    [Header("Level Order")]
    public LevelOrderData levelOrder;

    public int Score { get; private set; }
    public int CurrentLevelNumber => currentLevelIndex + 1;

    /// <summary>Seviye bitti (kazanıldı ya da kaybedildi) veya ayarlar açık — oyun içi girdiler
    /// (parça sürükleme, tahta döndürme, joker) bu andan itibaren engellenmelidir.</summary>
    public bool IsLevelOver => levelComplete || winLocked || IsSettingsOpen;

    public bool IsSettingsOpen { get; set; }
    public bool IsVibrationEnabled { get; private set; } = true;
    public bool IsAudioEnabled { get; private set; } = true;

    private const string PREF_VIBRATION = "VibrationEnabled";
    private const string PREF_AUDIO = "AudioEnabled";

    private bool  levelComplete;
    private bool  winLocked;           // Son katman temizlenmeye başladı — artık kaybedilemez
    private bool  pendingWin;          // Bloklayan animasyonlar biterken kazanma bekliyor
    private int   blockingAnimations;  // Win panelini erteleyen (parça yerleşimi/merge) animasyon sayısı
    private int   currentLevelIndex;
    private int   currentTargetScore;
    private float remainingTime;
    private float totalTime;
    private bool  timerRunning;

    private const string PREF_LEVEL = "CurrentLevelIndex";

    // ── Sürüm-kapılı ilerleme sıfırlama ──────────────────────────────────────────
    // Bir güncellemede TÜM oyuncuları level 1'den başlatmak istediğinde bu sayıyı ARTIR.
    // Açılışta oyuncunun kayıtlı reset-sürümü bundan küçükse ilerleme bir kez sıfırlanır ve
    // yeni sürüm işaretlenir → aynı oyuncuda tekrar tetiklenmez (mevcut oyuncular korunur,
    // sadece bu güncellemede bir kez level 1'e döner). Sıfırlama İSTEMİYORSAN dokunma.
    private const int LEVEL_RESET_VERSION = 1;
    private const string PREF_RESET_VERSION = "LevelResetVersion";

    private void Awake()
    {
        Instance = this;

        // Mobil FPS ve Performans Optimizasyonu (30 FPS kilidini kaldırıp 60/120 FPS akıcılık sağlar)
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    private void Start()
    {
        IsVibrationEnabled = PlayerPrefs.GetInt(PREF_VIBRATION, 1) == 1;
        IsAudioEnabled = PlayerPrefs.GetInt(PREF_AUDIO, 1) == 1;
        ApplyAudioSetting();

        // Bu güncellemede herkesi level 1'e döndür (bir kez). Bkz. LEVEL_RESET_VERSION notu.
        if (PlayerPrefs.GetInt(PREF_RESET_VERSION, 0) < LEVEL_RESET_VERSION)
        {
            PlayerPrefs.SetInt(PREF_LEVEL, 0);                         // level 1 (index 0)
            PlayerPrefs.SetInt(PREF_RESET_VERSION, LEVEL_RESET_VERSION);
            PlayerPrefs.Save();
        }

        currentLevelIndex = PlayerPrefs.GetInt(PREF_LEVEL, 0);
        LoadCurrentLevel();
    }

    private void Update()
    {
        if (!timerRunning || levelComplete || IsSettingsOpen) return;
        if (GridManager.Instance != null && GridManager.Instance.IsExplodingLayer) return; // Kanca animasyonu sırasında süre durur
        if (ControlButton.AdPanelOpen) return; // reklam paneli açıkken süre durur (win/lose gibi)
        remainingTime -= Time.deltaTime;
        UIManager.Instance?.UpdateTimer(remainingTime, totalTime);
        if (remainingTime <= 0f) { remainingTime = 0f; timerRunning = false; OnTimerExpired(); }
    }

    public void ToggleVibration()
    {
        IsVibrationEnabled = !IsVibrationEnabled;
        PlayerPrefs.SetInt(PREF_VIBRATION, IsVibrationEnabled ? 1 : 0);
        PlayerPrefs.Save();
        AudioManager.Instance?.PlayButtonClickSound();
    }

    public void ToggleAudio()
    {
        IsAudioEnabled = !IsAudioEnabled;
        PlayerPrefs.SetInt(PREF_AUDIO, IsAudioEnabled ? 1 : 0);
        PlayerPrefs.Save();
        ApplyAudioSetting();
        AudioManager.Instance?.PlayButtonClickSound();
    }

    private void ApplyAudioSetting()
    {
        AudioListener.volume = IsAudioEnabled ? 1f : 0f;
    }

    // ─── Level Loading ────────────────────────────────────────────────────────

    private void LoadCurrentLevel()
    {
        if (levelOrder == null || levelOrder.levels.Count == 0) return;
        
        int startIndex = currentLevelIndex;
        while (currentLevelIndex < 0 || currentLevelIndex >= levelOrder.levels.Count || levelOrder.levels[currentLevelIndex] == null)
        {
            currentLevelIndex = (currentLevelIndex + 1) % levelOrder.levels.Count;
            if (currentLevelIndex == startIndex)
            {
                break;
            }
        }

        currentLevelIndex = Mathf.Clamp(currentLevelIndex, 0, levelOrder.levels.Count - 1);
        var level = levelOrder.levels[currentLevelIndex];
        if (level == null) return;
        StartLevel(level);
        LevelManager.Instance?.LoadLevel(level);
    }

    private void StartLevel(LevelData level)
    {
        levelComplete        = false;
        winLocked          = false;
        pendingWin         = false;
        blockingAnimations = 0;
        Score              = 0;
        currentTargetScore = level.targetScore;

        // Eğer seviye süresi ayarlanmamışsa (0 veya daha az ise), test edebilmeniz ve oynayabilmeniz için varsayılan olarak 60 saniye süre veriyoruz.
        float limit = level.timeLimit > 0f ? level.timeLimit : 60f;
        totalTime          = limit;
        remainingTime      = limit;
        timerRunning       = true;

        UIManager.Instance?.OnLevelStart(currentTargetScore, remainingTime);

        // Analitik: level başladı (bkz. FirebaseManager / FirestoreAnalytics).
        FirebaseManager.Instance?.LogLevelStart(currentLevelIndex);
    }

    // ─── Win / Lose ───────────────────────────────────────────────────────────

    public void CheckWin()
    {
        if (levelComplete) return;

        bool won = false;
        if (GridManager.Instance != null && GridManager.Instance.IsComplete())
        {
            won = true;
        }

        if (!won) return;
        Debug.Log($"[WIN_TIMING] CheckWin → won=true — t={Time.time:F3}");
        levelComplete = true;
        timerRunning  = false;

        // Analitik: level kazanıldı. Geçen süre = toplam - kalan.
        int winMoves = FirestoreAnalytics.Instance != null ? FirestoreAnalytics.Instance.CurrentLevelMoves : -1;
        FirebaseManager.Instance?.LogLevelComplete(currentLevelIndex, totalTime - remainingTime, remainingTime, winMoves);

        // Parça yerleşim/merge animasyonu devam ediyorsa bitene kadar bekle
        if (blockingAnimations > 0)
        {
            Debug.Log($"[WIN_TIMING] blockingAnimations={blockingAnimations}, pendingWin bekliyor — t={Time.time:F3}");
            pendingWin = true;
        }
        else
        {
            TriggerWinPanel();
        }
    }

    /// <summary>
    /// Win panelini erteleyen bir animasyon (parça yerleşimi, merge/line-clear...) başladığında çağrılır.
    /// </summary>
    public void BeginBlockingAnimation() => blockingAnimations++;

    /// <summary>
    /// BeginBlockingAnimation ile başlatılan animasyon bittiğinde çağrılır. Tüm bloklayan
    /// animasyonlar bitip kazanma beklemedeyse win panelini gösterir.
    /// </summary>
    public void EndBlockingAnimation()
    {
        blockingAnimations = Mathf.Max(0, blockingAnimations - 1);
        if (blockingAnimations == 0 && pendingWin)
        {
            Debug.Log($"[WIN_TIMING] EndBlockingAnimation → pendingWin çözüldü — t={Time.time:F3}");
            pendingWin = false;
            TriggerWinPanel();
        }
    }

    private void TriggerWinPanel()
    {
        Debug.Log($"[WIN_TIMING] TriggerWinPanel — t={Time.time:F3}");
        if (CurrentLevelNumber > 5 && CurrentLevelNumber % 5 == 0)
        {
            InterstitialAds.Show(() => UIManager.Instance?.ShowWinPanel(Score));
        }
        else
        {
            UIManager.Instance?.ShowWinPanel(Score);
        }
    }

    /// <summary>
    /// Seviyenin SON katmanı temizlenmeye başladığında (kanca animasyonu başlar başlamaz)
    /// çağrılır — bkz. GridManager.ExplodeLayer. Sayaç anında durur, çünkü bu noktadan
    /// sonra seviye kazanılmıştır ve geri dönüşü yoktur; win paneli yalnızca animasyon
    /// bitince açılacaktır.
    ///
    /// Bu olmadan ~3.65 sn'lik kanca + çökme animasyonu boyunca sayaç işlemeye devam
    /// ediyordu ve süre o sırada biterse KAZANILMIŞ seviyede fail paneli açılıyordu.
    /// </summary>
    public void FreezeTimerForPendingWin()
    {
        winLocked    = true;
        timerRunning = false;
        UIManager.Instance?.UpdateTimer(remainingTime, totalTime);
    }

    /// <summary>Seviyenin neden kaybedildiği — kaybetme panelindeki başlık buna göre değişir.</summary>
    public enum LoseReason
    {
        TimeUp,     // süre doldu
        NoMoves,    // eldeki hiçbir parça hiçbir katmana sığmıyor
    }

    public void GameOver(LoseReason reason = LoseReason.TimeUp)
    {
        // Son katman temizlenmeye başladıysa seviye fiilen kazanılmıştır; animasyon
        // sürerken gelen hiçbir kayıp sebebi (süre dolması, elde sığan parça kalmaması...)
        // kabul edilmez. pendingWin bu pencerede HENÜZ true olmaz (CheckWin animasyondan
        // sonra çalışır), o yüzden ayrı bir kilit gerekiyor.
        if (levelComplete || winLocked) return;
        levelComplete = true;
        timerRunning = false;

        // Analitik: level kaybedildi (süre doldu / hamle kalmadı, kaç hamlede ve hangi katmanda).
        int failMoves = FirestoreAnalytics.Instance != null ? FirestoreAnalytics.Instance.CurrentLevelMoves : -1;
        int failLayer = GridManager.Instance != null ? GridManager.Instance.ActiveLayerY : 0;
        FirebaseManager.Instance?.LogLevelFail(currentLevelIndex, totalTime - remainingTime, failMoves, failLayer);
        LevelManager.Instance?.MarkCurrentLevelFailedOrRetried();

        UIManager.Instance?.ShowLosePanel(Score, reason);
    }

    private void OnTimerExpired()
    {
        GameOver(LoseReason.TimeUp);
    }

    // ─── Navigation ──────────────────────────────────────────────────────────

    private bool isTransitioningLevel = false;

    public void NextLevel()
    {
        if (isTransitioningLevel) return;
        isTransitioningLevel = true;

        if (levelOrder == null || levelOrder.levels.Count == 0)
        {
            isTransitioningLevel = false;
            return;
        }

        // Sıradaki DOLU (null olmayan) leveli bul; boş slotları atla. LevelOrder'ın sonunda
        // boş slotlar var (henüz doldurulmamış), o yüzden "son level = son index" değil,
        // "son level = son dolu index". Sıradaki dolu level yoksa (son gerçek leveldeyiz)
        // currentLevelIndex değişmez → başa dönmek yerine AYNI leveli tekrar oynatır.
        for (int i = currentLevelIndex + 1; i < levelOrder.levels.Count; i++)
        {
            if (levelOrder.levels[i] != null)
            {
                currentLevelIndex = i;
                break;
            }
        }

        PlayerPrefs.SetInt(PREF_LEVEL, currentLevelIndex);
        PlayerPrefs.Save();
        
        // 1. Önce arka planda yeni bölüm yüklensin (3D tahta sahnede oluşur)
        LoadCurrentLevel();

        // 2. Eğer Zafer paneli açıksa: Arkada yeni bölüm hazır durumdayken paneli eriterek kapat!
        if (UIManager.Instance != null && UIManager.Instance.IsWinPanelActive)
        {
            UIManager.Instance.AnimateWinPanelExit(() =>
            {
                isTransitioningLevel = false;
            });
        }
        else
        {
            isTransitioningLevel = false;
        }
    }

    public void RetryLevel()
    {
        // Reklam açıkken/hemen sonrasında gelen kaza tıkı leveli restart etmesin
        if (ControlButton.AdInputBlocked) return;
        if (isTransitioningLevel) return;
        isTransitioningLevel = true;

        // Analitik: oyuncu bu leveli yeniden denedi (retries++). Ardından LoadCurrentLevel
        // → StartLevel yeni bir attempt (LogLevelStart) olarak da kaydeder.
        FirebaseManager.Instance?.LogLevelRetry(currentLevelIndex);
        LevelManager.Instance?.MarkCurrentLevelFailedOrRetried();
        
        // 1. Önce arka planda seviye sıfırlanıp yüklensin (3D tahta sahnede oluşur)
        LoadCurrentLevel();

        // 2. Paneller açıksa: Arkada seviye hazır durumdayken paneli eriterek kapat!
        if (UIManager.Instance != null && UIManager.Instance.IsLosePanelActive)
        {
            UIManager.Instance.AnimateLosePanelExit(() =>
            {
                isTransitioningLevel = false;
            });
        }
        else if (UIManager.Instance != null && UIManager.Instance.IsWinPanelActive)
        {
            UIManager.Instance.AnimateWinPanelExit(() =>
            {
                isTransitioningLevel = false;
            });
        }
        else
        {
            isTransitioningLevel = false;
        }
    }

    // ─── Scoring ─────────────────────────────────────────────────────────────

    public void AddScore(int points)
    {
        Score += points;
        UIManager.Instance?.AnimateScore(Score);
    }

    public void DeductScore(int points)
    {
        Score = Mathf.Max(0, Score - points);
        UIManager.Instance?.AnimateScore(Score);
    }

    public void OnLinesCleared(int cellsCleared, int bonusLines)
    {
        int gained = cellsCleared * pointsPerCell;
        Score += gained;
        UIManager.Instance?.AnimateScore(Score);
        CheckWin();
    }
}
