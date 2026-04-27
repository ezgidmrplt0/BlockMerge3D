using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "LevelOrder", menuName = "BlockMerge3D/Level Order")]
public class LevelOrderData : ScriptableObject
{
    public List<LevelData> levels = new List<LevelData>();
}
