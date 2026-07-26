using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ═══════════════════════════════════════════════════════════════════
//  LEVEL TEMPLATE MANAGER  —  Level Şablonu Tasarımcısı & Düzenleyicisi
//  BlockMerge3D  •  Level şablonlarını katman katman 2D/3D ızgara
//  üzerinde tasarlamanızı, kaydetmenizi, düzenlemenizi ve silmenizi sağlar.
// ═══════════════════════════════════════════════════════════════════

public class LevelTemplateManagerWindow : EditorWindow
{
    private const string TEMPLATES_FOLDER = "Assets/Templates";

    private static readonly Color COL_HEADER = new Color(0.35f, 0.78f, 1.00f);
    private static readonly Color COL_WARN   = new Color(0.95f, 0.70f, 0.20f);

    private Vector3Int templateGridSize = new Vector3Int(4, 3, 4);
    private int activeYLayer = 0;
    private HashSet<Vector3Int> templateCells = new HashSet<Vector3Int>();

    private LevelTemplate editingTemplate;

    private string templateName = "Özel_Kule_Şablonu";
    private string templateDesc = "Özel tasarlanmış seviye şablonu";
    private float recTime = 60f;
    private int recTarget = 150;
    private int recMinSize = 3;
    private int recMaxSize = 10;

    private List<LevelTemplate> allTemplates = new List<LevelTemplate>();
    private Vector2 scrollPos;
    private Vector2 listScroll;

    private GUIStyle styleHeader, styleBox;
    private bool stylesBuilt;

    public System.Action onRepaintRequested;

    new public void Repaint()
    {
        base.Repaint();
        onRepaintRequested?.Invoke();
    }

    [MenuItem("BlockMerge3D/🏰 Level Şablonu Tasarımcısı")]
    public static void ShowWindow()
    {
        var w = GetWindow<LevelTemplateManagerWindow>("Şablon Tasarımcısı");
        w.minSize = new Vector2(720, 560);
    }

    private void OnEnable()
    {
        RefreshTemplateList();
        if (templateCells.Count == 0)
        {
            GenerateDefaultCuboid();
        }
    }

    private void BuildStyles()
    {
        if (stylesBuilt) return;
        styleHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        styleHeader.normal.textColor = COL_HEADER;
        styleBox = new GUIStyle(GUI.skin.box);
        stylesBuilt = true;
    }

    public void OnGUI()
    {
        BuildStyles();

        EditorGUILayout.Space(6);
        GUILayout.Label("🏰 LEVEL ŞABLONU TASARIMCISI — OLUŞTUR & DÜZENLE", styleHeader);
        EditorGUILayout.HelpBox(
            "Seviye şablonlarını (Level Template) katman katman çizebilir, kaydedebilir, düzenleyebilir ve silinebilirsiniz. " +
            "Oluşturduğunuz şablonlar doğrudan Seviye Oluşturma Sihirbazı ve AI Level Designer tarafından yüklenir.",
            MessageType.Info);

        EditorGUILayout.Space(4);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        DrawTemplateDesignerCard();

        EditorGUILayout.Space(12);
        DrawTemplateListCard();

        EditorGUILayout.EndScrollView();
    }

