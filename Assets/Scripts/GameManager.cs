using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("UI")]
    public GameObject winPanel;

    private bool levelComplete;

    private void Awake() { Instance = this; }

    private void Start()
    {
        levelComplete = false;
        if (winPanel != null) winPanel.SetActive(false);
    }

    public void CheckWin()
    {
        if (levelComplete) return;
        if (GridManager.Instance == null || !GridManager.Instance.IsComplete()) return;

        levelComplete = true;
        Debug.Log("Tebrikler! Puzzle tamamlandı.");
        if (winPanel != null) winPanel.SetActive(true);
    }
}
