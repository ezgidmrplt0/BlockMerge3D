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
    public Vector3 lockBoundsCenter  = new Vector3(2.5f, 0.258f, 2.013f);
    [Tooltip("Chain.fbx'in scale=1'deki X uzunluğu")]
    public float chainSourceLength = 10.62f;
    [Tooltip("Chain.fbx'in scale=1'deki en-kesit (Y/Z) kalınlığı")]
    public float chainSourceCross = 1.04f;
    [Tooltip("Lock.fbx'in scale=1'deki Y yüksekliği")]
    public float lockSourceHeight = 1.345f;

    [Header("Yerleşim")]
    [Tooltip("Katmanın üst yüzünün ne kadar üstünde dursun")]
    public float heightAboveTop = 0.2f;
    [Tooltip("Zincir kalınlığı (dünya birimi). Zincir UNIFORM ölçeklenir — germe yok; " +
             "köşegeni doldurmak için gereken kadar zincir kopyası uç uca dizilir.")]
    public float chainThickness = 0.55f;
    [Tooltip("Kilit görsel boyutu (hücre boyutuna oran)")]
    public float lockScaleFactor = 0.9f;
    [Tooltip("Kilidin zincirlerin üstünde durması için ek yükseklik")]
    public float lockRaise = 0.25f;
    [Tooltip("Kilit rotasyonu — yatay yatması için (90,0,0). Dik istenirse (0,0,0).")]
    public Vector3 lockEuler = new Vector3(90f, 0f, 0f);

    [Header("Açılma & Düşme")]
    [Tooltip("Açılma animasyonu hız çarpanı (3.71s klip için 2 ≈ 1.85s)")]
    public float openAnimSpeed = 2f;
    [Tooltip("Açılma bittikten sonra düşmeye kadar bekleme")]
    public float dropDelayAfterOpen = 0.05f;
    public float dropDistance = 12f;
    public float dropDuration = 1.1f;
    public Vector3 dropTumble = new Vector3(40f, 120f, 30f);
    [Tooltip("Düşüş bittikten sonra yok edilene kadar ek süre")]
    public float cleanupExtra = 0.3f;

    // layerY -> kilit rig kökü
    private readonly Dictionary<int, GameObject> rigs = new Dictionary<int, GameObject>();

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

        // Rig kökü: katman XZ merkezi, üst yüzün biraz üstünde (hepsi board local uzayında)
        Vector3 centerLocal = new Vector3((minX + maxX) * 0.5f, y, (minZ + maxZ) * 0.5f) * step
                              + Vector3.one * (cell * 0.5f);
        centerLocal.y = y * step + cell + heightAboveTop; // üst yüz + boşluk

        GameObject rig = new GameObject($"LayerLock_{y}");
        rig.transform.SetParent(boardParent, false);
        rig.transform.localPosition = centerLocal;
        rig.transform.localRotation = Quaternion.identity;
        rig.transform.localScale = Vector3.one;

        // Katmanın XZ boyutu + köşegen
        float sizeX = (maxX - minX) * step + cell;
        float sizeZ = (maxZ - minZ) * step + cell;
        float diag = Mathf.Sqrt(sizeX * sizeX + sizeZ * sizeZ);
        float angle = Mathf.Atan2(sizeZ, sizeX) * Mathf.Rad2Deg;

        // İki çapraz zincir → X. Tek zinciri GERMEZ; doğal oranlı (uniform) zincir
        // kopyalarını uç uca dizerek köşegeni doldurur.
        CreateChain(rig.transform, angle, diag);
        CreateChain(rig.transform, -angle, diag);

        // Kilit (kesişim, zincirlerin üstünde, yatay)
        float lockScale = lockSourceHeight > 0.001f ? (cell * lockScaleFactor) / lockSourceHeight : 1f;
        GameObject lockWrap = Wrap(lockPrefab, rig.transform, lockBoundsCenter);
        lockWrap.transform.localRotation = Quaternion.Euler(lockEuler);
        lockWrap.transform.localScale = Vector3.one * lockScale;
        lockWrap.transform.localPosition = new Vector3(0f, lockRaise, 0f);

        rigs[y] = rig;
    }

    private void CreateChain(Transform rigParent, float angleY, float spanLength)
    {
        float cross   = chainSourceCross > 0.001f ? chainSourceCross : 1.04f;
        float s       = chainThickness / cross;                 // UNIFORM ölçek (germe yok)
        float copyLen = chainSourceLength * s;                  // bir kopyanın doğal uzunluğu
        int   n       = Mathf.Max(1, Mathf.CeilToInt(spanLength / Mathf.Max(0.001f, copyLen)));

        Quaternion rot = Quaternion.Euler(0f, angleY, 0f);
        Vector3    dir = rot * Vector3.right;                   // zincirin uzunluk ekseni

        for (int i = 0; i < n; i++)
        {
            float off = (i - (n - 1) * 0.5f) * copyLen;         // uç uca, ortalanmış
            GameObject w = Wrap(chainPrefab, rigParent, chainBoundsCenter);
            w.transform.localPosition = dir * off;
            w.transform.localRotation = rot;
            w.transform.localScale    = Vector3.one * s;        // uniform → link oranı korunur
        }
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

        DOVirtual.DelayedCall(wait + dropDelayAfterOpen, () => DropRig(rig));
    }

    private void DropRig(GameObject rig)
    {
        if (rig == null) return;

        // Dünya uzayına kopar ki tahta dönüşünden bağımsız, düz aşağı düşsün
        rig.transform.SetParent(null, true);

        Sequence seq = DOTween.Sequence();
        seq.Join(rig.transform.DOMove(rig.transform.position + Vector3.down * dropDistance, dropDuration)
                    .SetEase(Ease.InQuad));
        seq.Join(rig.transform.DORotate(dropTumble, dropDuration, RotateMode.LocalAxisAdd)
                    .SetEase(Ease.InOutSine));
        seq.OnComplete(() => { if (rig != null) Destroy(rig); });
        seq.SetTarget(rig);

        // Emniyet: sekans bir şekilde tamamlanmazsa yine de temizle
        Destroy(rig, dropDuration + cleanupExtra);
    }

    /// <summary>Tüm mevcut kilit rig'lerini anında yok eder (seviye yeniden yüklenirken).</summary>
    public void ClearAll()
    {
        foreach (var kv in rigs)
            if (kv.Value != null) { DOTween.Kill(kv.Value); Destroy(kv.Value); }
        rigs.Clear();
    }
}
