using UnityEngine;

[DefaultExecutionOrder(10)] // LevelManager'dan sonra çalışır (LevelManager Awake'i tamamlamış olur)
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("UI")]
    public GameObject winPanel;
    public TMPro.TextMeshProUGUI scoreText;

    [Header("Scoring")]
    public int pointsPerCell = 10;
    public int bonusPerLine  = 50;

    [Header("Level Order")]
    public LevelOrderData levelOrder;

    public int Score { get; private set; }

    private bool levelComplete;
    private int  currentLevelIndex;

    private const string PREF_LEVEL = "CurrentLevelIndex";

    private void Awake() { Instance = this; }

    private void Start()
    {
        levelComplete = false;
        Score = 0;
        UpdateScoreUI();
        if (winPanel != null) winPanel.SetActive(false);

        currentLevelIndex = PlayerPrefs.GetInt(PREF_LEVEL, 0);
        LoadCurrentLevel();
    }

    private void LoadCurrentLevel()
    {
        if (levelOrder == null || levelOrder.levels.Count == 0) return;
        currentLevelIndex = Mathf.Clamp(currentLevelIndex, 0, levelOrder.levels.Count - 1);
        var level = levelOrder.levels[currentLevelIndex];
        if (level != null) LevelManager.Instance?.LoadLevel(level);
    }

    public void CheckWin()
    {
        if (levelComplete) return;
        if (GridManager.Instance == null || !GridManager.Instance.IsComplete()) return;
        levelComplete = true;
        Debug.Log($"Level {currentLevelIndex + 1} tamamlandı!");
        if (winPanel != null) winPanel.SetActive(true);
    }

    // Win panelindeki "Sonraki Level" butonuna bağla
    public void NextLevel()
    {
        if (levelOrder == null || levelOrder.levels.Count == 0) return;

        currentLevelIndex++;
        if (currentLevelIndex >= levelOrder.levels.Count)
            currentLevelIndex = 0;

        PlayerPrefs.SetInt(PREF_LEVEL, currentLevelIndex);
        PlayerPrefs.Save();

        levelComplete = false;
        Score = 0;
        UpdateScoreUI();
        if (winPanel != null) winPanel.SetActive(false);

        LoadCurrentLevel();
    }

    public void OnLinesCleared(int cellsCleared, int bonusLines)
    {
        int gained = cellsCleared * pointsPerCell + bonusLines * bonusPerLine;
        Score += gained;
        UpdateScoreUI();
        Debug.Log($"Renk bonusu! {bonusLines} çizgi — +{gained} puan (toplam: {Score})");
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null) scoreText.text = Score.ToString();
    }
}
