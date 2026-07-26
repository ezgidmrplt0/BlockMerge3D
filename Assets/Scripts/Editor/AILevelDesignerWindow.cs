using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LevelForge;

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
    public enum GenerationBaseType { Template, CustomPrefab, CustomSize }
    [SerializeField] internal GenerationBaseType generationBaseType = GenerationBaseType.Template;
    [SerializeField] internal GameObject customBasePrefab;
    [SerializeField] internal LevelTemplate selectedTemplate; // Şablon bazlı üretim
    [SerializeField] internal string levelName        = "AI_Level_1";
    [SerializeField] internal float levelTime         = 75f;
    [SerializeField] internal int levelTarget         = 0; // Hedef skor kaldırıldı
    [SerializeField] internal Vector3Int gridSize     = new Vector3Int(5, 5, 5);
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
    private List<int> frozenHitCounts           = new List<int>(); // frozenCells ile aynı indeks — bkz. IceHitCountUtility
    internal List<List<Vector3Int>> pieceSplitList = new List<List<Vector3Int>>();

    // ── Solver Sonucu ─────────────────────────────────────────────
    internal SolverResult lastSolverResult;
    internal bool solverRan = false;
    // RunDifficultySearch tarafından doldurulur — kapalı döngü aramanın tam teşhis izini taşır
    // (kaç deneme yapıldı, her denemenin skoru/nedeni). Wizard'ın (LevelCreationWizardWindow)
    // kendi layer-by-layer akışı bu motoru kullanmadığı için orada null kalır — DrawSolverResultSection
    // bunu null-safe ele alır.
    internal LevelForge.SearchResult<BlockMerge3DCandidate> lastSearchResult;
    private int highlightedPieceIndex = -1;

    // ── Katman Katman Üretim (LevelCreationWizardWindow Adım 3) ─────
    // Wizard, tüm şekli tek seferde üretmek yerine Y katmanlarını tek tek üretip her birini
    // kullanıcıya onaylatıyor (bkz. StartLayerByLayerGeneration/GenerateCurrentLayerPieces).
    internal bool layerGenActive = false;
    internal int genCurrentLayerY = 0;
    internal int pendingLayerPieceCount = 0; // pieceSplitList kuyruğundaki henüz onaylanmamış parça sayısı
    internal string layerGenError = null;
    internal int pieceLibraryVersion = 0; // RefreshPieceLibrary tarafından artırılır — bayat oturumları yakalamak için
    private string layerGenSignature = null;

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
    private GUIStyle stylePrimaryButton, styleSuccessButton, styleDangerButton, styleWarningButton, styleDarkButton;
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


    private void BeginSectionCard(string title, Color headerColor, string icon = "")
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        // Draw header bar
        Rect headerRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
        // Draw a nice colored background
        EditorGUI.DrawRect(headerRect, headerColor);
        
        // Draw title text inside header
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(6, 0, 0, 0)
        };
        headerStyle.normal.textColor = Color.white;
        
        string fullTitle = string.IsNullOrEmpty(icon) ? title : $"{icon}  {title}";
        GUI.Label(headerRect, fullTitle, headerStyle);
        
        // Indent content slightly
        EditorGUILayout.BeginVertical(new GUIStyle() { padding = new RectOffset(8, 8, 8, 8) });
    }
    
    private void EndSectionCard()
    {
        EditorGUILayout.EndVertical(); // inner content padding
        EditorGUILayout.EndVertical(); // outer helpBox
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
        // ŞABLON, PREFAB VEYA ÖZEL BOYUT SEÇİCİ
        BeginSectionCard("ÜRETİM KAYNAĞI", new Color(0.12f, 0.58f, 0.40f), "📐");
        EditorGUILayout.HelpBox("AI seviyeyi bir şablon, özel prefab veya belirleyeceğiniz XYZ boyutlarında özel bir grid baz alarak oluşturur.", MessageType.Info);

        // Custom choice cards for Production Source
        EditorGUILayout.LabelField("Üretim Kaynağı Seçimi:", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();

        bool isTemplate = (generationBaseType == GenerationBaseType.Template);
        GUIStyle srcBtnStyle = isTemplate ? stylePrimaryButton : styleDarkButton;
        if (GUILayout.Button("📐  ŞABLON BAZLI", srcBtnStyle, GUILayout.ExpandWidth(true), GUILayout.Height(30)))
        {
            generationBaseType = GenerationBaseType.Template;
        }

        GUILayout.Space(6);

        bool isPrefab = (generationBaseType == GenerationBaseType.CustomPrefab);
        GUIStyle prefabBtnStyle = isPrefab ? stylePrimaryButton : styleDarkButton;
        if (GUILayout.Button("🧊  ÖZEL PREFAB", prefabBtnStyle, GUILayout.ExpandWidth(true), GUILayout.Height(30)))
        {
            generationBaseType = GenerationBaseType.CustomPrefab;
        }

        GUILayout.Space(6);

        bool isCustomSize = (generationBaseType == GenerationBaseType.CustomSize);
        GUIStyle customSizeBtnStyle = isCustomSize ? stylePrimaryButton : styleDarkButton;
        if (GUILayout.Button("📏  ÖZEL BOYUT (XYZ)", customSizeBtnStyle, GUILayout.ExpandWidth(true), GUILayout.Height(30)))
        {
            generationBaseType = GenerationBaseType.CustomSize;
        }

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
                EditorGUILayout.HelpBox("⚠️ Şablon seçilmedi. Assets/Templates/ klasöründen bir şablon seçin veya yeni tasarlayın.", MessageType.Warning);
            }

            EditorGUILayout.Space(4);
            GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
            if (GUILayout.Button("🏰  Yeni Level Şablonu Tasarla / Düzenle", GUILayout.Height(28)))
            {
                LevelTemplateManagerWindow.ShowWindow();
            }
            GUI.backgroundColor = Color.white;
        }
        else if (generationBaseType == GenerationBaseType.CustomPrefab)
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
        else // CustomSize
        {
            EditorGUILayout.HelpBox("Belirttiğiniz X, Y, Z boyutlarında tam dolu dikdörtgensel ızgara (grid) oluşturulur.", MessageType.Info);
            gridSize = EditorGUILayout.Vector3IntField("Grid Boyutları (X, Y, Z)", gridSize);
            gridSize.x = Mathf.Clamp(gridSize.x, 1, 20);
            gridSize.y = Mathf.Clamp(gridSize.y, 1, 20);
            gridSize.z = Mathf.Clamp(gridSize.z, 1, 20);
            EditorGUILayout.LabelField($"📐 Toplam Blok Sayısı: {gridSize.x * gridSize.y * gridSize.z} hücre", EditorStyles.boldLabel);
        }
        EndSectionCard();

        GUILayout.Space(10);

        // 🏆 ZORLUK VE SEVİYE AYARLARI
        BeginSectionCard("ZORLUK VE SEVİYE AYARLARI", new Color(0.85f, 0.55f, 0.10f), "🏆");

        // 1. Zorluk Seviyesi Seçimi
        EditorGUILayout.LabelField("Zorluk Seviyesi Seçimi:", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();

        // Kolay
        bool isKolay = selectedDifficulty == AILevelDifficulty.Kolay;
        GUIStyle diffBtnStyle = isKolay ? styleSuccessButton : styleDarkButton;
        if (GUILayout.Button("🟢  KOLAY", diffBtnStyle, GUILayout.ExpandWidth(true), GUILayout.Height(28)))
        {
            ApplyDifficultyScaleForMode(AILevelDifficulty.Kolay);
        }

        // Orta
        bool isOrta = selectedDifficulty == AILevelDifficulty.Orta;
        GUIStyle diffOrtaStyle = isOrta ? styleWarningButton : styleDarkButton;
        if (GUILayout.Button("🟡  ORTA", diffOrtaStyle, GUILayout.ExpandWidth(true), GUILayout.Height(28)))
        {
            ApplyDifficultyScaleForMode(AILevelDifficulty.Orta);
        }

        // Zor
        bool isZor = selectedDifficulty == AILevelDifficulty.Zor;
        GUIStyle diffZorStyle = isZor ? styleDangerButton : styleDarkButton;
        if (GUILayout.Button("🔴  ZOR", diffZorStyle, GUILayout.ExpandWidth(true), GUILayout.Height(28)))
        {
            ApplyDifficultyScaleForMode(AILevelDifficulty.Zor);
        }

        // Uzman
        bool isUzman = selectedDifficulty == AILevelDifficulty.Uzman;
        GUIStyle diffUzmanStyle = isUzman ? stylePrimaryButton : styleDarkButton;
        if (GUILayout.Button("🟣  UZMAN", diffUzmanStyle, GUILayout.ExpandWidth(true), GUILayout.Height(28)))
        {
            ApplyDifficultyScaleForMode(AILevelDifficulty.Uzman);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // 2. Seviye Seçimi
        EditorGUILayout.LabelField("🎯  Kaydedilecek Seviye Numarası (Level Index):", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("◀◀", styleDarkButton, GUILayout.Width(35), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Max(1, targetLevelIndex - 10);
            levelName = $"AI_Level_{targetLevelIndex}";
        }

        if (GUILayout.Button("◀", styleDarkButton, GUILayout.Width(25), GUILayout.Height(22)))
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

        if (GUILayout.Button("▶", styleDarkButton, GUILayout.Width(25), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Min(100, targetLevelIndex + 1);
            levelName = $"AI_Level_{targetLevelIndex}";
        }

        if (GUILayout.Button("▶▶", styleDarkButton, GUILayout.Width(35), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Min(100, targetLevelIndex + 10);
            levelName = $"AI_Level_{targetLevelIndex}";
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        // 3. SEVİYE PARAMETRELERİ (Elle Düzenlenebilir)
        EditorGUILayout.LabelField("⚙️  SEVİYE PARAMETRELERİ (Elle Düzenlenebilir)", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();

        // ⏱️ Süre Sınırı
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("⏱️ Süre Sınırı", EditorStyles.miniLabel);
        levelTime = EditorGUILayout.FloatField(levelTime, GUILayout.Height(20));
        levelTime = Mathf.Max(0f, levelTime);
        EditorGUILayout.EndVertical();

        // 🧱 Hazır Küp Oranı (%)
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField($"🧱 Hazır Küp (%{prefillPercentage * 100f:F0})", EditorStyles.miniLabel);
        prefillPercentage = EditorGUILayout.Slider(prefillPercentage, 0f, 0.5f);
        EditorGUILayout.EndVertical();

        // ❄️ Buz Küpü Oranı (%)
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField($"❄️ Buz Küpü (%{icePercentage * 100f:F0})", EditorStyles.miniLabel);
        icePercentage = EditorGUILayout.Slider(icePercentage, 0f, 0.5f);
        EditorGUILayout.EndVertical();

        // 🧩 Parça Boyutu (Min - Max)
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("🧩 Parça Boyutu", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        minPieceSize = EditorGUILayout.IntField(minPieceSize, GUILayout.Width(35), GUILayout.Height(20));
        EditorGUILayout.LabelField("-", GUILayout.Width(8));
        maxPieceSize = EditorGUILayout.IntField(maxPieceSize, GUILayout.Width(35), GUILayout.Height(20));
        minPieceSize = Mathf.Clamp(minPieceSize, 1, maxPieceSize);
        maxPieceSize = Mathf.Clamp(maxPieceSize, minPieceSize, 10);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

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
        EndSectionCard();

        GUILayout.Space(10);

        // KÜP PREFABI SEÇİMİ (Global) — parça görselleri artık HER ZAMAN düz küp; tür/hayvan
        // görseli (varsa) sadece runtime'da LevelManager.SpawnSpeciesBadge ile tek bir "rozet"
        // olarak eklenir (bkz. LevelManager.cs). Eskiden burada hücre başına ayrı bir hayvan
        // modeli seçilebiliyordu ("Çoklu Asset Kullan") — bu, çok hücreli bir parçanın birbirine
        // yapıştırılmış birden fazla küçük hayvan modeline dönüşmesine yol açıyordu (görsel
        // olarak saçma), bu yüzden kaldırıldı.
        BeginSectionCard("GÖRSEL PARÇA YAPISI", new Color(0.50f, 0.35f, 0.75f), "🧊");

        // Always draw the Global Küp Prefabı field so the user can define the constant grid target box style
        GameObject prevCubePrefab = cubePrefab;
        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Grid Küp Prefabı (Kalıcı Hedef/Fallback)", cubePrefab, typeof(GameObject), false);
        
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
            EditorGUILayout.HelpBox("⚠ Lütfen bir küp prefabı seçin. (Örn: SingleCube / Untitled1)", MessageType.Warning);
        }

        EditorGUILayout.Space(4);
        EndSectionCard();
    }

    // ── Sol Panel (Parametreler) ──────────────────────────────────
    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(420), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        DrawTemplateAndDifficultySection();

        GUILayout.Space(10);

        float originalLabelWidth = EditorGUIUtility.labelWidth;

        BeginSectionCard("GENEL AYARLAR", new Color(0.15f, 0.50f, 0.75f), "📝");
        
        EditorGUIUtility.labelWidth = 110;
        levelName   = EditorGUILayout.TextField("Seviye Adı Öneki", levelName);
        levelTime   = EditorGUILayout.FloatField("Süre Sınırı (sn)", levelTime);

        EditorGUILayout.BeginHorizontal();
        if (levelTime <= 0)
        {
            EditorGUILayout.LabelField("ℹ Süresiz oyun modu aktif.", EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.LabelField($"ℹ Süre Önerisi: {levelTime} sn.", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();
        
        GUI.enabled = false; // Şablondan geldiği için değiştirilemez
        gridSize    = EditorGUILayout.Vector3IntField("Grid Boyutu (Şablon)", gridSize);
        GUI.enabled = true;
        EndSectionCard();

        GUILayout.Space(10);

        BeginSectionCard("YAPAY ZEKA PARAMETRE AYARLARI", new Color(0.95f, 0.40f, 0.70f), "🤖");
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
        EndSectionCard();

        GUILayout.Space(10);

        BeginSectionCard("PARÇA KÜTÜPHANESİ", new Color(0.35f, 0.35f, 0.40f), "🧬");
        EditorGUILayout.HelpBox("🧬 Parçalar Assets/PieceDefinitions/ kütüphanesinden gelir. Tasarladığınız tüm özel parçalar seviye/katman doldurma sırasında SolutionFirstBuilder tarafından doğrudan referans alınır.", MessageType.Info);
        int cachedCount = pieceLibraryCache?.Count ?? 0;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Yüklü parça sayısı: {cachedCount}", EditorStyles.miniLabel);
        if (GUILayout.Button("Kütüphaneyi Yenile", styleDarkButton, GUILayout.Width(140)))
        {
            RefreshPieceLibrary();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
        if (GUILayout.Button("🎨  Yeni Özel Parça Tasarla (Parça Kütüphanesi)", GUILayout.Height(30)))
        {
            PieceDefinitionMigrationWindow.ShowWindow();
        }
        GUI.backgroundColor = Color.white;
        EndSectionCard();

        GUILayout.Space(12);

        EditorGUIUtility.labelWidth = originalLabelWidth;

        if (prefilledMaterials == null || prefilledMaterials.Length == 0)
        {
            EditorGUILayout.HelpBox($"⚠ Prefilled (engel) küp renk paleti bulunamadı ({PREFILLED_MATERIALS_PATH}). Prefilled küpler pembe (varsayılan) görünecek.", MessageType.Warning);
            if (GUILayout.Button("Paleti Yeniden Yükle", styleDarkButton))
            {
                LoadPrefilledMaterialsPalette();
            }
        }
        else
        {
            EditorGUILayout.LabelField($"ℹ Prefilled Renk Paleti: {prefilledMaterials.Length} materyal ({PREFILLED_MATERIALS_PATH})", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(5);
        if (GUILayout.Button("⚡ BÖLÜM & PARÇALARI ÖNİZLE (YAPAY ZEKA)", stylePrimaryButton, GUILayout.Height(40)))
        {
            GenerateLevelProcedurally();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ── Merkez Grid Önizleme ───────────────────────────────────────
    // internal: LevelCreationWizardWindow Adım 3, katman katman üretim sırasında canlı
    // parça önizlemesini göstermek için bunu doğrudan çağırıyor (kopya değil).
    internal void DrawCenterGrid()
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

        GUILayout.Space(10);
        if (GUILayout.Button("❄️ Buzlu Yerleri Tespit Et", stylePrimaryButton, GUILayout.Height(24), GUILayout.Width(160)))
        {
            DetectAndDistributeIce();
        }
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
    // onRetry/retryLabel: katman katman üretim akışı (Wizard Adım 3), bütünsel doğrulama
    // başarısız olduğunda varsayılan AutoAdjustAndRegenerate (atomik yol) yerine katman
    // oturumunu baştan başlatan kendi retry'ını geçirebilsin diye — verilmezse davranış
    // (ve buton metni) eskisiyle BİREBİR aynı kalır.
    internal void DrawSolverResultSection(System.Action onRetry = null, string retryLabel = null)
    {
        if (!(solverRan && lastSolverResult != null)) return;

        BeginSectionCard("ÇÖZÜLEBİLİRLİK DOĞRULAMA (SOLVER)", new Color(0.15f, 0.50f, 0.75f), "🔍");

        if (lastSolverResult.isSolvable)
        {
            var solverSuccessStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.18f, 0.70f, 0.40f) }
            };
            GUILayout.Label("✅  BAŞARILI: SEVİYE ÇÖZÜLEBİLİR!", solverSuccessStyle);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            // Move count card
            DrawStatBlock("En Kısa Yol", $"{lastSolverResult.minMoveCount} Hamle");
            GUILayout.Space(8);

            // Difficulty card
            string diffText = string.IsNullOrEmpty(lastSolverResult.difficultyLabel) ? "Bilinmiyor" : lastSolverResult.difficultyLabel.ToUpper();
            DrawStatBlock("Zorluk", $"{diffText} ({lastSolverResult.difficultyScore:F2})");

            // Kapalı döngü arama kaç deneme sürdü (lastSearchResult sadece RunDifficultySearch
            // yolundan geçince set edilir — wizard'ın kendi akışında null kalır, bu blok o durumda
            // sadece bu kartı atlar).
            if (lastSearchResult != null)
            {
                GUILayout.Space(8);
                DrawStatBlock("Kapalı Döngü", $"{lastSearchResult.attemptsUsed} deneme");
            }

            EditorGUILayout.EndHorizontal();

            var lastAttemptDiag = lastSearchResult != null && lastSearchResult.allAttempts.Count > 0
                ? lastSearchResult.allAttempts[lastSearchResult.allAttempts.Count - 1]
                : null;
            if (lastAttemptDiag != null && lastAttemptDiag.stochasticPassRate < 1f)
            {
                EditorGUILayout.HelpBox($"❄️ Buz Monte Carlo doğrulaması: %{lastAttemptDiag.stochasticPassRate * 100f:F0} geçiş oranı.", MessageType.None);
            }

            // Havuz sessizce "tag+boyut" kademesinin altına düştüyse (bkz. SampleEligiblePool) —
            // ör. seçilen zorluğa uygun etiketli/boyutlu parça kalmadığı için tüm kütüphaneden
            // örneklendiyse — bunu burada görünür kıl.
            if (!string.IsNullOrEmpty(lastPoolFallbackInfo) && !lastPoolFallbackInfo.StartsWith("tag+boyut"))
            {
                EditorGUILayout.HelpBox($"🧬 Parça havuzu kademesi: {lastPoolFallbackInfo}", MessageType.Warning);
            }
        }
        else if (lastSolverResult.timedOut)
        {
            var solverTimeoutStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.95f, 0.65f, 0.15f) }
            };
            GUILayout.Label("⏱️  BELİRSİZ: ZAMAN / DURUM LİMİTİ AŞILDI", solverTimeoutStyle);
            EditorGUILayout.HelpBox(BuildSolverFailureMessage(), MessageType.Warning);

            if (GUILayout.Button(retryLabel ?? "🔁  Parametreleri İyileştir", styleSuccessButton, GUILayout.Height(28)))
            {
                (onRetry ?? AutoAdjustAndRegenerate)();
            }
        }
        else
        {
            var solverFailStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.88f, 0.25f, 0.25f) }
            };
            GUILayout.Label("❌  HATA: SEVİYE ÇÖZÜLEMEZ DURUMDA!", solverFailStyle);
            EditorGUILayout.HelpBox(BuildSolverFailureMessage(), MessageType.Error);

            if (GUILayout.Button(retryLabel ?? "🔁  Parametreleri İyileştir", styleSuccessButton, GUILayout.Height(28)))
            {
                (onRetry ?? AutoAdjustAndRegenerate)();
            }
        }
        EndSectionCard();
    }

    // lastSearchResult (kapalı döngü arama) varsa onun tam denemeler-arası özetini gösterir — kaç
    // deneme yapıldı ve en yakın denemenin nereden saptığı. Yoksa (wizard'ın kendi akışı gibi
    // lastSearchResult'ı hiç set etmeyen çağrı yolları için) eski davranışa (tek solver mesajı) düşer.
    private string BuildSolverFailureMessage()
    {
        if (lastSearchResult != null && !lastSearchResult.success)
        {
            return $"{lastSearchResult.failureSummary}\n\n" +
                   $"({lastSearchResult.attemptsUsed} deneme yapıldı, hiçbiri '{selectedDifficulty}' zorluğunu toleransla tutturamadı.)";
        }
        return lastSolverResult.failureReason;
    }

    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(190), GUILayout.ExpandHeight(true));
        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        BeginSectionCard("DIŞA AKTAR VE DENE", new Color(0.15f, 0.50f, 0.75f), "📊");
        EditorGUILayout.LabelField("İstatistikler:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Toplam Küp Sayısı: {occupiedCells.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Hazır (Renkli) Küpler: {prefilledCells.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Dondurulmuş Küpler: {frozenCells.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Otomatik Parça Sayısı: {pieceSplitList.Count}", EditorStyles.miniLabel);
        EndSectionCard();

        GUILayout.Space(10);

        BeginSectionCard("OLUŞTURULAN PARÇALAR", new Color(0.50f, 0.35f, 0.75f), "🧩");
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
                Color prevBG = GUI.backgroundColor;
                if (highlightedPieceIndex == i) GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 0.6f);
                
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

                int minX = piece.Min(c => c.x);
                int maxX = piece.Max(c => c.x);
                int minZ = piece.Min(c => c.z);
                int maxZ = piece.Max(c => c.z);
                float previewBlockSize = 14f;
                float previewWidth = (maxX - minX + 1) * previewBlockSize;
                float previewHeight = (maxZ - minZ + 1) * previewBlockSize;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(18);
                Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.Width(previewWidth), GUILayout.Height(previewHeight));
                EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.15f));
                foreach (var cell in piece)
                {
                    EditorGUI.DrawRect(new Rect(previewRect.x + (cell.x - minX) * previewBlockSize + 0.5f, previewRect.y + (cell.z - minZ) * previewBlockSize + 0.5f, previewBlockSize - 1f, previewBlockSize - 1f), col);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();

                if (Event.current.type == EventType.MouseDown && pieceClickRect.Contains(Event.current.mousePosition))
                {
                    highlightedPieceIndex = (highlightedPieceIndex == i) ? -1 : i;
                    if (highlightedPieceIndex != -1) drawView = AIDrawView.PiecesOnly;
                    Repaint();
                    Event.current.Use();
                }
                GUILayout.Space(4);
            }
        }
        EndSectionCard();

        GUILayout.Space(10);
        DrawSolverResultSection();

        GUILayout.Space(12);

        // Kaydet artık HER ZAMAN açık — solver doğrulaması TAVSİYE, engel değil (istek üzerine
        // eski "Zorunlu Koruma Kuralı #1" hard-block'u kaldırıldı). Kullanıcı çözülebilir görünen
        // ama solver'ın (çözemedi/bütçe aştı) doğrulayamadığı seviyeleri de kaydedip oyunda kendi
        // test edebilsin. Doğrulanmamışsa uyarı gösterilir + kayıtta bilinçli onay sorulur.
        bool isValidatedSolvable = solverRan && lastSolverResult != null && (lastSolverResult.isSolvable || lastSolverResult.timedOut);
        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f, 1f); // Doygun Yeşil
        if (GUILayout.Button("💾 SEVİYEYİ TAMAMEN OLUŞTUR\n(BÖLÜMÜ KAYDET)", GUILayout.Height(50)))
        {
            ExportProceduralLevel();
        }
        GUI.backgroundColor = Color.white;
        if (!isValidatedSolvable)
        {
            EditorGUILayout.HelpBox("Bu seviye solver tarafından çözülebilir DOĞRULANMADI (çözülemez bulundu ya da arama bütçesi aşıldı). Yine de kaydedebilirsin — kayıtta onay sorulur, oyunda kendin test et.", MessageType.Warning);
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
        GUILayout.Label(" • Dondurulmuş Bloklar (Buz): Oyuncu bu hücrelerin üzerine blok koyduğunda, katman temizlenmesi için önce buzun kırılması gerekir (bkz. GridManager.CheckAndResolveFrozenCells). Üretici, buzu taban katmanlara veya şekil dış çeperine ağırlıklı olarak yerleştirir (bkz. DistributeObstacles).\n" +
                      " • Renkli Bloklar (Prefilled): Seviye başında ızgaraya yerleştirilen sabit bloklardır, kozmetiktir. Renk bir katman başına TEK seferde seçilir (hücre bazında değil) — bu, aynı katmanda birbirinden farklı renkte prefilled hücrelerin o katmanı kalıcı olarak temizlenemez bırakmasını önlemek içindir; 'renk bütünlüğü' gibi bir estetik hedefi YOKTUR.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        EditorGUILayout.BeginVertical(styleInstructionBox);
        GUILayout.Label("4. PARÇALARA BÖLME: SOLUTION-FIRST KÜTÜPHANE + KAPALI DÖNGÜ ZORLUK ARAMASI", EditorStyles.boldLabel);
        GUILayout.Label("Şekli önce çizip sonra parçalara bölen bir BFS kümelemesi YOKTUR (bu, artık kaldırılmış eski bir moddu). Gerçek akış tam tersi yönde çalışır:\n" +
                      " 1. Assets/PieceDefinitions/ kütüphanesinden zorluk etiketine ve boyut aralığına uyan rastgele bir parça havuzu örneklenir (bkz. SampleEligiblePool).\n" +
                      " 2. SolutionFirstBuilder, bu havuzun şekli TAM olarak döşeyip döşeyemediğini katman katman, geri izlemeli (backtracking) arayarak dener — başarılıysa sonuç zaten inşa sırasında çözülmüş olur.\n" +
                      " 3. LevelSolver bu döşemeyi bağımsız olarak yeniden doğrular (geometri + buz erimesi dahil) ve 0-1 arası bir zorluk skoru üretir.\n" +
                      " 4. LevelForge.DifficultySearchEngine bu skoru seçilen zorluk hedefiyle karşılaştırır; tutmuyorsa parametreleri (buz/hazır küp oranı, parça boyutu) YAPILANDIRILMIŞ biçimde değiştirip yeniden dener — hedefe toleransla ulaşana kadar (sınırlı sayıda deneme) sürer, tutturamazsa AÇIKÇA başarısız bildirir (en yakın ama hedef dışı sonucu sessizce kabul etmez).", EditorStyles.wordWrappedLabel);
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
        GUILayout.Label("Bu, bir dil modeli DEĞİL — düz substring/keyword eşleştirmesidir (bkz. GeneratePromptBasedShape). " +
                      "Ana seviye şekli için tanınan kelimeler: 'hollow'/'oyuk'/'bos' (oyuk gövde), 'pyramid'/'piramit', " +
                      "'star'/'yildiz'/'cross'/'arti' (eksenler boyunca kollar), 'tower'/'kule'. " +
                      "(Not: 'AI Parça Yapıcı' sekmesindeki AYRI PromptBased modu — tek bir parça üretir, tüm seviyeyi değil — " +
                      "farklı ve bağımsız bir kelime kümesi kullanır: 'l-shaped', 'flat line', 't-shaped plus', 'stair step zigzag', 'compact box'.)\n" +
                      " • Algılanan kelimeye göre ilgili geometrik kural tetiklenir; hiçbir kelime eşleşmezse tam dolu bir kutu üretilir.\n" +
                      " • Boyutlar ve hücre yerleşimleri bu kısıtlamalar dahilinde rastgeleleştirilerek varyasyonlar üretilir.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ══ ALGORİTMİK YAPAY ZEKA METODLARI ═════════════════════════

    internal void GenerateLevelProcedurally()
    {
        if (!ValidateAndLoadSourceShape(out int W, out int H, out int D)) return;

        ApplyObstaclesAndSplitPieces(W, H, D);

        activeLayer = 0;
        Repaint();
        Debug.Log($"🤖 Yapay Zeka: '{levelName}' seviyesi procedurally oluşturuldu. Küp: {occupiedCells.Count}, Parça: {pieceSplitList.Count}");
    }

    // Kaynak (şablon/özel prefab) doğrulaması + occupiedCells'in o kaynaktan yüklenmesi —
    // GenerateLevelProcedurally VE StartLayerByLayerGeneration tarafından ortak kullanılır.
    // Kaynak seçilmemiş/geçersizse kullanıcıya dialog gösterip false döner (state hiç temizlenmez).
    private bool ValidateAndLoadSourceShape(out int W, out int H, out int D)
    {
        W = gridSize.x;
        H = gridSize.y;
        D = gridSize.z;

        // Kaynak kontrolü
        if (generationBaseType == GenerationBaseType.Template && selectedTemplate == null)
        {
            EditorUtility.DisplayDialog("Hata", "Lütfen önce bir Level Şablonu seçin (Assets/Templates/)", "Tamam");
            return false;
        }
        else if (generationBaseType == GenerationBaseType.CustomPrefab)
        {
            if (customBasePrefab == null || customBasePrefab.GetComponent<CubeShapeDataHolder>() == null)
            {
                EditorUtility.DisplayDialog("Hata", "Lütfen CubeShapeDataHolder içeren geçerli bir Prefab seçin", "Tamam");
                return false;
            }
        }
        else if (generationBaseType == GenerationBaseType.CustomSize)
        {
            if (W < 1 || H < 1 || D < 1)
            {
                EditorUtility.DisplayDialog("Hata", "Lütfen geçerli X, Y, Z grid boyutları girin (en az 1x1x1)", "Tamam");
                return false;
            }
        }

        occupiedCells.Clear();
        prefilledCells.Clear();
        prefilledMatIdx.Clear();
        frozenCells.Clear();
        frozenHitCounts.Clear();
        pieceSplitList.Clear();
        highlightedPieceIndex = -1;

        // 1. Kaynaktan Grid'i Yükle
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
        else if (generationBaseType == GenerationBaseType.CustomPrefab)
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
        else // CustomSize
        {
            BuildSolidBoxShape(W, H, D);
            Debug.Log($"✅ Özel XYZ Grid ({W}x{H}x{D}) - Tam dolu küp: {occupiedCells.Count} blok");
        }

        // Boş şekil koruması
        if (occupiedCells.Count == 0)
        {
            occupiedCells.Add(new Vector3Int(W / 2, 0, D / 2));
        }

        return true;
    }

    // ── Katman Katman Üretim (LevelCreationWizardWindow Adım 3) ────────────────────
    // Şekli tek seferde üretip tüm parçaları bir arada bölmek yerine, en alt Y katmanından
    // başlayarak SADECE o katmanın hücrelerini SolutionFirstBuilder ile döşer, sonucu
    // pieceSplitList kuyruğuna ekler ve kullanıcının onayını bekler. SolutionFirstBuilder'ın
    // arama mantığı zaten katman-katman (GetLowestIncompleteLayer/GetPossibleOffsets, bkz. o
    // dosya) — buraya sadece TEK bir Y'nin hücrelerini vermek, aramayı doğal olarak o katmanla
    // sınırlıyor, SolutionFirstBuilder.cs'de hiçbir değişiklik gerekmiyor.
    internal void StartLayerByLayerGeneration()
    {
        if (layerGenActive)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Katman Üretimini Yeniden Başlat",
                "Devam eden bir katman katman üretim oturumu var. Şimdiye kadar onayladığınız katmanlar silinecek. Baştan başlansın mı?",
                "Evet, Baştan Başla", "İptal");
            if (!confirmed) return;
        }

        if (!ValidateAndLoadSourceShape(out int W, out int H, out int D)) return;

        DistributeObstacles(W, H, D);

        genCurrentLayerY = occupiedCells.Count > 0 ? occupiedCells.Min(c => c.y) : 0;
        pendingLayerPieceCount = 0;
        layerGenError = null;
        solverRan = false;
        lastSolverResult = null;
        layerGenActive = true;
        drawView = AIDrawView.PiecesOnly;
        show3D = false;
        activeLayer = genCurrentLayerY;
        layerGenSignature = BuildLayerGenSignature();

        GenerateCurrentLayerPieces();
    }

    // Adım 1/2 girdileri (şablon/prefab, zorluk, kütüphane sürümü) bir katman oturumu açıkken
    // değişirse (kullanıcı Wizard'da geri gidip başka şablon/zorluk seçerse) oturum artık
    // tutarsız hale gelir — bkz. DrawStep3'teki bayat-oturum kontrolü.
    private string BuildLayerGenSignature()
    {
        string sourceId;
        if (generationBaseType == GenerationBaseType.Template)
            sourceId = selectedTemplate != null ? selectedTemplate.GetInstanceID().ToString() : "none";
        else if (generationBaseType == GenerationBaseType.CustomPrefab)
            sourceId = customBasePrefab != null ? customBasePrefab.GetInstanceID().ToString() : "none";
        else
            sourceId = $"{gridSize.x}x{gridSize.y}x{gridSize.z}";

        return $"{generationBaseType}|{sourceId}|{selectedDifficulty}|{pieceLibraryVersion}";
    }

    // Wizard Adım 3, layerGenActive iken her OnGUI'de imzayı bununla karşılaştırır.
    internal bool IsLayerGenSignatureStale()
    {
        return layerGenActive && layerGenSignature != BuildLayerGenSignature();
    }

    // Bayat bir oturumu (imza uyuşmazlığı) sessizce iptal eder — pieceSplitList/pending
    // durumunu temizler, kullanıcıya neden sıfırlandığını açıklayan bir mesaj bırakır.
    internal void CancelStaleLayerGenSession()
    {
        pieceSplitList.Clear();
        pendingLayerPieceCount = 0;
        layerGenActive = false;
        layerGenError = "⚠️ Adım 1/2'deki ayarlar değişti, katman katman üretim oturumu sıfırlandı. Lütfen yeniden başlatın.";
    }

    // Geçerli katmanı (genCurrentLayerY) döşemeye çalışır. Hedefsiz (tamamen prefilled) üst
    // katmanları otomatik atlar; en üst katman da bittiyse FinalizeLayerByLayerGeneration'ı
    // tetikler. pieceSplitList'in kuyruğundaki önceki (henüz onaylanmamış) deneme varsa önce
    // temizler — hem "Yeniden Üret" hem de bu metodun kendisi tarafından güvenle çağrılabilir.
    internal void GenerateCurrentLayerPieces()
    {
        layerGenError = null;

        if (pendingLayerPieceCount > 0)
        {
            pieceSplitList.RemoveRange(pieceSplitList.Count - pendingLayerPieceCount, pendingLayerPieceCount);
            pendingLayerPieceCount = 0;
        }

        int maxY = occupiedCells.Count > 0 ? occupiedCells.Max(c => c.y) : -1;

        // Hedef hücresi olmayan (tamamen prefilled) katmanları otomatik atla. DİKKAT: buz
        // (frozen) hücreler burada "hedefsiz" sayılmaz — onlar aşağıda ayrıca ele alınıyor,
        // çünkü "tamamen buzlu bir katman" gerçek bir üretim hatasıdır (o katmana hiçbir parça
        // asla yerleştirilemez), sessizce atlanacak zararsız bir durum değil.
        while (genCurrentLayerY <= maxY && !occupiedCells.Any(c => c.y == genCurrentLayerY && !prefilledCells.Contains(c)))
        {
            genCurrentLayerY++;
        }

        if (genCurrentLayerY > maxY)
        {
            FinalizeLayerByLayerGeneration();
            return;
        }

        activeLayer = genCurrentLayerY;

        // Buz hücreleri prefilled gibi döşeme hedefinden ÇIKARILIR — aksi halde SolutionFirstBuilder
        // buza dokunan bir parça üretebilir ve o parça buz erimeden ASLA yerleştirilemez (gerçek
        // CanPlace buzlu hücreye izin vermiyor). Bu, açık hücreleri buzla birlikte
        // "tek parça" olarak yutup açık bölgeyi parçasız bırakabiliyordu (bkz. oyun-içi teşhis
        // logu: iki bağlantısız 2'lik açık ada, elde sadece 3-4 hücrelik parçalar). Buz hücreleri
        // hâlâ toplam hedefin bir parçası — onlar için FinalizeLayerByLayerGeneration'daki ayrı
        // güvenlik payı (buz başına yedek parça) mekanizması kullanılıyor.
        var layerCells = new HashSet<Vector3Int>(occupiedCells.Where(c => c.y == genCurrentLayerY));
        layerCells.ExceptWith(prefilledCells);

        if (layerCells.Count == 0)
        {
            // Bu katmanda prefilled olmayan TÜM hücreler buzlu — hiçbir parça bu katmana asla
            // yerleştirilemez (buz komşu bir yerleşimle erimeden), üretim tekrar denemekle
            // düzelmez. Kullanıcının katmanı/oturumu yeniden başlatması gerekir.
            layerGenError = $"⚠️ Katman Y={genCurrentLayerY}'deki tüm boş hücreler buzlu — bu katmana hiçbir parça " +
                             "yerleştirilemez. Buz oranını düşürüp 'Katmanları Baştan Üret' ile tekrar deneyin.";
            Repaint();
            return;
        }

        var library = LoadPieceLibrary();
        if (library.Count == 0)
        {
            layerGenError = "⚠️ Assets/PieceDefinitions/ altında hiç PieceDefinition bulunamadı — önce parça kütüphanesini kurun.";
            Repaint();
            return;
        }

        int layerVolume = layerCells.Count;
        int stateLimit = layerVolume < 20 ? 20000 : layerVolume < 40 ? 40000 : 75000;
        int timeLimitMs = layerVolume < 20 ? 1200 : layerVolume < 40 ? 2000 : 3500;

        // Katman başına TOPLAM süre tavanı. Eskiden 15+10+1 deneme her biri kendi time-limit'ine
        // kadar bloklu koştuğu için zor katmanda editör ~2 dakika donabiliyordu (senkron, UI
        // thread'inde). Artık toplam bütçe aşılınca denemeler kesiliyor ve her denemenin süresi
        // KALAN bütçeye kırpılıyor — tek bir deneme bile tavanı aşamaz.
        var layerSw = System.Diagnostics.Stopwatch.StartNew();
        int totalBudgetMs = layerVolume < 20 ? 3500 : layerVolume < 40 ? 6000 : 9000;

        // Aşama 1: Zorluk profiline uygun kısıtlı havuzlarda denemeler
        for (int attempt = 0; attempt < 15; attempt++)
        {
            int remaining = totalBudgetMs - (int)layerSw.ElapsedMilliseconds;
            if (remaining <= 0) break;
            var pool = SampleEligiblePool(library);
            if (SolutionFirstBuilder.TryBuild(layerCells, gridSize, pool, stateLimit, Mathf.Min(timeLimitMs, remaining), out var resultPieces))
            {
                pieceSplitList.AddRange(resultPieces);
                pendingLayerPieceCount = resultPieces.Count;
                layerGenError = null;
                Repaint();
                return;
            }
        }

        // Aşama 2: Kütüphanedeki tüm parçaları daha geniş havuz ile denemeler
        var fullEligible = library.Where(d => d.volume >= 1 && d.volume <= maxPieceSize).ToList();
        if (fullEligible.Count == 0) fullEligible = library;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            int remaining = totalBudgetMs - (int)layerSw.ElapsedMilliseconds;
            if (remaining <= 0) break;
            var pool = fullEligible.OrderBy(_ => UnityEngine.Random.value).Take(Mathf.Min(fullEligible.Count, 15)).ToList();
            if (SolutionFirstBuilder.TryBuild(layerCells, gridSize, pool, stateLimit * 2, Mathf.Min(timeLimitMs * 2, remaining), out var resultPieces))
            {
                pieceSplitList.AddRange(resultPieces);
                pendingLayerPieceCount = resultPieces.Count;
                layerGenError = null;
                Repaint();
                return;
            }
        }

        // Aşama 3: Kütüphanedeki TÜM parçaları doğrudan arama motoruna vererek 1 deneme (kalan bütçe)
        int rem3 = totalBudgetMs - (int)layerSw.ElapsedMilliseconds;
        if (rem3 > 0 && SolutionFirstBuilder.TryBuild(layerCells, gridSize, library, stateLimit * 3, Mathf.Min(timeLimitMs * 3, rem3), out var fullResultPieces))
        {
            pieceSplitList.AddRange(fullResultPieces);
            pendingLayerPieceCount = fullResultPieces.Count;
            layerGenError = null;
            Repaint();
            return;
        }

        // Otomatik junk fallback KALDIRILDI: kütüphaneyle döşenemeyen katman için, kütüphanede
        // karşılığı OLMAYAN keyfi/çirkin parçalar üretmek yerine dürüstçe başarısız oluyoruz
        // ("geometrik cart curt"ın kaynağı buydu). Kullanıcı bilinçli olarak manuel "Otomatik
        // Tamamla" (ForceCompleteCurrentLayer) butonunu kullanabilir.
        layerGenError = $"⚠️ Katman Y={genCurrentLayerY} kütüphane parçalarıyla döşenemedi. Buz/hazır " +
                         "oranını düşürüp 'Yeniden Üret' deneyin; yine olmazsa 'Otomatik Tamamla' kullanın.";
        Repaint();
    }

    internal void ForceCompleteCurrentLayer()
    {
        var library = LoadPieceLibrary();
        var layerCells = new HashSet<Vector3Int>(occupiedCells.Where(c => c.y == genCurrentLayerY));
        layerCells.ExceptWith(prefilledCells);

        if (layerCells.Count > 0)
        {
            var fallbackPieces = FallbackDecomposeLayerCells(layerCells, library);
            if (fallbackPieces != null && fallbackPieces.Count > 0)
            {
                if (pendingLayerPieceCount > 0)
                {
                    pieceSplitList.RemoveRange(pieceSplitList.Count - pendingLayerPieceCount, pendingLayerPieceCount);
                    pendingLayerPieceCount = 0;
                }

                pieceSplitList.AddRange(fallbackPieces);
                pendingLayerPieceCount = fallbackPieces.Count;
                layerGenError = null;
                Repaint();
            }
        }
    }

    // Manuel "Otomatik Tamamla" için son çare bölme. Kütüphaneyle birebir eşleşmeyi GARANTİ
    // etmez (o yüzden otomatik akışta ARTIK kullanılmıyor, sadece kullanıcı bilinçli basınca),
    // ama düzgün BFS ile BAĞLI, kompakt 3-4 hücrelik parçalar üretir ve tek-hücrelik junk
    // parçaları komşularına kaynaştırır — eski açgözlü sürüm kopuk/tek-hücre şekiller üretebiliyordu.
    private List<List<Vector3Int>> FallbackDecomposeLayerCells(HashSet<Vector3Int> layerCells, List<PieceDefinition> library)
    {
        var result = new List<List<Vector3Int>>();
        var unvisited = new HashSet<Vector3Int>(layerCells);

        Vector3Int[] neighbors = { Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

        while (unvisited.Count > 0)
        {
            var start = unvisited.First();
            unvisited.Remove(start);
            var pieceCells = new List<Vector3Int> { start };

            int targetSize = UnityEngine.Random.Range(3, 5); // 3-4 hücre

            // BFS: parçanın HERHANGİ bir hücresinin komşularından büyü (eski sürüm sadece
            // start ve onun ilk komşusundan bakıyordu → çoğu zaman kopuk/eksik büyüyordu).
            var frontier = new Queue<Vector3Int>();
            frontier.Enqueue(start);
            while (pieceCells.Count < targetSize && frontier.Count > 0)
            {
                var cur = frontier.Dequeue();
                foreach (var dir in neighbors)
                {
                    if (pieceCells.Count >= targetSize) break;
                    Vector3Int n = cur + dir;
                    if (unvisited.Remove(n))
                    {
                        pieceCells.Add(n);
                        frontier.Enqueue(n);
                    }
                }
            }

            result.Add(pieceCells);
        }

        // Tek-hücrelik parça bırakma: komşu bir parçaya kaynaştır (junk single'ları yok et).
        for (int i = result.Count - 1; i >= 0; i--)
        {
            if (result[i].Count > 1) continue;
            var solo = result[i][0];
            foreach (var other in result)
            {
                if (other == result[i]) continue;
                bool adjacent = other.Any(c =>
                    Mathf.Abs(c.x - solo.x) + Mathf.Abs(c.y - solo.y) + Mathf.Abs(c.z - solo.z) == 1);
                if (adjacent) { other.Add(solo); result.RemoveAt(i); break; }
            }
        }

        return result;
    }

    // Geçerli katmanı kilitler (kuyruktan çıkarılamaz hale getirir) ve bir sonraki katmana geçer.
    internal void ApproveCurrentLayerAndAdvance()
    {
        pendingLayerPieceCount = 0;
        genCurrentLayerY++;
        GenerateCurrentLayerPieces();
    }

    // Geçerli katmanı (henüz onaylanmamış kuyruk parçalarını) yeni bir rastgele havuzla
    // yeniden dener — GenerateCurrentLayerPieces zaten kendi başında eski denemeyi temizliyor.
    internal void RegenerateCurrentLayer()
    {
        GenerateCurrentLayerPieces();
    }

    // Tüm katmanlar onaylandıktan sonra: buz güvenlik marjı parçalarını ekler (mevcut atomik
    // yoldaki SplitShapeWithSolutionFirstLibrary ile BİREBİR aynı mantık) ve bütünsel LevelSolver
    // doğrulamasını (renk/buz erime sırası dahil — bu, katman bazlı SolutionFirstBuilder'ın
    // KONTROL ETMEDİĞİ tek şey, bkz. SolutionFirstBuilder.cs üstündeki açıklama) çalıştırır.
    private void FinalizeLayerByLayerGeneration()
    {
        layerGenActive = false;

        int gridVolume = gridSize.x * gridSize.y * gridSize.z;
        int timeoutMs = gridVolume < 50 ? 1500 : gridVolume < 100 ? 2500 : 4000;
        lastSolverResult = TestCurrentPiecesWithSolver(timeoutMs);
        solverRan = true;

        Debug.Log($"🧩 Katman katman üretim tamamlandı: Parça={pieceSplitList.Count}, " +
                  $"Çözülebilir={lastSolverResult.isSolvable}, Zorluk={lastSolverResult.difficultyLabel}");

        Repaint();
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
        // 2+3. Engelleri dağıt + parçalara böl: artık DistributeObstacles/SolutionFirstBuilder tek
        // seferlik değil, LevelForge.DifficultySearchEngine tarafından hedef zorluğa toleransla
        // ulaşana kadar (yapılandırılmış parametre mutasyonlarıyla) tekrar tekrar çalıştırılıyor —
        // bkz. RunDifficultySearch.
        RunDifficultySearch(W, H, D);
    }

    // ApplyObstaclesAndSplitPieces'in buz/prefilled dağıtım kısmı — StartLayerByLayerGeneration
    // (katman katman üretim akışı) tarafından da RunDifficultySearch'ü hiç tetiklemeden ayrıca
    // TEK SEFERLİK kullanılabilmesi için ayrı bir metoda çıkarıldı (bkz. GenerateCandidateForSearch,
    // burayı her denemede TEKRAR TEKRAR çağırıyor — DistributeObstacles'ın kendisi bundan habersiz,
    // davranışı hep aynı, sadece çağrılma sıklığı bağlama göre değişiyor).
    private void DistributeObstacles(int W, int H, int D)
    {
        // 2. AI Parametreleri: Renkli Küpler (Prefilled) Dağıt
        // Otomatik üretim TAMAMEN buzsuz (ice-free) olarak çalışır.
        // Buzlar yalnızca kullanıcı 'Buzlu Yerleri Tespit Et' butonuna bastığında eklenir.
        List<Vector3Int> finalOccupied = occupiedCells.ToList();
        int targetPrefillCount = Mathf.RoundToInt(finalOccupied.Count * prefillPercentage);

        // Karıştır
        finalOccupied = finalOccupied.OrderBy(x => Random.value).ToList();

        // Hazır renkli blokları dağıt
        Dictionary<int, int> layerColorIdx = new Dictionary<int, int>();
        int prefilledDone = 0;
        foreach (var cell in finalOccupied)
        {
            if (prefilledDone >= targetPrefillCount) break;

            if (!layerColorIdx.TryGetValue(cell.y, out int colorIdx))
            {
                colorIdx = Random.Range(0, 6); // PREFILL_COLORS boyutu 6
                layerColorIdx[cell.y] = colorIdx;
            }

            prefilledCells.Add(cell);
            prefilledMatIdx.Add(colorIdx);
            prefilledDone++;
        }
    }

    // Buzları YALNIZCA kullanıcı 'Buzlu Yerleri Tespit Et' butonuna bastığında manuel dağıtır.
    internal void DetectAndDistributeIce()
    {
        frozenCells.Clear();
        frozenHitCounts.Clear();

        List<Vector3Int> finalOccupied = occupiedCells.ToList();
        if (finalOccupied.Count == 0) return;

        float ratio = icePercentage > 0f ? icePercentage : 0.20f;
        int targetIceCount = Mathf.Max(1, Mathf.RoundToInt(finalOccupied.Count * ratio));

        finalOccupied = finalOccupied.OrderBy(x => Random.value).ToList();

        int iceDone = 0;
        int H = gridSize.y, W = gridSize.x, D = gridSize.z;
        foreach (var cell in finalOccupied)
        {
            if (iceDone >= targetIceCount) break;
            if (prefilledCells.Contains(cell)) continue;

            bool strategicPlace = (cell.y <= H / 2) || (cell.x == 0 || cell.x == W - 1 || cell.z == 0 || cell.z == D - 1);
            if (strategicPlace || Random.value < 0.5f)
            {
                frozenCells.Add(cell);
                frozenHitCounts.Add(IceHitCountUtility.RollHitCount((IceHitCountUtility.IceDifficulty)(int)selectedDifficulty));
                iceDone++;
            }
        }

        Debug.Log($"❄️ Buz tespiti çalıştırıldı: {frozenCells.Count} dondurulmuş blok yerleştirildi.");
        Repaint();
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
        pieceLibraryVersion++;
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

    // Zorluk profiline uyan (difficultyTags), TAZE rastgele karıştırılmış bir parça havuzu
    // örnekler (spawnWeight'e göre ağırlıklandırma SolutionFirstBuilder'ın kendi sıralamasında
    // yapılıyor, burada sadece havuzun İÇERİĞİ seçiliyor). SplitShapeWithSolutionFirstLibrary
    // (şekil bazlı) ve GenerateCurrentLayerPieces (katman bazlı) tarafından ortak kullanılır —
    // ikisi de aynı örnekleme mantığına güveniyor, sadece cellsToFill kapsamları farklı.
    // Havuzun hangi kademeden geldiğini (tag+boyut / sadece boyut / tüm kütüphane) UI/log'a
    // yansıtmak için — eskiden bu kademe hiç dışa raporlanmıyordu, ör. "Kolay" bir seviye
    // sessizce tüm (etiketsiz, her boyuttan) kütüphaneden örnekleyebiliyordu ve kimse fark
    // etmiyordu. RunDifficultySearch başladığında sıfırlanır (bkz. çağrı yeri).
    internal string lastPoolFallbackInfo;

    private List<PieceDefinition> SampleEligiblePool(List<PieceDefinition> library)
    {
        string profileTag = selectedDifficulty.ToString();
        var eligible = library
            .Where(d => d.difficultyTags == null || d.difficultyTags.Count == 0 || d.difficultyTags.Contains(profileTag))
            .Where(d => d.volume >= minPieceSize && d.volume <= maxPieceSize)
            .ToList();

        lastPoolFallbackInfo = $"tag+boyut eşleşmesi ({profileTag}, {minPieceSize}-{maxPieceSize})";

        if (eligible.Count == 0)
        {
            // Zorluk etiketine uyan uygun boyutta parça yoksa, sadece boyut filtresini uygula
            eligible = library.Where(d => d.volume >= minPieceSize && d.volume <= maxPieceSize).ToList();
            lastPoolFallbackInfo = $"sadece boyut filtresi ({minPieceSize}-{maxPieceSize}) — '{profileTag}' etiketli uygun boyutta parça bulunamadı";
            Debug.LogWarning($"⚠️ SampleEligiblePool: {lastPoolFallbackInfo}");
        }

        if (eligible.Count == 0)
        {
            // Hiçbiri yoksa son çare olarak hepsini al
            eligible = library;
            lastPoolFallbackInfo = "TÜM kütüphane (boyut/etiket filtresi de eşleşmedi) — pool boyut/etiket açısından tamamen kontrolsüz";
            Debug.LogWarning($"⚠️ SampleEligiblePool: {lastPoolFallbackInfo}");
        }

        int idealCount = DifficultySpecs.TryGetValue(selectedDifficulty, out var spec) ? spec.idealPieceCount : 5;
        int poolSize = Mathf.Clamp(idealCount + 2, 3, eligible.Count);

        // Çeşitliliği garanti et: Sadece tek bir boyuttan (örneğin sadece 2'lik) dolmasını engelle.
        // Havuza her boyuttan en az 1 parça koymaya çalış.
        var selected = new List<PieceDefinition>();
        var groupedByVol = eligible.GroupBy(d => d.volume).ToList();
        
        // Önce her hacim grubundan 1'er tane al (örneğin bir tane 2'lik, bir tane 3'lük, bir tane 4'lük)
        foreach (var group in groupedByVol)
        {
            if (selected.Count >= poolSize) break;
            var list = group.ToList();
            selected.Add(list[UnityEngine.Random.Range(0, list.Count)]);
        }

        // Havuzda hala yer varsa geri kalanını rastgele doldur
        var remaining = eligible.Except(selected).ToList();
        while (selected.Count < poolSize && remaining.Count > 0)
        {
            int idx = UnityEngine.Random.Range(0, remaining.Count);
            selected.Add(remaining[idx]);
            remaining.RemoveAt(idx);
        }

        // ── Dolgu garantisi ──────────────────────────────────────────────────────
        // Exact tiling'in TAMAMLANABİLMESİ için havuzda mutlaka küçük dolgu parçası olmalı:
        // büyük parçalar hacmin çoğunu doldurur, 1-2 hücrelik dolgular artık boşlukları kapatır.
        // minPieceSize yüksekken (ör. "Uzman") bu dolgular boyut filtresince dışlanıp havuz
        // {5,6,8,9} gibi birkaç büyük parçaya düşüyor ve çoğu katman TAM döşenemiyordu → her
        // deneme InsufficientContent (bkz. SplitShapeWithSolutionFirstLibrary). Bu yüzden en
        // küçük 1-2 parçayı minPieceSize'tan BAĞIMSIZ garanti ekliyoruz. Zorluk yine büyük
        // parçalardan gelir: SolutionFirstBuilder büyüğü ÖNCE koyduğu için dolgu baskın olmaz,
        // yalnızca büyükler sığmadığında artık boşluğu kapatır; aşırı kolay çıkarsa zaten
        // DifficultySearchEngine skoru tutturamayıp o adayı eler.
        foreach (int fillerVol in new[] { 1, 2 })
        {
            if (selected.Any(d => d.volume == fillerVol)) continue;
            var cand = library.Where(d => d.volume == fillerVol).ToList();
            if (cand.Count > 0) selected.Add(cand[UnityEngine.Random.Range(0, cand.Count)]);
        }

        return selected;
    }

    // "Kütüphane / Solution-First" modu: RunDifficultySearch'ün (LevelForge.DifficultySearchEngine)
    // her denemesi için Assets/PieceDefinitions/ altından TAZE, rastgele bir
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

        // Doldurulması gereken hücreler: prefilled VE frozen hariç. [DÜZELTİLDİ, oyun-içi
        // teşhis: buz hücreleri eskiden dahil ediliyordu ("erime sırası sonradan LevelSolver'da
        // doğrulanır" varsayımıyla) — ama bu, SolutionFirstBuilder'ın açık bir hücreyle bitişik
        // bir buz hücresini TEK bir parçaya birlikte atamasına izin veriyordu. Öyle bir parça
        // gerçek oyunda ASLA yerleştirilemez (CanPlace buzlu hücreyi reddeder),
        // bu da açık hücreleri parçasız/döşenmemiş bırakıp (özellikle küçük, birbirinden kopuk
        // açık adacıklar oluştuğunda) oyunun daha ilk katmanda "hamle yok" diye kilitlenmesine
        // yol açabiliyordu. Buz hücreleri hâlâ toplam hedefin bir parçası — onlar için aşağıdaki
        // "buz güvenlik payı" (buz başına yedek parça) mekanizması kullanılıyor.
        var cellsToFill = new HashSet<Vector3Int>(occupiedCells);
        cellsToFill.ExceptWith(prefilledCells);

        var pool = SampleEligiblePool(library);

        int gridVolume = gridSize.x * gridSize.y * gridSize.z;
        int stateLimit = gridVolume < 50 ? 30000 : gridVolume < 100 ? 50000 : 80000;
        int timeLimitMs = gridVolume < 50 ? 1500 : gridVolume < 100 ? 2500 : 4000;

        bool built = SolutionFirstBuilder.TryBuild(cellsToFill, gridSize, pool, stateLimit, timeLimitMs, out var resultPieces);

        Debug.Log($"  [Kütüphane/Solution-First] Deneme {attempt + 1}: havuz={pool.Count} parça tipi, " +
                   (built ? $"BAŞARILI ({resultPieces.Count} parça yerleşti)" : "döşenemedi"));

        return built ? resultPieces : new List<List<Vector3Int>>();
    }

    // ═════════════════════════════════════════════════════════════
    // KAPALI DÖNGÜ ZORLUK ARAMASI — LevelForge.DifficultySearchEngine
    // Eskiden burada "3-5 strateji dene, çözülebilenler arasından en iyi puanlıyı seç" vardı
    // (bkz. git geçmişi) — hiçbiri hedef zorluğa yakın olmasa bile en az kötüyü export ediyordu.
    // Artık gerçek bir kapalı döngü: LevelForge motoru hedefe toleransla ulaşana kadar (yapılandırılmış
    // parametre mutasyonlarıyla, bkz. BlockMerge3DParameterSpace) dener, tutturamazsa AÇIKÇA
    // başarısız döner (lastSearchResult.success=false) — "en yakın ama hedef dışı" bir sonucu asla
    // sessizce kabul etmez. Bkz. Packages/com.fogboundgames.levelforge/ADAPTER_GUIDE.md.
    // ═════════════════════════════════════════════════════════════
    private int searchAttemptCounter;
    private BlockMerge3DCandidate lastGeneratedCandidateForSearch;

    private SearchBudget BuildSearchBudget()
    {
        int gridVolume = gridSize.x * gridSize.y * gridSize.z;
        return new SearchBudget
        {
            maxAttempts = gridVolume < 50 ? 18 : gridVolume < 100 ? 24 : 30,
            maxTotalTimeMs = gridVolume < 50 ? 12000 : gridVolume < 100 ? 20000 : 30000,
            // Bkz. BlockMerge3DDifficultyTiers.ScoreTolerance notu: en yüksek çarpan (1.5x) bile
            // komşu tier'ların hedef bandına asla taşmayacak şekilde seçildi.
            toleranceMultiplierSchedule = new float[] { 1f, 1f, 1f, 1f, 1.15f, 1.15f, 1.3f, 1.3f, 1.5f }
        };
    }

    // Her denemede DistributeObstacles + SolutionFirstBuilder'ı çalıştırıp bağımsız bir aday üretir.
    // DistributeObstacles/SplitShapeWithSolutionFirstLibrary pencerenin ENSTANTANE alanlarını
    // (icePercentage, prefillPercentage, minPieceSize, maxPieceSize, prefilledCells, frozenCells, ...)
    // doğrudan okur/yazar — bu yüzden her çağrıda önce bu denemenin parametreleriyle güncelleniyor
    // (GenerateAndExportAIBatchDataset'in de kullandığı "geçici state mutasyonu" deseniyle aynı).
    private BlockMerge3DCandidate GenerateCandidateForSearch(BlockMerge3DGenerationParams p, int W, int H, int D)
    {
        icePercentage = p.icePercentage;
        prefillPercentage = p.prefillPercentage;
        minPieceSize = p.minPieceSize;
        maxPieceSize = p.maxPieceSize;

        prefilledCells.Clear();
        prefilledMatIdx.Clear();
        frozenCells.Clear();
        frozenHitCounts.Clear();

        DistributeObstacles(W, H, D);
        var pieces = SplitShapeWithSolutionFirstLibrary(searchAttemptCounter++);

        var candidate = new BlockMerge3DCandidate
        {
            gridSize = gridSize,
            cellSize = cellSize,
            spacing = spacing,
            occupiedCells = new List<Vector3Int>(occupiedCells),
            prefilledCells = new List<Vector3Int>(prefilledCells),
            prefilledMaterialIndices = new List<int>(prefilledMatIdx),
            frozenCells = new List<Vector3Int>(frozenCells),
            frozenHitCounts = new List<int>(frozenHitCounts),
            pieceSplitList = pieces
        };
        lastGeneratedCandidateForSearch = candidate;
        return candidate;
    }

    // lastSearchResult.best (başarı) ya da son üretilen adayı (başarısızlık — kullanıcı en azından
    // NE üretildiğini görebilsin diye) pencere durumuna (grid önizleme, parça listesi) uygular.
    private void ApplyCandidateToWindowState(BlockMerge3DCandidate candidate)
    {
        if (candidate == null)
        {
            pieceSplitList = new List<List<Vector3Int>>();
            return;
        }
        prefilledCells = new List<Vector3Int>(candidate.prefilledCells);
        prefilledMatIdx = new List<int>(candidate.prefilledMaterialIndices);
        frozenCells = new List<Vector3Int>(candidate.frozenCells);
        frozenHitCounts = new List<int>(candidate.frozenHitCounts);
        pieceSplitList = candidate.pieceSplitList != null
            ? new List<List<Vector3Int>>(candidate.pieceSplitList)
            : new List<List<Vector3Int>>();
    }

    private void RunDifficultySearch(int W, int H, int D)
    {
        searchAttemptCounter = 0;
        lastGeneratedCandidateForSearch = null;

        var engine = new DifficultySearchEngine();
        var tier = BlockMerge3DDifficultyTiers.GetTier(selectedDifficulty);
        var evaluator = new BlockMerge3DDifficultyEvaluator();
        var paramSpace = new BlockMerge3DParameterSpace();
        var iceRevalidator = new BlockMerge3DIceRevalidator();

        var initialParams = new BlockMerge3DGenerationParams
        {
            icePercentage = icePercentage,
            prefillPercentage = prefillPercentage,
            minPieceSize = minPieceSize,
            maxPieceSize = maxPieceSize
        };

        var result = engine.Run(
            initialParams,
            tier,
            p => GenerateCandidateForSearch(p, W, H, D),
            evaluator,
            paramSpace,
            BuildSearchBudget(),
            stochasticCheck: iceRevalidator,
            stochasticTrials: 12,
            stochasticRequiredPassRate: 1f);

        lastSearchResult = result;

        if (result.success)
        {
            ApplyCandidateToWindowState(result.best);
            lastSolverResult = result.best.lastSolverResult;
            solverRan = true;

            Debug.Log($"✅ Kapalı döngü BAŞARILI: {result.attemptsUsed} deneme, " +
                     $"skor={lastSolverResult.difficultyScore:F2}, zorluk={lastSolverResult.difficultyLabel}, " +
                     $"parça={pieceSplitList.Count}");
        }
        else
        {
            // Son üretilen (ama hedefi tutturamayan) adayı yine de pencereye yükle — kullanıcı
            // inceleyip İSTERSE kaydedebilsin. Artık engellenmiyor: kayıtta yalnızca onay sorulur
            // (bkz. ExportProceduralLevel — eski "Zorunlu Koruma Kuralı #1" hard-block kaldırıldı).
            ApplyCandidateToWindowState(lastGeneratedCandidateForSearch);
            solverRan = true;
            lastSolverResult = new SolverResult
            {
                isSolvable = false,
                timedOut = false,
                failureReason = result.failureSummary
            };

            Debug.LogWarning($"❌ Kapalı döngü BAŞARISIZ: {result.failureSummary}");
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
    // Eskiden ApplyDifficultyScaleForMode ve (artık kaldırılmış) SelectBestStrategy'nin hedef
    // tablosu birbirinden bağımsız iki ayrı switch/hardcoded tablo tutuyordu — biri güncellenip
    // diğeri unutulursa (ör. ORTA'ya yeni bir alan eklenip diğer tabloya yansıtılmazsa) sessizce
    // birbirinden sapabiliyordu. Artık hem ApplyDifficultyScaleForMode hem de kapalı döngü arama
    // motorunun DifficultyTier'ları (bkz. BlockMerge3DDifficultyTiers) AYNI DifficultySpecs
    // sözlüğünden okuyor.
    // internal: LevelForgeAdapter/BlockMerge3DDifficultyTiers bu tabloyu DifficultyTier
    // asset'lerine dönüştürmek için doğrudan okuyor (kopyalamadan, tek gerçek kaynak burada kalır).
    internal struct AIDifficultySpec
    {
        public float baseTime;
        public float baseTarget;
        public float prefillPercentage;
        public float icePercentage;
        public int minPieceSize;
        public int maxPieceSize;
        // Kapalı döngü arama motorunun hedefleri (bkz. BlockMerge3DDifficultyTiers, DifficultySearchEngine).
        public float solverTargetScore; // LevelSolver 0.0-1.0 zorluk skalası
        public int idealPieceCount;
        public int minMoves;
        public int maxMoves;
    }

    internal static readonly Dictionary<AILevelDifficulty, AIDifficultySpec> DifficultySpecs = new Dictionary<AILevelDifficulty, AIDifficultySpec>
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

    internal static AIDifficultySpec GetDifficultySpec(AILevelDifficulty mode)
    {
        return DifficultySpecs.TryGetValue(mode, out var spec) ? spec : DifficultySpecs[AILevelDifficulty.Orta];
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

        // timedOut da geçerli sayılır: arama limiti aşımı "çözülemez" KANITLAMAZ, sadece bu
        // bütçede bulunamadı demektir (bkz. LevelSolver.SolverResult.timedOut). Sadece kanıtlanmış
        // (gerçek) çözülemezlik export'u engellemeli.
        // Eskiden burada HARD BLOCK vardı (doğrulanmadıysa kaydedilemez). İstek üzerine tavsiyeye
        // çevrildi: kullanıcı çözülebilir görünen bir seviyeyi kaydedip oyunda test edebilsin.
        // Silent kötü kayıtları önlemek için yalnızca bilinçli bir ONAY soruluyor, engel yok.
        bool validatedOk = solverRan && lastSolverResult != null && (lastSolverResult.isSolvable || lastSolverResult.timedOut);
        if (!validatedOk)
        {
            bool proceed = EditorUtility.DisplayDialog("Doğrulanmamış Seviye",
                "Bu seviye solver tarafından ÇÖZÜLEBİLİR olarak doğrulanmadı (çözülemez bulundu ya da " +
                "arama bütçesi aşıldı — çözülemez KANITLANMADI).\n\n" +
                "Yine de kaydetmek istiyor musun? Oyunda kendin test edebilirsin.",
                "Yine de Kaydet", "Vazgeç");
            if (!proceed) return;
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
                "Seviye kaydedilemedi (dosya/asset yazımı sırasında bir sorun oluştu). " +
                "Ayrıntı için Console'a bakın.", "Tamam");
        }
    }

    private LevelData ExportProceduralLevelCore(string targetLevelName, float targetLevelTime, int targetLevelTarget)
    {
        // Zorunlu Koruma Kuralı #1 (bkz. BlockMerge3D_Seviye_Uretim_Sistemi_v2.md §13): doğrulanmamış
        // (validated == false) hiçbir seviye kaydedilemez. Bu, tek gerçek kayıt noktası olduğu için
        // (ExportProceduralLevel VE GenerateAndExportAIBatchDataset ikisi de buraya çağrı yapıyor)
        // burada kontrol edilmesi, çağıran her yerin ayrı ayrı doğru davranmasına güvenmekten daha güvenli.
        // timedOut da geçerli sayılır — bkz. ExportProceduralLevel'daki aynı gerekçe.
        if (!(solverRan && lastSolverResult != null && (lastSolverResult.isSolvable || lastSolverResult.timedOut)))
        {
            // Artık ENGELLEMİYOR (istek üzerine): yalnızca uyarı. Kullanıcı bilinçli olarak
            // (bkz. ExportProceduralLevel onayı) doğrulanmamış bir seviyeyi kaydetmeyi seçebilir.
            Debug.LogWarning($"⚠️ '{targetLevelName}' solver tarafından doğrulanmadı — yine de kaydediliyor (kullanıcı isteği). Oyunda test et.");
        }

        // Zorunlu Koruma Kuralı #2: buz içeren bir seviye, LevelSolver'ın DETERMİNİSTİK vekil renk
        // simülasyonunu (proxyColor = pieceIndex % 8, bkz. LevelSolver.TryPlacePiece) geçmiş olsa
        // bile gerçek oyunun RASTGELE renk atamasında kırılabilir — bkz. LevelSolver.
        // ReplayWithRandomizedColors üstündeki not. RunDifficultySearch yolundan gelen bir seviye
        // bunu zaten (kabul edilmeden önce, bkz. BlockMerge3DIceRevalidator) doğrulamış olur — ama
        // bu kontrol burada, Kural #1 ile AYNI TEK kayıt noktasında, o motoru kullanmayan yollar
        // (ör. LevelCreationWizardWindow'un katman-katman akışı) için de tekrar çalıştırılır.
        // timedOut durumunda solutionSteps yok (hiçbir çözüm bulunamadı) — bu yüzden sadece
        // isSolvable=true iken kontrol edilir.
        if (lastSolverResult.isSolvable && frozenCells.Count > 0)
        {
            GameObject iceCheckShape = CreateTempMainShape();
            float passRate = new LevelSolver().ReplayWithRandomizedColors(
                lastSolverResult.solutionSteps, iceCheckShape.GetComponent<CubeShapeDataHolder>(),
                12, BlockMerge3DIceRevalidator.IcePaletteSize, new System.Random());
            DestroyImmediate(iceCheckShape);

            if (passRate < 1f)
            {
                // Artık ENGELLEMİYOR: uyarı. Buz erime sırası bazı renk dağılımlarında kırılabilir
                // ama kullanıcı yine de kaydedip oyunda test etmek isteyebilir.
                Debug.LogWarning($"⚠️ '{targetLevelName}': buz Monte Carlo doğrulaması zayıf " +
                                 $"(geçiş oranı %{passRate * 100f:F0}) — yine de kaydediliyor (kullanıcı isteği). Oyunda test et.");
            }
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
            ph.originLayerY  = minY; // Sıralı katman mekaniği bu parçanın hangi katman için çözüldüğünü bilmeli.

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
        fh.frozenHitCounts          = new List<int>(frozenHitCounts);

        foreach (var cell in occupiedCells)
        {
            // Target grid / full shape outline should always be made with the constant global cubePrefab
            // so they render as clean translucent slot boxes at runtime instead of duplicate animal models.
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
        holder.frozenHitCounts = new List<int>(frozenHitCounts);
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

    // Eskiden burada lastSolverResult.failureReason Türkçe metni üzerinde elle ayrıştırılan
    // (".Contains("yetersiz")" vb.) TEK SEFERLİK bir parametre düzeltmesi vardı — hem kırılgandı
    // (LevelSolver'ın mesajı değişirse sessizce bozulurdu) hem de "renk"/"katman" dalı artık asla
    // tetiklenmeyen ölü koddu (2026-07-13 redesign'da o kısıt kaldırıldı). Bu iş artık motorun
    // içinde, her deneme için YAPILANDIRILMIŞ (FailureReasonCode bazlı) olarak yapılıyor — bkz.
    // BlockMerge3DParameterSpace, RunDifficultySearch. Bu buton artık kapalı döngü aramayı yeni
    // bir rastgele tohumla baştan çalıştırır.
    private void AutoAdjustAndRegenerate()
    {
        GenerateLevelProcedurally();

        bool ok = lastSolverResult != null && lastSolverResult.isSolvable;
        EditorUtility.DisplayDialog("Yeniden Üretim",
            ok
                ? "Kapalı döngü arama başarılı oldu ve seviye yeniden oluşturuldu. Sonuçları kontrol edin."
                : "Kapalı döngü arama yine hedef zorluğu tutturamadı. 'Çözülebilirlik Doğrulama' kutusundaki " +
                  "özete bakıp zorluk parametrelerini elle gevşetmeyi (buz/hazır küp oranını azaltmayı) deneyin.",
            "Tamam");
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
            fontStyle = FontStyle.Normal,
            normal = { textColor = new Color(0.75f, 0.75f, 0.75f), background = MakeTex(2, 2, new Color(0.18f, 0.18f, 0.22f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.26f, 0.26f, 0.30f)) }
        };

        styleInstructionBox = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 10, 10),
            margin = new RectOffset(0, 0, 5, 5)
        };

        stylePrimaryButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.95f, 0.40f, 0.70f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(1.00f, 0.50f, 0.80f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.80f, 0.30f, 0.55f)) }
        };

        styleSuccessButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.18f, 0.70f, 0.40f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.24f, 0.80f, 0.48f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.14f, 0.58f, 0.32f)) }
        };

        styleDangerButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.90f, 0.30f, 0.30f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.98f, 0.38f, 0.38f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.75f, 0.22f, 0.22f)) }
        };

        styleWarningButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white, background = MakeTex(2, 2, new Color(1.00f, 0.75f, 0.20f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(1.00f, 0.82f, 0.32f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.85f, 0.62f, 0.15f)) }
        };

        styleDarkButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.9f, 0.9f, 0.9f), background = MakeTex(2, 2, new Color(0.24f, 0.24f, 0.28f)) },
            hover = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.32f, 0.32f, 0.38f)) },
            active = { textColor = Color.white, background = MakeTex(2, 2, new Color(0.18f, 0.18f, 0.22f)) }
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
        levelTarget = 0;

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
            levelTarget = 0;
        }
        else
        {
            levelTime = Mathf.Round(spec.baseTime);
            levelTarget = 0;
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
        AILevelDifficulty origSelectedDifficulty = selectedDifficulty;

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

            // RunDifficultySearch (bkz. ApplyObstaclesAndSplitPieces) artık selectedDifficulty'nin
            // DifficultyTier'ına doğru AKTİF olarak yönlendiriyor (eskiden sadece pasif puanlama
            // yapan SelectBestStrategy de aynı alanı okuyordu, ama pasif skorlamanın etkisi zayıftı).
            // Bu yüzden bu küratörlü 10-level müfredatın her adımı, pencerede o an seçili olan
            // (kullanıcının UI'de bıraktığı) zorlukla değil, KENDİ kademesine karşılık gelen tier'la
            // aranmalı — aksi halde örn. Level 1'in kasıtlı olarak trivial tasarımı, pencerede "Uzman"
            // seçiliyse kapalı döngü tarafından zorlaştırılmaya çalışılırdı.
            selectedDifficulty = i <= 3 ? AILevelDifficulty.Kolay
                                : i <= 6 ? AILevelDifficulty.Orta
                                : i <= 8 ? AILevelDifficulty.Zor
                                : AILevelDifficulty.Uzman;

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
            frozenHitCounts.Clear();
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
        selectedDifficulty = origSelectedDifficulty;

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
