using UnityEngine;
using System;

#if ADMOB_ENABLED
using GoogleMobileAds.Api;
#endif

// ═══════════════════════════════════════════════════════════════════
//  ADMOB —  IRewardedAd Adapter'ı
//  BlockMerge3D
//
//  KURULUM:
//  1. Google Mobile Ads SDK'yı (.unitypackage) indirip projeye kurun.
//  2. Player Settings > Scripting Define Symbols kısmına ADMOB_ENABLED ekleyin.
//  3. Bu bileşeni sahnedeki bir objeye (örn: AdsManager) ekleyin.
// ═══════════════════════════════════════════════════════════════════

public class AdMobRewarded : MonoBehaviour, IRewardedAd
{
    [Header("AdMob Ad Unit IDs")]
    [Tooltip("Android için Test ID: ca-app-pub-3940256099942544/5224354917")]
    [SerializeField] private string androidAdUnitId = "ca-app-pub-3940256099942544/5224354917";
    [Tooltip("iOS için Test ID: ca-app-pub-3940256099942544/1712485313")]
    [SerializeField] private string iosAdUnitId = "ca-app-pub-3940256099942544/1712485313";

    private string _adUnitId;

#if ADMOB_ENABLED
    private RewardedAd _rewardedAd;
    private Action _pendingReward;
    private Action _pendingFailed;
    private bool _isAdLoaded = false;
#endif

    public bool IsReady
    {
        get
        {
#if ADMOB_ENABLED
            return _rewardedAd != null && _rewardedAd.CanShowAd() && _isAdLoaded;
#else
            return false;
#endif
        }
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

#if UNITY_IOS
        _adUnitId = iosAdUnitId;
#else
        _adUnitId = androidAdUnitId;
#endif

        // Stub yerine gerçek sağlayıcı olarak kendini tak.
        RewardedAds.SetProvider(this);

#if ADMOB_ENABLED
        // Initialize the Google Mobile Ads SDK.
        MobileAds.Initialize((InitializationStatus initStatus) =>
        {
            // SDK başlatıldıktan sonra ilk reklamı yükle.
            Load();
        });
#else
        Debug.LogWarning("[AdMob] ADMOB_ENABLED tanımlı değil. AdMob kodu derlenmedi! (Scripting Define Symbols'a ekleyin).");
#endif
    }

    public void Load()
    {
#if ADMOB_ENABLED
        _isAdLoaded = false;
        
        // Önceki reklam varsa temizle
        if (_rewardedAd != null)
        {
            _rewardedAd.Destroy();
            _rewardedAd = null;
        }

        Debug.Log("[AdMob] Ödüllü reklam yükleniyor...");

        // Yeni reklam isteği oluştur
        var adRequest = new AdRequest();

        // Reklamı yükle
        RewardedAd.Load(_adUnitId, adRequest,
            (RewardedAd ad, LoadAdError error) =>
            {
                // Yükleme hatası varsa
                if (error != null || ad == null)
                {
                    Debug.LogError($"[AdMob] Reklam yüklenemedi: {error?.GetMessage()}");
                    _isAdLoaded = false;
                    return;
                }

                Debug.Log("[AdMob] Reklam başarıyla yüklendi!");
                
                _rewardedAd = ad;
                _isAdLoaded = true;
                
                RegisterEventHandlers(_rewardedAd);
            });
#endif
    }

    public void Show(Action onReward, Action onFailed)
    {
#if ADMOB_ENABLED
        if (_rewardedAd != null && _rewardedAd.CanShowAd())
        {
            _pendingReward = onReward;
            _pendingFailed = onFailed;
            _rewardedAd.Show((Reward reward) =>
            {
                // Kullanıcı reklamı başarıyla tamamlayıp ödülü hak ettiğinde çalışır
                _pendingReward?.Invoke();
                ClearPending();
            });
        }
        else
        {
            Debug.LogError("[AdMob] Gösterilecek hazır reklam yok.");
            onFailed?.Invoke();
        }
#else
        onFailed?.Invoke();
#endif
    }

#if ADMOB_ENABLED
    private void RegisterEventHandlers(RewardedAd ad)
    {
        // Reklam kapatıldığında (ödül alıp almadığından bağımsız)
        ad.OnAdFullScreenContentClosed += () =>
        {
            Debug.Log("[AdMob] Reklam kapatıldı.");
            // Bir sonraki gösterim için hemen yeni reklam yüklemeye başla
            Load();
        };

        // Reklam gösteriminde hata oluşursa
        ad.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogError($"[AdMob] Reklam gösterme hatası: {error.GetMessage()}");
            _pendingFailed?.Invoke();
            ClearPending();
            Load();
        };
    }

    private void ClearPending()
    {
        _pendingReward = null;
        _pendingFailed = null;
    }
#endif
}
