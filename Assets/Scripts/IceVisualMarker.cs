using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Buz modelini (Assets/Resources/IceCube.prefab) işaretler. Ayrıca buzun kaç vuruşa
/// dayandığını (totalHits) ve KÜPÜN İÇİNDE YER ALAN "kalan vuruş" sayı etiketini yönetir.
/// Sayaç, GridManager.CheckAndResolveFrozenCells her nitelikli temasta bir azaltıp
/// IceBreakEffect.PlayIceChip (kısmi erime) ile birlikte UpdateCount() çağırdığında değişir;
/// 0'a inince buz tamamen erir.
/// </summary>
public class IceVisualMarker : MonoBehaviour
{
    [HideInInspector] public Vector3 baseScale = Vector3.one;
    [HideInInspector] public int totalHits = 1;

    private TextMeshPro countLabel;
    private Transform labelPivot;

    /// <summary>Buz modeli oluşturulduğunda (GridManager.EnsureIceVisual) bir kez çağrılır.</summary>
    public void Initialize(int hitsRequired)
    {
        totalHits = Mathf.Max(1, hitsRequired);
        baseScale = transform.localScale;
        UpdateCount(totalHits, animate: false);
    }

    private void EnsureLabel()
    {
        if (countLabel != null && labelPivot != null) return;

        Transform parentTransform = transform.parent != null ? transform.parent : transform;

        // Eski/bozuk pivot varsa temizle
        if (labelPivot == null)
        {
            Transform existing = parentTransform.Find("HitCountLabel");
            if (existing != null)
            {
                labelPivot = existing;
                countLabel = existing.GetComponent<TextMeshPro>();
            }
        }

        if (labelPivot == null)
        {
            GameObject pivotGo = new GameObject("HitCountLabel");
            pivotGo.transform.SetParent(parentTransform, false);
            // KÜPÜN TAM MERKEZİNDE (İÇİNDE) KONUMLANDIR
            pivotGo.transform.localPosition = Vector3.zero;
            pivotGo.layer = parentTransform.gameObject.layer;
            labelPivot = pivotGo.transform;
        }

        if (countLabel == null && labelPivot != null)
        {
            countLabel = labelPivot.gameObject.GetComponent<TextMeshPro>();
            if (countLabel == null)
                countLabel = labelPivot.gameObject.AddComponent<TextMeshPro>();

            countLabel.alignment = TextAlignmentOptions.Center;
            countLabel.fontSize = 6.5f;
            countLabel.color = Color.white; // Parlak beyaz rakam
            countLabel.outlineColor = new Color(0.02f, 0.12f, 0.4f, 1f); // Koyu lacivert belirgin dış hat
            countLabel.outlineWidth = 0.4f;
            countLabel.fontStyle = FontStyles.Bold;
            countLabel.enableAutoSizing = false;
            countLabel.rectTransform.sizeDelta = new Vector2(1.5f, 1.5f);

            var defaultFont = TMP_Settings.defaultFontAsset;
            if (defaultFont != null) countLabel.font = defaultFont;

            if (countLabel.renderer != null)
            {
                countLabel.renderer.sortingOrder = 2000;
                if (countLabel.fontMaterial != null)
                {
                    // Buz küpünün materyali de Transparent kuyruğunda (queue 3000) ve ikisi de
                    // hücre merkezinde aynı konumda olduğu için, sadece ZTest Always yeterli
                    // DEĞİL: eşit mesafede/aynı kuyrukta iki saydam objenin çizim sırası garanti
                    // değildir — buz sayının ÜZERİNE çizilirse rakam tamamen görünmez olur (bu,
                    // "hiç sayı görünmüyor" şikayetinin en olası nedeniydi). renderQueue'yu buzun
                    // kuyruğunun (3000) üzerine çekmek, rakamın HER ZAMAN buzdan sonra ve onun
                    // üzerinde çizilmesini garanti eder.
                    countLabel.fontMaterial.renderQueue = 3100;
                    countLabel.fontMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    if (countLabel.fontMaterial.HasProperty("_CullMode"))
                    {
                        // Kamera hangi yönden bakarsa baksın (billboard rotasyonu ne olursa olsun)
                        // etiketin arka yüzü de görünür kalsın diye culling'i tamamen kapat.
                        countLabel.fontMaterial.SetInt("_CullMode", (int)UnityEngine.Rendering.CullMode.Off);
                    }
                }
            }
        }
    }

    /// <summary>Kalan vuruş sayısını gösterir; animate=true ise dinamik bir "pop" efekti oynatır.</summary>
    public void UpdateCount(int remaining, bool animate)
    {
        if (this == null) return;
        EnsureLabel();

        if (labelPivot == null || countLabel == null) return;

        int current = Mathf.Max(0, remaining);
        countLabel.text = current.ToString();

        if (current <= 0 || totalHits <= 1)
        {
            labelPivot.gameObject.SetActive(false);
            return;
        }

        labelPivot.gameObject.SetActive(true);

        if (!animate)
        {
            labelPivot.localScale = Vector3.one;
            return;
        }

        labelPivot.DOKill();
        labelPivot.localScale = Vector3.one * 1.8f;
        labelPivot.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
    }

    /// <summary>Erime efekti başladığında sayı etiketini gizler.</summary>
    public void HideLabel()
    {
        if (labelPivot != null && labelPivot.gameObject != null)
        {
            labelPivot.DOKill();
            labelPivot.gameObject.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        if (labelPivot == null || !labelPivot.gameObject.activeSelf) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        labelPivot.rotation = cam.transform.rotation;
    }

    private void OnDestroy()
    {
        if (labelPivot != null && labelPivot.gameObject != null)
        {
            labelPivot.DOKill();
            Destroy(labelPivot.gameObject);
        }
    }
}
