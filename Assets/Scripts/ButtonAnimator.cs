using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

/// <summary>
/// Arayüz butonlarına premium, pürüzsüz ve tepkisel animasyonlar kazandırır.
/// Üzerine gelince (hover) hafif büyüme, basınca (press) küçülme ve tıklayınca yaylanma animasyonu içerir.
/// </summary>
[RequireComponent(typeof(Button))]
public class ButtonAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private Vector3 originalScale;
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        originalScale = transform.localScale;

        button.onClick.AddListener(() =>
        {
            transform.DOKill();
            transform.localScale = originalScale;
            // Tıklama anında premium yaylanma efekti
            transform.DOPunchScale(Vector3.one * 0.12f, 0.32f, 8, 0.4f);
        });
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        transform.DOKill();
        // Hover: Pürüzsüzce %5 büyüt
        transform.DOScale(originalScale * 1.05f, 0.22f).SetEase(Ease.OutCubic);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        transform.DOKill();
        // Orijinal boyuta geri süzül
        transform.DOScale(originalScale, 0.20f).SetEase(Ease.OutCubic);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        transform.DOKill();
        // Press: Basıldığını hissettirmek için %6 küçült
        transform.DOScale(originalScale * 0.94f, 0.12f).SetEase(Ease.OutCubic);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;
        transform.DOKill();
        // Bırakılınca hover boyutuna geri gel
        transform.DOScale(originalScale * 1.05f, 0.15f).SetEase(Ease.OutCubic);
    }

    private void OnDisable()
    {
        transform.DOKill();
        transform.localScale = originalScale;
    }
}
