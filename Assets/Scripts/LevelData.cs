using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewLevel", menuName = "BlockMerge3D/Level Data")]
public class LevelData : ScriptableObject
{
    public string levelName;
    
    [Header("Main Target Shape")]
    public GameObject mainShapePrefab;
    
    // Seviyenin oynanabilir parçaları. AILevelDesignerWindow, Assets/PieceDefinitions/
    // kütüphanesinden seçtiği parçalarla çözümü kurar (bkz. SolutionFirstBuilder) ve
    // sonucu ÇÖZÜMDEKİ rotasyonda prefab'lara pişirip buraya yazar — bu yüzden çalışma
    // zamanında PieceDefinition'a ihtiyaç yoktur.
    [Header("Complementary Pieces")]
    public List<GameObject> complementaryPieces = new List<GameObject>();

    [Header("Win Conditions")]
    public float timeLimit   = 60f;   // saniye; 0 = süresiz
    public int   targetScore = 100;   // 0 = eski grid-tamamlama mantığı
}
