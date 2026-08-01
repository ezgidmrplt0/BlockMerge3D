using System.Linq;
using UnityEditor;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL YENİDEN PİŞİRME PENCERESİ
//  Bir levelin ŞEKLİNİ (occupied/buz/prefill) koruyup parçalarını GÜNCEL kütüphaneyle
//  yeniden üretir ve MEVCUT LevelData'ya yazar (GUID korunur → LevelOrder bozulmaz).
//  Parça küpleri, seçilen "Parça Obje Prefabı"ndan üretilir — boşsa varsayılan grid küpü
//  ("parçalar hep küp oldu" sorunu için buraya asıl obje/model prefabını ata).
//  Asıl iş AILevelDesignerWindow.RebakeLevelsPreservingShape'te.
// ═══════════════════════════════════════════════════════════════════
public class LevelRebakeWindow : EditorWindow
{
    private const string LEVELS_PATH = "Assets/Levels";

    private LevelData  targetLevel;
    private GameObject pieceObjectPrefab;
    private bool       canonicalizePieces = true;
    private Vector2    scroll;

    [MenuItem("BlockMerge3D/🔁 Level Yeniden Pişir (şekli koru)", false, 30)]
    public static void Open()
    {
        var w = GetWindow<LevelRebakeWindow>("Level Yeniden Pişir");
        w.minSize = new Vector2(380, 260);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("🔁 Şekli Koruyarak Parçaları Yeniden Pişir", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Seçilen levelin ŞEKLİ (occupied / buz / prefill) korunur; parçaları güncel kütüphaneyle " +
            "yeniden üretilir ve MEVCUT LevelData'nın içine yazılır (GUID korunur, LevelOrder bozulmaz). " +
            "Döşenemeyen/geçersiz levele DOKUNULMAZ.\n\n" +
            "Parça küpleri aşağıdaki 'Parça Obje Prefabı'ndan üretilir. Boş bırakırsan varsayılan grid " +
            "küpü kullanılır (bu yüzden parçalar düz küp görünür). Asıl obje/model prefabını buraya ata.",
            MessageType.Info);

        EditorGUILayout.Space(8);
        targetLevel       = (LevelData)EditorGUILayout.ObjectField("Level (LevelData)", targetLevel, typeof(LevelData), false);
        pieceObjectPrefab = (GameObject)EditorGUILayout.ObjectField("Parça Obje Prefabı", pieceObjectPrefab, typeof(GameObject), false);

        if (pieceObjectPrefab == null)
            EditorGUILayout.HelpBox("Parça Obje Prefabı boş — parçalar varsayılan grid küpü olarak üretilir.", MessageType.Warning);

        EditorGUILayout.Space(4);
        canonicalizePieces = EditorGUILayout.ToggleLeft(
            "Kanonik yön + aynı şekli birleştir (dönmüş kopyaları önle)", canonicalizePieces);
        EditorGUILayout.LabelField(
            "   Parçalar tek yönde saklanır; aynı şekil+katman tek prefab paylaşır. Oyun rotasyonu " +
            "zaten kendisi hallettiği için gameplay değişmez.", EditorStyles.miniLabel);

        EditorGUILayout.Space(10);

        using (new EditorGUI.DisabledScope(targetLevel == null))
        {
            if (GUILayout.Button("Bu Leveli Yeniden Pişir", GUILayout.Height(34)))
                Rebake(new[] { targetLevel });
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Toplu", EditorStyles.miniBoldLabel);
        if (GUILayout.Button("TÜM Levelleri Yeniden Pişir (bu prefabla)", GUILayout.Height(26)))
        {
            var all = AssetDatabase.FindAssets("t:LevelData", new[] { LEVELS_PATH })
                .Select(g => AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(l => l != null).ToArray();

            if (all.Length == 0)
            {
                EditorUtility.DisplayDialog("Yeniden Pişir", "Assets/Levels altında LevelData yok.", "Tamam");
            }
            else if (EditorUtility.DisplayDialog("Tüm Levelleri Yeniden Pişir",
                $"TÜM {all.Length} levelin şekli korunup parçaları yeniden üretilecek.\n\n" +
                "Döşenemeyene DOKUNULMAZ. Devam?", "Yeniden Pişir", "Vazgeç"))
            {
                Rebake(all);
            }
        }

        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.35f, 0.78f, 1.00f);
        if (GUILayout.Button("❄️ Buz Limitlerini Uygula (Max 1-2, Zor Max 3) & TÜM Levelleri Pişir", GUILayout.Height(32)))
        {
            if (EditorUtility.DisplayDialog("Buz Limitini Düzenle",
                "Tüm levellerdeki buz sayıları düzenlenecek:\n" +
                "- Normal leveller: En fazla 1-2 buz\n" +
                "- Zor / Expert leveller: En fazla 3 buz\n\n" +
                "Sonrasında tüm leveller yeni buz sayılarıyla yeniden pişirilecek. Devam?", "Evet, Düzenle & Pişir", "İptal"))
            {
                TrimIceLimitsAndRebakeAll();
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndScrollView();
    }

    private void TrimIceLimitsAndRebakeAll()
    {
        var allLevels = AssetDatabase.FindAssets("t:LevelData", new[] { LEVELS_PATH })
            .Select(g => AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(l => l != null).ToArray();

        int trimmedCount = 0;
        foreach (var ld in allLevels)
        {
            if (ld == null || ld.mainShapePrefab == null) continue;
            var holder = ld.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
            if (holder == null || holder.frozenCells == null || holder.frozenCells.Count == 0) continue;

            string nameUpper = (ld.levelName ?? "").ToUpper();
            string pathUpper = AssetDatabase.GetAssetPath(ld.mainShapePrefab).ToUpper();
            bool isHard = nameUpper.Contains("ZOR") || nameUpper.Contains("HARD") || nameUpper.Contains("EXPERT") ||
                         pathUpper.Contains("ZOR") || pathUpper.Contains("HARD") || pathUpper.Contains("EXPERT");

            int maxIce = isHard ? 3 : 2;

            if (holder.frozenCells.Count > maxIce)
            {
                holder.frozenCells = holder.frozenCells.Take(maxIce).ToList();
                if (holder.frozenHitCounts != null && holder.frozenHitCounts.Count > maxIce)
                {
                    holder.frozenHitCounts = holder.frozenHitCounts.Take(maxIce).ToList();
                }
                EditorUtility.SetDirty(holder.gameObject);
                PrefabUtility.SavePrefabAsset(ld.mainShapePrefab);
                trimmedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"❄️ {trimmedCount} seviyenin buz sayısı sınırlandı (Normal: max 2, Zor: max 3). Şimdi parçalar yeniden pişiriliyor...");
        Rebake(allLevels);
    }

    private void Rebake(LevelData[] levels)
    {
        // AILevelDesignerWindow instance'ı lazım (pişirme mantığı + OnEnable'da yüklenen
        // cubePrefab/prefilledMaterials orada). Açıksa yeniden kullan, değilse aç.
        var ai = Resources.FindObjectsOfTypeAll<AILevelDesignerWindow>().FirstOrDefault();
        if (ai == null) ai = GetWindow<AILevelDesignerWindow>();

        ai.RebakeLevelsPreservingShape(levels, pieceObjectPrefab, canonicalizePieces);
        Focus();
    }
}
