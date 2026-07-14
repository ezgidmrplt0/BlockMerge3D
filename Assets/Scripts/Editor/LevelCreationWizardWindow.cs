using UnityEngine;
using UnityEditor;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL CREATION WIZARD  —  Adım Adım Seviye Oluşturma
//  BlockMerge3D  •  1) Zorluk & Şablon  2) Parça Kütüphanesi
//                   3) Üret & Doğrula   4) Kaydet
//
//  Bu pencere AILevelDesignerWindow/PieceDefinitionMigrationWindow'un
//  üretim/export/migration MANTIĞINI kopyalamaz — onların birer arka
//  plan instance'ını tutar (Hub'ın zaten yaptığı gibi) ve o pencerelerin
//  artık 'internal' olan alan/metotlarını doğrudan çağırır. Eski üretim
//  modları (BFS/Geometrik/Tetromino carve) proje temizliği sırasında
//  AILevelDesignerWindow.cs'den tamamen kaldırıldı — Solution-First
//  (kütüphaneden geri izlemeli yerleştirme) artık TEK üretim yolu.
// ═══════════════════════════════════════════════════════════════════

public class LevelCreationWizardWindow : EditorWindow
{
    private const int STEP_COUNT = 4;
    private static readonly string[] StepLabels =
    {
        "1. Zorluk & Şablon", "2. Parça Kütüphanesi", "3. Üret & Doğrula", "4. Kaydet"
    };

    private int currentStep = 1;

    private AILevelDesignerWindow aiDesigner;
    private PieceDefinitionMigrationWindow migrationWindow;

    private GUIStyle styleHeader, styleStepActive, styleStepInactive, styleBox;
    private bool stylesBuilt;

    public System.Action onRepaintRequested;

    new public void Repaint()
    {
        base.Repaint();
        onRepaintRequested?.Invoke();
    }

    [MenuItem("BlockMerge3D/🧭 Seviye Oluşturma Sihirbazı", false, 1)]
    public static void ShowWindow()
    {
        var w = GetWindow<LevelCreationWizardWindow>("Seviye Sihirbazı");
        w.minSize = new Vector2(720, 560);
    }

    public void OnEnable()
    {
        EnsureSubWindows();
    }

    public void OnDisable()
    {
        if (aiDesigner != null) DestroyImmediate(aiDesigner);
        if (migrationWindow != null) DestroyImmediate(migrationWindow);
    }

    private void EnsureSubWindows()
    {
        if (aiDesigner == null) aiDesigner = CreateInstance<AILevelDesignerWindow>();
        if (migrationWindow == null) migrationWindow = CreateInstance<PieceDefinitionMigrationWindow>();
    }

    private void BuildStyles()
    {
        if (stylesBuilt) return;
        styleHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        styleHeader.normal.textColor = new Color(0.35f, 0.78f, 1.00f);
        styleStepActive = new GUIStyle(EditorStyles.toolbarButton) { fixedHeight = 30, fontStyle = FontStyle.Bold };
        styleStepActive.normal.textColor = new Color(0.35f, 0.85f, 1.00f);
        styleStepInactive = new GUIStyle(EditorStyles.toolbarButton) { fixedHeight = 30 };
        styleBox = new GUIStyle(GUI.skin.box);
        stylesBuilt = true;
    }

    public void OnGUI()
    {
        EnsureSubWindows();
        BuildStyles();
        aiDesigner.BuildStyles(); // DrawTemplateAndDifficultySection/DrawSolverResultSection buna güveniyor

        DrawStepIndicator();
        EditorGUILayout.Space(10);

        switch (currentStep)
        {
            case 1: DrawStep1(); break;
            case 2: DrawStep2(); break;
            case 3: DrawStep3(); break;
            case 4: DrawStep4(); break;
        }
    }

    private void DrawStepIndicator()
    {
        EditorGUILayout.BeginHorizontal();
        for (int i = 1; i <= STEP_COUNT; i++)
        {
            bool isActive = i == currentStep;
            bool isReachable = i <= currentStep; // ileri atlama yok, sadece geri/aynı adım
            EditorGUI.BeginDisabledGroup(!isReachable);
            if (GUILayout.Button(StepLabels[i - 1], isActive ? styleStepActive : styleStepInactive))
            {
                currentStep = i;
            }
            EditorGUI.EndDisabledGroup();
        }
        EditorGUILayout.EndHorizontal();
    }

    // ── Adım 1 ───────────────────────────────────────────────────
    private void DrawStep1()
    {
        GUILayout.Label("ADIM 1 — ZORLUK & ÜRETİM KAYNAĞI SEÇ", styleHeader);
        EditorGUILayout.HelpBox("Önce bir seviye şablonu veya özel prefab seçin ve zorluk profilini belirleyin.", MessageType.Info);
        EditorGUILayout.Space(6);

        EditorGUILayout.BeginVertical(styleBox);
        aiDesigner.DrawTemplateAndDifficultySection();
        EditorGUILayout.EndVertical();

        bool canGoNext = false;
        if (aiDesigner.generationBaseType == AILevelDesignerWindow.GenerationBaseType.Template)
            canGoNext = aiDesigner.selectedTemplate != null;
        else
            canGoNext = aiDesigner.customBasePrefab != null && aiDesigner.customBasePrefab.GetComponent<CubeShapeDataHolder>() != null;

        DrawNavigation(canGoNext: canGoNext);
    }

