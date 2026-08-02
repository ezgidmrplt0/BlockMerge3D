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

    [SerializeField] private int currentStep = 1;
    [SerializeField] private Vector2 wizardScrollPos;

    [SerializeField] private AILevelDesignerWindow aiDesigner;
    [SerializeField] private PieceDefinitionMigrationWindow migrationWindow;

    private GUIStyle styleHeader, styleBox;
    private GUIStyle stylePrimaryButton, styleSuccessButton, styleDangerButton, styleWarningButton, styleDarkButton;
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

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i)
        {
            pix[i] = col;
        }
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    private void BuildStyles()
    {
        if (stylesBuilt) return;
        styleHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        styleHeader.normal.textColor = new Color(0.35f, 0.78f, 1.00f);
        styleBox = new GUIStyle(GUI.skin.box);

        stylePrimaryButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.15f, 0.60f, 0.90f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.25f, 0.70f, 1.00f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.10f, 0.50f, 0.80f)) }
        };

        styleSuccessButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.18f, 0.70f, 0.40f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.24f, 0.80f, 0.48f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.14f, 0.58f, 0.32f)) }
        };

        styleDangerButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.90f, 0.30f, 0.30f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.98f, 0.38f, 0.38f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.75f, 0.22f, 0.22f)) }
        };

        styleWarningButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(1.00f, 0.75f, 0.20f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(1.00f, 0.82f, 0.32f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.85f, 0.62f, 0.15f)) }
        };

        styleDarkButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.9f, 0.9f, 0.9f), background = MakeTex(2, 2, new Color(0.24f, 0.24f, 0.28f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.32f, 0.32f, 0.38f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.18f, 0.18f, 0.22f)) }
        };

        stylesBuilt = true;
    }

    public void OnGUI()
    {
        EnsureSubWindows();
        BuildStyles();
        aiDesigner.BuildStyles();

        DrawStepIndicator();
        EditorGUILayout.Space(10);

        wizardScrollPos = EditorGUILayout.BeginScrollView(wizardScrollPos, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        switch (currentStep)
        {
            case 1: DrawStep1Content(); break;
            case 2: DrawStep2Content(); break;
            case 3: DrawStep3Content(); break;
            case 4: DrawStep4Content(); break;
        }

        EditorGUILayout.EndScrollView();

        bool canGoNext = GetStepCanGoNext(currentStep);
        bool showNext = currentStep < 4;
        DrawNavigation(canGoNext, showNext);
    }

    private bool GetStepCanGoNext(int step)
    {
        if (aiDesigner == null) return false;
        switch (step)
        {
            case 1:
                if (aiDesigner.generationBaseType == AILevelDesignerWindow.GenerationBaseType.Template)
                    return aiDesigner.selectedTemplate != null;
                else if (aiDesigner.generationBaseType == AILevelDesignerWindow.GenerationBaseType.CustomPrefab)
                    return aiDesigner.customBasePrefab != null && aiDesigner.customBasePrefab.GetComponent<CubeShapeDataHolder>() != null;
                else // CustomSize
                    return aiDesigner.gridSize.x > 0 && aiDesigner.gridSize.y > 0 && aiDesigner.gridSize.z > 0;
            case 2:
                var library = aiDesigner.LoadPieceLibrary();
                return library != null && library.Count > 0;
            case 3:
                return aiDesigner.solverRan && aiDesigner.lastSolverResult != null &&
                       (aiDesigner.lastSolverResult.isSolvable || aiDesigner.lastSolverResult.timedOut);
            default:
                return false;
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
                GUI.backgroundColor = new Color(0.15f, 0.60f, 0.90f); 
            }
            else if (isCompleted)
            {
                GUI.backgroundColor = new Color(0.18f, 0.70f, 0.40f); 
            }
            else
            {
                GUI.backgroundColor = new Color(0.24f, 0.24f, 0.28f); 
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

    private void DrawStep1Content()
    {
        GUILayout.Label("ADIM 1 — ZORLUK & ÜRETİM KAYNAĞI SEÇ", styleHeader);
        EditorGUILayout.HelpBox("Önce bir seviye şablonu veya özel prefab seçin ve zorluk profilini belirleyin.", MessageType.Info);
        EditorGUILayout.Space(6);

        EditorGUILayout.BeginVertical(styleBox);
        aiDesigner.DrawTemplateAndDifficultySection();
        EditorGUILayout.EndVertical();
    }

    private void DrawStep2Content()
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
            GUI.backgroundColor = new Color(0.88f, 0.25f, 0.25f, 0.12f);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;
            
            var warningHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.88f, 0.25f, 0.25f) } };
            GUILayout.Label("⚠️  KÜTÜPHANE BOŞ: HİÇ PARÇA BULUNAMADI!", warningHeaderStyle);
            GUILayout.Label("Seviye üretebilmek için Assets/Pieces klasörünü tarayıp parça kütüphanesini doldurmalısınız.", EditorStyles.wordWrappedMiniLabel);
            
            EditorGUILayout.Space(8);
            if (GUILayout.Button("🧬  Kütüphaneyi Otomatik Kur (Tara ve Migrate Et)", stylePrimaryButton, GUILayout.Height(36)))
            {
                migrationWindow.ScanAndMigrate();
                aiDesigner.RefreshPieceLibrary();
            }
            EditorGUILayout.EndVertical();
        }
        else
        {
            GUI.backgroundColor = new Color(0.18f, 0.70f, 0.40f, 0.12f);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;

            var successHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.18f, 0.70f, 0.40f) } };
            GUILayout.Label("✅  KÜTÜPHANE HAZIR: PARÇALAR YÜKLENDİ", successHeaderStyle);
            GUILayout.Label($"Kayıtlı Blueprint Parça Sayısı: {library.Count}", EditorStyles.boldLabel);
            GUILayout.Label("Parça kütüphanesi aktif. Detaylı düzenlemeler için üstteki 🧬 Parça Kütüphanesi sekmesini kullanabilirsiniz.", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("🔁  Kütüphaneyi Yenile / Yeniden Oku", styleDarkButton, GUILayout.Width(220), GUILayout.Height(26)))
            {
                aiDesigner.RefreshPieceLibrary();
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawStep3Content()
    {
        GUILayout.Label("ADIM 3 — KATMAN KATMAN ÜRETİM VE SOLVER TESTİ", styleHeader);
        EditorGUILayout.HelpBox(
            "Seviye Y ekseninde katman katman üretilir: parçalar katmanlarda buz yokmuşçasına tam katman geometrisine göre üretilir (böylece buz kırıldıktan sonra parçalar tam oturur ve leveller son derece çözülebilir kalır). Her katmanı onaylayıp ilerleyebilirsiniz.", MessageType.Info);
        EditorGUILayout.Space(6);

        if (aiDesigner.IsLayerGenSignatureStale())
        {
            aiDesigner.CancelStaleLayerGenSession();
        }

        GUI.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1.0f);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUI.backgroundColor = Color.white;

        GUILayout.Label("📋  AKTİF ÜRETİM PARAMETRELERİ", EditorStyles.boldLabel);

        string templateName = aiDesigner.selectedTemplate != null ? aiDesigner.selectedTemplate.templateName : "—";
        EditorGUILayout.LabelField($"• Şablon (Template): {templateName}", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField($"• Zorluk Modu: {aiDesigner.selectedDifficulty}", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField($"• Grid Çözünürlüğü: {aiDesigner.gridSize}", EditorStyles.miniBoldLabel);

        EditorGUILayout.Space(8);

        if (!aiDesigner.layerGenActive && !aiDesigner.solverRan)
        {
            if (GUILayout.Button("▶️  KATMAN KATMAN ÜRETİMİ BAŞLAT", stylePrimaryButton, GUILayout.Height(38)))
            {
                aiDesigner.StartLayerByLayerGeneration();
            }
        }

        EditorGUILayout.EndVertical();

        if (aiDesigner.layerGenActive)
        {
            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1.0f);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;

            int maxY = aiDesigner.gridSize.y;
            GUILayout.Label($"🧱  KATMAN {aiDesigner.genCurrentLayerY + 1} / {maxY} İNCELENİYOR", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(aiDesigner.layerGenError))
            {
                EditorGUILayout.HelpBox(aiDesigner.layerGenError, MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("🔄  Tekrar Dene (Farklı Parçalarla)", stylePrimaryButton, GUILayout.Height(30)))
                {
                    aiDesigner.RegenerateCurrentLayer();
                }
                if (GUILayout.Button("⚡  Otomatik Tamamla (Varsayılan Parçalarla Döşe)", styleSuccessButton, GUILayout.Height(30)))
                {
                    aiDesigner.ForceCompleteCurrentLayer();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(aiDesigner.layerGenError));
            if (GUILayout.Button("✅  Katmanı Onayla & Devam Et", styleSuccessButton, GUILayout.Height(32)))
            {
                aiDesigner.ApproveCurrentLayerAndAdvance();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(8);

            if (GUILayout.Button("❄️  Buzlu Yerleri Tespit Et", stylePrimaryButton, GUILayout.Height(32)))
            {
                aiDesigner.DetectAndDistributeIce();
            }

            GUILayout.Space(8);

            if (GUILayout.Button("🎨  Hazır Küpleri Dağıt", stylePrimaryButton, GUILayout.Height(32)))
            {
                aiDesigner.DetectAndDistributePrefilledCubes();
            }

            GUILayout.Space(8);

            if (GUILayout.Button("🔁  Bu Katmanı Yeniden Üret", styleDarkButton, GUILayout.Height(32)))
            {
                aiDesigner.RegenerateCurrentLayer();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);

            aiDesigner.DrawCenterGrid();

            EditorGUILayout.EndVertical();
        }

        if (aiDesigner.solverRan)
        {
            EditorGUILayout.Space(8);
            aiDesigner.DrawSolverResultSection(
                onRetry: aiDesigner.StartLayerByLayerGeneration,
                retryLabel: "🔁  Katmanları Baştan Üret");
        }
    }

    private void DrawStep4Content()
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

        EditorGUILayout.Space(8);
        aiDesigner.DrawSolverResultSection();

        EditorGUILayout.Space(8);
        bool isValidated = aiDesigner.solverRan && aiDesigner.lastSolverResult != null &&
                            (aiDesigner.lastSolverResult.isSolvable || aiDesigner.lastSolverResult.timedOut);
        
        EditorGUI.BeginDisabledGroup(!isValidated);
        if (GUILayout.Button("💾  SEVİYEYİ DOSYA OLARAK KAYDET (EXPORT)", styleSuccessButton, GUILayout.Height(42)))
        {
            aiDesigner.ExportProceduralLevel();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(12);
        if (GUILayout.Button("🎉  Yeni Seviye Atölyesi Aç (1. Adıma Dön)", styleDarkButton, GUILayout.Height(28)))
        {
            currentStep = 1;
        }
    }

    private void DrawNavigation(bool canGoNext, bool showNext = true)
    {
        EditorGUILayout.Space(12);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginDisabledGroup(currentStep <= 1);
        if (GUILayout.Button("◀  GERİ", styleDarkButton, GUILayout.Width(100), GUILayout.Height(30)))
        {
            currentStep--;
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.FlexibleSpace();

        if (showNext)
        {
            EditorGUI.BeginDisabledGroup(!canGoNext);
            GUIStyle nextStyle = canGoNext ? stylePrimaryButton : styleDarkButton;
            if (GUILayout.Button("İLERİ  ▶", nextStyle, GUILayout.Width(100), GUILayout.Height(30)))
            {
                currentStep++;
            }
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
