using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

public class LevelOrderEditorWindow : EditorWindow
{
    private LevelOrderData levelOrder;
    private ReorderableList reorderableList;
    private Vector2 scroll;

    private GUIStyle headerStyle;
    private GUIStyle indexStyle;

    private const string ASSET_PATH = "Assets/LevelOrder.asset";
    private const string PREF_LEVEL = "CurrentLevelIndex";

    // [MenuItem("BlockMerge3D/3. Seviye Sırası")]
    public static void ShowWindow()
    {
        var w = GetWindow<LevelOrderEditorWindow>("Seviye Sıralayıcı");
        w.minSize = new Vector2(420, 520);
    }

    private void OnEnable()
    {
        LoadOrCreate();
    }

    private void LoadOrCreate()
    {
        levelOrder = AssetDatabase.LoadAssetAtPath<LevelOrderData>(ASSET_PATH);
        if (levelOrder == null)
        {
            levelOrder = CreateInstance<LevelOrderData>();
            AssetDatabase.CreateAsset(levelOrder, ASSET_PATH);
            AssetDatabase.SaveAssets();
        }
        BuildList();
    }

    private void BuildList()
    {
        reorderableList = new ReorderableList(levelOrder.levels, typeof(LevelData),
            draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);

        reorderableList.drawHeaderCallback = rect =>
        {
            GUI.Label(rect, $"Level Sırası  —  {levelOrder.levels.Count} level",
                new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.4f, 0.85f, 1f) } });
        };

        reorderableList.elementHeight = EditorGUIUtility.singleLineHeight * 2 + 10;

        reorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            rect.y += 3;
            float singleH = EditorGUIUtility.singleLineHeight;

            int savedIdx  = PlayerPrefs.GetInt(PREF_LEVEL, 0);
            bool isCurrent = index == savedIdx;

            if (isCurrent)
                EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 3, rect.width + 4, reorderableList.elementHeight - 4),
                    new Color(0.25f, 0.6f, 0.25f, 0.25f));

            float btnW = isCurrent ? 24f : 52f;
            float numW = 28f;

            // Seç / ▶ butonu
            var btnStyle = new GUIStyle(EditorStyles.miniButton);
            if (isCurrent)
            {
                btnStyle.normal.textColor = Color.green;
                GUI.Button(new Rect(rect.x, rect.y, btnW, singleH), "▶", btnStyle);
            }
            else
            {
                if (GUI.Button(new Rect(rect.x, rect.y, btnW, singleH), "Seç", btnStyle))
                {
                    PlayerPrefs.SetInt(PREF_LEVEL, index);
                    PlayerPrefs.Save();
                    Repaint();
                }
            }

            GUI.Label(new Rect(rect.x + btnW + 4, rect.y, numW, singleH),
                $"{index + 1}.", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            var picked = (LevelData)EditorGUI.ObjectField(
                new Rect(rect.x + btnW + 4 + numW, rect.y, rect.width - btnW - 4 - numW, singleH),
                levelOrder.levels[index], typeof(LevelData), false);
            if (EditorGUI.EndChangeCheck())
            {
                levelOrder.levels[index] = picked;
                MarkDirty();
            }

            if (picked != null)
            {
                float secondLineY = rect.y + singleH + 4;
                float labelW = 55f;
                float valW = 50f;

                float currentX = rect.x + btnW + 4 + numW;

                // Süre Sınırı (Time Limit)
                GUI.Label(new Rect(currentX, secondLineY, labelW, singleH), "Süre (sn):", EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                float newTime = EditorGUI.FloatField(new Rect(currentX + labelW, secondLineY, valW, singleH), picked.timeLimit);
                if (EditorGUI.EndChangeCheck())
                {
                    picked.timeLimit = newTime;
                    EditorUtility.SetDirty(picked);
                }
            }
        };

        reorderableList.onChangedCallback = _ => MarkDirty();
    }

    private void OnGUI()
    {
        if (levelOrder == null) { LoadOrCreate(); return; }

        InitStyles();

        EditorGUILayout.Space(8);

        // --- Progress info ---
        int savedIdx = PlayerPrefs.GetInt(PREF_LEVEL, 0);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("OYUN İLERLEMESİ", headerStyle);
        string levelLabel = levelOrder.levels.Count > 0 && savedIdx < levelOrder.levels.Count
            ? (levelOrder.levels[savedIdx]?.levelName ?? $"Level {savedIdx + 1}")
            : "—";
        EditorGUILayout.LabelField($"Mevcut:  Level {savedIdx + 1}  —  {levelLabel}", EditorStyles.miniLabel);
        var currentLevelAsset = (levelOrder.levels.Count > 0 && savedIdx < levelOrder.levels.Count)
            ? levelOrder.levels[savedIdx] : null;
        if (currentLevelAsset != null && GUILayout.Button("Project'te Göster", EditorStyles.miniButton))
            EditorGUIUtility.PingObject(currentLevelAsset);
        EditorGUILayout.Space(4);
        if (GUILayout.Button("Sıfırla — Level 1'den Başlat", EditorStyles.miniButton))
        {
            PlayerPrefs.SetInt(PREF_LEVEL, 0);
            PlayerPrefs.Save();
            Repaint();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        // --- Reorderable list ---
        scroll = EditorGUILayout.BeginScrollView(scroll);
        reorderableList.DoLayoutList();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);

        // --- Save button ---
        GUI.backgroundColor = new Color(0.4f, 0.85f, 1f, 0.8f);
        if (GUILayout.Button("Kaydet", GUILayout.Height(36)))
        {
            MarkDirty();
            Debug.Log("[LevelOrder] Kaydedildi.");
        }
        GUI.backgroundColor = Color.white;
    }

    private void MarkDirty()
    {
        EditorUtility.SetDirty(levelOrder);
        AssetDatabase.SaveAssets();
    }

    private void InitStyles()
    {
        if (headerStyle != null) return;
        headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        headerStyle.normal.textColor = new Color(0.4f, 0.85f, 1f);
    }
}
