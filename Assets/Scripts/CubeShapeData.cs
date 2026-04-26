using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewCubeShapeData", menuName = "BlockMerge3D/Cube Shape Data")]
public class CubeShapeData : ScriptableObject
{
    public string shapeName;
    public Vector3Int gridSize;
    public float cellSize;
    public List<Vector3Int> occupiedCells = new List<Vector3Int>();
    public GameObject cubePrefab;
}
