using UnityEngine;
using DG.Tweening;

public class CameraOrbit : MonoBehaviour
{
    public static CameraOrbit Instance { get; private set; }

    [Header("Target")]
    public Transform cube;
    [Tooltip("Geriye dönük uyumluluk için pivot alanı")]
    public Transform pivot;

    [Header("Initial 3D Camera Angles")]
    [Tooltip("Kameranın başlangıçtaki yatay bakış açısı (Y rotasyonu)")]
    public float startAzimuth = 25f;
    [Tooltip("Kameranın başlangıçtaki dikey bakış açısı (X rotasyonu)")]
    public float startElevation = 28f;

    [Header("Snap")]
    [Tooltip("Küp döndürme animasyon hızı")]
    public float snapSpeed      = 10f;

    [Header("Sabit Zoom")]
    [Tooltip("Açıkken kamera her level'de (2x2, 3x3, 4x4...) board boyutundan bağımsız aynı " +
             "zoom/mesafede durur — küçük board'lar için yakınlaşmaz, büyükler için uzaklaşmaz. " +
             "Kapalıysa eski davranışa (board'a göre otomatik sığdırma) döner.")]
    public bool useFixedZoom = true;
    [Tooltip("useFixedZoom açıkken kullanılan sabit orthographic size (kamera orthographic ise). " +
             "ScreenAnchoredProp'un referenceOrthographicSize'ı ile aynı tutulursa (varsayılan 8) " +
             "joystick/buton gibi ekran-sabit objelerin boyutu da tüm levellerde birebir tutarlı kalır.")]
    public float fixedOrthographicSize = 8f;
    [Tooltip("useFixedZoom açıkken kullanılan sabit kamera mesafesi (perspective kamerada zorunlu, " +
             "orthographic'te sadece clipping'e girmeyecek kadar geride durmak için).")]
    public float fixedDistance = 40f;

    private float currentYaw;
    private float targetYaw;
    public float TargetYaw => targetYaw;

    public bool     IsLocked { get; set; }

    // Panel mode: saved 3D state
    private Vector3    savedCamPos;
    private Quaternion savedCamRot;
    public  bool       IsInPanelMode { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        IsLocked = false; // Layer-by-layer mode disables camera rotation
        
        if (cube == null && pivot != null)
        {
            cube = pivot;
        }

        // Eğer çalışma zamanında Main_Shape zaten oluşturulmuşsa pivot'a bağla
        GameObject mainShapeObj = GameObject.Find("Main_Shape");
        if (mainShapeObj != null && pivot != null)
        {
            mainShapeObj.transform.SetParent(pivot, true);
            cube = mainShapeObj.transform;
        }

        Transform targetRotate = pivot != null ? pivot : cube;
        if (targetRotate != null)
        {
            currentYaw = targetRotate.eulerAngles.y;
            targetYaw  = currentYaw;
            ApplyRotation();
        }
    }

    /// <summary>
    /// Tahtanın (pivot) swipe ile birikmiş döndürmesini nötrler.
    /// Yeni bir ana şekil pivot'a parent'lanmadan ÖNCE çağrılmalıdır; aksi halde
    /// SetParent(..., true) eski açıyı telafi eden bir local rotasyon "kilitler"
    /// ve döndürme sonradan sıfırlanınca şekil görsel olarak yanlış açıda kalır.
    /// </summary>
    public void ResetBoardRotation()
    {
        DOTween.Kill(transform);
        currentYaw = 0f;
        targetYaw  = 0f;
        if (pivot != null) pivot.rotation = Quaternion.identity;
        else if (cube != null) cube.rotation = Quaternion.identity;
    }

    public void FitInView(Bounds bounds)
    {
        DOTween.Kill(transform);
        IsInPanelMode = false;
        IsLocked = false;

        // Eğer cube henüz atanmamışsa sahneden bulmaya çalış (fallback)
        if (cube == null)
        {
            GameObject mainShapeObj = GameObject.Find("Main_Shape");
            if (mainShapeObj != null)
            {
                if (pivot != null) mainShapeObj.transform.SetParent(pivot, true);
                cube = mainShapeObj.transform;
            }
        }

        // Sadece ana şeklin (tahtanın) sınırlarını hesaplayarak pivotu tam ortasına hizala
        if (pivot != null && cube != null)
        {
            Bounds boardBounds = new Bounds();
            bool firstRenderer = true;
            foreach (var r in cube.GetComponentsInChildren<Renderer>())
            {
                if (firstRenderer)
                {
                    boardBounds = r.bounds;
                    firstRenderer = false;
                }
                else
                {
                    boardBounds.Encapsulate(r.bounds);
                }
            }

            // Geçici olarak parent'ı null yap ki pivot hareket ettiğinde küp kaymasın!
            cube.SetParent(null, true);

            if (!firstRenderer)
            {
                // Pivot'u tam olarak ana şeklin görsel merkezine konumlandır
                pivot.position = boardBounds.center;
            }
            else
            {
                pivot.position = cube.position;
            }

            // Küpü tekrar pivot'un child'ı yap (dünya pozisyonunu koruyarak)
            cube.SetParent(pivot, true);
        }
        else if (pivot != null)
        {
            pivot.position = bounds.center;
        }

        Camera cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null) 
        {
            return;
        }

