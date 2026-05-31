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
    public Color buttonCompleteColor = new Color(0.4f, 1f, 0.6f, 1f);

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
            backButton.onClick.AddListener(ClosePanel);
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
        int layerCount = maxY - minY + 1;

        if (layerCount <= 0) return;

        float spacing = 20f;
        float btnHeight = 60f;
        float totalHeight = (layerCount * btnHeight) + ((layerCount - 1) * spacing);
        float startY = (totalHeight / 2f) - (btnHeight / 2f);

        for (int i = 0; i < layerCount; i++)
        {
            int layerY = minY + i;
            
            GameObject btnObj = new GameObject($"Btn_Layer_{layerY}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(uiCanvas.transform, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-50, startY - ((layerCount - 1 - i) * (btnHeight + spacing)));
            rt.sizeDelta = new Vector2(60, 60);

            Image img = btnObj.GetComponent<Image>();
            img.color = buttonNormalColor;

            GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Text));
            textObj.transform.SetParent(btnObj.transform, false);

            RectTransform txtRt = textObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;

            UnityEngine.UI.Text tmp = textObj.GetComponent<UnityEngine.UI.Text>();
            tmp.text = (i + 1).ToString();
            tmp.alignment = TextAnchor.MiddleCenter;
            tmp.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            tmp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tmp.fontSize = 24;

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
        int minY = grid.GridMinY;

        for (int i = 0; i < layerButtons.Count; i++)
        {
            if (layerButtons[i] == null) continue;
            int layerY = minY + i;
            Image img = layerButtons[i].GetComponent<Image>();
            if (img == null) continue;

            if (layerY < grid.ActiveLayerY)
                img.color = buttonCompleteColor;
            else if (layerY == grid.ActiveLayerY)
                img.color = buttonActiveColor;
            else
                img.color = buttonNormalColor;
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

    public void ClosePanel()
    {
        if (isTransitioning || cam == null) return;
        if (!cam.IsInPanelMode) return;

        isTransitioning = true;
        if (backButton != null) backButton.gameObject.SetActive(false);

        cam.ReturnTo3D(() =>
        {
            isTransitioning = false;
            SetButtonsVisible(true);
            SetBottomPanelVisible(false);
            RefreshButtonColors();
            if (grid != null) grid.RefreshLayerVisibility();
        });
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
