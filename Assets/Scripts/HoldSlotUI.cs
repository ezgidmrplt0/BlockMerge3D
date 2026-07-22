using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Parça Saklama Kutusu (Hold Slot) UI bileşeni.
/// Oyuncunun elindeki parçaları kenara koymasını (Hold) ve saklanan parçayla takas (Swap) yapabilmesini sağlar.
/// </summary>
public class HoldSlotUI : MonoBehaviour, IDropHandler, IPointerClickHandler
{
    public static HoldSlotUI Instance { get; private set; }

    [Header("UI Referansları")]
    public PieceCardUI holdCard;
    public TextMeshProUGUI holdLabel;

    private void Awake()
    {
        Instance = this;
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (DraggablePiece.activeDrag != null)
        {
            LevelManager.Instance?.TryStoreOrSwapHold(DraggablePiece.activeDrag);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (holdCard != null && holdCard.HasPiece && !DraggablePiece.IsDragging)
        {
            // İsteğe bağlı tık etkileşimi
        }
    }
}
