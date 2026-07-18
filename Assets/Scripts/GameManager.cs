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

    /// <summary>Seviye bitti (kazanıldı ya da kaybedildi) — kazanma paneli açılmayı
    /// bekliyorsa da true. Oyun içi girdiler (parça sürükleme, tahta döndürme, joker)
    /// bu andan itibaren KAPANMALI: panel açıkken arkada oynanmaya devam edilebiliyordu.</summary>
    public bool IsLevelOver => levelComplete || winLocked;

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

    private void Awake() { Instance = this; }

    private void Start()
    {
        currentLevelIndex = PlayerPrefs.GetInt(PREF_LEVEL, 0);
        LoadCurrentLevel();
    }

    private void Update()
    {
        if (!timerRunning || levelComplete) return;
        remainingTime -= Time.deltaTime;
        UIManager.Instance?.UpdateTimer(remainingTime, totalTime);
        if (remainingTime <= 0f) { remainingTime = 0f; timerRunning = false; OnTimerExpired(); }
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
        LevelManager.Instance?.LoadLevel(level);
        StartLevel(level);
    }

    private void StartLevel(LevelData level)
    {
        levelComplete      = false;
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
    }

    // ─── Win / Lose ───────────────────────────────────────────────────────────

    public void CheckWin()
    {
        if (levelComplete) return;
        
        // Bölümü kazanmak için tüm katmanların (gridin tamamının) kusursuzca temizlenmiş olması gerekir.
        bool won = false;
        if (GridManager.Instance != null && GridManager.Instance.IsComplete())
        {
            won = true;
        }

        if (!won) return;
        levelComplete = true;
        timerRunning  = false;

        // Parça yerleşim/merge animasyonu devam ediyorsa bitene kadar bekle
        if (blockingAnimations > 0)
        {
            pendingWin = true;
        }
        else
        {
            UIManager.Instance?.ShowWinPanel(Score);
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
            pendingWin = false;
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

    public void GameOver()
    {
        // Son katman temizlenmeye başladıysa seviye fiilen kazanılmıştır; animasyon
        // sürerken gelen hiçbir kayıp sebebi (süre dolması, elde sığan parça kalmaması...)
        // kabul edilmez. pendingWin bu pencerede HENÜZ true olmaz (CheckWin animasyondan
        // sonra çalışır), o yüzden ayrı bir kilit gerekiyor.
        if (levelComplete || winLocked) return;
        levelComplete = true;
        timerRunning = false;
        UIManager.Instance?.ShowLosePanel(Score);
    }

    private void OnTimerExpired()
    {
        GameOver();
    }

    // ─── Navigation ──────────────────────────────────────────────────────────

    public void NextLevel()
    {
        if (levelOrder == null || levelOrder.levels.Count == 0) return;
        currentLevelIndex = (currentLevelIndex + 1) % levelOrder.levels.Count;
        PlayerPrefs.SetInt(PREF_LEVEL, currentLevelIndex);
        PlayerPrefs.Save();
        LoadCurrentLevel();
    }

    public void RetryLevel()
    {
        LoadCurrentLevel();
    }

    // ─── Scoring ─────────────────────────────────────────────────────────────

    public void OnLinesCleared(int cellsCleared, int bonusLines)
    {
        int gained = cellsCleared * pointsPerCell;
        Score += gained;
        UIManager.Instance?.AnimateScore(Score);
        CheckWin();
    }
}
