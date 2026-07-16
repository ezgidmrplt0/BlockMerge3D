using System.Collections.Generic;
using UnityEngine;

// Kancanın ucundaki Collider'ın, patlatılacak katmandaki bloklara GERÇEKTEN
// değdiği fiziksel anı yakalamak için kullanılır. GridManager, kanca inişe
// geçmeden hemen önce Arm() ile hangi bloklara değmesi beklendiğini bildirir;
// beklenen bloklardan biriyle ilk temasta callback bir kez tetiklenir.
[RequireComponent(typeof(Rigidbody))]
public class ClawTouchSensor : MonoBehaviour
{
    private HashSet<GameObject> watchedBlocks;
    private System.Action onTouch;
    private bool armed;

    private void Awake()
    {
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    public void Arm(IEnumerable<GameObject> targets, System.Action callback)
    {
        watchedBlocks = new HashSet<GameObject>(targets);
        onTouch = callback;
        armed = true;
    }

    public void Disarm()
    {
        armed = false;
        watchedBlocks = null;
        onTouch = null;
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleContact(other.transform);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleContact(collision.transform);
    }

    private void HandleContact(Transform hit)
    {
        if (!armed || watchedBlocks == null) return;

        Transform cur = hit;
        while (cur != null)
        {
            if (watchedBlocks.Contains(cur.gameObject))
            {
                var callback = onTouch;
                Disarm();
                callback?.Invoke();
                return;
            }
            cur = cur.parent;
        }
    }
}
