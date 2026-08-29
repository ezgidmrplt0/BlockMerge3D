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

    [Header("Öğretici")]
    [Tooltip("Bu seviyede sırayla gösterilecek öğretici adımları. Boşsa öğretici çıkmaz. " +
             "Her adım, oyuncu ilgili eylemi yapınca kendiliğinden tamamlanır.")]
    public List<TutorialStepType> tutorialSteps = new List<TutorialStepType>();

    [Header("Tutorial / Determinism")]
    [Tooltip("Kapalıysa parçalar rastgele 90° Y rotasyonuyla değil, prefab'daki orijinal " +
             "yönleriyle dağıtılır. Öğretici seviyelerde şart: 'bu parça ancak döndürünce " +
             "sığar' senaryosu, parça rastgele zaten doğru yönde gelirse çöker.")]
    public bool randomizeSpawnRotation = true;

    [Header("Buz")]
    [Tooltip("Açıksa bu seviyede buz TEK nitelikli vuruşta erir — çoklu-vuruş sayacı ve " +
             "dinamik zorluk rolü devre dışı kalır (hitCount=1). Tutorial (ör. LEVEL 4) için.")]
    public bool forceSingleHitIce = false;

    [Header("Pre-computed Solution")]
    [Tooltip("Rebuild aracı tarafından yazılır. Her eleman complementaryPieces ile paralel: " +
             "parçanın grid üzerindeki hedef hücrelerini (dünya koordinatı) tutar.")]
    public List<PiecePlacement> precomputedSolution = new List<PiecePlacement>();

    [Header("Win Conditions / Tower Collapse & Cracking")]
    public float timeLimit   = 60f;   // saniye; 0 = süresiz
    public int   targetScore = 100;   // 0 = eski grid-tamamlama mantığı
    [Tooltip("Her bir katmanı çökertmek için gereken satır/sütun patlatma sayısı.")]
    public int   linesPerLayer = 3;
    [Tooltip("Her bir katmanı çatlatıp yok etmek için gereken puan eşiği (0 ise linesPerLayer kullanılır).")]
    public int   pointsPerLayer = 400;
    [Tooltip("Katmanın çökmeden önce kaç aşamada çatlayacağı (1 = anında, 2 = 2 aşamalı çatlak, 3 = 3 aşamalı çatlak).")]
    public int   crackStages = 2;
}

[System.Serializable]
public class PiecePlacement
{
    public List<Vector3Int> targetWorldCells = new List<Vector3Int>();
    public bool isSacrifice;
    [Tooltip("Bu parçanın ait olduğu hedef katman indeksi (-1 = otomatik/katman bağımsız).")]
    public int targetLayerIndex = -1;
}
