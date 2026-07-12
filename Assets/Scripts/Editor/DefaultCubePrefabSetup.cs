using UnityEditor;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  DEFAULT CUBE PREFAB SETUP
//  Tüm seviye/parça araçları (CubeShapeEditorWindow, LevelBuilderWindow,
//  PieceSplitterWindow, AILevelDesignerWindow, PieceDesignerWindow, Level2Generator)
//  "BlockMerge3D_DefaultCubePrefab" EditorPrefs anahtarından ortak bir küp prefabı okur.
//  Bu dosya o anahtarı Assets/Prefabs/Untitled1.prefab olarak ayarlar — böylece her yeni
//  seviye/parça, elle seçmeye gerek kalmadan doğru görsel/collider'a sahip küpü kullanır.
// ═══════════════════════════════════════════════════════════════════

[InitializeOnLoad]
public static class DefaultCubePrefabSetup
{
    private const string PREF_DEFAULT_CUBE = "BlockMerge3D_DefaultCubePrefab";
    public const string DEFAULT_CUBE_PATH = "Assets/Prefabs/Untitled1.prefab";

    static DefaultCubePrefabSetup()
    {
        // Sadece hiç ayarlanmamışsa otomatik uygula — kullanıcı daha sonra araç
        // pencerelerinden elle farklı bir prefab seçerse bu değeri ezmez.
        if (string.IsNullOrEmpty(EditorPrefs.GetString(PREF_DEFAULT_CUBE, "")))
        {
            Apply();
        }
    }

    [MenuItem("BlockMerge3D/Varsayılan Küp Prefabını Ayarla (Untitled1)")]
    public static void ApplyFromMenu()
    {
        Apply();
        EditorUtility.DisplayDialog("Varsayılan Küp Prefabı",
            $"Tüm seviye/parça araçları artık varsayılan olarak\n{DEFAULT_CUBE_PATH}\nkullanacak.",
            "Tamam");
    }

    private static void Apply()
    {
        EditorPrefs.SetString(PREF_DEFAULT_CUBE, DEFAULT_CUBE_PATH);
        Debug.Log($"[DefaultCubePrefabSetup] Varsayılan küp prefabı ayarlandı: {DEFAULT_CUBE_PATH}");
    }
}
