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
    private CanvasSetupWindow canvasSetup;
    private AestheticSetupTool aestheticSetup;

    // Styles
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle toolbarStyle;
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

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        InitializeStyles();
        InitializeSubWindows(); // Fallback assurance

        // --- Top Header Panel ---
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("BLOCKMERGE 3D  •  GELİŞTİRİCİ MERKEZİ", headerStyle);
        EditorGUILayout.LabelField("Tüm tasarım, seviye düzenleme ve kurulum araçları tek bir yerde toplandı.", subHeaderStyle);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.5f, 0.9f);
        if (GUILayout.Button("3. 📋 Seviye Sıralama Penceresini Aç", GUILayout.Width(260), GUILayout.Height(26)))
        {
            LevelOrderEditorWindow.ShowWindow();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        GUI.backgroundColor = new Color(0.9f, 0.45f, 0.45f, 0.9f);
        if (GUILayout.Button("⚡ Katman Paneli Arayüzünü Kur", GUILayout.Width(220), GUILayout.Height(26)))
        {
            LayerPanelSetupTool.SetupLayerPanel();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(5);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        // --- Tab Selection Toolbar ---
        EditorGUI.BeginChangeCheck();
        activeTab = (Tab)GUILayout.Toolbar((int)activeTab, tabNames, toolbarStyle);
        if (EditorGUI.EndChangeCheck())
        {
            DisableActiveTab(previousTab);
            EnableActiveTab(activeTab);
            previousTab = activeTab;
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }

        EditorGUILayout.Space(10);

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
            case Tab.CanvasSetup:
                if (canvasSetup != null) canvasSetup.OnGUI();
                break;
            case Tab.Aesthetics:
                if (aestheticSetup != null) aestheticSetup.OnGUI();
                break;
        }
    }
}
