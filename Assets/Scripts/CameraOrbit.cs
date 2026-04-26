using UnityEngine;

public class CameraOrbit : MonoBehaviour
{
    public static CameraOrbit Instance { get; private set; }

    [Header("Target")]
    public Transform pivot;
    public float distance = 14f;

    [Header("Initial Angle")]
    public float startAzimuth   = 25f;
    public float startElevation = 28f;

    [Header("Swipe Snap")]
    [Tooltip("90° döndürme için gereken minimum yatay swipe mesafesi (pixel)")]
    public float swipeMinPixels = 60f;
    [Tooltip("Kamera döndürme animasyon hızı")]
    public float snapSpeed      = 10f;

    [Header("Auto-Fit")]
    [Tooltip("Sahne sığıştırılırken kenar boşluğu (oran)")]
    public float fitPadding = 1.18f;

    private float azimuth;
    private float elevation;
    private float targetAzimuth;

    // Swipe takibi
    private bool    trackingSwipe;
    private bool    swipeCancelled;
    private Vector2 swipeStartPos;

    private void Awake()
    {
        Instance       = this;
        azimuth        = startAzimuth;
        elevation      = startElevation;
        targetAzimuth  = startAzimuth;
    }

    /// <summary>Verilen bounds'u ekrana tam sığıdıracak şekilde kamerayı konumlandırır (portrait uyumlu).</summary>
    public void FitInView(Bounds bounds)
    {
        if (pivot != null) pivot.position = bounds.center;

        Camera cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null) { ApplyOrbit(); return; }

        float radius   = bounds.extents.magnitude;
        float vHalfRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float hHalfRad = Mathf.Atan(Mathf.Tan(vHalfRad) * cam.aspect);
        float minHalf  = Mathf.Min(vHalfRad, hHalfRad);

        distance = (radius / Mathf.Tan(minHalf)) * fitPadding;
        ApplyOrbit();
    }

    private void Start() => ApplyOrbit();

    private void Update()
    {
        // Animasyon: azimuth'u hedefine doğru yumuşakça çek
        if (!Mathf.Approximately(azimuth, targetAzimuth))
        {
            azimuth = Mathf.LerpAngle(azimuth, targetAzimuth, Time.deltaTime * snapSpeed);
            if (Mathf.Abs(Mathf.DeltaAngle(azimuth, targetAzimuth)) < 0.1f)
                azimuth = targetAzimuth;
            ApplyOrbit();
        }

        // Parça sürükleniyorsa swipe iptal
        if (DraggablePiece.IsDragging) swipeCancelled = true;

        HandleMouseSwipe();
        HandleTouchSwipe();
    }

    // ── Mouse swipe ───────────────────────────────────────────────────────────

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

    // ── Touch swipe ───────────────────────────────────────────────────────────

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

    // ── Snap logic ────────────────────────────────────────────────────────────

    private void TrySnapFromSwipe(Vector2 delta)
    {
        // Yalnızca baskın yatay harekette tetikle
        if (Mathf.Abs(delta.x) < swipeMinPixels) return;
        if (Mathf.Abs(delta.x) < Mathf.Abs(delta.y)) return;

        // Sağa sürükle → obje sağa döner → azimuth -90
        // Sola sürükle → obje sola döner → azimuth +90
        targetAzimuth -= Mathf.Sign(delta.x) * 90f;
    }

    private void ApplyOrbit()
    {
        if (pivot == null) return;
        Quaternion rot = Quaternion.Euler(elevation, azimuth, 0f);
        transform.position = pivot.position + rot * new Vector3(0f, 0f, -distance);
        transform.rotation = rot;
    }
}
