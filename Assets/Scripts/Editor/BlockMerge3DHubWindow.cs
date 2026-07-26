using UnityEngine;
using UnityEditor;

public class BlockMerge3DHubWindow : EditorWindow
{
    private enum Tab
    {
        Wizard,
        LevelBuilder,
        PieceDesigner,
        AILevelDesigner,
        ManualVoxelBuilder,
        PieceLibrary,
        LevelTemplates,
        CanvasSetup,
        Aesthetics
    }

    private Tab activeTab = Tab.Wizard;
    private Tab previousTab = Tab.Wizard;

    private readonly string[] tabNames = new string[]
    {
        "🧭 Sihirbaz",
        "1. 🗂 Seviye Tasarımcısı",
        "2. 🧩 Parça Tasarımcısı",
        "🤖 AI Seviye Tasarımcısı",
        "🧱 3D Sahne Yapıcı",
        "🧬 Parça Kütüphanesi",
        "🏰 Level Şablonları",
        "📺 Arayüz Kurulumu",
        "🎨 Görsel & Kurulum"
    };

    // Sub-window instances
    private LevelCreationWizardWindow wizard;
    private LevelBuilderWindow levelBuilder;
    private PieceDesignerWindow pieceDesigner;
    private AILevelDesignerWindow aiLevelDesigner;
    private ManualVoxelPieceBuilderWindow manualVoxelBuilder;
    private PieceDefinitionMigrationWindow pieceLibrary;
    private LevelTemplateManagerWindow levelTemplates;
    private CanvasSetupWindow canvasSetup;
    private AestheticSetupTool aestheticSetup;

    // Styles
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle toolbarStyle;
    private GUIStyle tabStyleActive;
    private GUIStyle tabStyleInactive;
    private bool stylesInitialized = false;

    [MenuItem("BlockMerge3D/BlockMerge3D Hub", false, 0)]
    public static void Open()
    {
        var w = GetWindow<BlockMerge3DHubWindow>("BlockMerge3D Geliştirici Merkezi");
        w.minSize = new Vector2(1000, 680);
    }

    private void OnEnable()
    {
        InitializeSubWindows();
        previousTab = activeTab;
        EnableActiveTab(activeTab);
    }

    private void OnDisable()
    {
        DisableActiveTab(activeTab);
        DestroySubWindows();
    }

    private void InitializeSubWindows()
    {
        if (wizard == null)
        {
            wizard = CreateInstance<LevelCreationWizardWindow>();
            wizard.onRepaintRequested = Repaint;
        }
        if (levelBuilder == null)
        {
            levelBuilder = CreateInstance<LevelBuilderWindow>();
            levelBuilder.onRepaintRequested = Repaint;
        }
        if (pieceDesigner == null)
        {
            pieceDesigner = CreateInstance<PieceDesignerWindow>();
            pieceDesigner.onRepaintRequested = Repaint;
        }
        if (aiLevelDesigner == null)
        {
            aiLevelDesigner = CreateInstance<AILevelDesignerWindow>();
            aiLevelDesigner.onRepaintRequested = Repaint;
        }
        if (manualVoxelBuilder == null)
        {
            manualVoxelBuilder = CreateInstance<ManualVoxelPieceBuilderWindow>();
            // Since it shares repaint, we can set up an action if needed, or simply let Unity handle standard repaints
        }
        if (pieceLibrary == null)
        {
            pieceLibrary = CreateInstance<PieceDefinitionMigrationWindow>();
            pieceLibrary.onRepaintRequested = Repaint;
        }
        if (levelTemplates == null)
        {
            levelTemplates = CreateInstance<LevelTemplateManagerWindow>();
            levelTemplates.onRepaintRequested = Repaint;
        }
        if (canvasSetup == null)
        {
            canvasSetup = CreateInstance<CanvasSetupWindow>();
            canvasSetup.onRepaintRequested = Repaint;
        }
        if (aestheticSetup == null)
        {
            aestheticSetup = CreateInstance<AestheticSetupTool>();
            aestheticSetup.onRepaintRequested = Repaint;
        }
    }

    private void DestroySubWindows()
    {
        if (wizard != null) DestroyImmediate(wizard);
        if (levelBuilder != null) DestroyImmediate(levelBuilder);
        if (pieceDesigner != null) DestroyImmediate(pieceDesigner);
        if (aiLevelDesigner != null) DestroyImmediate(aiLevelDesigner);
        if (manualVoxelBuilder != null) DestroyImmediate(manualVoxelBuilder);
        if (pieceLibrary != null) DestroyImmediate(pieceLibrary);
        if (levelTemplates != null) DestroyImmediate(levelTemplates);
        if (canvasSetup != null) DestroyImmediate(canvasSetup);
        if (aestheticSetup != null) DestroyImmediate(aestheticSetup);
    }

