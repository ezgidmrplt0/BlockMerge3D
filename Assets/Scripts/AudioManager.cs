using UnityEngine;

/// <summary>
/// Oyun içi ses efektlerini (SFX) yöneten Singleton sınıfı.
/// Parçaları yerleştirme, buz erime ve buton tıklama seslerini kontrol eder.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [Tooltip("Ses efektlerini çalmak için kullanılacak AudioSource. Atanmazsa Awake aşamasında otomatik oluşturulur.")]
    [SerializeField] private AudioSource sfxSource;

    [Header("Audio Clips")]
    [Tooltip("Parçaları tahtaya yerleştirme sesi.")]
    [SerializeField] private AudioClip placementSound;

    [Tooltip("Buz erime / kırılma sesi.")]
    [SerializeField] private AudioClip iceMeltSound;

    [Tooltip("Arayüz butonlarına tıklama sesi.")]
    [SerializeField] private AudioClip buttonClickSound;

    [Header("Settings")]
    [Tooltip("Yerleştirme sesi için hafif perde (pitch) dalgalanması yapılsın mı? (Daha doğal bir his verir)")]
    [SerializeField] private bool usePitchVariation = true;
    [SerializeField] [Range(0.8f, 1.2f)] private float minPitch = 0.95f;
    [SerializeField] [Range(0.8f, 1.2f)] private float maxPitch = 1.05f;

    private void Awake()
    {
        // Singleton Deseni kurulumu
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // AudioSource kontrolü
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // 2D sesler için yapılandırma
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f; // 2D Sound
    }

    /// <summary>
    /// Bir ses klibini 2D olarak çalar.
    /// </summary>
    public void PlaySFX(AudioClip clip, float pitch = 1f)
    {
        if (clip == null || sfxSource == null) return;

        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip);
    }

    /// <summary>
    /// Parça yerleştirildiğinde tetiklenir.
    /// </summary>
    public void PlayPlacementSound()
    {
        if (placementSound == null) return;

        float pitch = usePitchVariation ? Random.Range(minPitch, maxPitch) : 1f;
        PlaySFX(placementSound, pitch);
    }

    /// <summary>
    /// Buz hücresi eridiğinde tetiklenir.
    /// </summary>
    public void PlayIceMeltSound()
    {
        if (iceMeltSound == null) return;
        
        // Buz erime sesini orijinal perdesinde çal
        PlaySFX(iceMeltSound, 1f);
    }

    /// <summary>
    /// Butonlara tıklandığında tetiklenir.
    /// </summary>
    public void PlayButtonClickSound()
    {
        if (buttonClickSound == null) return;

        // Buton tıklama sesini orijinal perdesinde çal
        PlaySFX(buttonClickSound, 1f);
    }
}
