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

    private bool  levelComplete;
    private bool  pendingWin;          // Merge animasyonu biterken kazanma bekliyor
    private bool  mergeAnimating;      // Şu an merge animasyonu devam ediyor
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
        mergeAnimating     = false;
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
        bool won = currentTargetScore > 0
            ? Score >= currentTargetScore
            : (GridManager.Instance?.IsComplete() ?? false);
        if (!won) return;
        levelComplete = true;
        timerRunning  = false;

        // Merge animasyonu devam ediyorsa bitene kadar bekle
        if (mergeAnimating)
        {
            pendingWin = true;
        }
        else
        {
            UIManager.Instance?.ShowWinPanel(Score);
        }
    }

    /// <summary>
    /// GridManager merge animasyonları tamamlandığında çağrılır.
    /// Eğer bu sırada kazanma beklemedeyse win panelini gösterir.
    /// </summary>
    public void OnMergeAnimationComplete()
    {
        mergeAnimating = false;
        if (pendingWin)
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

        // Merge animasyonu başladı — win panel animasyon bitmeden gösterilmeyecek
        mergeAnimating = true;
        CheckWin();
    }
}
