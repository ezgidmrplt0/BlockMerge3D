using UnityEngine;

// Bu objeyi ekranda SABİT bir viewport konumunda tutar (0,0 = sol alt, 1,1 = sağ üst),
// kameranın orthographic boyutu (leveldan levele board büyüklüğüne göre değişiyor, bkz.
// CameraOrbit.FitInView) veya konumu ne olursa olsun. Kontrol paneli süsleri (joystick,
// buton) gibi "her zaman ekranın aynı yerinde dursun" istenen 3D dekor objeleri için.
public class ScreenAnchoredProp : MonoBehaviour
{
    [Tooltip("0-1 aralığında ekran konumu (0,0 = sol alt, 1,1 = sağ üst)")]
    public Vector2 viewportPosition = new Vector2(0.5f, 0.05f);

    [Tooltip("Kameradan uzaklık (derinlik) — clipping'e girmeyecek makul bir değer seç")]
    public float depth = 10f;

    [Tooltip("Objeyi kameraya karşı sabit rotasyonda tut (kabin/panel süsleri için genelde açık kalmalı)")]
    public bool matchCameraRotation = true;

    [Tooltip("matchCameraRotation üzerine eklenen ek açı (X/Y/Z derece) — objenin tam olarak nasıl " +
             "eğik/dik duracağını buradan ince ayar yap, Play modundayken canlı görürsün")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("Boyut")]
    [Tooltip("Objenin ekranda görünmesini istediğin temel boyut çarpanı — Play modundayken canlı ayarlanabilir")]
    public float scale = 3f;

    [Tooltip("Bu referans orthographic boyutunda 'scale' birebir uygulanır. Kamera leveldan levele " +
             "daha fazla/az yakınlaştığında (bkz. CameraOrbit.FitInView) obje ekranda YİNE DE SABİT " +
             "görünür boyutta kalsın diye ölçek buna oranla otomatik ayarlanır.")]
    public float referenceOrthographicSize = 8f;

    private Camera cam;

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        transform.position = cam.ViewportToWorldPoint(new Vector3(viewportPosition.x, viewportPosition.y, depth));
        if (matchCameraRotation) transform.rotation = cam.transform.rotation * Quaternion.Euler(rotationOffset);
        else transform.rotation = Quaternion.Euler(rotationOffset);

        float sizeFactor = (cam.orthographic && referenceOrthographicSize > 0f)
            ? cam.orthographicSize / referenceOrthographicSize
            : 1f;
        transform.localScale = Vector3.one * scale * sizeFactor;
    }
}
