using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public enum TutorialStepType
{
    /// <summary>Katman butonuna bas — parmak sağdaki en alt katman butonunun üzerinde nabız atar.</summary>
    TapLayerButton,

    /// <summary>Ekranı yatay kaydırıp tahtayı döndür — parmak ekran ortasında sağa-sola gider.</summary>
    SwipeToRotate,

    /// <summary>Parçayı karttan tahtaya sürükle — parmak karttan tahtaya doğru hareket eder.</summary>
    DragPieceToBoard,

    /// <summary>Joker (geri al) butonuna bas — parmak kırmızı arcade butonunun üzerinde
    /// nabız atar. Bu adımın kullanımı jokeri TÜKETMEZ (bkz. LevelManager.RestoreJoker).</summary>
    UseJoker,
}

// ═══════════════════════════════════════════════════════════════════
//  TUTORIAL OVERLAY  —  Ekranda Parmak Göstergesi
//  BlockMerge3D
//
//  LevelData.tutorialSteps'teki adımları SIRAYLA gösterir. Her adım, oyuncu
//  ilgili eylemi gerçekten yapınca (bkz. TutorialEvents) tamamlanır ve sıradaki
//  adıma geçilir; liste bitince gösterge kendini kapatır.
//
//  Gösterge oyunu KİLİTLEMEZ ve tıklamayı engellemez (raycastTarget=false) —
//  oyuncu isterse adımı yok sayıp kendi bildiğini yapabilir. Bu bilinçli:
//  zorlayıcı öğreticiler, mekaniği zaten bilen oyuncuyu cezalandırır.
// ═══════════════════════════════════════════════════════════════════

public class TutorialOverlay : MonoBehaviour
{
    public static TutorialOverlay Instance { get; private set; }

    [Header("Gösterge")]
    [Tooltip("Parmak/dokunuş ikonu — Assets/Violet Theme Ui/Colored Icons/Touch.png")]
    public Sprite touchIcon;

    [Tooltip("İkonun ekrandaki boyutu (piksel)")]
    public Vector2 iconSize = new Vector2(96f, 96f);

    [Tooltip("Parmak UCUNUN ikon merkezine göre kayması. Touch.png'de parmak ucu sol " +
             "üstte olduğu için ikon, hedefin biraz SOLUNA kaydırılır — aksi halde uç " +
             "hedefin sağ kenarına düşer ve ikon hedefi örter.")]
    public Vector2 tipOffset = new Vector2(-30f, -8f);

    [Tooltip("Adım tamamlanana kadar animasyonun tekrar süresi (saniye)")]
    public float loopDuration = 1.2f;

    [Tooltip("Öğretici adımı başlamadan önceki gecikme — seviye açılış animasyonu bitsin")]
    public float startDelay = 0.8f;

    private Canvas        canvas;
    private RectTransform icon;
    private CanvasGroup   iconGroup;
    private Sequence      loop;

    private readonly List<TutorialStepType> steps = new List<TutorialStepType>();

    // Adım sırası oyuncuyu BAĞLAMAZ: oyuncu bir eylemi öğretici o adıma gelmeden
    // yapabilir (ör. startDelay dolmadan katman butonuna basmak). Bu durumda olay
    // boşa gidip parmak, artık gizlenmiş bir butonu sonsuza dek göstermeye devam
    // ediyordu. Seviye başından beri gerçekleşmiş eylemleri burada tutuyoruz ve
    // o adıma gelindiğinde atlıyoruz.
    private readonly HashSet<TutorialStepType> alreadyDone = new HashSet<TutorialStepType>();

    private int  stepIndex = -1;
    private bool running;

    private void Awake()
    {
        Instance = this;
        TutorialEvents.LayerOpened  += OnLayerOpened;
        TutorialEvents.BoardRotated += OnBoardRotated;
        TutorialEvents.PiecePlaced  += OnPiecePlaced;
        TutorialEvents.JokerUsed    += OnJokerUsed;
    }

