using UnityEngine;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════
//  PIECE DEFINITION  —  Resmi Parça Kütüphanesi Kaydı
//  BlockMerge3D  •  CubeShapeDataHolder tabanlı prefab'lardan
//  PieceDefinitionMigrationWindow ile üretilir.
//
//  ÜRETİM ZAMANI varlığıdır, çalışma zamanı değil: AILevelDesignerWindow
//  Assets/PieceDefinitions/ altındaki tanımlardan bir havuz örnekler
//  (SampleEligiblePool), SolutionFirstBuilder bu havuzla şekli döşer ve
//  sonuç ÇÖZÜMDEKİ rotasyonda prefab'lara pişirilip
//  LevelData.complementaryPieces'a yazılır. LevelManager bu yüzden
//  PieceDefinition'ı hiç görmez.
// ═══════════════════════════════════════════════════════════════════

[CreateAssetMenu(fileName = "NewPieceDefinition", menuName = "BlockMerge3D/Piece Definition")]
public class PieceDefinition : ScriptableObject
{
    [Header("Kimlik")]
    public string id;
    public string displayName;

    [Header("Şekil (orijine ötelenmiş)")]
    public List<Vector3Int> cells = new List<Vector3Int>();
    public float cellSize = 1f;
    public float spacing = 0.1f;

    [Header("Türetilmiş Veri (RecomputeDerived ile hesaplanır)")]
    public int volume;
    public string canonicalSignature;
    public List<Vector3Int> allowedRotations = new List<Vector3Int>();

    [Header("Üretim Metadata'sı")]
    public List<string> difficultyTags = new List<string>();
    public float spawnWeight = 1f;
    public int maxCopiesPerLevel = -1; // -1 = sınırsız

    [Header("Migration İzi")]
    public GameObject representativePrefab;
    public List<string> sourceAssetPaths = new List<string>();

    /// <summary>
    /// volume/canonicalSignature/allowedRotations alanlarını cells'ten yeniden hesaplar.
    /// Migration aracı ve elle düzenleme sonrası "Yeniden Hesapla" butonu tarafından çağrılır.
    /// </summary>
    public void RecomputeDerived()
    {
        volume = cells != null ? cells.Count : 0;
        canonicalSignature = PieceGeometryUtils.ComputeCanonicalSignature(cells);
        allowedRotations = PieceGeometryUtils.ComputeDistinctRotations(cells);

        if (string.IsNullOrEmpty(id))
            id = GenerateId(canonicalSignature);
    }

    public static string GenerateId(string canonicalSignature)
    {
        return "PD_" + StableHash8(canonicalSignature ?? string.Empty);
    }

    // string.GetHashCode() değeri .NET sürümleri/platformlar arası kararlı olması garanti
    // edilmediği için (bkz. Microsoft docs), Assets üzerinde kalıcı olacak bir id için
    // basit ve kararlı bir FNV-1a hash'i kullanıyoruz.
    private static string StableHash8(string s)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in s)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("x8");
        }
    }
}
