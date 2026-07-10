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

    private static readonly Color COL_BG         = new Color(0.09f, 0.09f, 0.12f);
    private static readonly Color COL_GRID        = new Color(0.24f, 0.26f, 0.35f);
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
    private LevelTemplate selectedTemplate; // Şablon bazlı üretim
    private string levelName         = "AI_Level_1";
    private float levelTime          = 75f;
    private int levelTarget          = 150;
    private Vector3Int gridSize      = new Vector3Int(5, 5, 5);
    private float cellSize           = 1.0f;
    private float spacing            = 0.1f;

    private GeneratorType genType    = GeneratorType.Pyramid;
    private SymmetryMode symmetry    = SymmetryMode.XZ_Axis;
    private float fillDensity        = 1.0f;   // Tam dolu (default)
    private float icePercentage      = 0.10f;  // Donmuş blok oranı
    private float prefillPercentage  = 0.0f;   // Prefilled (renk çatışması riski var)
    
    // Parçalara Ayırma Ayarları
    private int minPieceSize         = 1;
    private int maxPieceSize         = 5;

    // Prompt tabanlı üretim
    private string aiPrompt          = "star with ice at base and golden corners";

    // ── Seviye Zorluk / Hızlı Ayar Ölçeği ─────────────────────────
    private int targetLevelIndex = 1;
    private string levelDifficultyModeSuggestion = "Kolay";
    private bool autoApplyDifficulty = true;

    // ── Grid Verisi ────────────────────────────────────────────────
    private HashSet<Vector3Int> occupiedCells   = new HashSet<Vector3Int>();
    private List<Vector3Int> prefilledCells     = new List<Vector3Int>();
    private List<int> prefilledMatIdx           = new List<int>();
    private List<Vector3Int> frozenCells        = new List<Vector3Int>();
    private List<List<Vector3Int>> pieceSplitList = new List<List<Vector3Int>>();

    // ── Solver Sonucu ─────────────────────────────────────────────
    private SolverResult lastSolverResult;
    private bool solverRan = false;

    // ── UI Durumu ─────────────────────────────────────────────────
    private int activeLayer          = 0;
    private float cellPx             = 35f;
    private AIDrawView drawView      = AIDrawView.FullShape;
    private Vector2 leftScroll, rightScroll;
    private GameObject cubePrefab;
    private int activeTab = 0; // 0: AI Jeneratör, 1: AI Eğitim Paneli

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
        else
        {
            DrawEducationPanel();
        }
    }

    // ── Sol Panel (Parametreler) ──────────────────────────────────
    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(420), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        // ŞABLON SEÇİCİ (ÖNCELİKLİ)
        GUILayout.Label("📐 ŞABLON BAZLI ÜRETİM", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.HelpBox("AI sıfırdan grid oluşturmaz. Hazır şablonlar üzerinden çalışır.", MessageType.Info);
        
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
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // 🏆 SEVİYE BAZLI DİNAMİK AYARLAR VE ÖLÇEK (HINTS & SCALE)
        GUILayout.Label("🏆 SEVİYE BAZLI HIZLI AYARLAR", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        // 1. Hızlı Zorluk Bölgesi Seçimi
        EditorGUILayout.LabelField("Hızlı Bölge Seçimi:", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        
        // Kolay
        GUI.backgroundColor = targetLevelIndex <= 10 ? Color.green : new Color(0.7f, 1f, 0.7f, 0.4f);
        if (GUILayout.Button("KOLAY (1-10)", EditorStyles.miniButtonLeft, GUILayout.Height(22)))
        {
            targetLevelIndex = 1;
            ApplyDifficultyScale(targetLevelIndex);
        }
        
        // Orta
        GUI.backgroundColor = (targetLevelIndex > 10 && targetLevelIndex <= 30) ? new Color(1.0f, 0.6f, 0.0f) : new Color(1f, 0.8f, 0.5f, 0.4f);
        if (GUILayout.Button("ORTA (11-30)", EditorStyles.miniButtonMid, GUILayout.Height(22)))
        {
            targetLevelIndex = 15;
            ApplyDifficultyScale(targetLevelIndex);
        }
        
        // Zor
        GUI.backgroundColor = (targetLevelIndex > 30 && targetLevelIndex <= 60) ? new Color(0.9f, 0.2f, 0.2f) : new Color(1f, 0.6f, 0.6f, 0.4f);
        if (GUILayout.Button("ZOR (31-60)", EditorStyles.miniButtonMid, GUILayout.Height(22)))
        {
            targetLevelIndex = 45;
            ApplyDifficultyScale(targetLevelIndex);
        }
        
        // Uzman
        GUI.backgroundColor = targetLevelIndex > 60 ? new Color(0.7f, 0.1f, 0.8f) : new Color(0.85f, 0.6f, 0.9f, 0.4f);
        if (GUILayout.Button("UZMAN (61+)", EditorStyles.miniButtonRight, GUILayout.Height(22)))
        {
            targetLevelIndex = 80;
            ApplyDifficultyScale(targetLevelIndex);
        }
        
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // 2. Seviye Seçimi (Etiket Üstte)
        EditorGUILayout.LabelField("🎯 Hedef Seviye Seçimi (Level):", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        
        // -10 Seviye
        if (GUILayout.Button("◀◀", GUILayout.Width(35), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Max(1, targetLevelIndex - 10);
            if (autoApplyDifficulty) ApplyDifficultyScale(targetLevelIndex);
        }
        
        // -1 Seviye
        if (GUILayout.Button("◀", GUILayout.Width(25), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Max(1, targetLevelIndex - 1);
            if (autoApplyDifficulty) ApplyDifficultyScale(targetLevelIndex);
        }

        int prevLevelIdx = targetLevelIndex;
        // Slider artık tüm genişliği kaplayabilir, çünkü etiket yukarı taşındı
        targetLevelIndex = EditorGUILayout.IntSlider(targetLevelIndex, 1, 100);
        if (targetLevelIndex != prevLevelIdx)
        {
            if (autoApplyDifficulty) ApplyDifficultyScale(targetLevelIndex);
        }

        // +1 Seviye
        if (GUILayout.Button("▶", GUILayout.Width(25), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Min(100, targetLevelIndex + 1);
            if (autoApplyDifficulty) ApplyDifficultyScale(targetLevelIndex);
        }
        
        // +10 Seviye
        if (GUILayout.Button("▶▶", GUILayout.Width(35), GUILayout.Height(22)))
        {
            targetLevelIndex = Mathf.Min(100, targetLevelIndex + 10);
            if (autoApplyDifficulty) ApplyDifficultyScale(targetLevelIndex);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        // 3. Otomatik Uygulama Seçeneği
        autoApplyDifficulty = EditorGUILayout.Toggle("Değerleri Otomatik Uygula", autoApplyDifficulty);

        EditorGUILayout.Space(4);

        // 4. Öneri Özeti ve Rozet
        Color badgeColor = Color.green;
        string badgeText = "KOLAY";
        if (levelDifficultyModeSuggestion == "Orta") { badgeColor = new Color(1.0f, 0.6f, 0.0f); badgeText = "ORTA"; }
        else if (levelDifficultyModeSuggestion == "Zor") { badgeColor = new Color(0.9f, 0.2f, 0.2f); badgeText = "ZOR"; }
        else if (levelDifficultyModeSuggestion == "Uzman") { badgeColor = new Color(0.7f, 0.1f, 0.8f); badgeText = "UZMAN"; }

        // Öneri Değerleri Önizlemesi
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"📈 LEVEL {targetLevelIndex} HESAPLANAN VERİLER", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField($"• Zorluk Modu: {badgeText}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"• Süre Limiti: {levelTime} sn", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"• Hedef Skor: {levelTarget} Puan", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"• Hazır Küp: %{prefillPercentage * 100f:F0}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"• Buz Küpü: %{icePercentage * 100f:F0}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"• Parça Boyutu: {minPieceSize} - {maxPieceSize} Küp", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        if (!autoApplyDifficulty)
        {
            GUI.backgroundColor = badgeColor;
            if (GUILayout.Button($"Önerilen Parametreleri Formlara Aktar", GUILayout.Height(28)))
            {
                ApplyDifficultyScale(targetLevelIndex);
            }
            GUI.backgroundColor = Color.white;
        }

        string hintText = "";
        if (targetLevelIndex <= 10)
        {
            hintText = "💡 Level 1-10: Kolay başlangıç. Buz blokları yok, küçük parçalar (1-3 küp) kullanılır. Süre sınırı bol tutulmuştur.";
        }
        else if (targetLevelIndex <= 30)
        {
            hintText = "💡 Level 11-30: Orta zorluk. Buz ve prefilled (renkli) bloklar eklenir. Parça boyutu 1-4 küp önerilir.";
        }
        else if (targetLevelIndex <= 60)
        {
            hintText = "💡 Level 31-60: Zor seviye. Buz oranı artırılmış ve prefilled bloklar stratejiktir. Parça boyutu 2-5 küp önerilir.";
        }
        else
        {
            hintText = "💡 Level 61-100: Uzman seviye. Dar zaman limitleri, yüksek buz oranı ve karmaşık parçalar (2-8 küp) bulunur.";
        }
        EditorGUILayout.HelpBox(hintText, MessageType.Info);
        EditorGUILayout.EndVertical();

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

        GUILayout.Label("PARÇA BÖLME ALGORİTMASI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        
        EditorGUILayout.BeginVertical();
        EditorGUIUtility.labelWidth = 110;
        minPieceSize = EditorGUILayout.IntSlider("Min Parça Küpü", minPieceSize, 1, 10);
        EditorGUILayout.EndVertical();
        
        GUILayout.Space(10);

        EditorGUILayout.BeginVertical();
        EditorGUIUtility.labelWidth = 100;
        maxPieceSize = EditorGUILayout.IntSlider("Max Parça Küpü", maxPieceSize, minPieceSize, 18);
        EditorGUILayout.EndVertical();
        
        EditorGUILayout.EndHorizontal();
        
        string pieceDifficultyTip = maxPieceSize <= 3 ? "Kolay Snapping" :
                                   maxPieceSize <= 5 ? "Standart Pentomino (Orta)" :
                                   maxPieceSize <= 7 ? "Büyük Parçalar (Zor)" :
                                   "Dev Parçalar (Uzman)";
        EditorGUILayout.LabelField($"ℹ Parça Önerisi: {pieceDifficultyTip}", EditorStyles.miniLabel);
        EditorGUILayout.HelpBox("AI, şekli otomatik olarak bu boyutlarda parçalara bölecektir. Tetris için max 4-5 önerilir.", MessageType.Info);
        EditorGUILayout.EndVertical();

        GUILayout.Space(12);

        EditorGUIUtility.labelWidth = originalLabelWidth;

        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Global Küp Prefabı", cubePrefab, typeof(GameObject), false);
        if (cubePrefab == null)
        {
            EditorGUILayout.HelpBox("⚠ Lütfen bir küp prefabı seçin.", MessageType.Warning);
        }

        GUI.backgroundColor = new Color(0.95f, 0.40f, 0.70f, 1f); // Magenta
        if (GUILayout.Button("⚡ SEVİYEYİ YAPAY ZEKA İLE OLUŞTUR", GUILayout.Height(40)))
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
        EditorGUILayout.EndVertical();

        // Katman Değiştirici
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

        // Çizim Alanı
        Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        DrawGrid2D(area);

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

            EditorGUI.DrawRect(cellRect, fill);

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
        }

        // Eksen etiketleri
        var lbl = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = new Color(1,1,1,0.32f) } };
        for (int x = 0; x < W; x++) GUI.Label(new Rect(ox + x * cellPx, oy - 14, cellPx, 14), x.ToString(), lbl);
        for (int z = 0; z < D; z++) GUI.Label(new Rect(ox - 18, oy + z * cellPx, 18, cellPx), z.ToString(), lbl);
    }

    private int GetPieceIndexForCell(Vector3Int cell)
    {
        for (int i = 0; i < pieceSplitList.Count; i++)
        {
            if (pieceSplitList[i].Contains(cell)) return i;
        }
        return -1;
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

        // Solver Sonuçları
        if (solverRan && lastSolverResult != null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🔍 ÇÖZÜLEBİLİRLİK ANALİZİ", styleHeader);
            
            if (lastSolverResult.isSolvable)
            {
                GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f, 1f);
                EditorGUILayout.HelpBox($"✅ Çözülebilir ({lastSolverResult.minMoveCount} hamle)\nZorluk: {lastSolverResult.difficultyLabel.ToUpper()} ({lastSolverResult.difficultyScore:F2})", MessageType.Info);
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = new Color(0.95f, 0.3f, 0.3f, 1f);
                EditorGUILayout.HelpBox($"❌ Çözülemez\n{lastSolverResult.failureReason}", MessageType.Error);
                GUI.backgroundColor = Color.white;
                
                GUILayout.Space(5);
                if (GUILayout.Button("🔁 Parametreleri Ayarlayıp Yeniden Üret", GUILayout.Height(35)))
                {
                    AutoAdjustAndRegenerate();
                }
            }
            EditorGUILayout.EndVertical();
        }

        GUILayout.Space(12);

        GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f, 1f); // Doygun Yeşil
        if (GUILayout.Button("💾 SEVİYEYİ VE PARÇALARI\nOTOMATİK DIŞA AKTAR", GUILayout.Height(50)))
        {
            ExportProceduralLevel();
        }
        GUI.backgroundColor = Color.white;

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
        GUILayout.Label("AI Level Designer, 3D matematiksel algoritmalar ve simetri kuralları kullanarak karmaşık bulmaca tasarımları üretir:\n" +
                      " • Şekil Tipi: Piramit, spiral, kale veya yıldız gibi temel şekilleri 3D voxel olarak hesaplar.\n" +
                      " • Doluluk Yoğunluğu: Şeklin ne kadarının dolu olacağını seçer. Düşük yoğunluklar daha oyuk/delikli şekiller üretir.\n" +
                      " • Simetri Modu: Sol tarafta yapılan çizimlerin sağ tarafta, ön taraftaki çizimlerin arkada otomatik tekrarlanmasını sağlayarak estetik tasarımlar ortaya çıkarır.", EditorStyles.wordWrappedLabel);
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

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ══ ALGORİTMİK YAPAY ZEKA METODLARI ═════════════════════════

    private void GenerateLevelProcedurally()
    {
        // Şablon kontrolü
        if (selectedTemplate == null)
        {
            EditorUtility.DisplayDialog("Hata", "Lütfen önce bir Level Şablonu seçin (Assets/Templates/)", "Tamam");
            return;
        }

        occupiedCells.Clear();
        prefilledCells.Clear();
        prefilledMatIdx.Clear();
        frozenCells.Clear();
        pieceSplitList.Clear();

        // 1. Şablondan Grid'i Yükle
        int W = gridSize.x;
        int H = gridSize.y;
        int D = gridSize.z;

        // Şablon boşsa (occupiedCells listesi boşsa) = tam dolu küp
        if (selectedTemplate.occupiedCells == null || selectedTemplate.occupiedCells.Count == 0)
        {
            // Tam dolu küp oluştur
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    for (int z = 0; z < D; z++)
                    {
                        occupiedCells.Add(new Vector3Int(x, y, z));
                    }
                }
            }
            Debug.Log($"✅ Şablon '{selectedTemplate.templateName}' - Tam dolu {W}x{H}x{D} küp: {occupiedCells.Count} blok");
        }
        else
        {
            // Şablondan hücreleri kopyala
            occupiedCells = new HashSet<Vector3Int>(selectedTemplate.occupiedCells);
            Debug.Log($"✅ Şablon '{selectedTemplate.templateName}' yüklendi: {occupiedCells.Count} blok");
        }

        // Boş şekil koruması
        if (occupiedCells.Count == 0)
        {
            occupiedCells.Add(new Vector3Int(W / 2, 0, D / 2));
        }

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
        int prefilledDone = 0;
        foreach (var cell in finalOccupied)
        {
            if (prefilledDone >= targetPrefillCount) break;
            if (frozenCells.Contains(cell)) continue;

            prefilledCells.Add(cell);
            // Renk gruplaması: Yan yana olanlara benzer renkler ver
            int colorIdx = (cell.x + cell.y) % 6; // PREFILL_COLORS boyutu 6
            prefilledMatIdx.Add(colorIdx);
            prefilledDone++;
        }

        // 3. Akıllı Parça Üretimi: Birden fazla strateji dene, en iyisini seç
        SmartPieceSplitting();

        activeLayer = 0;
        Repaint();
        Debug.Log($"🤖 Yapay Zeka: '{levelName}' seviyesi procedurally oluşturuldu. Küp: {occupiedCells.Count}, Parça: {pieceSplitList.Count}");
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
        
        int maxSolvableNeeded = gridVolume < 50 ? 1 : 2; // Küçük grid için 1 çözülebilir yeter
        
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
                case 1: // Sıkı Tetris (Sadece 4)
                    variantMinSize = 4;
                    variantMaxSize = 4;
                    strategyName = "Sıkı Tetris (4 Blok)";
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

            // Bu stratejiyle parçala
            var piecesForThisStrategy = SplitShapeWithStrategy(variantMinSize, variantMaxSize, attempt);
            
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
            // Hiçbiri çalışmadıysa standart yöntemi kullan
            Debug.LogWarning("⚠️ Hiçbir strateji başarılı olmadı, standart yöntem kullanılıyor");
            SplitShapeIntoPieces();
            RunSolverAnalysis();
        }
    }

    private List<List<Vector3Int>> SplitShapeWithStrategy(int minSize, int maxSize, int randomSeed)
    {
        // Random seed'i değiştir ki her denemede farklı sonuç alsın
        Random.InitState(randomSeed + System.DateTime.Now.Millisecond);
        
        List<List<Vector3Int>> pieces = new List<List<Vector3Int>>();
        
        HashSet<Vector3Int> assignable = new HashSet<Vector3Int>(occupiedCells);
        foreach (var pf in prefilledCells)
        {
            assignable.Remove(pf);
        }

        HashSet<Vector3Int> unassigned = new HashSet<Vector3Int>(assignable);

        // BFS ile grupla
        while (unassigned.Count > 0)
        {
            Vector3Int seed = unassigned.First();
            List<Vector3Int> piece = new List<Vector3Int>();
            Queue<Vector3Int> queue = new Queue<Vector3Int>();
            
            queue.Enqueue(seed);
            unassigned.Remove(seed);

            int targetSize = Random.Range(minSize, maxSize + 1);

            while (queue.Count > 0 && piece.Count < targetSize)
            {
                Vector3Int current = queue.Dequeue();
                piece.Add(current);

                // Parçalar yatay olmalı - sadece X-Z düzleminde büyüsün
                List<Vector3Int> horizontalNeighbors = new List<Vector3Int>
                {
                    current + Vector3Int.right,
                    current + Vector3Int.left,
                    new Vector3Int(current.x, current.y, current.z + 1),
                    new Vector3Int(current.x, current.y, current.z - 1)
                };

                foreach (var n in horizontalNeighbors)
                {
                    if (unassigned.Contains(n) && !queue.Contains(n))
                    {
                        if (piece.Count + queue.Count < targetSize)
                        {
                            queue.Enqueue(n);
                            unassigned.Remove(n);
                        }
                    }
                }
            }

            if (piece.Count > 0)
            {
                pieces.Add(piece);
            }
        }

        // Küçük parçaları birleştir
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            if (pieces[i].Count < minSize && pieces.Count > 1)
            {
                var smallPiece = pieces[i];
                int bestTargetPiece = -1;
                float minDist = float.MaxValue;

                for (int j = 0; j < pieces.Count; j++)
                {
                    if (i == j) continue;

                    foreach (var sc in smallPiece)
                    {
                        foreach (var tc in pieces[j])
                        {
                            float d = Vector3.Distance(sc, tc);
                            if (d < minDist)
                            {
                                minDist = d;
                                bestTargetPiece = j;
                            }
                        }
                    }
                }

                if (bestTargetPiece >= 0)
                {
                    pieces[bestTargetPiece].AddRange(smallPiece);
                    pieces.RemoveAt(i);
                }
            }
        }

        return pieces;
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

    private (List<List<Vector3Int>> pieces, SolverResult result, string strategyName) 
        SelectBestStrategy(List<(List<List<Vector3Int>> pieces, SolverResult result, string strategyName)> strategies)
    {
        // En iyi stratejiyi seçme kriterleri:
        // 1. Önce çözülebilir olanları filtrele
        // 2. İdeal parça sayısına yakın olanı seç (3-6 parça ideal)
        // 3. İstenen zorluk seviyesine yakın olanı seç

        var solvable = strategies.Where(s => s.result.isSolvable).ToList();
        
        if (solvable.Count == 0)
        {
            Debug.LogWarning("⚠️ Çözülebilir strateji bulunamadı!");
            return (null, null, "");
        }

        // İdeal parça sayısı: 3-6 arası
        int idealPieceCount = 4;
        
        // En iyi stratejiyi seç (çok faktörlü skor sistemi)
        var best = solvable
            .Select(s => new
            {
                strategy = s,
                // Skor hesaplama:
                // - Parça sayısı skoru (ideal 4, tolerans ±2)
                pieceScore = 100f - Mathf.Abs(s.pieces.Count - idealPieceCount) * 10f,
                // - Zorluk skoru (orta zorluk tercih edilir, 40-60 arası ideal)
                difficultyScore = 100f - Mathf.Abs(s.result.difficultyScore - 50f),
                // - Hamle sayısı skoru (3-10 hamle arası ideal)
                moveScore = (s.result.minMoveCount >= 3 && s.result.minMoveCount <= 10) ? 100f : 50f,
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

    private void SplitShapeIntoPieces()
    {
        pieceSplitList.Clear();
        // Sadece normal (prefilled olmayan) occupied hücreleri parçalara bölüyoruz
        HashSet<Vector3Int> assignable = new HashSet<Vector3Int>(occupiedCells);
        foreach (var pf in prefilledCells)
        {
            assignable.Remove(pf);
        }

        HashSet<Vector3Int> unassigned = new HashSet<Vector3Int>(assignable);

        // BFS ile grupla
        while (unassigned.Count > 0)
        {
            Vector3Int seed = unassigned.First();
            List<Vector3Int> piece = new List<Vector3Int>();
            Queue<Vector3Int> queue = new Queue<Vector3Int>();
            
            queue.Enqueue(seed);
            unassigned.Remove(seed);

            int targetSize = Random.Range(minPieceSize, maxPieceSize + 1);

            while (queue.Count > 0 && piece.Count < targetSize)
            {
                Vector3Int current = queue.Dequeue();
                piece.Add(current);

                // Parçalar tamamen yatay olmalı - sadece X-Z düzleminde büyüsün
                // Önce aynı Y seviyesindeki komşuları ekle
                List<Vector3Int> horizontalNeighbors = new List<Vector3Int>
                {
                    current + Vector3Int.right,
                    current + Vector3Int.left,
                    new Vector3Int(current.x, current.y, current.z + 1),
                    new Vector3Int(current.x, current.y, current.z - 1)
                };

                // Sadece yatay komşuları ekle
                foreach (var n in horizontalNeighbors)
                {
                    if (unassigned.Contains(n) && !queue.Contains(n))
                    {
                        if (piece.Count + queue.Count < targetSize)
                        {
                            queue.Enqueue(n);
                            unassigned.Remove(n);
                        }
                    }
                }
            }

            if (piece.Count > 0)
            {
                pieceSplitList.Add(piece);
            }
        }

        // Kenarda kalan küçük parçaları (min size'dan küçük olanları) en yakın parçayla birleştir
        for (int i = pieceSplitList.Count - 1; i >= 0; i--)
        {
            if (pieceSplitList[i].Count < minPieceSize && pieceSplitList.Count > 1)
            {
                var smallPiece = pieceSplitList[i];
                // En yakın parçayı bul (komşusu olan)
                int bestTargetPiece = -1;
                float minDist = float.MaxValue;

                for (int j = 0; j < pieceSplitList.Count; j++)
                {
                    if (i == j) continue;

                    foreach (var sc in smallPiece)
                    {
                        foreach (var tc in pieceSplitList[j])
                        {
                            float d = Vector3.Distance(sc, tc);
                            if (d < minDist)
                            {
                                minDist = d;
                                bestTargetPiece = j;
                            }
                        }
                    }
                }

                if (bestTargetPiece >= 0)
                {
                    pieceSplitList[bestTargetPiece].AddRange(smallPiece);
                    pieceSplitList.RemoveAt(i);
                }
            }
        }
    }

    private void ExportProceduralLevel()
    {
        if (occupiedCells.Count == 0)
        {
            EditorUtility.DisplayDialog("Hata", "Önce 'YAPAY ZEKA İLE OLUŞTUR' butonuna basarak bir seviye tasarlayın.", "Tamam");
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
    }

    private LevelData ExportProceduralLevelCore(string targetLevelName, float targetLevelTime, int targetLevelTarget)
    {
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

    private void BuildStyles()
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

    private void ApplyDifficultyScale(int level)
    {
        targetLevelIndex = Mathf.Clamp(level, 1, 100);
        levelName = $"AI_Level_{targetLevelIndex}";
        
        float baseTime = 75f;
        float baseTarget = 150f;
        
        if (targetLevelIndex <= 10)
        {
            levelDifficultyModeSuggestion = "Kolay";
            baseTime = 90f - (targetLevelIndex - 1) * 2f; 
            baseTarget = 80 + (targetLevelIndex - 1) * 10;
            prefillPercentage = 0f;
            icePercentage = 0f;
            minPieceSize = 1;
            maxPieceSize = Mathf.Clamp(3 + (targetLevelIndex / 5), 3, 4);
        }
        else if (targetLevelIndex <= 30)
        {
            levelDifficultyModeSuggestion = "Orta";
            baseTime = 75f - (targetLevelIndex - 10) * 1f; 
            baseTarget = 180 + (targetLevelIndex - 10) * 12;
            prefillPercentage = Mathf.Lerp(0.05f, 0.15f, (targetLevelIndex - 10) / 20f);
            icePercentage = Mathf.Lerp(0.08f, 0.15f, (targetLevelIndex - 10) / 20f);
            minPieceSize = Mathf.Clamp(1 + (targetLevelIndex / 15), 1, 2);
            maxPieceSize = Mathf.Clamp(4 + (targetLevelIndex / 15), 4, 5);
        }
        else if (targetLevelIndex <= 60)
        {
            levelDifficultyModeSuggestion = "Zor";
            baseTime = 60f - (targetLevelIndex - 30) * 0.5f; 
            baseTarget = 420 + (targetLevelIndex - 30) * 15;
            prefillPercentage = Mathf.Lerp(0.15f, 0.25f, (targetLevelIndex - 30) / 30f);
            icePercentage = Mathf.Lerp(0.15f, 0.28f, (targetLevelIndex - 30) / 30f);
            minPieceSize = 2;
            maxPieceSize = 5;
        }
        else
        {
            levelDifficultyModeSuggestion = "Uzman";
            baseTime = Mathf.Max(30f, 45f - (targetLevelIndex - 60) * 0.25f);
            baseTarget = 870 + (targetLevelIndex - 60) * 20;
            prefillPercentage = Mathf.Lerp(0.25f, 0.40f, (targetLevelIndex - 60) / 40f);
            icePercentage = Mathf.Lerp(0.28f, 0.40f, (targetLevelIndex - 60) / 40f);
            minPieceSize = Mathf.Clamp(2 + (targetLevelIndex / 40), 2, 4);
            maxPieceSize = Mathf.Clamp(5 + (targetLevelIndex / 30), 5, 8);
        }

        if (selectedTemplate != null)
        {
            levelTime = Mathf.Round(selectedTemplate.recommendedTimeLimit * (baseTime / 75f));
            levelTarget = Mathf.RoundToInt(selectedTemplate.recommendedTargetScore * (baseTarget / 150f));
        }
        else
        {
            levelTime = Mathf.Round(baseTime);
            levelTarget = Mathf.RoundToInt(baseTarget);
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
        GeneratorType origGenType = genType;
        SymmetryMode origSymmetry = symmetry;
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
                    genType = GeneratorType.Pyramid;            // Şekil önemsiz (tek katman)
                    symmetry = SymmetryMode.None;
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
                    genType = GeneratorType.Pyramid;            // Piramit: yukarı daralan yapı
                    symmetry = SymmetryMode.None;
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
                    genType = GeneratorType.Pyramid;
                    symmetry = SymmetryMode.None;
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
                    genType = GeneratorType.Pyramid;
                    symmetry = SymmetryMode.None;
                    fillDensity = 1.0f;
                    prefillPercentage = 0.0f;
                    icePercentage = 0.33f;                      // %33 frozen
                    minPieceSize = 3;
                    maxPieceSize = 3;                           // ~6 parça hedefi
                    levelTime = 0f;
                    levelTarget = 120;
                    break;

                case 5: // Küçük Piramit — 3D şekil
                    gridSize = new Vector3Int(5, 3, 5);         // 3 katman
                    genType = GeneratorType.Pyramid;            // Piramit şekli
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 1.0f;                         // Tam piramit
                    prefillPercentage = 0.0f;
                    icePercentage = 0.0f;
                    minPieceSize = 4;
                    maxPieceSize = 6;                           // ~8 parça hedefi
                    levelTime = 0f;
                    levelTarget = 150;
                    break;

                case 6: // Karışık Renk Katmanı — Çoklu renk
                    gridSize = new Vector3Int(4, 2, 4);         // 2 katman
                    genType = GeneratorType.Pyramid;
                    symmetry = SymmetryMode.None;
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
                    genType = GeneratorType.Pyramid;
                    symmetry = SymmetryMode.None;
                    fillDensity = 1.0f;
                    prefillPercentage = 0.1f;                   // ~6 hücre
                    icePercentage = 0.25f;                      // %25 frozen
                    minPieceSize = 4;
                    maxPieceSize = 6;                           // ~12 parça hedefi
                    levelTime = 0f;
                    levelTarget = 220;
                    break;

                case 8: // Kale Yapısı — Karmaşık şekil
                    gridSize = new Vector3Int(6, 3, 6);         // 3 katman
                    genType = GeneratorType.Castle;             // Kale şekli
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 1.0f;
                    prefillPercentage = 0.08f;                  // Köşelerde
                    icePercentage = 0.20f;                      // %20 frozen
                    minPieceSize = 4;
                    maxPieceSize = 7;                           // ~14 parça hedefi
                    levelTime = 0f;
                    levelTarget = 260;
                    break;

                case 9: // Spiral Zorluk — Yüksek katman
                    gridSize = new Vector3Int(5, 5, 5);         // 5 katman
                    genType = GeneratorType.HelixSpiral;        // Spiral şekli
                    symmetry = SymmetryMode.None;
                    fillDensity = 1.0f;
                    prefillPercentage = 0.15f;                  // %15
                    icePercentage = 0.20f;                      // %20 frozen
                    minPieceSize = 5;
                    maxPieceSize = 8;                           // ~18 parça hedefi
                    levelTime = 180f;                           // İlk süre limiti
                    levelTarget = 300;
                    break;

                case 10: // Usta Seviyesi — Tüm mekanikler
                default:
                    gridSize = new Vector3Int(6, 4, 6);         // 4 katman
                    genType = GeneratorType.SphereDome;         // Küre şekli
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 1.0f;
                    prefillPercentage = 0.20f;                  // %20
                    icePercentage = 0.25f;                      // %25 frozen
                    minPieceSize = 6;
                    maxPieceSize = 10;                          // ~22 parça hedefi
                    levelTime = 240f;                           // Uzun süre limiti
                    levelTarget = 350;
                    break;
            }

            // Seviyeyi algoritmik olarak oluştur
            GenerateLevelProcedurally();

            // Kaydet
            LevelData levelAsset = ExportProceduralLevelCore(levelName, levelTime, levelTarget);
            if (levelAsset != null)
            {
                levelOrder.levels.Add(levelAsset);
            }

            // Eğitim verisi modelini doldur
            AIDatasetEntry entry = new AIDatasetEntry();
            entry.levelName = levelName;
            entry.difficultyIndex = i;
            entry.shapeType = genType.ToString();
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
        genType = origGenType;
        symmetry = origSymmetry;
        fillDensity = origFillDensity;
        prefillPercentage = origPrefillPercentage;
        icePercentage = origIcePercentage;
        minPieceSize = origMinPieceSize;
        maxPieceSize = origMaxPieceSize;

        // Seviye 1'i önizlemede göstermek için son kez oluştur
        GenerateLevelProcedurally();

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("🎮 İlk 10 Level Başarıyla Oluşturuldu!",
            $"✅ Kademeli Öğretim Sistemi Devrede:\n\n" +
            $"📦 10 Level Oluşturuldu (Assets/Levels/)\n" +
            $"📋 Level Sırası Güncellendi (LevelOrder.asset)\n" +
            $"🎯 Oyun Level 1'den Başlıyor\n" +
            $"📊 Eğitim Dataseti Kaydedildi:\n\t{jsonPath}\n\n" +
            $"Level 1-4:  Temel mekanikler (sürükle, katman, renk, buz)\n" +
            $"Level 5-8:  Karmaşık şekiller (piramit, kale, spiral)\n" +
            $"Level 9-10: Usta seviyesi (çoklu mekanik + süre limiti)\n\n" +
            $"Oyunu test edebilir veya bu dataseti AI modeline öğretebilirsiniz!", "Harika! 🚀");
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
