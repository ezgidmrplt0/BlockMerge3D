using UnityEngine;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class CanvasSetupWindow : EditorWindow
{
    private Vector2Int referenceResolution = new Vector2Int(1080, 1920);
    private Color bgColor        = new Color(0.45f, 0.70f, 0.95f, 1f); // Casual açık/canlı mavi arkaplan
    private Color accentColor    = new Color(1.00f, 0.80f, 0.00f, 1f); // Altın Sarısı
    private Color winColor       = new Color(1.00f, 0.85f, 0.10f, 1f); // Canlı Altın Sarısı (Yeşille mükemmel kontrast)
    private Color loseColor      = new Color(1.00f, 0.95f, 0.90f, 1f); // Parlak Krem Beyazı
    private Color textColor      = new Color(1.00f, 1.00f, 1.00f, 1f); // Beyaz metin
    private Color darkTextColor  = new Color(0.15f, 0.15f, 0.20f, 1f); // Koyu metin (açık zeminler için)

    [MenuItem("BlockMerge3D/Canvas Setup")]
    public static void ShowWindow()
    {
        var w = GetWindow<CanvasSetupWindow>("Canvas Setup");
        w.minSize = new Vector2(400, 500);
    }

    private GUIStyle _title;
    private GUIStyle TitleStyle => _title ??= new GUIStyle(EditorStyles.boldLabel)
        { fontSize = 14, normal = { textColor = accentColor } };

    private void OnGUI()
    {
        EditorGUILayout.LabelField("CANVAS KURULUM (Sıfırdan Premium UI)", TitleStyle);
        EditorGUILayout.Space(8);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Renk Paleti", EditorStyles.boldLabel);
        bgColor     = EditorGUILayout.ColorField("Kamera Arkaplan", bgColor);
        accentColor = EditorGUILayout.ColorField("Vurgu Rengi", accentColor);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox("Sahnede GameManager bulunmalıdır. Her şey eski kalıntılardan arındırılarak sıfırdan oluşturulacaktır.", MessageType.Info);
        EditorGUILayout.Space(10);

        GUI.backgroundColor = new Color(0.4f, 0.85f, 1f, 0.85f);
        if (GUILayout.Button("1. UICanvas ve Arayüzü Oluştur", GUILayout.Height(50)))
            BuildCanvas();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(10);

        GUI.backgroundColor = new Color(0.5f, 1f, 0.45f, 0.9f);
        if (GUILayout.Button("2. Parça Kartlarını (Bottom Panel) Oluştur", GUILayout.Height(50)))
            BuildPieceCardPanel();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(10);

        GUI.backgroundColor = new Color(1f, 0.78f, 0.35f, 0.9f);
        if (GUILayout.Button("3. Işık ve Gölgeleri Optimize Et", GUILayout.Height(50)))
            BuildLightingAndEnvironment();
        GUI.backgroundColor = Color.white;
    }

    private void BuildCanvas()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        }

        var old = GameObject.Find("UICanvas");
        if (old != null) Undo.DestroyObjectImmediate(old);

        var canvasGO = new GameObject("UICanvas");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create UICanvas");

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        var ui = canvasGO.AddComponent<UIManager>();
        var gm = FindObjectOfType<GameManager>();

        // ── Üst Bar (Top Bar) ──
        // GUI_28 sprite'ını kullanıyoruz ve rengini Color.white yaparak asset'in gerçek rengini koruyoruz
        var topBar = MakeImg("TopBar", canvasGO.transform, Color.white, GetSprite("GUI_28"));
        Anc(topBar.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, 240));
        
        // Gölgelendirme
        var tbShadow = topBar.gameObject.AddComponent<Shadow>();
        tbShadow.effectColor = new Color(0, 0, 0, 0.5f);
        tbShadow.effectDistance = new Vector2(0, -5);

        // Notch Korumalı İçerik (Yeşil Alan Ortası: Y = [50, 150])
        var contentGO = new GameObject("ContentContainer", typeof(RectTransform));
        contentGO.transform.SetParent(topBar.transform, false);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 0f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.offsetMin = new Vector2(40f, 50f);   // Alt beyaz border'ı ve kenarları temizle
        contentRT.offsetMax = new Vector2(-40f, -90f); // Üst notch ve safe area payı

        var hlg = contentGO.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(0, 0, 0, 0); // Padding zaten offsetMin/offsetMax ile sağlandı
        hlg.spacing = 20;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = false;

        ui.scoreText       = StatGroup("ScoreGroup",  contentGO.transform, "PUAN", "0", textColor);
        ui.timerText       = StatGroup("TimerGroup",  contentGO.transform, "SÜRE", "01:00", accentColor);
        ui.targetScoreText = StatGroup("TargetGroup", contentGO.transform, "HEDEF", "/ 100", textColor);

        // Alt Çizgi İlerleme Çubuğu (Progress Bar)
        var barBg = MakeImg("ProgressBarBg", topBar.transform, new Color(0, 0, 0, 0.4f));
        Anc(barBg.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0, 8));

        var barFillGO = new GameObject("ProgressBarFill", typeof(RectTransform));
        barFillGO.transform.SetParent(barBg.transform, false);
        FullStr(barFillGO.GetComponent<RectTransform>());

        var barFillImg = barFillGO.AddComponent<Image>();
        barFillImg.color = accentColor;
        barFillImg.type = Image.Type.Filled;
        barFillImg.fillMethod = Image.FillMethod.Horizontal;
        barFillImg.fillAmount = 0.5f;
        ui.scoreProgressBar = barFillImg;

        // ── Win Overlay ──
        var winOv = MakeImg("WinOverlay", canvasGO.transform, new Color(0, 0, 0, 0.85f));
        FullStr(winOv.rectTransform);
        ui.winOverlay = winOv.gameObject.AddComponent<CanvasGroup>();
        winOv.gameObject.SetActive(false);

        // Büyük Popup Paneli (GUI_27)
        var winCard = MakeImg("WinCard", winOv.transform, Color.white, GetSprite("GUI_27"));
        Anc(winCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, new Vector2(750, 600));
        ui.winCard = winCard.rectTransform;
        VLayout(winCard.gameObject, 120, 60, 40); // İç kenar boşlukları daraltıldı (buton ve yazılar daha şık durur)

        MakeTxtWithOutline("Title", winCard.transform, "TEBRİKLER!", 80, winColor, TextAlignmentOptions.Center, true);
        ui.winFinalScoreText = MakeTxtWithOutline("Score", winCard.transform, "SKOR: 0", 54, Color.white, TextAlignmentOptions.Center, true);
        MakeBtn("NextBtn", winCard.transform, "SONRAKİ LEVEL", Color.white, Color.white, 40, 100, gm, nameof(GameManager.NextLevel), GetSprite("GUI_12"));

        // ── Lose Overlay ──
        var loseOv = MakeImg("LoseOverlay", canvasGO.transform, new Color(0, 0, 0, 0.85f));
        FullStr(loseOv.rectTransform);
        ui.loseOverlay = loseOv.gameObject.AddComponent<CanvasGroup>();
        loseOv.gameObject.SetActive(false);

        var loseCard = MakeImg("LoseCard", loseOv.transform, Color.white, GetSprite("GUI_27"));
        Anc(loseCard.rectTransform, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, new Vector2(750, 550));
        ui.loseCard = loseCard.rectTransform;
        VLayout(loseCard.gameObject, 120, 60, 40); // İç kenar boşlukları daraltıldı

        var loseTitle = MakeTxtWithOutline("Title", loseCard.transform, "SÜRE DOLDU", 80, loseColor, TextAlignmentOptions.Center, true);
        loseTitle.outlineColor = new Color32(180, 20, 20, 255); // Koyu kırmızı kontur (Krem beyaz zemin üstünde harika durur)
        loseTitle.outlineWidth = 0.25f;

        ui.loseFinalScoreText = MakeTxtWithOutline("Score", loseCard.transform, "SKOR: 0", 54, Color.white, TextAlignmentOptions.Center, true);
        MakeBtn("RetryBtn", loseCard.transform, "TEKRAR DENE", Color.white, Color.white, 40, 100, gm, nameof(GameManager.RetryLevel), GetSprite("GUI_13"));

        EditorUtility.SetDirty(canvasGO);
        Selection.activeGameObject = canvasGO;
        Debug.Log("[CanvasSetup] Tertemiz bir UICanvas oluşturuldu.");
    }

    private void BuildPieceCardPanel()
    {
        var canvasGO = GameObject.Find("UICanvas");
        if (canvasGO == null) return;

        var existing = canvasGO.transform.Find("BottomPiecePanel");
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        // ── Alt Panel ──
        var panel = new GameObject("BottomPiecePanel", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(panel, "Create BottomPiecePanel");
        panel.transform.SetParent(canvasGO.transform, false);

        var panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.04f, 0.03f); 
        panelRT.anchorMax = new Vector2(0.96f, 0.03f);
        panelRT.pivot = new Vector2(0.5f, 0f);
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta = new Vector2(0f, 300f);

        // Parent panelin görsel arka planı kaldırıldı. Artık boş/şeffaf bir kapsayıcı.

        var hlg = panel.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(10, 10, 10, 10);
        hlg.spacing = 25;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        var camRoot = new GameObject("PiecePreviewCameras");
        Undo.RegisterCreatedObjectUndo(camRoot, "Create PiecePreviewCameras");

        var lm = FindObjectOfType<LevelManager>();
        SerializedObject lmSO = lm != null ? new SerializedObject(lm) : null;
        SerializedProperty cardsProp = lmSO?.FindProperty("pieceCards");
        if (cardsProp != null) cardsProp.ClearArray();

        for (int i = 0; i < 3; i++)
        {
            var card = new GameObject($"PieceCard_{i}", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(card, "Create PieceCard");
            card.transform.SetParent(panel.transform, false);

            var arf = card.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            arf.aspectRatio = 1f;

            // Slot Arka Plan (GUI_52)
            var cardImg = card.AddComponent<Image>();
            cardImg.color = Color.white;
            cardImg.sprite = GetSprite("GUI_52");
            cardImg.type = Image.Type.Sliced;
            
            var le = card.AddComponent<LayoutElement>();
            le.preferredWidth = 180f;
            le.preferredHeight = 180f;

            var rawGO = new GameObject("PreviewImage", typeof(RectTransform));
            rawGO.transform.SetParent(card.transform, false);
            var rawRT = rawGO.GetComponent<RectTransform>();
            rawRT.anchorMin = new Vector2(0.05f, 0.05f);
            rawRT.anchorMax = new Vector2(0.95f, 0.95f);
            rawRT.sizeDelta = Vector2.zero;
            var rawImg = rawGO.AddComponent<RawImage>();

            var emptyGO = new GameObject("EmptyOverlay", typeof(RectTransform));
            emptyGO.transform.SetParent(card.transform, false);
            var emptyRT = emptyGO.GetComponent<RectTransform>();
            emptyRT.anchorMin = new Vector2(0.1f, 0.1f);
            emptyRT.anchorMax = new Vector2(0.9f, 0.9f);
            emptyRT.sizeDelta = Vector2.zero;
            var emptyImg = emptyGO.AddComponent<Image>();
            emptyImg.sprite = GetSprite("GUI_53");
            emptyImg.type = Image.Type.Sliced;
            emptyImg.color = new Color(1, 1, 1, 0.1f);
            emptyGO.SetActive(false);

            var cardUI = card.AddComponent<PieceCardUI>();
            cardUI.previewImage = rawImg;
            cardUI.emptyOverlay = emptyGO;

            var camGO = new GameObject($"PreviewCam_{i}");
            Undo.RegisterCreatedObjectUndo(camGO, "Create PreviewCam");
            camGO.transform.SetParent(camRoot.transform, false);
            camGO.transform.position = new Vector3(-10000f - i * 20f, 3.5f, -5.5f);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); 
            cam.orthographic = true;
            cam.orthographicSize = 2f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            cam.depth = -2;

            cardUI.previewCam = cam;

            if (cardsProp != null)
            {
                cardsProp.InsertArrayElementAtIndex(i);
                cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = cardUI;
            }
        }

        if (lmSO != null)
        {
            lmSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(lm);
        }
        
        Selection.activeGameObject = panel;
        Debug.Log("[CanvasSetup] BottomPiecePanel oluşturuldu.");
    }

    private void BuildLightingAndEnvironment()
    {
        var mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = bgColor;
            EditorUtility.SetDirty(mainCam);
        }

        var lights = FindObjectsOfType<Light>();
        Light dirLight = null;
        foreach (var l in lights)
        {
            if (l.type == LightType.Directional) { dirLight = l; break; }
        }

        if (dirLight == null)
        {
            var lightGO = new GameObject("Directional Light", typeof(Light));
            dirLight = lightGO.GetComponent<Light>();
            dirLight.type = LightType.Directional;
        }

        dirLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        dirLight.color = new Color(1f, 0.98f, 0.95f);
        dirLight.intensity = 1.1f;
        dirLight.shadows = LightShadows.Soft;
        dirLight.shadowStrength = 0.5f;
        dirLight.shadowBias = 0.02f;
        dirLight.shadowNormalBias = 0.1f;
        dirLight.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;

        QualitySettings.shadowDistance = 25f;
        QualitySettings.shadowCascades = 0;

        Debug.Log("[CanvasSetup] Işıklandırma ve kamera ayarları yapıldı.");
    }

    private TextMeshProUGUI StatGroup(string name, Transform parent, string label, string value, Color valueColor)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.spacing = 2; // Metinler düzgün hizalansın

        var lbl = MakeTxtWithOutline("Label", go.transform, label, 20, Color.white, TextAlignmentOptions.Center, false);
        lbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;

        var val = MakeTxtWithOutline("Value", go.transform, value, 44, valueColor, TextAlignmentOptions.Center, true);
        val.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;

        return val;
    }

    private void MakeBtn(string name, Transform parent, string label, Color bg, Color fg, int fontSize, int height, GameManager gm, string methodName, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;

        var img = go.AddComponent<Image>();
        img.color = bg;
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
        }

        var btn = go.AddComponent<Button>();
        var cs = btn.colors;
        cs.normalColor = Color.white;
        cs.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        cs.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = cs;

        go.AddComponent<ButtonAnimator>();

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        FullStr(lblGO.GetComponent<RectTransform>());
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        AssignDefaultFont(tmp);
        tmp.text = label;
        tmp.fontSize = fontSize;
        tmp.color = fg;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        
        // TMPro Native SDF Outline (Buton yazısının arka plandan mükemmel ayrışması için)
        tmp.outlineColor = new Color32(20, 20, 20, 255);
        tmp.outlineWidth = 0.2f;

        if (gm != null)
        {
            var method = gm.GetType().GetMethod(methodName);
            if (method != null)
            {
                var action = (UnityEngine.Events.UnityAction)System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), gm, method);
                UnityEventTools.AddPersistentListener(btn.onClick, action);
            }
        }
    }

    private static Sprite GetSprite(string name)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("Assets/2D Casual UI/Sprite/GUI.png");
        foreach (var a in assets)
            if (a is Sprite s && s.name == name) return s;
        return null;
    }

    private static Image MakeImg(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
        }
        return img;
    }

    private static TextMeshProUGUI MakeTxtWithOutline(string name, Transform parent, string text, int size, Color color, TextAlignmentOptions align, bool bold)
    {
        var tmp = MakeTxt(name, parent, text, size, color, align, bold);
        
        // TMPro Native SDF Outline (Kusursuz, kalın ve yumuşak casual stil kontur)
        tmp.outlineColor = new Color32(20, 20, 20, 255); // Koyu kahve/siyah kontur
        tmp.outlineWidth = 0.22f; // SDF kontur kalınlığı
        
        return tmp;
    }

    private static TextMeshProUGUI MakeTxt(string name, Transform parent, string text, int size, Color color, TextAlignmentOptions align, bool bold)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        AssignDefaultFont(tmp);
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        return tmp;
    }

    private static void AssignDefaultFont(TextMeshProUGUI tmp)
    {
        if (tmp.font != null) return;
        var font = TMPro.TMP_Settings.defaultFontAsset;
        if (font == null)
            font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (font != null) tmp.font = font;
    }

    private static void VLayout(GameObject go, int padH, int padV, int spacing)
    {
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(padH, padH, padV, padV);
        vlg.spacing = spacing;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
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
