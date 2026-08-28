using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class AestheticSetupTool : EditorWindow
{
    private Color[] premiumColors = new Color[]
    {
        new Color(0.96f, 0.18f, 0.22f), // Sıcak Canlı Kırmızı
        new Color(1.00f, 0.45f, 0.05f), // Sıcak Canlı Turuncu
        new Color(1.00f, 0.76f, 0.05f), // Sıcak Altın Sarısı
        new Color(0.98f, 0.16f, 0.52f), // Sıcak Canlı Pembe / Fuşya
        new Color(0.64f, 0.18f, 0.95f), // Sıcak Canlı Mor
        new Color(0.32f, 0.85f, 0.15f), // Sıcak Canlı Fıstık Yeşili
        new Color(0.05f, 0.75f, 0.95f)  // Canlı Elektrik Turkuaz / Aqua
    };

    private float materialSmoothness = 0.60f;
    private float materialMetallic = 0.0f;
    private float materialEmissionMultiplier = 0.0f;

    public System.Action onRepaintRequested;

    new public void Repaint()
    {
        base.Repaint();
        if (onRepaintRequested != null)
            onRepaintRequested();
    }

    // [MenuItem("BlockMerge3D/Aesthetic Setup Tool")]
    // public static void ShowWindow()
    // {
    //     GetWindow<AestheticSetupTool>("Aesthetic Setup");
    // }

    public void OnGUI()
    {
        GUILayout.Label("Spider-Verse / Premium Görsel Ayarları", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Bu araç, kalıcı Materyal varlıkları (.mat) oluşturur ve bunları sahnedeki LevelManager'a atar.", MessageType.Info);

        EditorGUILayout.Space();
        GUILayout.Label("Renkler", EditorStyles.boldLabel);
        for (int i = 0; i < premiumColors.Length; i++)
        {
            premiumColors[i] = EditorGUILayout.ColorField($"Renk {i + 1}", premiumColors[i]);
        }

        EditorGUILayout.Space();
        GUILayout.Label("Gölgelendirici (Shader) Özellikleri", EditorStyles.boldLabel);
        materialSmoothness = EditorGUILayout.Slider("Pürüzsüzlük (Smoothness)", materialSmoothness, 0f, 1f);
        materialMetallic = EditorGUILayout.Slider("Metaliklik (Metallic)", materialMetallic, 0f, 1f);
        materialEmissionMultiplier = EditorGUILayout.Slider("Işıma Çarpanı (Emission Multiplier)", materialEmissionMultiplier, 0f, 2f);

        EditorGUILayout.Space();

        if (GUILayout.Button("Materyalleri Üret & Sahneye Uygula", GUILayout.Height(40)))
        {
            GenerateAndApply();
        }
    }

    private void GenerateAndApply()
    {
        string folderPath = "Assets/Materials/Premium";
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "Premium");
        }

        Shader standardShader = Shader.Find("Universal Render Pipeline/Lit");
        if (standardShader == null) standardShader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (standardShader == null) standardShader = Shader.Find("Standard");

        if (standardShader == null)
        {
            Debug.LogError("[AestheticSetupTool] Could not find a suitable shader.");
            return;
        }

        List<Material> generatedMaterials = new List<Material>();

        for (int i = 0; i < premiumColors.Length; i++)
        {
            Color col = premiumColors[i];
            string assetPath = $"{folderPath}/PremiumMaterial_{i}.mat";

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                mat = new Material(standardShader);
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            else
            {
                mat.shader = standardShader; // Ensure it uses the right shader
            }

            // Apply properties
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);

            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", materialSmoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", materialMetallic);

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", col * materialEmissionMultiplier);
                mat.EnableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(mat);
            generatedMaterials.Add(mat);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Find LevelManager and assign materials
        LevelManager lm = FindObjectOfType<LevelManager>();
        if (lm != null)
        {
            Undo.RecordObject(lm, "Assign Premium Materials");
            lm.pieceMaterials = generatedMaterials.ToArray();
            EditorUtility.SetDirty(lm);
            Debug.Log("[AestheticSetupTool] Successfully assigned materials to LevelManager.");
        }
        else
        {
            Debug.LogWarning("[AestheticSetupTool] LevelManager not found in the current scene. Materials were generated but not assigned.");
        }

        // Find UIManager and assign UI colors
        UIManager ui = FindObjectOfType<UIManager>();
        if (ui != null)
        {
            Undo.RecordObject(ui, "Update UI Aesthetics");
            ui.UpdateUIAesthetics(premiumColors);
            EditorUtility.SetDirty(ui.gameObject);
            Debug.Log("[AestheticSetupTool] Successfully updated UIManager colors.");
        }
        else
        {
            Debug.LogWarning("[AestheticSetupTool] UIManager not found in the current scene. UI colors were not updated.");
        }

        EditorUtility.DisplayDialog("Aesthetics Setup Complete", "Successfully generated materials and applied UI colors to the scene!", "OK");
    }
}
