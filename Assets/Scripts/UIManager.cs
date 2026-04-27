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

    private void Awake() { Instance = this; }

    // ─── Level Start ──────────────────────────────────────────────────────────

    public void OnLevelStart(int targetScore, float timeLimit)
    {
        HidePanelsImmediate();
        displayedScore = 0;
        if (scoreText) scoreText.text = "0";
        SetTargetScore(targetScore);
        UpdateTimer(timeLimit, timeLimit);
    }

    // ─── Score ────────────────────────────────────────────────────────────────

    public void AnimateScore(int newTotal)
    {
        scoreTween?.Kill();
        int from = displayedScore;
        scoreTween = DOTween.To(
            ()  => from,
            x   => { from = x; displayedScore = x; if (scoreText) scoreText.text = x.ToString(); },
            newTotal, 0.4f
        ).SetEase(Ease.OutCubic);

        if (scoreText)
            scoreText.rectTransform
                .DOScale(1.28f, 0.12f).SetEase(Ease.OutBack)
                .OnComplete(() => scoreText.rectTransform.DOScale(1f, 0.10f));
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
            timerText.color = Color.white;
        }
    }

    // ─── Win / Lose ───────────────────────────────────────────────────────────

    public void ShowWinPanel(int finalScore)
    {
        StopTimerPulse();
        if (winFinalScoreText) winFinalScoreText.text = $"Puan: {finalScore}";

        winOverlay.gameObject.SetActive(true);
        winOverlay.alpha   = 0f;
        winCard.localScale = Vector3.zero;

        var seq = DOTween.Sequence();
        seq.Append(winOverlay.DOFade(1f, 0.22f));
        seq.Join(winCard.DOScale(1f, 0.42f).SetEase(Ease.OutBack));
        seq.AppendInterval(0.08f);
        seq.Append(winCard.DOPunchScale(Vector3.one * 0.07f, 0.3f, 5, 0.5f));
    }

    public void ShowLosePanel(int finalScore)
    {
        StopTimerPulse();
        if (loseFinalScoreText) loseFinalScoreText.text = $"Puan: {finalScore}";

        loseOverlay.gameObject.SetActive(true);
        loseOverlay.alpha   = 0f;
        loseCard.localScale = new Vector3(1f, 0f, 1f);

        var seq = DOTween.Sequence();
        seq.Append(loseOverlay.DOFade(1f, 0.22f));
        seq.Join(loseCard.DOScaleY(1f, 0.32f).SetEase(Ease.OutBack));
    }

    private void HidePanelsImmediate()
    {
        if (winOverlay)  { winOverlay.alpha  = 0f; winOverlay.gameObject.SetActive(false); }
        if (loseOverlay) { loseOverlay.alpha = 0f; loseOverlay.gameObject.SetActive(false); }
    }

    private void OnDestroy()
    {
        scoreTween?.Kill();
        timerPulseTween?.Kill();
    }
}
