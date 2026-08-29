using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections.Generic;

/// <summary>
/// Ekranın sol üst köşesinde tüm katmanları gösteren 3D Kule Önizleme (Mini-Tower Preview) penceresi.
/// Canlı RenderTexture ve izometrik kamera ile kulenin tüm 3D yapısını ve aktif katmanı gösterir.
/// </summary>
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
    private RawImage previewRawImage;
    private RectTransform previewCardRect;

    private GameObject miniTowerInstance;
    private Dictionary<int, List<GameObject>> floorMiniBlocks = new Dictionary<int, List<GameObject>>();
    private int currentActiveFloorY = 0;
    private float autoRotateSpeed = 25f;

    private readonly Vector3 PreviewStagePos = new Vector3(300f, 300f, 300f);

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

        // Ana sahneyi kör etmeyecek LOKAL Point Light (sadece mini sahne pozisyonunu aydınlatır)
        Transform oldLight = previewStageRoot.transform.Find("MiniTowerLight");
        if (oldLight != null) Destroy(oldLight.gameObject);

        GameObject lightGo = new GameObject("MiniTowerLight");
        lightGo.transform.SetParent(previewStageRoot.transform);
        lightGo.transform.position = PreviewStagePos + new Vector3(2.5f, 4f, -2.5f);
        var pointLight = lightGo.AddComponent<Light>();
        pointLight.type = LightType.Point;
        pointLight.intensity = 2.5f;
        pointLight.range = 10f; // Sadece mini kuleyi aydınlatır, ana ekrana taşmaz
        pointLight.color = Color.white;

        // İzometrik Kamera
        if (previewCamera == null)
        {
            GameObject camGo = new GameObject("MiniTowerCamera");
            camGo.transform.SetParent(previewStageRoot.transform);
            camGo.transform.position = PreviewStagePos + new Vector3(3.2f, 3.8f, -3.2f);
            camGo.transform.LookAt(PreviewStagePos);

            previewCamera = camGo.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.08f, 0.12f, 0.18f, 1f);
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
            return;
        }

        // UI Kart Kutusu ("KATMAN 1/3" yazısının hemen altına yerleştirilir)
        GameObject cardGo = new GameObject("TowerMiniPreviewCard", typeof(RectTransform), typeof(Image));
        cardGo.transform.SetParent(canvas.transform, false);

        previewCardRect = cardGo.GetComponent<RectTransform>();
        previewCardRect.anchorMin = new Vector2(0f, 1f);
        previewCardRect.anchorMax = new Vector2(0f, 1f);
        previewCardRect.pivot = new Vector2(0f, 1f);
        previewCardRect.anchoredPosition = new Vector2(24f, -95f);
        previewCardRect.sizeDelta = new Vector2(76f, 76f);

        Image bgImage = cardGo.GetComponent<Image>();
        bgImage.color = new Color(0.10f, 0.14f, 0.22f, 0.95f);

        var outline = cardGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.35f, 0.75f, 1f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);

        // RawImage (3D Kuleyi gösteren ekran)
        GameObject rawGo = new GameObject("TowerView", typeof(RectTransform), typeof(RawImage));
        rawGo.transform.SetParent(cardGo.transform, false);

        var rawRect = rawGo.GetComponent<RectTransform>();
        rawRect.anchorMin = Vector2.zero;
        rawRect.anchorMax = Vector2.one;
        rawRect.sizeDelta = new Vector2(-6f, -6f);

        previewRawImage = rawGo.GetComponent<RawImage>();
        previewRawImage.texture = renderTexture;
        previewRawImage.color = Color.white;
    }

    private void Update()
    {
        if (miniTowerInstance != null)
        {
            miniTowerInstance.transform.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.World);
        }
    }

    /// <summary>
    /// Seviye başladığında tüm katmanları içeren 3D mini kule modelini kurar.
    /// </summary>
    public void Initialize(GameObject mainShapePrefab, int activeLayerY)
    {
        EnsurePreviewStage();
        EnsureUIElement();

        if (miniTowerInstance != null)
        {
            Destroy(miniTowerInstance);
        }

        floorMiniBlocks.Clear();
        currentActiveFloorY = activeLayerY;

        if (mainShapePrefab == null) return;

        miniTowerInstance = Instantiate(mainShapePrefab, PreviewStagePos, Quaternion.identity, previewStageRoot.transform);
        miniTowerInstance.name = "MiniTower_Model";

        // Bütün collider'ları kaldır
        foreach (var col in miniTowerInstance.GetComponentsInChildren<Collider>())
        {
            Destroy(col);
        }

        // Modeli Preview merkezine tam oturt (Center alignment)
        var allRenderers = miniTowerInstance.GetComponentsInChildren<Renderer>();
        if (allRenderers.Length > 0)
        {
            Bounds b = allRenderers[0].bounds;
            for (int i = 1; i < allRenderers.Length; i++) b.Encapsulate(allRenderers[i].bounds);
            Vector3 offset = b.center - PreviewStagePos;
            miniTowerInstance.transform.position -= offset;

            // Kamera boyutunu kule ebatına göre otomatik ayarla
            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (previewCamera != null)
            {
                previewCamera.orthographicSize = Mathf.Max(1.8f, maxDim * 0.85f);
            }
        }

        var shapeHolder = miniTowerInstance.GetComponent<CubeShapeDataHolder>();
        float step = shapeHolder != null ? shapeHolder.Step : 1f;

        // Katman bazlı blokları kaydet ve görselleştir
        foreach (Transform child in miniTowerInstance.transform)
        {
            var rend = child.GetComponentInChildren<Renderer>(true);
            if (rend == null) continue;

            Vector3Int cell = shapeHolder != null 
                ? shapeHolder.WorldToCell(child.position)
                : new Vector3Int(0, Mathf.RoundToInt(child.localPosition.y / step), 0);

            if (!floorMiniBlocks.ContainsKey(cell.y))
                floorMiniBlocks[cell.y] = new List<GameObject>();

            floorMiniBlocks[cell.y].Add(child.gameObject);
        }

        RefreshMiniTowerVisuals();
    }

    /// <summary>
    /// Katmanların renk durumlarını günceller.
    /// </summary>
    public void SetActiveFloor(int activeFloorY)
    {
        currentActiveFloorY = activeFloorY;
        RefreshMiniTowerVisuals();
    }

    private void RefreshMiniTowerVisuals()
    {
        if (floorMiniBlocks == null) return;

        foreach (var kvp in floorMiniBlocks)
        {
            int floorY = kvp.Key;
            bool isActive = (floorY == currentActiveFloorY);

            foreach (var go in kvp.Value)
            {
                if (go == null) continue;
                var r = go.GetComponentInChildren<Renderer>();
                if (r == null) continue;

                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);

                if (isActive)
                {
                    Color activeCol = new Color(0.2f, 0.85f, 1.0f, 1f);
                    block.SetColor("_BaseColor", activeCol);
                    block.SetColor("_Color", activeCol);
                }
                else
                {
                    Color bodyCol = new Color(0.42f, 0.48f, 0.58f, 0.9f);
                    block.SetColor("_BaseColor", bodyCol);
                    block.SetColor("_Color", bodyCol);
                }
                r.SetPropertyBlock(block);
            }
        }
    }

    /// <summary>
    /// Bir katman tamamlanıp yıkıldığında mini kuleden o katmanı animasyonla patlatarak siler.
    /// </summary>
    public void OnFloorDemolished(int demolishedFloorY)
    {
        if (floorMiniBlocks.TryGetValue(demolishedFloorY, out var blocks))
        {
            foreach (var go in blocks)
            {
                if (go == null) continue;
                go.transform.DOScale(Vector3.zero, 0.35f).SetEase(Ease.InBack).OnComplete(() =>
                {
                    Destroy(go);
                });
            }
            floorMiniBlocks.Remove(demolishedFloorY);
        }

        if (previewCardRect != null)
        {
            previewCardRect.DOPunchScale(Vector3.one * 0.18f, 0.25f, 6, 0.5f);
        }
    }
}
