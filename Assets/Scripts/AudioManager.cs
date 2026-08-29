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

    [Tooltip("Arayüz butonlarına tıklama sesi (genel).")]
    [SerializeField] private AudioClip buttonClickSound;

    [Tooltip("Katman butonuna basma sesi (katmana girerken).")]
    [SerializeField] private AudioClip layerButtonSound;

    [Tooltip("Geri butonuna basma sesi (katmandan çıkarken).")]
    [SerializeField] private AudioClip backButtonSound;

    [Tooltip("Kanca item'leri kavradığı an çalan ses.")]
    [SerializeField] private AudioClip clawGrabSound;

    [Tooltip("Tahta 90° döndürülürken çalan swoosh sesi.")]
    [SerializeField] private AudioClip boardRotateSound;

    [Tooltip("Kazanma paneli açılınca çalan ses.")]
    [SerializeField] private AudioClip winSound;

    [Tooltip("Kaybetme paneli açılınca çalan ses.")]
    [SerializeField] private AudioClip loseSound;

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

    /// <summary>Katman butonuna basınca (katmana girerken).</summary>
    public void PlayLayerButtonSound()
    {
        // Klip atanmamışsa genel tık sesine düş.
        PlaySFX(layerButtonSound != null ? layerButtonSound : buttonClickSound, 1f);
    }

    /// <summary>Geri butonuna basınca (katmandan çıkarken).</summary>
    public void PlayBackButtonSound()
    {
        PlaySFX(backButtonSound != null ? backButtonSound : buttonClickSound, 1f);
    }

    /// <summary>Kazanma paneli açılınca (bkz. UIManager.ShowWinPanel).</summary>
    public void PlayWinSound()
    {
        if (winSound == null) return;
        PlaySFX(winSound, 1f);
    }

    /// <summary>Kaybetme paneli açılınca (bkz. UIManager.ShowLosePanel).</summary>
    public void PlayLoseSound()
    {
        if (loseSound == null) return;
        PlaySFX(loseSound, 1f);
    }

    /// <summary>
    /// Kanca bir katmanı temizlemek için harekete geçtiğinde tetiklenir
    /// (bkz. GridManager.AnimateLayerDisappear).
    /// </summary>
    /// <summary>Kanca item'leri kavradığı an (bkz. GridManager.RunGrabAndLift).</summary>
    public void PlayClawGrabSound()
    {
        if (clawGrabSound == null) return;
        PlaySFX(clawGrabSound, 1f);
    }

    /// <summary>Tahta 90° döndürüldüğünde (bkz. CameraOrbit.SnapRotate).</summary>
    public void PlayBoardRotateSound()
    {
        if (boardRotateSound == null) return;
        PlaySFX(boardRotateSound, 1f);
    }

    /// <summary>Block Blast tarzı satır/sütun patladığında tetiklenir.</summary>
    public void PlayLineClearSound(int combo = 1)
    {
        float pitch = Mathf.Clamp(1f + (combo - 1) * 0.15f, 1f, 2.2f);
        if (iceMeltSound != null)
            PlaySFX(iceMeltSound, pitch);
        else if (placementSound != null)
            PlaySFX(placementSound, pitch);
    }

    /// <summary>Kule katmanı çöktüğünde ve üst katmanlar düştüğünde tetiklenir.</summary>
    public void PlayCollapseSound()
    {
        if (clawGrabSound != null)
            PlaySFX(clawGrabSound, 0.85f);
        else if (iceMeltSound != null)
            PlaySFX(iceMeltSound, 0.8f);
        else if (placementSound != null)
            PlaySFX(placementSound, 0.75f);
    }

    /// <summary>Kule katmanı hasar alıp çatladığında tetiklenir.</summary>
    public void PlayCrackSound(int stage = 1)
    {
        float pitch = Mathf.Clamp(1.35f + stage * 0.2f, 1.2f, 2.5f);
        if (iceMeltSound != null)
            PlaySFX(iceMeltSound, pitch);
        else if (placementSound != null)
            PlaySFX(placementSound, pitch);
    }
}
