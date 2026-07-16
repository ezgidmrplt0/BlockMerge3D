using UnityEngine;

// Ortografik kameranın gördüğü alanı TAM olarak kaplayan bir arka plan quad'ı.
// Kamera CameraOrbit ile döndüğünde veya orthographicSize leveldan levele
// değiştiğinde (bkz. CameraOrbit.FitInView) bile ekranı eksiksiz kaplamaya devam eder.
// ScreenAnchoredProp'un aksine sabit bir ölçek oranı değil, her karede kameranın
// gerçek görüş alanına göre YENİDEN hesaplanan boyut kullanır.
public class ScreenFillBackground : MonoBehaviour
{
    [Tooltip("Kameradan uzaklık (derinlik) — board ve taşların GERİSİNDE kalacak bir değer seç")]
    public float depth = 60f;

    private Camera cam;

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        transform.position = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, depth));
        transform.rotation = cam.transform.rotation;

        if (cam.orthographic)
        {
            float height = 2f * cam.orthographicSize;
            float width = height * cam.aspect;
            transform.localScale = new Vector3(width, height, 1f);
        }
    }
}
