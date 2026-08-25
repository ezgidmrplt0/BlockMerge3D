using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class LayerPanelController : MonoBehaviour
{
    public static LayerPanelController Instance { get; private set; }

    [Header("UI References")]
    public Canvas uiCanvas;

    [Header("Button Style")]
    public Color buttonNormalColor   = new Color(0.9f, 0.9f, 1f, 0.85f);
    public Color buttonActiveColor   = new Color(1.0f, 0.85f, 0.25f, 1f);
    public Color buttonLockedColor   = new Color(0.35f, 0.35f, 0.4f, 0.4f);
    public Color buttonCompletedColor = new Color(0.22f, 0.78f, 0.42f, 1f);
    public Sprite buttonSprite;

    [Header("Button Typography & Layout")]
    public Font buttonFont;
    public int buttonFontSize = 24;
    public Color buttonTextColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    public Vector2 buttonSize = new Vector2(60, 60);
    public float buttonSpacing = 20f;

    [Header("Panel UI")]
    public Button backButton;

    private GridManager   grid;
    private CameraOrbit   cam;
    private List<Button>  layerButtons = new List<Button>();
    private bool          isTransitioning;
    private int           initialTotalLayers = 0;

    private void Awake() { Instance = this; }

    /// <summary>Öğretici göstergesinin (bkz. TutorialOverlay) parmağı hangi butonun
    /// üzerine koyacağını bilmesi için: doldurulması gereken İLK katmanın butonu.</summary>
    public RectTransform FirstLayerButton =>
        layerButtons.Count > 0 && layerButtons[0] != null
            ? layerButtons[0].transform as RectTransform
            : null;

    private void Start()
    {
        grid = GridManager.Instance;
        cam  = CameraOrbit.Instance;

        if (backButton != null)
        {
            backButton.onClick.AddListener(() => {
                AudioManager.Instance?.PlayBackButtonSound();
                ClosePanel();
            });
            backButton.gameObject.SetActive(false);
        }

        SetBottomPanelVisible(false);

        Invoke(nameof(BuildLayerButtons), 0.2f);
    }

    public void BuildLayerButtons()
    {
        grid = GridManager.Instance;
        if (grid == null || uiCanvas == null) return;

        foreach (var b in layerButtons)
            if (b != null) Destroy(b.gameObject);
        layerButtons.Clear();

        int currentRemainingLayers = (grid.GridMaxY - grid.GridMinY + 1);
        if (initialTotalLayers <= 0 || currentRemainingLayers > initialTotalLayers)
        {
            initialTotalLayers = currentRemainingLayers;
        }

        int completedLayersCount = Mathf.Max(0, initialTotalLayers - currentRemainingLayers);
        int totalButtonsToCreate = initialTotalLayers;
        if (totalButtonsToCreate <= 0) return;

        float spacing = buttonSpacing;
        float btnHeight = buttonSize.y;
        float totalHeight = (totalButtonsToCreate * btnHeight) + ((totalButtonsToCreate - 1) * spacing);
        float startY = (totalHeight / 2f) - (btnHeight / 2f);

        for (int i = 0; i < totalButtonsToCreate; i++)
        {
            int layerY = grid.GridMaxY - (i - completedLayersCount);
            
            GameObject btnObj = new GameObject($"Btn_Layer_Idx_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(uiCanvas.transform, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-50, startY - (i * (btnHeight + spacing)));
            rt.sizeDelta = buttonSize;

            Image img = btnObj.GetComponent<Image>();
            if (buttonSprite != null)
            {
                img.sprite = buttonSprite;
                img.type = Image.Type.Sliced;
            }

            GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Text));
            textObj.transform.SetParent(btnObj.transform, false);

            RectTransform txtRt = textObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;

            UnityEngine.UI.Text tmp = textObj.GetComponent<UnityEngine.UI.Text>();
            tmp.alignment = TextAnchor.MiddleCenter;
            tmp.font = buttonFont != null ? buttonFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tmp.fontSize = buttonFontSize;

            Button btn = btnObj.GetComponent<Button>();

            if (i < completedLayersCount)
            {
                // Tamamlanan katman: Tik işareti (✓) ve yeşil renk
                tmp.text = "✓";
                tmp.color = Color.white;
                img.color = buttonCompletedColor;
                btn.interactable = false;
            }
            else if (i == completedLayersCount)
            {
                // Aktif katman: Katman numarası ve aktif altın renk
                tmp.text = (i + 1).ToString();
                tmp.color = buttonTextColor;
                img.color = buttonActiveColor;
                btn.interactable = true;
                int targetY = layerY;
                btn.onClick.AddListener(() => {
                    AudioManager.Instance?.PlayLayerButtonSound();
                    OpenPanel(targetY);
                });
            }
            else
            {
                // Kilitli / Gelecek katman: Karartılmış renk
                tmp.text = (i + 1).ToString();
                tmp.color = new Color(buttonTextColor.r, buttonTextColor.g, buttonTextColor.b, 0.5f);
                img.color = buttonLockedColor;
                btn.interactable = false;
            }

            layerButtons.Add(btn);
        }
    }

    public void RefreshButtonColors()
    {
        BuildLayerButtons();
    }

    public void OpenPanel(int layerY)
    {
        // Reklam paneli açıkken (ve kapanışından hemen sonra) katman girişi yok sayılır —
        // reklamı açan/kapatan tık arkadaki butonlara sızıyordu.
        if (ControlButton.AdInputBlocked) return;
        if (isTransitioning || cam == null || grid == null) return;
        if (cam.IsInPanelMode && grid.ActiveLayerY == layerY)
        {
            SetBottomPanelVisible(true);
            return;
        }
        if (grid.IsExplodingLayer) return;
        if (GameManager.Instance != null && GameManager.Instance.IsLevelOver) return;

        // Sıralı katman kuralı: sadece şu an doldurulması gereken katman (grid.ActiveLayerY)
        // açılabilir. Butonlar zaten bunun dışındakiler için disabled (bkz. BuildLayerButtons/
        // RefreshButtonColors), bu ikinci bir güvenlik katmanı.
        if (layerY != grid.ActiveLayerY) return;

        // Tutorial check: Sadece TapLayerButton, DragPieceToBoard veya DragPieceToHold adımındayken ve hedeflenen katman ise geçişe izin ver
        if (TutorialOverlay.Instance != null && TutorialOverlay.Instance.IsRunning)
        {
            var tutStep = TutorialOverlay.Instance.CurrentStep;
            if (tutStep != TutorialStepType.TapLayerButton &&
                tutStep != TutorialStepType.DragPieceToBoard &&
                tutStep != TutorialStepType.DragPieceToHold)
                return;

            // Katmanlar artık üstten alta dolduruluyor — öğreticinin hedeflediği ilk katman
            // GridMinY değil GridMaxY.
            if (layerY != grid.GridMaxY)
                return;
        }

        isTransitioning = true;
        CancelInvoke(nameof(BuildLayerButtons));
        TutorialEvents.RaiseLayerOpened();

        float step = grid.Step;
        Vector3 layerCenter = Vector3.zero;
        int count = 0;
        foreach (var cell in grid.TargetCells)
        {
            if (cell.y == layerY)
            {
                layerCenter += grid.CellToWorld(cell);
                count++;
            }
        }
        if (count == 0)
        {
            foreach (var cell in grid.occupiedCells)
            {
                if (cell.y == layerY)
                {
                    layerCenter += grid.CellToWorld(cell);
                    count++;
                }
            }
        }

        if (count > 0)
        {
            layerCenter /= count;
        }
        else
        {
            layerCenter = grid.CellToWorld(new Vector3Int(0, layerY, 0));
        }

        cam.ZoomToLayer(layerCenter, () => isTransitioning = false);

        grid.SetActiveLayer(layerY);
        RefreshButtonColors();

        SetButtonsVisible(true);
        if (backButton != null) backButton.gameObject.SetActive(false);
        SetBottomPanelVisible(true);
    }

    public void ClosePanel(System.Action onComplete = null)
    {
        // Reklam paneli AÇIKKEN (reklamı açan tık sızması) ve kapanışından hemen SONRA
        // (kapatan tık sızması) kullanıcı kaynaklı kapatmayı yok say.
        if (onComplete == null && ControlButton.AdInputBlocked) return;

        // Tutorial kilitliyken panel kullanıcı tarafından (onComplete == null) kapatılamaz
        if (onComplete == null && TutorialOverlay.Instance != null && TutorialOverlay.Instance.IsRunning)
            return;

        if (!cam.IsInPanelMode)
        {
            isTransitioning = false;
            onComplete?.Invoke();
            return;
        }

        isTransitioning = true;

        if (backButton != null) backButton.gameObject.SetActive(false);
        SetButtonsVisible(false);
        SetBottomPanelVisible(false);

        cam.ReturnTo3D(() =>
        {
            isTransitioning = false;
            if (grid != null && !grid.IsExplodingLayer)
            {
                SetButtonsVisible(true);
                RefreshButtonColors();
            }
            if (grid != null) grid.RefreshLayerVisibility();
            onComplete?.Invoke();
        });
    }

    public void ResetPanel()
    {
        CancelInvoke(nameof(BuildLayerButtons));
        isTransitioning = false;
        if (backButton != null) backButton.gameObject.SetActive(false);
        SetButtonsVisible(true);
        SetBottomPanelVisible(false);
        BuildLayerButtons();
        GridManager.Instance?.RefreshLayerVisibility();
    }

    private void SetBottomPanelVisible(bool visible)
    {
        var lm = LevelManager.Instance;
        if (lm != null && lm.pieceCards != null && lm.pieceCards.Count > 0)
        {
            var firstCard = lm.pieceCards[0];
            if (firstCard != null && firstCard.transform.parent != null)
            {
                firstCard.transform.parent.gameObject.SetActive(visible);
            }
        }

        // Sıradaki parça önizlemesini kart paneliyle birlikte göster/gizle.
        // İSİMLE ARAMA YOK: panel çalışma zamanında yeniden üretiliyor ve seviye
        // geçişinde eskisi Destroy ile işaretlenmiş ama hâlâ hiyerarşide oluyor;
        // Transform.Find o eskisini döndürüp yenisini görünür bırakıyordu.
        var previewPanel = LevelManager.Instance != null
            ? LevelManager.Instance.NextPiecePreviewPanel
            : null;
        if (previewPanel != null) previewPanel.SetActive(visible);

        var holdPanel = LevelManager.Instance != null
            ? LevelManager.Instance.HoldSlotPanel
            : null;
        if (holdPanel != null) holdPanel.SetActive(visible);
    }

    public void SetButtonsVisible(bool visible)
    {
        foreach (var b in layerButtons)
            if (b != null) b.gameObject.SetActive(visible);

        if (!visible && backButton != null)
        {
            backButton.gameObject.SetActive(false);
        }
    }
}