    private void OnDestroy()
    {
        TutorialEvents.LayerOpened  -= OnLayerOpened;
        TutorialEvents.BoardRotated -= OnBoardRotated;
        TutorialEvents.PiecePlaced  -= OnPiecePlaced;
        TutorialEvents.JokerUsed    -= OnJokerUsed;
        if (Instance == this) Instance = null;
        KillLoop();
    }

    /// <summary>Seviye yüklenince LevelManager tarafından çağrılır.</summary>
    public void BeginForLevel(LevelData level)
    {
        StopAllCoroutines();
        KillLoop();
        steps.Clear();
        alreadyDone.Clear();
        stepIndex = -1;
        running   = false;
        HideIcon();

        if (level == null || level.tutorialSteps == null || level.tutorialSteps.Count == 0) return;

        steps.AddRange(level.tutorialSteps);
        running = true;
        // Katman butonları BuildLayerButtons içinde gecikmeli üretiliyor; ayrıca
        // seviye açılış animasyonunun üstüne binmesin diye kısa bir bekleme.
        Invoke(nameof(AdvanceStep), startDelay);
    }

    private void AdvanceStep()
    {
        if (!running) return;

        stepIndex++;
        if (stepIndex >= steps.Count)
        {
            running = false;
            HideIcon();
            return;
        }

        // Oyuncu bu eylemi öğretici buraya gelmeden yaptıysa adımı gösterme, atla.
        if (alreadyDone.Contains(steps[stepIndex]))
        {
            AdvanceStep();
            return;
        }

        ShowStep(steps[stepIndex]);
    }

    // ─── Olaylar ──────────────────────────────────────────────────────────────

    private void OnLayerOpened()  => CompleteIfCurrent(TutorialStepType.TapLayerButton);
    private void OnBoardRotated() => CompleteIfCurrent(TutorialStepType.SwipeToRotate);
    private void OnPiecePlaced()  => CompleteIfCurrent(TutorialStepType.DragPieceToBoard);

    private void OnJokerUsed()
    {
        if (!running || stepIndex < 0 || stepIndex >= steps.Count) return;
        if (steps[stepIndex] != TutorialStepType.UseJoker) return;

        // Öğretici amaçlı kullanım hakkı yakmasın — oyuncu asıl hatasında jokeri
        // elinde bulsun. Geri alma animasyonu bitsin diye kısa gecikme.
        Invoke(nameof(GiveJokerBack), 0.5f);
        CompleteIfCurrent(TutorialStepType.UseJoker);
    }

    private void GiveJokerBack() => LevelManager.Instance?.RestoreJoker();

    private void CompleteIfCurrent(TutorialStepType type)
    {
        alreadyDone.Add(type);

        if (!running || stepIndex < 0 || stepIndex >= steps.Count) return;
        if (steps[stepIndex] != type) return;

        KillLoop();
        HideIcon();
        // Eylemin kendi animasyonu (panel açılışı, tahta dönüşü, parça yerleşimi)
        // bitsin diye kısa bir nefes payı.
        Invoke(nameof(AdvanceStep), 0.6f);
    }

    // ─── Görsel ───────────────────────────────────────────────────────────────

    private void ShowStep(TutorialStepType type)
    {
        if (!EnsureIcon()) return;

        switch (type)
        {
            case TutorialStepType.TapLayerButton:  PlayTap();   break;
            case TutorialStepType.SwipeToRotate:   PlaySwipe(); break;
            case TutorialStepType.DragPieceToBoard: PlayDrag();  break;
            case TutorialStepType.UseJoker:        PlayJoker(); break;
        }
    }