    // ── Şablon Çizim ve Tasarım Paneli ───────────────────────────────
    private void DrawTemplateDesignerCard()
    {
        EditorGUILayout.BeginVertical(styleBox);

        string title = editingTemplate != null 
            ? $"✏️ DÜZENLENEN ŞABLON: {editingTemplate.templateName}" 
            : "🎨 YENİ ŞABLON ÇİZ VE TASARLA";
        
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(title, styleHeader);
        if (editingTemplate != null)
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("İptal / Yeniye Dön", GUILayout.Width(130)))
            {
                editingTemplate = null;
                templateName = "Özel_Şablon_1";
                GenerateDefaultCuboid();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // 🟢 Grid Boyutları ve Katman Seçici
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(GUILayout.Width(220));
        EditorGUILayout.LabelField("📐 Grid Boyutu (X, Y, Z)", EditorStyles.boldLabel);
        Vector3Int newSize = EditorGUILayout.Vector3IntField("", templateGridSize);
        if (newSize.x < 1) newSize.x = 1; if (newSize.x > 8) newSize.x = 8;
        if (newSize.y < 1) newSize.y = 1; if (newSize.y > 8) newSize.y = 8;
        if (newSize.z < 1) newSize.z = 1; if (newSize.z > 8) newSize.z = 8;

        if (newSize != templateGridSize)
        {
            templateGridSize = newSize;
            activeYLayer = Mathf.Clamp(activeYLayer, 0, templateGridSize.y - 1);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("📍 Katman Seçici (Y Ekseninde):", EditorStyles.boldLabel);
        for (int y = templateGridSize.y - 1; y >= 0; y--)
        {
            bool isActive = (y == activeYLayer);
            Color prevBG = GUI.backgroundColor;
            GUI.backgroundColor = isActive ? COL_HEADER : new Color(0.3f, 0.3f, 0.35f);

            int countInLayer = templateCells.Count(c => c.y == y);
            if (GUILayout.Button($"KATMAN Y={y} ({countInLayer} Blok)", GUILayout.Height(24)))
            {
                activeYLayer = y;
            }
            GUI.backgroundColor = prevBG;
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"Toplam Küp Sayısı: {templateCells.Count}", EditorStyles.boldLabel);
        EditorGUILayout.EndVertical();

        GUILayout.Space(12);

        // 🟦 Active Layer Grid Drawer (2D Layer Editor)
        EditorGUILayout.BeginVertical(GUILayout.Width(280));
        EditorGUILayout.LabelField($"✏️ KATMAN Y={activeYLayer} Şekil Çizimi (Karelere Tıklayın):", EditorStyles.boldLabel);

        for (int z = 0; z < templateGridSize.z; z++)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < templateGridSize.x; x++)
            {
                var cell = new Vector3Int(x, activeYLayer, z);
                bool active = templateCells.Contains(cell);

                Color prevBG = GUI.backgroundColor;
                GUI.backgroundColor = active ? new Color(0.35f, 0.78f, 1.00f) : new Color(0.20f, 0.20f, 0.23f);

                if (GUILayout.Button(active ? "█" : "·", GUILayout.Width(32), GUILayout.Height(32)))
                {
                    if (active) templateCells.Remove(cell);
                    else templateCells.Add(cell);
                }
                GUI.backgroundColor = prevBG;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(6);
        // Presets
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Temizle", GUILayout.Width(65))) templateCells.Clear();
        if (GUILayout.Button("Tam Küp", GUILayout.Width(65))) GenerateDefaultCuboid();
        if (GUILayout.Button("Piramit", GUILayout.Width(65))) GeneratePyramid();
        if (GUILayout.Button("Kule", GUILayout.Width(65))) GenerateTower();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        GUILayout.Space(12);

        // 📝 Şablon Meta-Veri Formu
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("⚙️ Şablon Ayarları & Parametreleri:", EditorStyles.boldLabel);

        templateName = EditorGUILayout.TextField("Şablon Adı", templateName);
        templateDesc = EditorGUILayout.TextField("Açıklama", templateDesc);
        recTime      = EditorGUILayout.FloatField("Önerilen Süre (sn)", recTime);
        recTarget    = EditorGUILayout.IntField("Hedef Skor", recTarget);
        recMinSize   = EditorGUILayout.IntSlider("Min Parça Hacmi", recMinSize, 2, 10);
        recMaxSize   = EditorGUILayout.IntSlider("Max Parça Hacmi", recMaxSize, recMinSize, 20);

        EditorGUILayout.Space(10);
        GUI.backgroundColor = editingTemplate != null ? new Color(0.25f, 0.65f, 0.95f) : new Color(0.20f, 0.75f, 0.40f);
        string btnLabel = editingTemplate != null ? "💾  Şablon Değişikliklerini Kaydet" : "➕  Yeni Şablon Olarak Kaydet ve Aktif Et";
        if (GUILayout.Button(btnLabel, GUILayout.Height(38)))
        {
            SaveCurrentTemplate();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    // ── Şablon Listesi Paneli (Silme ve Düzenleme) ──────────────────────
    private void DrawTemplateListCard()
    {
        EditorGUILayout.BeginVertical(styleBox);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"📚 KAYITLI LEVEL ŞABLONLARI ({allTemplates.Count})", styleHeader);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Listeyi Yenile", GUILayout.Width(120)))
        {
            RefreshTemplateList();
        }
        EditorGUILayout.EndHorizontal();

        listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.MinHeight(180), GUILayout.MaxHeight(300));
        foreach (var t in allTemplates)
        {
            if (t == null) continue;
            DrawTemplateRow(t);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    private void DrawTemplateRow(LevelTemplate t)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField($"🏰 {t.templateName}", EditorStyles.boldLabel);
        int totalCubes = t.occupiedCells != null && t.occupiedCells.Count > 0 ? t.occupiedCells.Count : (t.gridSize.x * t.gridSize.y * t.gridSize.z);
        EditorGUILayout.LabelField($"Boyut: {t.gridSize.x}x{t.gridSize.y}x{t.gridSize.z} | Toplam Küp: {totalCubes} Blok | Süre: {t.recommendedTimeLimit}s", EditorStyles.miniLabel);
        if (!string.IsNullOrEmpty(t.description))
            EditorGUILayout.LabelField(t.description, EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        GUI.backgroundColor = new Color(0.25f, 0.65f, 0.95f);
        if (GUILayout.Button("✏ Düzenle", GUILayout.Width(80), GUILayout.Height(26)))
        {
            LoadTemplateForEditing(t);
        }

        GUI.backgroundColor = new Color(0.90f, 0.30f, 0.30f);
        if (GUILayout.Button("🗑 Sil", GUILayout.Width(60), GUILayout.Height(26)))
        {
            DeleteTemplate(t);
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    // ── Şablon Yükleme, Kaydetme ve Silme Mantığı ────────────────────
    private void GenerateDefaultCuboid()
    {
        templateCells.Clear();
        for (int y = 0; y < templateGridSize.y; y++)
        {
            for (int z = 0; z < templateGridSize.z; z++)
            {
                for (int x = 0; x < templateGridSize.x; x++)
                {
                    templateCells.Add(new Vector3Int(x, y, z));
                }
            }
        }
    }

    private void GeneratePyramid()
    {
        templateCells.Clear();
        for (int y = 0; y < templateGridSize.y; y++)
        {
            int inset = y;
            for (int x = inset; x < templateGridSize.x - inset; x++)
            {
                for (int z = inset; z < templateGridSize.z - inset; z++)
                {
                    templateCells.Add(new Vector3Int(x, y, z));
                }
            }
        }
    }

    private void GenerateTower()
    {
        templateCells.Clear();
        for (int y = 0; y < templateGridSize.y; y++)
        {
            for (int x = 1; x < templateGridSize.x - 1; x++)
            {
                for (int z = 1; z < templateGridSize.z - 1; z++)
                {
                    templateCells.Add(new Vector3Int(x, y, z));
                }
            }
        }
    }

    private void LoadTemplateForEditing(LevelTemplate t)
    {
        if (t == null) return;
        editingTemplate = t;
        templateName = t.templateName;
        templateDesc = t.description;
        templateGridSize = t.gridSize;
        recTime = t.recommendedTimeLimit;
        recTarget = t.recommendedTargetScore;
        recMinSize = t.recommendedMinPieceSize;
        recMaxSize = t.recommendedMaxPieceSize;

        templateCells = new HashSet<Vector3Int>();
        if (t.occupiedCells != null && t.occupiedCells.Count > 0)
        {
            foreach (var c in t.occupiedCells) templateCells.Add(c);
        }
        else
        {
            GenerateDefaultCuboid();
        }

        activeYLayer = 0;
        GUI.FocusControl(null);
    }

    private void SaveCurrentTemplate()
    {
        if (templateCells.Count == 0)
        {
            EditorUtility.DisplayDialog("Uyarı", "Şablon en az 1 dolu blok içermelidir.", "Tamam");
            return;
        }

        if (!AssetDatabase.IsValidFolder(TEMPLATES_FOLDER))
        {
            AssetDatabase.CreateFolder("Assets", "Templates");
        }

        if (editingTemplate != null)
        {
            editingTemplate.templateName = string.IsNullOrEmpty(templateName) ? "Özel_Şablon" : templateName;
            editingTemplate.description = templateDesc;
            editingTemplate.gridSize = templateGridSize;
            editingTemplate.occupiedCells = templateCells.ToList();
            editingTemplate.recommendedTimeLimit = recTime;
            editingTemplate.recommendedTargetScore = recTarget;
            editingTemplate.recommendedMinPieceSize = recMinSize;
            editingTemplate.recommendedMaxPieceSize = recMaxSize;

            EditorUtility.SetDirty(editingTemplate);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string name = editingTemplate.templateName;
            editingTemplate = null;
            RefreshTemplateList();

            EditorUtility.DisplayDialog("Başarılı", $"✅ '{name}' şablonu başarıyla güncellendi!", "Harika");
        }
        else
        {
            string cleanName = string.IsNullOrEmpty(templateName) ? "Ozel_Sablon" : templateName.Replace(" ", "_");
            string path = $"{TEMPLATES_FOLDER}/{cleanName}.asset";
            if (File.Exists(path))
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
            }

            var t = ScriptableObject.CreateInstance<LevelTemplate>();
            t.templateName = string.IsNullOrEmpty(templateName) ? "Özel Şablon" : templateName;
            t.description = templateDesc;
            t.gridSize = templateGridSize;
            t.occupiedCells = templateCells.ToList();
            t.recommendedTimeLimit = recTime;
            t.recommendedTargetScore = recTarget;
            t.recommendedMinPieceSize = recMinSize;
            t.recommendedMaxPieceSize = recMaxSize;

            AssetDatabase.CreateAsset(t, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RefreshTemplateList();
            EditorUtility.DisplayDialog("Başarılı", $"✅ '{t.templateName}' şablonu kütüphaneye kaydedildi!\n\nKonum: {path}", "Harika");
        }
    }

    private void DeleteTemplate(LevelTemplate t)
    {
        if (t == null) return;
        string path = AssetDatabase.GetAssetPath(t);
        bool confirmed = EditorUtility.DisplayDialog(
            "Şablonu Sil",
            $"'{t.templateName}' şablonu diskten ve projeden silinecek.\n\nDosya: {path}\n\nEmin misiniz?",
            "Evet, Sil", "İptal");

        if (confirmed)
        {
            if (editingTemplate == t) editingTemplate = null;
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshTemplateList();
        }
    }

    private void RefreshTemplateList()
    {
        allTemplates.Clear();
        if (!AssetDatabase.IsValidFolder(TEMPLATES_FOLDER)) return;

        var guids = AssetDatabase.FindAssets("t:LevelTemplate", new[] { TEMPLATES_FOLDER });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var t = AssetDatabase.LoadAssetAtPath<LevelTemplate>(path);
            if (t != null) allTemplates.Add(t);
        }
        allTemplates = allTemplates.OrderBy(t => t.templateName).ToList();
    }
}
