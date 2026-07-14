using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  AI LEVEL DESIGNER  —  Yapay Zeka Destekli Seviye Oluşturucu
//  BlockMerge3D  •  BlockMerge3D / 🤖 AI Level Designer
// ═══════════════════════════════════════════════════════════════════
public class AILevelDesignerWindow : EditorWindow
{
    private enum AIDrawView { FullShape, PrefilledOnly, IceOnly, PiecesOnly }
    private enum GeneratorType { Pyramid, Castle, HelixSpiral, SphereDome, CrossStar, PromptBased }
    private enum SymmetryMode { None, X_Axis, Z_Axis, XZ_Axis }

    // ── Sabitler ─────────────────────────────────────────────────
    private const string LEVELS_PATH = "Assets/Levels";
    private const string PREF_DEFAULT_CUBE = "BlockMerge3D_DefaultCubePrefab";
    private const string PREFILLED_MATERIALS_PATH = "Assets/Materials/Premium";

    private static readonly Color COL_BG         = new Color(0.047f, 0.047f, 0.07f); // #0c0c12
    private static readonly Color COL_GRID        = new Color(0.30f, 0.32f, 0.40f); // #4d5266
    private static readonly Color COL_OCCUPIED    = new Color(0.35f, 0.78f, 1.00f); // Açık Mavi
    private static readonly Color COL_PREFILLED   = new Color(1.00f, 0.75f, 0.20f); // Warm Gold
    private static readonly Color COL_ICE         = new Color(0.50f, 0.88f, 1.00f); // Buz Mavisi
    private static readonly Color COL_HEADER      = new Color(0.95f, 0.40f, 0.70f); // AI Temalı Pembe/Magenta

    private static readonly Color[] PIECE_COLORS = new Color[]
    {
        new Color(0.95f, 0.30f, 0.30f),
        new Color(0.28f, 0.65f, 0.95f),
        new Color(0.25f, 0.88f, 0.42f),
        new Color(0.95f, 0.80f, 0.15f),
        new Color(0.72f, 0.22f, 0.92f),
        new Color(0.95f, 0.55f, 0.15f),
        new Color(0.20f, 0.88f, 0.88f),
        new Color(0.95f, 0.40f, 0.70f),
    };

    // ── AI Üretim Ayarları ─────────────────────────────────────────
    // NOT: Aşağıdaki bazı alanlar/enumlar 'internal' — LevelCreationWizardWindow bu pencerenin
    // arka planda tuttuğu bir instance'ı üzerinden bunlara doğrudan erişip mevcut üretim/export
    // mantığını (kopyalamadan) yeniden kullanıyor. Davranış AYNI, sadece görünürlük değişti.
    public enum GenerationBaseType { Template, CustomPrefab }
    internal GenerationBaseType generationBaseType = GenerationBaseType.Template;
    internal GameObject customBasePrefab;
    internal LevelTemplate selectedTemplate; // Şablon bazlı üretim
    internal string levelName        = "AI_Level_1";
    internal float levelTime         = 75f;
    internal int levelTarget         = 150;
    internal Vector3Int gridSize     = new Vector3Int(5, 5, 5);
    private float cellSize           = 1.0f;
    private float spacing            = 0.1f;

    private GeneratorType genType    = GeneratorType.Pyramid;
    private SymmetryMode symmetry    = SymmetryMode.XZ_Axis;
    private float fillDensity        = 1.0f;   // Tam dolu (default)
    private float icePercentage      = 0.10f;  // Donmuş blok oranı
    private float prefillPercentage  = 0.0f;   // Prefilled (renk çatışması riski var)

    // Parçalara Ayırma Ayarları — minPieceSize/maxPieceSize artık UI'dan seçilmiyor (tek üretim
    // yolu kütüphaneden solution-first), ama AutoAdjustAndRegenerate bunları programatik olarak
    // hâlâ ayarlıyor, bu yüzden alanlar korunuyor.
    private int minPieceSize         = 1;
    private int maxPieceSize         = 5;

    // Prompt tabanlı üretim
    private string aiPrompt          = "star with ice at base and golden corners";

    // ── Seviye Zorluk / Hızlı Ayar Ölçeği ─────────────────────────
    private int targetLevelIndex = 1;
    internal enum AILevelDifficulty { Kolay, Orta, Zor, Uzman }
    internal AILevelDifficulty selectedDifficulty = AILevelDifficulty.Kolay;
    private string levelDifficultyModeSuggestion = "Kolay";

    // ── Grid Verisi ────────────────────────────────────────────────
    internal HashSet<Vector3Int> occupiedCells  = new HashSet<Vector3Int>();
    private List<Vector3Int> prefilledCells     = new List<Vector3Int>();
    private List<int> prefilledMatIdx           = new List<int>();
    private List<Vector3Int> frozenCells        = new List<Vector3Int>();
    internal List<List<Vector3Int>> pieceSplitList = new List<List<Vector3Int>>();

    // ── Solver Sonucu ─────────────────────────────────────────────
    internal SolverResult lastSolverResult;
    internal bool solverRan = false;
    private int highlightedPieceIndex = -1;

    // ── Parça Kütüphanesi (Kutuphane_SolutionFirst modu) ────────────
    private const string PIECE_DEFINITIONS_PATH = "Assets/PieceDefinitions";
    private List<PieceDefinition> pieceLibraryCache;

    // ── UI Durumu ─────────────────────────────────────────────────
    private int activeLayer          = 0;
    private float cellPx             = 35f;
    private AIDrawView drawView      = AIDrawView.FullShape;
    private bool show3D              = true;
    private Vector2 leftScroll, rightScroll;
    private GameObject cubePrefab;
    private Material[] prefilledMaterials; // Engel (prefilled) küplerin gerçek rengini oynatan materyal paleti — pieceMaterials (LevelManager) ile aynı sırada olmalı
    private int activeTab = 0; // 0: AI Jeneratör, 1: AI Eğitim Paneli, 2: AI Parça Jeneratörü

    // ── AI Parça Yapıcı Ayarları ──────────────────────────────────
    private string pmPiecePrefix = "AI_Piece_";
    private int pmPieceCount = 4;
    private int pmMinSize = 2;
    private int pmMaxSize = 5;
    private enum PMPieceType { BFS_Free, Geometric_Rect, Symmetrical, PromptBased, ClassicTetris }
    private PMPieceType pmPieceType = PMPieceType.BFS_Free;
    private enum PMPieceDifficulty { Kolay, Orta, Zor, Uzman }
    private PMPieceDifficulty pmDifficulty = PMPieceDifficulty.Orta;
    private string pmPrompt = "L-shaped blocks";
    private int pmSelectedPieceIndex = -1;
    private List<List<Vector3Int>> pmGeneratedPieces = new List<List<Vector3Int>>();
    private List<int> pmPieceColors = new List<int>();
    private Vector2 pmLeftScroll, pmRightScroll;
    private Vector3Int pmGridSize = new Vector3Int(6, 1, 6);
    private List<AIPieceDatasetEntry> pmTaughtPieces = new List<AIPieceDatasetEntry>();
    private string pmManualPieceLabel = "my_custom_shape";
    private Vector2 pmManualScroll;
    private List<List<Vector3Int>> pmManualPiecesList = new List<List<Vector3Int>>();
    private List<string> pmManualPieceNames = new List<string>();
    private Vector2 pmManualPiecesScroll;
    private int pmRightTab = 0; // 0: AI Üretilenler, 1: Manuel Tasarımlar
    private int pmSelectedManualIndex = -1;


    // ── Stil ─────────────────────────────────────────────────────
    private GUIStyle styleHeader, styleBox, styleTabActive, styleTabInactive, styleInstructionBox;
    private bool stylesBuilt;

    public System.Action onRepaintRequested;

    new public void Repaint()
    {
        base.Repaint();
        if (onRepaintRequested != null)
            onRepaintRequested();
    }

