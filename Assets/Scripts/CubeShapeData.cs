using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewCubeShapeData", menuName = "BlockMerge3D/Cube Shape Data")]
public class CubeShapeData : ScriptableObject
{
    public string shapeName;
    public Vector3Int gridSize;
    public float cellSize;
    public float spacing;
    public List<Vector3Int> occupiedCells = new List<Vector3Int>();
    public List<Vector3Int> prefilledCells = new List<Vector3Int>();
    public List<Color> prefilledColors = new List<Color>();
    public List<int> prefilledMaterialIndices = new List<int>();
    public GameObject cubePrefab;
}
