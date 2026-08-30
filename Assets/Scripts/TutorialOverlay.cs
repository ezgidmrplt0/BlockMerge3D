using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public enum TutorialStepType
{
    /// <summary>Ekranı yatay kaydırıp tahtayı döndür — parmak ekran ortasında sağa-sola gider.</summary>
    SwipeToRotate,

    /// <summary>Parçayı karttan tahtaya sürükle — parmak karttan tahtaya doğru hareket eder.</summary>
    DragPieceToBoard,

    /// <summary>Joker (geri al) butonuna bas — parmak kırmızı arcade butonunun üzerinde
    /// nabız atar. Bu adımın kullanımı jokeri TÜKETMEZ (bkz. LevelManager.RestoreJoker).</summary>
    UseJoker,

    /// <summary>Parçayı saklama kutusuna (Hold) sürükle — parmak karttan saklama kutusuna doğru hareket eder.</summary>
    DragPieceToHold,
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
    private LevelData currentLevel;
    private int dragStepCount = 0;

    public bool IsRunning => running;
    public TutorialStepType? CurrentStep => running && stepIndex >= 0 && stepIndex < steps.Count ? (TutorialStepType?)steps[stepIndex] : null;
    public bool IsDragStepActive => running && stepIndex >= 0 && stepIndex < steps.Count && steps[stepIndex] == TutorialStepType.DragPieceToBoard;

    // Engel öğreticisinde (Tutorial_4_Obstacle) öğreticinin işaret ettiği hedef hücreler.
    // Sürükleme sırasında GridManager, geçerli TÜM konumları parlatmak yerine yalnızca
    // bunları parlatır (bkz. RestrictDragHighlights). HighlightTutorialTargetCells doldurur.
    private readonly HashSet<Vector3Int> dragHighlightCells = new HashSet<Vector3Int>();

    private bool IsLevel1 =>
        (currentLevel != null && (currentLevel.levelName == "LEVEL 1" || currentLevel.levelName.StartsWith("LEVEL 1")))
        || (GameManager.Instance != null && GameManager.Instance.CurrentLevelNumber == 1);

    private bool IsLevel2 =>
        (currentLevel != null && (currentLevel.levelName == "LEVEL2" || currentLevel.levelName.StartsWith("LEVEL2") || currentLevel.levelName == "LEVEL 2"))
        || (GameManager.Instance != null && GameManager.Instance.CurrentLevelNumber == 2);

    private bool IsLevel3 =>
        (currentLevel != null && (currentLevel.levelName == "LEVEL 3" || currentLevel.levelName.StartsWith("LEVEL 3") || currentLevel.levelName == "Tutorial_3_Layers"))
        || (GameManager.Instance != null && GameManager.Instance.CurrentLevelNumber == 3);

    /// <summary>Sürükleme sırasında geçerli tüm konumların parlaması KAPATILIP yalnızca
    /// öğreticinin gösterdiği hedefin (DragHighlightCells) parlaması gereken an.</summary>
    public bool RestrictDragHighlights =>
        running && stepIndex >= 0 && stepIndex < steps.Count
        && steps[stepIndex] == TutorialStepType.DragPieceToBoard
        && (IsLevel1 || IsLevel2 || IsLevel3);

    public IReadOnlyCollection<Vector3Int> DragHighlightCells => dragHighlightCells;

    private void Awake()
    {
        Instance = this;
        TutorialEvents.BoardRotated += OnBoardRotated;
        TutorialEvents.PiecePlaced  += OnPiecePlaced;
        TutorialEvents.JokerUsed    += OnJokerUsed;
        TutorialEvents.PieceHeld    += OnPieceHeld;
    }

    private void OnDestroy()
    {
        TutorialEvents.BoardRotated -= OnBoardRotated;
        TutorialEvents.PiecePlaced  -= OnPiecePlaced;
        TutorialEvents.JokerUsed    -= OnJokerUsed;
        TutorialEvents.PieceHeld    -= OnPieceHeld;
        if (Instance == this) Instance = null;
        KillLoop();
    }

    /// <summary>Seviye yüklenince LevelManager tarafından çağrılır.</summary>
    public void BeginForLevel(LevelData level)
    {
        currentLevel = level;
        dragStepCount = 0;
        StopAllCoroutines();
        KillLoop();
        steps.Clear();
        alreadyDone.Clear();
        stepIndex = -1;
        running   = false;
        HideIcon();
        HideStepText();
        HideSpotlight();

        if (level == null) return;

        // Öğretici SADECE ilk 3 seviyede çalışır — Level 4 ve sonrası için serbest oyun başlar.
        int currentLevelNum = GameManager.Instance != null ? GameManager.Instance.CurrentLevelNumber : 0;
        if (currentLevelNum > 3) return;

        if (IsLevel1)
        {
            // Level 1: Çizgi Tamamlama & Puan Kazanma Öğreticisi
            steps.Add(TutorialStepType.DragPieceToBoard);
        }
        else if (IsLevel2)
        {
            // Level 2: Undo Jokeri & Kombo Öğreticisi
            // 1. Parçayı yerleştir
            // 2. Kırmızı Joker butonuna bas (Undo)
            // 3. 3'lü parçayı yerleştir (Kombo hazırlığı)
            // 4. 1'li parçayı yerleştir (Kombo patlaması & zemin yıkımı)
            steps.Add(TutorialStepType.DragPieceToBoard);
            steps.Add(TutorialStepType.UseJoker);
            steps.Add(TutorialStepType.DragPieceToBoard);
            steps.Add(TutorialStepType.DragPieceToBoard);
        }
        else if (IsLevel3)
        {
            // Level 3: 3D Döndürme & Katman Tamamlama Öğreticisi
            // 1. H parçayı yerleştir
            // 2. Tahtayı döndür (SWIPE TO ROTATE)
            // 3. 1'li parçayı yerleştir
            // 4. Kalan 1'li parçayı yerleştir (Katmanı tamamla)
            steps.Add(TutorialStepType.DragPieceToBoard);
            steps.Add(TutorialStepType.SwipeToRotate);
            steps.Add(TutorialStepType.DragPieceToBoard);
            steps.Add(TutorialStepType.DragPieceToBoard);
        }
        else
        {
            // İlk 3 seviye dışındaki seviyelerde öğretici çalıştırılmaz
            return;
        }

        running = true;
        HighlightTutorialTargetCells(false); // Reset highlights
        Invoke(nameof(AdvanceStep), startDelay);
    }

    private void AdvanceStep()
    {
        if (!running) return;

        stepIndex++;
        if (stepIndex >= steps.Count)
        {
            running = false;
            HighlightTutorialTargetCells(false);
            HideIcon();
            HideStepText();
            HideSpotlight();
            return;
        }

        // Oyuncu bu eylemi öğretici buraya gelmeden yaptıysa adımı gösterme, atla.
        // Tekrarlanan sürükleme adımlarında erken tamamlama olamayacağı için atlama yapma.
        bool shouldSkip = alreadyDone.Contains(steps[stepIndex]) && steps[stepIndex] != TutorialStepType.DragPieceToBoard;
        if (shouldSkip)
        {
            AdvanceStep();
            return;
        }

        if (steps[stepIndex] == TutorialStepType.DragPieceToBoard ||
            steps[stepIndex] == TutorialStepType.DragPieceToHold)
        {
            if (GridManager.Instance != null && CameraOrbit.Instance != null && !CameraOrbit.Instance.IsInPanelMode)
            {
                LayerPanelController.Instance?.OpenPanel(GridManager.Instance.GridMaxY);
            }
            HighlightTutorialTargetCells(steps[stepIndex] == TutorialStepType.DragPieceToBoard);
        }
        else
        {
            HighlightTutorialTargetCells(false);
        }

        ShowStep(steps[stepIndex]);
    }

    // ─── Olaylar ──────────────────────────────────────────────────────────────

    private void OnBoardRotated() => CompleteIfCurrent(TutorialStepType.SwipeToRotate);
    private void OnPiecePlaced()
    {
        if (running && stepIndex >= 0 && stepIndex < steps.Count && steps[stepIndex] == TutorialStepType.DragPieceToBoard)
        {
            dragStepCount++;
        }
        CompleteIfCurrent(TutorialStepType.DragPieceToBoard);
    }

    private void OnJokerUsed()
    {
        if (!running || stepIndex < 0 || stepIndex >= steps.Count) return;
        if (steps[stepIndex] != TutorialStepType.UseJoker) return;

        // Öğretici amaçlı kullanım hakkı yakmasın — oyuncu asıl hatasında jokeri
        // elinde bulsun. Geri alma animasyonu bitsin diye kısa gecikme.
        Invoke(nameof(GiveJokerBack), 0.5f);
        CompleteIfCurrent(TutorialStepType.UseJoker);
    }

    private void OnPieceHeld() => CompleteIfCurrent(TutorialStepType.DragPieceToHold);

    private void GiveJokerBack() => LevelManager.Instance?.RestoreJoker();

    private void CompleteIfCurrent(TutorialStepType type)
    {
        alreadyDone.Add(type);

        if (!running || stepIndex < 0 || stepIndex >= steps.Count) return;
        if (steps[stepIndex] != type) return;

        HighlightTutorialTargetCells(false);
        KillLoop();
        HideIcon();
        HideStepText();
        HideSpotlight();
        // Eylemin kendi animasyonu (panel açılışı, tahta dönüşü, parça yerleşimi)
        // bitsin diye kısa bir nefes payı.
        Invoke(nameof(AdvanceStep), 0.6f);
    }

    public int GetTargetCardIndex()
    {
        if (LevelManager.Instance != null && LevelManager.Instance.pieceCards != null && GridManager.Instance != null)
        {
            var cards = LevelManager.Instance.pieceCards;

            // Level 2 Özel Mantık:
            // 1. Adım (dragStepCount == 0, Joker öncesi): 1'li parçayı hedefle
            // 3. Adım (dragStepCount == 1, Joker sonrası): 3'lü parçayı hedefle (kombo hazırlığı)
            // 4. Adım (dragStepCount >= 2, final): 1'li parçayı hedefle (kombo patlaması)
            if (IsLevel2)
            {
                if (dragStepCount == 1) // Joker sonrası 3'lü parça
                {
                    for (int i = 0; i < cards.Count; i++)
                    {
                        var c = cards[i];
                        if (c != null && c.HasPiece && c.Draggable != null && c.Draggable.CurrentCells.Count > 1)
                        {
                            return i;
                        }
                    }
                }
                else // 1'li parça
                {
                    for (int i = 0; i < cards.Count; i++)
                    {
                        var c = cards[i];
                        if (c != null && c.HasPiece && c.Draggable != null && c.Draggable.CurrentCells.Count == 1)
                        {
                            return i;
                        }
                    }
                }
            }

            // Level 3 Özel Mantık:
            // 1. Adımda (dragStepCount == 0): KESİNLİKLE H parçasını (hücre sayısı > 1 olan büyük parça) hedefle!
            // Sonraki adımlarda: 1x1 tekli parçaları hedefle.
            if (IsLevel3)
            {
                if (dragStepCount == 0)
                {
                    for (int i = 0; i < cards.Count; i++)
                    {
                        var c = cards[i];
                        if (c != null && c.HasPiece && c.Draggable != null && c.Draggable.CurrentCells.Count > 1)
                        {
                            return i;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < cards.Count; i++)
                    {
                        var c = cards[i];
                        if (c != null && c.HasPiece && c.Draggable != null && c.Draggable.CurrentCells.Count == 1)
                        {
                            return i;
                        }
                    }
                }
            }

            // 1. Önce aktif katmana yerleşebilen ilk kartı hedefle
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c != null && c.HasPiece && c.Draggable != null)
                {
                    var offsets = GridManager.Instance.GetPossibleOffsetsOnLayer(c.Draggable.CurrentCells, GridManager.Instance.ActiveLayerY);
                    if (offsets != null && offsets.Count > 0)
                    {
                        return i;
                    }
                }
            }

            // 2. Fallback: Dolu ilk kartı hedefle
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c != null && c.HasPiece) return i;
            }

            // 3. Fallback: Herhangi geçerli kart indeksi
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null) return i;
            }
        }
        return 0;
    }

    public void HighlightTutorialTargetCells(bool active)
    {
        if (GridManager.Instance == null) return;
        GridManager.Instance.highlightedCells.Clear();
        dragHighlightCells.Clear();

        if (active && steps != null && stepIndex >= 0 && stepIndex < steps.Count)
        {
            // Joker adımında sarı parlamayı TAMAMEN KAPA, normal grid renginde kalsın
            if (steps[stepIndex] == TutorialStepType.UseJoker || steps[stepIndex] == TutorialStepType.DragPieceToHold)
            {
                GridManager.Instance.RefreshLayerVisibility();
                return;
            }

            bool isTutorialLevel = GameManager.Instance == null || GameManager.Instance.CurrentLevelNumber <= 3;
            if (!isTutorialLevel)
            {
                GridManager.Instance.RefreshLayerVisibility();
                return;
            }

            PieceCardUI card = null;
            int targetSlot = GetTargetCardIndex();
            if (LevelManager.Instance != null && LevelManager.Instance.pieceCards != null && targetSlot < LevelManager.Instance.pieceCards.Count)
            {
                card = LevelManager.Instance.pieceCards[targetSlot];
            }

            int layerY = GridManager.Instance.ActiveLayerY;

            if (IsLevel1)
            {
                // Level 1: 1x2 sütun tamamlama
                Vector3Int c1 = new Vector3Int(0, layerY, 0);
                Vector3Int c2 = new Vector3Int(0, layerY, 1);
                GridManager.Instance.highlightedCells.Add(c1);
                GridManager.Instance.highlightedCells.Add(c2);
                dragHighlightCells.Add(c1);
                dragHighlightCells.Add(c2);
            }
            else if (IsLevel2)
            {
                if (dragStepCount == 0)
                {
                    // 1. Adım (Undo öncesi): 1'li parçayı (0,0) konumuna koy
                    Vector3Int c1 = new Vector3Int(0, layerY, 0);
                    GridManager.Instance.highlightedCells.Add(c1);
                    dragHighlightCells.Add(c1);
                }
                else if (dragStepCount == 1)
                {
                    // 3. Adım (Undo sonrası): 3'lü L parça için hedef hücreler
                    Vector3Int c1 = new Vector3Int(0, layerY, 0);
                    Vector3Int c2 = new Vector3Int(0, layerY, 1);
                    Vector3Int c3 = new Vector3Int(1, layerY, 0);
                    GridManager.Instance.highlightedCells.Add(c1);
                    GridManager.Instance.highlightedCells.Add(c2);
                    GridManager.Instance.highlightedCells.Add(c3);
                    dragHighlightCells.Add(c1);
                    dragHighlightCells.Add(c2);
                    dragHighlightCells.Add(c3);
                }
                else
                {
                    // 4. Adım: Kalan son boşluk (1x1) için hedef hücre (Kombo patlaması)
                    Vector3Int c4 = new Vector3Int(1, layerY, 1);
                    GridManager.Instance.highlightedCells.Add(c4);
                    dragHighlightCells.Add(c4);
                }
            }
            else if (IsLevel3)
            {
                if (dragStepCount == 0)
                {
                    // H parçası hücreleri (7 hücre)
                    Vector3Int[] hCells = {
                        new Vector3Int(0, layerY, 0),
                        new Vector3Int(0, layerY, 1),
                        new Vector3Int(0, layerY, 2),
                        new Vector3Int(1, layerY, 1),
                        new Vector3Int(2, layerY, 0),
                        new Vector3Int(2, layerY, 1),
                        new Vector3Int(2, layerY, 2)
                    };
                    foreach (var c in hCells)
                    {
                        GridManager.Instance.highlightedCells.Add(c);
                        dragHighlightCells.Add(c);
                    }
                }
                else if (dragStepCount == 1)
                {
                    // Döndürme sonrası 1. tekli parça
                    Vector3Int c1 = new Vector3Int(1, layerY, 0);
                    GridManager.Instance.highlightedCells.Add(c1);
                    dragHighlightCells.Add(c1);
                }
                else
                {
                    // 2. tekli parça
                    Vector3Int c2 = new Vector3Int(1, layerY, 2);
                    GridManager.Instance.highlightedCells.Add(c2);
                    dragHighlightCells.Add(c2);
                }
            }
            else if (card != null && card.Draggable != null)
            {
                var draggable = card.Draggable;
                var cells = draggable.CurrentCells;
                var offsets = GridManager.Instance.GetPossibleOffsetsOnLayer(cells, layerY);
                if (offsets != null && offsets.Count > 0)
                {
                    Vector3Int off = offsets[0];
                    foreach (var c in cells)
                    {
                        var target = c + off;
                        GridManager.Instance.highlightedCells.Add(target);
                        dragHighlightCells.Add(target);
                    }
                }
            }
        }

        GridManager.Instance.RefreshLayerVisibility();
    }

    // ─── Görsel ───────────────────────────────────────────────────────────────

    private void ShowStep(TutorialStepType type)
    {
        if (!EnsureIcon()) return;

        switch (type)
        {
            case TutorialStepType.SwipeToRotate:    PlaySwipe();      break;
            case TutorialStepType.DragPieceToBoard: PlayDrag();       break;
            case TutorialStepType.UseJoker:         PlayJoker();      break;
            case TutorialStepType.DragPieceToHold:  PlayDragToHold();  break;
        }

        ApplySpotlightFor(type);
    }

    /// <summary>Adımın hedefine göre karartmadaki deliği konumlandırır.
    /// Ölçüler cömert tutuluyor: delik hedefi sıkıştırırsa oyuncu neye
    /// bakacağını değil, neyin kesildiğini fark ediyor.</summary>
    private void ApplySpotlightFor(TutorialStepType type)
    {
        var canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect == null) return;
        float W = canvasRect.rect.width, H = canvasRect.rect.height;

        switch (type)
        {
            case TutorialStepType.SwipeToRotate:
                // Döndürülen şey tahtanın kendisi: sahne kararır, tahta parlak kalır.
                ShowSpotlight(Vector2.zero, 0f, BoardObject());
                ShowStepText(TextFor(type), new Vector2(0f, H * 0.30f), 0);
                break;

            case TutorialStepType.DragPieceToBoard:
            {
                // İki hedef birden: kartlar (UI, üste çıkarılır) ve tahtadaki hücreler
                // (3D, karartmanın üstüne alınır). Hücreler ayrıca sarı vurguyla
                // yanıp sönüyor; karartma üzerinde çok daha belirgin duruyorlar.
                var cardsPanel = canvasRect.Find("BottomPiecePanel");
                ShowSpotlight(Vector2.zero, 0f, BoardObject(), cardsPanel);
                // Kartların hemen ÜSTÜNDE: hem kaynağa hem tahtaya yakın.
                float cardsY = cardsPanel != null
                    ? (cardsPanel as RectTransform).anchoredPosition.y : -H * 0.28f;
                ShowStepText(TextFor(type), new Vector2(0f, cardsY + 200f), 0);
                break;
            }
            case TutorialStepType.UseJoker:
            {
                var jokerBtn = ControlButton.Instance;
                if (jokerBtn == null) { HideSpotlight(); return; }
                var jokerRT = jokerBtn.transform as RectTransform;
                if (jokerRT == null) { HideSpotlight(); return; }
                ShowSpotlight(Vector2.zero, 0f, jokerRT);
                ShowStepText(TextFor(type), WorldToCanvas(jokerRT.position), -1);
                break;
            }
            case TutorialStepType.DragPieceToHold:
            {
                var cardsPanel = canvasRect.Find("BottomPiecePanel");
                var holdPanel = LevelManager.Instance != null ? LevelManager.Instance.HoldSlotPanel : null;
                ShowSpotlight(Vector2.zero, 0f, cardsPanel, holdPanel != null ? holdPanel.transform : null);
                float cardsY = cardsPanel != null
                    ? (cardsPanel as RectTransform).anchoredPosition.y : -H * 0.28f;
                ShowStepText(TextFor(type), new Vector2(0f, cardsY + 200f), 0);
                break;
            }
        }
    }

    private void PlayJoker()
    {
        Vector2 pos = new Vector2(0f, -Screen.height * 0.4f);
        var jokerBtn = ControlButton.Instance;
        if (jokerBtn != null)
        {
            var jokerRT = jokerBtn.transform as RectTransform;
            if (jokerRT != null)
                pos = WorldToCanvas(jokerRT.position) + tipOffset;
        }

        PlaceIcon(pos);
        loop = DOTween.Sequence().SetLink(icon.gameObject).SetLoops(-1)
            .Append(icon.DOScale(0.75f, loopDuration * 0.35f).SetEase(Ease.OutQuad))
            .Join(iconGroup.DOFade(1f, loopDuration * 0.35f))
            .Append(icon.DOScale(1f, loopDuration * 0.4f).SetEase(Ease.OutBack))
            .AppendInterval(loopDuration * 0.25f);
    }

    private void PlayDragToHold()
    {
        int targetSlot = GetTargetCardIndex();
        Vector2 from = new Vector2(0f, -Screen.height * 0.28f);
        var cards = LevelManager.Instance != null ? LevelManager.Instance.pieceCards : null;
        if (cards != null && targetSlot < cards.Count && cards[targetSlot] != null)
        {
            var rt = cards[targetSlot].transform as RectTransform;
            if (rt != null) { from = WorldToCanvas(rt.position) + tipOffset; }
        }

        Vector2 to = new Vector2(-Screen.width * 0.35f, -Screen.height * 0.35f);
        var holdPanel = LevelManager.Instance != null ? LevelManager.Instance.HoldSlotPanel : null;
        if (holdPanel != null)
        {
            var holdRT = holdPanel.transform as RectTransform;
            if (holdRT != null)
            {
                to = WorldToCanvas(holdRT.position) + new Vector2(40f, 0f);
            }
        }

        PlaceIcon(from);
        loop = DOTween.Sequence().SetLink(icon.gameObject).SetLoops(-1)
            .Append(iconGroup.DOFade(1f, loopDuration * 0.15f))
            .Append(icon.DOAnchorPos(to, loopDuration * 0.6f).SetEase(Ease.InOutQuad))
            .Append(iconGroup.DOFade(0f, loopDuration * 0.2f))
            .AppendCallback(() => icon.anchoredPosition = from)
            .AppendInterval(loopDuration * 0.10f);
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
        int targetSlot = GetTargetCardIndex();
        Vector2 from = new Vector2(0f, -Screen.height * 0.28f);   // kart bölgesi
        var cards = LevelManager.Instance != null ? LevelManager.Instance.pieceCards : null;
        if (cards != null && targetSlot < cards.Count && cards[targetSlot] != null)
        {
            var rt = cards[targetSlot].transform as RectTransform;
            if (rt != null) { from = WorldToCanvas(rt.position) + tipOffset; }
        }

        Vector2 to = ScreenCenter() + tipOffset;

        // If we are highlighting cells, we animate the pointer to the center of the highlighted cells!
        if (GridManager.Instance != null && GridManager.Instance.highlightedCells.Count > 0)
        {
            Vector3 centerWorld = Vector3.zero;
            foreach (var cell in GridManager.Instance.highlightedCells)
            {
                centerWorld += GridManager.Instance.CellToWorld(cell);
            }
            centerWorld /= GridManager.Instance.highlightedCells.Count;
            to = WorldObjectToCanvas(centerWorld) + tipOffset;
        }

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

    // ─── Adım metni ───────────────────────────────────────────────────────────
    // Parmak "nereye" diyor ama "neden" demiyordu. Tek satırlık metin, göstergenin
    // taşıdığı bilginin çoğunu tek başına veriyor.
    private TMPro.TextMeshProUGUI stepText;
    private CanvasGroup           stepTextGroup;

    private string TextFor(TutorialStepType type)
    {
        if (IsLevel1)
        {
            return "COMPLETE THE LINE TO SCORE!";
        }
        if (IsLevel2)
        {
            if (type == TutorialStepType.UseJoker) return "TAP TO UNDO";
            if (dragStepCount == 0) return "PLACE A PIECE";
            if (dragStepCount == 1) return "PLACE TO SET UP A COMBO";
            return "COMPLETE FOR A COMBO BLAST!";
        }
        if (IsLevel3)
        {
            if (type == TutorialStepType.SwipeToRotate) return "SWIPE TO ROTATE";
            if (dragStepCount == 0) return "PLACE TO BUILD THE SHAPE";
            if (dragStepCount == 1) return "COMPLETE FOR A COMBO BLAST!";
            return "FINISH THE FLOOR!";
        }

        switch (type)
        {
            case TutorialStepType.SwipeToRotate:    return "SWIPE TO ROTATE";
            case TutorialStepType.DragPieceToBoard: return "DRAG A PIECE";
            case TutorialStepType.UseJoker:         return "TAP TO UNDO";
            case TutorialStepType.DragPieceToHold:  return "DRAG TO HOLD";
            default:                                return "";
        }
    }

    private void EnsureStepText()
    {
        if (stepText != null || canvas == null) return;

        var go = new GameObject("TutorialStepText",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup));
        go.transform.SetParent(canvas.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(700f, 120f);

        // Görünüm, tebrik yazılarıyla (NICE! / GREAT! — bkz. UIManager.PlayFloatingPraise)
        // AYNI dili konuşsun diye birebir aynı stil: kalın+italik, 58 punto, canlı renk,
        // koyu kontur. Oyuncu bu görünümü zaten "oyunun bana seslendiği an" diye tanıyor.
        stepText = go.AddComponent<TMPro.TextMeshProUGUI>();
        stepText.enableWordWrapping = false;
        stepText.alignment = TMPro.TextAlignmentOptions.Center;
        stepText.fontSize  = 58f;
        stepText.fontStyle = TMPro.FontStyles.Bold | TMPro.FontStyles.Italic;
        stepText.color     = new Color(1f, 0.85f, 0.1f);   // altın sarısı
        stepText.raycastTarget = false;
        stepText.outlineColor = new Color32(20, 20, 20, 255);
        stepText.outlineWidth = 0.25f;

        // Oyunun geri kalanıyla aynı font
        var ui = UIManager.Instance;
        if (ui != null && ui.scoreText != null) stepText.font = ui.scoreText.font;
        else if (TMPro.TMP_Settings.defaultFontAsset != null) stepText.font = TMPro.TMP_Settings.defaultFontAsset;

        stepTextGroup = go.GetComponent<CanvasGroup>();
        stepTextGroup.alpha = 0f;
        stepTextGroup.blocksRaycasts = false;
        stepTextGroup.interactable   = false;
    }

    /// <summary>Metni HEDEFİN YANINDA gösterir. anchoredPos hedefin canvas konumu,
    /// side ise metnin hangi tarafa yerleşeceği (-1 sol, +1 sağ, 0 üst).</summary>
    private void ShowStepText(string text, Vector2 anchoredPos, int side)
    {
        EnsureStepText();
        if (stepText == null) return;

        var canvasRect = canvas.transform as RectTransform;
        float W = canvasRect.rect.width, H = canvasRect.rect.height;

        // Metin genişliğini SINIRLA ve gerekince alt satıra kaydır. Aksi halde uzun
        // Türkçe cümleler tek satırda yayılıp tahtanın/katman kutusunun üstüne biniyordu
        // ("fazla içe giriyo"). Genişlik sınırı → yatay ayak izi kontrol altında.
        float maxWidth = Mathf.Min(760f, W * 0.7f);
        stepText.enableWordWrapping = true;
        stepText.rectTransform.sizeDelta = new Vector2(maxWidth, 0f);
        stepText.text = text;
        stepText.gameObject.SetActive(true);
        stepText.ForceMeshUpdate();

        float halfW = Mathf.Min(maxWidth, stepText.preferredWidth) * 0.5f;
        float halfH = stepText.preferredHeight * 0.5f;

        // Metin hedefe DEĞMESİN: sabit bir boşluk (clearance) bırakılır. 60 → 90:
        // yazı butonların "çok dibinde" duruyordu.
        const float gap = 90f;
        Vector2 pos = anchoredPos;
        if (side < 0)      pos.x -= halfW + gap;
        else if (side > 0) pos.x += halfW + gap;
        else               pos.y += halfH + gap;

        // İKİ EKSENDE de ekran içinde, kenardan güvenli marj bırakarak tut. Önceden
        // yalnızca yatay taşma kontrol ediliyordu; dikeyde yazı üstten/alttan taşıp
        // butonun/katman kutusunun içine girebiliyordu.
        const float margin = 28f;
        float limX = W * 0.5f - halfW - margin;
        float limY = H * 0.5f - halfH - margin;
        pos.x = Mathf.Clamp(pos.x, -limX, limX);
        pos.y = Mathf.Clamp(pos.y, -limY, limY);

        var rt = stepText.rectTransform;
        rt.anchoredPosition = pos;
        rt.SetAsLastSibling();

        // Tebrik yazılarındaki "fırlayıp yaylanarak oturma" hissi.
        stepTextGroup.DOKill();
        rt.DOKill();
        stepTextGroup.alpha = 0f;
        rt.localScale = Vector3.zero;
        DOTween.Sequence().SetLink(stepText.gameObject)
            .Append(rt.DOScale(1.25f, 0.20f).SetEase(Ease.OutQuad))
            .Join(stepTextGroup.DOFade(1f, 0.20f))
            .Append(rt.DOScale(1f, 0.14f).SetEase(Ease.OutBack))
            // Sonra hafifçe nabız atarak dikkat çekmeye devam etsin
            .Append(rt.DOScale(1.06f, 0.55f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo));
    }

    private void HideStepText()
    {
        if (stepText == null) return;
        stepTextGroup.DOKill();
        stepText.gameObject.SetActive(false);
    }

    // ─── Karartma (spotlight) ─────────────────────────────────────────────────
    // Hedef dışındaki her yer karartılır. Gerçek bir "delik" için shader/maske
    // yerine DÖRT panel kullanılıyor: hedef dikdörtgenin üstü/altı/solu/sağı.
    // Böylece hedef bölge gerçekten karartılmamış kalıyor ve hem UI hem 3D
    // hedeflerde çalışıyor.
    private Image        dimPanel;      // tek parça, tam ekran
    private Image        glowImage;     // 3D hedefler için yumuşak yuvarlak hale
    private CanvasGroup  dimGroup;
    private static Sprite glowSprite;

    // Karartmanın üstüne çıkarılan UI hedeflerinin ESKİ sıraları — adım bitince
    // yerlerine geri konmaları için.
    private readonly List<(Transform tr, int index)> liftedTargets = new List<(Transform, int)>();

    // ─── 3D sahne karartması ──────────────────────────────────────────────────
    // UI karartması (ekran-üstü canvas) 3D nesneleri KAÇINILMAZ olarak örter, bu
    // yüzden "sadece şu küp parlasın" UI ile yapılamıyordu. Çözüm iki parçalı:
    //   1) Kameranın önüne, derinliğe YAZMAYAN ve her şeyin üstüne çizilen koyu
    //      bir düzlem konur → tüm 3D sahne kararır.
    //   2) Hedef nesnelerin materyalleri bu düzlemden SONRA çizilecek render
    //      sırasına alınır → sadece onlar karartmanın üzerinde parlak kalır.
    private const int DIM3D_QUEUE   = 3900;
    private const int TARGET3D_QUEUE = 3950;

    private GameObject dim3D;
    private readonly List<(Renderer r, Material[] mats)> lifted3D = new List<(Renderer, Material[])>();

    private void Ensure3DDim()
    {
        if (dim3D != null) return;
        var cam = Camera.main;
        if (cam == null) return;

        dim3D = GameObject.CreatePrimitive(PrimitiveType.Quad);
        dim3D.name = "Tutorial3DDim";
        Destroy(dim3D.GetComponent<Collider>());
        dim3D.transform.SetParent(cam.transform, false);

        // Kameranın hemen önünde, görüş alanını tamamen kaplayacak boyutta.
        float dist = cam.nearClipPlane + 0.05f;
        dim3D.transform.localPosition = new Vector3(0f, 0f, dist);
        dim3D.transform.localRotation = Quaternion.identity;
        float h = cam.orthographic ? cam.orthographicSize * 2f
                                   : 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        dim3D.transform.localScale = new Vector3(h * cam.aspect * 1.2f, h * 1.2f, 1f);

        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        var mat = new Material(sh);
        mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.72f));
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0f, 0f, 0f, 0.72f));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always); // her şeyin üstüne
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = DIM3D_QUEUE;

        var rend = dim3D.GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        dim3D.SetActive(false);
    }

    /// <summary>Bir 3D nesneyi karartmanın ÜSTÜNE çıkarır: materyalleri örneklenip
    /// render sırası karartma düzleminin arkasına alınır. Materyal örneği alınıyor
    /// çünkü render queue paylaşımlı materyalde değiştirilirse aynı materyali
    /// kullanan TÜM nesneler etkilenirdi.</summary>
    private void Lift3D(GameObject target)
    {
        if (target == null) return;
        foreach (var r in target.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            lifted3D.Add((r, r.sharedMaterials));
            var copies = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < copies.Length; i++)
            {
                if (r.sharedMaterials[i] == null) continue;
                copies[i] = new Material(r.sharedMaterials[i]) { renderQueue = TARGET3D_QUEUE };
                copies[i].SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            }
            r.materials = copies;
        }
    }

    // Sürüklenen parça tahtanın çocuğu DEĞİL, ayrı bir dünya nesnesi — 3D karartma
    // açıkken kararırdı. Sürükleme adımı boyunca elde tutulan/sürüklenen parça da
    // öne alınır. Parça değiştikçe (yeni parça çekildikçe) takip edilir.
    private GameObject liftedPiece;

    private void LateUpdate()
    {
        if (dim3D == null || !dim3D.activeSelf) { liftedPiece = null; return; }
        if (!IsDragStepActive) return;

        var drag = DraggablePiece.activeDrag;
        var current = drag != null ? drag.gameObject : null;
        if (current != liftedPiece)
        {
            liftedPiece = current;
            if (current != null) Lift3D(current);
        }
    }

    /// <summary>Oyun tahtası (Main_Shape). Hücreler onun çocukları olduğu için
    /// tahtayı öne almak bütün hücreleri öne alır.</summary>
    private GameObject BoardObject()
    {
        if (LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null)
            return LevelManager.Instance.ActiveMainPiece;
        return GameObject.Find("Main_Shape");
    }

    private void Restore3D()
    {
        for (int i = lifted3D.Count - 1; i >= 0; i--)
        {
            var (r, mats) = lifted3D[i];
            if (r != null) r.sharedMaterials = mats;
        }
        lifted3D.Clear();
        if (dim3D != null) dim3D.SetActive(false);
    }

    /// <summary>Kenarları eriyen yuvarlak hale dokusu. Dosya bağımlılığı olmasın
    /// diye çalışma zamanında üretiliyor.</summary>
    private static Sprite GetGlowSprite()
    {
        if (glowSprite != null) return glowSprite;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S];
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                a = a * a;                       // kenarlara doğru hızlı sönüm
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px); tex.Apply();
        glowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        return glowSprite;
    }

    private void EnsureDim()
    {
        if (dimPanel != null || canvas == null) return;

        var host = new GameObject("TutorialDim", typeof(RectTransform), typeof(CanvasGroup));
        host.transform.SetParent(canvas.transform, false);
        var hrt = host.GetComponent<RectTransform>();
        hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
        hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;

        dimGroup = host.GetComponent<CanvasGroup>();
        dimGroup.alpha = 0f;
        // Karartma tıklamayı ENGELLEMEZ: öğretici oyuncuyu kilitlemiyor.
        dimGroup.blocksRaycasts = false;
        dimGroup.interactable   = false;

        var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dimGo.transform.SetParent(host.transform, false);
        dimPanel = dimGo.GetComponent<Image>();
        dimPanel.color = new Color(0f, 0f, 0f, 0.66f);
        dimPanel.raycastTarget = false;
        var drt = dimGo.GetComponent<RectTransform>();
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
        drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;

        var glowGo = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        glowGo.transform.SetParent(host.transform, false);
        glowImage = glowGo.GetComponent<Image>();
        glowImage.sprite = GetGlowSprite();
        glowImage.raycastTarget = false;
        glowImage.color = new Color(1f, 1f, 0.85f, 0.30f);
        var grt = glowGo.GetComponent<RectTransform>();
        grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
        grt.pivot = new Vector2(0.5f, 0.5f);
        glowGo.SetActive(false);
    }

    /// <summary>Tam ekran karartma açar. UI hedefleri karartmanın ÜSTÜNE çıkarılır
    /// (gerçekten parlak kalırlar). 3D hedefler için — ekran-üstü bir UI katmanı
    /// onları kaçınılmaz olarak örttüğü için — konumlarına yumuşak bir hale konur.</summary>
    private void ShowSpotlight(Vector2 glowCenter, float glowSize, params Transform[] uiTargets)
    {
        ShowSpotlight(glowCenter, glowSize, null, uiTargets);
    }

    /// <summary>target3D verilirse 3D sahne de kararır ve YALNIZCA o nesne parlak kalır.</summary>
    private void ShowSpotlight(Vector2 glowCenter, float glowSize, GameObject target3D, params Transform[] uiTargets)
    {
        EnsureDim();
        if (dimPanel == null) return;

        RestoreLifted();
        Restore3D();

        // 3D hedef varsa karartmayı SADECE 3D düzlem yapar.
        //
        // Sebebi: UI karartması ekran-üstü canvas'ta ve HER ŞEYİN üstüne çizilir —
        // 3D nesneyi render sırasıyla ne kadar öne alırsak alalım UI karartması onu
        // yine örter. İkisi birlikte açıkken tahta karanlık kalıyordu. 3D düzlem
        // kameranın önünde durduğu için sahneyi zaten karartıyor; öne alınan nesne
        // ondan sonra çizildiği için parlak kalıyor.
        bool has3D = target3D != null;
        if (has3D)
        {
            Ensure3DDim();
            if (dim3D != null) dim3D.SetActive(true);
            Lift3D(target3D);
        }
        dimPanel.enabled = !has3D;

        dimGroup.transform.SetAsLastSibling();
        dimGroup.gameObject.SetActive(true);
        dimGroup.DOKill();
        dimGroup.DOFade(1f, 0.25f).SetLink(dimGroup.gameObject);

        if (glowSize > 0f)
        {
            glowImage.gameObject.SetActive(true);
            glowImage.rectTransform.anchoredPosition = glowCenter;
            glowImage.rectTransform.sizeDelta = Vector2.one * glowSize;
        }
        else glowImage.gameObject.SetActive(false);

        // UI hedeflerini karartmanın üstüne al (eski sıralarını saklayarak).
        if (uiTargets != null)
        {
            foreach (var t in uiTargets)
            {
                if (t == null || t.parent != canvas.transform) continue;
                liftedTargets.Add((t, t.GetSiblingIndex()));
                t.SetAsLastSibling();
            }
        }

        // Parmak ve metin her zaman en üstte.
        if (icon != null)     icon.SetAsLastSibling();
        if (stepText != null) stepText.transform.SetAsLastSibling();
    }

    private void RestoreLifted()
    {
        for (int i = liftedTargets.Count - 1; i >= 0; i--)
        {
            var (tr, idx) = liftedTargets[i];
            if (tr != null) tr.SetSiblingIndex(idx);
        }
        liftedTargets.Clear();
    }

    private void HideSpotlight()
    {
        RestoreLifted();
        Restore3D();
        if (dimGroup == null) return;
        dimGroup.DOKill();
        dimGroup.gameObject.SetActive(false);
        dimGroup.alpha = 0f;
        if (dimPanel != null) dimPanel.enabled = true;   // sonraki adım için varsayılan
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