    private void OnEnable()
    {
        // Global default cube prefab'ı yükle
        string prefabPath = EditorPrefs.GetString(PREF_DEFAULT_CUBE, "");
        if (!string.IsNullOrEmpty(prefabPath))
        {
            cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        LoadPrefilledMaterialsPalette();
        LoadManualPieceDataset();
        LoadManualLibraryFromJson();
    }



    // Prefilled (engel) küpler için Assets/Materials/Premium altındaki PremiumMaterial_N.mat
    // dosyalarını index sırasına göre yükler. Bu sıra, LevelManager.pieceMaterials ile aynı
    // olmalıdır ki matIdx hem oyun mantığında hem görsel olarak aynı rengi ifade etsin.
    private void LoadPrefilledMaterialsPalette()
    {
        var guids = AssetDatabase.FindAssets("t:Material", new[] { PREFILLED_MATERIALS_PATH });
        var mats = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(m => m != null)
            .OrderBy(m => m.name, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (mats.Length > 0) prefilledMaterials = mats;
    }

    public void OnGUI()
    {
        BuildStyles();

        // Üst Tab Seçimi (Eğitim Modu vs.)
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🤖 AI SEVİYE YAPICI", activeTab == 0 ? styleTabActive : styleTabInactive, GUILayout.Height(30)))
        {
            activeTab = 0;
        }
        if (GUILayout.Button("🧩 AI PARÇA YAPICI", activeTab == 2 ? styleTabActive : styleTabInactive, GUILayout.Height(30)))
        {
            activeTab = 2;
        }
        if (GUILayout.Button("📚 AI EĞİTİM & BİLGİ MERKEZİ", activeTab == 1 ? styleTabActive : styleTabInactive, GUILayout.Height(30)))
        {
            activeTab = 1;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        if (activeTab == 0)
        {
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawCenterGrid();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
            DrawStatusBar();
        }
        else if (activeTab == 2)
        {
            EditorGUILayout.BeginHorizontal();
            DrawPieceMakerLeftPanel();
            DrawPieceMakerCenterGrid();
            DrawPieceMakerRightPanel();
            EditorGUILayout.EndHorizontal();
            DrawPieceMakerStatusBar();
        }
        else
        {
            DrawEducationPanel();
        }
    }


    // Şablon seçimi + zorluk hızlı-ayar bloğu — DrawLeftPanel'den çıkarıldı, hem eski panel
    // hem de LevelCreationWizardWindow'un 1. adımı bu AYNI metodu çağırıyor (kopya değil).
    private void DrawStatBlock(string title, string val)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
        var titleStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.7f, 0.75f, 0.8f) }
        };
        var valueStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            normal = { textColor = new Color(0.15f, 0.60f, 0.90f) }
        };
        EditorGUILayout.LabelField(title, titleStyle);
        EditorGUILayout.LabelField(val, valueStyle);
        EditorGUILayout.EndVertical();
    }

    internal void DrawTemplateAndDifficultySection()
    {
        // ŞABLON VEYA PREFAB SEÇİCİ
        GUILayout.Label("📐 ÜRETİM KAYNAĞI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.HelpBox("AI seviyeyi bir şablon veya özel prefab baz alarak oluşturur.", MessageType.Info);

        // Custom choice cards for Production Source
        EditorGUILayout.LabelField("Üretim Kaynağı Seçimi:", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();

        bool isTemplate = (generationBaseType == GenerationBaseType.Template);
        GUI.backgroundColor = isTemplate ? new Color(0.15f, 0.60f, 0.90f) : new Color(0.24f, 0.24f, 0.28f);
        var sourceCardStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            fixedHeight = 36,
            wordWrap = true
        };
        sourceCardStyle.normal.textColor = Color.white;

        if (GUILayout.Button("📐  ŞABLON BAZLI (Template)", sourceCardStyle, GUILayout.ExpandWidth(true)))
        {
            generationBaseType = GenerationBaseType.Template;
        }

        GUILayout.Space(10);

        bool isPrefab = (generationBaseType == GenerationBaseType.CustomPrefab);
        GUI.backgroundColor = isPrefab ? new Color(0.15f, 0.60f, 0.90f) : new Color(0.24f, 0.24f, 0.28f);
        if (GUILayout.Button("🧊  ÖZEL PREFAB BAZLI (Custom)", sourceCardStyle, GUILayout.ExpandWidth(true)))
        {
            generationBaseType = GenerationBaseType.CustomPrefab;
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(8);

        if (generationBaseType == GenerationBaseType.Template)
        {
            LevelTemplate prevTemplate = selectedTemplate;
            selectedTemplate = (LevelTemplate)EditorGUILayout.ObjectField("Level Şablonu", selectedTemplate, typeof(LevelTemplate), false);

            if (selectedTemplate != prevTemplate && selectedTemplate != null)
            {
                LoadTemplateParameters();
            }

            if (selectedTemplate != null)
            {
                EditorGUILayout.LabelField($"📝 {selectedTemplate.templateName}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(selectedTemplate.description, EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("⚠️ Şablon seçilmedi. Assets/Templates/ klasöründen bir şablon seçin.", MessageType.Warning);
            }
        }
        else
        {
            GameObject prevPrefab = customBasePrefab;
            customBasePrefab = (GameObject)EditorGUILayout.ObjectField("Özel Prefab", customBasePrefab, typeof(GameObject), false);

            if (customBasePrefab != prevPrefab && customBasePrefab != null)
            {
                LoadPrefabParameters();
            }

            if (customBasePrefab == null)
            {
                EditorGUILayout.HelpBox("⚠️ Lütfen CubeShapeDataHolder içeren bir prefab seçin.", MessageType.Warning);
            }
            else if (customBasePrefab.GetComponent<CubeShapeDataHolder>() == null)
            {
                EditorGUILayout.HelpBox("⚠️ Seçilen prefab CubeShapeDataHolder bileşenine sahip değil!", MessageType.Error);
            }
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // 🏆 ZORLUK VE SEVİYE AYARLARI
        GUILayout.Label("🏆 ZORLUK VE SEVİYE AYARLARI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // 1. Zorluk Seviyesi Seçimi
        EditorGUILayout.LabelField("Zorluk Seviyesi Seçimi:", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();

        // Kolay
        bool isKolay = selectedDifficulty == AILevelDifficulty.Kolay;
        GUI.backgroundColor = isKolay ? new Color(0.18f, 0.70f, 0.40f) : new Color(0.18f, 0.70f, 0.40f, 0.25f);
        var diffCardStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            fixedHeight = 28,
            wordWrap = true
        };
        diffCardStyle.normal.textColor = isKolay ? Color.white : new Color(0.85f, 0.85f, 0.85f);
        if (GUILayout.Button("🟢  KOLAY", diffCardStyle, GUILayout.ExpandWidth(true)))
        {
            ApplyDifficultyScaleForMode(AILevelDifficulty.Kolay);
        }

        // Orta
        bool isOrta = selectedDifficulty == AILevelDifficulty.Orta;
        GUI.backgroundColor = isOrta ? new Color(0.95f, 0.65f, 0.15f) : new Color(0.95f, 0.65f, 0.15f, 0.25f);
        diffCardStyle.normal.textColor = isOrta ? Color.white : new Color(0.85f, 0.85f, 0.85f);
        if (GUILayout.Button("🟡  ORTA", diffCardStyle, GUILayout.ExpandWidth(true)))
        {
            ApplyDifficultyScaleForMode(AILevelDifficulty.Orta);
        }

        // Zor
        bool isZor = selectedDifficulty == AILevelDifficulty.Zor;
        GUI.backgroundColor = isZor ? new Color(0.88f, 0.25f, 0.25f) : new Color(0.88f, 0.25f, 0.25f, 0.25f);
        diffCardStyle.normal.textColor = isZor ? Color.white : new Color(0.85f, 0.85f, 0.85f);
        if (GUILayout.Button("🔴  ZOR", diffCardStyle, GUILayout.ExpandWidth(true)))
        {
            ApplyDifficultyScaleForMode(AILevelDifficulty.Zor);
        }

        // Uzman
        bool isUzman = selectedDifficulty == AILevelDifficulty.Uzman;
        GUI.backgroundColor = isUzman ? new Color(0.60f, 0.25f, 0.80f) : new Color(0.60f, 0.25f, 0.80f, 0.25f);
        diffCardStyle.normal.textColor = isUzman ? Color.white : new Color(0.85f, 0.85f, 0.85f);
        if (GUILayout.Button("🟣  UZMAN", diffCardStyle, GUILayout.ExpandWidth(true)))
        {
            ApplyDifficultyScaleForMode(AILevelDifficulty.Uzman);
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // 2. Seviye Seçimi
        EditorGUILayout.LabelField("🎯  Kaydedilecek Seviye Numarası (Level Index):", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("◀◀", GUILayout.Width(35), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Max(1, targetLevelIndex - 10);
            levelName = $"AI_Level_{targetLevelIndex}";
        }

        if (GUILayout.Button("◀", GUILayout.Width(25), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Max(1, targetLevelIndex - 1);
            levelName = $"AI_Level_{targetLevelIndex}";
        }

        int prevLevelIdx = targetLevelIndex;
        targetLevelIndex = EditorGUILayout.IntSlider(targetLevelIndex, 1, 100);
        if (targetLevelIndex != prevLevelIdx)
        {
            levelName = $"AI_Level_{targetLevelIndex}";
        }

        if (GUILayout.Button("▶", GUILayout.Width(25), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Min(100, targetLevelIndex + 1);
            levelName = $"AI_Level_{targetLevelIndex}";
        }

        if (GUILayout.Button("▶▶", GUILayout.Width(35), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Min(100, targetLevelIndex + 10);
            levelName = $"AI_Level_{targetLevelIndex}";
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // 3. Öneri Değerleri Önizlemesi (Dashboard Panel)
        EditorGUILayout.LabelField("📈  ÖNERİLEN PARAMETRELER", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        DrawStatBlock("⏱️ Süre Sınırı", $"{levelTime} sn");
        DrawStatBlock("⭐ Hedef Skor", $"{levelTarget}");
        DrawStatBlock("🧱 Hazır Küp", $"%{prefillPercentage * 100f:F0}");
        DrawStatBlock("❄️ Buz Küpü", $"%{icePercentage * 100f:F0}");
        DrawStatBlock("🧩 Parça Boyutu", $"{minPieceSize}-{maxPieceSize}");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        string hintText = "";
        switch (selectedDifficulty)
        {
            case AILevelDifficulty.Kolay:
                hintText = "💡 Kolay Mod: Buz blokları yok, küçük parçalar (1-3 küp) kullanılır. Süre sınırı bol tutulmuştur.";
                break;
            case AILevelDifficulty.Orta:
                hintText = "💡 Orta Zorluk: Az miktarda buz ve prefilled (renkli) bloklar eklenir. Parça boyutu 1-3 küp arasındadır.";
                break;
            case AILevelDifficulty.Zor:
                hintText = "💡 Zor Zorluk: Prefilled bloklar stratejiktir, buz oranı artırılmıştır. Parça boyutu 2-5 küp arasındadır.";
                break;
            case AILevelDifficulty.Uzman:
                hintText = "💡 Uzman Zorluk: Dar zaman limitleri, yüksek buz oranı ve karmaşık büyük parçalar (3-6 küp) bulunur.";
                break;
        }
        EditorGUILayout.HelpBox(hintText, MessageType.Info);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // KÜP PREFABI SEÇİMİ (Global)
        GUILayout.Label("🧊 GÖRSEL PARÇA YAPISI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GameObject prevCubePrefab = cubePrefab;
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Global Küp Prefabı", cubePrefab, typeof(GameObject), false);
        
        if (cubePrefab != prevCubePrefab)
        {
            if (cubePrefab != null)
            {
                string path = AssetDatabase.GetAssetPath(cubePrefab);
                EditorPrefs.SetString(PREF_DEFAULT_CUBE, path);
            }
            else
            {
                EditorPrefs.SetString(PREF_DEFAULT_CUBE, "");
            }
        }

        if (cubePrefab == null)
        {
            EditorGUILayout.HelpBox("⚠ Lütfen bir küp prefabı seçin. Seçilmezse varsayılan beyaz küp kullanılır.", MessageType.Warning);
        }
        EditorGUILayout.EndVertical();
    }

    // ── Sol Panel (Parametreler) ──────────────────────────────────
    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(420), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        DrawTemplateAndDifficultySection();

        GUILayout.Space(10);

        float originalLabelWidth = EditorGUIUtility.labelWidth;

        GUILayout.Label("GENEL AYARLAR", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUIUtility.labelWidth = 110;
        levelName   = EditorGUILayout.TextField("Seviye Adı Öneki", levelName);
        
        EditorGUILayout.BeginHorizontal();
        levelTime   = EditorGUILayout.FloatField("Süre Sınırı (sn)", levelTime);
        levelTarget = EditorGUILayout.IntField("Hedef Skor", levelTarget);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (levelTime <= 0)
        {
            EditorGUILayout.LabelField("ℹ Süresiz oyun modu aktif.", EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.LabelField($"ℹ Süre Önerisi: {levelTime} sn.", EditorStyles.miniLabel);
        }
        EditorGUILayout.LabelField($"ℹ Skor Önerisi: {levelTarget} Puan.", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        
        GUI.enabled = false; // Şablondan geldiği için değiştirilemez
        gridSize    = EditorGUILayout.Vector3IntField("Grid Boyutu (Şablon)", gridSize);
        GUI.enabled = true;
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("YAPAY ZEKA PARAMETRE AYARLARI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("AI sadece bu parametreleri ayarlar:", EditorStyles.miniBoldLabel);
        
        EditorGUILayout.BeginHorizontal();
        
        EditorGUILayout.BeginVertical();
        EditorGUIUtility.labelWidth = 110;
        prefillPercentage = EditorGUILayout.Slider("Hazır Küp Oranı", prefillPercentage, 0f, 0.4f);
        string prefillScaleText = prefillPercentage == 0f ? "Yok" :
                                  prefillPercentage <= 0.15f ? $"Orta (%{prefillPercentage*100f:F0})" :
                                  prefillPercentage <= 0.25f ? $"Zor (%{prefillPercentage*100f:F0})" :
                                  $"Uzm (%{prefillPercentage*100f:F0})";
        EditorGUILayout.LabelField($"ℹ Ölçek: {prefillScaleText}", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
        
        GUILayout.Space(10);

        EditorGUILayout.BeginVertical();
        EditorGUIUtility.labelWidth = 100;
        icePercentage = EditorGUILayout.Slider("Buz Küpü Oranı", icePercentage, 0f, 0.4f);
        string iceScaleText = icePercentage == 0f ? "Yok" :
                              icePercentage <= 0.15f ? $"Orta (%{icePercentage*100f:F0})" :
                              icePercentage <= 0.28f ? $"Zor (%{icePercentage*100f:F0})" :
                              $"Uzm (%{icePercentage*100f:F0})";
        EditorGUILayout.LabelField($"ℹ Ölçek: {iceScaleText}", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("PARÇA KÜTÜPHANESİ", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.HelpBox("🧬 Parçalar Assets/PieceDefinitions/ kütüphanesinden gelir: rastgele bir havuz seçilir ve şekil geri izlemeli (backtracking) olarak ÖNCE ÇÖZÜLMÜŞ halde inşa edilir — sonradan 'çözülür mü' diye kontrol edilmez, zaten inşa sırasında garantilidir.", MessageType.Info);
        int cachedCount = pieceLibraryCache?.Count ?? 0;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Yüklü parça sayısı: {cachedCount}", EditorStyles.miniLabel);
        if (GUILayout.Button("Kütüphaneyi Yenile", GUILayout.Width(140)))
        {
            RefreshPieceLibrary();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        GUILayout.Space(12);

        EditorGUIUtility.labelWidth = originalLabelWidth;

        if (prefilledMaterials == null || prefilledMaterials.Length == 0)
        {
            EditorGUILayout.HelpBox($"⚠ Prefilled (engel) küp renk paleti bulunamadı ({PREFILLED_MATERIALS_PATH}). Prefilled küpler pembe (varsayılan) görünecek.", MessageType.Warning);
            if (GUILayout.Button("Paleti Yeniden Yükle"))
            {
                LoadPrefilledMaterialsPalette();
            }
        }
        else
        {
            EditorGUILayout.LabelField($"ℹ Prefilled Renk Paleti: {prefilledMaterials.Length} materyal ({PREFILLED_MATERIALS_PATH})", EditorStyles.miniLabel);
        }

        GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 1f); // Magenta
        if (GUILayout.Button("⚡ BÖLÜM & PARÇALARI ÖNİZLE (YAPAY ZEKA)", GUILayout.Height(40)))
        {
            GenerateLevelProcedurally();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ── Merkez Grid Önizleme ───────────────────────────────────────
    private void DrawCenterGrid()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // Görünüm Modu
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("👁️ ÖNİZLEME MODU SEÇİN", styleHeader);
        EditorGUILayout.BeginHorizontal();
        DrawViewBtn("📌 Tüm Şekil", AIDrawView.FullShape);
        DrawViewBtn("⬛ Sadece Renkliler", AIDrawView.PrefilledOnly);
        DrawViewBtn("❄️ Sadece Buzlar", AIDrawView.IceOnly);
        DrawViewBtn("🧩 Bölünmüş Parçalar", AIDrawView.PiecesOnly);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        bool prev3D = show3D;
        show3D = GUILayout.Toolbar(show3D ? 1 : 0, new string[] { "🖥️ 2D Katman Görünümü", "🧱 3D İzometrik Görünüm" }, GUILayout.Height(24)) == 1;
        if (show3D != prev3D) Repaint();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        // Katman Değiştirici
        if (!show3D)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("📍 KATMAN ÖNİZLEME (Y Ekseninde Katman Seçin)", styleHeader);
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = activeLayer > 0;
            if (GUILayout.Button("◀ ALT KATMAN", GUILayout.Height(28), GUILayout.Width(120)))
            {
                activeLayer--;
                Repaint();
            }
            GUI.enabled = true;

            GUILayout.Space(4);

            for (int y = 0; y < gridSize.y; y++)
            {
                bool isActive = (y == activeLayer);
                GUI.backgroundColor = isActive ? COL_HEADER : new Color(0.85f, 0.85f, 0.85f);
                
                int countInLayer = occupiedCells.Count(c => c.y == y);
                string lbl = $"KATMAN Y={y}\n({countInLayer} Küp)";

                if (GUILayout.Button(lbl, new GUIStyle(GUI.skin.button) { fontSize = 10, fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal }))
                {
                    activeLayer = y;
                    Repaint();
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(4);

            GUI.enabled = activeLayer < gridSize.y - 1;
            if (GUILayout.Button("ÜST KATMAN ▶", GUILayout.Height(28), GUILayout.Width(120)))
            {
                activeLayer++;
                Repaint();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        else
        {
            EditorGUILayout.HelpBox("💡 3D İzometrik Görünüm: Tüm seviyenin üç boyutlu genel yapısını gösterir. Çözüm doğruluğu ve hacmini kolayca kontrol edebilirsiniz.", MessageType.None);
        }

        // Çizim Alanı
        Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (show3D)
        {
            DrawIsometricGrid3D(area);
        }
        else
        {
            DrawGrid2D(area);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawViewBtn(string label, AIDrawView view)
    {
        bool isActive = (drawView == view);
        GUI.backgroundColor = isActive ? COL_HEADER : new Color(0.8f, 0.8f, 0.8f);
        if (GUILayout.Button(label, GUILayout.Height(26), GUILayout.ExpandWidth(true)))
        {
            drawView = view;
            Repaint();
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawGrid2D(Rect area)
    {
        if (Event.current.type != EventType.Repaint) return;
        int W = gridSize.x, D = gridSize.z;
        float tw = cellPx * W, th = cellPx * D;
        float ox = area.x + (area.width  - tw) * 0.5f;
        float oy = area.y + (area.height - th) * 0.5f;

        EditorGUI.DrawRect(area, COL_BG);

        // Çizgiler
        for (int x = 0; x <= W; x++)
            EditorGUI.DrawRect(new Rect(ox + x * cellPx, oy, 1, th), COL_GRID);
        for (int z = 0; z <= D; z++)
            EditorGUI.DrawRect(new Rect(ox, oy + z * cellPx, tw, 1), COL_GRID);

        // Hücreler
        foreach (var cell in occupiedCells)
        {
            if (cell.y != activeLayer) continue;
            Rect cellRect = new Rect(ox + cell.x * cellPx + 1.5f, oy + cell.z * cellPx + 1.5f, cellPx - 3, cellPx - 3);

            bool isPf = prefilledCells.Contains(cell);
            bool isIce = frozenCells.Contains(cell);

            // Görünüm filtresi
            if (drawView == AIDrawView.PrefilledOnly && !isPf) continue;
            if (drawView == AIDrawView.IceOnly && !isIce) continue;

            Color fill = COL_OCCUPIED;

            if (drawView == AIDrawView.PiecesOnly)
            {
                int pIdx = GetPieceIndexForCell(cell);
                fill = pIdx >= 0 ? PIECE_COLORS[pIdx % PIECE_COLORS.Length] : new Color(0.3f, 0.3f, 0.35f);
            }
            else
            {
                if (isPf)
                {
                    int idx = prefilledCells.IndexOf(cell);
                    int mat = (idx >= 0 && idx < prefilledMatIdx.Count) ? prefilledMatIdx[idx] : 0;
                    fill = PIECE_COLORS[mat % PIECE_COLORS.Length];
                }
                else if (isIce)
                {
                    fill = COL_ICE;
                }
            }

            // Dim if highlight is active and cell is not part of highlighted piece
            bool isDimmed = false;
            if (highlightedPieceIndex >= 0)
            {
                int pIdx = GetPieceIndexForCell(cell);
                if (pIdx != highlightedPieceIndex)
                {
                    fill.a = 0.15f;
                    isDimmed = true;
                }
            }

            EditorGUI.DrawRect(cellRect, fill);

            Color originalGUIColor = GUI.color;
            if (isDimmed)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
            }

            if (isIce && cellPx >= 20 && drawView != AIDrawView.PiecesOnly)
            {
                var iceLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(cellPx * 0.50f), 9, 22)
                };
                GUI.Label(cellRect, "❄️", iceLabelStyle);
            }
            else if (isPf && cellPx >= 18 && drawView != AIDrawView.PiecesOnly)
            {
                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(cellPx * 0.40f), 8, 16),
                    fontStyle = FontStyle.Bold
                };
                labelStyle.normal.textColor = Color.white;
                GUI.Label(cellRect, "P", labelStyle);
            }
            else if (drawView == AIDrawView.PiecesOnly && cellPx >= 18)
            {
                int pIdx = GetPieceIndexForCell(cell);
                if (pIdx >= 0)
                {
                    var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = Mathf.Clamp(Mathf.RoundToInt(cellPx * 0.40f), 8, 16),
                        fontStyle = FontStyle.Bold
                    };
                    labelStyle.normal.textColor = new Color(0, 0, 0, 0.5f);
                    GUI.Label(cellRect, (pIdx + 1).ToString(), labelStyle);
                }
            }

            GUI.color = originalGUIColor;
        }

        // Eksen etiketleri
        var lbl = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = new Color(1,1,1,0.32f) } };
        for (int x = 0; x < W; x++) GUI.Label(new Rect(ox + x * cellPx, oy - 14, cellPx, 14), x.ToString(), lbl);
        for (int z = 0; z < D; z++) GUI.Label(new Rect(ox - 18, oy + z * cellPx, 18, cellPx), z.ToString(), lbl);
    }

    private void DrawIsometricGrid3D(Rect area)
    {
        if (Event.current.type != EventType.Repaint) return;

        EditorGUI.DrawRect(area, COL_BG);

        if (occupiedCells == null || occupiedCells.Count == 0)
        {
            var emptyLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };
            emptyLabelStyle.normal.textColor = Color.gray;
            GUI.Label(area, "Gösterilecek şekil yok.\n(Önizlemek için soldaki '⚡ BÖLÜM & PARÇALARI ÖNİZLE' butonuna basın)", emptyLabelStyle);
            return;
        }

        // 1. Calculate boundaries of projected centers
        float minPx = float.MaxValue, maxPx = float.MinValue;
        float minPy = float.MaxValue, maxPy = float.MinValue;

        foreach (var cell in occupiedCells)
        {
            float px = (cell.x - cell.z) * 0.866f;
            float py = (cell.x + cell.z) * 0.5f - cell.y;
            if (px < minPx) minPx = px;
            if (px > maxPx) maxPx = px;
            if (py < minPy) minPy = py;
            if (py > maxPy) maxPy = py;
        }

        float shapeWidth = maxPx - minPx;
        float shapeHeight = maxPy - minPy;

        // Add padding for cell size
        shapeWidth += 2f;
        shapeHeight += 2f;

        // Scale S
        float scaleX = (area.width - 40f) / shapeWidth;
        float scaleY = (area.height - 40f) / shapeHeight;
        float S = Mathf.Min(scaleX, scaleY);
        S = Mathf.Clamp(S, 12f, 65f); // Clamp size to prevent scaling too huge or tiny

        Vector2 areaCenter = new Vector2(area.x + area.width * 0.5f, area.y + area.height * 0.5f);
        float shapeCenterX = (minPx + maxPx) * 0.5f;
        float shapeCenterY = (minPy + maxPy) * 0.5f;

        // 2. Sort cells: Bottom-to-top (Y), then Back-to-front (X+Z)
        var sortedCells = occupiedCells
            .OrderBy(c => c.y)
            .ThenBy(c => c.x + c.z)
            .ToList();

        // 3. Draw each cell
        Handles.BeginGUI();
        foreach (var cell in sortedCells)
        {
            bool isPf = prefilledCells.Contains(cell);
            bool isIce = frozenCells.Contains(cell);

            // Visibility filters
            if (drawView == AIDrawView.PrefilledOnly && !isPf) continue;
            if (drawView == AIDrawView.IceOnly && !isIce) continue;

            Color fill = COL_OCCUPIED;

            if (drawView == AIDrawView.PiecesOnly)
            {
                int pIdx = GetPieceIndexForCell(cell);
                fill = pIdx >= 0 ? PIECE_COLORS[pIdx % PIECE_COLORS.Length] : new Color(0.3f, 0.3f, 0.35f);
            }
            else
            {
                if (isPf)
                {
                    int idx = prefilledCells.IndexOf(cell);
                    int mat = (idx >= 0 && idx < prefilledMatIdx.Count) ? prefilledMatIdx[idx] : 0;
                    fill = PIECE_COLORS[mat % PIECE_COLORS.Length];
                }
                else if (isIce)
                {
                    fill = COL_ICE;
                }
            }

            // Dim if highlight is active and cell is not part of highlighted piece
            bool isDimmed = false;
            if (highlightedPieceIndex >= 0)
            {
                int pIdx = GetPieceIndexForCell(cell);
                if (pIdx != highlightedPieceIndex)
                {
                    fill.a = 0.12f;
                    isDimmed = true;
                }
            }

            // Calculate center
            float px = (cell.x - cell.z) * 0.866f;
            float py = (cell.x + cell.z) * 0.5f - cell.y;

            float cx = areaCenter.x + (px - shapeCenterX) * S;
            float cy = areaCenter.y + (py - shapeCenterY) * S;

            DrawIsometricCube(new Vector2(cx, cy), S, fill, isIce, isPf && drawView != AIDrawView.PiecesOnly, isDimmed);
        }
        Handles.EndGUI();
    }

    private void DrawIsometricCube(Vector2 center, float S, Color color, bool isIce, bool isPf, bool isDimmed)
    {
        float cx = center.x;
        float cy = center.y;

        Vector3[] vTop = new Vector3[]
        {
            new Vector3(cx, cy - S * 0.5f, 0),
            new Vector3(cx + S * 0.866f, cy - S * 0.25f, 0),
            new Vector3(cx, cy, 0),
            new Vector3(cx - S * 0.866f, cy - S * 0.25f, 0)
        };

        Vector3[] vLeft = new Vector3[]
        {
            new Vector3(cx - S * 0.866f, cy - S * 0.25f, 0),
            new Vector3(cx, cy, 0),
            new Vector3(cx, cy + S, 0),
            new Vector3(cx - S * 0.866f, cy + S * 0.75f, 0)
        };

        Vector3[] vRight = new Vector3[]
        {
            new Vector3(cx, cy, 0),
            new Vector3(cx + S * 0.866f, cy - S * 0.25f, 0),
            new Vector3(cx + S * 0.866f, cy + S * 0.75f, 0),
            new Vector3(cx, cy + S, 0)
        };

        // Shading colors
        Color cTop = color;
        Color cLeft = color * 0.82f;
        Color cRight = color * 0.65f;

        // Ensure alpha is preserved
        cTop.a = color.a;
        cLeft.a = color.a;
        cRight.a = color.a;

        // Draw faces
        Handles.color = cTop;
        Handles.DrawAAConvexPolygon(vTop);
        Handles.color = cLeft;
        Handles.DrawAAConvexPolygon(vLeft);
        Handles.color = cRight;
        Handles.DrawAAConvexPolygon(vRight);

        // Draw thin wire outlines to prevent alias gaps
        Handles.color = new Color(0, 0, 0, isDimmed ? 0.05f : 0.25f);
        Handles.DrawPolyLine(vTop[0], vTop[1], vTop[2], vTop[3], vTop[0]);
        Handles.DrawPolyLine(vLeft[0], vLeft[1], vLeft[2], vLeft[3], vLeft[0]);
        Handles.DrawPolyLine(vRight[0], vRight[1], vRight[2], vRight[3], vRight[0]);

        // Draw overlays on top face
        if (isDimmed) return;

        if (isIce && S >= 18f)
        {
            var iceLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(S * 0.45f), 9, 22)
            };
            Rect labelRect = new Rect(cx - S * 0.5f, cy - S * 0.5f, S, S * 0.5f);
            GUI.Label(labelRect, "❄️", iceLabelStyle);
        }
        else if (isPf && S >= 14f)
        {
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(S * 0.35f), 8, 16),
                fontStyle = FontStyle.Bold
            };
            labelStyle.normal.textColor = Color.white;
            Rect labelRect = new Rect(cx - S * 0.5f, cy - S * 0.45f, S, S * 0.4f);
            GUI.Label(labelRect, "P", labelStyle);
        }
    }

    private int GetPieceIndexForCell(Vector3Int cell)
    {
        for (int i = 0; i < pieceSplitList.Count; i++)
        {
            if (pieceSplitList[i].Contains(cell)) return i;
        }
        return -1;
    }

    // Çözülebilirlik analizi kutusu — DrawRightPanel'den çıkarıldı, hem eski panel hem de
    // LevelCreationWizardWindow'un 3. adımı bu AYNI metodu çağırıyor (kopya değil).
    internal void DrawSolverResultSection()
    {
        if (!(solverRan && lastSolverResult != null)) return;

        GUI.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1.0f);
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUI.backgroundColor = Color.white;

        GUILayout.Label("🔍  ÇÖZÜLEBİLİRLİK DOĞRULAMA (SOLVER)", styleHeader);
        EditorGUILayout.Space(4);

        if (lastSolverResult.isSolvable)
        {
            // Solvable layout: Green background tint box
            GUI.backgroundColor = new Color(0.18f, 0.70f, 0.40f, 0.12f);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;

            var solverSuccessStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.18f, 0.70f, 0.40f) }
            };
            GUILayout.Label("✅  BAŞARILI: SEVİYE ÇÖZÜLEBİLİR DURUMDA!", solverSuccessStyle);
            
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            
            // Move count card
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(130));
            var statTitleStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            var statValStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 13, normal = { textColor = new Color(0.15f, 0.60f, 0.90f) } };
            GUILayout.Label("En Kısa Yol", statTitleStyle);
            GUILayout.Label($"{lastSolverResult.minMoveCount} Hamle", statValStyle);
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);

            // Difficulty card
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(150));
            GUILayout.Label("Hesaplanan Zorluk", statTitleStyle);
            string diffText = string.IsNullOrEmpty(lastSolverResult.difficultyLabel) ? "Bilinmiyor" : lastSolverResult.difficultyLabel.ToUpper();
            GUILayout.Label($"{diffText} ({lastSolverResult.difficultyScore:F2})", statValStyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        else
        {
            // Unsolvable layout: Red background tint box
            GUI.backgroundColor = new Color(0.88f, 0.25f, 0.25f, 0.12f);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;

            var solverFailStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.88f, 0.25f, 0.25f) }
            };
            GUILayout.Label("❌  HATA: SEVİYE ÇÖZÜLEMEZ DURUMDA!", solverFailStyle);
            
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(lastSolverResult.failureReason, MessageType.Error);
            
            EditorGUILayout.Space(6);
            GUI.backgroundColor = new Color(0.95f, 0.65f, 0.15f, 1.0f); // Accent yellow/orange
            var retryStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            if (GUILayout.Button("🔁  Parametreleri İyileştir & Yeniden Üret", retryStyle, GUILayout.Height(28)))
            {
                AutoAdjustAndRegenerate();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();
    }

    // ── Sağ Panel (Dışa Aktarma) ──────────────────────────────────
    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(190), GUILayout.ExpandHeight(true));
        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        GUILayout.Label("DIŞA AKTAR VE DENE", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("İstatistikler:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Toplam Küp Sayısı: {occupiedCells.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Hazır (Renkli) Küpler: {prefilledCells.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Dondurulmuş Küpler: {frozenCells.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Otomatik Parça Sayısı: {pieceSplitList.Count}", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // OLUŞTURULAN PARÇALAR (Her zaman görünür, parça yoksa boş durum mesajı verir)
        GUILayout.Label("🧩 OLUŞTURULAN PARÇALAR", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        if (pieceSplitList == null || pieceSplitList.Count == 0)
        {
            EditorGUILayout.LabelField("Henüz parça üretilmedi.\n\nSoldaki 'BÖLÜM & PARÇALARI ÖNİZLE' butonuna basarak seviye ve parçaları önizleyebilirsiniz.", EditorStyles.wordWrappedMiniLabel);
        }
        else
        {
            for (int i = 0; i < pieceSplitList.Count; i++)
            {
                var piece = pieceSplitList[i];
                if (piece == null || piece.Count == 0) continue;

                Color col = PIECE_COLORS[i % PIECE_COLORS.Length];

                // If this piece is highlighted, paint the box background slightly Magenta/Header color
                Color prevBG = GUI.backgroundColor;
                if (highlightedPieceIndex == i)
                {
                    GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 0.6f);
                }
                
                Rect pieceClickRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = prevBG;

                EditorGUILayout.BeginHorizontal();
                Rect colorRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12), GUILayout.Height(12));
                colorRect.y += 2;
                EditorGUI.DrawRect(colorRect, col);
                GUILayout.Space(4);
                
                var itemLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                if (highlightedPieceIndex == i) itemLabelStyle.normal.textColor = Color.white;
                EditorGUILayout.LabelField($"Parça {i + 1} ({piece.Count} Blok, Y={piece[0].y})", itemLabelStyle);
                EditorGUILayout.EndHorizontal();

                // Draw miniature 2D preview
                int minX = piece.Min(c => c.x);
                int maxX = piece.Max(c => c.x);
                int minZ = piece.Min(c => c.z);
                int maxZ = piece.Max(c => c.z);

                int w = maxX - minX + 1;
                int h = maxZ - minZ + 1;

                float previewBlockSize = 14f; // Increased from 8f to 14f for a much better view!
                float previewWidth = w * previewBlockSize;
                float previewHeight = h * previewBlockSize;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(18); // Indent to align with the text above
                Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.Width(previewWidth), GUILayout.Height(previewHeight));
                EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.15f));

                foreach (var cell in piece)
                {
                    int rx = cell.x - minX;
                    int rz = cell.z - minZ;
                    Rect blockRect = new Rect(
                        previewRect.x + rx * previewBlockSize + 0.5f,
                        previewRect.y + rz * previewBlockSize + 0.5f,
                        previewBlockSize - 1f,
                        previewBlockSize - 1f
                    );
                    EditorGUI.DrawRect(blockRect, col);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();

                // Detect click on this helpbox rect
                if (Event.current.type == EventType.MouseDown && pieceClickRect.Contains(Event.current.mousePosition))
                {
                    if (highlightedPieceIndex == i)
                    {
                        highlightedPieceIndex = -1;
                    }
                    else
                    {
                        highlightedPieceIndex = i;
                        drawView = AIDrawView.PiecesOnly; // Auto-switch view to pieces view!
                    }
                    Repaint();
                    Event.current.Use();
                }

                GUILayout.Space(4);
            }
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        DrawSolverResultSection();

        GUILayout.Space(12);

        // Zorunlu Koruma Kuralı #1: doğrulanmamış (validated == false) hiçbir seviye
        // kaydedilemez. Solver hiç çalışmadıysa, sonuç yoksa veya çözülemez bulduysa buton
        // tamamen devre dışı — ExportProceduralLevelCore de aynı kontrolü tekrar yapıyor
        // (savunmacı: buton her nasılsa aktifleşse bile kayıt yine reddedilir).
        bool isValidatedSolvable = solverRan && lastSolverResult != null && lastSolverResult.isSolvable;
        EditorGUI.BeginDisabledGroup(!isValidatedSolvable);
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f, 1f); // Doygun Yeşil
        if (GUILayout.Button("💾 SEVİYEYİ TAMAMEN OLUŞTUR\n(BÖLÜMÜ KAYDET)", GUILayout.Height(50)))
        {
            ExportProceduralLevel();
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();
        if (!isValidatedSolvable)
        {
            EditorGUILayout.HelpBox("Kaydetmeden önce seviye solver tarafından ÇÖZÜLEBİLİR olarak doğrulanmalı. Önce '⚡ BÖLÜM & PARÇALARI ÖNİZLE' ile üretip yeşil '✅ Çözülebilir' sonucunu bekleyin.", MessageType.Warning);
        }

        GUILayout.Space(15);
        
        GUILayout.Label("HIZLI EĞİTİM NOTU", styleHeader);
        EditorGUILayout.HelpBox("Yapay Zeka ile oluşturduğunuz seviyeler otomatik olarak hem ana şekil hem de parçalar olarak kaydedilir ve birbirine bağlanır. Bu sayede elle parça tasarımı yapmanız gerekmez.", MessageType.Info);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(22));
        EditorGUILayout.LabelField($"BlockMerge3D  •  AI Seviye Jeneratörü  •  Durum: Hazır", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ── AI Eğitim & Bilgi Merkezi ────────────────────────────────
    private void DrawEducationPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        GUILayout.Label("📚 YAPAY ZEKA SEVİYE DÜZENLEME EĞİTİM MERKEZİ", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter });
        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(styleInstructionBox);
        GUILayout.Label("🤖 YAPAY ZEKA TOPLU EĞİTİM MODU (DATASET GENERATOR)", styleHeader);
        GUILayout.Label("Bu mod, yapay zekanın eğitimi için 10 adet tamamen optimize edilmiş, zorluk seviyeleri dengeli 3D bulmaca seviyesini tek tıkla üretir. " +
                      "Bu seviyeler oyuna otomatik olarak yüklenecek ve ayrıca dışa aktarılan JSON dosyası sayesinde yapay zekanın seviye yapısını ve çözümlerini öğrenmesini sağlayacaktır.", EditorStyles.wordWrappedLabel);
        
        GUILayout.Space(8);
        GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 1f); // AI Magenta
        if (GUILayout.Button("⚡ 10 ADET OPTİMİZE SEVİYE ÜRET & JSON EĞİTİM VERİSİ YAZ", GUILayout.Height(40)))
        {
            GenerateAndExportAIBatchDataset();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(styleInstructionBox);
        GUILayout.Label("1. SEVİYE VERİ YAPISI NASIL ÇALIŞIR?", EditorStyles.boldLabel);
        GUILayout.Label("BlockMerge3D projesinde seviyeler 3 bileşenden oluşur:\n" +
                      " • LevelData (ScriptableObject): Seviyenin süre sınırı, hedef puanı, ana şekil prefabı ve parçalarını yönetir.\n" +
                      " • Ana Şekil (_FullShape Prefab): Grid içinde oyuncunun birleştireceği ana şekli ve onun üzerindeki dondurulmuş / renkli blokların koordinatlarını saklar.\n" +
                      " • Complementary Pieces (Parça Prefabları): Oyuncunun sahneye sürükleyeceği, ana şekilden koparılmış parçaları modeller.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(styleInstructionBox);
        GUILayout.Label("2. ALGORİTMİK YAPAY ZEKA NASIL KULLANILIR?", EditorStyles.boldLabel);
        GUILayout.Label("AI Level Designer, seçilen Level Şablonu üzerinden çalışır ve buna prosedürel engel/parça mantığı ekler:\n" +
                      " • Şekil: Şablon dolu hücrelere sahipse o elle tasarlanmış şekil kullanılır; boşsa (ör. hazır 'tam küp' şablonları) Grid Boyutu kadar düz dolu bir kutu üretilir.\n" +
                      " • Doluluk Yoğunluğu: Şeklin ne kadarının dolu olacağını seçer. Düşük yoğunluklar daha oyuk/delikli şekiller üretir.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(styleInstructionBox);
        GUILayout.Label("3. DONDURULMUŞ VE RENKLİ BLOK YERLEŞTİRME KURALLARI", EditorStyles.boldLabel);
        GUILayout.Label(" • Dondurulmuş Bloklar (Buz): Oyuncu bu hücrelerin üzerine blok koyduğunda, katman temizlenmesi için önce buzun kırılması gerekir. AI, buzu stratejik olarak taban katmanlara veya şekil dış çeperine yerleştirir.\n" +
                      " • Renkli Bloklar (Prefilled): Seviye başında ızgaraya yerleştirilen sabit bloklardır. Bunlar seviyenin renk temasını belirler. AI, renk bütünlüğünü korumak adına aynı renkteki blokları yan yana dizmeye çalışır.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(styleInstructionBox);
        GUILayout.Label("4. PARÇALARA OTOMATİK BÖLME ALGORİTMASI (INTELLIGENT AUTO-SPLITTER)", EditorStyles.boldLabel);
        GUILayout.Label("Bir 3D bulmaca seviyesini elle parçalara ayırmak oldukça zordur. AI, bunu yapabilmek için BFS (Genişlik Öncelikli Arama) tabanlı kümeleme kullanır:\n" +
                      " 1. Şekildeki atanmamış bloklardan rastgele bir başlangıç noktası (seed) seçer.\n" +
                      " 2. Komşularını tarayarak (6-yönlü 3D komşuluk) parçayı hedef boyuta (örneğin 4 blok) ulaşana kadar büyütür.\n" +
                      " 3. İşlemi tüm bloklar atanana kadar tekrarlar.\n" +
                      " 4. Kenarda tek kalan veya küçük parçaları, en yakınındaki büyük parçaya ekleyerek bütünlüğü korur.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(styleInstructionBox);
        GUILayout.Label("🤖 YAPAY ZEKA PARÇA EĞİTİM MODU (PIECE DATASET GENERATOR)", styleHeader);
        GUILayout.Label("Bu mod, yapay zekanın eğitimi için 50 adet farklı geometride, zorluklarda ve promptlara uygun puzzle parçasını tek tıkla üretir. " +
                      "Bu parçalar kaydedilecek ve ayrıca dışa aktarılan JSON dosyası sayesinde yapay zekanın parça yapılarını, geometrik özelliklerini (kompaktlık, simetri, köşe sayısı vb.) öğrenmesini sağlayacaktır.", EditorStyles.wordWrappedLabel);
        
        GUILayout.Space(8);
        GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 1f); // AI Magenta
        if (GUILayout.Button("⚡ 50 ADET BENZERSİZ PARÇA ÜRET & JSON EĞİTİM VERİSİ YAZ", GUILayout.Height(40)))
        {
            GenerateAndExportPieceDataset();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(styleInstructionBox);
        GUILayout.Label("5. PARÇA GEOMETRİSİ VE ANALİZ NOTLARI (COMPACTNESS & SYMMETRY)", EditorStyles.boldLabel);
        GUILayout.Label("Yapay zeka parça oluştururken geometrik özelliklerini şu kriterlere göre ölçer:\n" +
                      " • Yoğunluk (Compactness): Parça hücre sayısının kapladığı çevreleyen kutunun (bounding box) alanına oranıdır. %100 oranındaki şekiller tam dolu kutudur. Düşük oranlar daha karmaşık/delikli şekillerdir.\n" +
                      " • Simetri: Parçanın X veya Z eksenine göre yansıtıldığında kendisiyle birebir çakışma durumudur. Simetrik parçaların çözülmesi genelde daha öngörülebilirdir.\n" +
                      " • Sınıflandırma: Şeklin morfolojisine göre L-Şekli, T-Şekli, Düz Çubuk, Kompakt Kutu veya Karmaşık olarak kategorize edilmesidir.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(styleInstructionBox);
        GUILayout.Label("6. PROMPT TABANLI ŞEKİL ÜRETİM MODELİ (PROMPT-BASED GENERATOR)", EditorStyles.boldLabel);
        GUILayout.Label("Yapay Zeka prompt içindeki 'l-shaped', 'flat line', 't-shaped plus', 'stair step zigzag', 'compact box' gibi anahtar kelimeleri algılar:\n" +
                      " • Algılanan kelimeye göre ilgili geometrik algoritma tetiklenir (örn. L-şeklinde 90 derecelik köşe dönüşü ekleme).\n" +
                      " • Boyutlar ve hücre yerleşimleri bu kısıtlamalar dahilinde rastgeleleştirilerek kurallara uyan benzersiz varyasyonlar üretilir.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ══ ALGORİTMİK YAPAY ZEKA METODLARI ═════════════════════════

    internal void GenerateLevelProcedurally()
    {
        // Kaynak kontrolü
        if (generationBaseType == GenerationBaseType.Template && selectedTemplate == null)
        {
            EditorUtility.DisplayDialog("Hata", "Lütfen önce bir Level Şablonu seçin (Assets/Templates/)", "Tamam");
            return;
        }
        else if (generationBaseType == GenerationBaseType.CustomPrefab)
        {
            if (customBasePrefab == null || customBasePrefab.GetComponent<CubeShapeDataHolder>() == null)
            {
                EditorUtility.DisplayDialog("Hata", "Lütfen CubeShapeDataHolder içeren geçerli bir Prefab seçin", "Tamam");
                return;
            }
        }

        occupiedCells.Clear();
        prefilledCells.Clear();
        prefilledMatIdx.Clear();
        frozenCells.Clear();
        pieceSplitList.Clear();
        highlightedPieceIndex = -1;

        // 1. Kaynaktan Grid'i Yükle
        int W = gridSize.x;
        int H = gridSize.y;
        int D = gridSize.z;

        if (generationBaseType == GenerationBaseType.Template)
        {
            // Şablon boşsa (occupiedCells listesi boşsa) = tam dolu küp
            if (selectedTemplate.occupiedCells == null || selectedTemplate.occupiedCells.Count == 0)
            {
                BuildSolidBoxShape(W, H, D);
                Debug.Log($"✅ Şablon '{selectedTemplate.templateName}' - Tam dolu {W}x{H}x{D} küp: {occupiedCells.Count} blok");
            }
            else
            {
                // Şablondan hücreleri kopyala
                occupiedCells = new HashSet<Vector3Int>(selectedTemplate.occupiedCells);
                Debug.Log($"✅ Şablon '{selectedTemplate.templateName}' yüklendi: {occupiedCells.Count} blok");
            }
        }
        else
        {
            var holder = customBasePrefab.GetComponent<CubeShapeDataHolder>();
            if (holder.occupiedCells == null || holder.occupiedCells.Count == 0)
            {
                BuildSolidBoxShape(W, H, D);
                Debug.Log($"✅ Prefab '{customBasePrefab.name}' - Tam dolu {W}x{H}x{D} küp: {occupiedCells.Count} blok");
            }
            else
            {
                occupiedCells = new HashSet<Vector3Int>(holder.occupiedCells);
                Debug.Log($"✅ Prefab '{customBasePrefab.name}' yüklendi: {occupiedCells.Count} blok");
            }
        }

        // Boş şekil koruması
        if (occupiedCells.Count == 0)
        {
            occupiedCells.Add(new Vector3Int(W / 2, 0, D / 2));
        }

        ApplyObstaclesAndSplitPieces(W, H, D);

        activeLayer = 0;
        Repaint();
        Debug.Log($"🤖 Yapay Zeka: '{levelName}' seviyesi procedurally oluşturuldu. Küp: {occupiedCells.Count}, Parça: {pieceSplitList.Count}");
    }

    // occupiedCells'i W×H×D'lik tam dolu bir kutuyla doldurur. Şablon seçilmediği/boş olduğu
    // durumlarda (bkz. yukarısı) ve şablon gerektirmeyen toplu üretimde (bkz.
    // GenerateAndExportAIBatchDataset) kullanılan tek, ortak "düz kutu" üretim yolu.
    private void BuildSolidBoxShape(int W, int H, int D)
    {
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                for (int z = 0; z < D; z++)
                    occupiedCells.Add(new Vector3Int(x, y, z));
    }

    // occupiedCells zaten dolduktan sonra: buz/prefilled dağıtımı + akıllı parça bölme.
    // GenerateLevelProcedurally ve GenerateAndExportAIBatchDataset tarafından ortak kullanılır.
    private void ApplyObstaclesAndSplitPieces(int W, int H, int D)
    {
        // 2. AI Parametreleri: Renkli Küpler (Prefilled) ve Buzları Dağıt
        List<Vector3Int> finalOccupied = occupiedCells.ToList();
        int targetPrefillCount = Mathf.RoundToInt(finalOccupied.Count * prefillPercentage);
        int targetIceCount = Mathf.RoundToInt(finalOccupied.Count * icePercentage);

        // Karıştır
        finalOccupied = finalOccupied.OrderBy(x => Random.value).ToList();

        // Buzları dağıt (buzlar prefilled olamaz)
        int iceDone = 0;
        foreach (var cell in finalOccupied)
        {
            if (iceDone >= targetIceCount) break;

            // Strateji: Buzlar genellikle daha alt katmanlarda veya kenarlarda olsun
            bool strategicPlace = (cell.y <= H / 2) || (cell.x == 0 || cell.x == W - 1 || cell.z == 0 || cell.z == D - 1);
            if (strategicPlace || Random.value < 0.5f)
            {
                frozenCells.Add(cell);
                iceDone++;
            }
        }

        // Hazır renkli blokları dağıt (buz olanlara renk atanmaz)
        // Önemli: Bir katman (Y) sadece tek bir renk olduğunda temizlenebiliyor
        // (bkz. GridManager/LevelSolver: katman tamamen dolduğunda tüm hücreler aynı
        // materyalde olmalı). Eskiden renk "(x + y) % 6" ile hücre bazında seçiliyordu;
        // bu da aynı katmanda x'e göre farklı renkte prefilled hücreler üretip o katmanı
        // -hiçbir parça diziliminde- çözülemez hale getirebiliyordu (özellikle Uzman
        // modda prefill oranı yüksek olduğu için sık rastlanıyordu). Bu yüzden renk artık
        // katman başına bir kere seçiliyor.
        Dictionary<int, int> layerColorIdx = new Dictionary<int, int>();
        int prefilledDone = 0;
        foreach (var cell in finalOccupied)
        {
            if (prefilledDone >= targetPrefillCount) break;
            if (frozenCells.Contains(cell)) continue;

            if (!layerColorIdx.TryGetValue(cell.y, out int colorIdx))
            {
                colorIdx = Random.Range(0, 6); // PREFILL_COLORS boyutu 6
                layerColorIdx[cell.y] = colorIdx;
            }

            prefilledCells.Add(cell);
            prefilledMatIdx.Add(colorIdx);
            prefilledDone++;
        }

        // 3. Akıllı Parça Üretimi: Birden fazla strateji dene, en iyisini seç
        SmartPieceSplitting();
    }

    private bool EvaluateShapeFormula(Vector3Int c, int W, int H, int D)
    {
        float cx = W / 2f - 0.5f;
        float cy = 0f; // Tabandan başlasın
        float cz = D / 2f - 0.5f;

        float dx = c.x - cx;
        float dy = c.y - cy;
        float dz = c.z - cz;

        switch (genType)
        {
            case GeneratorType.Pyramid:
                // Piramit: Yukarı çıkıldığında genişlik azalır
                int maxDist = Mathf.Max(Mathf.Abs(Mathf.RoundToInt(dx)), Mathf.Abs(Mathf.RoundToInt(dz)));
                return (maxDist < (H - c.y));

            case GeneratorType.Castle:
                // Kale: Köşelerde kuleler, ortalarda surlar
                bool isCorner = (c.x == 0 || c.x == W - 1) && (c.z == 0 || c.z == D - 1);
                if (isCorner) return true; // Köşe sütunları tam dolu
                // Surlar (Y=0 ve Y=1'de duvarlar, Y=2'de dişler)
                if (c.y < 2 && (c.x == 0 || c.x == W - 1 || c.z == 0 || c.z == D - 1)) return true;
                if (c.y == 2 && (c.x == 0 || c.x == W - 1 || c.z == 0 || c.z == D - 1) && (c.x % 2 == 0 && c.z % 2 == 0)) return true;
                return false;

            case GeneratorType.HelixSpiral:
                // Spiral: Yükseldikçe açı dönen tek yönlü basamaklar
                float angle = c.y * (Mathf.PI * 0.5f); // Her katmanda 90 derece dön
                float targetX = cx + Mathf.Cos(angle) * (W / 2.5f);
                float targetZ = cz + Mathf.Sin(angle) * (D / 2.5f);
                float dFromTarget = Mathf.Sqrt(Mathf.Pow(c.x - targetX, 2) + Mathf.Pow(c.z - targetZ, 2));
                return (dFromTarget < 1.2f);

            case GeneratorType.SphereDome:
                // Küre/Kubbe: Yarıçapa göre mesafe hesabı
                float r = Mathf.Min(W, D) * 0.5f;
                float dist = Mathf.Sqrt(dx*dx + dy*dy + dz*dz);
                return (dist <= r);

            case GeneratorType.CrossStar:
                // Yıldız/Artı: Eksenler boyunca uzanan kollar
                bool onXAxis = Mathf.Abs(dx) < 1f;
                bool onZAxis = Mathf.Abs(dz) < 1f;
                return (onXAxis || onZAxis);
        }
        return true;
    }

    private void GeneratePromptBasedShape(int W, int H, int D)
    {
        string prompt = aiPrompt.ToLower();
        float cx = W / 2f - 0.5f;
        float cz = D / 2f - 0.5f;

        // Basit NLP kelime eşleşmesi
        bool wantsHollow = prompt.Contains("hollow") || prompt.Contains("oyuk") || prompt.Contains("bos");
        bool wantsPyramid = prompt.Contains("pyramid") || prompt.Contains("piramit");
        bool wantsStar = prompt.Contains("star") || prompt.Contains("yildiz") || prompt.Contains("cross") || prompt.Contains("arti");
        bool wantsTower = prompt.Contains("tower") || prompt.Contains("kule");

        for (int x = 0; x < W; x++)
        {
            for (int y = 0; y < H; y++)
            {
                for (int z = 0; z < D; z++)
                {
                    float dx = x - cx;
                    float dy = y;
                    float dz = z - cz;
                    float distFromCenter = Mathf.Sqrt(dx*dx + dz*dz);

                    bool shapeMatch = false;

                    if (wantsPyramid)
                    {
                        int maxDist = Mathf.Max(Mathf.Abs(Mathf.RoundToInt(dx)), Mathf.Abs(Mathf.RoundToInt(dz)));
                        shapeMatch = (maxDist < (H - y));
                    }
                    else if (wantsStar)
                    {
                        shapeMatch = (Mathf.Abs(dx) < 1f || Mathf.Abs(dz) < 1f);
                    }
                    else if (wantsTower)
                    {
                        shapeMatch = (distFromCenter < 1.8f);
                    }
                    else
                    {
                        // Varsayılan kutu
                        shapeMatch = true;
                    }

                    // Oyukluk filtresi
                    if (wantsHollow && shapeMatch)
                    {
                        // Kenarlarda değilse ve ortadaysa oy
                        bool isInternal = (x > 0 && x < W - 1) && (y > 0 && y < H - 1) && (z > 0 && z < D - 1);
                        if (isInternal) shapeMatch = false;
                    }

                    if (shapeMatch)
                    {
                        occupiedCells.Add(new Vector3Int(x, y, z));
                    }
                }
            }
        }
    }

    private void ApplySymmetry(int W, int H, int D)
    {
        if (symmetry == SymmetryMode.None) return;

        List<Vector3Int> current = occupiedCells.ToList();
        foreach (var c in current)
        {
            if (symmetry == SymmetryMode.X_Axis || symmetry == SymmetryMode.XZ_Axis)
            {
                // X eksenine göre yansıt
                var mirrorX = new Vector3Int(W - 1 - c.x, c.y, c.z);
                occupiedCells.Add(mirrorX);
            }
            if (symmetry == SymmetryMode.Z_Axis || symmetry == SymmetryMode.XZ_Axis)
            {
                // Z eksenine göre yansıt
                var mirrorZ = new Vector3Int(c.x, c.y, D - 1 - c.z);
                occupiedCells.Add(mirrorZ);
            }
            if (symmetry == SymmetryMode.XZ_Axis)
            {
                // Hem X hem Z
                var mirrorBoth = new Vector3Int(W - 1 - c.x, c.y, D - 1 - c.z);
                occupiedCells.Add(mirrorBoth);
            }
        }
    }

    // ── Parça Kütüphanesi (Faz 3 — Solution-First) ─────────────────────

    // internal: LevelCreationWizardWindow, 2. adımda kütüphaneyi migrate ettikten sonra
    // önbelleği zorla tazelemek için bunu çağırıyor (aksi halde stale sayım gösterilir).
    internal void RefreshPieceLibrary()
    {
        pieceLibraryCache = null;
        LoadPieceLibrary();
    }

    internal List<PieceDefinition> LoadPieceLibrary()
    {
        if (pieceLibraryCache != null) return pieceLibraryCache;

        pieceLibraryCache = new List<PieceDefinition>();
        if (!AssetDatabase.IsValidFolder(PIECE_DEFINITIONS_PATH)) return pieceLibraryCache;

        var guids = AssetDatabase.FindAssets("t:PieceDefinition", new[] { PIECE_DEFINITIONS_PATH });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var def = AssetDatabase.LoadAssetAtPath<PieceDefinition>(path);
            if (def == null || def.cells == null || def.cells.Count == 0) continue;

            // [2026-07-14] DÜZELTİLDİ: kütüphanede, hiçbir rotasyonda TEK katmana
            // yassılamayan (gerçek 3D hacimli, örn. tam dolu 3x3x2/4x4x4 gibi) 8 tanım
            // bulundu — bunlar muhtemelen eski bir migration'da yanlışlıkla "parça" diye
            // kaydedilmiş komple SEVİYE şekilleri. SolutionFirstBuilder/LevelSolver katman-
            // katman (tek Y) yerleştirme yaptığı için bunlar HİÇBİR ZAMAN yerleştirilemiyor —
            // havuz örneklemesinde (SplitShapeWithSolutionFirstLibrary) sadece ölü ağırlık
            // olup gerçek/küçük parçaların örneklenme şansını düşürüyorlardı (bu da tekli-küp
            // Filler_1x1'in aşırı öne çıkmasının nedenlerinden biriydi). Hiçbir rotasyonu tek
            // katmana sığmayan tanımlar kütüphaneden (ve dolayısıyla örneklemeden) çıkarılıyor.
            if (!IsFlattenableToSingleLayer(def))
                continue;

            pieceLibraryCache.Add(def);
        }
        return pieceLibraryCache;
    }

    // def.cells'in, allowedRotations'daki rotasyonlardan EN AZ biriyle tek bir Y katmanına
    // sığıp sığmadığını kontrol eder (tüm hücreler aynı Y'yi paylaşıyor mu). allowedRotations
    // boşsa sadece kimlik rotasyonu (rotasyonsuz hâli) denenir.
    private static bool IsFlattenableToSingleLayer(PieceDefinition def)
    {
        var rotations = (def.allowedRotations != null && def.allowedRotations.Count > 0)
            ? def.allowedRotations
            : new List<Vector3Int> { Vector3Int.zero };

        foreach (var rotEuler in rotations)
        {
            var rotated = PieceGeometryUtils.RotateAndNormalize(
                def.cells, Quaternion.Euler(rotEuler.x, rotEuler.y, rotEuler.z));
            if (rotated.Count == 0) continue;

            int minY = rotated[0].y, maxY = rotated[0].y;
            foreach (var c in rotated)
            {
                if (c.y < minY) minY = c.y;
                if (c.y > maxY) maxY = c.y;
            }
            if (minY == maxY) return true;
        }
        return false;
    }

    // "Kütüphane / Solution-First" modu: SmartPieceSplitting'in mevcut çok-denemeli
    // döngüsündeki her attempt için Assets/PieceDefinitions/ altından TAZE, rastgele bir
    // parça havuzu örnekler (spawnWeight'e göre ağırlıklı, difficultyTags'e göre filtrelenmiş)
    // ve SolutionFirstBuilder ile o havuzun bu şekli GERÇEKTEN döşeyip döşeyemediğini
    // geri izlemeli olarak dener. Diğer 3 mod gibi "önce şekli çiz, sonra parçalara böl"
    // DEĞİL — başarılıysa sonuç zaten inşa sırasında çözülmüş olur.
    private List<List<Vector3Int>> SplitShapeWithSolutionFirstLibrary(int attempt)
    {
        var library = LoadPieceLibrary();
        if (library.Count == 0)
        {
            Debug.LogWarning("⚠️ Assets/PieceDefinitions/ altında hiç PieceDefinition bulunamadı — " +
                              "önce BlockMerge3D Hub → 🧬 Parça Kütüphanesi'nden 'Tara ve Migrate Et' çalıştırın.");
            return new List<List<Vector3Int>>();
        }

        // Zorluk profiline uyan parçalar: difficultyTags boşsa (henüz hiç etiketlenmemiş,
        // Faz 1 migration'ının varsayılan durumu) her zorlukta kullanılabilir sayılır.
        string profileTag = selectedDifficulty.ToString();
        var eligible = library
            .Where(d => d.difficultyTags == null || d.difficultyTags.Count == 0 || d.difficultyTags.Contains(profileTag))
            .ToList();
        if (eligible.Count == 0) eligible = library;

        // Doldurulması gereken hücreler: prefilled hariç tüm hedef hücreler (frozen dahil —
        // buz erimesi sırası SolutionFirstBuilder'da değil, sonradan gerçek LevelSolver'da
        // doğrulanır, bkz. SolutionFirstBuilder.cs üstündeki açıklama).
        var cellsToFill = new HashSet<Vector3Int>(occupiedCells);
        cellsToFill.ExceptWith(prefilledCells);

        int idealCount = DifficultySpecs.TryGetValue(selectedDifficulty, out var spec) ? spec.idealPieceCount : 5;
        int poolSize = Mathf.Clamp(idealCount + 2, 3, eligible.Count);

        var shuffledEligible = eligible.OrderBy(_ => Random.value).ToList();
        var pool = shuffledEligible.Take(poolSize).ToList();

        int gridVolume = gridSize.x * gridSize.y * gridSize.z;
        int stateLimit = gridVolume < 50 ? 30000 : gridVolume < 100 ? 50000 : 80000;
        int timeLimitMs = gridVolume < 50 ? 1500 : gridVolume < 100 ? 2500 : 4000;

        bool built = SolutionFirstBuilder.TryBuild(cellsToFill, gridSize, pool, stateLimit, timeLimitMs, out var resultPieces);

        Debug.Log($"  [Kütüphane/Solution-First] Deneme {attempt + 1}: havuz={pool.Count} parça tipi, " +
                   (built ? $"BAŞARILI ({resultPieces.Count} parça yerleşti)" : "döşenemedi"));

        if (built && frozenCells != null && frozenCells.Count > 0)
        {
            // [2026-07-14, ekip kararıyla, 2. revizyon] Buz erimesini tetikleyen 2 aynı renkli
            // komşu parça anında yok oluyor (bkz. GridManager.CheckAndResolveFrozenCells) — o
            // hücreler tekrar doldurulmalı. Havuz normalde hedefe TAM eşit hacimde üretildiği
            // için yedek parça yok; her buz hücresi en fazla BİR kez erir ve en fazla 2 hücre
            // yok eder, bu yüzden buz başına 2 hücrelik toplam pay ekliyoruz — AMA bunu 2 AYRI
            // tek-küplük parça yerine TEK bir 2-hücrelik "domino" parçası olarak ekliyoruz
            // (toplam hacim/güvenlik payı birebir aynı kalır, sadece parça SAYISI yarıya iner
            // ve hiçbiri tekli küp olmaz). İlk denemede tek-küplük yedekler kullanılmıştı ama
            // bu, levellerin tekli parçaya boğulmasına katkıda bulunduğu için domino'ya çevrildi
            // — LevelSolver.SolveFromPrefabs'teki `maxExtraForIce = frozenCells.Count * 2` üst
            // sınırı hâlâ geçerli (hacim aynı, sadece parçalanma şekli değişti). Hiç yok-olma
            // yaşanmazsa bu parçalar seviyeyi tamamlamadan önce hiç çekilmez, zararsız kalır.
            for (int i = 0; i < frozenCells.Count; i++)
                resultPieces.Add(new List<Vector3Int> { Vector3Int.zero, new Vector3Int(1, 0, 0) });
        }

        return built ? resultPieces : new List<List<Vector3Int>>();
    }

    // ═════════════════════════════════════════════════════════════
    // AKILLI PARÇA ÜRETİMİ - Birden fazla strateji dene, en iyisini seç
    // ═════════════════════════════════════════════════════════════
    private void SmartPieceSplitting()
    {
        Debug.Log("🧠 Akıllı Parça Üretimi başlatılıyor - birden fazla strateji deneniyor...");
        
        List<(List<List<Vector3Int>> pieces, SolverResult result, string strategyName)> strategies = 
            new List<(List<List<Vector3Int>>, SolverResult, string)>();

        // Grid boyutuna göre strateji sayısını ayarla
        int gridVolume = gridSize.x * gridSize.y * gridSize.z;
        int maxStrategies = gridVolume < 50 ? 3 :    // Küçük grid: 3 strateji
                            gridVolume < 100 ? 4 :   // Orta grid: 4 strateji
                            5;                        // Büyük grid: 5 strateji
        
        // En az 2 çözülebilir aday toplanmadan seçim yapılmaz; aksi halde ilk bulunan
        // (genelde en kolay/standart) strateji hiç karşılaştırılmadan kabul edilir ve
        // seçilen zorluk modu (Zor/Uzman) etkisiz kalır.
        int maxSolvableNeeded = gridVolume < 50 ? 2 : 3;
        
        for (int attempt = 0; attempt < maxStrategies; attempt++)
        {
            // Her denemede farklı parametrelerle parçala
            int variantMinSize = minPieceSize;
            int variantMaxSize = maxPieceSize;
            string strategyName = "Standart";

            switch (attempt)
            {
                case 0: // Standart Tetris Odaklı
                    variantMinSize = minPieceSize;
                    variantMaxSize = maxPieceSize;
                    strategyName = "Standart";
                    break;
                case 1: // Sıkı Triplet (Sadece 3)
                    variantMinSize = 3;
                    variantMaxSize = 3;
                    strategyName = "Sıkı Triplet (3 Blok)";
                    break;
                case 2: // Daha Büyük, Toplu Parçalar
                    variantMinSize = minPieceSize + 1;
                    variantMaxSize = maxPieceSize + 2;
                    strategyName = "Biraz Daha Büyük (Kolay)";
                    break;
                case 3: // Orta Boyutlar
                    variantMinSize = Mathf.Max(3, minPieceSize);
                    variantMaxSize = Mathf.Min(8, maxPieceSize + 1);
                    strategyName = "Orta Boyutlar";
                    break;
                case 4: // Karma Boyutlar
                    variantMinSize = Mathf.Max(2, minPieceSize - 1);
                    variantMaxSize = maxPieceSize;
                    strategyName = "Karma Boyutlar";
                    break;
            }

            // Bu stratejiyle parçala — tek üretim yolu: kütüphaneden solution-first backtracking
            // (bkz. SolutionFirstBuilder.cs). variantMinSize/variantMaxSize bu modda kullanılmıyor,
            // sadece yukarıdaki switch'in çeşitlilik amacıyla ürettiği attempt indeksi kullanılıyor.
            List<List<Vector3Int>> piecesForThisStrategy = SplitShapeWithSolutionFirstLibrary(attempt);

            if (piecesForThisStrategy.Count == 0)
            {
                Debug.Log($"  Strateji {attempt + 1}/{maxStrategies} ({strategyName}): Parça üretilemedi, atlandı");
                continue;
            }

            // Adaptif timeout: Grid boyutuna göre ayarla (gridVolume üstte tanımlı)
            int adaptiveTimeout = gridVolume < 50 ? 1000 :   // Küçük grid: 1 saniye
                                  gridVolume < 100 ? 2000 :  // Orta grid: 2 saniye
                                  3000;                       // Büyük grid: 3 saniye
            
            pieceSplitList = piecesForThisStrategy;
            var solverResult = TestCurrentPiecesWithSolver(adaptiveTimeout);
            
            // Timeout veya arama limiti aşıldıysa atla
            if (solverResult.failureReason != null && 
                (solverResult.failureReason.Contains("Arama limiti") || solverResult.failureReason.Contains("Timeout")))
            {
                Debug.Log($"  Strateji {attempt + 1}/{maxStrategies} ({strategyName}): " +
                         $"Parça={piecesForThisStrategy.Count}, TIMEOUT - atlandı");
                continue;
            }
            
            strategies.Add((new List<List<Vector3Int>>(piecesForThisStrategy), solverResult, strategyName));
            
            Debug.Log($"  Strateji {attempt + 1}/{maxStrategies} ({strategyName}): " +
                     $"Parça={piecesForThisStrategy.Count}, " +
                     $"Çözülebilir={solverResult.isSolvable}, " +
                     $"Hamle={solverResult.minMoveCount}, " +
                     $"Zorluk={solverResult.difficultyLabel}");

            // Erken durma: Yeterli çözülebilir strateji bulundu mu?
            int solvableCount = strategies.Count(s => s.result.isSolvable);
            if (solvableCount >= maxSolvableNeeded)
            {
                Debug.Log($"✅ {solvableCount} çözülebilir strateji bulundu, arama durduruluyor (performans için)");
                break;
            }
        }

        // En iyi stratejiyi seç
        var bestStrategy = SelectBestStrategy(strategies);
        
        if (bestStrategy.pieces != null)
        {
            pieceSplitList = bestStrategy.pieces;
            lastSolverResult = bestStrategy.result;
            solverRan = true;
            
            Debug.Log($"✅ EN İYİ STRATEJİ SEÇİLDİ: {bestStrategy.strategyName} - " +
                     $"Parça={pieceSplitList.Count}, " +
                     $"Çözülebilir={bestStrategy.result.isSolvable}, " +
                     $"Hamle={bestStrategy.result.minMoveCount}, " +
                     $"Zorluk={bestStrategy.result.difficultyLabel} ({bestStrategy.result.difficultyScore:F2})");
        }
        else
        {
            // Hiçbir deneme çözülebilir bulunamadıysa, kütüphaneden son bir kez daha dene
            // (taze bir rastgele havuzla) ve sonucu (başarısız da olsa) doğrudan raporla —
            // pieceSplitList/lastSolverResult/solverRan burada set edilmiş olur.
            Debug.LogWarning("⚠️ Hiçbir strateji çözülebilir bulunamadı, son bir deneme daha yapılıyor");
            pieceSplitList = SplitShapeWithSolutionFirstLibrary(0);
            RunSolverAnalysis();
        }
    }

    private SolverResult TestCurrentPiecesWithSolver(int timeoutMs = 5000)
    {
        GameObject tempMainShape = CreateTempMainShape();
        if (tempMainShape == null)
        {
            return new SolverResult 
            { 
                isSolvable = false, 
                failureReason = "Main shape oluşturulamadı" 
            };
        }

        List<GameObject> tempPieces = CreateTempPieces();
        if (tempPieces.Count == 0)
        {
            DestroyImmediate(tempMainShape);
            return new SolverResult 
            { 
                isSolvable = false, 
                failureReason = "Parçalar oluşturulamadı" 
            };
        }

        // Adaptif state limiti: Grid boyutuna göre
        int gridVolume = gridSize.x * gridSize.y * gridSize.z;
        int stateLimit = gridVolume < 50 ? 50000 :   // Küçük: 50k
                         gridVolume < 100 ? 75000 :  // Orta: 75k
                         100000;                      // Büyük: 100k

        var solver = new LevelSolver();
        solver.maxSearchTimeMs = timeoutMs;
        solver.maxStatesExplored = stateLimit;
        var result = solver.SolveFromPrefabs(tempMainShape, tempPieces);

        // Temizlik
        DestroyImmediate(tempMainShape);
        foreach (var piece in tempPieces)
        {
            if (piece != null) DestroyImmediate(piece);
        }

        return result;
    }

    // Tüm zorluk modlarının parametreleri TEK bir yerden okunur (bkz. DifficultySpecs altta).
    // Eskiden ApplyDifficultyScaleForMode ve GetDifficultyTargets birbirinden bağımsız iki ayrı
    // switch/hardcoded tablo tutuyordu — biri güncellenip diğeri unutulursa (ör. ORTA'ya yeni bir
    // alan eklenip diğer tabloya yansıtılmazsa) sessizce birbirinden sapabiliyordu. Artık ikisi de
    // aynı DifficultySpecs sözlüğünden okuyor.
    private struct AIDifficultySpec
    {
        public float baseTime;
        public float baseTarget;
        public float prefillPercentage;
        public float icePercentage;
        public int minPieceSize;
        public int maxPieceSize;
        // Solver tabanlı strateji seçiminde kullanılan hedefler (bkz. SelectBestStrategy).
        public float solverTargetScore; // LevelSolver 0.0-1.0 zorluk skalası
        public int idealPieceCount;
        public int minMoves;
        public int maxMoves;
    }

    private static readonly Dictionary<AILevelDifficulty, AIDifficultySpec> DifficultySpecs = new Dictionary<AILevelDifficulty, AIDifficultySpec>
    {
        { AILevelDifficulty.Kolay, new AIDifficultySpec {
            baseTime = 90f, baseTarget = 80f, prefillPercentage = 0f, icePercentage = 0f,
            minPieceSize = 1, maxPieceSize = 3, // 4 yasaklandı
            solverTargetScore = 0.15f, idealPieceCount = 3, minMoves = 2, maxMoves = 6 } },

        { AILevelDifficulty.Orta, new AIDifficultySpec {
            baseTime = 75f, baseTarget = 180f,
            // Prefilled (hazır renkli engel) hücreler kapalı: LevelManager.GetDominantMaterialOnActiveLayer
            // prefilled hücreleri bilerek dominant renk hesabına katmıyor (bkz. o metodun içindeki yorum) —
            // bu da bir katmanda SADECE prefilled hücre varken sonraki parçaların rastgele (prefilled ile
            // eşleşmeyen) bir renk alıp katmanı kalıcı olarak temizlenemez bırakabilmesi riskini taşıyor.
            // Buz (ice) aynı riski taşımıyor (rengi yok, sadece bitişik parça erittiriyor) — o yüzden ice
            // açık kalıyor, prefill kapalı.
            prefillPercentage = 0f, icePercentage = 0.10f,
            minPieceSize = 2, maxPieceSize = 3, // 4 yasaklandı
            solverTargetScore = 0.40f, idealPieceCount = 5, minMoves = 4, maxMoves = 10 } },

        { AILevelDifficulty.Zor, new AIDifficultySpec {
            baseTime = 60f, baseTarget = 420f, prefillPercentage = 0.20f, icePercentage = 0.20f,
            minPieceSize = 2, maxPieceSize = 5,
            solverTargetScore = 0.65f, idealPieceCount = 7, minMoves = 6, maxMoves = 16 } },

        { AILevelDifficulty.Uzman, new AIDifficultySpec {
            baseTime = 45f, baseTarget = 870f, prefillPercentage = 0.30f, icePercentage = 0.30f,
            minPieceSize = 3, maxPieceSize = 6, // 4 olmasın
            solverTargetScore = 0.85f, idealPieceCount = 10, minMoves = 8, maxMoves = 24 } },
    };

    private static AIDifficultySpec GetDifficultySpec(AILevelDifficulty mode)
    {
        return DifficultySpecs.TryGetValue(mode, out var spec) ? spec : DifficultySpecs[AILevelDifficulty.Orta];
    }

    // Seçilen zorluk moduna göre hedef zorluk skoru (LevelSolver 0.0-1.0 skalasında çalışır),
    // ideal parça sayısı ve ideal hamle aralığı. SelectBestStrategy bu hedeflere göre puanlar;
    // aksi halde (eskiden olduğu gibi) sabit "orta zorluk" hedefi Zor/Uzman modlarında bile
    // en kolay/az parçalı sonucu seçmeye devam eder.
    private (float targetScore, int idealPieceCount, int minMoves, int maxMoves) GetDifficultyTargets(AILevelDifficulty mode)
    {
        var spec = GetDifficultySpec(mode);
        return (spec.solverTargetScore, spec.idealPieceCount, spec.minMoves, spec.maxMoves);
    }

    private (List<List<Vector3Int>> pieces, SolverResult result, string strategyName)
        SelectBestStrategy(List<(List<List<Vector3Int>> pieces, SolverResult result, string strategyName)> strategies)
    {
        // En iyi stratejiyi seçme kriterleri:
        // 1. Önce çözülebilir olanları filtrele
        // 2. Seçilen zorluk moduna (Kolay/Orta/Zor/Uzman) en yakın olanı seç

        var solvable = strategies.Where(s => s.result.isSolvable).ToList();

        if (solvable.Count == 0)
        {
            Debug.LogWarning("⚠️ Çözülebilir strateji bulunamadı!");
            return (null, null, "");
        }

        var targets = GetDifficultyTargets(selectedDifficulty);

        // En iyi stratejiyi seç (çok faktörlü skor sistemi)
        var best = solvable
            .Select(s => new
            {
                strategy = s,
                // Skor hesaplama:
                // - Parça sayısı skoru (ideal, zorluk moduna göre değişir)
                pieceScore = 100f - Mathf.Abs(s.pieces.Count - targets.idealPieceCount) * 8f,
                // - Zorluk skoru: solver 0.0-1.0 döndürür, hedefle aynı skalada karşılaştırılır
                difficultyScore = 100f - Mathf.Abs(s.result.difficultyScore - targets.targetScore) * 100f,
                // - Hamle sayısı skoru (zorluk moduna göre ideal aralık)
                moveScore = (s.result.minMoveCount >= targets.minMoves && s.result.minMoveCount <= targets.maxMoves) ? 100f : 50f,
                // Toplam skor
                totalScore = 0f
            })
            .Select(x => new
            {
                x.strategy,
                x.pieceScore,
                x.difficultyScore,
                x.moveScore,
                totalScore = x.pieceScore + x.difficultyScore + x.moveScore
            })
            .OrderByDescending(x => x.totalScore)
            .First();

        Debug.Log($"📊 Skor Detayları - {best.strategy.strategyName}: " +
                 $"Parça Skoru={best.pieceScore:F1}, " +
                 $"Zorluk Skoru={best.difficultyScore:F1}, " +
                 $"Hamle Skoru={best.moveScore:F1}, " +
                 $"TOPLAM={best.totalScore:F1}");

        return best.strategy;
    }

    // ═════════════════════════════════════════════════════════════
    // TETROMİNO ŞABLONLARI
    // Seviye şeklini parçalara bölen eski "Tetromino Klasik" modu kaldırıldı (bkz. Solution-First
    // kütüphane modu), ama bu 7 klasik şekil hâlâ "AI Parça Yapıcı" sekmesinin ClassicTetris
    // üretiminde kullanılıyor (bkz. TETROMINO_BASE_SHAPES kullanım yeri).
    // ═════════════════════════════════════════════════════════════
    private static readonly Vector2Int[][] TETROMINO_BASE_SHAPES = new Vector2Int[][]
    {
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0), new Vector2Int(3,0) }, // I
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(1,1) }, // O
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0), new Vector2Int(1,1) }, // T
        new[] { new Vector2Int(1,0), new Vector2Int(2,0), new Vector2Int(0,1), new Vector2Int(1,1) }, // S
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(1,1), new Vector2Int(2,1) }, // Z
        new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(2,1) }, // J
        new[] { new Vector2Int(2,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(2,1) }, // L
    };

    internal void ExportProceduralLevel()
    {
        if (occupiedCells.Count == 0)
        {
            EditorUtility.DisplayDialog("Hata", "Önce 'YAPAY ZEKA İLE OLUŞTUR' butonuna basarak bir seviye tasarlayın.", "Tamam");
            return;
        }

        if (!(solverRan && lastSolverResult != null && lastSolverResult.isSolvable))
        {
            EditorUtility.DisplayDialog("Doğrulanmamış Seviye",
                "Bu seviye solver tarafından ÇÖZÜLEBİLİR olarak doğrulanmadı, bu yüzden kaydedilemez " +
                "(Zorunlu Koruma Kuralı #1).\n\n" +
                "Önce '⚡ BÖLÜM & PARÇALARI ÖNİZLE' ile tekrar üretin ya da '🔁 Parametreleri Ayarlayıp " +
                "Yeniden Üret' butonunu kullanın.", "Tamam");
            return;
        }

        LevelData ld = ExportProceduralLevelCore(levelName, levelTime, levelTarget);
        if (ld != null)
        {
            EditorUtility.DisplayDialog("AI Export Başarılı!",
                $"🤖 Yapay Zeka Başarıyla Kaydetti:\n" +
                $"✅  LevelData Varlığı\n" +
                $"✅  Ana Şekil Prefabı\n" +
                $"✅  {ld.complementaryPieces.Count} Adet Bulmaca Parçası\n\nHedef Dizin: {LEVELS_PATH}/{levelName}/", "Harika!");
        }
        else
        {
            EditorUtility.DisplayDialog("Kaydedilemedi",
                "Seviye doğrulanmamış olduğu için kaydedilmedi (Zorunlu Koruma Kuralı #1).", "Tamam");
        }
    }

    private LevelData ExportProceduralLevelCore(string targetLevelName, float targetLevelTime, int targetLevelTarget)
    {
        // Zorunlu Koruma Kuralı #1 (bkz. BlockMerge3D_Seviye_Uretim_Sistemi_v2.md §13): doğrulanmamış
        // (validated == false) hiçbir seviye kaydedilemez. Bu, tek gerçek kayıt noktası olduğu için
        // (ExportProceduralLevel VE GenerateAndExportAIBatchDataset ikisi de buraya çağrı yapıyor)
        // burada kontrol edilmesi, çağıran her yerin ayrı ayrı doğru davranmasına güvenmekten daha güvenli.
        if (!(solverRan && lastSolverResult != null && lastSolverResult.isSolvable))
        {
            Debug.LogWarning($"⛔ '{targetLevelName}' kaydedilmedi: solver tarafından doğrulanmamış/çözülemez.");
            return null;
        }

        string levelDir = $"{LEVELS_PATH}/{targetLevelName}";
        if (!Directory.Exists(levelDir)) Directory.CreateDirectory(levelDir);
        AssetDatabase.Refresh();

        float step = cellSize + spacing;

        // 1. Her Bir Parça Prefabını Oluştur
        List<GameObject> piecePrefabs = new List<GameObject>();
        for (int i = 0; i < pieceSplitList.Count; i++)
        {
            List<Vector3Int> cells = pieceSplitList[i];
            if (cells.Count == 0) continue;

            // Normalize et (en küçük koordinat local sıfıra çekilir)
            int minX = cells.Min(c => c.x), minY = cells.Min(c => c.y), minZ = cells.Min(c => c.z);
            var shift = new Vector3Int(minX, minY, minZ);
            List<Vector3Int> normCells = cells.Select(c => c - shift).ToList();

            string pPath = $"{levelDir}/{targetLevelName}_Piece_{i + 1}.prefab";
            GameObject pRoot = new GameObject($"{targetLevelName}_Piece_{i + 1}");
            var ph = pRoot.AddComponent<CubeShapeDataHolder>();
            ph.shapeName     = $"{targetLevelName}_Piece_{i + 1}";
            ph.gridSize      = gridSize;
            ph.cellSize      = cellSize;
            ph.spacing       = spacing;
            ph.occupiedCells = new List<Vector3Int>(normCells);

            foreach (var cell in normCells)
            {
                GameObject cube = cubePrefab != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(pRoot.transform);
                cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
                cube.transform.localScale    = Vector3.one * cellSize;
                cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            }

            GameObject savedPiece = PrefabUtility.SaveAsPrefabAsset(pRoot, pPath);
            DestroyImmediate(pRoot);
            piecePrefabs.Add(savedPiece);
        }

        // 2. Ana Şekil Prefabını Oluştur
        string fullPath = $"{levelDir}/{targetLevelName}_FullShape.prefab";
        GameObject fullRoot = new GameObject($"{targetLevelName}_FullShape");
        var fh = fullRoot.AddComponent<CubeShapeDataHolder>();
        fh.shapeName    = targetLevelName; fh.gridSize = gridSize; fh.cellSize = cellSize; fh.spacing = spacing;
        fh.occupiedCells            = new List<Vector3Int>(occupiedCells);
        fh.prefilledCells           = new List<Vector3Int>(prefilledCells);
        fh.prefilledColors          = prefilledMatIdx.Select(idx => PIECE_COLORS[idx % PIECE_COLORS.Length]).ToList();
        fh.prefilledMaterialIndices = new List<int>(prefilledMatIdx);
        fh.frozenCells              = new List<Vector3Int>(frozenCells);

        foreach (var cell in occupiedCells)
        {
            GameObject cube = cubePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(fullRoot.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
            cube.transform.localScale    = Vector3.one * cellSize;
            
            if (prefilledCells.Contains(cell))
            {
                int pfIndex = prefilledCells.IndexOf(cell);
                int matIdx = (pfIndex >= 0 && pfIndex < prefilledMatIdx.Count) ? prefilledMatIdx[pfIndex] : 0;
                cube.name = $"Prefilled_{matIdx}_{cell.x}_{cell.y}_{cell.z}";

                // Küpün ismindeki matIdx sadece mantık içindi; oyuncunun engelin rengini
                // görebilmesi için gerçek Renderer materyalini de aynı index'e eşleyelim.
                if (prefilledMaterials != null && matIdx >= 0 && matIdx < prefilledMaterials.Length && prefilledMaterials[matIdx] != null)
                {
                    var pfRend = cube.GetComponentInChildren<Renderer>(true);
                    if (pfRend != null) pfRend.sharedMaterial = prefilledMaterials[matIdx];
                }
            }
            else
            {
                cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            }
        }

        GameObject savedFull = PrefabUtility.SaveAsPrefabAsset(fullRoot, fullPath);
        DestroyImmediate(fullRoot);

        // 3. LevelData ScriptableObject Oluştur
        string ldPath = $"{levelDir}/{targetLevelName}_LevelData.asset";
        LevelData ld = AssetDatabase.LoadAssetAtPath<LevelData>(ldPath);
        bool isNew = ld == null;
        if (isNew) ld = ScriptableObject.CreateInstance<LevelData>();

        ld.levelName           = targetLevelName;
        ld.mainShapePrefab     = savedFull;
        ld.complementaryPieces = piecePrefabs;
        ld.timeLimit           = targetLevelTime;
        ld.targetScore         = targetLevelTarget;

        if (isNew) AssetDatabase.CreateAsset(ld, ldPath);
        else       EditorUtility.SetDirty(ld);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return ld;
    }

    // ══ SOLVER ENTEGRASYONU ══════════════════════════════════════════════════

    private void RunSolverAnalysis()
    {
        solverRan = false;
        lastSolverResult = null;

        // Adaptif timeout
        int gridVolume = gridSize.x * gridSize.y * gridSize.z;
        int timeout = gridVolume < 50 ? 2000 :   // Küçük: 2 saniye
                      gridVolume < 100 ? 3000 :  // Orta: 3 saniye
                      5000;                       // Büyük: 5 saniye
        
        lastSolverResult = TestCurrentPiecesWithSolver(timeout);
        solverRan = true;

        // Sonucu logla
        if (lastSolverResult.isSolvable)
        {
            Debug.Log($"✅ Seviye çözülebilir: {lastSolverResult.minMoveCount} hamle, Zorluk: {lastSolverResult.difficultyLabel} ({lastSolverResult.difficultyScore:F2})");
        }
        else
        {
            Debug.LogWarning($"❌ Seviye çözülemez: {lastSolverResult.failureReason}");
        }
    }

    private GameObject CreateTempMainShape()
    {
        GameObject root = new GameObject("TempMainShape");
        var holder = root.AddComponent<CubeShapeDataHolder>();
        holder.gridSize = gridSize;
        holder.cellSize = cellSize;
        holder.spacing = spacing;
        holder.occupiedCells = new List<Vector3Int>(occupiedCells);
        holder.prefilledCells = new List<Vector3Int>(prefilledCells);
        holder.prefilledMaterialIndices = new List<int>(prefilledMatIdx);
        holder.frozenCells = new List<Vector3Int>(frozenCells);
        return root;
    }

    private List<GameObject> CreateTempPieces()
    {
        List<GameObject> pieces = new List<GameObject>();
        for (int i = 0; i < pieceSplitList.Count; i++)
        {
            if (pieceSplitList[i].Count == 0) continue;

            // Normalize et
            var cells = pieceSplitList[i];
            int minX = cells.Min(c => c.x);
            int minY = cells.Min(c => c.y);
            int minZ = cells.Min(c => c.z);
            var shift = new Vector3Int(minX, minY, minZ);
            var normCells = cells.Select(c => c - shift).ToList();

            GameObject piece = new GameObject($"TempPiece_{i}");
            var holder = piece.AddComponent<CubeShapeDataHolder>();
            holder.gridSize = gridSize;
            holder.cellSize = cellSize;
            holder.spacing = spacing;
            holder.occupiedCells = new List<Vector3Int>(normCells);
            
            pieces.Add(piece);
        }
        return pieces;
    }

    private void AutoAdjustAndRegenerate()
    {
        // Çözülemez seviyeyi düzeltmeye çalış
        if (lastSolverResult != null && !string.IsNullOrEmpty(lastSolverResult.failureReason))
        {
            string reason = lastSolverResult.failureReason.ToLower();

            // Yetersiz/fazla hücre problemi
            if (reason.Contains("yetersiz") || reason.Contains("fazla"))
            {
                // Parça boyutlarını ayarla
                minPieceSize = Mathf.Max(2, minPieceSize - 1);
                maxPieceSize = Mathf.Min(8, maxPieceSize + 1);
                Debug.Log("Parça boyutları ayarlandı");
            }
            // Renk çakışması problemi
            else if (reason.Contains("renk") || reason.Contains("katman"))
            {
                // Prefilled ve frozen oranlarını azalt
                prefillPercentage = Mathf.Max(0f, prefillPercentage - 0.05f);
                icePercentage = Mathf.Max(0f, icePercentage - 0.05f);
                Debug.Log("Engel oranları azaltıldı");
            }
            // Zaman aşımı
            else if (reason.Contains("limit"))
            {
                // Grid boyutunu küçült veya parça sayısını azalt
                gridSize = new Vector3Int(
                    Mathf.Max(3, gridSize.x - 1),
                    Mathf.Max(3, gridSize.y - 1),
                    Mathf.Max(3, gridSize.z - 1)
                );
                fillDensity = Mathf.Max(0.5f, fillDensity - 0.1f);
                Debug.Log("Grid boyutu ve doluluk azaltıldı");
            }
        }

        // Yeniden üret
        GenerateLevelProcedurally();
        EditorUtility.DisplayDialog("Yeniden Üretim", 
            "Parametreler ayarlandı ve seviye yeniden oluşturuldu. Sonuçları kontrol edin.", "Tamam");
    }

    // internal: LevelCreationWizardWindow, aiDesigner.OnGUI()'yi hiç çağırmadan
    // DrawTemplateAndDifficultySection/DrawSolverResultSection'ı doğrudan kullandığı için
    // stil alanlarının (styleHeader vb.) dolu olduğundan kendi başına emin olmalı.
    internal void BuildStyles()
    {
        if (stylesBuilt) return;
        styleHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        styleHeader.normal.textColor = COL_HEADER;

        styleBox = new GUIStyle(GUI.skin.box);

        styleTabActive = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.95f, 0.40f, 0.70f, 0.9f)) }
        };

        styleTabInactive = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Normal
        };

        styleInstructionBox = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 10, 10),
            margin = new RectOffset(0, 0, 5, 5)
        };

        stylesBuilt = true;
    }

    private void LoadTemplateParameters()
    {
        if (selectedTemplate == null) return;

        // Şablondan sadece grid yapısı ve zorluk parametrelerini yükle
        gridSize = selectedTemplate.gridSize;
        icePercentage = selectedTemplate.recommendedIceRatio;
        prefillPercentage = selectedTemplate.recommendedPrefilledRatio;
        levelTime = selectedTemplate.recommendedTimeLimit;
        levelTarget = selectedTemplate.recommendedTargetScore;

        int volume = selectedTemplate.occupiedCells != null && selectedTemplate.occupiedCells.Count > 0 
            ? selectedTemplate.occupiedCells.Count 
            : gridSize.x * gridSize.y * gridSize.z;

        // Hacim küçükse tam bir Tetris hissi için 3-5 blok; hacim büyüdükçe çok fazla parça (örn 20 parça) 
        // oluşmasını engellemek için boyutu orantılı olarak artırıyoruz.
        if (volume <= 30)
        {
            minPieceSize = 3;
            maxPieceSize = 5;
        }
        else
        {
            int targetPieceCount = 6; // Büyük seviyelerde ortalama 6-8 parça hedeflenir
            maxPieceSize = Mathf.Clamp(volume / targetPieceCount + 1, 5, 12);
            minPieceSize = Mathf.Max(3, maxPieceSize - 3);
        }

        Debug.Log($"📐 Şablon yüklendi: {selectedTemplate.templateName} ({gridSize.x}x{gridSize.y}x{gridSize.z}) - Dinamik Boyut: Min {minPieceSize}, Max {maxPieceSize}");
        Repaint();
    }

    private void LoadPrefabParameters()
    {
        if (customBasePrefab == null) return;

        var holder = customBasePrefab.GetComponent<CubeShapeDataHolder>();
        if (holder != null)
        {
            gridSize = holder.gridSize;

            int volume = holder.occupiedCells != null && holder.occupiedCells.Count > 0
                ? holder.occupiedCells.Count
                : gridSize.x * gridSize.y * gridSize.z;

            if (volume <= 30)
            {
                minPieceSize = 3;
                maxPieceSize = 5;
            }
            else
            {
                int targetPieceCount = 6;
                maxPieceSize = Mathf.Clamp(volume / targetPieceCount + 1, 5, 12);
                minPieceSize = Mathf.Max(3, maxPieceSize - 3);
            }

            Debug.Log($"📐 Prefab yüklendi: {customBasePrefab.name} ({gridSize.x}x{gridSize.y}x{gridSize.z}) - Dinamik Boyut: Min {minPieceSize}, Max {maxPieceSize}");
        }

        Repaint();
    }

    internal void ApplyDifficultyScaleForMode(AILevelDifficulty mode)
    {
        selectedDifficulty = mode;
        levelDifficultyModeSuggestion = mode.ToString();

        var spec = GetDifficultySpec(mode);
        prefillPercentage = spec.prefillPercentage;
        icePercentage = spec.icePercentage;
        minPieceSize = spec.minPieceSize;
        maxPieceSize = spec.maxPieceSize;

        if (selectedTemplate != null)
        {
            levelTime = Mathf.Round(selectedTemplate.recommendedTimeLimit * (spec.baseTime / 75f));
            levelTarget = Mathf.RoundToInt(selectedTemplate.recommendedTargetScore * (spec.baseTarget / 150f));
        }
        else
        {
            levelTime = Mathf.Round(spec.baseTime);
            levelTarget = Mathf.RoundToInt(spec.baseTarget);
        }

        prefillPercentage = Mathf.Round(prefillPercentage * 100f) / 100f;
        icePercentage = Mathf.Round(icePercentage * 100f) / 100f;
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

    private void GenerateAndExportAIBatchDataset()
    {
        // 1. Kullanıcının mevcut ayarlarını yedekle
        string origLevelName = levelName;
        float origLevelTime = levelTime;
        int origLevelTarget = levelTarget;
        Vector3Int origGridSize = gridSize;
        float origFillDensity = fillDensity;
        float origPrefillPercentage = prefillPercentage;
        float origIcePercentage = icePercentage;
        int origMinPieceSize = minPieceSize;
        int origMaxPieceSize = maxPieceSize;

        // 2. Seviye Sırasını yükle veya oluştur
        const string LEVEL_ORDER_PATH = "Assets/LevelOrder.asset";
        LevelOrderData levelOrder = AssetDatabase.LoadAssetAtPath<LevelOrderData>(LEVEL_ORDER_PATH);
        if (levelOrder == null)
        {
            levelOrder = ScriptableObject.CreateInstance<LevelOrderData>();
            AssetDatabase.CreateAsset(levelOrder, LEVEL_ORDER_PATH);
        }
        levelOrder.levels.Clear();

        // Eğitim verisi veri kümesi wrapperı
        AIDatasetWrapper datasetWrapper = new AIDatasetWrapper();

        // Zorunlu Koruma Kuralı #1 gereği ExportProceduralLevelCore artık doğrulanmamış/çözülemez
        // seviyeleri reddedip null döndürebiliyor — bu sayaç, kaç tanesinin gerçekten kaydedildiğini
        // (ve kaçının atlandığını) izler, böylece kapanış diyaloğu "10 level oluşturuldu" diye
        // yanlış bir şey iddia etmez.
        int savedCount = 0;
        var skippedLevelNames = new List<string>();

        // 3. Kullanıcı tasarımı: İlk 10 Level (Kademeli Öğretim)
        for (int i = 1; i <= 10; i++)
        {
            // İlerleme çubuğu
            EditorUtility.DisplayProgressBar("İlk 10 Level Üretimi", $"Level {i}/10 üretiliyor...", i / 10f);

            levelName = $"Level_{i}";
            
            // Tasarım spesifikasyonlarını uygula
            switch (i)
            {
                case 1: // İlk Adım — Temel sürükle-bırak
                    gridSize = new Vector3Int(3, 1, 3);         // Tek katman 3×3 = 9 küp
                    fillDensity = 1.0f;                         // Tam dolu
                    prefillPercentage = 0.0f;                   // Prefilled yok
                    icePercentage = 0.0f;                       // Frozen yok
                    minPieceSize = 3;
                    maxPieceSize = 3;                           // 3 parça hedefi
                    levelTime = 0f;                             // Süre yok
                    levelTarget = 50;
                    break;

                case 2: // İki Katman — Katman geçişi
                    gridSize = new Vector3Int(3, 2, 3);         // Alt: 3×3 (9 küp), Üst: 1 küp = 10 toplam
                    fillDensity = 1.0f;
                    prefillPercentage = 0.0f;
                    icePercentage = 0.0f;
                    minPieceSize = 2;                           // Daha küçük parçalar
                    maxPieceSize = 4;                           // 4-5 parça hedefi
                    levelTime = 0f;
                    levelTarget = 80;
                    break;

                case 3: // Renk Farkındalığı — Renk kısıtı
                    gridSize = new Vector3Int(3, 1, 3);         // Tek katman
                    fillDensity = 1.0f;
                    prefillPercentage = 0.22f;                  // 2/9 ≈ 22%
                    icePercentage = 0.0f;
                    minPieceSize = 2;
                    maxPieceSize = 3;                           // ~4 parça hedefi
                    levelTime = 0f;
                    levelTarget = 100;
                    break;

                case 4: // Buz Girişi — Frozen hücre
                    gridSize = new Vector3Int(3, 2, 3);         // 2 katman
                    fillDensity = 1.0f;
                    prefillPercentage = 0.0f;
                    icePercentage = 0.33f;                      // %33 frozen
                    minPieceSize = 3;
                    maxPieceSize = 3;                           // ~6 parça hedefi
                    levelTime = 0f;
                    levelTarget = 120;
                    break;

                case 5: // Küçük Küp — 3 katmana geçiş
                    gridSize = new Vector3Int(5, 3, 5);         // 3 katman
                    fillDensity = 1.0f;                         // Tam dolu
                    prefillPercentage = 0.0f;
                    icePercentage = 0.0f;
                    minPieceSize = 4;
                    maxPieceSize = 6;                           // ~8 parça hedefi
                    levelTime = 0f;
                    levelTarget = 150;
                    break;

                case 6: // Karışık Renk Katmanı — Çoklu renk
                    gridSize = new Vector3Int(4, 2, 4);         // 2 katman
                    fillDensity = 1.0f;
                    prefillPercentage = 0.125f;                 // 4/32 ≈ 12.5%
                    icePercentage = 0.0f;
                    minPieceSize = 3;
                    maxPieceSize = 4;                           // ~10 parça hedefi
                    levelTime = 0f;
                    levelTarget = 180;
                    break;

                case 7: // Buz + Çok Renk — Kombine mekanikler
                    gridSize = new Vector3Int(4, 3, 4);         // 3 katman
                    fillDensity = 1.0f;
                    prefillPercentage = 0.1f;                   // ~6 hücre
                    icePercentage = 0.25f;                      // %25 frozen
                    minPieceSize = 4;
                    maxPieceSize = 6;                           // ~12 parça hedefi
                    levelTime = 0f;
                    levelTarget = 220;
                    break;

                case 8: // Büyük Küp — Daha fazla hazır renkli blok
                    gridSize = new Vector3Int(6, 3, 6);         // 3 katman
                    fillDensity = 1.0f;
                    prefillPercentage = 0.08f;
                    icePercentage = 0.20f;                      // %20 frozen
                    minPieceSize = 4;
                    maxPieceSize = 7;                           // ~14 parça hedefi
                    levelTime = 0f;
                    levelTarget = 260;
                    break;

                case 9: // Yüksek Küp — 5 katmana çıkış
                    gridSize = new Vector3Int(5, 5, 5);         // 5 katman
                    fillDensity = 1.0f;
                    prefillPercentage = 0.15f;                  // %15
                    icePercentage = 0.20f;                      // %20 frozen
                    minPieceSize = 5;
                    maxPieceSize = 8;                           // ~18 parça hedefi
                    levelTime = 180f;                           // İlk süre limiti
                    levelTarget = 300;
                    break;

                case 10: // Usta Seviyesi — En büyük küp, en yüksek buz/prefilled oranı
                default:
                    gridSize = new Vector3Int(6, 4, 6);         // 4 katman
                    fillDensity = 1.0f;
                    prefillPercentage = 0.20f;                  // %20
                    icePercentage = 0.25f;                      // %25 frozen
                    minPieceSize = 6;
                    maxPieceSize = 10;                          // ~22 parça hedefi
                    levelTime = 240f;                           // Uzun süre limiti
                    levelTarget = 350;
                    break;
            }

            // Seviyeyi oluştur: bu toplu üretim bir şablona bağlı değildir (kullanıcı UI'de
            // şablon seçmemiş olabilir), her zaman doğrudan case'in kendi gridSize'ından tam
            // dolu bir kutu inşa eder — GenerateLevelProcedurally'nin şablon zorunluluğuna takılmaz.
            occupiedCells.Clear();
            prefilledCells.Clear();
            prefilledMatIdx.Clear();
            frozenCells.Clear();
            pieceSplitList.Clear();
            highlightedPieceIndex = -1;
            BuildSolidBoxShape(gridSize.x, gridSize.y, gridSize.z);
            ApplyObstaclesAndSplitPieces(gridSize.x, gridSize.y, gridSize.z);

            // Kaydet
            LevelData levelAsset = ExportProceduralLevelCore(levelName, levelTime, levelTarget);
            if (levelAsset != null)
            {
                levelOrder.levels.Add(levelAsset);
                savedCount++;
            }
            else
            {
                skippedLevelNames.Add(levelName);
            }

            // Eğitim verisi modelini doldur
            AIDatasetEntry entry = new AIDatasetEntry();
            entry.levelName = levelName;
            entry.difficultyIndex = i;
            entry.shapeType = "SolidBox"; // Bu toplu üretim her zaman düz dolu kutu üretir (bkz. yukarısı)
            entry.gridSize = new SerializableCell(gridSize);
            entry.occupiedCells = occupiedCells.Select(c => new SerializableCell(c)).ToList();
            entry.prefilledCells = prefilledCells.Select(c => new SerializableCell(c)).ToList();
            entry.prefilledMaterialIndices = new List<int>(prefilledMatIdx);
            entry.frozenCells = frozenCells.Select(c => new SerializableCell(c)).ToList();
            entry.pieces = new List<AIPieceEntry>();
            
            // Solver sonuçlarını ekle
            entry.pieceCount = pieceSplitList.Count;
            entry.frozenRatio = occupiedCells.Count > 0 ? (float)frozenCells.Count / occupiedCells.Count : 0f;
            if (solverRan && lastSolverResult != null)
            {
                entry.isSolvable = lastSolverResult.isSolvable;
                entry.minMoveCount = lastSolverResult.minMoveCount;
                entry.difficultyScore = lastSolverResult.difficultyScore;
                entry.difficultyLabel = lastSolverResult.difficultyLabel;
            }

            for (int pIdx = 0; pIdx < pieceSplitList.Count; pIdx++)
            {
                AIPieceEntry pEntry = new AIPieceEntry();
                pEntry.pieceIndex = pIdx;
                
                // Parça hücrelerini normalize et
                var cells = pieceSplitList[pIdx];
                int minX = cells.Min(c => c.x), minY = cells.Min(c => c.y), minZ = cells.Min(c => c.z);
                var shift = new Vector3Int(minX, minY, minZ);
                List<Vector3Int> normCells = cells.Select(c => c - shift).ToList();

                pEntry.localCells = normCells.Select(c => new SerializableCell(c)).ToList();
                entry.pieces.Add(pEntry);
            }

            datasetWrapper.dataset.Add(entry);
        }

        // İlerleme çubuğunu kapat
        EditorUtility.ClearProgressBar();

        // 4. Seviye Sırasını kaydet
        EditorUtility.SetDirty(levelOrder);
        AssetDatabase.SaveAssets();

        // 5. JSON dosyasını yaz
        string jsonPath = $"{LEVELS_PATH}/ai_training_dataset.json";
        string jsonContent = JsonUtility.ToJson(datasetWrapper, true);
        File.WriteAllText(jsonPath, jsonContent);

        // 6. Oyunu 1. levelden başlatmak için PlayerPrefs sıfırla
        PlayerPrefs.SetInt("CurrentLevelIndex", 0);
        PlayerPrefs.Save();

        // 7. Kullanıcının orijinal ayarlarını geri yükle
        levelName = origLevelName;
        levelTime = origLevelTime;
        levelTarget = origLevelTarget;
        gridSize = origGridSize;
        fillDensity = origFillDensity;
        prefillPercentage = origPrefillPercentage;
        icePercentage = origIcePercentage;
        minPieceSize = origMinPieceSize;
        maxPieceSize = origMaxPieceSize;

        // Seviye 1'i önizlemede göstermek için son kez oluştur
        GenerateLevelProcedurally();

        AssetDatabase.Refresh();

        string skippedNote = skippedLevelNames.Count > 0
            ? $"\n⚠️ {skippedLevelNames.Count} seviye solver tarafından doğrulanamadığı için ATLANDI " +
              $"(Zorunlu Koruma Kuralı #1): {string.Join(", ", skippedLevelNames)}\n"
            : "";

        EditorUtility.DisplayDialog(
            savedCount == 10 ? "🎮 İlk 10 Level Başarıyla Oluşturuldu!" : "🎮 Level Üretimi Tamamlandı",
            $"✅ Kademeli Öğretim Sistemi Devrede:\n\n" +
            $"📦 {savedCount}/10 Level Oluşturuldu (Assets/Levels/)\n" +
            skippedNote +
            $"📋 Level Sırası Güncellendi (LevelOrder.asset)\n" +
            $"🎯 Oyun Level 1'den Başlıyor\n" +
            $"📊 Eğitim Dataseti Kaydedildi:\n\t{jsonPath}\n\n" +
            $"Level 1-4:  Temel mekanikler (sürükle, katman, renk, buz)\n" +
            $"Level 5-8:  Karmaşık şekiller (piramit, kale, spiral)\n" +
            $"Level 9-10: Usta seviyesi (çoklu mekanik + süre limiti)\n\n" +
            $"Oyunu test edebilir veya bu dataseti AI modeline öğretebilirsiniz!", "Harika! 🚀");
    }

    // ═════════════════════════════════════════════════════════════
    // AI PARÇA YAPICI YARDIMCI VE GUI METODLARI
    // ═════════════════════════════════════════════════════════════

    private void ApplyPieceDifficultyScale(PMPieceDifficulty mode)
    {
        pmDifficulty = mode;
        switch (mode)
        {
            case PMPieceDifficulty.Kolay:
                pmMinSize = 2;
                pmMaxSize = 3;
                pmPieceType = PMPieceType.Geometric_Rect;
                break;
            case PMPieceDifficulty.Orta:
                pmMinSize = 3;
                pmMaxSize = 4;
                pmPieceType = PMPieceType.BFS_Free;
                break;
            case PMPieceDifficulty.Zor:
                pmMinSize = 4;
                pmMaxSize = 6;
                pmPieceType = PMPieceType.Symmetrical;
                break;
            case PMPieceDifficulty.Uzman:
                pmMinSize = 5;
                pmMaxSize = 8;
                pmPieceType = PMPieceType.PromptBased;
                pmPrompt = "complex jagged shapes";
                break;
        }
    }

    private void GeneratePiecesProcedurally()
    {
        pmGeneratedPieces.Clear();
        pmPieceColors.Clear();
        pmSelectedPieceIndex = -1;

        for (int i = 0; i < pmPieceCount; i++)
        {
            List<Vector3Int> piece = GenerateSinglePiece(pmPieceType, pmMinSize, pmMaxSize, pmPrompt, i);
            if (piece != null && piece.Count > 0)
            {
                // Normalize et
                int minX = piece.Min(c => c.x);
                int minY = piece.Min(c => c.y);
                int minZ = piece.Min(c => c.z);
                var shift = new Vector3Int(minX, minY, minZ);
                var normPiece = piece.Select(c => c - shift).ToList();

                pmGeneratedPieces.Add(normPiece);
                pmPieceColors.Add(i % PIECE_COLORS.Length);
            }
        }

        if (pmGeneratedPieces.Count > 0)
        {
            pmSelectedPieceIndex = 0;
        }
        Repaint();
        Debug.Log($"🤖 AI Piece Maker: Generated {pmGeneratedPieces.Count} pieces using strategy {pmPieceType}");
    }

    private List<Vector3Int> GenerateSinglePiece(PMPieceType type, int minSize, int maxSize, string prompt, int seedOffset)
    {
        Random.InitState(System.DateTime.Now.Millisecond + seedOffset * 313 + seedOffset);
        int targetSize = Random.Range(minSize, maxSize + 1);
        List<Vector3Int> piece = new List<Vector3Int>();

        if (type == PMPieceType.ClassicTetris)
        {
            var shapes = TETROMINO_BASE_SHAPES;
            var chosen = shapes[Random.Range(0, shapes.Length)];
            foreach (var c in chosen)
            {
                piece.Add(new Vector3Int(c.x, 0, c.y));
            }
            return piece;
        }

        if (type == PMPieceType.Geometric_Rect)
        {
            int w = 1, d = 1;
            if (targetSize == 2) { w = 2; d = 1; }
            else if (targetSize == 3) { w = 3; d = 1; }
            else if (targetSize == 4) { if (Random.value < 0.5f) { w = 4; d = 1; } else { w = 2; d = 2; } }
            else if (targetSize == 6) { if (Random.value < 0.5f) { w = 3; d = 2; } else { w = 6; d = 1; } }
            else
            {
                w = Random.Range(1, targetSize);
                d = Mathf.Max(1, targetSize / w);
            }
            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < d; z++)
                {
                    piece.Add(new Vector3Int(x, 0, z));
                }
            }
            while (piece.Count < targetSize)
            {
                var neighbors = GetHorizontalNeighborsOfPiece(piece);
                if (neighbors.Count == 0) break;
                piece.Add(neighbors[Random.Range(0, neighbors.Count)]);
            }
            return piece;
        }

        if (type == PMPieceType.BFS_Free)
        {
            piece.Add(Vector3Int.zero);
            while (piece.Count < targetSize)
            {
                var neighbors = GetHorizontalNeighborsOfPiece(piece);
                if (neighbors.Count == 0) break;
                piece.Add(neighbors[Random.Range(0, neighbors.Count)]);
            }
            return piece;
        }

        if (type == PMPieceType.Symmetrical)
        {
            int halfSize = Mathf.Max(1, targetSize / 2);
            var halfPiece = new List<Vector3Int> { Vector3Int.zero };
            while (halfPiece.Count < halfSize)
            {
                var neighbors = GetHorizontalNeighborsOfPiece(halfPiece);
                if (neighbors.Count == 0) break;
                var filtered = neighbors.Where(n => n.x >= 0).ToList();
                var listToUse = filtered.Count > 0 ? filtered : neighbors;
                halfPiece.Add(listToUse[Random.Range(0, listToUse.Count)]);
            }

            piece = new List<Vector3Int>(halfPiece);
            foreach (var c in halfPiece)
            {
                if (c.x != 0)
                {
                    piece.Add(new Vector3Int(-c.x, 0, c.z));
                }
            }

            if (piece.Count > targetSize)
            {
                piece = piece.Take(targetSize).ToList();
            }
            else if (piece.Count < targetSize)
            {
                while (piece.Count < targetSize)
                {
                    var neighbors = GetHorizontalNeighborsOfPiece(piece);
                    if (neighbors.Count == 0) break;
                    piece.Add(neighbors[Random.Range(0, neighbors.Count)]);
                }
            }
            return piece;
        }

        if (type == PMPieceType.PromptBased)
        {
            string p = prompt.ToLower();
            if (p.Contains("l-shaped") || p.Contains("corner") || p.Contains("l ") || p.Contains("köşe"))
            {
                int length1 = Random.Range(2, Mathf.Max(3, targetSize - 1));
                int length2 = targetSize - length1;
                for (int x = 0; x < length1; x++) piece.Add(new Vector3Int(x, 0, 0));
                for (int z = 1; z <= length2; z++) piece.Add(new Vector3Int(0, 0, z));
                return piece;
            }
            else if (p.Contains("flat") || p.Contains("plate") || p.Contains("düz") || p.Contains("line"))
            {
                for (int x = 0; x < targetSize; x++) piece.Add(new Vector3Int(x, 0, 0));
                return piece;
            }
            else if (p.Contains("t-shaped") || p.Contains("plus") || p.Contains("artı") || p.Contains("t "))
            {
                int crossLen = Random.Range(3, Mathf.Max(4, targetSize));
                int stemLen = targetSize - crossLen;
                for (int x = 0; x < crossLen; x++) piece.Add(new Vector3Int(x, 0, 1));
                int midX = crossLen / 2;
                for (int z = 0; z < stemLen; z++) piece.Add(new Vector3Int(midX, 0, -z));
                return piece;
            }
            else if (p.Contains("stair") || p.Contains("basamak") || p.Contains("diagonal") || p.Contains("zigzag"))
            {
                int currX = 0, currZ = 0;
                piece.Add(new Vector3Int(currX, 0, currZ));
                while (piece.Count < targetSize)
                {
                    if (Random.value < 0.5f) currX++; else currZ++;
                    piece.Add(new Vector3Int(currX, 0, currZ));
                }
                return piece;
            }
            else if (p.Contains("compact") || p.Contains("box") || p.Contains("kutu"))
            {
                int side = Mathf.RoundToInt(Mathf.Sqrt(targetSize));
                for (int x = 0; x < side; x++)
                {
                    for (int z = 0; z < side; z++)
                    {
                        if (piece.Count < targetSize) piece.Add(new Vector3Int(x, 0, z));
                    }
                }
                while (piece.Count < targetSize)
                {
                    var neighbors = GetHorizontalNeighborsOfPiece(piece);
                    if (neighbors.Count == 0) break;
                    piece.Add(neighbors[Random.Range(0, neighbors.Count)]);
                }
                return piece;
            }
            else
            {
                piece.Add(Vector3Int.zero);
                while (piece.Count < targetSize)
                {
                    var neighbors = GetHorizontalNeighborsOfPiece(piece);
                    if (neighbors.Count == 0) break;
                    piece.Add(neighbors[Random.Range(0, neighbors.Count)]);
                }
                return piece;
            }
        }

        return piece;
    }

    private List<Vector3Int> GetHorizontalNeighborsOfPiece(List<Vector3Int> piece)
    {
        var neighbors = new List<Vector3Int>();
        var dirs = new Vector3Int[] { Vector3Int.right, Vector3Int.left, new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };
        foreach (var c in piece)
        {
            foreach (var dir in dirs)
            {
                Vector3Int n = c + dir;
                if (!piece.Contains(n) && !neighbors.Contains(n))
                {
                    neighbors.Add(n);
                }
            }
        }
        return neighbors;
    }

    private void DrawPieceMakerLeftPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(420), GUILayout.ExpandHeight(true));
        pmLeftScroll = EditorGUILayout.BeginScrollView(pmLeftScroll);

        GUILayout.Label("🤖 YAPAY ZEKA PARÇA ÜRETİM AYARLARI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.HelpBox("Yapay Zeka geometrik kurallar ve promptunuza göre benzersiz parçalar tasarlar.", MessageType.Info);
        
        pmPiecePrefix = EditorGUILayout.TextField("Parça Adı Öneki", pmPiecePrefix);
        pmPieceCount = EditorGUILayout.IntSlider("Üretilecek Parça Sayısı", pmPieceCount, 1, 12);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("🏆 ZORLUK VE KURAL SETLERİ", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Zorluk Seviyesi Seçimi (Hızlı Ayar):", EditorStyles.miniBoldLabel);
        
        EditorGUILayout.BeginHorizontal();
        
        // Kolay
        GUI.backgroundColor = pmDifficulty == PMPieceDifficulty.Kolay ? Color.green : new Color(0.7f, 1f, 0.7f, 0.4f);
        if (GUILayout.Button("KOLAY", EditorStyles.miniButtonLeft, GUILayout.Height(22)))
        {
            ApplyPieceDifficultyScale(PMPieceDifficulty.Kolay);
        }
        
        // Orta
        GUI.backgroundColor = pmDifficulty == PMPieceDifficulty.Orta ? new Color(1.0f, 0.6f, 0.0f) : new Color(1f, 0.8f, 0.5f, 0.4f);
        if (GUILayout.Button("ORTA", EditorStyles.miniButtonMid, GUILayout.Height(22)))
        {
            ApplyPieceDifficultyScale(PMPieceDifficulty.Orta);
        }
        
        // Zor
        GUI.backgroundColor = pmDifficulty == PMPieceDifficulty.Zor ? new Color(0.9f, 0.2f, 0.2f) : new Color(1f, 0.6f, 0.6f, 0.4f);
        if (GUILayout.Button("ZOR", EditorStyles.miniButtonMid, GUILayout.Height(22)))
        {
            ApplyPieceDifficultyScale(PMPieceDifficulty.Zor);
        }
        
        // Uzman
        GUI.backgroundColor = pmDifficulty == PMPieceDifficulty.Uzman ? new Color(0.7f, 0.1f, 0.8f) : new Color(0.85f, 0.6f, 0.9f, 0.4f);
        if (GUILayout.Button("UZMAN", EditorStyles.miniButtonRight, GUILayout.Height(22)))
        {
            ApplyPieceDifficultyScale(PMPieceDifficulty.Uzman);
        }
        
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("🧩 GEOMETRİK PARÇA PARAMETRELERİ", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        pmPieceType = (PMPieceType)EditorGUILayout.EnumPopup("Parça Üretim Modu", pmPieceType);
        
        bool isClassic = pmPieceType == PMPieceType.ClassicTetris;
        EditorGUI.BeginDisabledGroup(isClassic);
        
        EditorGUILayout.BeginHorizontal();
        pmMinSize = EditorGUILayout.IntSlider("Min Hücre Sayısı", pmMinSize, 1, 10);
        pmMaxSize = EditorGUILayout.IntSlider("Max Hücre Sayısı", pmMaxSize, pmMinSize, 18);
        EditorGUILayout.EndHorizontal();
        
        EditorGUI.EndDisabledGroup();

        if (isClassic)
        {
            EditorGUILayout.HelpBox("🧩 Klasik Tetris modu: 7 temel Tetromino şeklini (I, O, T, S, Z, J, L) rastgele üretir. Hücre sayıları 4'e sabitlenir.", MessageType.Info);
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("💬 YAPAY ZEKA PROMPT GİRİŞİ", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        bool isPromptMode = pmPieceType == PMPieceType.PromptBased;
        EditorGUI.BeginDisabledGroup(!isPromptMode);
        pmPrompt = EditorGUILayout.TextField("AI Prompt", pmPrompt);
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.LabelField("💡 Prompt Önerileri: 'l-shaped', 'flat line', 't-shaped plus', 'stair step zigzag', 'compact box'", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(12);
        GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 1f); // AI Magenta
        if (GUILayout.Button("⚡ PARÇALARI YAPAY ZEKA İLE OLUŞTUR", GUILayout.Height(40)))
        {
            GeneratePiecesProcedurally();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(15);
        GUILayout.Label("✍️ MANUEL TASARIM VE ÖĞRETİM MERKEZİ", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.HelpBox("Önce boş tuval oluşturun, sağdaki grid üzerinde istediğiniz şekli boyayın, ardından yapay zekaya öğretmek için aşağıdaki butonu kullanın.", MessageType.Info);
        
        if (GUILayout.Button("🆕 BOŞ TASARIM TUVALİ AÇ", GUILayout.Height(30)))
        {
            pmGeneratedPieces.Clear();
            pmGeneratedPieces.Add(new List<Vector3Int>());
            pmPieceColors.Clear();
            pmPieceColors.Add(0);
            pmSelectedPieceIndex = 0;
            Repaint();
        }

        GUILayout.Space(8);
        pmManualPieceLabel = EditorGUILayout.TextField("Tasarım Etiketi (Prompt/Tag)", pmManualPieceLabel);

        GUI.backgroundColor = new Color(0.2f, 0.6f, 1f, 1f); // Mavi
        if (GUILayout.Button("🎓 BU TASARIMI EĞİTİM VERİSETİNE EKLE", GUILayout.Height(35)))
        {
            TeachCurrentPieceToAI();
        }
        GUI.backgroundColor = Color.white;


        GUILayout.Space(5);
        GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 1f); // AI Pink
        if (GUILayout.Button("📥 BU TASARIMI KÜTÜPHANEYE EKLE (LİSTELE)", GUILayout.Height(35)))
        {
            AddCurrentPieceToManualLibrary();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();


        // Hafızadaki eğitilmiş parçaları göster
        if (pmTaughtPieces.Count > 0)
        {
            GUILayout.Space(15);
            GUILayout.Label($"📚 ÖĞRETİLEN MANUEL PARÇALAR ({pmTaughtPieces.Count})", styleHeader);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            pmManualScroll = EditorGUILayout.BeginScrollView(pmManualScroll, GUILayout.Height(120));
            for (int i = 0; i < pmTaughtPieces.Count; i++)
            {
                var taught = pmTaughtPieces[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                
                EditorGUILayout.LabelField($"#{i + 1}: {taught.prompt} ({taught.cubeCount} Küp - {taught.shapeClassification})", EditorStyles.miniLabel);
                
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Sil", GUILayout.Width(40), GUILayout.Height(15)))
                {
                    pmTaughtPieces.RemoveAt(i);
                    Repaint();
                }
                GUI.backgroundColor = Color.white;
                
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(5);
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f, 1f); // Yeşil
            if (GUILayout.Button("💾 EĞİTİM VERİSİNİ JSON OLARAK KAYDET", GUILayout.Height(35)))
            {
                SaveManualDatasetToJson();
            }
            GUI.backgroundColor = Color.white;
            
            if (GUILayout.Button("🗑 TÜM EĞİTİLENLERİ TEMİZLE", GUILayout.Height(20)))
            {
                if (EditorUtility.DisplayDialog("Emin misiniz?", "Tüm eğitilen manuel parçaları hafızadan temizlemek istiyor musunuz?", "Evet", "Hayır"))
                {
                    pmTaughtPieces.Clear();
                    Repaint();
                }
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }


    private void DrawPieceMakerCenterGrid()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("📍 SEÇİLİ PARÇAYI DÜZENLE / ÖNİZLE", styleHeader);
        EditorGUILayout.BeginHorizontal();
        if (pmGeneratedPieces == null || pmGeneratedPieces.Count == 0)
        {
            GUILayout.Label("Henüz parça üretilmedi.", EditorStyles.miniBoldLabel);
        }
        else
        {
            for (int i = 0; i < pmGeneratedPieces.Count; i++)
            {
                bool isActive = (i == pmSelectedPieceIndex);
                GUI.backgroundColor = isActive ? COL_HEADER : new Color(0.85f, 0.85f, 0.85f);
                string lbl = $"Parça {i + 1}\n({pmGeneratedPieces[i].Count} Blok)";
                if (GUILayout.Button(lbl, GUILayout.Height(32), GUILayout.Width(90)))
                {
                    pmSelectedPieceIndex = i;
                    Repaint();
                }
            }
            GUI.backgroundColor = Color.white;
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        
        if (pmSelectedPieceIndex >= 0 && pmSelectedPieceIndex < pmGeneratedPieces.Count)
        {
            DrawPieceMakerGrid2D(area);
        }
        else
        {
            EditorGUI.DrawRect(area, COL_BG);
            var centerStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            centerStyle.normal.textColor = Color.gray;
            GUI.Label(area, "Lütfen soldan 'Yapay Zeka ile Oluştur' butonuna basarak parça üretin.", centerStyle);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawPieceMakerGrid2D(Rect area)
    {
        Color COL_HOVER_ASSIGN = new Color(0.2f, 0.8f, 0.2f, 0.4f);
        Color COL_HOVER_REMOVE = new Color(0.8f, 0.2f, 0.2f, 0.4f);

        var cells = pmGeneratedPieces[pmSelectedPieceIndex];
        int maxPieceX = cells.Count > 0 ? cells.Max(c => c.x) : 0;
        int maxPieceZ = cells.Count > 0 ? cells.Max(c => c.z) : 0;
        
        int W = Mathf.Max(5, maxPieceX + 1);
        int D = Mathf.Max(5, maxPieceZ + 1);

        float tw = cellPx * W, th = cellPx * D;
        float ox = area.x + (area.width - tw) * 0.5f;
        float oy = area.y + (area.height - th) * 0.5f;

        EditorGUI.DrawRect(area, COL_BG);

        // Lines
        for (int x = 0; x <= W; x++)
            EditorGUI.DrawRect(new Rect(ox + x * cellPx, oy, 1, th), COL_GRID);
        for (int z = 0; z <= D; z++)
            EditorGUI.DrawRect(new Rect(ox, oy + z * cellPx, tw, 1), COL_GRID);

        // Draw cells
        Color fill = PIECE_COLORS[pmSelectedPieceIndex % PIECE_COLORS.Length];
        foreach (var cell in cells)
        {
            if (cell.x >= W || cell.z >= D) continue;
            Rect cellRect = new Rect(ox + cell.x * cellPx + 1.5f, oy + cell.z * cellPx + 1.5f, cellPx - 3, cellPx - 3);
            EditorGUI.DrawRect(cellRect, fill);
        }

        // Draw hover cell and handle clicks

        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;
        if (area.Contains(mousePos))
        {
            int hx = Mathf.FloorToInt((mousePos.x - ox) / cellPx);
            int hz = Mathf.FloorToInt((mousePos.y - oy) / cellPx);

            if (hx >= 0 && hx < W && hz >= 0 && hz < D)
            {
                Rect hoverRect = new Rect(ox + hx * cellPx + 1.5f, oy + hz * cellPx + 1.5f, cellPx - 3, cellPx - 3);
                bool hasCell = cells.Any(c => c.x == hx && c.z == hz);

                if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
                {
                    if (e.button == 0) // Left click: Add
                    {
                        if (!hasCell)
                        {
                            cells.Add(new Vector3Int(hx, 0, hz));
                            NormalizeSelectedPMPiece();
                            Repaint();
                        }
                        e.Use();
                    }
                    else if (e.button == 1) // Right click: Remove
                    {
                        if (hasCell)
                        {
                            cells.RemoveAll(c => c.x == hx && c.z == hz);
                            NormalizeSelectedPMPiece();
                            Repaint();
                        }
                        e.Use();
                    }
                }

                EditorGUI.DrawRect(hoverRect, hasCell ? COL_HOVER_REMOVE : COL_HOVER_ASSIGN);
                Repaint();
            }
        }

        // Labels
        var lbl = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = new Color(1,1,1,0.32f) } };
        for (int x = 0; x < W; x++) GUI.Label(new Rect(ox + x * cellPx, oy - 14, cellPx, 14), x.ToString(), lbl);
        for (int z = 0; z < D; z++) GUI.Label(new Rect(ox - 18, oy + z * cellPx, 18, cellPx), z.ToString(), lbl);
        GUI.Label(new Rect(ox - 18, oy - 14, 18, 14), "Z\\x", lbl);
    }

    private void NormalizeSelectedPMPiece()
    {
        if (pmSelectedPieceIndex < 0 || pmSelectedPieceIndex >= pmGeneratedPieces.Count) return;
        var cells = pmGeneratedPieces[pmSelectedPieceIndex];
        if (cells.Count == 0) return;

        int minX = cells.Min(c => c.x);
        int minY = cells.Min(c => c.y);
        int minZ = cells.Min(c => c.z);
        
        for (int i = 0; i < cells.Count; i++)
        {
            cells[i] = new Vector3Int(cells[i].x - minX, cells[i].y - minY, cells[i].z - minZ);
        }
    }

    private void DrawPieceMakerRightPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(220), GUILayout.ExpandHeight(true));

        // Tab selection bar
        int nextTab = GUILayout.Toolbar(pmRightTab, new string[] { "🤖 AI LİSTE", "✍️ MANUEL LİSTE" }, GUILayout.Height(25));
        if (nextTab != pmRightTab)
        {
            pmRightTab = nextTab;
            Repaint();
        }

        pmRightScroll = EditorGUILayout.BeginScrollView(pmRightScroll);

        if (pmRightTab == 0) // AI Generated Pieces
        {
            GUILayout.Label("🤖 YAPAY ZEKA PARÇALARI", styleHeader);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            if (pmGeneratedPieces == null || pmGeneratedPieces.Count == 0)
            {
                EditorGUILayout.LabelField("Henüz parça üretilmedi.\n\nSoldaki 'PARÇALARI OLUŞTUR' butonuna basarak yapay zekayla parça üretebilirsiniz.", EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                for (int i = 0; i < pmGeneratedPieces.Count; i++)
                {
                    var piece = pmGeneratedPieces[i];
                    if (piece == null || piece.Count == 0) continue;

                    Color col = PIECE_COLORS[i % PIECE_COLORS.Length];
                    Color prevBG = GUI.backgroundColor;
                    if (pmSelectedPieceIndex == i)
                    {
                        GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 0.6f);
                    }
                    
                    Rect pieceClickRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = prevBG;

                    EditorGUILayout.BeginHorizontal();
                    Rect colorRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12), GUILayout.Height(12));
                    colorRect.y += 2;
                    EditorGUI.DrawRect(colorRect, col);
                    GUILayout.Space(4);
                    
                    var itemLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                    if (pmSelectedPieceIndex == i) itemLabelStyle.normal.textColor = Color.white;
                    EditorGUILayout.LabelField($"Parça {i + 1} ({piece.Count} Blok)", itemLabelStyle);
                    EditorGUILayout.EndHorizontal();

                    // Draw miniature 2D preview
                    int minX = piece.Min(c => c.x);
                    int maxX = piece.Max(c => c.x);
                    int minZ = piece.Min(c => c.z);
                    int maxZ = piece.Max(c => c.z);

                    int w = maxX - minX + 1;
                    int h = maxZ - minZ + 1;

                    float previewBlockSize = 14f;
                    float previewWidth = w * previewBlockSize;
                    float previewHeight = h * previewBlockSize;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(18);
                    Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.Width(previewWidth), GUILayout.Height(previewHeight));
                    EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.15f));

                    foreach (var cell in piece)
                    {
                        int rx = cell.x - minX;
                        int rz = cell.z - minZ;
                        Rect blockRect = new Rect(
                            previewRect.x + rx * previewBlockSize + 0.5f,
                            previewRect.y + rz * previewBlockSize + 0.5f,
                            previewBlockSize - 1f,
                            previewBlockSize - 1f
                        );
                        EditorGUI.DrawRect(blockRect, col);
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();

                    if (Event.current.type == EventType.MouseDown && pieceClickRect.Contains(Event.current.mousePosition))
                    {
                        pmSelectedPieceIndex = i;
                        Repaint();
                        Event.current.Use();
                    }

                    GUILayout.Space(4);
                }
            }
            EditorGUILayout.EndVertical();

            if (pmSelectedPieceIndex >= 0 && pmSelectedPieceIndex < pmGeneratedPieces.Count)
            {
                var piece = pmGeneratedPieces[pmSelectedPieceIndex];
                if (piece != null && piece.Count > 0)
                {
                    int minX = piece.Min(c => c.x);
                    int maxX = piece.Max(c => c.x);
                    int minZ = piece.Min(c => c.z);
                    int maxZ = piece.Max(c => c.z);
                    int w = maxX - minX + 1;
                    int h = maxZ - minZ + 1;
                    float compactness = (float)piece.Count / (w * h);
                    
                    bool isSym = CheckPieceSymmetry(piece);
                    string classification = ClassifyPieceShape(piece);

                    GUILayout.Space(10);
                    GUILayout.Label("📊 PARÇA GEOMETRİK ANALİZİ", styleHeader);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"• Toplam Küp: {piece.Count}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"• Boyutlar (GxD): {w} x {h}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"• Yoğunluk (Compact): %{compactness * 100f:F0}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"• Simetri: {(isSym ? "Simetrik" : "Asimetrik")}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"• Şekil Tipi: {classification}", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
            }

            GUILayout.Space(12);
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f, 1f);
            if (GUILayout.Button("💾 TÜMÜNÜ DIŞA AKTAR\n(PREFAB & ASSET YAP)", GUILayout.Height(50)))
            {
                ExportGeneratedPieces();
            }
            GUI.backgroundColor = Color.white;
        }
        else // Manual Designs Library
        {
            GUILayout.Label("✍️ MANUEL TASARIMLARINIZ", styleHeader);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (pmManualPiecesList.Count == 0)
            {
                EditorGUILayout.LabelField("Kütüphanede henüz manuel tasarımınız yok.\n\nSoldaki tuvali boyayıp 'Kütüphaneye Ekle' butonuna basarak ekleyebilirsiniz.", EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                for (int i = 0; i < pmManualPiecesList.Count; i++)
                {
                    var piece = pmManualPiecesList[i];
                    if (piece == null || piece.Count == 0) continue;

                    Color col = PIECE_COLORS[i % PIECE_COLORS.Length];
                    Color prevBG = GUI.backgroundColor;
                    if (pmSelectedManualIndex == i)
                    {
                        GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 0.6f);
                    }

                    Rect pieceClickRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = prevBG;

                    EditorGUILayout.BeginHorizontal();
                    Rect colorRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12), GUILayout.Height(12));
                    colorRect.y += 2;
                    EditorGUI.DrawRect(colorRect, col);
                    GUILayout.Space(4);

                    var itemLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                    if (pmSelectedManualIndex == i) itemLabelStyle.normal.textColor = Color.white;
                    EditorGUILayout.LabelField($"{pmManualPieceNames[i]} ({piece.Count} Küp)", itemLabelStyle);
                    EditorGUILayout.EndHorizontal();

                    // Draw miniature 2D preview
                    int minX = piece.Min(c => c.x);
                    int maxX = piece.Max(c => c.x);
                    int minZ = piece.Min(c => c.z);
                    int maxZ = piece.Max(c => c.z);

                    int w = maxX - minX + 1;
                    int h = maxZ - minZ + 1;

                    float previewBlockSize = 14f;
                    float previewWidth = w * previewBlockSize;
                    float previewHeight = h * previewBlockSize;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(18);
                    Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.Width(previewWidth), GUILayout.Height(previewHeight));
                    EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.15f));

                    foreach (var cell in piece)
                    {
                        int rx = cell.x - minX;
                        int rz = cell.z - minZ;
                        Rect blockRect = new Rect(
                            previewRect.x + rx * previewBlockSize + 0.5f,
                            previewRect.y + rz * previewBlockSize + 0.5f,
                            previewBlockSize - 1f,
                            previewBlockSize - 1f
                        );
                        EditorGUI.DrawRect(blockRect, col);
                    }
                    EditorGUILayout.EndHorizontal();

                    GUILayout.Space(6);
                    EditorGUILayout.BeginHorizontal();
                    
                    // Düzenle
                    if (GUILayout.Button("Düzenle", EditorStyles.miniButtonLeft))
                    {
                        // Load into paint canvas
                        pmGeneratedPieces.Clear();
                        pmGeneratedPieces.Add(new List<Vector3Int>(piece));
                        pmPieceColors.Clear();
                        pmPieceColors.Add(i % PIECE_COLORS.Length);
                        pmSelectedPieceIndex = 0;
                        pmManualPieceLabel = pmManualPieceNames[i];
                        Repaint();
                    }

                    // Prefab Yap
                    if (GUILayout.Button("Prefab Yap", EditorStyles.miniButtonMid))
                    {
                        ExportSinglePieceToPrefab(piece, pmManualPieceNames[i]);
                    }

                    // Sil
                    GUI.backgroundColor = Color.red;
                    if (GUILayout.Button("Sil", EditorStyles.miniButtonRight))
                    {
                        if (EditorUtility.DisplayDialog("Emin misiniz?", $"'{pmManualPieceNames[i]}' parçasını kütüphaneden silmek istiyor musunuz?", "Evet", "Hayır"))
                        {
                            pmManualPiecesList.RemoveAt(i);
                            pmManualPieceNames.RemoveAt(i);
                            SaveManualLibraryToJson();
                            pmSelectedManualIndex = -1;
                            Repaint();
                        }
                    }
                    GUI.backgroundColor = Color.white;

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();

                    if (Event.current.type == EventType.MouseDown && pieceClickRect.Contains(Event.current.mousePosition))
                    {
                        pmSelectedManualIndex = i;
                        Repaint();
                        Event.current.Use();
                    }

                    GUILayout.Space(4);
                }
            }
            EditorGUILayout.EndVertical();

            if (pmSelectedManualIndex >= 0 && pmSelectedManualIndex < pmManualPiecesList.Count)
            {
                var piece = pmManualPiecesList[pmSelectedManualIndex];
                if (piece != null && piece.Count > 0)
                {
                    int minX = piece.Min(c => c.x);
                    int maxX = piece.Max(c => c.x);
                    int minZ = piece.Min(c => c.z);
                    int maxZ = piece.Max(c => c.z);
                    int w = maxX - minX + 1;
                    int h = maxZ - minZ + 1;
                    float compactness = (float)piece.Count / (w * h);
                    
                    bool isSym = CheckPieceSymmetry(piece);
                    string classification = ClassifyPieceShape(piece);

                    GUILayout.Space(10);
                    GUILayout.Label("📊 PARÇA GEOMETRİK ANALİZİ", styleHeader);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"• Toplam Küp: {piece.Count}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"• Boyutlar (GxD): {w} x {h}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"• Yoğunluk (Compact): %{compactness * 100f:F0}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"• Simetri: {(isSym ? "Simetrik" : "Asimetrik")}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"• Şekil Tipi: {classification}", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
            }

            GUILayout.Space(12);
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f, 1f);
            if (GUILayout.Button("📦 TÜM MANUELLERİ DIŞA AKTAR\n(PREFAB & ASSET YAP)", GUILayout.Height(50)))
            {
                ExportAllManualPieces();
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }


    private bool CheckPieceSymmetry(List<Vector3Int> piece)
    {
        if (piece.Count <= 1) return true;
        int minX = piece.Min(c => c.x);
        int maxX = piece.Max(c => c.x);
        int minZ = piece.Min(c => c.z);
        int maxZ = piece.Max(c => c.z);
        
        bool symX = true;
        foreach (var c in piece)
        {
            int mirroredX = maxX - (c.x - minX);
            if (!piece.Any(p => p.x == mirroredX && p.z == c.z))
            {
                symX = false;
                break;
            }
        }
        
        bool symZ = true;
        foreach (var c in piece)
        {
            int mirroredZ = maxZ - (c.z - minZ);
            if (!piece.Any(p => p.x == c.x && p.z == mirroredZ))
            {
                symZ = false;
                break;
            }
        }
        
        return symX || symZ;
    }

    private string ClassifyPieceShape(List<Vector3Int> piece)
    {
        if (piece.Count == 0) return "Boş";
        if (piece.Count == 1) return "Tekli Dolgu (Filler)";
        
        int minX = piece.Min(c => c.x);
        int maxX = piece.Max(c => c.x);
        int minZ = piece.Min(c => c.z);
        int maxZ = piece.Max(c => c.z);
        int w = maxX - minX + 1;
        int h = maxZ - minZ + 1;

        if (w == 1 || h == 1) return "Düz Çubuk (Flat Line)";
        if (w == 2 && h == 2 && piece.Count == 4) return "Kare Kutu (Classic O)";
        
        bool hasCorner = false;
        foreach (var c in piece)
        {
            int neighborCount = 0;
            if (piece.Any(p => p.x == c.x + 1 && p.z == c.z)) neighborCount++;
            if (piece.Any(p => p.x == c.x - 1 && p.z == c.z)) neighborCount++;
            if (piece.Any(p => p.x == c.x && p.z == c.z + 1)) neighborCount++;
            if (piece.Any(p => p.x == c.x && p.z == c.z - 1)) neighborCount++;
            
            if (neighborCount >= 3) return "T-Şekli / Artı (T-Shape/Cross)";
            if (neighborCount == 2)
            {
                bool xNeigh = piece.Any(p => p.x == c.x + 1 && p.z == c.z) || piece.Any(p => p.x == c.x - 1 && p.z == c.z);
                bool zNeigh = piece.Any(p => p.x == c.x && p.z == c.z + 1) || piece.Any(p => p.x == c.x && p.z == c.z - 1);
                if (xNeigh && zNeigh) hasCorner = true;
            }
        }
        
        float compactness = (float)piece.Count / (w * h);
        if (compactness >= 0.8f) return "Kompakt Kutu (Box)";
        if (hasCorner && piece.Count <= 5) return "L-Şekli (Corner)";
        if (compactness <= 0.5f) return "Diagonal / Zigzag";
        
        return "Karmaşık / Serbest (Jagged)";
    }

    private void ExportGeneratedPieces()
    {
        if (pmGeneratedPieces.Count == 0)
        {
            EditorUtility.DisplayDialog("Hata", "Dışa aktarılacak parça yok. Önce parça üretin.", "Tamam");
            return;
        }

        string folderPath = PieceTemplateLibrary.FOLDER;
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        int successCount = 0;
        float step = cellSize + spacing;

        for (int i = 0; i < pmGeneratedPieces.Count; i++)
        {
            var piece = pmGeneratedPieces[i];
            if (piece.Count == 0) continue;

            string name = $"{pmPiecePrefix}{i + 1}";
            string prefabPath = $"{folderPath}/{name}.prefab";
            string assetPath = $"{folderPath}/{name}.asset";

            // 1. Create ScriptableObject (CubeShapeData)
            CubeShapeData data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(assetPath);
            bool isNewAsset = data == null;
            if (isNewAsset) data = ScriptableObject.CreateInstance<CubeShapeData>();

            data.shapeName = name;
            data.gridSize = new Vector3Int(piece.Max(c => c.x) + 1, 1, piece.Max(c => c.z) + 1);
            data.cellSize = cellSize;
            data.spacing = spacing;
            data.occupiedCells = new List<Vector3Int>(piece);

            if (isNewAsset) AssetDatabase.CreateAsset(data, assetPath);
            else EditorUtility.SetDirty(data);

            // 2. Create Prefab
            GameObject pRoot = new GameObject(name);
            var ph = pRoot.AddComponent<CubeShapeDataHolder>();
            ph.shapeName = name;
            ph.gridSize = data.gridSize;
            ph.cellSize = cellSize;
            ph.spacing = spacing;
            ph.occupiedCells = new List<Vector3Int>(piece);

            foreach (var cell in piece)
            {
                GameObject cube = cubePrefab != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(pRoot.transform);
                cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
                cube.transform.localScale = Vector3.one * cellSize;
                cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            }

            PrefabUtility.SaveAsPrefabAsset(pRoot, prefabPath);
            DestroyImmediate(pRoot);

            successCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("AI Parça Export Başarılı!",
            $"🤖 Yapay Zeka Parça Yapıcı Başarıyla Kaydetti:\n" +
            $"✅ {successCount} Adet CubeShapeData ScriptableObject\n" +
            $"✅ {successCount} Adet Parça Prefabı\n\nHedef Dizin: {folderPath}/", "Harika!");
    }

    private void DrawPieceMakerStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(22));
        EditorGUILayout.LabelField($"BlockMerge3D  •  AI Parça Jeneratörü  •  Durum: {(pmGeneratedPieces.Count > 0 ? "Parçalar Hazır" : "Hazır")}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void GenerateAndExportPieceDataset()
    {
        string origPrefix = pmPiecePrefix;
        int origCount = pmPieceCount;
        int origMin = pmMinSize;
        int origMax = pmMaxSize;
        PMPieceType origType = pmPieceType;
        PMPieceDifficulty origDiff = pmDifficulty;
        string origPrompt = pmPrompt;

        AIPieceDatasetWrapper datasetWrapper = new AIPieceDatasetWrapper();

        for (int i = 0; i < 50; i++)
        {
            EditorUtility.DisplayProgressBar("Parça Veriseti Üretimi", $"Parça {i + 1}/50 üretiliyor...", (i + 1) / 50f);

            PMPieceType type = (PMPieceType)(i / 10);
            int minSize = 2;
            int maxSize = 4;
            string prompt = "";

            if (type == PMPieceType.ClassicTetris)
            {
                minSize = 4; maxSize = 4;
            }
            else if (type == PMPieceType.Geometric_Rect)
            {
                minSize = 2; maxSize = 6;
            }
            else if (type == PMPieceType.BFS_Free)
            {
                minSize = 3; maxSize = 6;
            }
            else if (type == PMPieceType.Symmetrical)
            {
                minSize = 4; maxSize = 8;
            }
            else // PromptBased
            {
                minSize = 3; maxSize = 7;
                string[] prompts = { "L-shaped blocks", "flat plate lines", "T-shaped plus cross", "stair steps diagonal", "compact box shapes" };
                prompt = prompts[i % prompts.Length];
            }

            List<Vector3Int> piece = GenerateSinglePiece(type, minSize, maxSize, prompt, i);
            if (piece == null || piece.Count == 0) continue;

            int minX = piece.Min(c => c.x);
            int minY = piece.Min(c => c.y);
            int minZ = piece.Min(c => c.z);
            var shift = new Vector3Int(minX, minY, minZ);
            var normPiece = piece.Select(c => c - shift).ToList();

            int w = normPiece.Max(c => c.x) + 1;
            int h = normPiece.Max(c => c.z) + 1;
            float compactness = (float)normPiece.Count / (w * h);
            bool isSym = CheckPieceSymmetry(normPiece);
            string classification = ClassifyPieceShape(normPiece);

            AIPieceDatasetEntry entry = new AIPieceDatasetEntry
            {
                pieceIndex = i,
                generationMode = type.ToString(),
                prompt = prompt,
                cubeCount = normPiece.Count,
                dimensions = new SerializableCell(new Vector3Int(w, 1, h)),
                compactness = compactness,
                isSymmetrical = isSym,
                shapeClassification = classification,
                cells = normPiece.Select(c => new SerializableCell(c)).ToList()
            };

            datasetWrapper.dataset.Add(entry);
        }

        EditorUtility.ClearProgressBar();

        string jsonPath = $"{LEVELS_PATH}/ai_pieces_training_dataset.json";
        string jsonContent = JsonUtility.ToJson(datasetWrapper, true);
        File.WriteAllText(jsonPath, jsonContent);

        pmPiecePrefix = origPrefix;
        pmPieceCount = origCount;
        pmMinSize = origMin;
        pmMaxSize = origMax;
        pmPieceType = origType;
        pmDifficulty = origDiff;
        pmPrompt = origPrompt;

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("🎮 Parça Veriseti Başarıyla Oluşturuldu!",
            $"✅ Yapay Zeka Parça Veriseti Devrede:\n\n" +
            $"📊 {datasetWrapper.dataset.Count} Benzersiz Parça Oluşturuldu\n" +
            $"💾 Veriseti Kaydedildi:\n\t{jsonPath}\n\n" +
            $"Veriseti içerisinde her parçanın:\n" +
            $" - Hücre koordinatları\n" +
            $" - Kompaktlık oranı (compactness)\n" +
            $" - Simetri bilgisi\n" +
            $" - Şekil sınıflandırması (L-Shape, T-Shape, Line, Box, vb.)\n\n" +
            $"Yapay zekaya bu verileri öğreterek parça tanıma ve yerleştirme modelleri eğitebilirsiniz!", "Harika! 🚀");
    }

    private void TeachCurrentPieceToAI()
    {
        if (pmSelectedPieceIndex < 0 || pmSelectedPieceIndex >= pmGeneratedPieces.Count)
        {
            EditorUtility.DisplayDialog("Hata", "Seçili bir parça bulunamadı. Önce bir tasarım tuvali açın veya boyayın.", "Tamam");
            return;
        }

        var cells = pmGeneratedPieces[pmSelectedPieceIndex];
        if (cells == null || cells.Count == 0)
        {
            EditorUtility.DisplayDialog("Hata", "Tuval üzerinde en az 1 blok boyanmış olmalıdır.", "Tamam");
            return;
        }

        // Normalize the piece first
        NormalizeSelectedPMPiece();

        int w = cells.Max(c => c.x) + 1;
        int h = cells.Max(c => c.z) + 1;
        float compactness = (float)cells.Count / (w * h);
        bool isSym = CheckPieceSymmetry(cells);
        string classification = ClassifyPieceShape(cells);

        AIPieceDatasetEntry entry = new AIPieceDatasetEntry
        {
            pieceIndex = pmTaughtPieces.Count,
            generationMode = "Manuel_Tasarim",
            prompt = string.IsNullOrEmpty(pmManualPieceLabel) ? "custom_shape" : pmManualPieceLabel,
            cubeCount = cells.Count,
            dimensions = new SerializableCell(new Vector3Int(w, 1, h)),
            compactness = compactness,
            isSymmetrical = isSym,
            shapeClassification = classification,
            cells = cells.Select(c => new SerializableCell(c)).ToList()
        };

        pmTaughtPieces.Add(entry);
        Repaint();

        EditorUtility.DisplayDialog("Tasarım Hafızaya Alındı! 🎓",
            $"✅ Şekil Başarıyla Hafızaya Eklendi!\n\n" +
            $"• Boyut: {w}x{h}\n" +
            $"• Küp Sayısı: {cells.Count}\n" +
            $"• Sınıflandırma: {classification}\n" +
            $"• Etiket (Prompt): {entry.prompt}\n\n" +
            $"Farklı şekiller boyayıp eklemeye devam edebilir veya alttaki butonla JSON verisetine kaydedebilirsiniz.", "Tamam");
    }

    private void SaveManualDatasetToJson()
    {
        if (pmTaughtPieces.Count == 0)
        {
            EditorUtility.DisplayDialog("Hata", "Kaydedilecek eğitilmiş parça yok.", "Tamam");
            return;
        }

        string jsonPath = $"{LEVELS_PATH}/ai_pieces_manual_training_dataset.json";
        AIPieceDatasetWrapper wrapper = new AIPieceDatasetWrapper();
        wrapper.dataset = new List<AIPieceDatasetEntry>(pmTaughtPieces);

        string jsonContent = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(jsonPath, jsonContent);

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Manuel Eğitim Veriseti Kaydedildi! 💾",
            $"✅ {pmTaughtPieces.Count} Adet Manuel Parça Verisi Kaydedildi!\n\n" +
            $"Dosya Konumu:\n{jsonPath}\n\n" +
            $"Bu JSON dosyası, yapay zekanın sizin elinizle çizdiğiniz özel tasarımları, prompt etiketleriyle birlikte birebir öğrenmesi için eğitim verisi olarak kullanılacaktır.", "Harika!");
    }

    private void LoadManualPieceDataset()
    {
        string jsonPath = $"{LEVELS_PATH}/ai_pieces_manual_training_dataset.json";
        if (File.Exists(jsonPath))
        {
            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                AIPieceDatasetWrapper wrapper = JsonUtility.FromJson<AIPieceDatasetWrapper>(jsonContent);
                if (wrapper != null && wrapper.dataset != null)
                {
                    pmTaughtPieces = new List<AIPieceDatasetEntry>(wrapper.dataset);
                    Debug.Log($"📚 {pmTaughtPieces.Count} adet eğitilmiş parça verisi {jsonPath} dosyasından yüklendi.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Manuel parça dataseti yüklenirken hata oluştu: {ex.Message}");
            }
        }
    }

    private void AddCurrentPieceToManualLibrary()
    {
        if (pmSelectedPieceIndex < 0 || pmSelectedPieceIndex >= pmGeneratedPieces.Count)
        {
            EditorUtility.DisplayDialog("Hata", "Seçili bir parça bulunamadı. Önce bir tasarım tuvali açın veya boyayın.", "Tamam");
            return;
        }

        var cells = pmGeneratedPieces[pmSelectedPieceIndex];
        if (cells == null || cells.Count == 0)
        {
            EditorUtility.DisplayDialog("Hata", "Tuval üzerinde en az 1 blok boyanmış olmalıdır.", "Tamam");
            return;
        }

        // Normalize the piece first
        NormalizeSelectedPMPiece();

        string name = string.IsNullOrEmpty(pmManualPieceLabel) ? $"Manual_Piece_{pmManualPiecesList.Count + 1}" : pmManualPieceLabel;
        
        // Prevent duplicate names in manual list
        if (pmManualPieceNames.Contains(name))
        {
            name = $"{name}_{pmManualPiecesList.Count + 1}";
        }

        pmManualPiecesList.Add(new List<Vector3Int>(cells));
        pmManualPieceNames.Add(name);
        SaveManualLibraryToJson();

        pmRightTab = 1; // Switch right panel to Manual designs tab!
        pmSelectedManualIndex = pmManualPiecesList.Count - 1;
        Repaint();

        EditorUtility.DisplayDialog("Kütüphaneye Eklendi! 📥",
            $"✅ '{name}' başarıyla Manuel Tasarım Kütüphanesine eklendi!\n\n" +
            $"Sağ taraftaki 'MANUEL LİSTE' sekmesinden bu şekli görebilir, düzenleyebilir ve prefab olarak dışa aktarabilirsiniz.", "Tamam");
    }

    private void ExportSinglePieceToPrefab(List<Vector3Int> cells, string name)
    {
        if (cells == null || cells.Count == 0) return;

        string folderPath = "Assets/Pieces";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets", "Pieces");
        }

        string prefabPath = $"{folderPath}/{name}.prefab";
        string assetPath = $"{folderPath}/{name}.asset";

        // 1. Create ScriptableObject (CubeShapeData)
        CubeShapeData data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(assetPath);
        bool isNewAsset = data == null;
        if (isNewAsset) data = ScriptableObject.CreateInstance<CubeShapeData>();

        data.shapeName = name;
        data.gridSize = new Vector3Int(cells.Max(c => c.x) + 1, 1, cells.Max(c => c.z) + 1);
        data.cellSize = cellSize;
        data.spacing = spacing;
        data.occupiedCells = new List<Vector3Int>(cells);

        if (isNewAsset) AssetDatabase.CreateAsset(data, assetPath);
        else EditorUtility.SetDirty(data);

        // 2. Create Prefab
        GameObject pRoot = new GameObject(name);
        var ph = pRoot.AddComponent<CubeShapeDataHolder>();
        ph.shapeName = name;
        ph.gridSize = data.gridSize;
        ph.cellSize = cellSize;
        ph.spacing = spacing;
        ph.occupiedCells = new List<Vector3Int>(cells);

        float step = cellSize + spacing;
        foreach (var cell in cells)
        {
            GameObject cube = cubePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(pRoot.transform);
            cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
            cube.transform.localScale = Vector3.one * cellSize;
            cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
        }

        PrefabUtility.SaveAsPrefabAsset(pRoot, prefabPath);
        DestroyImmediate(pRoot);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Başarılı! 💾",
            $"✅ Parça Prefab ve ScriptableObject olarak dışa aktarıldı!\n\n" +
            $"• Prefab: {prefabPath}\n" +
            $"• Asset: {assetPath}", "Tamam");
    }

    private void ExportAllManualPieces()
    {
        if (pmManualPiecesList.Count == 0)
        {
            EditorUtility.DisplayDialog("Hata", "Dışa aktarılacak manuel parça yok.", "Tamam");
            return;
        }

        string folderPath = "Assets/Pieces";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets", "Pieces");
        }

        int count = 0;
        float step = cellSize + spacing;

        for (int i = 0; i < pmManualPiecesList.Count; i++)
        {
            string name = pmManualPieceNames[i];
            var cells = pmManualPiecesList[i];
            if (cells.Count == 0) continue;

            string prefabPath = $"{folderPath}/{name}.prefab";
            string assetPath = $"{folderPath}/{name}.asset";

            // ScriptableObject
            CubeShapeData data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(assetPath);
            bool isNewAsset = data == null;
            if (isNewAsset) data = ScriptableObject.CreateInstance<CubeShapeData>();

            data.shapeName = name;
            data.gridSize = new Vector3Int(cells.Max(c => c.x) + 1, 1, cells.Max(c => c.z) + 1);
            data.cellSize = cellSize;
            data.spacing = spacing;
            data.occupiedCells = new List<Vector3Int>(cells);

            if (isNewAsset) AssetDatabase.CreateAsset(data, assetPath);
            else EditorUtility.SetDirty(data);

            // Prefab
            GameObject pRoot = new GameObject(name);
            var ph = pRoot.AddComponent<CubeShapeDataHolder>();
            ph.shapeName = name;
            ph.gridSize = data.gridSize;
            ph.cellSize = cellSize;
            ph.spacing = spacing;
            ph.occupiedCells = new List<Vector3Int>(cells);

            foreach (var cell in cells)
            {
                GameObject cube = cubePrefab != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(pRoot.transform);
                cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (cellSize * 0.5f);
                cube.transform.localScale = Vector3.one * cellSize;
                cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            }

            PrefabUtility.SaveAsPrefabAsset(pRoot, prefabPath);
            DestroyImmediate(pRoot);
            count++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Toplu Dışa Aktarma Başarılı! 📦",
            $"✅ {count} adet manuel parça '{folderPath}/' klasörüne prefab ve asset olarak kaydedildi.", "Harika!");
    }

    private void SaveManualLibraryToJson()
    {
        string jsonPath = $"{LEVELS_PATH}/ai_pieces_manual_library.json";
        AIPieceListWrapper wrapper = new AIPieceListWrapper();
        for (int i = 0; i < pmManualPiecesList.Count; i++)
        {
            wrapper.names.Add(pmManualPieceNames[i]);
            var cells = pmManualPiecesList[i];
            AIPieceDatasetEntry entry = new AIPieceDatasetEntry
            {
                pieceIndex = i,
                prompt = pmManualPieceNames[i],
                cubeCount = cells.Count,
                cells = cells.Select(c => new SerializableCell(c)).ToList()
            };
            wrapper.pieces.Add(entry);
        }

        string jsonContent = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(jsonPath, jsonContent);
        AssetDatabase.Refresh();
    }

    private void LoadManualLibraryFromJson()
    {
        string jsonPath = $"{LEVELS_PATH}/ai_pieces_manual_library.json";
        if (File.Exists(jsonPath))
        {
            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                AIPieceListWrapper wrapper = JsonUtility.FromJson<AIPieceListWrapper>(jsonContent);
                if (wrapper != null && wrapper.pieces != null)
                {
                    pmManualPiecesList.Clear();
                    pmManualPieceNames.Clear();
                    for (int i = 0; i < wrapper.pieces.Count; i++)
                    {
                        var name = (i < wrapper.names.Count) ? wrapper.names[i] : $"Manual_Piece_{i + 1}";
                        var cells = wrapper.pieces[i].cells.Select(c => new Vector3Int(c.x, c.y, c.z)).ToList();
                        pmManualPiecesList.Add(cells);
                        pmManualPieceNames.Add(name);
                    }
                    Debug.Log($"📚 Manuel Kütüphaneden {pmManualPiecesList.Count} adet parça yüklendi.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Manuel kütüphane yüklenirken hata: {ex.Message}");
            }
        }
    }
}

