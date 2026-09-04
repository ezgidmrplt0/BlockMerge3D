/// <summary>
/// Bir analitik olayı anındaki oynanış bağlamı.
///
/// Neden var: "oyuncu Level 12'de 8 kez fail oldu" tek başına bir şey söylemiyor.
/// Sıfır hamleyle fail olduysa tahtayı ANLAMADI; sekiz hamleyle fail olduysa anladı
/// ama ÇÖZEMEDİ. İkisi farklı problem, farklı çözüm gerektirir. Bu alanlar o ayrımı
/// yapılabilir kılar.
///
/// Alanlar bu oyunun mekaniklerine göre uyarlandı (parça döndürme yok, tahta döndürme var).
/// </summary>
public struct LevelContext
{
    /// <summary>Level mekanik bayrakları — <see cref="LevelTypeFlags"/> ile birleştirilir.</summary>
    public int levelType;

    /// <summary>Bu denemede tahtaya yerleştirilen parça sayısı.</summary>
    public int movesMade;

    /// <summary>Bu denemede tahtanın kaç kez döndürüldüğü (swipe veya A/D) —
    /// mekaniğin keşfedilip keşfedilmediğini gösterir.</summary>
    public int rotationsUsed;

    /// <summary>Bu denemede patlatılan satır/sütun sayısı — ne kadar yaklaştığı.</summary>
    public int matchesMade;

    /// <summary>Olay anında oyuncunun elindeki parça sayısı (kartlar + hold).</summary>
    public int piecesRemaining;

    /// <summary>Bu cihazda bu levelin kaçıncı denemesi (1'den başlar).</summary>
    public int attemptNumber;

    /// <summary>Bu levelde öğretici gösteriliyor muydu.</summary>
    public bool tutorialShown;
}

/// <summary>
/// <see cref="LevelContext.levelType"/> için bit bayrakları. Bir level birden fazla
/// niteliğe sahip olabildiği için (ör. hem buzlu hem süreli) bayrak olarak tutulur.
/// </summary>
public static class LevelTypeFlags
{
    public const int Classic   = 1;   // özel mekanik yok
    public const int Ice       = 2;   // tahtada donmuş hücre var
    public const int Prefilled = 4;   // tahtada önceden dolu hücre var
    public const int Timed     = 8;   // süre sınırı var
    public const int MultiLayer = 16; // birden fazla katman
}
