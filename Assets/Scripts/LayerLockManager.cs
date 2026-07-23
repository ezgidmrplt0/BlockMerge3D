using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Sıralı katman mekaniğinin GÖRSELİ: o an oynanabilir (en üst/gereken) katman dışındaki her
/// katmanın üstüne, geniş XZ yüzünde çapraz X oluşturan iki zincir + kesişimde bir kilit koyar.
///
/// Bir üst katman claw ile alınıp bir alttaki katman "gereken katman" olduğunda
/// (bkz. GridManager.ExplodeLayer → TryFindNextRequiredLayer), o katmanın kilidi açılma
/// animasyonunu oynatır, ardından kilit + zincirler tek parça olarak yere düşer ve yok olur.
///
/// Kilitler tahtayla (ActiveMainPiece) birlikte dönsün diye ona parent'lanır; düşerken
/// dünya uzayına koparılır (SetParent(null, true)).
/// </summary>
public class LayerLockManager : MonoBehaviour
{
    public static LayerLockManager Instance { get; private set; }

    [Header("Prefabs")]
    public GameObject lockPrefab;   // Assets/Prefabs/LockVisual.prefab
    public GameObject chainPrefab;  // Assets/Prefabs/ChainVisual.prefab

    [Header("Model bounds merkezleri (scale=1, pivot ofseti) — ölçümden")]
    public Vector3 chainBoundsCenter = new Vector3(-4.374f, 0f, -2.0f);
    public Vector3 lockBoundsCenter  = new Vector3(0f, 0.228f, 0.022f);
    [Tooltip("Chain.fbx'in scale=1'deki X uzunluğu")]
    public float chainSourceLength = 10.62f;
    [Tooltip("Chain.fbx'in scale=1'deki en-kesit (Y/Z) kalınlığı")]
    public float chainSourceCross = 1.04f;
    [Tooltip("Chain.fbx'te ardışık iki link arası mesafe (scale=1)")]
    public float chainLinkSpacing = 0.97f;
    [Tooltip("Lock.fbx'in scale=1'deki Y yüksekliği")]
    public float lockSourceHeight = 1.402f;

    [Header("Kemer yerleşimi")]
    [Tooltip("Zincir kalınlığı (dünya birimi). UNIFORM ölçeklenir — germe yok; kenarı " +
             "doldurmak için gereken kadar zincir kopyası uç uca dizilir.")]
    public float chainThickness = 0.55f;
    [Tooltip("Kemerin katman yüzlerinden ne kadar dışarıda sarsın")]
    public float beltOutwardOffset = 0.1f;
    [Tooltip("Kemerin dikey konumu — katman ortasına göre (+ yukarı, - aşağı)")]
    public float beltHeightOffset = 0f;
    [Tooltip("Kilit görsel boyutu (hücre boyutuna oran)")]
    public float lockScaleFactor = 1.3f;
    [Tooltip("Toka (kilit) ön kenardan ne kadar dışarı çıksın")]
    public float lockBuckleOut = 0.1f;
    [Tooltip("Kilit rotasyonu — kemer tokası. Anahtar deliği dışa baksın diye Y=180.")]
    public Vector3 lockEuler = new Vector3(0f, 180f, 0f);

    [Header("Kilitli katman karartma")]
    [Tooltip("Kilitli katmanları koyu göster (yoğunluk: GridManager.lockedLayerDim). " +
             "Hücre rengini koyulaştırır — kilit düşünce aydınlanır.")]
    public bool darkenLockedLayers = true;

    [Header("Açılma & Düşme")]
    [Tooltip("Açılma animasyonu hız çarpanı (yeni klip 1.04s; 1 ≈ 1.04s)")]
    public float openAnimSpeed = 1f;
    [Tooltip("Açılma bittikten sonra düşmeye kadar bekleme")]
    public float dropDelayAfterOpen = 0.05f;
    [Tooltip("Dalga: kilide uzaklık başına gecikme (s/birim). Büyük = daha yavaş yayılan dalga")]
    public float waveDelayPerUnit = 0.035f;
    [Tooltip("Her parça ne kadar aşağı düşsün (birim)")]
    public float fallDistance = 14f;
    [Tooltip("Bir parçanın düşme süresi")]
    public float fallDuration = 1.2f;

