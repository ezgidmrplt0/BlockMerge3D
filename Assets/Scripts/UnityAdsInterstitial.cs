using UnityEngine;
#if UNITY_ADS_ENABLED
using UnityEngine.Advertisements;
#endif

// ═══════════════════════════════════════════════════════════════════
//  UNITY ADS  —  IInterstitialAd Adapter'ı
//  BlockMerge3D
//
//  Geçiş (Interstitial) reklamları için Unity Ads entegrasyonu.
//  Initialization işlemi UnityAdsRewarded tarafından yapıldığı için
//  burada sadece SDK'nın hazır olmasını bekleyip reklamı yükleriz.
// ═══════════════════════════════════════════════════════════════════

public class UnityAdsInterstitial : MonoBehaviour
#if UNITY_ADS_ENABLED
    , IInterstitialAd, IUnityAdsLoadListener, IUnityAdsShowListener
#endif
{
#pragma warning disable 0414
    [Header("Geçiş (Interstitial) Ad Unit / Placement ID")]
    [SerializeField] private string androidAdUnitId = "Interstitial_Android";
    [SerializeField] private string iosAdUnitId = "Interstitial_iOS";
#pragma warning restore 0414

#if UNITY_ADS_ENABLED
    private string adUnitId;
    private bool initialized;
    private bool loaded;
    private bool loading;
    
    private bool showQueued;
    private float showDeadline;
    private System.Action pendingCompleted;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

#if UNITY_IOS
        adUnitId = iosAdUnitId;
#else
        adUnitId = androidAdUnitId;
#endif
        InterstitialAds.SetProvider(this);
    }

    private void Update()
    {
        // Başlatılmayı bekle
        if (!initialized && Advertisement.isInitialized)
        {
            initialized = true;
            Load();
        }

        // Zaman aşımı kontrolü (asılı kalmasın)
        if (showQueued && Time.unscaledTime > showDeadline)
        {
            showQueued = false;
            FailPending();
        }
    }

    // ── IInterstitialAd ──────────────────────────────────────────────
    public bool IsReady => loaded;

    public void Load()
    {
        if (!initialized || loading || loaded || string.IsNullOrEmpty(adUnitId)) return;
        loading = true;
        Advertisement.Load(adUnitId, this);
    }

    public void Show(System.Action onCompleted)
    {
        pendingCompleted = onCompleted;

        if (loaded)
        {
            Advertisement.Show(adUnitId, this);
        }
        else
        {
            // Eğer reklam henüz yüklü değilse kısa bir süre bekleyip göster
            showQueued = true;
            showDeadline = Time.unscaledTime + 5f; // Geçiş reklamında 5 saniye beklemek yeterli
            Load();
        }
    }

    private void FailPending()
    {
        var f = pendingCompleted;
        pendingCompleted = null;
        f?.Invoke();
    }

    // ── Load listener ────────────────────────────────────────────────
    public void OnUnityAdsAdLoaded(string placementId)
    {
        if (placementId != adUnitId) return;
        loaded = true; loading = false;
        
        if (showQueued)
        {
            showQueued = false;
            Advertisement.Show(adUnitId, this);
        }
    }

    public void OnUnityAdsFailedToLoad(string placementId, UnityAdsLoadError error, string message)
    {
        Debug.LogWarning($"[UnityAds Interstitial] load başarısız: {error} — {message}");
        loaded = false; loading = false;
        if (showQueued) { showQueued = false; FailPending(); }
    }

    // ── Show listener ────────────────────────────────────────────────
    public void OnUnityAdsShowComplete(string placementId, UnityAdsShowCompletionState state)
    {
        // Geçiş reklamında atlanmış olsa bile reklam izleme bitmiştir, oyuna devam edilir.
        var f = pendingCompleted;
        pendingCompleted = null;
        f?.Invoke();
        
        loaded = false;
        Load(); // Sonraki için yükle
    }

    public void OnUnityAdsShowFailure(string placementId, UnityAdsShowError error, string message)
    {
        Debug.LogWarning($"[UnityAds Interstitial] show başarısız: {error} — {message}");
        FailPending();
        loaded = false;
        Load();
    }
    
    public void OnUnityAdsShowStart(string placementId) { }
    public void OnUnityAdsShowClick(string placementId) { }
#endif
}
