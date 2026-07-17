using UnityEngine;
using DG.Tweening;

// Dekoratif arcade joystick'ini gerçek bir kontrole çevirir: dokunup yatay sürükleyince
// board'u CameraOrbit.SnapRotate ile döndürür (eski swipe-anywhere mekaniğinin yerini alır).
// Stick koluna sürükleme sırasında ekran yönüne doğru bir eğilme animasyonu uygulanır,
// bırakılınca DOTween ile nötr konuma yaylanır.
[RequireComponent(typeof(Collider))]
public class ControlStick : MonoBehaviour
{
    [Tooltip("Eğilme animasyonu uygulanacak stick kolu (ControlStickfbx child'ı)")]
    public Transform stickHandle;

    [Tooltip("Tam döndürme komutu için gereken yatay sürükleme mesafesi (piksel)")]
    public float maxDragPixels = 60f;

    [Tooltip("maxDragPixels'in bu oranı kadar sürüklenirse bırakınca 90° snap tetiklenir")]
    [Range(0.1f, 1f)]
    public float snapThreshold = 0.5f;

    [Tooltip("Tam sürüklemede stick kolunun eğileceği maksimum açı (derece)")]
    public float maxTiltAngle = 22f;

    public float returnDuration = 0.25f;

    private Camera mainCam;
    private Quaternion handleRestRotation;
    private bool isDragging;
    private Vector2 dragStartScreenPos;

    private void Awake()
    {
        mainCam = Camera.main;
        if (stickHandle != null) handleRestRotation = stickHandle.localRotation;
    }

    private void Update()
    {
        if (!isDragging)
        {
            if (Input.GetMouseButtonDown(0) && !HitsPieceOrButton(Input.mousePosition))
                BeginDrag(Input.mousePosition);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            EndDrag(Input.mousePosition);
        }
        else
        {
            UpdateDrag(Input.mousePosition);
        }
    }

    private bool HitsPieceOrButton(Vector3 screenPos)
    {
        // UI Check
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            return true;
        }

        // Raycast Check
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return false;

        Ray ray = mainCam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // Parçalara veya butonlara dokunulduysa döndürmeyi engelle
            if (hit.transform.GetComponentInParent<DraggablePiece>() != null)
                return true;

            if (hit.transform.GetComponentInParent<ControlButton>() != null)
                return true;

            // Kart/Slot veya benzeri etkileşimli nesnelerin isim bazlı kontrolü
            if (hit.transform.name.Contains("Card") || hit.transform.name.Contains("Slot"))
                return true;
        }

        return false;
    }

    private void BeginDrag(Vector3 screenPos)
    {
        isDragging = true;
        dragStartScreenPos = screenPos;
    }

    private void UpdateDrag(Vector3 screenPos)
    {
        if (stickHandle == null) return;

        float deltaX = Mathf.Clamp(screenPos.x - dragStartScreenPos.x, -maxDragPixels, maxDragPixels);
        float t = deltaX / maxDragPixels;

        DOTween.Kill(stickHandle);
        stickHandle.localRotation = handleRestRotation * Quaternion.Euler(0f, 0f, -t * maxTiltAngle);
    }

    private void EndDrag(Vector3 screenPos)
    {
        isDragging = false;

        if (stickHandle != null)
        {
            DOTween.Kill(stickHandle);
            stickHandle.DOLocalRotateQuaternion(handleRestRotation, returnDuration).SetEase(Ease.OutBack);
        }

        float deltaX = screenPos.x - dragStartScreenPos.x;
        if (Mathf.Abs(deltaX) >= maxDragPixels * snapThreshold && CameraOrbit.Instance != null)
            CameraOrbit.Instance.SnapRotate(deltaX);
    }
}