        // Sahneyi kameraya sığdıracak doğru mesafeyi hesapla
        float radius   = bounds.extents.magnitude;
        
        if (cam.orthographic)
        {
            // --- ORTHOGRAPHIC PROJECTION ---
            float orthoSize;
            float distance;

            if (useFixedZoom)
            {
                orthoSize = fixedOrthographicSize;
                distance  = fixedDistance;
            }
            else
            {
                float requiredSize = radius;
                if (cam.aspect > 0f)
                {
                    requiredSize = Mathf.Max(radius, radius / cam.aspect);
                }

                // Dinamik sığdırma katsayısı (Grid boyutuna göre konforlu kenar boşluğu)
                float multiplier = 1.15f + (radius * 0.05f);
                orthoSize = requiredSize * multiplier;

                // Kırpılmayı önlemek için kamerayı yine de güvenli bir arkaya konumlandırıyoruz
                distance = radius * 4f;
            }

            cam.orthographicSize = orthoSize;
            Quaternion rot = Quaternion.Euler(startElevation, startAzimuth, 0f);
            transform.rotation = rot;
            transform.position = bounds.center + rot * new Vector3(0f, 0f, -distance);
        }
        else
        {
            // --- PERSPECTIVE PROJECTION ---
            float distance;

            if (useFixedZoom)
            {
                distance = fixedDistance;
            }
            else
            {
                float vHalfRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float hHalfRad = Mathf.Atan(Mathf.Tan(vHalfRad) * cam.aspect);
                float minHalf  = Mathf.Min(vHalfRad, hHalfRad);
                float multiplier = 2.0f + (radius * 0.15f);
                distance = (radius / Mathf.Tan(minHalf)) * multiplier;
            }

            Quaternion rot = Quaternion.Euler(startElevation, startAzimuth, 0f);
            transform.rotation = rot;
            transform.position = bounds.center + rot * new Vector3(0f, 0f, -distance);
        }

        // Yeni seviye her zaman nötr (0°) tahta rotasyonuyla başlar; önceki seviyeden
        // kalan swipe döndürmesi bir sonraki seviyeye taşınmamalı (parça yerleşim yönü kayar).
        currentYaw = 0f;
        targetYaw  = 0f;
        ApplyRotation();
    }

    public void ZoomToLayer(Vector3 layerWorldCenter, System.Action onComplete = null)
    {
        if (!IsInPanelMode)
        {
            savedCamPos = transform.position;
            savedCamRot = transform.rotation;
        }

        IsInPanelMode = true;
        IsLocked = true;

        Camera cam = GetComponent<Camera>() ?? Camera.main;
        float distance = cam != null && cam.orthographic ? cam.orthographicSize * 2.5f : 15f;
        
        // 2.5D Isometric tilted angle (55 degrees elevation, 30 degrees azimuth)
        Quaternion targetRot = Quaternion.Euler(55f, 30f, 0f);
        Vector3 targetPos = layerWorldCenter + targetRot * new Vector3(0f, 0f, -distance);

        DOTween.Kill(transform);
        Sequence seq = DOTween.Sequence();
        seq.Join(transform.DOMove(targetPos, 0.4f).SetEase(Ease.InOutCubic));
        seq.Join(transform.DORotateQuaternion(targetRot, 0.4f).SetEase(Ease.InOutCubic));
        seq.OnComplete(() => onComplete?.Invoke());
    }

    public void ReturnTo3D(System.Action onComplete = null)
    {
        DOTween.Kill(transform);
        Sequence seq = DOTween.Sequence();
        seq.Join(transform.DOMove(savedCamPos, 0.4f).SetEase(Ease.InOutCubic));
        seq.Join(transform.DORotateQuaternion(savedCamRot, 0.4f).SetEase(Ease.InOutCubic));
        seq.OnComplete(() => {
            IsInPanelMode = false;
            IsLocked = false; // 3D modunda kamera dönebilir
            onComplete?.Invoke();
        });
    }

    public void Shake(float duration = 0.25f, float strength = 0.15f)
    {
        transform.DOShakePosition(duration, strength, 15, 90, false, true);
    }

    /// <summary>
    /// ControlStick tarafından çağrılır — joystick'in sürüklenme yönüne göre board'u 90° döndürür.
    /// direction > 0 -> sağa sürükleme, direction &lt; 0 -> sola sürükleme.
    /// </summary>
    public void SnapRotate(float direction)
    {
        if (DraggablePiece.IsDragging || DOTween.IsTweening(transform) || (IsLocked && !IsInPanelMode)) return;
        targetYaw -= Mathf.Sign(direction) * 90f;
    }

    private void Update()
    {
        // Animasyon: currentYaw'u hedefine doğru yumuşakça çek
        if (!Mathf.Approximately(currentYaw, targetYaw))
        {
            currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, Time.deltaTime * snapSpeed);
            if (Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw)) < 0.1f)
                currentYaw = targetYaw;

            ApplyRotation();
        }
    }

    private void ApplyRotation()
    {
        if (pivot != null)
        {
            pivot.rotation = Quaternion.Euler(0f, currentYaw, 0f);
        }
        else if (cube != null)
        {
            cube.rotation = Quaternion.Euler(0f, currentYaw, 0f);
        }
    }
}