    // layerY -> kilit rig kökü
    private readonly Dictionary<int, GameObject> rigs = new Dictionary<int, GameObject>();

    // Tek link klonlamak için gizli şablon (Chain.fbx içindeki even/odd link çocukları)
    private GameObject chainTemplate;
    private Transform  evenLink, oddLink;

    private void Awake() { Instance = this; }

    /// <summary>Seviye kurulunca (GridManager.Initialize sonu) çağrılır — eskiyi temizler,
    /// aktif (gereken) katman hariç her katmana kilit rig'i koyar.</summary>
    public void BuildForNewLevel()
    {
        ClearAll();

        var grid = GridManager.Instance;
        var main = LevelManager.Instance != null ? LevelManager.Instance.ActiveMainPiece : null;
        if (grid == null || main == null || lockPrefab == null || chainPrefab == null) return;

        var layers = new HashSet<int>();
        foreach (var c in grid.allShapeCells) layers.Add(c.y);

        foreach (var y in layers)
        {
            if (y == grid.ActiveLayerY) continue; // en üst / gereken katman serbest
            BuildRigForLayer(grid, main.transform, y);
        }
    }

    private void BuildRigForLayer(GridManager grid, Transform boardParent, int y)
    {
        // Katmanın XZ sınırlarını hücre koordinatlarından bul
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        bool any = false;
        foreach (var c in grid.allShapeCells)
        {
            if (c.y != y) continue;
            any = true;
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.z < minZ) minZ = c.z;
            if (c.z > maxZ) maxZ = c.z;
        }
        if (!any) return;

        float step = grid.Step;
        float cell = grid.CellSize;

        // Rig kökü: katman XZ merkezi, dikey ORTA (kemer beli) — board local uzayında.
        Vector3 centerLocal = new Vector3((minX + maxX) * 0.5f, y, (minZ + maxZ) * 0.5f) * step
                              + Vector3.one * (cell * 0.5f);
        centerLocal.y += beltHeightOffset; // katman dikey ortası + ayar

        GameObject rig = new GameObject($"LayerLock_{y}");
        rig.transform.SetParent(boardParent, false);
        rig.transform.localPosition = centerLocal;
        rig.transform.localRotation = Quaternion.identity;
        rig.transform.localScale = Vector3.one;

        // Katmanın XZ boyutu; kemer bu dikdörtgenin 4 kenarını (yüzlerin biraz dışından) sarar.
        float sizeX = (maxX - minX) * step + cell;
        float sizeZ = (maxZ - minZ) * step + cell;
        float halfX = sizeX * 0.5f + beltOutwardOffset;
        float halfZ = sizeZ * 0.5f + beltOutwardOffset;

        // Kemer halkası: ön/arka (X boyunca), sol/sağ (Z boyunca) — katmanı çevreler.
        // Kenar tam köşelere (±halfX/±halfZ) kadar; zincir kenara TAM sığar (exact-fit) → taşma yok.
        BuildChainRun(rig.transform, new Vector3(0f, 0f, -halfZ), 0f,  sizeX + 2f * beltOutwardOffset); // ön
        BuildChainRun(rig.transform, new Vector3(0f, 0f,  halfZ), 0f,  sizeX + 2f * beltOutwardOffset); // arka
        BuildChainRun(rig.transform, new Vector3(-halfX, 0f, 0f), 90f, sizeZ + 2f * beltOutwardOffset); // sol
        BuildChainRun(rig.transform, new Vector3( halfX, 0f, 0f), 90f, sizeZ + 2f * beltOutwardOffset); // sağ

