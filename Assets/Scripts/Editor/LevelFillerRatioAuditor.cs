using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  DOLGU (1x1) & H-PARÇA DENETİM PENCERESİ
//  Assets/Levels altındaki tüm LevelData'ları tarar, her katman (originLayerY) için
//  tekli (1x1) dolgu ve H-şekilli parça oranını hesaplar. Level 17 layer 1'de
//  görülen "aşırı tekli/H parça geliyor" şikayetinin kaynağı — level tasarım zamanında
//  AI generator'ın exact-tiling için garanti ettiği dolgu parçaları (bkz. AILevelDesignerWindow
//  SampleEligiblePool) — bu pencere hangi level/katmanların sorunlu olduğunu listeler.
// ═══════════════════════════════════════════════════════════════════
public class LevelFillerRatioAuditor : EditorWindow
{
    private const string LEVELS_PATH = "Assets/Levels";

    // H parçası: iki adet 3 hücrelik dikey çubuk + ortada köprü (TutorialOverlay.cs'deki
    // Level 3 öğretici parçasıyla birebir aynı 7 hücrelik desen).
    private static readonly List<Vector3Int> CanonicalH = new List<Vector3Int>
    {
        new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, 2),
        new Vector3Int(1, 0, 1),
        new Vector3Int(2, 0, 0), new Vector3Int(2, 0, 1), new Vector3Int(2, 0, 2),
    };
    private static readonly HashSet<Vector3Int> CanonicalHSet = new HashSet<Vector3Int>(CanonicalH);

    private static readonly Quaternion[] Rotations =
    {
        Quaternion.identity,
        Quaternion.Euler(0, 90, 0),
        Quaternion.Euler(0, 180, 0),
        Quaternion.Euler(0, 270, 0),
    };

    private class LayerRow
    {
        public string levelName;
        public int layerY;
        public int total;
        public int singleCount;
        public int hCount;
        public float SingleRatio => total > 0 ? (float)singleCount / total : 0f;
        public float HRatio => total > 0 ? (float)hCount / total : 0f;
    }

    private List<LayerRow> rows = new List<LayerRow>();
    private Vector2 scroll;
    private bool scanned;

    [MenuItem("BlockMerge3D/🔍 Dolgu & H-Parça Denetimi", false, 31)]
    public static void Open()
    {
        var w = GetWindow<LevelFillerRatioAuditor>("Dolgu & H-Parça Denetimi");
        w.minSize = new Vector2(560, 360);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("🔍 Level Katmanlarında Tekli (1x1) & H-Parça Oranı", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Assets/Levels altındaki tüm LevelData'ların complementaryPieces listesini originLayerY'ye " +
            "göre gruplar; her katmanda kaç parçanın 1x1 (tekli dolgu) veya H-şekli olduğunu hesaplar. " +
            "%30'un üzerindeki katmanlar kırmızı vurgulanır — bunlar oyunda 'sürekli tekli/H parça geliyor' " +
            "şikayetine yol açan katmanlardır.",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Taramayı Başlat", GUILayout.Height(28)))
        {
            Scan();
        }
        GUI.enabled = scanned;
        if (GUILayout.Button("Sonucu Kopyala", GUILayout.Height(28), GUILayout.Width(140)))
        {
            EditorGUIUtility.systemCopyBuffer = BuildReportText();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (!scanned) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"Toplam {rows.Count} katman tarandı. En sorunlu olanlar üstte.", EditorStyles.miniLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("Level", GUILayout.Width(180));
        GUILayout.Label("Katman", GUILayout.Width(50));
        GUILayout.Label("Toplam", GUILayout.Width(50));
        GUILayout.Label("1x1", GUILayout.Width(70));
        GUILayout.Label("H", GUILayout.Width(70));
        EditorGUILayout.EndHorizontal();

        var sorted = rows.OrderByDescending(r => r.SingleRatio + r.HRatio).ToList();
        foreach (var r in sorted)
        {
            bool warn = r.SingleRatio >= 0.3f || r.HRatio >= 0.3f;
            var prevColor = GUI.color;
            if (warn) GUI.color = new Color(1f, 0.55f, 0.5f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(r.levelName, GUILayout.Width(180));
            GUILayout.Label(r.layerY.ToString(), GUILayout.Width(50));
            GUILayout.Label(r.total.ToString(), GUILayout.Width(50));
            GUILayout.Label($"{r.singleCount} (%{r.SingleRatio * 100f:0})", GUILayout.Width(70));
            GUILayout.Label($"{r.hCount} (%{r.HRatio * 100f:0})", GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            GUI.color = prevColor;
        }

        EditorGUILayout.EndScrollView();
    }

    private void Scan()
    {
        rows.Clear();

        var guids = AssetDatabase.FindAssets("t:LevelData", new[] { LEVELS_PATH });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (level == null) continue;

            var byLayer = new Dictionary<int, LayerRow>();

            foreach (var piece in level.complementaryPieces)
            {
                if (piece == null) continue;
                var holder = piece.GetComponent<CubeShapeDataHolder>();
                if (holder == null || holder.occupiedCells == null || holder.occupiedCells.Count == 0) continue;

                int layerY = holder.originLayerY;
                if (!byLayer.TryGetValue(layerY, out var row))
                {
                    row = new LayerRow { levelName = level.levelName, layerY = layerY };
                    byLayer[layerY] = row;
                }

                row.total++;
                if (holder.occupiedCells.Count == 1) row.singleCount++;
                else if (IsHShape(holder.occupiedCells)) row.hCount++;
            }

            rows.AddRange(byLayer.Values);
        }

        scanned = true;
    }

    private string BuildReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Level\tKatman\tToplam\t1x1\t1x1%\tH\tH%");

        var sorted = rows.OrderByDescending(r => r.SingleRatio + r.HRatio);
        foreach (var r in sorted)
        {
            sb.AppendLine($"{r.levelName}\t{r.layerY}\t{r.total}\t{r.singleCount}\t{r.SingleRatio * 100f:0}\t{r.hCount}\t{r.HRatio * 100f:0}");
        }

        return sb.ToString();
    }

    private static bool IsHShape(List<Vector3Int> cells)
    {
        if (cells.Count != CanonicalH.Count) return false;

        foreach (var rot in Rotations)
        {
            var rotated = GridManager.RotateCells(cells, rot);
            if (new HashSet<Vector3Int>(rotated).SetEquals(CanonicalHSet))
                return true;
        }
        return false;
    }
}
