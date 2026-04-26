using UnityEngine;
using System.Collections.Generic;

public class CubeShapeDataHolder : MonoBehaviour
{
    [Header("Shape Information")]
    public string shapeName;
    public Vector3Int gridSize;
    public float cellSize;
    
    [Header("Occupied Cells")]
    public List<Vector3Int> occupiedCells = new List<Vector3Int>();

    /// <summary>
    /// Checks if a specific grid coordinate is occupied.
    /// </summary>
    public bool IsCellOccupied(Vector3Int gridPos)
    {
        return occupiedCells.Contains(gridPos);
    }
}