        // Kilit = kemer tokası, ön kenarın ortasında, dışarı bakar.
        float lockScale = lockSourceHeight > 0.001f ? (cell * lockScaleFactor) / lockSourceHeight : 1f;
        GameObject lockWrap = Wrap(lockPrefab, rig.transform, lockBoundsCenter);
        lockWrap.transform.localRotation = Quaternion.Euler(lockEuler);
        lockWrap.transform.localScale = Vector3.one * lockScale;
        lockWrap.transform.localPosition = new Vector3(0f, 0f, -halfZ - lockBuckleOut);

        // Kilitliyken katmanı koyulaştır (hücre rengini; kilit düşünce aydınlanır).
        if (darkenLockedLayers)
            GridManager.Instance?.SetLayerDarkened(y, true);

        rigs[y] = rig;
    }

    /// <summary>Belirli bir merkez ve yön boyunca, verilen uzunlukta düz bir zincir koşusu dizer.
    /// Kalınlık SABİT (level/katman boyutundan bağımsız); kenarı tek tek linklerle doldurur —
    /// link sayısı uzunlukla artar ama kalınlık değişmez ve kenara TAM sığar (taşma yok).</summary>
    private void BuildChainRun(Transform rigParent, Vector3 centerLocal, float angleY, float length)
    {
        EnsureChainTemplate();
        if (evenLink == null || oddLink == null) return;

        float cross       = chainSourceCross > 0.001f ? chainSourceCross : 1.04f;
        float s           = chainThickness / cross;             // SABİT uniform ölçek → sabit kalınlık
        float linkSpacing = chainLinkSpacing * s;               // doğal link aralığı
        int   n           = Mathf.Max(1, Mathf.RoundToInt(length / Mathf.Max(0.001f, linkSpacing)));
        float step        = length / n;                         // kenara tam sığsın diye (≈linkSpacing)

        Quaternion rot = Quaternion.Euler(0f, angleY, 0f);
        Vector3    dir = rot * Vector3.right;

        for (int i = 0; i < n; i++)
        {
            float off = (i - (n - 1) * 0.5f) * step;
            Transform tmpl = (i % 2 == 0) ? evenLink : oddLink; // interlock için parite
            GameObject link = Instantiate(tmpl.gameObject, rigParent);
            link.hideFlags = HideFlags.None;
            link.SetActive(true);
            // Linkin kendi ölçeği (mesh küçük yazıldığı için ~100) KORUNUR, s ile çarpılır.
            // Eskiden Vector3.one*s ile bu 100 eziliyordu → linkler görünmeyecek kadar küçük kalıyordu.
            link.transform.localScale    = tmpl.localScale * s;
            link.transform.localPosition = centerLocal + dir * off;
            link.transform.localRotation = rot * tmpl.localRotation;
        }
    }

    /// <summary>Chain.fbx'ten tek link klonlamak için gizli şablonu (bir kez) hazırlar.</summary>
    private void EnsureChainTemplate()
    {
        if (chainTemplate != null && evenLink != null && oddLink != null) return;
        if (chainPrefab == null) return;

        chainTemplate = Instantiate(chainPrefab);
        chainTemplate.name = "ChainTemplate(hidden)";
        chainTemplate.hideFlags = HideFlags.HideAndDontSave;
        chainTemplate.transform.SetParent(transform, false);
        chainTemplate.SetActive(false);

        var links = new List<Transform>();
        foreach (Transform c in chainTemplate.transform)
            if (c.name.StartsWith("ChainLink")) links.Add(c);
        links.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (links.Count > 0) evenLink = links[0];
        if (links.Count > 1) oddLink  = links[1]; else oddLink = evenLink;
    }

    /// <summary>Modeli, görsel merkezi wrapper'ın orijinine denk gelecek şekilde sarar
    /// (pivot ofsetini nötrler). Böylece wrapper'ı ölçekleyip döndürmek modeli merkezinden
    /// ölçekler/döndürür.</summary>
    private GameObject Wrap(GameObject prefab, Transform parent, Vector3 boundsCenter)
    {
        GameObject w = new GameObject("w");
        w.transform.SetParent(parent, false);
        GameObject m = Instantiate(prefab, w.transform);
        m.transform.localPosition = -boundsCenter;
        m.transform.localRotation = Quaternion.identity;
        m.transform.localScale = Vector3.one;
        return w;
    }

    /// <summary>Verilen katman "gereken katman" olduğunda (bir üst katman claw ile alındı):
    /// kilidi aç, sonra rig'i yere düşür ve yok et.</summary>
    public void UnlockLayer(int y)
    {
        if (!rigs.TryGetValue(y, out var rig) || rig == null) { rigs.Remove(y); return; }
        rigs.Remove(y);

        float wait = 0f;
        var anim = rig.GetComponentInChildren<Animation>();
        if (anim != null && anim.clip != null)
        {
            var state = anim[anim.clip.name];
            if (state != null) state.speed = Mathf.Max(0.01f, openAnimSpeed);
            anim.Play();
            wait = anim.clip.length / Mathf.Max(0.01f, openAnimSpeed);
        }

        int layer = y;
        DOVirtual.DelayedCall(wait + dropDelayAfterOpen, () =>
        {
            // Kilit düşerken katman aydınlanır (koyulaştırmayı kaldır).
            GridManager.Instance?.SetLayerDarkened(layer, false);
            DropRig(rig);
        });
    }

    private void DropRig(GameObject rig)
    {
        if (rig == null) return;

        // Dalga merkezi = kilidin konumu.
        var lockAnim = rig.GetComponentInChildren<Animation>();
        Vector3 lockPos = lockAnim != null ? lockAnim.transform.position : rig.transform.position;

        // Düşecek parçalar = rig'in doğrudan çocukları (kilit wrapper'ı + her zincir linki).
        var parts = new List<Transform>();
        foreach (Transform child in rig.transform) parts.Add(child);

        // Her parça, kilide UZAKLIĞINA göre gecikmeyle düşer → önce kilit ve ona yapışık
        // linkler, sonra sırayla en uzaktakiler (dalga/kaskad).
        float maxDelay = 0f;
        foreach (var part in parts)
        {
            float delay = Vector3.Distance(part.position, lockPos) * waveDelayPerUnit;
            maxDelay = Mathf.Max(maxDelay, delay);
            var p = part;
            DOVirtual.DelayedCall(delay, () => DropPart(p)).SetId(GridManager.LEVEL_ANIM_ID);
        }

        // Boşalan rig kökünü sonda temizle.
        Destroy(rig, maxDelay + fallDuration + 0.5f);
    }

    /// <summary>Tek bir parçayı (kilit ya da zincir linki) dünyaya koparıp DÜZ aşağı,
    /// hızlanan (yerçekimi gibi) bir hareketle düşürür. Savrulma/dönüş yok → linkler birbirinden
    /// kopmaz, zincir şekli korunur; sadece başlama anları kilide uzaklığa göre kaydığından
    /// dalga/kaskad görünür.</summary>
    private void DropPart(Transform part)
    {
        if (part == null) return;
        part.SetParent(null, true);

        // Kontrollü düşüş kullanıyoruz — varsa kinematik rigidbody'yi kaldır.
        foreach (var childRb in part.GetComponentsInChildren<Rigidbody>())
            Destroy(childRb);

        Vector3 target = part.position + Vector3.down * fallDistance;
        part.DOMove(target, fallDuration).SetEase(Ease.InQuad).SetId(GridManager.LEVEL_ANIM_ID)
            .OnComplete(() => { if (part != null) Destroy(part.gameObject); });
    }

    /// <summary>Tüm mevcut kilit rig'lerini anında yok eder (seviye yeniden yüklenirken).</summary>
    public void ClearAll()
    {
        foreach (var kv in rigs)
            if (kv.Value != null) { DOTween.Kill(kv.Value); Destroy(kv.Value); }
        rigs.Clear();
    }
}
