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

    // [MenuItem("BlockMerge3D/🧭 Seviye Oluşturma Sihirbazı", false, 1)]
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
        GUI.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1.0f);
        EditorGUILayout.BeginHorizontal(GUI.skin.box);
        GUI.backgroundColor = Color.white;

        for (int i = 1; i <= STEP_COUNT; i++)
        {
            bool isActive = i == currentStep;
            bool isCompleted = i < currentStep;

            if (isActive)
            {
                GUI.backgroundColor = new Color(0.15f, 0.60f, 0.90f); // Bright blue
            }
            else if (isCompleted)
            {
                GUI.backgroundColor = new Color(0.18f, 0.70f, 0.40f); // Green for completed
            }
            else
            {
                GUI.backgroundColor = new Color(0.24f, 0.24f, 0.28f); // Dark grey
            }

            var stepStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                fixedHeight = 32
            };
            stepStyle.normal.textColor = Color.white;

            string prefix = isCompleted ? "✓ " : (isActive ? "▶ " : "○ ");
            string labelText = prefix + StepLabels[i - 1];

            bool isReachable = i <= currentStep;
            EditorGUI.BeginDisabledGroup(!isReachable);
            if (GUILayout.Button(labelText, stepStyle, GUILayout.ExpandWidth(true)))
            {
                currentStep = i;
            }
            EditorGUI.EndDisabledGroup();

            if (i < STEP_COUNT)
            {
                var arrowStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
                };
                GUILayout.Label(" ➔ ", arrowStyle, GUILayout.Width(20), GUILayout.Height(32));
            }
        }
        GUI.backgroundColor = Color.white;
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
        GUILayout.Label("ADIM 2 — PARÇA KÜTÜPHANESİ KONTROLÜ", styleHeader);
        EditorGUILayout.HelpBox(
            "Üretim, Assets/PieceDefinitions/ altındaki parçalar kullanılarak yapılacak (Solution-First).", MessageType.Info);
        EditorGUILayout.Space(6);

        var library = aiDesigner.LoadPieceLibrary();

        GUI.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1.0f);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUI.backgroundColor = Color.white;

        if (library.Count == 0)
        {
            // Empty library layout
            GUI.backgroundColor = new Color(0.88f, 0.25f, 0.25f, 0.12f);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;
            
            var warningHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.88f, 0.25f, 0.25f) } };
            GUILayout.Label("⚠️  KÜTÜPHANE BOŞ: HİÇ PARÇA BULUNAMADI!", warningHeaderStyle);
            GUILayout.Label("Seviye üretebilmek için Assets/Pieces klasörünü tarayıp parça kütüphanesini doldurmalısınız.", EditorStyles.wordWrappedMiniLabel);
            
            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.15f, 0.60f, 0.90f);
            var setupBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            if (GUILayout.Button("🧬  Kütüphaneyi Otomatik Kur (Tara ve Migrate Et)", setupBtnStyle, GUILayout.Height(36)))
            {
                migrationWindow.ScanAndMigrate();
                aiDesigner.RefreshPieceLibrary();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }
        else
        {
            // Ready library layout
            GUI.backgroundColor = new Color(0.18f, 0.70f, 0.40f, 0.12f);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;

            var successHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.18f, 0.70f, 0.40f) } };
            GUILayout.Label("✅  KÜTÜPHANE HAZIR: PARÇALAR YÜKLENDİ", successHeaderStyle);
            GUILayout.Label($"Kayıtlı Blueprint Parça Sayısı: {library.Count}", EditorStyles.boldLabel);
            GUILayout.Label("Parça kütüphanesi aktif. Detaylı düzenlemeler için üstteki 🧬 Parça Kütüphanesi sekmesini kullanabilirsiniz.", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.24f, 0.24f, 0.28f);
            var refreshBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, normal = { textColor = Color.white } };
            if (GUILayout.Button("🔁  Kütüphaneyi Yenile / Yeniden Oku", refreshBtnStyle, GUILayout.Width(220), GUILayout.Height(26)))
            {
                aiDesigner.RefreshPieceLibrary();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();

        DrawNavigation(canGoNext: library.Count > 0);
    }

    // ── Adım 3 ───────────────────────────────────────────────────
    private void DrawStep3()
    {
        GUILayout.Label("ADIM 3 — SEVİYE ÜRETİMİ VE SOLVER TESTİ", styleHeader);
        EditorGUILayout.HelpBox(
            "Seçilen şablon, zorluk ve kütüphaneye göre seviye oluşturulur ve otomatik solver ile test edilir.", MessageType.Info);
        EditorGUILayout.Space(6);

        // Parameters Summary Panel
        GUI.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1.0f);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUI.backgroundColor = Color.white;

        GUILayout.Label("📋  AKTİF ÜRETİM PARAMETRELERİ", EditorStyles.boldLabel);
        
        string templateName = aiDesigner.selectedTemplate != null ? aiDesigner.selectedTemplate.templateName : "—";
        EditorGUILayout.LabelField($"• Şablon (Template): {templateName}", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField($"• Zorluk Modu: {aiDesigner.selectedDifficulty}", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField($"• Grid Çözünürlüğü: {aiDesigner.gridSize}", EditorStyles.miniBoldLabel);

        EditorGUILayout.Space(8);

        // Big visual generate button
        GUI.backgroundColor = new Color(0.15f, 0.60f, 0.90f);
        var genBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        if (GUILayout.Button("🎲  SEVİYEYİ RASTGELE ÜRET VE DOĞRULA", genBtnStyle, GUILayout.Height(38)))
        {
            aiDesigner.GenerateLevelProcedurally();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(8);
        aiDesigner.DrawSolverResultSection();
        EditorGUILayout.EndVertical();

        bool isValidated = aiDesigner.solverRan && aiDesigner.lastSolverResult != null && aiDesigner.lastSolverResult.isSolvable;
        DrawNavigation(canGoNext: isValidated);
    }

    // ── Adım 4 ───────────────────────────────────────────────────
    private void DrawStep4()
    {
        GUILayout.Label("ADIM 4 — VERİTABANINA SEVİYEYİ KAYDET", styleHeader);
        EditorGUILayout.HelpBox(
            "Seviye meta-verilerini (isim, süre, hedef) gözden geçirip veritabanına ihraç edin.", MessageType.Info);
        EditorGUILayout.Space(6);

        GUI.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1.0f);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUI.backgroundColor = Color.white;

        GUILayout.Label("✍️  SEVİYE METADATALARI", EditorStyles.boldLabel);
        aiDesigner.levelName = EditorGUILayout.TextField("Seviye Dosya Adı", aiDesigner.levelName);
        aiDesigner.levelTime = EditorGUILayout.FloatField("Süre Sınırı (Saniye, 0 = Süresiz)", aiDesigner.levelTime);
        aiDesigner.levelTarget = EditorGUILayout.IntField("Hedef Puan (Target)", aiDesigner.levelTarget);

        EditorGUILayout.Space(8);
        aiDesigner.DrawSolverResultSection();

        EditorGUILayout.Space(8);
        bool isValidated = aiDesigner.solverRan && aiDesigner.lastSolverResult != null && aiDesigner.lastSolverResult.isSolvable;
        
        EditorGUI.BeginDisabledGroup(!isValidated);
        GUI.backgroundColor = new Color(0.18f, 0.70f, 0.40f);
        var saveBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        if (GUILayout.Button("💾  SEVİYEYİ DOSYA OLARAK KAYDET (EXPORT)", saveBtnStyle, GUILayout.Height(42)))
        {
            aiDesigner.ExportProceduralLevel();
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(12);
        GUI.backgroundColor = new Color(0.24f, 0.24f, 0.28f);
        var resetStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            normal = { textColor = Color.white }
        };
        if (GUILayout.Button("🎉  Yeni Seviye Atölyesi Aç (1. Adıma Dön)", resetStyle, GUILayout.Height(28)))
        {
            currentStep = 1;
        }
        GUI.backgroundColor = Color.white;

        DrawNavigation(canGoNext: false, showNext: false);
    }

    private void DrawNavigation(bool canGoNext, bool showNext = true)
    {
        EditorGUILayout.Space(12);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginDisabledGroup(currentStep <= 1);
        GUI.backgroundColor = currentStep > 1 ? new Color(0.32f, 0.32f, 0.36f) : Color.white;
        var navStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        if (GUILayout.Button("◀  GERİ", navStyle, GUILayout.Width(100), GUILayout.Height(30)))
        {
            currentStep--;
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();

        GUILayout.FlexibleSpace();

        if (showNext)
        {
            EditorGUI.BeginDisabledGroup(!canGoNext);
            GUI.backgroundColor = canGoNext ? new Color(0.15f, 0.60f, 0.90f) : new Color(0.25f, 0.25f, 0.25f, 0.4f);
            if (GUILayout.Button("İLERİ  ▶", navStyle, GUILayout.Width(100), GUILayout.Height(30)))
            {
                currentStep++;
            }
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();
            
            if (!canGoNext)
            {
                GUILayout.Space(6);
                var warningLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.85f, 0.35f, 0.35f) },
                    fontStyle = FontStyle.Italic
                };
                EditorGUILayout.LabelField("(bu adımın koşulu henüz sağlanmadı)", warningLabelStyle, GUILayout.Width(180));
            }
        }

        EditorGUILayout.EndHorizontal();
    }
}
