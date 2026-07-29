using UnityEngine;
#if UNITY_ADS_ENABLED
using UnityEngine.Advertisements;
#endif

// ═══════════════════════════════════════════════════════════════════
//  UNITY ADS  —  IRewardedAd Adapter'ı + Başlatıcı
//  BlockMerge3D
//
//  Sahnedeki bir GameObject'e ekle, Game ID'leri (Unity Dashboard >
//  Monetization) ve rewarded Ad Unit / Placement ID'lerini gir. Awake'te
//  Unity Ads'i başlatır, ödüllü reklamı yükler ve kendini
//  RewardedAds.SetProvider(this) ile oyuna takar — böylece joker-yenileme
//  akışı (ControlButton) stub yerine gerçek Unity Ads'i kullanır.
//
//  KURULUM:
//   1) Package Manager'da Advertisement (com.unity.ads) kurulu olsun
//      (manifest'e eklendi; Unity çözecek).
//   2) Player Settings > Scripting Define Symbols'a  UNITY_ADS_ENABLED  ekle.
//   3) Bu bileşeni sahneye koy, Game ID'leri ve Ad Unit ID'lerini gir.
//
//  Define kurulana kadar sınıf boş bir MonoBehaviour'dır; proje derlenmeye
//  devam eder ve stub reklam devrede kalır.
// ═══════════════════════════════════════════════════════════════════

public class UnityAdsRewarded : MonoBehaviour
#if UNITY_ADS_ENABLED
    , IRewardedAd, IUnityAdsInitializationListener, IUnityAdsLoadListener, IUnityAdsShowListener
#endif
{
    // iOS alanları yalnızca #if UNITY_IOS altında okunuyor; Android build'inde
    // "atandı ama kullanılmadı" (CS0414) uyarısı verirler — Inspector'da görünsünler
    // diye tutuyoruz, uyarıyı susturuyoruz.
#pragma warning disable 0414
    [Header("Unity Dashboard > Monetization > Game IDs")]
    [SerializeField] private string androidGameId = "";
    [SerializeField] private string iosGameId = "";
    [Tooltip("Yayına almadan önce KAPAT. Açıkken gerçek reklam yerine test reklamı gelir.")]
    [SerializeField] private bool testMode = true;

    [Header("Ödüllü (Rewarded) Ad Unit / Placement ID")]
    [SerializeField] private string androidAdUnitId = "Rewarded_Android";
    [SerializeField] private string iosAdUnitId = "Rewarded_iOS";
#pragma warning restore 0414

#if UNITY_ADS_ENABLED
    private string adUnitId;
    private bool initialized;
    private bool loaded;
    private bool loading;
    private bool showQueued;      // Show çağrıldı ama reklam henüz yüklü değil → yüklenince göster
    private float showDeadline;   // showQueued için zaman aşımı (unscaled)
    private System.Action pendingReward;
    private System.Action pendingFailed;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

#if UNITY_IOS
        string gameId = iosGameId;   adUnitId = iosAdUnitId;
#else
        string gameId = androidGameId; adUnitId = androidAdUnitId;
#endif
        // Stub yerine gerçek sağlayıcı olarak kendini tak. (SetProvider erken Load() çağırır
        // ama init bitmeden Load no-op'tur; asıl yükleme OnInitializationComplete'te olur.)
        RewardedAds.SetProvider(this);

        if (string.IsNullOrEmpty(gameId))
        {
            Debug.LogWarning("[UnityAds] Game ID boş — Dashboard'dan alıp Inspector'a gir.");
            return;
        }
        if (Advertisement.isInitialized) { initialized = true; Load(); }
        else if (Advertisement.isSupported) Advertisement.Initialize(gameId, testMode, this);
    }

    private void Update()
    {
        // Yüklenmesini beklediğimiz bir Show varsa ve süre dolduysa nazikçe başarısız ol
        // (onay paneli sonsuza dek "yükleniyor" durumunda asılı kalmasın).
        if (showQueued && Time.unscaledTime > showDeadline)
        {
            showQueued = false;
            FailPending();
        }
    }

    // ── IRewardedAd ──────────────────────────────────────────────────
    public bool IsReady => loaded;

    public void Load()
    {
        // Init bitmeden yükleme yapılamaz (OnInitializationComplete tekrar çağırır).
        // Zaten yüklü/yükleniyorsa tekrar tetikleme.
        if (!initialized || loading || loaded || string.IsNullOrEmpty(adUnitId)) return;
        loading = true;
        Advertisement.Load(adUnitId, this);
    }

    public void Show(System.Action onReward, System.Action onFailed)
    {
        pendingReward = onReward;
        pendingFailed = onFailed;

        if (loaded)
        {
            Advertisement.Show(adUnitId, this);
        }
        else
        {
            // Cihazda açılış yüklemesi geç kaldıysa/başarısızsa: burada YENİDEN yüklemeyi
            // tetikle ve reklam hazır olur olmaz göster (kısa zaman aşımıyla). Böylece
            // "hazır değil" durumu teklifi kalıcı olarak kilitlemez.
            showQueued = true;
            showDeadline = Time.unscaledTime + 12f;
            Load();
        }
    }

    private void FailPending()
    {
        var f = pendingFailed;
        pendingReward = null; pendingFailed = null;
        f?.Invoke();
    }

    // ── Initialization listener ──────────────────────────────────────
    public void OnInitializationComplete() { initialized = true; Load(); }
    public void OnInitializationFailed(UnityAdsInitializationError error, string message)
    {
        Debug.LogWarning($"[UnityAds] init başarısız: {error} — {message}");
        if (showQueued) { showQueued = false; FailPending(); }
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
        Debug.LogWarning($"[UnityAds] load başarısız: {error} — {message}");
        loaded = false; loading = false;
        if (showQueued) { showQueued = false; FailPending(); }
    }

    // ── Show listener ────────────────────────────────────────────────
    public void OnUnityAdsShowComplete(string placementId, UnityAdsShowCompletionState state)
    {
        // Ödül SADECE reklam TAMAMEN izlenince verilir.
        if (state == UnityAdsShowCompletionState.COMPLETED) pendingReward?.Invoke();
        else pendingFailed?.Invoke();      // atlandı / erken kapatıldı
        pendingReward = null; pendingFailed = null;
        loaded = false;                     // gösterilen reklam tüketildi
        Load();                             // sonraki gösterim için yeniden yükle
    }
    public void OnUnityAdsShowFailure(string placementId, UnityAdsShowError error, string message)
    {
        Debug.LogWarning($"[UnityAds] show başarısız: {error} — {message}");
        var f = pendingFailed;
        pendingReward = null; pendingFailed = null;
        f?.Invoke();
        loaded = false;
        Load();
    }
    public void OnUnityAdsShowStart(string placementId) { }
    public void OnUnityAdsShowClick(string placementId) { }
#endif
}
