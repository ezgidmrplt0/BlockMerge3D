using UnityEngine;

[DefaultExecutionOrder(10)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Scoring")]
    public int pointsPerCell = 10;
    public int bonusPerLine  = 50;

    [Header("Level Order")]
    public LevelOrderData levelOrder;

    public int Score { get; private set; }

    private bool  levelComplete;
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
        UIManager.Instance?.ShowWinPanel(Score);
        Debug.Log($"Level {currentLevelIndex + 1} tamamlandı! Puan: {Score}");
    }

    private void OnTimerExpired()
    {
        if (levelComplete) return;
        levelComplete = true;
        UIManager.Instance?.ShowLosePanel(Score);
        Debug.Log($"Süre doldu! Puan: {Score} / Hedef: {currentTargetScore}");
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
        int gained = cellsCleared * pointsPerCell + bonusLines * bonusPerLine;
        Score += gained;
        UIManager.Instance?.AnimateScore(Score);
        Debug.Log($"Renk bonusu! {bonusLines} çizgi — +{gained} puan (toplam: {Score})");
        CheckWin();
    }
}
