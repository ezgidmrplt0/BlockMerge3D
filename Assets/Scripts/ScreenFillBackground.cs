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

    // Orijinal Koyu Lacivert Arka Plan Rengi (#0B1120)
    public static readonly Color DarkNavy = new Color(0.043f, 0.067f, 0.125f, 1f);

    private Camera cam;

    private void Awake()
    {
        ApplyBackgroundColor();
    }

    private void Start()
    {
        ApplyBackgroundColor();
    }

    private void OnEnable()
    {
        ApplyBackgroundColor();
    }

    public void ApplyBackgroundColor()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = DarkNavy;
        }

        var r = GetComponent<Renderer>();
        if (r != null)
        {
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty("_BaseColor")) r.sharedMaterial.SetColor("_BaseColor", DarkNavy);
                if (r.sharedMaterial.HasProperty("_Color")) r.sharedMaterial.SetColor("_Color", DarkNavy);
                r.sharedMaterial.color = DarkNavy;
            }
            if (r.material != null)
            {
                if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", DarkNavy);
                if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", DarkNavy);
                r.material.color = DarkNavy;
            }
        }
    }

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (cam.backgroundColor != DarkNavy)
        {
            cam.backgroundColor = DarkNavy;
        }

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
