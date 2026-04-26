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

    public bool IsCellOccupied(Vector3Int gridPos)
    {
        return occupiedCells.Contains(gridPos);
    }
}