    // ── Adım 2 ───────────────────────────────────────────────────
    private void DrawStep2()
    {
        GUILayout.Label("ADIM 2 — PARÇA KÜTÜPHANESİ", styleHeader);
        EditorGUILayout.HelpBox(
            "Üretim Assets/PieceDefinitions/ altındaki gerçek parçalardan yapılacak " +
            "(Solution-First backtracking) — bu artık projedeki tek üretim yolu.", MessageType.Info);
        EditorGUILayout.Space(6);

        var library = aiDesigner.LoadPieceLibrary();

        EditorGUILayout.BeginVertical(styleBox);
        EditorGUILayout.LabelField($"Yüklü parça sayısı: {library.Count}", EditorStyles.boldLabel);

        if (library.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Kütüphane boş. Assets/Pieces ve Assets/Levels altındaki mevcut parça " +
                "prefab'larını tarayıp PieceDefinition assetleri oluşturmak için:", MessageType.Warning);
            GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
            if (GUILayout.Button("🧬 Kütüphaneyi Kur (Tara ve Migrate Et)", GUILayout.Height(32)))
            {
                migrationWindow.ScanAndMigrate();
                aiDesigner.RefreshPieceLibrary();
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            EditorGUILayout.LabelField(
                "Kütüphane hazır. Detaylı düzenleme için 🧬 Parça Kütüphanesi sekmesini kullanabilirsin.",
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Kütüphaneyi Yenile", GUILayout.Width(140)))
            {
                aiDesigner.RefreshPieceLibrary();
            }
        }
        EditorGUILayout.EndVertical();

        DrawNavigation(canGoNext: aiDesigner.LoadPieceLibrary().Count > 0);
    }

    // ── Adım 3 ───────────────────────────────────────────────────
    private void DrawStep3()
    {
        GUILayout.Label("ADIM 3 — ÜRET & DOĞRULA", styleHeader);
        EditorGUILayout.HelpBox(
            "Seçilen şablon + zorluk + kütüphaneyle bir seviye üretilecek ve otomatik olarak " +
            "solver ile doğrulanacak. Doğrulanmadan bir sonraki adıma geçilemez.", MessageType.Info);
        EditorGUILayout.Space(6);

        EditorGUILayout.BeginVertical(styleBox);
        string templateName = aiDesigner.selectedTemplate != null ? aiDesigner.selectedTemplate.templateName : "—";
        EditorGUILayout.LabelField($"Şablon: {templateName}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Zorluk: {aiDesigner.selectedDifficulty}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Grid: {aiDesigner.gridSize}", EditorStyles.miniLabel);

        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
        if (GUILayout.Button("🎲 Üret", GUILayout.Height(36)))
        {
            aiDesigner.GenerateLevelProcedurally();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(6);
        aiDesigner.DrawSolverResultSection();
        EditorGUILayout.EndVertical();

        bool isValidated = aiDesigner.solverRan && aiDesigner.lastSolverResult != null && aiDesigner.lastSolverResult.isSolvable;
        DrawNavigation(canGoNext: isValidated);
    }

    // ── Adım 4 ───────────────────────────────────────────────────
    private void DrawStep4()
    {
        GUILayout.Label("ADIM 4 — KAYDET", styleHeader);
        EditorGUILayout.HelpBox(
            "Seviye adı/süre/hedef puanı gözden geçir ve kaydet. Doğrulanmamış bir seviye " +
            "kaydedilemez (Zorunlu Koruma Kuralı #1).", MessageType.Info);
        EditorGUILayout.Space(6);

        EditorGUILayout.BeginVertical(styleBox);
        aiDesigner.levelName = EditorGUILayout.TextField("Seviye Adı", aiDesigner.levelName);
        aiDesigner.levelTime = EditorGUILayout.FloatField("Süre Sınırı (sn, 0 = süresiz)", aiDesigner.levelTime);
        aiDesigner.levelTarget = EditorGUILayout.IntField("Hedef Skor", aiDesigner.levelTarget);

        EditorGUILayout.Space(6);
        aiDesigner.DrawSolverResultSection();

        bool isValidated = aiDesigner.solverRan && aiDesigner.lastSolverResult != null && aiDesigner.lastSolverResult.isSolvable;
        EditorGUI.BeginDisabledGroup(!isValidated);
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f, 1f);
        if (GUILayout.Button("💾 Kaydet", GUILayout.Height(44)))
        {
            aiDesigner.ExportProceduralLevel();
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);
        if (GUILayout.Button("🎉 Yeni Seviye Oluştur (1. Adıma Dön)", GUILayout.Height(30)))
        {
            currentStep = 1;
        }

        DrawNavigation(canGoNext: false, showNext: false);
    }

    private void DrawNavigation(bool canGoNext, bool showNext = true)
    {
        EditorGUILayout.Space(12);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginDisabledGroup(currentStep <= 1);
        if (GUILayout.Button("◀ Geri", GUILayout.Width(100), GUILayout.Height(30)))
        {
            currentStep--;
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.FlexibleSpace();

        if (showNext)
        {
            EditorGUI.BeginDisabledGroup(!canGoNext);
            if (GUILayout.Button("İleri ▶", GUILayout.Width(100), GUILayout.Height(30)))
            {
                currentStep++;
            }
            EditorGUI.EndDisabledGroup();
            if (!canGoNext)
            {
                GUILayout.Space(6);
                EditorGUILayout.LabelField("(bu adımın koşulu henüz sağlanmadı)", EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.EndHorizontal();
    }
}
