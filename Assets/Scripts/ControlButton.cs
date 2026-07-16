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

    public float pressDepth = 0.05f;
    public float pressDuration = 0.08f;
    public float releaseDuration = 0.18f;

    private Camera mainCam;
    private Vector3 restLocalPosition;
    private bool isPressed;

    private void Awake()
    {
        mainCam = Camera.main;
        if (buttonCap != null) restLocalPosition = buttonCap.localPosition;
    }

    private void Update()
    {
        if (!isPressed)
        {
            if (Input.GetMouseButtonDown(0) && HitsSelf(Input.mousePosition)) Press();
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
        DOTween.Kill(buttonCap);
        buttonCap.DOLocalMove(restLocalPosition - Vector3.up * pressDepth, pressDuration).SetEase(Ease.OutCubic);
    }

    private void Release()
    {
        isPressed = false;
        if (buttonCap == null) return;
        DOTween.Kill(buttonCap);
        buttonCap.DOLocalMove(restLocalPosition, releaseDuration).SetEase(Ease.OutBack);
    }
}
