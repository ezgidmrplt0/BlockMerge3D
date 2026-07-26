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

        EditorGUILayout.EndScrollView();
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
