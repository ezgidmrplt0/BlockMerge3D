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

    private bool  levelComplete;
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
        pendingWin         = false;
        blockingAnimations = 0;
        Score              = 0;
        currentTargetScore = level.targetScore;
        totalTime          = level.timeLimit;
        remainingTime      = level.timeLimit;
        timerRunning       = level.timeLimit > 0f;
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

    public void GameOver()
    {
        if (levelComplete) return;
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
