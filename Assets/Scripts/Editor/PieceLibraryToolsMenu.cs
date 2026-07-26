using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  PARÇA KÜTÜPHANESİ TEMİZLE & GENİŞLET
//  Assets/PieceDefinitions/ altındaki kütüphaneyi:
//   1) BOZUK parçaları (ortogonal bağlı olmayan — köşegen/kopuk) siler,
//   2) KOPYALARI (aynı kanonik şekil) tekilleştirir,
//   3) Eksik orta-boy (4-6 hücre) ÇEŞİTLERİ ekler (varsa atlar).
//  Amaç: tiling'de "hep aynı parça / tek-küp dolgu" tekrarını kütüphane
//  tarafından da azaltmak. Rotasyonlar RecomputeDerived ile düzgün hesaplanır.
// ═══════════════════════════════════════════════════════════════════
public static class PieceLibraryToolsMenu
{
    private const string DIR = "Assets/PieceDefinitions";

    private static Vector3Int V(int x, int z) => new Vector3Int(x, 0, z);

    [MenuItem("BlockMerge3D/🧹 Parça Kütüphanesini Temizle ve Genişlet")]
    private static void CleanAndExpand()
    {
        if (!AssetDatabase.IsValidFolder(DIR))
        {
            EditorUtility.DisplayDialog("Hata", $"{DIR} bulunamadı.", "Tamam");
            return;
        }
        if (!EditorUtility.DisplayDialog("Kütüphaneyi Temizle ve Genişlet",
            "• Ortogonal bağlı OLMAYAN (köşegen/kopuk) parçalar SİLİNİR\n" +
            "• Aynı şeklin KOPYALARI silinir (biri kalır)\n" +
            "• Eksik orta-boy (4-6 hücre) çeşitler EKLENİR\n\n" +
            "Devam? (git'te commit'liysen geri alınabilir)", "Devam", "Vazgeç"))
            return;

        var sb = new System.Text.StringBuilder();
        int deletedBroken = 0, deletedDup = 0, added = 0;

        // ── Yükle ──
        var defs = AssetDatabase.FindAssets("t:PieceDefinition", new[] { DIR })
            .Select(g => AssetDatabase.GUIDToAssetPath(g))
            .Select(p => (path: p, def: AssetDatabase.LoadAssetAtPath<PieceDefinition>(p)))
            .Where(t => t.def != null && t.def.cells != null && t.def.cells.Count > 0)
            .ToList();

        // ── 1) Bozuk (bağlı olmayan) parçaları sil ──
        foreach (var (path, def) in defs.ToList())
        {
            if (!IsOrthogonallyConnected(def.cells))
            {
                sb.AppendLine($"🗑 bozuk (kopuk): {System.IO.Path.GetFileNameWithoutExtension(path)}");
                AssetDatabase.DeleteAsset(path);
                defs.RemoveAll(t => t.path == path);
                deletedBroken++;
            }
        }

        // ── 2) Kopyaları (aynı kanonik imza) tekilleştir ──
        var seen = new HashSet<string>();
        foreach (var (path, def) in defs.ToList())
        {
            string sig = PieceGeometryUtils.ComputeCanonicalSignature(def.cells);
            if (!seen.Add(sig))
            {
                sb.AppendLine($"🗑 kopya: {System.IO.Path.GetFileNameWithoutExtension(path)}");
                AssetDatabase.DeleteAsset(path);
                defs.RemoveAll(t => t.path == path);
                deletedDup++;
            }
        }

        // ── 3) Eksik orta-boy çeşitleri ekle ──
        var existing = new HashSet<string>(
            defs.Select(t => PieceGeometryUtils.ComputeCanonicalSignature(t.def.cells)));

        foreach (var (nice, cells) in NewShapes())
        {
            string sig = PieceGeometryUtils.ComputeCanonicalSignature(cells);
            if (existing.Contains(sig)) { sb.AppendLine($"· zaten var: {nice}"); continue; }
            existing.Add(sig);

            var pd = ScriptableObject.CreateInstance<PieceDefinition>();
            pd.cells = PieceGeometryUtils.NormalizeCells(cells);
            pd.displayName = nice;
            pd.rotationMode = PieceRotationMode.FlatYRotations; // düzlemde 4 yön
            pd.RecomputeDerived();

            string path = AssetDatabase.GenerateUniqueAssetPath($"{DIR}/{nice}_{pd.id}.asset");
            AssetDatabase.CreateAsset(pd, path);
            sb.AppendLine($"➕ eklendi: {nice} ({pd.volume} hücre, {pd.allowedRotations.Count} yön)");
            added++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string report = $"Temizlik: {deletedBroken} bozuk + {deletedDup} kopya silindi, {added} yeni çeşit eklendi.\n\n{sb}";
        Debug.Log("🧩 " + report);
        EditorUtility.DisplayDialog("Kütüphane Güncellendi", report, "Tamam");
    }

    // Eklenecek orta-boy çeşitler (x,z düzleminde). Zaten varsa (kanonik) atlanır.
    private static IEnumerable<(string, List<Vector3Int>)> NewShapes()
    {
        yield return ("Kare2x2",       new List<Vector3Int> { V(0,0), V(1,0), V(0,1), V(1,1) });                 // O
        yield return ("Cizgi4",        new List<Vector3Int> { V(0,0), V(1,0), V(2,0), V(3,0) });                  // I4
        yield return ("S_tetromino",   new List<Vector3Int> { V(1,0), V(2,0), V(0,1), V(1,1) });                  // S
        yield return ("Z_tetromino",   new List<Vector3Int> { V(0,0), V(1,0), V(1,1), V(2,1) });                  // Z
        yield return ("Arti5",         new List<Vector3Int> { V(1,0), V(0,1), V(1,1), V(2,1), V(1,2) });          // +
        yield return ("Dikdortgen2x3", new List<Vector3Int> { V(0,0), V(1,0), V(2,0), V(0,1), V(1,1), V(2,1) });  // 2x3
        yield return ("P_pentomino",   new List<Vector3Int> { V(0,0), V(1,0), V(0,1), V(1,1), V(0,2) });          // P
        yield return ("Cizgi5",        new List<Vector3Int> { V(0,0), V(1,0), V(2,0), V(3,0), V(4,0) });          // I5
        yield return ("L5_pentomino",  new List<Vector3Int> { V(0,0), V(0,1), V(0,2), V(0,3), V(1,0) });          // L5
        yield return ("W_pentomino",   new List<Vector3Int> { V(0,0), V(0,1), V(1,1), V(1,2), V(2,2) });          // W
    }

    private static bool IsOrthogonallyConnected(List<Vector3Int> cells)
    {
        if (cells.Count <= 1) return true;
        var set = new HashSet<Vector3Int>(cells);
        var seen = new HashSet<Vector3Int> { cells[0] };
        var stack = new Stack<Vector3Int>();
        stack.Push(cells[0]);
        Vector3Int[] dirs =
        {
            new Vector3Int(1,0,0), new Vector3Int(-1,0,0),
            new Vector3Int(0,0,1), new Vector3Int(0,0,-1),
            new Vector3Int(0,1,0), new Vector3Int(0,-1,0)
        };
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            foreach (var d in dirs)
            {
                var n = c + d;
                if (set.Contains(n) && seen.Add(n)) stack.Push(n);
            }
        }
        return seen.Count == cells.Count;
    }
}
