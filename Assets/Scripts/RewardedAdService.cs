using System;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  REWARDED AD SERVICE  —  SDK'dan Bağımsız Ödüllü Reklam Katmanı
//  BlockMerge3D
//
//  Oyun kodu SADECE IRewardedAd arayüzünü ve RewardedAds statik cephesini
//  görür. Gerçek ağ (Unity LevelPlay / AppLovin MAX / AdMob ...) için TEK
//  bir adapter sınıfı yazılır ve uygulama açılışında bir kez
//  RewardedAds.SetProvider(new BenimAgAdapterim()) ile takılır. Geri kalan
//  hiçbir kod değişmez.
//
//  Ağ takılana kadar editörde StubRewardedAd devrededir: her zaman "hazır"
//  ve gösterilince ödülü ANINDA verir — böylece joker-yenileme akışı gerçek
//  reklam olmadan test edilebilir.
// ═══════════════════════════════════════════════════════════════════

public interface IRewardedAd
{
    /// <summary>Şu an gösterilmeye hazır bir reklam var mı?</summary>
    bool IsReady { get; }

    /// <summary>Yeni bir reklam yüklemeye başla (fire-and-forget).</summary>
    void Load();

    /// <summary>Reklamı göster. Ödül SADECE reklam tamamlanınca onReward ile verilir;
    /// atlanır/kapatılır/yüklenemezse onFailed çağrılır.</summary>
    void Show(Action onReward, Action onFailed);
}

/// <summary>Editör / SDK yokken kullanılan sahte reklam.</summary>
public class StubRewardedAd : IRewardedAd
{
    public bool IsReady => true;
    public void Load() { }
    public void Show(Action onReward, Action onFailed)
    {
        Debug.Log("[RewardedAds] STUB reklam gösterildi → ödül anında veriliyor (gerçek ağ takılınca değişecek).");
        onReward?.Invoke();
    }
}

public static class RewardedAds
{
    private static IRewardedAd provider;
    public static IRewardedAd Provider => provider ??= new StubRewardedAd();

    /// <summary>Gerçek ağ adapter'ını uygulama açılışında bir kez tak.</summary>
    public static void SetProvider(IRewardedAd p)
    {
        provider = p;
        provider?.Load();
    }

    public static bool IsReady => Provider.IsReady;
    public static void Show(Action onReward, Action onFailed = null) => Provider.Show(onReward, onFailed);
}