[System.Serializable]
public struct SerializableCell
{
    public int x;
    public int y;
    public int z;
    public SerializableCell(Vector3Int v) { x = v.x; y = v.y; z = v.z; }
}

[System.Serializable]
public class AIPieceEntry
{
    public int pieceIndex;
    public List<SerializableCell> localCells;
}

[System.Serializable]
public class AIDatasetEntry
{
    public string levelName;
    public int difficultyIndex;
    public string shapeType;
    public SerializableCell gridSize;
    public List<SerializableCell> occupiedCells;
    public List<SerializableCell> prefilledCells;
    public List<int> prefilledMaterialIndices;
    public List<SerializableCell> frozenCells;
    public List<AIPieceEntry> pieces;
    
    // Solver sonuçları
    public int pieceCount;
    public float frozenRatio;
    public bool isSolvable;
    public int minMoveCount;
    public float difficultyScore;
    public string difficultyLabel;
}

[System.Serializable]
public class AIDatasetWrapper
{
    public List<AIDatasetEntry> dataset = new List<AIDatasetEntry>();
}

[System.Serializable]
public class AIPieceDatasetEntry
{
    public int pieceIndex;
    public string generationMode;
    public string prompt;
    public int cubeCount;
    public SerializableCell dimensions;
    public float compactness;
    public bool isSymmetrical;
    public string shapeClassification;
    public List<SerializableCell> cells;
}

[System.Serializable]
public class AIPieceDatasetWrapper
{
    public List<AIPieceDatasetEntry> dataset = new List<AIPieceDatasetEntry>();
}

[System.Serializable]
public class AIPieceListWrapper
{
    public List<string> names = new List<string>();
    public List<AIPieceDatasetEntry> pieces = new List<AIPieceDatasetEntry>();
}

