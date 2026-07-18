using UnityEngine;
using DG.Tweening;

// Dekoratif arcade butonuna dokunma geri bildirimi kazandırır: basılı tutulunca kapak
// (ControlButtonFbx) içeri çöker, bırakılınca yaylanarak geri döner. Şu an gameplay
// fonksiyonu yok — joker mantığı sonradan eklenecek.
[RequireComponent(typeof(Collider))]
public class ControlButton : MonoBehaviour
{
    [Tooltip("Basma animasyonu uygulanacak buton kapağı (ControlButtonFbx child'ı)")]
    public Transform buttonCap;

    [Header("Joker UI Settings")]
    [Tooltip("Joker kullanıldığında yok olacak olan şimşek/joker görsel ikonu")]
    public GameObject jokerIcon;

    [Header("Movement Settings")]
    [Tooltip("Buton kapağının basıldığındaki yerel kayma miktarı ve yönü (Ebeveyn koordinat sisteminde). Z ekseni derinliktir; eksi değerler içeri basılmayı, artı değerler dışarı çıkmayı temsil eder (Örn: X:0, Y:0, Z:-0.003).")]
    public Vector3 pressOffset = new Vector3(0, 0, -0.003f);
    public float pressDuration = 0.08f;
    public float releaseDuration = 0.18f;

    [Header("Tactile Click Settings")]
    [Tooltip("Basma anında buton kapağının kendi Y ekseninde (yükseklik) ne kadar ezileceği (örn. 0.90)")]
    public float pressSquashY = 0.9f;
    [Tooltip("Basma anında buton kapağının kendi X ve Z eksenlerinde (genişlik) ne kadar esneyeceği (örn. 1.05)")]
    public float pressStretchXZ = 1.05f;

    [Header("Easing Settings")]
    public Ease pressEase = Ease.OutQuad;
    public Ease releaseEase = Ease.OutBack;

    private Camera mainCam;
    private Vector3 restLocalPosition;
    private Vector3 originalScale;
    private bool isPressed;

    private void Awake()
    {
        mainCam = Camera.main;
        if (buttonCap != null)
        {
            restLocalPosition = buttonCap.localPosition;
            originalScale = buttonCap.localScale;
        }
    }

    private void Update()
    {
        // Seviye bittiyse joker kullanılamaz (bkz. GameManager.IsLevelOver).
        if (GameManager.Instance != null && GameManager.Instance.IsLevelOver) return;

        if (!isPressed)
        {
            if (Input.GetMouseButtonDown(0) && HitsSelf(Input.mousePosition))
            {
                if (LevelManager.Instance == null || LevelManager.Instance.CanUseJoker)
                {
                    Press();
                }
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            Release();
        }
    }

    private bool HitsSelf(Vector3 screenPos)
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return false;

        Ray ray = mainCam.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out RaycastHit hit) &&
               (hit.transform == transform || hit.transform.IsChildOf(transform));
    }

    private void Press()
    {
        if (buttonCap == null) return;
        isPressed = true;
        
        buttonCap.DOKill();
        
        // Doğrudan ebeveyn (parent) yerel uzayında hareket ettiriyoruz.
        // Bu sayede Inspector'daki Z değeri doğrudan derinliği (içeri/dışarı) kontrol eder.
        buttonCap.DOLocalMove(restLocalPosition + pressOffset, pressDuration).SetEase(pressEase);
        
        // Ezilme-büzülme (Squash & Stretch) efekti ile tıklama hissini güçlendiriyoruz
        Vector3 targetScale = new Vector3(
            originalScale.x * pressStretchXZ, 
            originalScale.y * pressSquashY, 
            originalScale.z * pressStretchXZ
        );
        buttonCap.DOScale(targetScale, pressDuration).SetEase(pressEase);

        // Şimşek ikonunu animasyonlu bir şekilde küçülterek yok et
        if (jokerIcon != null)
        {
            jokerIcon.transform.DOKill();
            jokerIcon.transform.DOScale(Vector3.zero, 0.25f)
                .SetEase(Ease.InBack)
                .OnComplete(() => jokerIcon.SetActive(false));
        }

        // Joker fonksiyonu: Son yerleştirilen parçayı yok eder
        LevelManager.Instance?.UndoLastPlace();
    }

    private void Release()
    {
        isPressed = false;
        if (buttonCap == null) return;
        
        buttonCap.DOKill();
        
        // Yaylanarak eski konumuna geri dönme
        buttonCap.DOLocalMove(restLocalPosition, releaseDuration).SetEase(releaseEase);
        buttonCap.DOScale(originalScale, releaseDuration).SetEase(releaseEase);
    }

    /// <summary>
    /// LevelManager tarafından yeni seviyeye geçildiğinde veya seviye sıfırlandığında çağrılır.
    /// Jokeri ve şimşek ikonunu sıfırlayıp animasyonlu bir şekilde geri getirir.
    /// </summary>
    public void ResetJoker()
    {
        isPressed = false;
        
        if (jokerIcon != null)
        {
            jokerIcon.transform.DOKill();
            jokerIcon.SetActive(true);
            jokerIcon.transform.localScale = Vector3.zero;
            // Pop-up scale animasyonu ile geri getir
            jokerIcon.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
        }

        if (buttonCap != null)
        {
            buttonCap.DOKill();
            buttonCap.localPosition = restLocalPosition;
            buttonCap.localScale = originalScale;
        }
    }
}
