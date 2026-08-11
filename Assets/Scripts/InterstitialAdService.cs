using System;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  INTERSTITIAL AD SERVICE  —  SDK'dan Bağımsız Geçiş Reklamı Katmanı
//  BlockMerge3D
//
//  Oyun kodu SADECE IInterstitialAd arayüzünü ve InterstitialAds statik cephesini
//  görür. Gerçek ağ adapter'ı (UnityAdsInterstitial vb.) uygulama açılışında
//  InterstitialAds.SetProvider(...) ile takılır.
// ═══════════════════════════════════════════════════════════════════

public interface IInterstitialAd
{
    /// <summary>Şu an gösterilmeye hazır bir reklam var mı?</summary>
    bool IsReady { get; }

    /// <summary>Yeni bir reklam yüklemeye başla (fire-and-forget).</summary>
    void Load();

    /// <summary>Reklamı göster. Reklam kapatıldığında (başarılı, atlanarak veya hata vererek) onCompleted çağrılır.</summary>
    void Show(Action onCompleted);
}

/// <summary>Editör / SDK yokken kullanılan sahte reklam.</summary>
public class StubInterstitialAd : IInterstitialAd
{
    public bool IsReady => true;
    public void Load() { }
    public void Show(Action onCompleted)
    {
        Debug.Log("[InterstitialAds] STUB geçiş reklamı gösterildi (gerçek ağ takılınca değişecek).");
        onCompleted?.Invoke();
    }
}

public static class InterstitialAds
{
    private static IInterstitialAd provider;
    public static IInterstitialAd Provider => provider ??= new StubInterstitialAd();

    /// <summary>Gerçek ağ adapter'ını uygulama açılışında bir kez tak.</summary>
    public static void SetProvider(IInterstitialAd p)
    {
        provider = p;
        provider?.Load();
    }

    public static bool IsReady => Provider.IsReady;
    
    public static void Show(Action onCompleted = null) => Provider.Show(onCompleted);
}
