using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

[RequireComponent(typeof(Button))]
public class ButtonAnimator : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() =>
        {
            transform.DOKill();
            transform.DOPunchScale(Vector3.one * 0.13f, 0.28f, 6, 0.5f);
        });
    }
}