    private void DisableActiveTab(Tab tab)
    {
        if (tab == Tab.ManualVoxelBuilder && manualVoxelBuilder != null)
        {
            // We can call its OnDisable manually to unsubscribe from SceneView
            var method = manualVoxelBuilder.GetType().GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (method != null) method.Invoke(manualVoxelBuilder, null);
        }
    }

    private void EnableActiveTab(Tab tab)
    {
        if (tab == Tab.ManualVoxelBuilder && manualVoxelBuilder != null)
        {
            // We can call its OnEnable manually to subscribe to SceneView
            var method = manualVoxelBuilder.GetType().GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (method != null) method.Invoke(manualVoxelBuilder, null);
        }
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i) pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    private void InitializeStyles()
    {
        if (stylesInitialized) return;

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
            margin = new RectOffset(0, 0, 10, 5)
        };
        headerStyle.normal.textColor = new Color(0.35f, 0.78f, 1.00f);

        subHeaderStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            fontSize = 11,
            margin = new RectOffset(0, 0, 0, 10)
        };

        toolbarStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            fixedHeight = 35
        };

        tabStyleActive = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            fixedHeight = 36,
            wordWrap = true,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.15f, 0.60f, 0.90f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.25f, 0.70f, 1.00f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.10f, 0.50f, 0.80f)) }
        };

        tabStyleInactive = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Normal,
            fixedHeight = 36,
            wordWrap = true,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f), background = MakeTex(2, 2, new Color(0.24f, 0.24f, 0.28f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.32f, 0.32f, 0.38f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.18f, 0.18f, 0.22f)) }
        };

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        InitializeStyles();
        InitializeSubWindows(); // Fallback assurance

        // --- Top Header Panel (Modern Workshop Design) ---
        GUI.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1.0f);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        var logoStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        logoStyle.normal.textColor = new Color(0.35f, 0.82f, 1.00f);
        GUILayout.Label("🛠️  BLOCKMERGE 3D  •  GELİŞTİRİCİ ATÖLYESİ", logoStyle);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        var descStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.75f, 0.78f, 0.82f) }
        };
        GUILayout.Label("Seviye tasarımı, AI üretimi ve görsel kurulum araçları tek bir kontrol panelinde birleştirildi.", descStyle);

        EditorGUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        GUI.backgroundColor = new Color(0.15f, 0.65f, 0.35f, 1.0f);
        var quickBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        if (GUILayout.Button("📋  Seviye Sıralama Penceresini Aç", quickBtnStyle, GUILayout.Width(250), GUILayout.Height(28)))
        {
            LevelOrderEditorWindow.ShowWindow();
        }

        GUILayout.Space(12);

        GUI.backgroundColor = new Color(0.82f, 0.30f, 0.30f, 1.0f);
        if (GUILayout.Button("⚡  Katman Paneli Arayüzünü Kur", quickBtnStyle, GUILayout.Width(230), GUILayout.Height(28)))
        {
            LayerPanelSetupTool.SetupLayerPanel();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(6);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // --- Tab Selection (Custom Buttons Layout replacing default Grid Toolbar) ---
        int prevTabIdx = (int)activeTab;
        
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < tabNames.Length; i++)
        {
            bool isActive = ((int)activeTab == i);
            GUIStyle tabStyle = isActive ? tabStyleActive : tabStyleInactive;
            
            if (GUILayout.Button(tabNames[i], tabStyle, GUILayout.ExpandWidth(true)))
            {
                activeTab = (Tab)i;
            }
        }
        EditorGUILayout.EndHorizontal();

        if ((int)activeTab != prevTabIdx)
        {
            DisableActiveTab((Tab)prevTabIdx);
            EnableActiveTab(activeTab);
            previousTab = activeTab;
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }

        EditorGUILayout.Space(12);

        // --- Active Tab Draw Content ---
        switch (activeTab)
        {
            case Tab.Wizard:
                if (wizard != null) wizard.OnGUI();
                break;
            case Tab.LevelBuilder:
                if (levelBuilder != null) levelBuilder.OnGUI();
                break;
            case Tab.PieceDesigner:
                if (pieceDesigner != null) pieceDesigner.OnGUI();
                break;
            case Tab.AILevelDesigner:
                if (aiLevelDesigner != null) aiLevelDesigner.OnGUI();
                break;
            case Tab.ManualVoxelBuilder:
                if (manualVoxelBuilder != null) manualVoxelBuilder.OnGUI();
                break;
            case Tab.PieceLibrary:
                if (pieceLibrary != null) pieceLibrary.OnGUI();
                break;
            case Tab.LevelTemplates:
                if (levelTemplates != null) levelTemplates.OnGUI();
                break;
            case Tab.CanvasSetup:
                if (canvasSetup != null) canvasSetup.OnGUI();
                break;
            case Tab.Aesthetics:
                if (aestheticSetup != null) aestheticSetup.OnGUI();
                break;
        }
    }
}
