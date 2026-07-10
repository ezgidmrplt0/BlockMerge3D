using UnityEngine;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL TEMPLATE - Hazır Grid Şablonları
//  AI sadece bu şablonlar üzerinde çalışır, sıfırdan grid oluşturmaz
// ═══════════════════════════════════════════════════════════════════

[CreateAssetMenu(fileName = "NewLevelTemplate", menuName = "BlockMerge3D/Level Template")]
public class LevelTemplate : ScriptableObject
{
    [Header("Şablon Bilgisi")]
    public string templateName = "Template_1";
    
    [TextArea(2, 4)]
    public string description = "3x3x3 temel küp";
    
    [Header("Grid Yapısı")]
    public Vector3Int gridSize = new Vector3Int(3, 3, 3);
    
    [Header("Şekil Tanımı")]
    [Tooltip("Boş = Tam dolu küp. Doldurulursa sadece bu hücreler kullanılır.")]
    public List<Vector3Int> occupiedCells = new List<Vector3Int>();
    
    [Header("Zorluk Önerileri")]
    [Range(0f, 0.5f)]
    public float recommendedIceRatio = 0.1f;
    
    [Range(0f, 0.3f)]
    public float recommendedPrefilledRatio = 0.0f;
    
    [Range(3, 15)]
    public int recommendedMinPieceSize = 5;
    
    [Range(5, 20)]
    public int recommendedMaxPieceSize = 10;
    
    [Header("Oynanış")]
    public float recommendedTimeLimit = 60f;
    public int recommendedTargetScore = 150;
}
