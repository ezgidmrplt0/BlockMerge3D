using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LevelForge.EditorTools
{
    /// <summary>
    /// 5-step guided setup: ask the developer about their game's generation parameters, difficulty
    /// metrics and named difficulty tiers, then generate a compilable adapter code scaffold (see
    /// LevelForgeAdapterCodeGenerator) into their project. This replaces hand-writing the 5-6 files
    /// described in ADAPTER_GUIDE.md with a questionnaire — the only part that can genuinely be
    /// automated is the mechanical scaffolding; the game-specific validity/scoring logic (Candidate
    /// shape, Evaluate() body) is still the developer's to write, clearly marked with TODOs.
    /// </summary>
    public class LevelForgeSetupWizardWindow : EditorWindow
    {
        [MenuItem("LevelForge/Setup Wizard...")]
        public static void Open()
        {
            var win = GetWindow<LevelForgeSetupWizardWindow>(true, "LevelForge Kurulum Sihirbazı");
            win.minSize = new Vector2(560, 480);
        }

        private static readonly string[] StepTitles =
        {
            "1. Proje Bilgisi",
            "2. Üretim Parametreleri",
            "3. Zorluk Metrikleri",
            "4. Zorluk Seviyeleri",
            "5. Önizle & Üret"
        };

        private WizardConfig config = new WizardConfig();
        private int currentStep = 0;
        private Vector2 scroll;
        private bool outputFolderManuallyEdited = false;

        private void OnGUI()
        {
            DrawStepHeader();
            EditorGUILayout.Space(8);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (currentStep)
            {
                case 0: DrawStepProjectInfo(); break;
                case 1: DrawStepParamFields(); break;
                case 2: DrawStepMetrics(); break;
                case 3: DrawStepTiers(); break;
                case 4: DrawStepPreviewAndGenerate(); break;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            DrawNavigation();
        }

        private void DrawStepHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            for (int i = 0; i < StepTitles.Length; i++)
            {
                bool isCurrent = i == currentStep;
                var prevColor = GUI.backgroundColor;
                if (isCurrent) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f, 1f);
                if (GUILayout.Button(StepTitles[i], EditorStyles.toolbarButton))
                {
                    currentStep = i;
                }
                GUI.backgroundColor = prevColor;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawNavigation()
        {
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = currentStep > 0;
            if (GUILayout.Button("◀ Geri", GUILayout.Width(80))) currentStep--;
            GUI.enabled = currentStep < StepTitles.Length - 1;
            if (GUILayout.Button("İleri ▶", GUILayout.Width(80))) currentStep++;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        // ── Adım 1 ──────────────────────────────────────────────────────
        private void DrawStepProjectInfo()
        {
            EditorGUILayout.HelpBox(
                "Bu sihirbaz, LevelForge'un jenerik 'üret → değerlendir → hedefe ulaşana kadar dene' " +
                "motorunu senin oyununa bağlayan bir kod iskeleti üretir. Şekil/kural bilgisini senden " +
                "bu adımlarda toplar, sonra 5. adımda dosyaları üretir.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            string prevName = config.gameName;
            config.gameName = EditorGUILayout.TextField("Oyun Adı", config.gameName);
            if (config.gameName != prevName && !outputFolderManuallyEdited)
            {
                config.outputFolder = WizardConfig.DefaultOutputFolder(config.gameName);
            }

            EditorGUILayout.BeginHorizontal();
            string newFolder = EditorGUILayout.TextField("Çıktı Klasörü", config.outputFolder);
            if (newFolder != config.outputFolder)
            {
                config.outputFolder = newFolder;
                outputFolderManuallyEdited = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Kısa Açıklama (opsiyonel, sadece README'ye yazılır)");
            config.description = EditorGUILayout.TextArea(config.description, GUILayout.Height(60));
        }

        // ── Adım 2 ──────────────────────────────────────────────────────
        private void DrawStepParamFields()
        {
            EditorGUILayout.HelpBox(
                "Üretimini yönlendiren sayısal/mantıksal 'knob'ları listele — ör. buz oranı, parça " +
                "boyutu, renk çeşitliliği. Bunlar LevelForge'un hedefi tutturamadığında değiştireceği " +
                "parametrelerdir (TParams).", MessageType.Info);
            EditorGUILayout.Space(6);

            for (int i = 0; i < config.paramFields.Count; i++)
            {
                var f = config.paramFields[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                f.name = EditorGUILayout.TextField("Ad", f.name);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    config.paramFields.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                f.type = (ParamFieldType)EditorGUILayout.EnumPopup("Tip", f.type);
                f.description = EditorGUILayout.TextField("Açıklama", f.description);
                if (f.type != ParamFieldType.Bool)
                {
                    EditorGUILayout.BeginHorizontal();
                    f.min = EditorGUILayout.FloatField("Min", f.min);
                    f.max = EditorGUILayout.FloatField("Max", f.max);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("+ Parametre Ekle"))
            {
                config.paramFields.Add(new ParamFieldSpec());
            }
        }

        // ── Adım 3 ──────────────────────────────────────────────────────
        private static readonly (string name, string description)[] MetricExamples =
        {
            ("moveCount", "Levelin çözümü için gereken tahmini hamle/adım sayısı"),
            ("pieceCount", "Levelde toplam kaç parça/obje olduğu"),
            ("complexity", "Aktif mekanik/kural sayısına dayalı genel karmaşıklık"),
        };

        private void DrawStepMetrics()
        {
            EditorGUILayout.HelpBox(
                "Bu adımda kod ÜRETMİYORSUN — sadece Evaluate() metodunun HANGİ SAYILARI hesaplayıp " +
                "raporlayacağını isimlendiriyorsun. 'Ad' olarak yazdığın her isim için sihirbaz " +
                "Evaluator dosyasına şöyle bir TODO satırı üretecek:\n\n" +
                "    metrics[\"<ad>\"] = 0f; // TODO: hesapla — <açıklama>\n\n" +
                "Sen daha sonra bu satırdaki 0f'i, o metriği levelinden GERÇEKTEN hesaplayan kodla " +
                "değiştireceksin (ör. pieceCount için level.pieces.Count). Metrik isimleri, 4. adımdaki " +
                "zorluk seviyelerinin metrik aralıklarıyla da eşleştirilebilir.",
                MessageType.Info);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Emin değilsen sade başla: sadece 'pieceCount' bile yeterli bir başlangıçtır — " +
                "istediğin kadar ekleyip çıkarabilirsin, bu liste sadece isim/açıklama, mantık değil.",
                MessageType.None);
            EditorGUILayout.Space(6);

            if (config.metrics.Count == 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Hızlı başlangıç:", GUILayout.Width(90));
                if (GUILayout.Button("Örnek Metrikleri Ekle (moveCount, pieceCount, complexity)"))
                {
                    foreach (var ex in MetricExamples)
                        config.metrics.Add(new MetricSpec { name = ex.name, description = ex.description });
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(6);
            }

            for (int i = 0; i < config.metrics.Count; i++)
            {
                var m = config.metrics[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                m.name = EditorGUILayout.TextField("Ad", m.name);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    config.metrics.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                if (string.IsNullOrWhiteSpace(m.name))
                {
                    EditorGUILayout.LabelField($"    ör: {MetricExamples[i % MetricExamples.Length].name}", EditorStyles.miniLabel);
                }

                m.description = EditorGUILayout.TextField("Açıklama", m.description);

                if (!string.IsNullOrWhiteSpace(m.name))
                {
                    EditorGUILayout.LabelField($"    → üretilecek satır: metrics[\"{m.name}\"] = 0f; // TODO: hesapla", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("+ Metrik Ekle"))
            {
                config.metrics.Add(new MetricSpec());
            }
        }

        // ── Adım 4 ──────────────────────────────────────────────────────
        private void DrawStepTiers()
        {
            EditorGUILayout.HelpBox(
                "Zorluk seviyelerini (Kolay/Orta/Zor/Uzman ya da kendi adlandırman) ve her birinin 0-1 " +
                "aralığındaki hedef skorunu/toleransını tanımla. Motor, üretilen bir adayın skorunu bu " +
                "hedefle karşılaştırır.", MessageType.Info);
            EditorGUILayout.Space(6);

            if (config.tiers.Count == 0 && GUILayout.Button("Varsayılan 4 Seviye Ekle (Kolay .15 / Orta .40 / Zor .65 / Uzman .85)"))
            {
                config.tiers = TierSpec.DefaultFour();
            }

            for (int i = 0; i < config.tiers.Count; i++)
            {
                var t = config.tiers[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                t.name = EditorGUILayout.TextField("Ad", t.name);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    config.tiers.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                t.targetScore = EditorGUILayout.Slider("Hedef Skor", t.targetScore, 0f, 1f);
                t.tolerance = EditorGUILayout.Slider("Tolerans", t.tolerance, 0f, 0.3f);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("+ Zorluk Seviyesi Ekle"))
            {
                config.tiers.Add(new TierSpec());
            }
        }

        // ── Adım 5 ──────────────────────────────────────────────────────
        private void DrawStepPreviewAndGenerate()
        {
            var errors = LevelForgeAdapterCodeGenerator.Validate(config);
            if (errors.Count > 0)
            {
                foreach (var e in errors)
                    EditorGUILayout.HelpBox(e, MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Üretilecek Dosyalar", EditorStyles.boldLabel);
            var files = LevelForgeAdapterCodeGenerator.GenerateFiles(config);
            foreach (var kv in files.OrderBy(k => k.Key))
            {
                int lineCount = kv.Value.Split('\n').Length;
                EditorGUILayout.LabelField($"  {config.outputFolder}/{kv.Key}", $"{lineCount} satır");
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                $"'{config.gameName}Candidate.cs' ve '{config.gameName}DifficultyEvaluator.cs' TODO'lu " +
                "iskelet olarak üretilir — geri kalanı (Params/ParameterSpace/Tiers) çalışır haldedir.",
                MessageType.Warning);

            if (GUILayout.Button("⚡ Kodu Üret", GUILayout.Height(36)))
            {
                LevelForgeAdapterCodeGenerator.WriteToDisk(config, files);
                EditorUtility.DisplayDialog("Üretildi",
                    $"{files.Count} dosya '{config.outputFolder}/' altına yazıldı.\n\n" +
                    "README_NEXT_STEPS.md dosyasını açıp kalan TODO'ları tamamla.", "Tamam");
            }
        }
    }
}
