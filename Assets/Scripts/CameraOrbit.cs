using UnityEngine;

public class CameraOrbit : MonoBehaviour
{
    public static CameraOrbit Instance { get; private set; }

    [Header("Target")]
    public Transform pivot;
    public Transform rotatableTarget;
    public float distance = 14f;

    [Header("Initial Angle")]
    public float startAzimuth   = 25f;
    public float startElevation = 28f;

    [Header("Object Rotation")]
    [Tooltip("90° döndürme için gereken minimum yatay swipe mesafesi (pixel)")]
    public float swipeMinPixels = 60f;
    [Tooltip("Obje döndürme animasyon hızı")]
    public float snapSpeed      = 10f;

    private Quaternion targetObjectRotation = Quaternion.identity;

    private bool    trackingSwipe;
    private bool    swipeCancelled;
    private Vector2 swipeStartPos;
    public bool     IsLocked { get; set; }

    private void Awake()
    {
        Instance = this;
        if (rotatableTarget != null)
            targetObjectRotation = rotatableTarget.rotation;
    }

    public void FitInView(Bounds bounds)
    {
        if (pivot != null) pivot.position = bounds.center;

        Camera cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null) { ApplyCameraPosition(); return; }

        float radius   = bounds.extents.magnitude;
        float vHalfRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float hHalfRad = Mathf.Atan(Mathf.Tan(vHalfRad) * cam.aspect);
        float minHalf  = Mathf.Min(vHalfRad, hHalfRad);

        distance = (radius / Mathf.Tan(minHalf)) * 1.18f;
        ApplyCameraPosition();
    }

    private void Start()
    {
        if (rotatableTarget == null && LevelManager.Instance != null)
        {
            rotatableTarget = LevelManager.Instance.mainCubeLocation;
            if (rotatableTarget != null)
                targetObjectRotation = rotatableTarget.rotation;
        }
        ApplyCameraPosition();
    }

    private void Update()
    {
        if (rotatableTarget != null)
        {
            if (Quaternion.Angle(rotatableTarget.rotation, targetObjectRotation) > 0.1f)
                rotatableTarget.rotation = Quaternion.Slerp(
                    rotatableTarget.rotation, targetObjectRotation, Time.deltaTime * snapSpeed);
            else
                rotatableTarget.rotation = targetObjectRotation;
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

        targetObjectRotation = Quaternion.Euler(0f, Mathf.Sign(delta.x) * 90f, 0f) * targetObjectRotation;
    }

    private void ApplyCameraPosition()
    {
        if (pivot == null) return;
        Quaternion rot = Quaternion.Euler(startElevation, startAzimuth, 0f);
        transform.position = pivot.position + rot * new Vector3(0f, 0f, -distance);
        transform.rotation = rot;
    }
}
