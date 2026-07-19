using UnityEngine;

/// <summary>
/// Buz modelini (Assets/Resources/IceCube.prefab) işaretler.
///
/// Buz modeli, hücrenin küpüne ÇOCUK olarak eklenir. LevelManager.ApplyTargetGhost
/// ise bir hücrenin TÜM alt renderer'larına ghost materyalini basıyor — işaretlenmezse
/// buzun kendi saydam materyalini de eziyor ve buz, ghost küpüyle aynı görünüyordu.
///
/// Renderer'ları maddi olarak değiştiren her kod bu işareti kontrol etmeli.
/// </summary>
public class IceVisualMarker : MonoBehaviour
{
}