    private void PlayJoker()
    {
        Vector2 pos = new Vector2(0f, -Screen.height * 0.4f);
        var jokerBtn = FindObjectOfType<ControlButton>();
        if (jokerBtn != null)
        {
            // ControlButton UI DEĞİL, sahnedeki 3D arcade butonu — konumu kamera
            // üzerinden ekrana yansıtılmalı (bkz. WorldObjectToCanvas).
            //
            // transform.position KULLANILMAZ: ControlButton kökünün orijini görünen
            // kırmızı kapaktan çok uzakta (ekranda ~450 piksel fark ölçüldü), parmak
            // bu yüzden boşluğu gösteriyordu. Görünen geometrinin merkezini alıyoruz.
            Vector3 target = jokerBtn.buttonCap != null
                ? jokerBtn.buttonCap.position
                : jokerBtn.transform.position;

            var rend = jokerBtn.GetComponentInChildren<Renderer>();
            if (rend != null) target = rend.bounds.center;

            pos = WorldObjectToCanvas(target) + tipOffset;
        }

        PlaceIcon(pos);
        loop = DOTween.Sequence().SetLink(icon.gameObject).SetLoops(-1)
            .Append(icon.DOScale(0.75f, loopDuration * 0.35f).SetEase(Ease.OutQuad))
            .Join(iconGroup.DOFade(1f, loopDuration * 0.35f))
            .Append(icon.DOScale(1f, loopDuration * 0.4f).SetEase(Ease.OutBack))
            .AppendInterval(loopDuration * 0.25f);
    }

    private void PlayTap()
    {
        Vector2 pos = ScreenCenter();
        var btn = LayerPanelController.Instance != null
            ? LayerPanelController.Instance.FirstLayerButton
            : null;
        if (btn != null) pos = WorldToCanvas(btn.position) + tipOffset;

        PlaceIcon(pos);
        loop = DOTween.Sequence().SetLink(icon.gameObject).SetLoops(-1)
            .Append(icon.DOScale(0.75f, loopDuration * 0.35f).SetEase(Ease.OutQuad))
            .Join(iconGroup.DOFade(1f, loopDuration * 0.35f))
            .Append(icon.DOScale(1f, loopDuration * 0.4f).SetEase(Ease.OutBack))
            .AppendInterval(loopDuration * 0.25f);
    }

    private void PlaySwipe()
    {
        Vector2 center = ScreenCenter();
        float dist = Mathf.Min(Screen.width * 0.18f, 160f);

        PlaceIcon(center + Vector2.left * dist);
        loop = DOTween.Sequence().SetLink(icon.gameObject).SetLoops(-1)
            .Append(iconGroup.DOFade(1f, loopDuration * 0.15f))
            .Append(icon.DOAnchorPos(center + Vector2.right * dist, loopDuration * 0.55f)
                        .SetEase(Ease.InOutQuad))
            .Append(iconGroup.DOFade(0f, loopDuration * 0.2f))
            .AppendCallback(() => icon.anchoredPosition = center + Vector2.left * dist)
            .AppendInterval(loopDuration * 0.1f);
    }

    private void PlayDrag()
    {
        Vector2 from = new Vector2(0f, -Screen.height * 0.28f);   // kart bölgesi
        var cards = LevelManager.Instance != null ? LevelManager.Instance.pieceCards : null;
        if (cards != null)
        {
            foreach (var c in cards)
            {
                if (c == null) continue;
                var rt = c.transform as RectTransform;
                if (rt != null) { from = WorldToCanvas(rt.position) + tipOffset; break; }
            }
        }

        Vector2 to = ScreenCenter() + tipOffset;

        PlaceIcon(from);
        loop = DOTween.Sequence().SetLink(icon.gameObject).SetLoops(-1)
            .Append(iconGroup.DOFade(1f, loopDuration * 0.15f))
            .Append(icon.DOAnchorPos(to, loopDuration * 0.6f).SetEase(Ease.InOutQuad))
            .Append(iconGroup.DOFade(0f, loopDuration * 0.2f))
            .AppendCallback(() => icon.anchoredPosition = from)
            .AppendInterval(loopDuration * 0.1f);
    }

