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

    // Faz 1 (Piece Library) — yazarlık/kütüphane katmanı. Şu an LevelManager tarafından
    // çalışma zamanında OKUNMUYOR (spawn akışı hâlâ complementaryPieces üzerinden çalışıyor);
    // runtime entegrasyonu Faz 3 (Solution-First üretim) kapsamına bırakıldı.
    public List<PieceDefinition> pieceDefinitions = new List<PieceDefinition>();
    
    [Header("Win Conditions")]
    public float timeLimit   = 60f;   // saniye; 0 = süresiz
    public int   targetScore = 100;   // 0 = eski grid-tamamlama mantığı
}
