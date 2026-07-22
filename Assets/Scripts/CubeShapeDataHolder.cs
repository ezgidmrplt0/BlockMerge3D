using UnityEngine;
using System.Collections.Generic;

public class CubeShapeDataHolder : MonoBehaviour
{
    [Header("Shape Information")]
    public string shapeName;
    public Vector3Int gridSize;
    public float cellSize;
    public float spacing;

    [Header("Occupied Cells")]
    public List<Vector3Int> occupiedCells = new List<Vector3Int>();

    [Header("Prefilled Cells (Obstacles)")]
    public List<Vector3Int> prefilledCells = new List<Vector3Int>();
    public List<Color> prefilledColors = new List<Color>();
    public List<int> prefilledMaterialIndices = new List<int>();

    [Header("Frozen Cells (Ice)")]
    public List<Vector3Int> frozenCells = new List<Vector3Int>();

    [Header("Layer Origin (complementary piece'ler için)")]
    [Tooltip("Bu parçanın hangi katman (dünya Y'si) için çözüldüğü — AILevelDesignerWindow." +
             "ExportProceduralLevelCore tarafından bake zamanında yazılır. -1 = etiketlenmemiş " +
             "(migration bekleyen eski seviye parçası ya da ana şekil holder'ı — ana şekilde " +
             "kullanılmaz, sadece complementaryPieces için anlamlıdır).")]
    public int originLayerY = -1;

    /// <summary>Hücre merkezleri arası mesafe. Veri eksikse 1 (eski davranış).</summary>
    public float Step
    {
        get
        {
            float s = cellSize + spacing;
            return s > 0.0001f ? s : 1f;
        }
    }

    /// <summary>Dünya pozisyonunu bu şeklin kendi grid hücresine çevirir. Şeklin
    /// çocuk Renderer'larını mantıksal hücrelerle eşleştirmenin TEK yolu budur —
    /// obje ismine ("Cube_x_y_z") bakılmaz.</summary>
    public Vector3Int WorldToCell(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        float step = Step;
        return new Vector3Int(
            Mathf.RoundToInt(local.x / step),
            Mathf.RoundToInt(local.y / step),
            Mathf.RoundToInt(local.z / step));
    }

    public bool IsCellOccupied(Vector3Int gridPos)
    {
        return occupiedCells.Contains(gridPos);
    }

    public bool IsCellFrozen(Vector3Int gridPos)
    {
        return frozenCells != null && frozenCells.Contains(gridPos);
    }

    public bool IsCellPrefilled(Vector3Int gridPos)
    {
        return PrefilledLookup.ContainsKey(gridPos);
    }

    /// <summary>Prefilled hücrenin tür indeksi (pieceMaterials/pieceSpeciesPrefabs ile aynı
    /// indeksleme) ve rengi. Hücre prefilled değilse false döner.</summary>
    public bool TryGetPrefilledInfo(Vector3Int gridPos, out int matIndex, out Color color)
    {
        matIndex = -1;
        color    = Color.white;
        if (!PrefilledLookup.TryGetValue(gridPos, out int i)) return false;

        if (prefilledMaterialIndices != null && i < prefilledMaterialIndices.Count)
            matIndex = prefilledMaterialIndices[i];
        if (prefilledColors != null && i < prefilledColors.Count)
            color = prefilledColors[i];
        return true;
    }

    // prefilledCells doğrusal aranırsa şeklin her hücresi için O(n) tarama gerekir
    // (yükleme O(n²)); hücre -> liste indeksi eşlemesini bir kez kurup önbelleğe alıyoruz.
    private Dictionary<Vector3Int, int> prefilledLookup;
    private int prefilledLookupCount = -1;

    private Dictionary<Vector3Int, int> PrefilledLookup
    {
        get
        {
            int count = prefilledCells != null ? prefilledCells.Count : 0;
            // Editör araçları listeyi çalışırken değiştirebiliyor; eleman sayısı
            // değiştiyse önbelleği tazeliyoruz.
            if (prefilledLookup == null || prefilledLookupCount != count)
                RebuildPrefilledLookup();
            return prefilledLookup;
        }
    }

    /// <summary>prefilledCells elle düzenlendiğinde (eleman sayısı değişmeden) çağrılmalı.</summary>
    public void RebuildPrefilledLookup()
    {
        prefilledLookup = new Dictionary<Vector3Int, int>();
        if (prefilledCells != null)
        {
            for (int i = 0; i < prefilledCells.Count; i++)
                prefilledLookup[prefilledCells[i]] = i;
        }
        prefilledLookupCount = prefilledCells != null ? prefilledCells.Count : 0;
    }
}