    // ─── Altyapı ──────────────────────────────────────────────────────────────

    private bool EnsureIcon()
    {
        if (icon != null) return true;
        if (touchIcon == null)
        {
            Debug.LogWarning("[TutorialOverlay] touchIcon atanmamış — öğretici gösterilemiyor.");
            return false;
        }

        canvas = LayerPanelController.Instance != null && LayerPanelController.Instance.uiCanvas != null
            ? LayerPanelController.Instance.uiCanvas
            : FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[TutorialOverlay] Sahnede Canvas bulunamadı.");
            return false;
        }

        var go = new GameObject("TutorialTouchIcon",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsLastSibling();   // her şeyin üstünde

        var img = go.GetComponent<Image>();
        img.sprite = touchIcon;
        img.preserveAspect = true;
        // Gösterge oyunu ENGELLEMEZ: tıklamalar altındaki butona geçer.
        img.raycastTarget = false;

        icon = go.GetComponent<RectTransform>();
        icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
        icon.pivot     = new Vector2(0.5f, 0.5f);
        icon.sizeDelta = iconSize;

        iconGroup = go.GetComponent<CanvasGroup>();
        iconGroup.blocksRaycasts = false;
        iconGroup.interactable   = false;
        iconGroup.alpha          = 0f;

        return true;
    }

    private void PlaceIcon(Vector2 anchoredPos)
    {
        icon.gameObject.SetActive(true);
        // HER adımda yeniden en öne alınmalı: katman butonları seviye yüklenirken
        // BuildLayerButtons içinde yok edilip yeniden üretiliyor ve canvas'a EN SONA
        // ekleniyor — ikon yalnızca oluşturulurken öne alınsaydı ilk seviyeden sonra
        // butonların ARKASINDA kalırdı.
        icon.SetAsLastSibling();
        icon.anchoredPosition = anchoredPos;
        icon.localScale       = Vector3.one;
        iconGroup.alpha       = 0f;
    }

    private void HideIcon()
    {
        if (icon != null) icon.gameObject.SetActive(false);
    }

    private void KillLoop()
    {
        if (loop != null) { loop.Kill(); loop = null; }
        if (icon != null) icon.DOKill();
        if (iconGroup != null) iconGroup.DOKill();
    }

    private Vector2 ScreenCenter() => Vector2.zero;   // canvas merkezi (anchor 0.5/0.5)

    /// <summary>Dünya/ekran uzayındaki bir noktayı, ikonun bağlı olduğu Canvas'ın
    /// anchoredPosition uzayına çevirir. Canvas'ın render moduna göre kamera
    /// referansı değişir — Overlay modda kamera verilmemeli.</summary>
    private Vector2 WorldToCanvas(Vector3 worldPos)
    {
        var canvasRect = canvas.transform as RectTransform;
        Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCam, worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenPoint, uiCam, out Vector2 local);
        return local;
    }

    /// <summary>WorldToCanvas'ın 3D SAHNE nesneleri için olan sürümü. Fark kritik:
    /// ScreenSpaceOverlay canvas'ta RectTransformUtility.WorldToScreenPoint(null, p)
    /// noktayı ZATEN ekran uzayında sayar — bu UI RectTransform'ları için doğru, ama
    /// gerçek bir dünya konumu (ör. arcade joker butonu) için tamamen yanlış sonuç
    /// verir. Bu yüzden önce oyun kamerasıyla ekrana yansıtıyoruz.</summary>
    private Vector2 WorldObjectToCanvas(Vector3 worldPos)
    {
        var gameCam = Camera.main;
        if (gameCam == null) return Vector2.zero;

        Vector2 screenPoint = gameCam.WorldToScreenPoint(worldPos);
        var canvasRect = canvas.transform as RectTransform;
        Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenPoint, uiCam, out Vector2 local);
        return local;
    }
}
