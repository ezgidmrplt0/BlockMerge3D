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

    [Header("Swipe Snap")]
    [Tooltip("90° döndürme için gereken minimum yatay swipe mesafesi (pixel)")]
    public float swipeMinPixels = 60f;
    [Tooltip("Küp döndürme animasyon hızı")]
    public float snapSpeed      = 10f;

    private float currentYaw;
    private float targetYaw;
    public float TargetYaw => targetYaw;

    // Swipe takibi
    private bool    trackingSwipe;
    private bool    swipeCancelled;
    private Vector2 swipeStartPos;
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
            float requiredSize = radius;
            if (cam.aspect > 0f)
            {
                requiredSize = Mathf.Max(radius, radius / cam.aspect);
            }
            
            // Dinamik sığdırma katsayısı (Grid boyutuna göre konforlu kenar boşluğu)
            float multiplier = 1.15f + (radius * 0.05f);
            float oldOrthSize = cam.orthographicSize;
            cam.orthographicSize = requiredSize * multiplier;


            // Kırpılmayı önlemek için kamerayı yine de güvenli bir arkaya konumlandırıyoruz
            float distance = radius * 4f;
            Quaternion rot = Quaternion.Euler(startElevation, startAzimuth, 0f);
            transform.rotation = rot;
            Vector3 oldPos = transform.position;
            transform.position = bounds.center + rot * new Vector3(0f, 0f, -distance);
            
        }
        else
        {
            // --- PERSPECTIVE PROJECTION ---
            float vHalfRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalfRad = Mathf.Atan(Mathf.Tan(vHalfRad) * cam.aspect);
            float minHalf  = Mathf.Min(vHalfRad, hHalfRad);
            float multiplier = 2.0f + (radius * 0.15f);
            float distance = (radius / Mathf.Tan(minHalf)) * multiplier;


            Quaternion rot = Quaternion.Euler(startElevation, startAzimuth, 0f);
            transform.rotation = rot;
            Vector3 oldPos = transform.position;
            transform.position = bounds.center + rot * new Vector3(0f, 0f, -distance);
            
        }

        // Küpün yeni başlangıç rotasyonunu belirle
        Transform targetRotate = pivot != null ? pivot : cube;
        if (targetRotate != null)
        {
            currentYaw = targetRotate.eulerAngles.y;
            targetYaw  = currentYaw;
            ApplyRotation();
        }
    }

    public void ZoomToLayer(Vector3 layerWorldCenter, System.Action onComplete = null)
    {
        IsInPanelMode = true;
        IsLocked = true;

        savedCamPos = transform.position;
        savedCamRot = transform.rotation;

        // Target: straight above the layer center
        Camera cam = GetComponent<Camera>() ?? Camera.main;
        float distance = cam != null && cam.orthographic ? cam.orthographicSize * 3f : 20f;
        Vector3 targetPos = layerWorldCenter + Vector3.up * distance;
        Quaternion targetRot = Quaternion.Euler(90f, 0f, 0f);

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

        bool isTransitioning = DOTween.IsTweening(transform);
        if (DraggablePiece.IsDragging || isTransitioning || (IsLocked && !IsInPanelMode)) 
        {
            swipeCancelled = true;
            trackingSwipe = false;
        }

        HandleMouseSwipe();
        HandleTouchSwipe();
    }

    private void HandleMouseSwipe()
    {
        if (Input.GetMouseButtonDown(0))
        {
            trackingSwipe  = true;
            swipeCancelled = false;
            swipeStartPos  = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(0) && trackingSwipe)
        {
            trackingSwipe = false;
            if (!swipeCancelled)
            {
                Vector2 delta = (Vector2)Input.mousePosition - swipeStartPos;
                TrySnapFromSwipe(delta);
            }
        }
    }

    private void HandleTouchSwipe()
    {
        if (Input.touchCount != 1) { trackingSwipe = false; return; }

        Touch t = Input.GetTouch(0);
        switch (t.phase)
        {
            case TouchPhase.Began:
                trackingSwipe  = true;
                swipeCancelled = false;
                swipeStartPos  = t.position;
                break;
            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                if (trackingSwipe)
                {
                    trackingSwipe = false;
                    if (!swipeCancelled)
                    {
                        Vector2 delta = t.position - swipeStartPos;
                        TrySnapFromSwipe(delta);
                    }
                }
                break;
        }
    }

    private void TrySnapFromSwipe(Vector2 delta)
    {
        if (Mathf.Abs(delta.x) < swipeMinPixels) return;
        if (Mathf.Abs(delta.x) < Mathf.Abs(delta.y)) return;

        // Ekranda yatayda (X ekseninde) swipe yapıldığında küp Y ekseninde 90 derece döner
        // Swipe sağa (delta.x > 0) -> sola dön (+ angle'ı azalt)
        // Swipe sola (delta.x < 0) -> sağa dön (+ angle'ı arttır)
        targetYaw -= Mathf.Sign(delta.x) * 90f;
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
