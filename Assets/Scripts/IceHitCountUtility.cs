using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  Buzun erimesi için gereken vuruş sayısını zorluğa göre üretir.
//  Hem runtime (GridManager / LevelManager) hem de Editor araçları
//  (LevelBuilderWindow, AILevelDesignerWindow, CubeShapeDataHolderEditor)
//  tarafından ortak kullanılır.
// ═══════════════════════════════════════════════════════════════════
public static class IceHitCountUtility
{
    public enum IceDifficulty { Kolay = 0, Orta = 1, Zor = 2, Uzman = 3 }

    public static readonly (int min, int max)[] RangeByDifficulty =
    {
        (1, 1), // Kolay  — 1 vuruş
        (1, 1), // Orta   — 1 vuruş
        (1, 1), // Zor    — 1 vuruş
        (1, 1), // Uzman  — 1 vuruş
    };

    public static (int min, int max) GetRange(IceDifficulty difficulty)
    {
        int idx = (int)difficulty;
        if (idx < 0 || idx >= RangeByDifficulty.Length) idx = 0;
        return RangeByDifficulty[idx];
    }

    public static int RollHitCount(IceDifficulty difficulty)
    {
        var r = GetRange(difficulty);
        return Random.Range(r.min, r.max + 1); // Random.Range max inclusive için +1
    }

    /// <summary>
    /// Seviye numarasına göre zorluk derecesini belirler.
    /// 1-2: Kolay, 3-5: Orta, 6-10: Zor, 11+: Uzman
    /// </summary>
    public static IceDifficulty GetDifficultyForLevel(int levelIndex)
    {
        int levelNum = Mathf.Max(1, levelIndex);
        if (levelNum <= 2) return IceDifficulty.Kolay;
        if (levelNum <= 5) return IceDifficulty.Orta;
        if (levelNum <= 10) return IceDifficulty.Zor;
        return IceDifficulty.Uzman;
    }

    /// <summary>
    /// Seviye numarasına göre rastgele vuruş sayısı üretir.
    /// </summary>
    public static int RollHitCountForLevel(int levelIndex)
    {
        IceDifficulty difficulty = GetDifficultyForLevel(levelIndex);
        return RollHitCount(difficulty);
    }
}
