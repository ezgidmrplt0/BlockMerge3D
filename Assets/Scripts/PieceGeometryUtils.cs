using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  PIECE GEOMETRY UTILS  —  Rotasyon/Öteleme Bağımsız Şekil İmzası
//  BlockMerge3D  •  PieceDefinition ve migration aracı tarafından kullanılır
// ═══════════════════════════════════════════════════════════════════

public static class PieceGeometryUtils
{
    /// <summary>Hücreleri minimum eksen değerleri 0 olacak şekilde öteler.</summary>
    public static List<Vector3Int> NormalizeCells(List<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return new List<Vector3Int>();

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        foreach (var c in cells)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.z < minZ) minZ = c.z;
        }

        var result = new List<Vector3Int>(cells.Count);
        foreach (var c in cells)
            result.Add(new Vector3Int(c.x - minX, c.y - minY, c.z - minZ));
        return result;
    }

    /// <summary>
    /// Küpün tam kiral rotasyon grubu (24 eleman): "yukarı" eksenini 6 yüzden birine
    /// çeviren 6 taban rotasyonu, her biri için Y ekseninde 4 spin (6×4 = 24).
    /// </summary>
    public static List<Quaternion> Get24Rotations()
    {
        var baseRotations = new Quaternion[]
        {
            Quaternion.identity,
            Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(180f, 0f, 0f),
            Quaternion.Euler(270f, 0f, 0f),
            Quaternion.Euler(0f, 0f, 90f),
            Quaternion.Euler(0f, 0f, 270f),
        };

        var result = new List<Quaternion>(24);
        foreach (var b in baseRotations)
            for (int y = 0; y < 4; y++)
                result.Add(Quaternion.Euler(0f, y * 90f, 0f) * b);

        return result;
    }

    /// <summary>Hücreleri verilen rotasyonla döndürür ve orijine öteler.</summary>
    public static List<Vector3Int> RotateAndNormalize(List<Vector3Int> cells, Quaternion rotation)
    {
        var rotated = new List<Vector3Int>(cells.Count);
        foreach (var c in cells)
        {
            Vector3 v = rotation * new Vector3(c.x, c.y, c.z);
            rotated.Add(new Vector3Int(
                Mathf.RoundToInt(v.x),
                Mathf.RoundToInt(v.y),
                Mathf.RoundToInt(v.z)));
        }
        return NormalizeCells(rotated);
    }

    private static string CellsToKey(List<Vector3Int> cells)
    {
        var sorted = new List<Vector3Int>(cells);
        sorted.Sort((a, b) =>
        {
            if (a.x != b.x) return a.x.CompareTo(b.x);
            if (a.y != b.y) return a.y.CompareTo(b.y);
            return a.z.CompareTo(b.z);
        });

        var parts = new string[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
            parts[i] = $"{sorted[i].x},{sorted[i].y},{sorted[i].z}";
        return string.Join("|", parts);
    }

    /// <summary>
    /// Öteleme VE rotasyon bağımsız kanonik şekil imzası. Aynı fiziksel şeklin (hangi
    /// açıyla/konumla kaydedilmiş olursa olsun) her zaman aynı imzayı üretmesi garantidir.
    /// Kasıtlı olarak yansıma (mirror) bağımsız DEĞİLDİR — S/Z gibi ayna simetrik parçalar
    /// farklı imza almaya devam eder.
    /// </summary>
    public static string ComputeCanonicalSignature(List<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return string.Empty;

        string best = null;
        foreach (var rot in Get24Rotations())
        {
            string key = CellsToKey(RotateAndNormalize(cells, rot));
            if (best == null || string.CompareOrdinal(key, best) < 0)
                best = key;
        }
        return best;
    }

    /// <summary>
    /// 24 rotasyondan, bu parça için geometrik olarak birbirinden FARKLI hücre dizilimi
    /// üretenlerin Euler açılarını döndürür. Simetrik parçalarda (örn. 2x2 kare) tek bir
    /// sonuç döner; simetrisiz parçalarda (örn. L şekli) daha fazla sonuç döner.
    /// </summary>
    public static List<Vector3Int> ComputeDistinctRotations(List<Vector3Int> cells)
    {
        var seenKeys = new HashSet<string>();
        var distinct = new List<Vector3Int>();

        if (cells == null || cells.Count == 0) return distinct;

        foreach (var rot in Get24Rotations())
        {
            var normalized = RotateAndNormalize(cells, rot);
            string key = CellsToKey(normalized);
            if (seenKeys.Add(key))
            {
                Vector3 euler = rot.eulerAngles;
                distinct.Add(new Vector3Int(
                    Mathf.RoundToInt(euler.x),
                    Mathf.RoundToInt(euler.y),
                    Mathf.RoundToInt(euler.z)));
            }
        }

        return distinct;
    }
}
