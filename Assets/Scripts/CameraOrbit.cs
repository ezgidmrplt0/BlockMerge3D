using UnityEngine;

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

    // Swipe takibi
    private bool    trackingSwipe;
    private bool    swipeCancelled;
    private Vector2 swipeStartPos;
    public bool     IsLocked { get; set; }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
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
        if (cam == null) return;

        // Sahneyi kameraya sığdıracak doğru mesafeyi hesapla
        float radius   = bounds.extents.magnitude;
        float vHalfRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float hHalfRad = Mathf.Atan(Mathf.Tan(vHalfRad) * cam.aspect);
        float minHalf  = Mathf.Min(vHalfRad, hHalfRad);
        float distance = (radius / Mathf.Tan(minHalf)) * 1.18f;
        
        // Kamerayı izometrik 3D gösterecek şekilde, tüm sahnenin (tahta + slotlar) merkezini baz alarak konumlandır (İlk yüklemede 1 kez)
        Quaternion rot = Quaternion.Euler(startElevation, startAzimuth, 0f);
        transform.rotation = rot;
        transform.position = bounds.center + rot * new Vector3(0f, 0f, -distance);

        // Küpün yeni başlangıç rotasyonunu belirle
        Transform targetRotate = pivot != null ? pivot : cube;
        if (targetRotate != null)
        {
            currentYaw = targetRotate.eulerAngles.y;
            targetYaw  = currentYaw;
            ApplyRotation();
        }
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

        if (DraggablePiece.IsDragging || IsLocked) 
        {
            swipeCancelled = true;
            if (IsLocked) trackingSwipe = false;
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
