using System;

// ═══════════════════════════════════════════════════════════════════
//  TUTORIAL EVENTS  —  Öğretici Adımlarının Tamamlanma Sinyalleri
//  BlockMerge3D
//
//  TutorialOverlay'in oyun sistemlerine (LayerPanelController, CameraOrbit,
//  LevelManager) doğrudan bağlanmaması için araya konan ince katman.
//  Oyun tarafı sadece "şu oldu" diye haber verir, öğreticinin var olup
//  olmadığını bilmez.
// ═══════════════════════════════════════════════════════════════════

public static class TutorialEvents
{
    /// <summary>Oyuncu ekranı yatay kaydırıp tahtayı 90° döndürdü.</summary>
    public static event Action BoardRotated;

    /// <summary>Oyuncu bir parçayı tahtaya yerleştirdi.</summary>
    public static event Action PiecePlaced;

    /// <summary>Oyuncu joker (geri al) butonunu kullandı.</summary>
    public static event Action JokerUsed;

    /// <summary>Oyuncu bir parçayı HOLD (sakla) kutusuna sürükledi.</summary>
    public static event Action PieceHeld;

    public static void RaiseBoardRotated() => BoardRotated?.Invoke();
    public static void RaisePiecePlaced()  => PiecePlaced?.Invoke();
    public static void RaiseJokerUsed()    => JokerUsed?.Invoke();
    public static void RaisePieceHeld()    => PieceHeld?.Invoke();
}
