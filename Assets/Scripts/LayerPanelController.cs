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
    public Color buttonActiveColor   = new Color(0.4f, 0.8f, 1f, 1f);
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

    private void Awake() { Instance = this; }

    private void Start()
    {
        grid = GridManager.Instance;
        cam  = CameraOrbit.Instance;

        if (backButton != null)
        {
            backButton.onClick.AddListener(() => ClosePanel());
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

        int minY = grid.GridMinY;
        int maxY = grid.GridMaxY;
        
        List<int> activeLayers = new List<int>();
        for (int y = minY; y <= maxY; y++)
        {
            // Only count layers that have at least one shape/target cell or occupied cell
            bool hasCells = false;
            foreach (var cell in grid.TargetCells)
            {
                if (cell.y == y) { hasCells = true; break; }
            }
            if (!hasCells)
            {
                foreach (var cell in grid.occupiedCells)
                {
                    if (cell.y == y) { hasCells = true; break; }
                }
            }

            if (hasCells && !grid.IsLayerCleared(y))
            {
                activeLayers.Add(y);
            }
        }

        int layerCount = activeLayers.Count;
        if (layerCount <= 0) return;

        float spacing = buttonSpacing;
        float btnHeight = buttonSize.y;
        float totalHeight = (layerCount * btnHeight) + ((layerCount - 1) * spacing);
        float startY = (totalHeight / 2f) - (btnHeight / 2f);

        for (int i = 0; i < layerCount; i++)
        {
            int layerY = activeLayers[i];
            
            GameObject btnObj = new GameObject($"Btn_Layer_{layerY}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(uiCanvas.transform, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-50, startY - ((layerCount - 1 - i) * (btnHeight + spacing)));
            rt.sizeDelta = buttonSize;

            Image img = btnObj.GetComponent<Image>();
            img.color = buttonNormalColor;
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
            tmp.text = (i + 1).ToString(); // Show sequential user-friendly indices (e.g. 1, 2, 3...)
            tmp.alignment = TextAnchor.MiddleCenter;
            tmp.color = buttonTextColor;
            tmp.font = buttonFont != null ? buttonFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tmp.fontSize = buttonFontSize;

            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.AddListener(() => OpenPanel(layerY));
            layerButtons.Add(btn);
        }

        RefreshButtonColors();

        SetBottomPanelVisible(false);
    }

    public void RefreshButtonColors()
    {
        if (grid == null) return;

        for (int i = 0; i < layerButtons.Count; i++)
        {
            if (layerButtons[i] == null) continue;
            
            string name = layerButtons[i].name;
            if (name.StartsWith("Btn_Layer_") && int.TryParse(name.Substring(10), out int layerY))
            {
                Image img = layerButtons[i].GetComponent<Image>();
                if (img != null)
                {
                    if (layerY == grid.ActiveLayerY)
                    {
                        img.color = buttonActiveColor;
                    }
                    else
                    {
                        img.color = buttonNormalColor;
                    }
                }
            }
        }
    }

    public void OpenPanel(int layerY)
    {
        if (isTransitioning || cam == null || grid == null) return;
        if (cam.IsInPanelMode) return;

        isTransitioning = true;

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
        if (count > 0) layerCenter /= count;

        cam.ZoomToLayer(layerCenter, () => isTransitioning = false);

        grid.SetActiveLayer(layerY);
        RefreshButtonColors();

        SetButtonsVisible(false);
        if (backButton != null) backButton.gameObject.SetActive(true);
        SetBottomPanelVisible(true);
    }

    public void ClosePanel(System.Action onComplete = null)
    {
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
            SetButtonsVisible(true);
            RefreshButtonColors();
            if (grid != null) grid.RefreshLayerVisibility();
            onComplete?.Invoke();
        });
    }

    public void ResetPanel()
    {
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
    }

    private void SetButtonsVisible(bool visible)
    {
        foreach (var b in layerButtons)
            if (b != null) b.gameObject.SetActive(visible);
    }
}
