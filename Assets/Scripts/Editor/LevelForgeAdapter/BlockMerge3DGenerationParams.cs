// ═══════════════════════════════════════════════════════════════════
//  BLOCKMERGE3D LEVELFORGE ADAPTER — Üretim Parametreleri
//  Bu klasördeki dosyalar, LevelForge paketinin (Packages/com.fogboundgames.levelforge)
//  jenerik "üret → doğrula → puanla → hedefe ulaşana kadar dene" motorunu BlockMerge3D'nin
//  kendi voxel/ice/parça-kütüphanesi kurallarına bağlayan somut adapter'dır — bkz.
//  Packages/com.fogboundgames.levelforge/ADAPTER_GUIDE.md.
// ═══════════════════════════════════════════════════════════════════

// LevelForge.DifficultySearchEngine.Run<TParams,...>'un her denemede mutasyona uğrattığı
// TParams. Şeklin kendisi (occupiedCells/gridSize) bu parametrelerin DIŞINDA tutulur — mevcut
// akışta şekil bir kez (ValidateAndLoadSourceShape) yüklenir, arama döngüsü sadece engel
// oranlarını ve parça boyut aralığını değiştirir (AILevelDesignerWindow.DistributeObstacles /
// SplitShapeWithSolutionFirstLibrary'nin okuduğu enstantane alanlarla birebir eşleşir).
public struct BlockMerge3DGenerationParams
{
    public float icePercentage;
    public float prefillPercentage;
    public int minPieceSize;
    public int maxPieceSize;

    public override string ToString() =>
        $"buz=%{icePercentage * 100f:F0} hazır=%{prefillPercentage * 100f:F0} parça={minPieceSize}-{maxPieceSize}";
}
