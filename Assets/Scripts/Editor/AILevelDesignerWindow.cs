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
    private string levelName         = "AI_Level_1";
    private float levelTime          = 75f;
    private int levelTarget          = 150;
    private Vector3Int gridSize      = new Vector3Int(5, 5, 5);
    private float cellSize           = 1.0f;
    private float spacing            = 0.1f;

    private GeneratorType genType    = GeneratorType.Pyramid;
    private SymmetryMode symmetry    = SymmetryMode.XZ_Axis;
    private float fillDensity        = 0.75f;
    private float icePercentage      = 0.15f;
    private float prefillPercentage  = 0.10f;
    
    // Parçalara Ayırma Ayarları
    private int minPieceSize         = 3;
    private int maxPieceSize         = 5;

    // Prompt tabanlı üretim
    private string aiPrompt          = "star with ice at base and golden corners";

    // ── Grid Verisi ────────────────────────────────────────────────
    private HashSet<Vector3Int> occupiedCells   = new HashSet<Vector3Int>();
    private List<Vector3Int> prefilledCells     = new List<Vector3Int>();
    private List<int> prefilledMatIdx           = new List<int>();
    private List<Vector3Int> frozenCells        = new List<Vector3Int>();
    private List<List<Vector3Int>> pieceSplitList = new List<List<Vector3Int>>();

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
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(300), GUILayout.ExpandHeight(true));
        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        GUILayout.Label("GENEL AYARLAR", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        levelName   = EditorGUILayout.TextField("Seviye Adı Öneki", levelName);
        levelTime   = EditorGUILayout.FloatField("Süre Sınırı (sn)", levelTime);
        levelTarget = EditorGUILayout.IntField("Hedef Skor", levelTarget);
        gridSize    = EditorGUILayout.Vector3IntField("Grid Boyutu", gridSize);
        gridSize    = new Vector3Int(Mathf.Clamp(gridSize.x, 2, 8), Mathf.Clamp(gridSize.y, 2, 8), Mathf.Clamp(gridSize.z, 2, 8));
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("YAPAY ZEKA GENERATOR PARAMETRELERİ", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        genType = (GeneratorType)EditorGUILayout.EnumPopup("Şekil Tipi", genType);
        symmetry = (SymmetryMode)EditorGUILayout.EnumPopup("Simetri Modu", symmetry);
        
        if (genType == GeneratorType.PromptBased)
        {
            EditorGUILayout.LabelField("Yapay Zeka İsteminiz (Prompt):", EditorStyles.miniBoldLabel);
            aiPrompt = EditorGUILayout.TextArea(aiPrompt, GUILayout.Height(40));
            EditorGUILayout.HelpBox("Anahtar Kelimeler: pyramid, castle, spiral, sphere, cross, star, ice, gold, corner, hollow", MessageType.None);
        }

        fillDensity = EditorGUILayout.Slider("Doluluk Yoğunluğu", fillDensity, 0.2f, 1.0f);
        prefillPercentage = EditorGUILayout.Slider("Hazır Küp Oranı", prefillPercentage, 0f, 0.4f);
        icePercentage = EditorGUILayout.Slider("Buz Küpü Oranı", icePercentage, 0f, 0.4f);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("PARÇA BÖLME ALGORİTMASI", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        minPieceSize = EditorGUILayout.IntSlider("Min Parça Küpü", minPieceSize, 2, 6);
        maxPieceSize = EditorGUILayout.IntSlider("Max Parça Küpü", maxPieceSize, minPieceSize, 12);
        EditorGUILayout.HelpBox("AI, şekli otomatik olarak birbirine bağlı (contiguous) bu boyutlarda parçalara bölecektir.", MessageType.Info);
        EditorGUILayout.EndVertical();

        GUILayout.Space(12);

        cubePrefab = (GameObject)EditorGUILayout.ObjectField("Global Küp Prefabı", cubePrefab, typeof(GameObject), false);
        if (cubePrefab == null)
        {
            EditorGUILayout.HelpBox("⚠ Lütfen bir küp prefabı seçin, aksi halde varsayılan Unity küpü üretilecektir.", MessageType.Warning);
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
        EditorGUILayout.BeginVertical(styleBox, GUILayout.Width(250), GUILayout.ExpandHeight(true));
        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        GUILayout.Label("DIŞA AKTAR VE DENE", styleHeader);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("İstatistikler:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Toplam Küp Sayısı: {occupiedCells.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Hazır (Renkli) Küpler: {prefilledCells.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Dondurulmuş Küpler: {frozenCells.Count}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Otomatik Parça Sayısı: {pieceSplitList.Count}", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

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
        occupiedCells.Clear();
        prefilledCells.Clear();
        prefilledMatIdx.Clear();
        frozenCells.Clear();
        pieceSplitList.Clear();

        // 1. Ana Şekli Üret
        int W = gridSize.x;
        int H = gridSize.y;
        int D = gridSize.z;

        if (genType == GeneratorType.PromptBased)
        {
            GeneratePromptBasedShape(W, H, D);
        }
        else
        {
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    for (int z = 0; z < D; z++)
                    {
                        var cell = new Vector3Int(x, y, z);
                        if (EvaluateShapeFormula(cell, W, H, D))
                        {
                            occupiedCells.Add(cell);
                        }
                    }
                }
            }
        }

        // Simetri Uygula
        ApplySymmetry(W, H, D);

        // Rastgele Doluluk Yoğunluğu Elemesi
        List<Vector3Int> cellList = occupiedCells.ToList();
        foreach (var c in cellList)
        {
            if (Random.value > fillDensity)
            {
                occupiedCells.Remove(c);
            }
        }

        // Boş şekil koruması
        if (occupiedCells.Count == 0)
        {
            // Ortada en azından bir blok bırak
            occupiedCells.Add(new Vector3Int(W / 2, 0, D / 2));
        }

        // 2. Renkli Küpleri (Prefilled) ve Buzları Dağıt
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

        // 3. Parçalara Otomatik Ayır (BFS Auto-Splitter)
        SplitShapeIntoPieces();

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

                // 6 yönlü 3D komşular
                Vector3Int[] neighbors = new Vector3Int[]
                {
                    current + Vector3Int.right,
                    current + Vector3Int.left,
                    current + Vector3Int.up,
                    current + Vector3Int.down,
                    new Vector3Int(current.x, current.y, current.z + 1),
                    new Vector3Int(current.x, current.y, current.z - 1)
                };

                foreach (var n in neighbors)
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

        // 3. 10 adet farklı parametrelerle seviye oluştur
        for (int i = 1; i <= 10; i++)
        {
            // İlerleme çubuğu
            EditorUtility.DisplayProgressBar("Yapay Zeka Eğitim Seti", $"Level {i}/10 üretiliyor...", i / 10f);

            levelName = $"AI_Level_{i}";
            
            // Parametreleri zorluk derecesine göre ata
            switch (i)
            {
                case 1:
                    gridSize = new Vector3Int(3, 3, 3);
                    genType = GeneratorType.Pyramid;
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 1.0f;
                    prefillPercentage = 0.0f;
                    icePercentage = 0.0f;
                    minPieceSize = 3;
                    maxPieceSize = 3;
                    levelTime = 60f;
                    levelTarget = 50;
                    break;
                case 2:
                    gridSize = new Vector3Int(3, 3, 3);
                    genType = GeneratorType.CrossStar;
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 0.8f;
                    prefillPercentage = 0.1f;
                    icePercentage = 0.0f;
                    minPieceSize = 3;
                    maxPieceSize = 4;
                    levelTime = 70f;
                    levelTarget = 80;
                    break;
                case 3:
                    gridSize = new Vector3Int(4, 4, 4);
                    genType = GeneratorType.Castle;
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 0.75f;
                    prefillPercentage = 0.1f;
                    icePercentage = 0.05f;
                    minPieceSize = 3;
                    maxPieceSize = 4;
                    levelTime = 80f;
                    levelTarget = 120;
                    break;
                case 4:
                    gridSize = new Vector3Int(4, 4, 4);
                    genType = GeneratorType.HelixSpiral;
                    symmetry = SymmetryMode.None;
                    fillDensity = 0.85f;
                    prefillPercentage = 0.1f;
                    icePercentage = 0.1f;
                    minPieceSize = 3;
                    maxPieceSize = 4;
                    levelTime = 90f;
                    levelTarget = 140;
                    break;
                case 5:
                    gridSize = new Vector3Int(5, 5, 5);
                    genType = GeneratorType.SphereDome;
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 0.7f;
                    prefillPercentage = 0.12f;
                    icePercentage = 0.12f;
                    minPieceSize = 4;
                    maxPieceSize = 5;
                    levelTime = 100f;
                    levelTarget = 180;
                    break;
                case 6:
                    gridSize = new Vector3Int(5, 5, 5);
                    genType = GeneratorType.Pyramid;
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 0.8f;
                    prefillPercentage = 0.1f;
                    icePercentage = 0.15f;
                    minPieceSize = 4;
                    maxPieceSize = 5;
                    levelTime = 110f;
                    levelTarget = 200;
                    break;
                case 7:
                    gridSize = new Vector3Int(5, 5, 5);
                    genType = GeneratorType.Castle;
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 0.75f;
                    prefillPercentage = 0.15f;
                    icePercentage = 0.15f;
                    minPieceSize = 4;
                    maxPieceSize = 6;
                    levelTime = 120f;
                    levelTarget = 220;
                    break;
                case 8:
                    gridSize = new Vector3Int(6, 6, 6);
                    genType = GeneratorType.CrossStar;
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 0.7f;
                    prefillPercentage = 0.15f;
                    icePercentage = 0.2f;
                    minPieceSize = 4;
                    maxPieceSize = 6;
                    levelTime = 140f;
                    levelTarget = 260;
                    break;
                case 9:
                    gridSize = new Vector3Int(6, 6, 6);
                    genType = GeneratorType.HelixSpiral;
                    symmetry = SymmetryMode.None;
                    fillDensity = 0.8f;
                    prefillPercentage = 0.2f;
                    icePercentage = 0.2f;
                    minPieceSize = 5;
                    maxPieceSize = 6;
                    levelTime = 160f;
                    levelTarget = 300;
                    break;
                case 10:
                default:
                    gridSize = new Vector3Int(6, 6, 6);
                    genType = GeneratorType.SphereDome;
                    symmetry = SymmetryMode.XZ_Axis;
                    fillDensity = 0.75f;
                    prefillPercentage = 0.25f;
                    icePercentage = 0.25f;
                    minPieceSize = 5;
                    maxPieceSize = 7;
                    levelTime = 180f;
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

        EditorUtility.DisplayDialog("Yapay Zeka Toplu Üretim Başarılı!",
            $"🤖 Yapay Zeka Eğitim Modu Başarıyla Çalıştırıldı:\n\n" +
            $"✅  10 Adet Yeni Seviye Üretildi (Assets/Levels/)\n" +
            $"✅  Sıralayıcı Güncellendi (LevelOrder.asset)\n" +
            $"✅  Oyun İlerlemesi 1. Seviyeye Sıfırlandı\n" +
            $"✅  Eğitim Verisi JSON Dosyası Yazıldı:\n\t{jsonPath}\n\n" +
            $"Artık oyunu hemen test edebilir ve yapay zekaya bu dataseti öğretebilirsiniz!", "Harika!");
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
}

[System.Serializable]
public class AIDatasetWrapper
{
    public List<AIDatasetEntry> dataset = new List<AIDatasetEntry>();
}
