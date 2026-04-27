using UnityEngine;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class CanvasSetupWindow : EditorWindow
{
    private Vector2Int referenceResolution = new Vector2Int(1080, 1920);
    private Color accentColor = new Color(0.25f, 0.75f, 1f);
    private Color winColor    = new Color(0.20f, 0.85f, 0.40f);
    private Color loseColor   = new Color(0.95f, 0.28f, 0.28f);
    private Color topBarBg    = new Color(0.06f, 0.07f, 0.10f, 0.95f);
    private Color cardBg      = new Color(0.12f, 0.13f, 0.19f);

    [MenuItem("BlockMerge3D/Canvas Setup")]
    public static void ShowWindow()
    {
        var w = GetWindow<CanvasSetupWindow>("Canvas Setup");
        w.minSize = new Vector2(390, 500);
    }

    private GUIStyle _title;
    private GUIStyle TitleStyle => _title ??= new GUIStyle(EditorStyles.boldLabel)
        { fontSize = 14, normal = { textColor = new Color(0.4f, 0.85f, 1f) } };

    private void OnGUI()
    {
        EditorGUILayout.LabelField("CANVAS KURULUM", TitleStyle);
        EditorGUILayout.Space(8);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Ayarlar", EditorStyles.boldLabel);
        referenceResolution = EditorGUILayout.Vector2IntField("Referans Çözünürlük", referenceResolution);
        accentColor         = EditorGUILayout.ColorField("Accent Rengi",     accentColor);
        winColor            = EditorGUILayout.ColorField("Kazanma Rengi",    winColor);
        loseColor           = EditorGUILayout.ColorField("Kaybetme Rengi",   loseColor);
        topBarBg            = EditorGUILayout.ColorField("Üst Bar Rengi",    topBarBg);
        cardBg              = EditorGUILayout.ColorField("Kart Arka Planı",  cardBg);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "Sahneye GameManager eklenmiş olmalı.\n" +
            "Canvas oluşturulunca UIManager referansları otomatik atanır,\n" +
            "buton onClick'leri GameManager'a bağlanır.",
            MessageType.Info);
        EditorGUILayout.Space(10);

        GUI.backgroundColor = new Color(0.4f, 0.85f, 1f, 0.85f);
        if (GUILayout.Button("Canvas'ı Oluştur / Yenile", GUILayout.Height(52)))
            BuildCanvas();
        GUI.backgroundColor = Color.white;
    }

    private void BuildCanvas()
    {
        // EventSystem
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        }

        // Remove old
        var old = GameObject.Find("UICanvas");
        if (old != null)
        {
            if (!EditorUtility.DisplayDialog("Mevcut Canvas",
                "Sahnede UICanvas zaten var. Silip yeniden oluşturulsun mu?", "Evet", "İptal"))
                return;
            Undo.DestroyObjectImmediate(old);
        }

        // Canvas root
        var canvasGO = new GameObject("UICanvas");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create UICanvas");

        var canvas      = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode    = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        var ui = canvasGO.AddComponent<UIManager>();

        var gm = FindObjectOfType<GameManager>();
        if (gm == null)
            Debug.LogWarning("[CanvasSetup] Sahnede GameManager bulunamadı. Butonlar bağlanamadı.");

        // ── Top Bar ──────────────────────────────────────────────────────────
        var topBar = MakeImg("TopBar", canvasGO.transform, topBarBg);
        Anc(topBar.rectTransform, new Vector2(0,1), new Vector2(1,1),
            new Vector2(0.5f,1f), Vector2.zero, new Vector2(0, 132));

        var hlg = topBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding              = new RectOffset(24, 24, 8, 8);
        hlg.spacing              = 0;
        hlg.childAlignment       = TextAnchor.MiddleCenter;
        hlg.childControlWidth    = true;  hlg.childControlHeight    = true;
        hlg.childForceExpandWidth= true;  hlg.childForceExpandHeight= true;

        ui.scoreText       = StatGroup("ScoreGroup",  topBar.transform, "PUAN",  "0",     Color.white);
        ui.timerText       = StatGroup("TimerGroup",  topBar.transform, "SÜRE",  "01:00", Color.white);
        ui.targetScoreText = StatGroup("TargetGroup", topBar.transform, "HEDEF", "/ 100", accentColor);

        // ── Win Overlay ───────────────────────────────────────────────────────
        var winOv = MakeImg("WinOverlay", canvasGO.transform, new Color(0,0,0,0.72f));
        FullStr(winOv.rectTransform);
        ui.winOverlay = winOv.gameObject.AddComponent<CanvasGroup>();
        winOv.gameObject.SetActive(false);

        var winCard = MakeImg("WinCard", winOv.transform, cardBg);
        Anc(winCard.rectTransform, Vector2.one*0.5f, Vector2.one*0.5f,
            Vector2.one*0.5f, Vector2.zero, new Vector2(700, 530));
        ui.winCard = winCard.rectTransform;
        VLayout(winCard.gameObject, 52, 52, 32);

        MakeTxt("Title",       winCard.transform, "TEBRİKLER!",   74, winColor,               TextAlignmentOptions.Center, true);
        ui.winFinalScoreText = MakeTxt("Score", winCard.transform, "Puan: 0", 46, new Color(0.88f,0.88f,0.88f), TextAlignmentOptions.Center, false);
        MakeBtn("NextBtn", winCard.transform, "SONRAKİ LEVEL  ▶", winColor,  Color.white, 38, 88, gm, nameof(GameManager.NextLevel));

        // ── Lose Overlay ──────────────────────────────────────────────────────
        var loseOv = MakeImg("LoseOverlay", canvasGO.transform, new Color(0,0,0,0.72f));
        FullStr(loseOv.rectTransform);
        ui.loseOverlay = loseOv.gameObject.AddComponent<CanvasGroup>();
        loseOv.gameObject.SetActive(false);

        var loseCard = MakeImg("LoseCard", loseOv.transform, cardBg);
        Anc(loseCard.rectTransform, Vector2.one*0.5f, Vector2.one*0.5f,
            Vector2.one*0.5f, Vector2.zero, new Vector2(700, 490));
        ui.loseCard = loseCard.rectTransform;
        VLayout(loseCard.gameObject, 52, 52, 28);

        MakeTxt("Title",        loseCard.transform, "SÜRE DOLDU",  72, loseColor,             TextAlignmentOptions.Center, true);
        ui.loseFinalScoreText = MakeTxt("Score", loseCard.transform, "Puan: 0", 46, new Color(0.88f,0.88f,0.88f), TextAlignmentOptions.Center, false);
        MakeBtn("RetryBtn", loseCard.transform, "↺  TEKRAR DENE", loseColor, Color.white, 38, 88, gm, nameof(GameManager.RetryLevel));

        EditorUtility.SetDirty(canvasGO);
        Selection.activeGameObject = canvasGO;

        string gmMsg = gm != null ? "Butonlar GameManager'a bağlandı." : "⚠ GameManager bulunamadı — butonları manuel bağla.";
        EditorUtility.DisplayDialog("Başarılı", $"UICanvas oluşturuldu!\n\n{gmMsg}", "Tamam");
        Debug.Log("[CanvasSetup] UICanvas oluşturuldu.");
    }

    // ── Stat group (label + value stacked) ───────────────────────────────────

    private TextMeshProUGUI StatGroup(string name, Transform parent, string label, string value, Color valueColor)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment       = TextAnchor.MiddleCenter;
        vlg.childControlWidth    = true;  vlg.childControlHeight    = true;
        vlg.childForceExpandWidth= true;  vlg.childForceExpandHeight= true;
        vlg.spacing = 0;

        var lbl = MakeTxt("Label", go.transform, label, 20, new Color(0.62f,0.62f,0.70f), TextAlignmentOptions.Center, false);
        lbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 26;

        var val = MakeTxt("Value", go.transform, value, 50, valueColor, TextAlignmentOptions.Center, true);
        val.gameObject.AddComponent<LayoutElement>().preferredHeight = 68;

        return val;
    }

    // ── Button ────────────────────────────────────────────────────────────────

    private void MakeBtn(string name, Transform parent, string label,
        Color bg, Color fg, int fontSize, int height, GameManager gm, string methodName)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;

        var img   = go.AddComponent<Image>();
        img.color = bg;

        var btn = go.AddComponent<Button>();
        var cs  = btn.colors;
        Color hc = bg * 1.18f; hc.a = 1f;
        Color pc = bg * 0.78f; pc.a = 1f;
        cs.highlightedColor = hc;
        cs.pressedColor     = pc;
        btn.colors = cs;

        go.AddComponent<ButtonAnimator>();

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        FullStr(lblGO.GetComponent<RectTransform>());
        var tmp       = lblGO.AddComponent<TextMeshProUGUI>();
        AssignDefaultFont(tmp);
        tmp.text      = label;
        tmp.fontSize  = fontSize;
        tmp.color     = fg;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.enableWordWrapping = false;

        if (gm != null)
        {
            var method = gm.GetType().GetMethod(methodName);
            if (method != null)
            {
                var action = (UnityEngine.Events.UnityAction)
                    System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), gm, method);
                UnityEventTools.AddPersistentListener(btn.onClick, action);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Image MakeImg(string name, Transform parent, Color color)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = color;
        return img;
    }

    private static TextMeshProUGUI MakeTxt(string name, Transform parent, string text,
        int size, Color color, TextAlignmentOptions align, bool bold)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp            = go.AddComponent<TextMeshProUGUI>();
        AssignDefaultFont(tmp);
        tmp.text           = text;
        tmp.fontSize       = size;
        tmp.color          = color;
        tmp.alignment      = align;
        tmp.fontStyle      = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.enableWordWrapping = false;
        return tmp;
    }

    private static void AssignDefaultFont(TextMeshProUGUI tmp)
    {
        if (tmp.font != null) return;
        var font = TMPro.TMP_Settings.defaultFontAsset;
        if (font == null)
            font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (font != null) tmp.font = font;
    }

    private static void VLayout(GameObject go, int padH, int padV, int spacing)
    {
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(padH, padH, padV, padV);
        vlg.spacing              = spacing;
        vlg.childAlignment       = TextAnchor.MiddleCenter;
        vlg.childControlWidth    = true;  vlg.childControlHeight    = false;
        vlg.childForceExpandWidth= true;  vlg.childForceExpandHeight= false;
    }

    private static void FullStr(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
    }

    private static void Anc(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
    }
}
