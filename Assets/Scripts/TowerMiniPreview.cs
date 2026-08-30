using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections.Generic;
using System.Linq;

public class TowerMiniPreview : MonoBehaviour
{
    private static TowerMiniPreview _instance;
    public static TowerMiniPreview Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<TowerMiniPreview>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("[TowerMiniPreview_Manager]");
                    _instance = go.AddComponent<TowerMiniPreview>();
                }
            }
            return _instance;
        }
    }

    private GameObject previewStageRoot;
    private Camera previewCamera;
    private RenderTexture renderTexture;
    private RenderTexture expandedRenderTexture;
    private RawImage previewRawImage;
    private RectTransform previewCardRect;

    private GameObject miniTowerPivot;
    private GameObject miniTowerInstance;
    private int currentActiveFloorY = 0;
    private float autoRotateSpeed = 25f;

    private readonly Vector3 PreviewStagePos = new Vector3(300f, 300f, 300f);

    // Cell → renderer mapping (3D mini kule blokları)
    private Dictionary<Vector3Int, Renderer> cellRenderers = new Dictionary<Vector3Int, Renderer>();
    private Dictionary<Vector3Int, GameObject> cellGameObjects = new Dictionary<Vector3Int, GameObject>();
    private Dictionary<int, List<Vector3Int>> floorCells = new Dictionary<int, List<Vector3Int>>();

    // Expanded panel
    private bool isExpanded;
    private GameObject expandedPanel;
    private RectTransform expandedPanelRect;
    private RawImage expanded3DView;
    private GameObject layerGridContainer;
    private CanvasGroup expandedCanvasGroup;
    private Image dimOverlay;

    // Ghost renk sabitleri
    private static readonly Color GhostColor = new Color(0.35f, 0.40f, 0.50f, 0.45f);
    private static readonly Color IceColor = new Color(0.55f, 0.85f, 0.95f, 0.9f);

    private void Awake()
    {
        if (_instance == null) _instance = this;
        else if (_instance != this) { Destroy(gameObject); return; }

        EnsurePreviewStage();
        EnsureUIElement();
    }

    private void EnsurePreviewStage()
    {
        if (previewStageRoot != null && previewCamera != null && renderTexture != null) return;

        if (previewStageRoot == null)
        {
            previewStageRoot = new GameObject("[TowerMiniPreview_Stage]");
            previewStageRoot.transform.position = PreviewStagePos;
        }

        for (int li = previewStageRoot.transform.childCount - 1; li >= 0; li--)
        {
            var ch = previewStageRoot.transform.GetChild(li);
            if (ch.name.StartsWith("MiniTowerLight")) Destroy(ch.gameObject);
        }

        Vector3[] lightOffsets = new Vector3[]
        {
            new Vector3( 3f,  2f,  3f),
            new Vector3(-3f,  2f, -3f),
            new Vector3( 3f, -2f, -3f),
            new Vector3(-3f, -2f,  3f),
        };
        for (int li = 0; li < lightOffsets.Length; li++)
        {
            var lgo = new GameObject($"MiniTowerLight{li}");
            lgo.transform.SetParent(previewStageRoot.transform);
            lgo.transform.position = PreviewStagePos + lightOffsets[li];
            var pl = lgo.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.intensity = 1.8f;
            pl.range = 12f;
            pl.color = Color.white;
        }

        if (previewCamera == null)
        {
            GameObject camGo = new GameObject("MiniTowerCamera");
            camGo.transform.SetParent(previewStageRoot.transform);
            Quaternion camRot = Quaternion.Euler(-35f, 225f, 0f);
            camGo.transform.position = PreviewStagePos + camRot * new Vector3(0f, 0f, -6f);
            camGo.transform.rotation = camRot;

            previewCamera = camGo.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0, 0, 0, 0);
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 2.2f;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 30f;
        }

        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);
            renderTexture.antiAliasing = 2;
            renderTexture.Create();
        }

        if (expandedRenderTexture == null)
        {
            expandedRenderTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
            expandedRenderTexture.antiAliasing = 4;
            expandedRenderTexture.Create();
        }

        previewCamera.targetTexture = renderTexture;
    }

    private void EnsureUIElement()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        Transform existingCard = canvas.transform.Find("TowerMiniPreviewCard");
        if (existingCard != null)
        {
            previewCardRect = existingCard.GetComponent<RectTransform>();
            previewRawImage = existingCard.GetComponentInChildren<RawImage>();
            if (previewRawImage != null && renderTexture != null) previewRawImage.texture = renderTexture;
            EnsureButton(existingCard.gameObject);
            return;
        }

        GameObject cardGo = new GameObject("TowerMiniPreviewCard", typeof(RectTransform), typeof(Image));
        cardGo.transform.SetParent(canvas.transform, false);

        previewCardRect = cardGo.GetComponent<RectTransform>();
        previewCardRect.anchorMin = new Vector2(1f, 1f);
        previewCardRect.anchorMax = new Vector2(1f, 1f);
        previewCardRect.pivot = new Vector2(1f, 1f);
        previewCardRect.anchoredPosition = new Vector2(-12f, -350f);
        previewCardRect.sizeDelta = new Vector2(300f, 300f);

        Image bgImage = cardGo.GetComponent<Image>();
        Sprite cardSprite = FindCardSprite();
        if (cardSprite != null)
        {
            bgImage.sprite = cardSprite;
            bgImage.type = Image.Type.Sliced;
            bgImage.color = Color.white;
        }
        else
        {
            bgImage.color = new Color(0.15f, 0.55f, 0.85f, 1f);
        }
        bgImage.raycastTarget = true;

        var shadow = cardGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.4f);
        shadow.effectDistance = new Vector2(3f, -3f);

        GameObject rawGo = new GameObject("TowerView", typeof(RectTransform), typeof(RawImage));
        rawGo.transform.SetParent(cardGo.transform, false);

        var rawRect = rawGo.GetComponent<RectTransform>();
        rawRect.anchorMin = new Vector2(0.05f, 0.05f);
        rawRect.anchorMax = new Vector2(0.95f, 0.95f);
        rawRect.sizeDelta = Vector2.zero;

        previewRawImage = rawGo.GetComponent<RawImage>();
        previewRawImage.texture = renderTexture;
        previewRawImage.uvRect = new Rect(0f, 1f, 1f, -1f);
        previewRawImage.color = Color.white;

        EnsureButton(cardGo);
    }

    private void EnsureButton(GameObject cardGo)
    {
        if (cardGo.GetComponent<Button>() != null) return;
        var btn = cardGo.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(ToggleExpanded);
    }

    public void SetVisible(bool visible)
    {
        if (isExpanded) CollapseImmediate();
        if (previewCardRect != null)
            previewCardRect.gameObject.SetActive(visible);
    }

    private void Update()
    {
        if (miniTowerPivot != null)
        {
            miniTowerPivot.transform.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.Self);
        }
    }

    // ─── Initialize ─────────────────────────────────────────────────────────

    public void Initialize(GameObject mainShapePrefab, int activeLayerY)
    {
        EnsurePreviewStage();
        EnsureUIElement();

        if (miniTowerPivot != null)
            Destroy(miniTowerPivot);
        miniTowerInstance = null;

        cellRenderers.Clear();
        cellGameObjects.Clear();
        floorCells.Clear();
        currentActiveFloorY = activeLayerY;

        if (isExpanded) CollapseImmediate();

        if (mainShapePrefab == null) return;

        miniTowerPivot = new GameObject("MiniTower_Pivot");
        miniTowerPivot.transform.SetParent(previewStageRoot.transform);
        miniTowerPivot.transform.position = PreviewStagePos;

        miniTowerInstance = Instantiate(mainShapePrefab, Vector3.zero, Quaternion.identity, miniTowerPivot.transform);
        miniTowerInstance.name = "MiniTower_Model";

        foreach (var col in miniTowerInstance.GetComponentsInChildren<Collider>())
            Destroy(col);

        var allRenderers = miniTowerInstance.GetComponentsInChildren<Renderer>();
        if (allRenderers.Length > 0)
        {
            Bounds b = allRenderers[0].bounds;
            for (int i = 1; i < allRenderers.Length; i++) b.Encapsulate(allRenderers[i].bounds);
            miniTowerInstance.transform.position = PreviewStagePos - (b.center - miniTowerInstance.transform.position);

            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (previewCamera != null)
                previewCamera.orthographicSize = Mathf.Max(1.8f, maxDim * 0.85f);
        }

        var shapeHolder = miniTowerInstance.GetComponent<CubeShapeDataHolder>();
        float step = shapeHolder != null ? shapeHolder.Step : 1f;

        foreach (Transform child in miniTowerInstance.transform)
        {
            var rend = child.GetComponentInChildren<Renderer>(true);
            if (rend == null) continue;

            // localPosition prefab orijinine göre sabit — bounds kaydırmasından etkilenmez
            Vector3 lp = child.localPosition;
            Vector3Int cell = new Vector3Int(
                Mathf.RoundToInt(lp.x / step),
                Mathf.RoundToInt(lp.y / step),
                Mathf.RoundToInt(lp.z / step));

            cellRenderers[cell] = rend;
            cellGameObjects[cell] = child.gameObject;

            if (!floorCells.ContainsKey(cell.y))
                floorCells[cell.y] = new List<Vector3Int>();
            floorCells[cell.y].Add(cell);
        }

        RefreshAllCells();
    }

    // ─── Live State Sync ────────────────────────────────────────────────────

    public void SetActiveFloor(int activeFloorY)
    {
        currentActiveFloorY = activeFloorY;
        if (isExpanded) RefreshExpandedLayerGrids();
    }

    public void OnCellPlaced(Vector3Int cell, Color color)
    {
        SetCellColor(cell, color);
        if (isExpanded) RefreshExpandedLayerGrids();
    }

    public void OnCellRemoved(Vector3Int cell)
    {
        SetCellColor(cell, GhostColor);
        if (isExpanded) RefreshExpandedLayerGrids();
    }

    public void OnFloorDemolished(int demolishedFloorY)
    {
        if (floorCells.TryGetValue(demolishedFloorY, out var cells))
        {
            foreach (var cell in cells)
            {
                if (cellGameObjects.TryGetValue(cell, out var go) && go != null)
                {
                    go.transform.DOScale(Vector3.zero, 0.35f).SetEase(Ease.InBack).OnComplete(() =>
                    {
                        Destroy(go);
                    });
                }
                cellRenderers.Remove(cell);
                cellGameObjects.Remove(cell);
            }
            floorCells.Remove(demolishedFloorY);
        }

        if (previewCardRect != null)
            previewCardRect.DOPunchScale(Vector3.one * 0.18f, 0.25f, 6, 0.5f);

        if (isExpanded) RefreshExpandedLayerGrids();
    }

    // ─── Cell Coloring ──────────────────────────────────────────────────────

    private void RefreshAllCells()
    {
        var gm = GridManager.Instance;

        foreach (var kvp in cellRenderers)
        {
            Vector3Int cell = kvp.Key;
            Renderer r = kvp.Value;
            if (r == null) continue;

            Color col = GetCellStateColor(cell, gm);
            ApplyColor(r, col);
        }
    }

    private Color GetCellStateColor(Vector3Int cell, GridManager gm)
    {
        if (gm == null) return GhostColor;

        if (gm.occupiedCells.Contains(cell))
        {
            if (gm.GetCellColor(cell, out Color c))
                return c;
            return Color.white;
        }

        if (gm.frozenCells.Contains(cell))
            return IceColor;

        if (gm.IsCellPrefilled(cell))
        {
            if (gm.GetCellColor(cell, out Color c))
                return c;
            return new Color(0.55f, 0.50f, 0.45f, 1f);
        }

        return GhostColor;
    }

    private void SetCellColor(Vector3Int cell, Color color)
    {
        if (cellRenderers.TryGetValue(cell, out var r) && r != null)
            ApplyColor(r, color);
    }

    private void ApplyColor(Renderer r, Color col)
    {
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor("_BaseColor", col);
        block.SetColor("_Color", col);
        r.SetPropertyBlock(block);
    }

    // ─── Expand / Collapse ──────────────────────────────────────────────────

    private void ToggleExpanded()
    {
        if (isExpanded) Collapse();
        else Expand();
    }

    private void Expand()
    {
        if (isExpanded) return;
        isExpanded = true;

        previewCamera.targetTexture = expandedRenderTexture;

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        if (dimOverlay == null)
        {
            var dimGo = new GameObject("TowerPreviewDim", typeof(RectTransform), typeof(Image));
            dimGo.transform.SetParent(canvas.transform, false);
            var dimRect = dimGo.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.sizeDelta = Vector2.zero;
            dimOverlay = dimGo.GetComponent<Image>();
            dimOverlay.color = new Color(0, 0, 0, 0);
            dimOverlay.raycastTarget = true;

            var dimBtn = dimGo.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Collapse);
        }

        dimOverlay.gameObject.SetActive(true);
        dimOverlay.transform.SetAsLastSibling();
        dimOverlay.DOFade(0.6f, 0.25f);

        if (expandedPanel == null)
            BuildExpandedPanel(canvas);

        expandedPanel.SetActive(true);
        expandedPanel.transform.SetAsLastSibling();

        expandedCanvasGroup.alpha = 0f;
        expandedPanelRect.localScale = Vector3.one * 0.85f;
        expandedCanvasGroup.DOFade(1f, 0.25f);
        expandedPanelRect.DOScale(Vector3.one, 0.25f).SetEase(Ease.OutBack);

        RefreshExpandedLayerGrids();
    }

    private void Collapse()
    {
        if (!isExpanded) return;
        isExpanded = false;

        previewCamera.targetTexture = renderTexture;

        if (dimOverlay != null)
            dimOverlay.DOFade(0f, 0.2f).OnComplete(() => dimOverlay.gameObject.SetActive(false));

        if (expandedPanel != null && expandedCanvasGroup != null)
        {
            expandedCanvasGroup.DOFade(0f, 0.2f);
            expandedPanelRect.DOScale(Vector3.one * 0.85f, 0.2f).SetEase(Ease.InBack)
                .OnComplete(() => expandedPanel.SetActive(false));
        }
    }

    private void CollapseImmediate()
    {
        isExpanded = false;
        previewCamera.targetTexture = renderTexture;
        if (dimOverlay != null) dimOverlay.gameObject.SetActive(false);
        if (expandedPanel != null) expandedPanel.SetActive(false);
    }

    // ─── Expanded Panel Build ───────────────────────────────────────────────

    private void BuildExpandedPanel(Canvas canvas)
    {
        expandedPanel = new GameObject("TowerExpandedPreview", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        expandedPanel.transform.SetParent(canvas.transform, false);

        expandedPanelRect = expandedPanel.GetComponent<RectTransform>();
        expandedPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
        expandedPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
        expandedPanelRect.pivot = new Vector2(0.5f, 0.5f);
        expandedPanelRect.sizeDelta = new Vector2(380f, 620f);

        var panelBg = expandedPanel.GetComponent<Image>();
        panelBg.color = new Color(0.08f, 0.10f, 0.16f, 0.97f);

        var panelOutline = expandedPanel.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.35f, 0.75f, 1f, 0.7f);
        panelOutline.effectDistance = new Vector2(2f, -2f);

        expandedCanvasGroup = expandedPanel.GetComponent<CanvasGroup>();

        // Başlık
        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(expandedPanel.transform, false);
        var titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -8f);
        titleRect.sizeDelta = new Vector2(0f, 30f);

        var titleText = titleGo.AddComponent<TMPro.TextMeshProUGUI>();
        titleText.text = "KULE ÖNİZLEME";
        titleText.fontSize = 16f;
        titleText.alignment = TMPro.TextAlignmentOptions.Center;
        titleText.color = new Color(0.35f, 0.80f, 1f, 1f);
        titleText.fontStyle = TMPro.FontStyles.Bold;

        // 3D Kule Görünümü
        var view3DBg = new GameObject("Expanded3DBg", typeof(RectTransform), typeof(Image));
        view3DBg.transform.SetParent(expandedPanel.transform, false);
        var view3DBgRect = view3DBg.GetComponent<RectTransform>();
        view3DBgRect.anchorMin = new Vector2(0f, 1f);
        view3DBgRect.anchorMax = new Vector2(1f, 1f);
        view3DBgRect.pivot = new Vector2(0.5f, 1f);
        view3DBgRect.anchoredPosition = new Vector2(0f, -42f);
        view3DBgRect.sizeDelta = new Vector2(-24f, 320f);
        view3DBg.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 1f);
        var view3DBgOutline = view3DBg.AddComponent<Outline>();
        view3DBgOutline.effectColor = new Color(0.25f, 0.55f, 0.8f, 0.4f);
        view3DBgOutline.effectDistance = new Vector2(1f, -1f);

        var view3DGo = new GameObject("Expanded3DView", typeof(RectTransform), typeof(RawImage));
        view3DGo.transform.SetParent(expandedPanel.transform, false);
        var view3DRect = view3DGo.GetComponent<RectTransform>();
        view3DRect.anchorMin = new Vector2(0f, 1f);
        view3DRect.anchorMax = new Vector2(1f, 1f);
        view3DRect.pivot = new Vector2(0.5f, 1f);
        view3DRect.anchoredPosition = new Vector2(0f, -42f);
        view3DRect.sizeDelta = new Vector2(-24f, 320f);

        expanded3DView = view3DGo.GetComponent<RawImage>();
        expanded3DView.texture = expandedRenderTexture;
        expanded3DView.uvRect = new Rect(0f, 1f, 1f, -1f);
        expanded3DView.color = Color.white;

        // "KATMANLAR" başlık
        var layerTitleGo = new GameObject("LayerTitle", typeof(RectTransform));
        layerTitleGo.transform.SetParent(expandedPanel.transform, false);
        var layerTitleRect = layerTitleGo.GetComponent<RectTransform>();
        layerTitleRect.anchorMin = new Vector2(0f, 1f);
        layerTitleRect.anchorMax = new Vector2(1f, 1f);
        layerTitleRect.pivot = new Vector2(0.5f, 1f);
        layerTitleRect.anchoredPosition = new Vector2(0f, -370f);
        layerTitleRect.sizeDelta = new Vector2(0f, 24f);

        var layerTitleText = layerTitleGo.AddComponent<TMPro.TextMeshProUGUI>();
        layerTitleText.text = "KATMANLAR";
        layerTitleText.fontSize = 13f;
        layerTitleText.alignment = TMPro.TextAlignmentOptions.Center;
        layerTitleText.color = new Color(0.6f, 0.65f, 0.75f, 1f);
        layerTitleText.fontStyle = TMPro.FontStyles.Bold;

        // 2D Katman Grid Container (ScrollView)
        var scrollGo = new GameObject("LayerGridScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(expandedPanel.transform, false);
        var scrollRect = scrollGo.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(1f, 1f);
        scrollRect.offsetMin = new Vector2(12f, 12f);
        scrollRect.offsetMax = new Vector2(-12f, -398f);

        scrollGo.AddComponent<Mask>();
        var scrollBg = scrollGo.AddComponent<Image>();
        scrollBg.color = new Color(0, 0, 0, 0.01f);

        layerGridContainer = new GameObject("LayerGridContent", typeof(RectTransform),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        layerGridContainer.transform.SetParent(scrollGo.transform, false);
        var contentRect = layerGridContainer.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 0f);
        contentRect.anchorMax = new Vector2(0f, 1f);
        contentRect.pivot = new Vector2(0f, 0.5f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);

        var hlg = layerGridContainer.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.padding = new RectOffset(4, 4, 4, 4);

        var csf = layerGridContainer.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.content = contentRect;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 20f;
    }

    // ─── 2D Layer Grid Rendering ────────────────────────────────────────────

    private void RefreshExpandedLayerGrids()
    {
        if (layerGridContainer == null) return;

        for (int i = layerGridContainer.transform.childCount - 1; i >= 0; i--)
            Destroy(layerGridContainer.transform.GetChild(i).gameObject);

        if (floorCells.Count == 0) return;

        var gm = GridManager.Instance;
        var sortedLayers = floorCells.Keys.OrderByDescending(k => k).ToList();

        int globalMinX = int.MaxValue, globalMaxX = int.MinValue;
        int globalMinZ = int.MaxValue, globalMaxZ = int.MinValue;
        foreach (var kvp in floorCells)
        {
            foreach (var cell in kvp.Value)
            {
                if (cell.x < globalMinX) globalMinX = cell.x;
                if (cell.x > globalMaxX) globalMaxX = cell.x;
                if (cell.z < globalMinZ) globalMinZ = cell.z;
                if (cell.z > globalMaxZ) globalMaxZ = cell.z;
            }
        }

        int gridW = globalMaxX - globalMinX + 1;
        int gridH = globalMaxZ - globalMinZ + 1;

        float availableHeight = 150f;
        float cellPx = Mathf.Min(14f, (availableHeight - 28f) / Mathf.Max(gridW, gridH));
        cellPx = Mathf.Max(6f, cellPx);
        float gap = 1f;

        for (int li = 0; li < sortedLayers.Count; li++)
        {
            int layerY = sortedLayers[li];
            bool isActive = layerY == currentActiveFloorY;

            float totalW = gridW * (cellPx + gap) - gap;
            float totalH = gridH * (cellPx + gap) - gap;
            float cardW = totalW + 12f;
            float cardH = totalH + 32f;

            var layerCard = new GameObject($"LayerCard_{layerY}", typeof(RectTransform), typeof(Image));
            layerCard.transform.SetParent(layerGridContainer.transform, false);
            var cardRect = layerCard.GetComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(cardW, cardH);

            var cardBg = layerCard.GetComponent<Image>();
            cardBg.color = isActive
                ? new Color(0.12f, 0.18f, 0.25f, 0.95f)
                : new Color(0.10f, 0.12f, 0.18f, 0.8f);

            if (isActive)
            {
                var cardOutline = layerCard.AddComponent<Outline>();
                cardOutline.effectColor = new Color(0.35f, 0.75f, 1f, 0.8f);
                cardOutline.effectDistance = new Vector2(1.5f, -1.5f);
            }

            // Katman etiketi
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(layerCard.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -2f);
            labelRect.sizeDelta = new Vector2(0f, 16f);

            var labelText = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
            labelText.text = isActive ? $"▶ K{li + 1}" : $"K{li + 1}";
            labelText.fontSize = 11f;
            labelText.alignment = TMPro.TextAlignmentOptions.Center;
            labelText.color = isActive ? new Color(0.35f, 0.85f, 1f) : new Color(0.6f, 0.65f, 0.7f, 1f);
            labelText.fontStyle = TMPro.FontStyles.Bold;

            // Grid hücreleri
            float gridStartX = (cardW - totalW) * 0.5f;
            float gridStartY = -20f;

            foreach (var cell in floorCells[layerY])
            {
                int gx = cell.x - globalMinX;
                int gz = cell.z - globalMinZ;

                float px = gridStartX + gx * (cellPx + gap);
                float py = gridStartY - gz * (cellPx + gap);

                var cellGo = new GameObject("C", typeof(RectTransform), typeof(Image));
                cellGo.transform.SetParent(layerCard.transform, false);
                var cellRect = cellGo.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(0f, 1f);
                cellRect.anchorMax = new Vector2(0f, 1f);
                cellRect.pivot = new Vector2(0f, 1f);
                cellRect.anchoredPosition = new Vector2(px, py);
                cellRect.sizeDelta = new Vector2(cellPx, cellPx);

                var cellImg = cellGo.GetComponent<Image>();
                cellImg.color = GetCellStateColor(cell, gm);
            }
        }
    }

    // ─── Shift Support ──────────────────────────────────────────────────────

    public void OnLayersShifted(int removedLayerY)
    {
        var newCellRenderers = new Dictionary<Vector3Int, Renderer>();
        var newCellGameObjects = new Dictionary<Vector3Int, GameObject>();
        var newFloorCells = new Dictionary<int, List<Vector3Int>>();

        foreach (var kvp in cellRenderers)
        {
            Vector3Int cell = kvp.Key;
            if (cell.y == removedLayerY) continue;

            Vector3Int newCell = cell.y > removedLayerY
                ? new Vector3Int(cell.x, cell.y - 1, cell.z)
                : cell;

            newCellRenderers[newCell] = kvp.Value;
            if (cellGameObjects.TryGetValue(cell, out var go))
                newCellGameObjects[newCell] = go;

            if (!newFloorCells.ContainsKey(newCell.y))
                newFloorCells[newCell.y] = new List<Vector3Int>();
            newFloorCells[newCell.y].Add(newCell);
        }

        cellRenderers = newCellRenderers;
        cellGameObjects = newCellGameObjects;
        floorCells = newFloorCells;

        RefreshAllCells();
    }

    private Sprite FindCardSprite()
    {
        var cards = FindObjectsOfType<PieceCardUI>(true);
        foreach (var card in cards)
        {
            if (card == null) continue;
            var img = card.GetComponent<Image>();
            if (img != null && img.sprite != null)
                return img.sprite;
        }
        return null;
    }
}
