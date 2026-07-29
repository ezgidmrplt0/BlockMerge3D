using UnityEngine;

public class FaceCamera : MonoBehaviour
{
    [Tooltip("If true, all instances face in the same parallel direction (recommended for isometric). If false, they look directly at the camera position.")]
    public bool useParallel = true;

    /// <summary>Kamera hizasının ÜSTÜNE, local eksende eklenen ekstra döndürme — örn.
    /// oyuncu bir parçayı elindeyken döndürünce (bkz. DraggablePiece.RotateAroundY/X),
    /// içindeki hayvan görseli de bu kadar dönsün diye kullanılır.</summary>
    [HideInInspector] public Quaternion extraRotation = Quaternion.identity;

    private Camera mainCam;

    private void Start()
    {
        mainCam = Camera.main;
    }

    private void LateUpdate()
    {
        if (mainCam == null)
        {
            mainCam = Camera.main;
        }

        if (mainCam != null)
        {
            if (useParallel)
            {
                Vector3 camForward = mainCam.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude > 0.001f)
                {
                    camForward.Normalize();
                    transform.rotation = Quaternion.LookRotation(camForward, Vector3.up) * extraRotation;
                }
            }
            else
            {
                Vector3 targetPos = mainCam.transform.position;
                targetPos.y = transform.position.y;
                Vector3 dir = targetPos - transform.position;
                if (dir.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up) * extraRotation;
                }
            }
        }
    }
}
