using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewLevel", menuName = "BlockMerge3D/Level Data")]
public class LevelData : ScriptableObject
{
    public string levelName;
    
    [Header("Main Target Shape")]
    public GameObject mainShapePrefab;
    
    [Header("Complementary Pieces")]
    public List<GameObject> complementaryPieces = new List<GameObject>();
    
    [Header("Level Settings")]
    public int levelIndex;
}
