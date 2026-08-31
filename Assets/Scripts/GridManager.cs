using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DG.Tweening;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    public GridState State { get; private set; } = new GridState();

    public HashSet<Vector3Int> targetCells   = new HashSet<Vector3Int>();
    public HashSet<Vector3Int> allShapeCells = new HashSet<Vector3Int>();
    public HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
    public HashSet<Vector3Int> frozenCells   = new HashSet<Vector3Int>();
    private Dictionary<Vector3Int, GameObject> cellObjects  = new Dictionary<Vector3Int, GameObject>();
    private Dictionary<Vector3Int, Color>       cellColors   = new Dictionary<Vector3Int, Color>();
    private Dictionary<Vector3Int, int>         cellMatIndex = new Dictionary<Vector3Int, int>(); // -1 = unknown
    private Dictionary<Vector3Int, Renderer>    targetRenderers = new Dictionary<Vector3Int, Renderer>();
    private Dictionary<Vector3Int, Renderer>    prefilledRenderers = new Dictionary<Vector3Int, Renderer>(); // Prefilled blokların renderer'ları
    private HashSet<Vector3Int> temporarilyHiddenGridCells = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> occludedGridCells           = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> sparklingCells               = new HashSet<Vector3Int>(); // Aynı türden 3+ bağlı grup parıldıyor

    // Erime animasyonu SÜREN buz hücreleri. RefreshLayerVisibility bunlara dokunmaz:
    // erime efekti renderer.material üzerinden çalışırken, tazeleme MaterialPropertyBlock
    // ile aynı renderer'ın rengini EZİYOR ve buz gri/tuhaf bir renge bürünüyordu.
    private readonly HashSet<Vector3Int> meltingIceCells = new HashSet<Vector3Int>();


    // ─── Buz görseli ──────────────────────────────────────────────────────────
    // Buz artık hedef küpünün boyanmış hali değil, kendi 3D modeli
    // (Assets/Resources/IceCube.prefab — Ice.fbx'ten üretildi, saydam materyal,
    // ışık/collider/gölge çıkarıldı). Model hücre küpüne çocuk olarak eklenir,
    // küpün kendi renderer'ı kapatılır.
    private readonly Dictionary<Vector3Int, GameObject> iceVisuals = new Dictionary<Vector3Int, GameObject>();
    private static GameObject icePrefabCache;

    // Buz hücresi başına kalan vuruş sayısı (bkz. CubeShapeDataHolder.GetFrozenHitCount /
    // IceVisualMarker). Değer 0'a inince buz gerçekten erir; ara vuruşlarda IceBreakEffect.PlayIceChip
    // oynar ve buz frozenCells'te kalmaya devam eder (bkz. CheckAndResolveFrozenCells).
    private Dictionary<Vector3Int, int> iceRemainingHits = new Dictionary<Vector3Int, int>();
    private Dictionary<Vector3Int, int> cellRemainingHits = new Dictionary<Vector3Int, int>();

    private static GameObject IcePrefab
    {
        get
        {
            if (icePrefabCache == null) icePrefabCache = Resources.Load<GameObject>("IceCube");
            return icePrefabCache;
        }
    }

    /// <summary>Hücrede buz modeli yoksa oluşturur. Modelin pivotu MERKEZDE ve hücre
    /// küpünün pivotu da hücre merkezinde olduğu için ofset gerekmiyor.</summary>
    private void EnsureIceVisual(Vector3Int cell, Renderer host)
    {
        if (host == null) return;
        if (iceVisuals.TryGetValue(cell, out var existing) && existing != null) return;

        var prefab = IcePrefab;
        if (prefab == null) return;

        var go = Instantiate(prefab, host.transform);
        go.name = $"Ice_{cell.x}_{cell.y}_{cell.z}";
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        // DÜZELTME: ApplyTargetGhost (LevelManager.cs) fonksiyonunun buzun kendi materyalini
        // yarı saydam yeşil ghost materyaliyle ezmesini önlemek için IceVisualMarker'ı dinamik olarak ekliyoruz.
        var marker = go.GetComponent<IceVisualMarker>();
        if (marker == null)
        {
            marker = go.AddComponent<IceVisualMarker>();
        }
        int hitsRequired = iceRemainingHits.TryGetValue(cell, out int h) ? h : 1;
        marker.Initialize(hitsRequired);

        iceVisuals[cell] = go;
    }

    /// <summary>Erime SÜRERKEN çağrılırsa modeli silmez — erime efekti onun
    /// materyali üzerinde çalışıyor, ortadan kaldırmak efekti ve callback'ini
    /// öldürürdü (bkz. meltingIceCells).</summary>
    private void RemoveIceVisual(Vector3Int cell)
    {
        if (meltingIceCells.Contains(cell)) return;
        ForceRemoveIceVisual(cell);
    }

    private void ForceRemoveIceVisual(Vector3Int cell)
    {
        if (iceVisuals.TryGetValue(cell, out var go))
        {
            if (go != null) Destroy(go);
            iceVisuals.Remove(cell);
        }
    }

    private GameObject GetIceVisual(Vector3Int cell)
    {
        iceVisuals.TryGetValue(cell, out var go);
        return go;
    }

    private void ClearAllIceVisuals()
    {
        foreach (var kv in iceVisuals) if (kv.Value != null) Destroy(kv.Value);
        iceVisuals.Clear();
        iceRemainingHits.Clear();
    }

    public float  CellSize { get; private set; }
    public float  Spacing  { get; private set; }
    public float  Step     => CellSize + Spacing;
    public Vector3 Origin  { get; private set; }

    public bool GetCellColor(Vector3Int cell, out Color color)
    {
        return cellColors.TryGetValue(cell, out color);
    }

    // Hücre, seviye başında hazır (prefilled) bir engel mi, yoksa oyuncu tarafından mı yerleştirildi?
    public bool IsCellPrefilled(Vector3Int cell) => prefilledRenderers.ContainsKey(cell);

    public bool IsLayerCleared(int y) => false; // Dinamik daralan sistemde katman tamamlandığında silinir, "temizlenmiş katman" kalmaz.

    public int TotalCells   => targetCells.Count;
    public int PlacedCells  => occupiedCells.Count;
    public IEnumerable<Vector3Int> TargetCells => targetCells;

    public bool lineClearEnabled = true;

    private int gridMinX, gridMaxX, gridMinY, gridMaxY, gridMinZ, gridMaxZ;

    private void Awake()
    {
        Instance = this;
        hideLowerLayerEmptyGrid = true;
        hideLowerLayerPlacedBlocks = true;
        lowerLayerBlockAlpha = 0f;
    }

    public HashSet<Vector3Int> highlightedCells = new HashSet<Vector3Int>();

    /// <summary>Bir önceki karede vurgulanan hücreler. Vurgudan ÇIKAN hücrenin sarı
    /// damgasının geri alınabilmesi için tutulur (bkz. Update).</summary>
    private readonly HashSet<Vector3Int> lastHighlightedCells = new HashSet<Vector3Int>();

    private void Update()
    {
        // Grid hücre parlatma (sarı yerleştirme vurgusu) sadece ilk 3 tutorial seviyesinde aktiftir
        bool isTutorialLevel = GameManager.Instance == null || GameManager.Instance.CurrentLevelNumber <= 3;

        // 1. Dinamik parlatma güncellemesi (sürüklenen parça varsa)
        if (DraggablePiece.activeDrag != null && isTutorialLevel)
        {
            highlightedCells.Clear();
            var drag = DraggablePiece.activeDrag;
            if (drag.IsBeingDragged && !drag.IsPlaced)
            {
                var tut = TutorialOverlay.Instance;

                if (tut != null && tut.IsRunning && tut.CurrentStep == TutorialStepType.DragPieceToHold)
                {
                    highlightedCells.Clear();
                }
                else if (tut != null && tut.RestrictDragHighlights && tut.DragHighlightCells.Count > 0)
                {
                    foreach (var c in tut.DragHighlightCells)
                    {
                        highlightedCells.Add(c);
                    }
                }
                else
                {
                    var cells = drag.CurrentCells;
                    var offsets = GetPossibleOffsetsOnLayer(cells, ActiveLayerY);
                    foreach (var off in offsets)
                    {
                        foreach (var c in cells)
                        {
                            highlightedCells.Add(c + off);
                        }
                    }
                }
            }
        }
        else
        {
            // Sürüklenen parça yoksa veya tutorial seviyesi değilse temizle
            bool tutorialHighlighting = false;
            if (isTutorialLevel && TutorialOverlay.Instance != null && TutorialOverlay.Instance.IsDragStepActive)
            {
                tutorialHighlighting = true;
            }
            
            if (!tutorialHighlighting)
            {
                highlightedCells.Clear();
            }
        }

        // 1b. Vurgulaması BİTEN hücrelerin rengini geri al.
        // Vurgulama, MaterialPropertyBlock ile sarı bir damga basıyor ama küme
        // boşalınca damgayı KALDIRMIYORDU; hücre, başka bir sistem rengini yeniden
        // yazana kadar sarı kalıyordu (parça yerleştirildikten sonra tahtada sarı
        // kalan hücrelerin sebebi buydu). Damgayı basan taraf geri almaktan da
        // sorumlu: bir hücre vurgudan çıktığı anda görünürlük tazelemesi çağrılıp
        // hücrenin gerçek rengi geri yazılır.
        bool highlightEnded = false;
        foreach (var c in lastHighlightedCells)
        {
            if (!highlightedCells.Contains(c)) { highlightEnded = true; break; }
        }
        if (highlightEnded) RefreshLayerVisibility();

        lastHighlightedCells.Clear();
        foreach (var c in highlightedCells) lastHighlightedCells.Add(c);

        // 2. Parıldayan ipucu hücrelerinin animasyonu (yanıp sönme & güçlü altın neon parlama)
        if (highlightedCells.Count > 0)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.5f);
            Color highlightColor = Color.Lerp(new Color(1.0f, 0.88f, 0.20f, 0.22f), new Color(1.0f, 0.88f, 0.20f, 0.42f), pulse);
            Color emissionColor = new Color(0.15f, 0.12f, 0.02f) * (0.5f + 0.5f * pulse);

            Material hintMat = GetOrCreateHintTransparentMaterial();

            foreach (var cell in highlightedCells)
            {
                Renderer r = null;
                if (targetRenderers.TryGetValue(cell, out var r1) && r1 != null) r = r1;
                else if (prefilledRenderers.TryGetValue(cell, out var r2) && r2 != null) r = r2;
                else if (cellObjects.TryGetValue(cell, out var go) && go != null) r = go.GetComponent<Renderer>();

                if (r != null && r.enabled)
                {
                    if (r.sharedMaterial != hintMat) r.sharedMaterial = hintMat;
                    r.GetPropertyBlock(PropBlock);
                    PropBlock.SetColor("_BaseColor", highlightColor);
                    PropBlock.SetColor("_Color", highlightColor);
                    PropBlock.SetColor("_EmissionColor", emissionColor);
                    r.SetPropertyBlock(PropBlock);
                }
            }
        }
    }

    public int ActiveLayerY { get; private set; }
    public bool IsExplodingLayer { get; set; }

    [Header("Layer Visualization Settings")]
    [Tooltip("Alt katmanlardaki boş hedef (ghost) grid küplerini gizler.")]
    public bool hideLowerLayerEmptyGrid = true;

    [Tooltip("Alt katmanlardaki dizilmiş/dolu blokları tamamen gizler (Sadece aktif katman görünür).")]
    public bool hideLowerLayerPlacedBlocks = true;

    [Tooltip("Alt katmandaki dolu blokların saydamlığı (0 = tamamen gizli, 1 = tam opak).")]
    [Range(0f, 1f)]
    public float lowerLayerBlockAlpha = 0.25f;

    [Tooltip("Alt katmandaki dolu blokları karartma/gölge faktörü (0 = kendi rengi, 1 = tamamen siyah silüet).")]
    [Range(0f, 1f)]
    public float lowerLayerDarkenFactor = 0.75f;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying && Instance == this)
        {
            RefreshLayerVisibility();
        }
    }
#endif

    public void SetActiveLayer(int y)
    {
        ActiveLayerY = Mathf.Clamp(y, gridMinY, gridMaxY);
        lineClearEnabled = false;
        RefreshLayerVisibility();
        TowerMiniPreview.Instance?.SetActiveFloor(ActiveLayerY);
    }

    public int GridMinY => gridMinY;
    public int GridMaxY => gridMaxY;

    public void Initialize(GameObject mainShape, float cellSize, float spacing, Vector3 origin)
    {
        CellSize = cellSize;
        Spacing  = spacing;
        Origin   = origin;
        IsExplodingLayer = false;
        occupiedCells.Clear();
        targetCells.Clear();
        allShapeCells.Clear();
        cellMatIndex.Clear();
        frozenCells.Clear();
        cellRemainingHits.Clear();
        ClearAllCellObjects();

        targetRenderers.Clear();
        prefilledRenderers.Clear();

        var shapeHolder = mainShape != null ? mainShape.GetComponent<CubeShapeDataHolder>() : null;

        if (mainShape != null)
        {
            // Mantıksal grid'i yalnızca görünür Renderer'lardan kurarsak,
            // panelde gizli olan üst katmanlar hiç kaydedilmez.
            // Bu yüzden bütün hücreleri doğrudan CubeShapeDataHolder'dan alıyoruz.
            bool loadedLogicalCellsFromHolder =
                shapeHolder != null &&
                shapeHolder.occupiedCells != null &&
                shapeHolder.occupiedCells.Count > 0;

            if (loadedLogicalCellsFromHolder)
            {
                foreach (var cell in shapeHolder.occupiedCells)
                {
                    allShapeCells.Add(cell);

                    if (shapeHolder.IsCellPrefilled(cell))
                        occupiedCells.Add(cell);

                    targetCells.Add(cell);
                }
            }

            // Hücre -> Renderer eşlemesi YAPIDAN kurulur (şeklin her doğrudan çocuğu bir
            // hücredir; küp prefabı kullanılıyorsa Renderer o çocuğun altında olabilir),
            // hücre koordinatı da o çocuğun KONUMUNDAN. Obje ismi ("Cube_x_y_z" /
            // "Prefilled_matIdx_x_y_z") artık yalnızca holder verisi olmayan eski
            // prefab'lar için fallback'tir — yeniden adlandırma seviyeyi bozmaz.
            float step = shapeHolder != null ? shapeHolder.Step : cellSize + spacing;
            if (step <= 0.0001f) step = 1f;

            foreach (Transform cellRoot in mainShape.transform)
            {
                // true: Kapalı/disabled Renderer'ları da bul.
                var r = cellRoot.GetComponentInChildren<Renderer>(true);
                if (r == null) continue;

                string name = cellRoot.gameObject.name;
                bool nameIsCube      = name.StartsWith("Cube_");
                bool nameIsPrefilled = name.StartsWith("Prefilled_");

                Vector3Int cell;
                int  holderMatIdx = -1;
                bool isPrefilled;

                if (shapeHolder != null)
                {
                    cell = shapeHolder.WorldToCell(cellRoot.position);
                    // Holder'ın tanımadığı çocuklar (dekor, efekt...) hücre değildir.
                    // Holder'da hücre listesi hiç yoksa eski isim filtresine düşülür.
                    if (loadedLogicalCellsFromHolder)
                    {
                        if (!allShapeCells.Contains(cell))
                        {
                            // Renderer'ı olan ama holder'ın tanımadığı bir çocuk: ya dekor/efekt
                            // (zararsız), ya da cellSize/spacing ile çocuk konumları tutmuyor
                            // (seviye görünmez olur). Sessizce yutmak yerine uyarıyoruz.
                            Debug.LogWarning($"[GridManager] '{cellRoot.name}' -> hücre {cell} " +
                                $"CubeShapeDataHolder.occupiedCells içinde yok, atlanıyor. " +
                                $"(step={step}) Şekil: {mainShape.name}", cellRoot);
                            continue;
                        }
                    }
                    else if (!nameIsCube && !nameIsPrefilled) continue;

                    // Holder prefilled listesi boş olan eski prefab'larda isim hâlâ geçerli kaynak.
                    isPrefilled = shapeHolder.TryGetPrefilledInfo(cell, out holderMatIdx, out _)
                                  || nameIsPrefilled;
                }
                else
                {
                    if (!nameIsCube && !nameIsPrefilled) continue;
                    Vector3 localPos = mainShape.transform.InverseTransformPoint(cellRoot.position);
                    cell = new Vector3Int(
                        Mathf.RoundToInt(localPos.x / step),
                        Mathf.RoundToInt(localPos.y / step),
                        Mathf.RoundToInt(localPos.z / step));
                    isPrefilled = nameIsPrefilled;
                }

                // Holder verisi yoksa eski Renderer tabanlı sistemi fallback olarak kullan.
                if (!loadedLogicalCellsFromHolder)
                {
                    allShapeCells.Add(cell);
                    targetCells.Add(cell);
                }

                if (isPrefilled)
                {
                    occupiedCells.Add(cell);
                    prefilledRenderers[cell] = r;

                    if (r.sharedMaterial != null)
                        cellColors[cell] = GetMaterialColor(r.sharedMaterial);

                    if (holderMatIdx >= 0)
                    {
                        cellMatIndex[cell] = holderMatIdx;
                    }
                    else
                    {
                        // Eski veri: tür indeksi "Prefilled_matIdx_x_y_z" isminde kodlanmış.
                        var parts = name.Split('_');
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedIdx))
                            cellMatIndex[cell] = parsedIdx;
                    }
                }
                else
                {
                    targetRenderers[cell] = r;
                }
            }

            // Emniyet Koruması (Phantom Cell Sanitization):
            // Eğer CubeShapeDataHolder.occupiedCells içinde tanımlı ama sahnede / prefabda
            // fiziksel Transform/Renderer'ı olmayan hayalet hücreler kalmışsa (ör. eski editör kalıntısı),
            // bunların oyuncu tarafından sahte bir grid alanıymış gibi kullanılması engellenir.
            if (loadedLogicalCellsFromHolder)
            {
                var phantomCells = new List<Vector3Int>();
                foreach (var c in allShapeCells)
                {
                    if (!targetRenderers.ContainsKey(c) && !prefilledRenderers.ContainsKey(c))
                    {
                        phantomCells.Add(c);
                    }
                }
                foreach (var c in phantomCells)
                {
                    allShapeCells.Remove(c);
                    targetCells.Remove(c);
                    occupiedCells.Remove(c);
                    frozenCells.Remove(c);
                    cellMatIndex.Remove(c);
                    cellColors.Remove(c);
                    cellRemainingHits.Remove(c);
                    Debug.LogWarning($"[GridManager] Phantom cell {c} kaldırıldı — '{mainShape.name}' prefabında karşılık gelen GameObject/Renderer bulunamadı.");
                }
            }
        }

        if (allShapeCells.Count > 0)
        {
            gridMinX = allShapeCells.Min(c => c.x);
            gridMaxX = allShapeCells.Max(c => c.x);
            gridMinY = allShapeCells.Min(c => c.y);
            gridMaxY = allShapeCells.Max(c => c.y);
            gridMinZ = allShapeCells.Min(c => c.z);
            gridMaxZ = allShapeCells.Max(c => c.z);
        }
        else if (shapeHolder != null && shapeHolder.gridSize != Vector3Int.zero)
        {
            gridMinX = 0;
            gridMaxX = shapeHolder.gridSize.x - 1;
            gridMinY = 0;
            gridMaxY = shapeHolder.gridSize.y - 1;
            gridMinZ = 0;
            gridMaxZ = shapeHolder.gridSize.z - 1;
        }
        else
        {
            gridMinX = gridMinY = gridMinZ = 0;
            gridMaxX = gridMaxY = gridMaxZ = 0;
        }

        // Buz hücreleri yalnızca seviyenin kendi CubeShapeDataHolder.frozenCells listesinden
        // yüklenir. Liste boşsa seviyede hiç buz yoktur — rastgele/otomatik buzlama YAPILMAZ
        // (önceden burada boş listeyi "tanımlanmamış" sayıp katman başına rastgele %25 hücreyi
        // buzlayan bir fallback vardı; bu, buzsuz tasarlanan seviyeleri de oynanamaz hale
        // getirebiliyordu — özellikle tek parçanın tüm tahtayı kapladığı küçük seviyelerde).
        iceRemainingHits.Clear();
        if (shapeHolder != null && shapeHolder.frozenCells != null)
        {
            foreach (var cell in shapeHolder.frozenCells)
            {
                frozenCells.Add(cell);
                iceRemainingHits[cell] = 1;
            }
        }

        SyncGridState();

        // Katmanları tek tek sırayla göster (0'dan başlayarak)
        ActiveLayerY = gridMinY;
        lineClearEnabled = false;
        RefreshLayerVisibility();
    }

    public void SyncGridState()
    {
        if (State == null) State = new GridState();
        State.Clear();
        State.CellSize = CellSize;
        State.Spacing = Spacing;
        State.Origin = Origin;
        State.SetBounds(gridMinX, gridMaxX, gridMinY, gridMaxY, gridMinZ, gridMaxZ);

        foreach (var c in targetCells) State.TargetCells.Add(c);
        foreach (var c in allShapeCells) State.AllShapeCells.Add(c);
        foreach (var c in occupiedCells) State.OccupiedCells.Add(c);
        foreach (var c in frozenCells) State.FrozenCells.Add(c);
        foreach (var kv in prefilledRenderers) State.PrefilledCells.Add(kv.Key);
        foreach (var kv in cellColors) State.CellColors[kv.Key] = kv.Value;
        foreach (var kv in cellMatIndex) State.CellMatIndex[kv.Key] = kv.Value;
        foreach (var kv in iceRemainingHits) State.IceRemainingHits[kv.Key] = kv.Value;
    }

    private static int ParseCoordinate(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        string clean = "";
        foreach (char ch in s)
        {
            if (char.IsDigit(ch) || ch == '-') clean += ch;
            else break;
        }
        if (int.TryParse(clean, out int val)) return val;
        return 0;
    }

    private static MaterialPropertyBlock _propBlock;
    private static MaterialPropertyBlock PropBlock => _propBlock ??= new MaterialPropertyBlock();

    public void RefreshLayerVisibility()
    {
        bool applyScale = !IsExplodingLayer;

        // Hedef (ghost) renderer'ları kontrol et - Sadece o anki aktif katman görünür (tek tek katman)
        foreach (var kvp in targetRenderers)
        {
            Vector3Int cell = kvp.Key;
            Renderer r = kvp.Value;
            if (r == null) continue;
            if (meltingIceCells.Contains(cell)) continue;

            if (cell.y == ActiveLayerY)
            {
                // O anki aktif katman: açık hedefleri göster, dolu olanları gizle
                r.enabled = !occupiedCells.Contains(cell);
                if (applyScale) r.transform.localScale = Vector3.one * CellSize;

                var col = r.GetComponent<Collider>();
                if (col != null) col.enabled = !occupiedCells.Contains(cell);

                // Buz 3D modeli
                bool isFrozenHere = frozenCells.Contains(cell);
                if (isFrozenHere)
                {
                    EnsureIceVisual(cell, r);
                    var iceGo = GetIceVisual(cell);
                    if (iceGo != null)
                    {
                        iceGo.SetActive(true);
                        r.enabled = false;
                    }
                }
                else
                {
                    RemoveIceVisual(cell);
                }

                if (r.enabled)
                {
                    r.GetPropertyBlock(PropBlock);
                    if (highlightedCells.Contains(cell))
                    {
                        Material hintMat = GetOrCreateHintTransparentMaterial();
                        if (r.sharedMaterial != hintMat) r.sharedMaterial = hintMat;

                        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.5f);
                        Color highlightColor = Color.Lerp(new Color(1.0f, 0.88f, 0.20f, 0.22f), new Color(1.0f, 0.88f, 0.20f, 0.42f), pulse);
                        Color emissionColor = new Color(0.15f, 0.12f, 0.02f) * (0.5f + 0.5f * pulse);

                        PropBlock.SetColor("_BaseColor", highlightColor);
                        PropBlock.SetColor("_Color", highlightColor);
                        PropBlock.SetColor("_EmissionColor", emissionColor);
                    }
                    else
                    {
                        if (LevelManager.Instance != null && LevelManager.Instance.ghostTargetMaterial != null)
                        {
                            if (r.sharedMaterial != LevelManager.Instance.ghostTargetMaterial)
                                r.sharedMaterial = LevelManager.Instance.ghostTargetMaterial;
                        }

                        Color defaultColor = LevelManager.Instance != null && LevelManager.Instance.ghostTargetMaterial != null
                            ? LevelManager.Instance.ghostTargetMaterial.color
                            : new Color(0.41f, 0.57f, 0.35f, 0.53f);

                        PropBlock.SetColor("_BaseColor", defaultColor);
                        PropBlock.SetColor("_Color", defaultColor);
                        PropBlock.SetColor("_EmissionColor", Color.clear);
                    }
                    r.SetPropertyBlock(PropBlock);
                }
            }
            else
            {
                // Aktif olmayan diğer katmanlar tamamen gizli
                r.enabled = false;
                var col = r.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }
        }

        // Prefilled blokları kontrol et (sadece aktif katmanda görünür)
        foreach (var kvp in prefilledRenderers)
        {
            Vector3Int cell = kvp.Key;
            Renderer r = kvp.Value;
            if (r != null)
            {
                r.enabled = (cell.y == ActiveLayerY);
                if (applyScale) r.transform.localScale = Vector3.one * CellSize;
                r.SetPropertyBlock(null);

                var col = r.GetComponent<Collider>();
                if (col != null) col.enabled = (cell.y == ActiveLayerY);
            }
        }

        // Yerleştirilen blokları kontrol et (sadece aktif katmanda görünür)
        foreach (var kvp in cellObjects)
        {
            Vector3Int cell = kvp.Key;
            GameObject cube = kvp.Value;
            if (cube != null)
            {
                cube.SetActive(cell.y == ActiveLayerY);
                if (applyScale) cube.transform.localScale = Vector3.one * CellSize;
                
                Renderer r = cube.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    r.SetPropertyBlock(null);
                }

                var col = cube.GetComponentInChildren<Collider>();
                if (col != null) col.enabled = (cell.y == ActiveLayerY);
            }
        }
    }

    /// <summary>
    /// Kombolar ve patlamalar olduğunda yerleştirilen grid bloklarını ve hücrelerini çatlatır.
    /// Sarsıntı, çatlak kıvılcımları/parçacıkları ve çıtırtı sesleri üretir.
    /// </summary>
    public void AnimateGridCracking(int comboCount, int linesCleared, System.Action onComplete = null)
    {
        int intensity = Mathf.Clamp(comboCount, 1, 4);

        // Kamera hafif sarsıntı
        if (Camera.main != null)
        {
            Camera.main.transform.DOComplete();
            Camera.main.transform.DOShakePosition(0.18f + 0.05f * intensity, 0.1f * intensity, 16);
        }

        // Haptic titreşim
        if (GameManager.Instance != null && GameManager.Instance.IsVibrationEnabled)
        {
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        // Ses
        AudioManager.Instance?.PlayCrackSound(intensity);

        // Aktif katmandaki yerleştirilen blokları ve prefilled blokları sars & çatlat
        foreach (var c in allShapeCells)
        {
            if (c.y != ActiveLayerY) continue;

            if (cellObjects.TryGetValue(c, out var go) && go != null)
            {
                go.transform.DOComplete();
                go.transform.DOShakePosition(0.22f, 0.08f * intensity, 16);
                go.transform.DOPunchScale(new Vector3(0.12f, -0.1f, 0.12f) * (0.35f * intensity), 0.22f, 6, 0.5f);

                Color crackCol = intensity > 2 ? new Color(1f, 0.4f, 0.1f) : new Color(1f, 0.9f, 0.5f);
                CreateShatterEffect(go.transform.position, crackCol);
            }

            if (prefilledRenderers.TryGetValue(c, out var pr) && pr != null)
            {
                pr.transform.DOComplete();
                pr.transform.DOShakePosition(0.22f, 0.08f * intensity, 16);
                CreateShatterEffect(pr.transform.position, new Color(1f, 0.85f, 0.4f));
            }
        }

        DOVirtual.DelayedCall(0.25f, () => onComplete?.Invoke());
    }

    /// <summary>
    /// Katmana hasar verir, blokları sarsarak çatlatır ve kıvılcım/toz efektleri çıkarır.
    /// </summary>
    public void AnimateLayerCrack(int layerY, int currentStage, int maxStages, System.Action onComplete = null)
    {
        // Kamera hafif sarsıntı
        if (Camera.main != null)
        {
            Camera.main.transform.DOComplete();
            Camera.main.transform.DOShakePosition(0.2f, 0.12f * Mathf.Clamp(currentStage, 1, 3), 16);
        }

        // Haptic titreşim
        if (GameManager.Instance != null && GameManager.Instance.IsVibrationEnabled)
        {
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        // Ses
        AudioManager.Instance?.PlayCrackSound(currentStage);

        float stageRatio = Mathf.Clamp01((float)currentStage / Mathf.Max(1, maxStages));

        // Katmandaki blokları ve kılavuzları sars
        foreach (var c in allShapeCells)
        {
            if (c.y != layerY) continue;

            if (cellObjects.TryGetValue(c, out var go) && go != null)
            {
                go.transform.DOComplete();
                go.transform.DOShakePosition(0.25f, 0.15f * stageRatio, 15);
                go.transform.DOPunchScale(new Vector3(0.12f, -0.1f, 0.12f) * stageRatio, 0.25f, 5, 0.5f);
                CreateShatterEffect(go.transform.position, new Color(1f, 0.85f, 0.4f, 0.7f));
            }

            if (prefilledRenderers.TryGetValue(c, out var pr) && pr != null)
            {
                pr.transform.DOComplete();
                pr.transform.DOShakePosition(0.25f, 0.15f * stageRatio, 15);
                CreateShatterEffect(pr.transform.position, new Color(1f, 0.85f, 0.4f, 0.7f));
            }

            if (targetRenderers.TryGetValue(c, out var tr) && tr != null && tr.enabled)
            {
                tr.transform.DOComplete();
                tr.transform.DOShakePosition(0.2f, 0.1f * stageRatio, 12);
            }
        }

        DOVirtual.DelayedCall(0.28f, () => onComplete?.Invoke());
    }


    // DÜZELTİLDİ (renksiz sisteme geçiş): eskiden bir katmanın tamamlanması için tüm hücrelerin
    // dolu OLMASI YETMEZ, hepsi aynı renk/materyal olması da şarttı. Bu monokromluk şartı
    // kaldırıldı — katman artık sadece doluluğa göre tamamlanır. Renk artık tamamen kozmetik.
    // layerY parametresi alır — artık SADECE ActiveLayerY değil, oyuncunun az önce yerleştirdiği
    // parçanın gerçekte bulunduğu katman kontrol edilmeli (bkz. LevelManager.OnPiecePlaced),
    // çünkü artık herhangi bir katmana yerleştirme yapılabiliyor.
    public bool IsLayerComplete(int layerY)
    {
        SyncGridState();
        return State.IsLayerComplete(layerY);
    }

    public int GetTotalCellsInLayer(int layerY)
    {
        int total = 0;
        foreach (var c in allShapeCells)
        {
            if (c.y == layerY) total++;
        }
        return total;
    }

    public int GetOccupiedCellsInLayer(int layerY)
    {
        int filled = 0;
        foreach (var c in allShapeCells)
        {
            if (c.y == layerY && occupiedCells.Contains(c)) filled++;
        }
        return filled;
    }

    public float GetLayerFillRatio(int layerY)
    {
        int total = 0;
        int filled = 0;
        foreach (var c in allShapeCells)
        {
            if (c.y == layerY)
            {
                total++;
                if (occupiedCells.Contains(c)) filled++;
            }
        }
        return total > 0 ? (float)filled / total : 0f;
    }

    // Sıralı katman mekaniği: Oyuncunun doldurması gereken bir sonraki katman en alt tamamlanmamış katmandır.
    private bool TryFindNextRequiredLayer(out int layerY)
    {
        SyncGridState();
        return State.TryFindFirstIncompleteLayer(out layerY);
    }


    // layerY: patlatılacak (tamamlanmış) katman — artık her zaman ActiveLayerY olmak zorunda
    // değil, oyuncu herhangi bir katmanı tamamlamış olabilir (bkz. LevelManager.OnPiecePlaced).
    // Çökme matematiği zaten Y-göreceli (clearedY'nin üstündeki her şey 1 aşağı kayar), tek
    // fark ActiveLayerY'nin artık "hangi katman patladı" değil "kamera/panel hangi katmana
    // odaklanmış" anlamına gelmesi — bu yüzden patlamadan sonra ayrıca güncelleniyor (aşağıda).
    /// <summary>Katman patlatma/kanca animasyonlarının ortak DOTween kimliği.
    /// Seviye yeniden yüklenirken hepsi tek çağrıyla öldürülebilsin diye.</summary>
    public const string LEVEL_ANIM_ID = "BM3D_LevelAnim";

    /// <summary>
    /// Devam eden TÜM katman animasyonlarını iptal eder, kancayı yerine döndürür ve
    /// artık animasyon konteynerlerini yok eder. Seviye yeniden yüklenirken (Retry /
    /// NextLevel) çağrılmalı.
    ///
    /// Bu olmadan: Retry'a basıldıktan sonra önceki seviyenin kanca animasyonu
    /// çalışmaya devam ediyordu; daha kötüsü, bekleyen DOVirtual.DelayedCall
    /// çağrıları (onLevelComplete/onLayerComplete) YENİ seviyeye ateşlenip
    /// yanlışlıkla kazanma akışını tetikleyebiliyordu.
    /// </summary>
    public void CancelLevelAnimations()
    {
        DOTween.Kill(LEVEL_ANIM_ID);
        IsExplodingLayer = false;

        StopAllCoroutines();
        meltingIceCells.Clear();
        if (IceBreakEffect.Instance != null) IceBreakEffect.Instance.StopAllEffects();

        // Kanca kalıcı bir sahne nesnesi — tween'lerini öldürüp evine yolluyoruz.
        GameObject claw = GameObject.Find("Claw");
        if (claw == null) claw = GameObject.Find("ToyMachine/Claw");
        if (claw != null)
        {
            claw.transform.DOKill();

            // DÜZELTME: Kancaya tutturulmuş ve henüz imha edilmemiş eski blokları temizle
            var toDestroy = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in claw.transform)
            {
                if (child != null && (child.GetComponent<CubeShapeDataHolder>() != null || 
                                      child.name.StartsWith("TempPiece") || 
                                      child.name.Contains("Cube") || 
                                      child.name.Contains("Prefilled")))
                {
                    toDestroy.Add(child.gameObject);
                }
            }
            foreach (var go in toDestroy)
            {
                if (go != null) Destroy(go);
            }

            if (clawHomeCaptured)
            {
                claw.transform.position = clawHomePos;
                claw.transform.rotation = clawHomeRot;
            }
            var col = claw.GetComponent<Collider>();
            if (col != null) col.isTrigger = false;
            // Animasyon yarıda kesildiyse pençeler kapalı (default) kalsın.
            SetClawGrip(claw, 1f);
        }

        // Patlama animasyonu için üretilen geçici konteynerler (bloklar onlara
        // parent'lanıyor) seviye temizliğinde sahipsiz kalıyordu.
        foreach (var leftover in GameObject.FindObjectsOfType<Transform>())
        {
            if (leftover != null && leftover.name == "LayerAnimContainer")
                Destroy(leftover.gameObject);
        }
    }

    // ─── Kanca pençe animasyonu ───────────────────────────────────────────────
    // Claw.fbx üç menteşeyi AYRI kliplerde ihraç ediyor; üçü tek bir klipte
    // birleştirilir (Assets/Prefabs/ClawClose.anim, legacy). Animator/Controller
    // kurmak yerine klip elle örnekleniyor — böylece pençenin kapanma hızı
    // kancanın iniş/kaldırma hareketiyle birebir senkronlanabiliyor.
    // t=0 → pençeler AÇIK, t=1 → KAPALI.
    //
    // Kanca modelinde Animation bileşeni/klip yoksa tüm bu çağrılar sessizce
    // hiçbir şey yapmaz — eski tek parça kancayla da güvenle çalışır.
    private static AnimationClip clawGripClip;

    private static AnimationClip GetClawGripClip(GameObject claw)
    {
        if (clawGripClip != null) return clawGripClip;
        if (claw == null) return null;
        var anim = claw.GetComponent<Animation>();
        if (anim != null) clawGripClip = anim.GetClip("ClawClose");
        return clawGripClip;
    }

    private static void SetClawGrip(GameObject claw, float t01)
    {
        var clip = GetClawGripClip(claw);
        if (clip == null || claw == null) return;
        clip.SampleAnimation(claw, Mathf.Clamp01(t01) * clip.length);
    }

    private static Tween AnimateClawGrip(GameObject claw, float from, float to, float duration)
    {
        var clip = GetClawGripClip(claw);
        if (clip == null || claw == null) return null;
        return DOVirtual.Float(from, to, duration, v => SetClawGrip(claw, v))
            .SetEase(Ease.OutQuad).SetId(LEVEL_ANIM_ID).SetLink(claw);
    }

    private static Vector3    clawHomePos;
    private static Quaternion clawHomeRot;
    private static bool       clawHomeCaptured;

    public void ExplodeLayer(int layerY, System.Action onLayerComplete, System.Action onLevelComplete)
    {
        IsExplodingLayer = true;
        TowerMiniPreview.Instance?.OnFloorDemolished(layerY);

        // Katman patlarken geniş 3D açıya uzaklaş
        CameraOrbit.Instance?.ReturnTo3D();
        // Bu patlama seviyedeki SON katmanı mı temizliyor? (Şekilde bu katmandan başka
        // hücre kalmamışsa evet.) Öyleyse süreyi HEMEN durdur: kanca + çökme animasyonu
        // ~3.65 sn sürüyor ve bu süre içinde sayaç dolarsa kazanılmış seviyede fail
        // paneli açılıyordu. Kontrol, hücreler aşağıda silinmeden ÖNCE yapılmalı.
        bool isFinalLayer = true;
        foreach (var c in allShapeCells)
        {
            if (c.y != layerY) { isFinalLayer = false; break; }
        }
        if (isFinalLayer && allShapeCells.Count > 0)
        {
            GameManager.Instance?.FreezeTimerForPendingWin();
        }

        List<Vector3Int> cellsToRemove = new List<Vector3Int>();
        foreach (var c in targetCells)
        {
            if (c.y == layerY)
            {
                cellsToRemove.Add(c);
            }
        }
        foreach (var c in occupiedCells)
        {
            if (c.y == layerY && !cellsToRemove.Contains(c))
            {
                cellsToRemove.Add(c);
            }
        }

        // Yok edilen katmanın kusursuz (grid-kesin) merkezini hesapla: blok
        // konumlarının ARİTMETİK ORTALAMASI yerine sınırlayıcı kutunun (bounding box)
        // ortası kullanılır. Ortalama, düzensiz/asimetrik şekillerde merkezi bloklara
        // doğru kaydırır; kutu ortası ise kanca makinesindeki gibi katmanın gerçek
        // geometrik merkezine iner.
        Vector3 center = Vector3.zero;
        bool hasBounds = false;
        Vector3 minB = Vector3.zero, maxB = Vector3.zero;
        foreach (var cell in cellsToRemove)
        {
            Vector3? worldPos = null;
            if (cellObjects.TryGetValue(cell, out var go) && go != null)
            {
                worldPos = go.transform.position;
            }
            else if (prefilledRenderers.TryGetValue(cell, out var prefilledRend) && prefilledRend != null)
            {
                worldPos = prefilledRend.transform.position;
            }

            if (worldPos.HasValue)
            {
                if (!hasBounds)
                {
                    minB = maxB = worldPos.Value;
                    hasBounds = true;
                }
                else
                {
                    minB = Vector3.Min(minB, worldPos.Value);
                    maxB = Vector3.Max(maxB, worldPos.Value);
                }
            }
        }
        if (hasBounds) center = (minB + maxB) * 0.5f;

        // Tüm katman için ortak bir yön belirle (Sola veya Sağa)
        Vector3 moveOffset = (Random.value > 0.5f ? Vector3.left : Vector3.right) * 5f;

        // Katmanı bir bütün olarak hareket ettirmek için geçici bir ebeveyn (parent) oluştur
        GameObject layerContainer = new GameObject("LayerAnimContainer");
        layerContainer.transform.position = center;

        List<GameObject> blocksToAnimate = new List<GameObject>();

        int clearedY = layerY;

        foreach (var cell in cellsToRemove)
        {
            occupiedCells.Remove(cell);
            cellColors.Remove(cell);
            targetCells.Remove(cell);
            frozenCells.Remove(cell); // Buz hücreleri de kaldır
            iceRemainingHits.Remove(cell);

            if (targetRenderers.TryGetValue(cell, out var renderer) && renderer != null)
            {
                Object.Destroy(renderer.gameObject);
                targetRenderers.Remove(cell);
            }

            if (cellObjects.TryGetValue(cell, out var go) && go != null)
            {
                cellObjects.Remove(cell);
                go.transform.SetParent(layerContainer.transform, true);
                blocksToAnimate.Add(go);
            }

            // Prefilled blokları da kontrol et ve animasyona ekle
            if (prefilledRenderers.TryGetValue(cell, out var prefilledRenderer) && prefilledRenderer != null)
            {
                prefilledRenderers.Remove(cell);
                var prefilledGo = prefilledRenderer.gameObject;
                if (prefilledGo != null)
                {
                    prefilledGo.transform.SetParent(layerContainer.transform, true);
                    blocksToAnimate.Add(prefilledGo);
                }
            }

            // Ice görsellerini de kontrol et ve animasyona ekle (böylece tüm katman nesneleri birlikte kayar)
            if (iceVisuals.TryGetValue(cell, out var iceGO) && iceGO != null)
            {
                iceVisuals.Remove(cell);
                iceGO.transform.SetParent(layerContainer.transform, true);
                blocksToAnimate.Add(iceGO);
            }
        }

        // Patlatılan katman en üstteki dolu katman değilse (oyuncu katmanları sırasıyla
        // değil de örn. önce en alttakini tamamladıysa), kanca inip çıkarken hâlâ dolu
        // olan üst katmanların (ve boş hedef/ghost hücrelerinin) içinden geçer. Bunlar
        // geçiş süresince geçici olarak gizlenir (bkz. AnimateLayerDisappear) ki kanca
        // içlerinden "klip" görünmeden geçsin; fiziksel bir sorun yok (yerleştirilmiş
        // bloklarda Collider zaten devre dışı, Rigidbody hiç yok), sadece görsel bir düzeltme.
        List<Renderer> renderersAboveClearedLayer = new List<Renderer>();
        foreach (var kvp in cellObjects)
        {
            if (kvp.Key.y <= clearedY || kvp.Value == null) continue;
            var r = kvp.Value.GetComponentInChildren<Renderer>();
            if (r != null) renderersAboveClearedLayer.Add(r);
        }
        foreach (var kvp in prefilledRenderers)
        {
            if (kvp.Key.y > clearedY && kvp.Value != null)
                renderersAboveClearedLayer.Add(kvp.Value);
        }
        // Boş hedef/ghost hücre küpleri de (henüz doldurulmamış grid kılavuzları) kancanın
        // geçtiği üst katmanlarda görünür durumdaysa aynı şekilde geçici olarak gizlenir.
        foreach (var kvp in targetRenderers)
        {
            if (kvp.Key.y > clearedY && kvp.Value != null)
                renderersAboveClearedLayer.Add(kvp.Value);
        }

        // Katman tamamlandığı o an tebrik yazısını fırlat (ekrana fırlatma efektiyle)
        UIManager.Instance?.PlayFloatingPraise(center);

        float layerSlideDelay = 0.45f; // Katmanın tamamlandığı anlaşılsın ve yerleşen son parça otursun diye bekleme süresi

        AnimateLayerDisappear(layerContainer, blocksToAnimate, moveOffset, renderersAboveClearedLayer, clearedY, layerSlideDelay);

        // --- MANTIKSAL ÇÖKME (LOGICAL COLLAPSE) ---
        var newAllShapeCells = new HashSet<Vector3Int>();
        var newTargetCells = new HashSet<Vector3Int>();
        var newOccupiedCells = new HashSet<Vector3Int>();
        var newCellObjects = new Dictionary<Vector3Int, GameObject>();
        var newCellColors = new Dictionary<Vector3Int, Color>();
        var newCellMatIndex = new Dictionary<Vector3Int, int>();
        var newTargetRenderers = new Dictionary<Vector3Int, Renderer>();
        var newPrefilledRenderers = new Dictionary<Vector3Int, Renderer>();
        var newFrozenCells = new HashSet<Vector3Int>();

        // Tamamlanan katmanı allShapeCells'ten çıkar ve üst katmanları aşağı kaydır.
        foreach (var c in allShapeCells)
        {
            if (c.y == clearedY) continue;

            Vector3Int newC = c.y > clearedY
                ? new Vector3Int(c.x, c.y - 1, c.z)
                : c;

            newAllShapeCells.Add(newC);
        }

        foreach (var c in targetCells)
        {
            Vector3Int newC = c.y > clearedY ? new Vector3Int(c.x, c.y - 1, c.z) : c;
            newTargetCells.Add(newC);
            if (targetRenderers.TryGetValue(c, out var r)) newTargetRenderers[newC] = r;
        }

        foreach (var c in occupiedCells)
        {
            Vector3Int newC = c.y > clearedY ? new Vector3Int(c.x, c.y - 1, c.z) : c;
            newOccupiedCells.Add(newC);
            if (cellObjects.TryGetValue(c, out var go)) newCellObjects[newC] = go;
            if (cellColors.TryGetValue(c, out var col)) newCellColors[newC] = col;
            if (cellMatIndex.TryGetValue(c, out var mi)) newCellMatIndex[newC] = mi;
        }

        // Prefilled renderer'ları da kaydır
        foreach (var kvp in prefilledRenderers)
        {
            Vector3Int c = kvp.Key;
            Vector3Int newC = c.y > clearedY ? new Vector3Int(c.x, c.y - 1, c.z) : c;
            newPrefilledRenderers[newC] = kvp.Value;
        }

        // Frozen (buz) hücrelerini de kaydır
        foreach (var c in frozenCells)
        {
            Vector3Int newC = c.y > clearedY ? new Vector3Int(c.x, c.y - 1, c.z) : c;
            newFrozenCells.Add(newC);
        }

        // Erime bayrakları da aynı kaymayı yemeli; yoksa çökmeden sonra o hücre
        // kalıcı olarak görünürlük tazelemesinden muaf kalırdı.
        // Buz MODELLERİNİN sözlüğü de aynı kaymayı yer. Model, hücre küpünün çocuğu
        // olduğu için görsel olarak zaten birlikte iniyor; sözlük kaydırılmazsa eski
        // koordinatta kalır ve RefreshLayerVisibility aynı hücreye İKİNCİ bir buz üretir.
        var newIce = new Dictionary<Vector3Int, GameObject>();
        foreach (var kvp in iceVisuals)
        {
            if (kvp.Value == null) continue;
            newIce[kvp.Key.y > clearedY ? new Vector3Int(kvp.Key.x, kvp.Key.y - 1, kvp.Key.z) : kvp.Key] = kvp.Value;
        }
        iceVisuals.Clear();
        foreach (var kvp in newIce) iceVisuals[kvp.Key] = kvp.Value;

        var newIceHits = new Dictionary<Vector3Int, int>();
        foreach (var kvp in iceRemainingHits)
        {
            if (kvp.Key.y == clearedY) continue;
            newIceHits[kvp.Key.y > clearedY ? new Vector3Int(kvp.Key.x, kvp.Key.y - 1, kvp.Key.z) : kvp.Key] = kvp.Value;
        }
        iceRemainingHits = newIceHits;

        var newMelting = new HashSet<Vector3Int>();
        foreach (var c in meltingIceCells)
        {
            if (c.y == clearedY) continue;
            newMelting.Add(c.y > clearedY ? new Vector3Int(c.x, c.y - 1, c.z) : c);
        }
        meltingIceCells.Clear();
        foreach (var c in newMelting) meltingIceCells.Add(c);

        allShapeCells = newAllShapeCells;
        targetCells = newTargetCells;
        occupiedCells = newOccupiedCells;
        cellObjects = newCellObjects;
        cellColors = newCellColors;
        cellMatIndex = newCellMatIndex;
        targetRenderers = newTargetRenderers;
        prefilledRenderers = newPrefilledRenderers;
        frozenCells = newFrozenCells;

        if (gridMaxY > gridMinY) gridMaxY--;

        TowerMiniPreview.Instance?.OnLayersShifted(clearedY);

        // --- GÖRSEL ÇÖKME (ELASTIC CASCADE & SQUASH-STRETCH LANDING) ---
        float collapseBaseDelay = layerSlideDelay + 0.32f;

        foreach (var kvp in cellObjects)
        {
            if (kvp.Key.y >= clearedY && kvp.Value != null)
            {
                var t = kvp.Value.transform;
                float stagger = (Mathf.Abs(kvp.Key.x) + Mathf.Abs(kvp.Key.z)) * 0.03f;
                float delay = collapseBaseDelay + stagger;
                float targetY = t.localPosition.y - Step;

                Sequence dropSeq = DOTween.Sequence().SetDelay(delay).SetId(LEVEL_ANIM_ID);
                dropSeq.Append(t.DOLocalMoveY(targetY, 0.38f).SetEase(Ease.OutBack, 1.15f));
                dropSeq.AppendCallback(() =>
                {
                    t.DOPunchScale(new Vector3(0.14f, -0.16f, 0.14f), 0.22f, 6, 0.5f).SetId(LEVEL_ANIM_ID);
                });
            }
        }

        foreach (var kvp in targetRenderers)
        {
            if (kvp.Key.y >= clearedY && kvp.Value != null)
            {
                var t = kvp.Value.transform;
                float stagger = (Mathf.Abs(kvp.Key.x) + Mathf.Abs(kvp.Key.z)) * 0.03f;
                float delay = collapseBaseDelay + stagger;
                float targetY = t.localPosition.y - Step;

                t.DOLocalMoveY(targetY, 0.38f).SetEase(Ease.OutBack, 1.15f).SetDelay(delay).SetId(LEVEL_ANIM_ID);
            }
        }

        foreach (var kvp in prefilledRenderers)
        {
            if (kvp.Key.y >= clearedY && kvp.Value != null)
            {
                var t = kvp.Value.transform;
                float stagger = (Mathf.Abs(kvp.Key.x) + Mathf.Abs(kvp.Key.z)) * 0.03f;
                float delay = collapseBaseDelay + stagger;
                float targetY = t.localPosition.y - Step;

                Sequence dropSeq = DOTween.Sequence().SetDelay(delay).SetId(LEVEL_ANIM_ID);
                dropSeq.Append(t.DOLocalMoveY(targetY, 0.38f).SetEase(Ease.OutBack, 1.15f));
                dropSeq.AppendCallback(() =>
                {
                    t.DOPunchScale(new Vector3(0.14f, -0.16f, 0.14f), 0.22f, 6, 0.5f).SetId(LEVEL_ANIM_ID);
                });
            }
        }

        if (allShapeCells.Count == 0)
        {
            ActiveLayerY = gridMaxY + 1;
            RefreshLayerVisibility();
            RefreshSpeciesSparkle();

            float totalAnimDuration = layerSlideDelay + 0.35f;
            Debug.Log($"[WIN_TIMING] ExplodeLayer SON KATMAN — t={Time.time:F3}, winCallback {totalAnimDuration:F2}sn sonra");
            System.Action winFinish = () => { Debug.Log($"[WIN_TIMING] onLevelComplete fired — t={Time.time:F3}"); IsExplodingLayer = false; onLevelComplete?.Invoke(); };
            DOVirtual.DelayedCall(totalAnimDuration, () => winFinish()).SetId(LEVEL_ANIM_ID);
        }
        else
        {
            int targetNextLayer = ActiveLayerY;
            if (ActiveLayerY == clearedY)
            {
                if (TryFindNextRequiredLayer(out int nextLayer))
                {
                    targetNextLayer = nextLayer;
                }
                else
                    targetNextLayer = gridMaxY;
            }
            else if (ActiveLayerY > clearedY)
            {
                targetNextLayer = ActiveLayerY - 1;
            }

            // Patlama ve parçalanma animasyonu tamamen bittikten sonra 0.5s bekleyip sıradaki katmanı pürüzsüz dalga ile ortaya çıkar
            float explosionEndDelay = layerSlideDelay + 1.15f;
            DOVirtual.DelayedCall(explosionEndDelay, () =>
            {
                ActiveLayerY = targetNextLayer;
                AnimateLayerEntrance(ActiveLayerY, () =>
                {
                    IsExplodingLayer = false;
                    onLayerComplete?.Invoke();
                });
            }).SetId(LEVEL_ANIM_ID);
        }
    }

    /// <summary>
    /// Katman patlaması bittiğinde, sıradaki katmanın hücrelerini dalga şeklinde (radial pop-in)
    /// ve yumuşak kamera odağıyla pürüzsüzce ortaya çıkarır.
    /// </summary>
    public void AnimateLayerEntrance(int layerY, System.Action onComplete = null)
    {
        RefreshLayerVisibility();
        RefreshSpeciesSparkle();
        LayerPanelController.Instance?.RefreshButtonColors();

        Vector3 layerCenter = Vector3.zero;
        int count = 0;
        List<Transform> entranceTransforms = new List<Transform>();
        List<Vector3Int> entranceCells = new List<Vector3Int>();

        foreach (var kvp in targetRenderers)
        {
            if (kvp.Key.y == layerY && kvp.Value != null && kvp.Value.enabled)
            {
                entranceTransforms.Add(kvp.Value.transform);
                entranceCells.Add(kvp.Key);
                layerCenter += kvp.Value.transform.position;
                count++;
            }
        }

        foreach (var kvp in prefilledRenderers)
        {
            if (kvp.Key.y == layerY && kvp.Value != null && kvp.Value.enabled)
            {
                entranceTransforms.Add(kvp.Value.transform);
                entranceCells.Add(kvp.Key);
                layerCenter += kvp.Value.transform.position;
                count++;
            }
        }

        foreach (var kvp in cellObjects)
        {
            if (kvp.Key.y == layerY && kvp.Value != null)
            {
                entranceTransforms.Add(kvp.Value.transform);
                entranceCells.Add(kvp.Key);
                layerCenter += kvp.Value.transform.position;
                count++;
            }
        }

        if (count > 0)
        {
            layerCenter /= count;
        }
        else
        {
            layerCenter = CellToWorld(new Vector3Int(0, layerY, 0));
        }

        // Bütün giriş yapacak hücreleri önce sıfır ölçeğe al
        for (int i = 0; i < entranceTransforms.Count; i++)
        {
            var t = entranceTransforms[i];
            if (t != null)
            {
                t.localScale = Vector3.zero;
            }
        }

        // Merkezden dışa doğru dalga şeklinde pop-in animasyonu
        float maxDelay = 0f;
        for (int i = 0; i < entranceTransforms.Count; i++)
        {
            var t = entranceTransforms[i];
            var cell = entranceCells[i];
            if (t == null) continue;

            float dist = Mathf.Sqrt(cell.x * cell.x + cell.z * cell.z);
            float delay = dist * 0.035f;
            if (delay > maxDelay) maxDelay = delay;

            t.DOScale(Vector3.one * CellSize, 0.32f)
             .SetEase(Ease.OutBack, 1.25f)
             .SetDelay(delay)
             .SetId(LEVEL_ANIM_ID);
        }

        // Kamera yumuşakça yeni katmana odaklansın
        if (CameraOrbit.Instance != null)
        {
            CameraOrbit.Instance.ZoomToLayer(layerCenter, null, instant: false);
        }

        float totalDuration = maxDelay + 0.35f;
        DOVirtual.DelayedCall(totalDuration, () =>
        {
            onComplete?.Invoke();
        }).SetId(LEVEL_ANIM_ID);
    }

    public void SetCellColor(Vector3Int cell, Color color)
    {
        cellColors[cell] = color;
    }

    public void SetCellMatIndex(Vector3Int cell, int matIndex)
    {
        cellMatIndex[cell] = matIndex;
    }

    public void AddCell(Vector3Int cell, GameObject cube, Color color, int matIndex = -1, bool animateBump = true)
    {
        occupiedCells.Add(cell);
        cellObjects[cell] = cube;
        cellColors[cell] = color;
        cellMatIndex[cell] = matIndex;

        if (animateBump)
        {
            StartCoroutine(BumpAnimation(cube.transform));
        }

        TowerMiniPreview.Instance?.OnCellPlaced(cell, color);
    }

    public void RemoveCellAnimated(Vector3Int cell, float delay)
    {
        occupiedCells.Remove(cell);
        cellColors.Remove(cell);
        cellMatIndex.Remove(cell);
        TowerMiniPreview.Instance?.OnCellRemoved(cell);

        if (cellObjects.TryGetValue(cell, out var go))
        {
            cellObjects.Remove(cell);
            if (go != null)
            {
                DOTween.Kill(go.transform);
                go.transform.DOScale(Vector3.zero, 0.25f)
                    .SetEase(Ease.InBack)
                    .SetDelay(delay)
                    .OnComplete(() => {
                        if (go != null) Destroy(go);
                    });
            }
        }

        if (targetRenderers.TryGetValue(cell, out var r) && r != null)
        {
            r.enabled = true;
        }
    }

    /// <summary>Geriye dönük uyumluluk wrapper'ı</summary>
    public (int cleared, int bonusLines) CheckAndClearLines(System.Action onComplete = null)
    {
        var result = CheckAndClearActiveLayerLines(null, (lines, cells) => onComplete?.Invoke());
        return (result.clearedLines, 0);
    }

    public (int clearedLines, int clearedCells) CheckAndClearActiveLayerLines(System.Action<int, int> onComplete)
    {
        return CheckAndClearActiveLayerLines(null, onComplete);
    }

    /// <summary>
    /// Aktif katmandaki dolu satırları (X ekseni) ve sütunları (Z ekseni) tespit eder,
    /// Block Blast tarzı patlatır, temizler ve yok eder.
    /// onComplete(clearedLinesCount, clearedCellsCount) döndürür.
    /// </summary>
    public (int clearedLines, int clearedCells) CheckAndClearActiveLayerLines(List<Vector3Int> newlyPlacedCells, System.Action<int, int> onComplete = null)
    {
        int targetY = ActiveLayerY;
        var linesToBlast = new List<List<Vector3Int>>();

        int totalInLayer = GetTotalCellsInLayer(targetY);
        int occInLayer = GetOccupiedCellsInLayer(targetY);
        bool isLayer100PercentFull = totalInLayer > 0 && occInLayer >= totalInLayer;

        HashSet<Vector3Int> newSet = newlyPlacedCells != null ? new HashSet<Vector3Int>(newlyPlacedCells) : null;

        // 1. Yatay Satırlar (Sabit Z, Değişen X)
        for (int z = gridMinZ; z <= gridMaxZ; z++)
        {
            var line = new List<Vector3Int>();
            int targetCellsInLine = 0;
            bool isFull = true;

            for (int x = gridMinX; x <= gridMaxX; x++)
            {
                Vector3Int cell = new Vector3Int(x, targetY, z);
                if (targetCells.Contains(cell))
                {
                    targetCellsInLine++;
                    if (!occupiedCells.Contains(cell))
                    {
                        isFull = false;
                        break;
                    }
                    line.Add(cell);
                }
            }

            if (targetCellsInLine >= 2 && isFull && line.Count > 0)
            {
                // Parçanın tek başına doldurduğu satır, katman henüz tamamlanmamışsa hemen patlamaz;
                // oyuncunun en az 1 tamamlayıcı parça (ekleme) daha yerleştirmesi beklenir.
                bool isPurelyNewPiece = newSet != null && line.All(c => newSet.Contains(c));
                if (!isPurelyNewPiece || isLayer100PercentFull)
                {
                    linesToBlast.Add(line);
                }
            }
        }

        // 2. Dikey Sütunlar (Sabit X, Değişen Z)
        for (int x = gridMinX; x <= gridMaxX; x++)
        {
            var line = new List<Vector3Int>();
            int targetCellsInLine = 0;
            bool isFull = true;

            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                Vector3Int cell = new Vector3Int(x, targetY, z);
                if (targetCells.Contains(cell))
                {
                    targetCellsInLine++;
                    if (!occupiedCells.Contains(cell))
                    {
                        isFull = false;
                        break;
                    }
                    line.Add(cell);
                }
            }

            if (targetCellsInLine >= 2 && isFull && line.Count > 0)
            {
                bool isPurelyNewPiece = newSet != null && line.All(c => newSet.Contains(c));
                if (!isPurelyNewPiece || isLayer100PercentFull)
                {
                    linesToBlast.Add(line);
                }
            }
        }

        // 3. 2x2 Dolu Kare Alanlar
        for (int x = gridMinX; x < gridMaxX; x++)
        {
            for (int z = gridMinZ; z < gridMaxZ; z++)
            {
                Vector3Int p1 = new Vector3Int(x, targetY, z);
                Vector3Int p2 = new Vector3Int(x + 1, targetY, z);
                Vector3Int p3 = new Vector3Int(x, targetY, z + 1);
                Vector3Int p4 = new Vector3Int(x + 1, targetY, z + 1);

                if (targetCells.Contains(p1) && targetCells.Contains(p2) &&
                    targetCells.Contains(p3) && targetCells.Contains(p4) &&
                    occupiedCells.Contains(p1) && occupiedCells.Contains(p2) &&
                    occupiedCells.Contains(p3) && occupiedCells.Contains(p4))
                {
                    var square = new List<Vector3Int> { p1, p2, p3, p4 };
                    bool isPurelyNewPiece = newSet != null && square.All(c => newSet.Contains(c));
                    if (!isPurelyNewPiece || isLayer100PercentFull)
                    {
                        linesToBlast.Add(square);
                    }
                }
            }
        }

        if (linesToBlast.Count == 0)
        {
            onComplete?.Invoke(0, 0);
            return (0, 0);
        }

        var toClear = new HashSet<Vector3Int>();
        foreach (var line in linesToBlast)
        {
            foreach (var cell in line)
            {
                toClear.Add(cell);
            }
        }

        int linesCount = linesToBlast.Count;
        int clearedCellsCount = toClear.Count;

        // Kamera hafif sarsıntı
        if (Camera.main != null)
        {
            Camera.main.transform.DOComplete();
            Camera.main.transform.DOShakePosition(0.28f, 0.22f * Mathf.Clamp(linesCount, 1, 3), 22);
        }

        var sorted = new List<Vector3Int>(toClear);
        sorted.Sort((a, b) => (a.x + a.z).CompareTo(b.x + b.z));

        int pendingCount = sorted.Count;
        System.Action onOneDone = () =>
        {
            pendingCount--;
            if (pendingCount <= 0)
            {
                RefreshLayerVisibility();
                onComplete?.Invoke(linesCount, clearedCellsCount);
            }
        };

        for (int i = 0; i < sorted.Count; i++)
        {
            var cell = sorted[i];
            cellColors.TryGetValue(cell, out Color blastColor);

            occupiedCells.Remove(cell);
            cellColors.Remove(cell);
            cellMatIndex.Remove(cell);
            cellRemainingHits.Remove(cell);

            GameObject targetGo = null;
            bool wasPrefilled = false;
            Renderer prefilledRend = null;
            if (cellObjects.TryGetValue(cell, out var go) && go != null)
            {
                cellObjects.Remove(cell);
                targetGo = go;
            }
            else if (prefilledRenderers.TryGetValue(cell, out var pr) && pr != null)
            {
                prefilledRenderers.Remove(cell);
                wasPrefilled = true;
                prefilledRend = pr;
                var clone = Instantiate(pr.gameObject, pr.transform.position, pr.transform.rotation, pr.transform.parent);
                targetGo = clone;
            }

            TowerMiniPreview.Instance?.OnCellRemoved(cell);

            if (targetGo != null)
            {
                var targetRend = targetGo.GetComponentInChildren<Renderer>();
                if (targetRend != null && targetRend.sharedMaterial != null)
                {
                    Color mc = GetMaterialColor(targetRend.sharedMaterial);
                    if (mc != Color.white) blastColor = mc;
                }
                if (blastColor == default || blastColor == Color.clear) blastColor = new Color(1f, 0.75f, 0.2f);

                float stagger = i * 0.032f;
                var capturedGo = targetGo;
                var capturedCell = cell;
                bool capturedPrefilled = wasPrefilled;
                var capturedPrefRend = prefilledRend;
                Color capturedColor = blastColor;

                if (stagger > 0f)
                {
                    DOVirtual.DelayedCall(stagger, () =>
                    {
                        if (capturedGo != null)
                        {
                            IceBreakEffect.Play(capturedGo, capturedColor, () =>
                            {
                                if (capturedGo != null) Destroy(capturedGo);
                                onOneDone();
                            }, hideTarget: true);

                            if (capturedPrefilled && capturedPrefRend != null)
                            {
                                RestoreAsGhostTarget(capturedCell, capturedPrefRend);
                            }
                            else if (targetRenderers.TryGetValue(capturedCell, out var r) && r != null)
                            {
                                r.enabled = true;
                                r.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 5, 0.5f);
                            }
                        }
                        else
                        {
                            onOneDone();
                        }
                    });
                }
                else
                {
                    IceBreakEffect.Play(capturedGo, capturedColor, () =>
                    {
                        if (capturedGo != null) Destroy(capturedGo);
                        onOneDone();
                    }, hideTarget: true);

                    if (capturedPrefilled && capturedPrefRend != null)
                    {
                        RestoreAsGhostTarget(capturedCell, capturedPrefRend);
                    }
                    else if (targetRenderers.TryGetValue(capturedCell, out var r) && r != null)
                    {
                        r.enabled = true;
                        r.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 5, 0.5f);
                    }
                }
            }
            else
            {
                onOneDone();
            }
        }

        return (linesCount, clearedCellsCount);
    }

    /// <summary>
    /// Aktif katmandaki dolu satırları ve sütunları tespit eder.
    /// Küpleri SİLMEZ; sadece parıldama/vuruş efekti uygular ve yeni eşleşen satır/sütun sayısını döndürür.
    /// </summary>
    public int CheckLineMatchesWithoutClearing(HashSet<string> alreadyMatchedLines, System.Action<List<Vector3Int>> onMatchedCells = null)
    {
        int targetY = ActiveLayerY;
        int newlyMatchedLinesCount = 0;
        var matchedCells = new List<Vector3Int>();

        // 1. Yatay Satırlar (Sabit Z, Değişen X)
        for (int z = gridMinZ; z <= gridMaxZ; z++)
        {
            string lineId = $"H_{targetY}_{z}";
            if (alreadyMatchedLines.Contains(lineId)) continue;

            var line = new List<Vector3Int>();
            int targetCount = 0;
            bool isFull = true;

            for (int x = gridMinX; x <= gridMaxX; x++)
            {
                Vector3Int cell = new Vector3Int(x, targetY, z);
                if (targetCells.Contains(cell))
                {
                    targetCount++;
                    if (!occupiedCells.Contains(cell))
                    {
                        isFull = false;
                        break;
                    }
                    line.Add(cell);
                }
            }

            if (targetCount >= 2 && isFull && line.Count > 0)
            {
                alreadyMatchedLines.Add(lineId);
                newlyMatchedLinesCount++;
                matchedCells.AddRange(line);
            }
        }

        // 2. Dikey Sütunlar (Sabit X, Değişen Z)
        for (int x = gridMinX; x <= gridMaxX; x++)
        {
            string lineId = $"V_{targetY}_{x}";
            if (alreadyMatchedLines.Contains(lineId)) continue;

            var line = new List<Vector3Int>();
            int targetCount = 0;
            bool isFull = true;

            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                Vector3Int cell = new Vector3Int(x, targetY, z);
                if (targetCells.Contains(cell))
                {
                    targetCount++;
                    if (!occupiedCells.Contains(cell))
                    {
                        isFull = false;
                        break;
                    }
                    line.Add(cell);
                }
            }

            if (targetCount >= 2 && isFull && line.Count > 0)
            {
                alreadyMatchedLines.Add(lineId);
                newlyMatchedLinesCount++;
                matchedCells.AddRange(line);
            }
        }

        // Eşleşen küplerin üzerinde görsel parıldama ve vuruş efekti
        if (matchedCells.Count > 0)
        {
            foreach (var cell in matchedCells)
            {
                GameObject go = null;
                if (cellObjects.TryGetValue(cell, out var playerObj) && playerObj != null) go = playerObj;
                else if (prefilledRenderers.TryGetValue(cell, out var pr) && pr != null) go = pr.gameObject;

                if (go != null)
                {
                    go.transform.DOComplete();
                    go.transform.DOPunchScale(Vector3.one * 0.22f, 0.25f, 6, 0.5f);

                    var rend = go.GetComponentInChildren<Renderer>();
                    if (rend != null && rend.material != null && rend.material.HasProperty("_EmissionColor"))
                    {
                        rend.material.EnableKeyword("_EMISSION");
                        rend.material.DOColor(new Color(1f, 0.9f, 0.3f) * 2.5f, "_EmissionColor", 0.14f).SetLoops(2, LoopType.Yoyo);
                    }
                }
            }
            onMatchedCells?.Invoke(matchedCells);
        }

        return newlyMatchedLinesCount;
    }

    /// <summary>
    /// Kule Çökertme (Tower Collapse): Aktif katmanı tamamen imha eder.
    /// Katta bulunan tüm parçalar ve grid blokları fiziksel olarak aşağı dökülür / yıkılır,
    /// ardından bir sonraki katman aktifleşir.
    /// </summary>
    public void CollapseActiveLayerAndDropTower(System.Action onComplete = null)
    {
        IsExplodingLayer = true;

        // Kamera güçlü sarsıntı
        if (Camera.main != null)
        {
            Camera.main.transform.DOComplete();
            Camera.main.transform.DOShakePosition(0.55f, 0.4f, 25);
        }

        // Haptic titreşim
        if (GameManager.Instance != null && GameManager.Instance.IsVibrationEnabled)
        {
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        // Ses
        AudioManager.Instance?.PlayCollapseSound();

        int clearedY = ActiveLayerY;

        // 1. Aktif katmanda kalan tüm blokları topla
        List<Vector3Int> cellsToRemove = new List<Vector3Int>();
        foreach (var c in allShapeCells)
        {
            if (c.y == clearedY) cellsToRemove.Add(c);
        }

        List<GameObject> rubbleObjects = new List<GameObject>();

        foreach (var cell in cellsToRemove)
        {
            occupiedCells.Remove(cell);
            targetCells.Remove(cell);
            allShapeCells.Remove(cell);
            frozenCells.Remove(cell);
            cellMatIndex.Remove(cell);
            cellColors.Remove(cell);
            cellRemainingHits.Remove(cell);

            if (cellObjects.TryGetValue(cell, out var go) && go != null)
            {
                cellObjects.Remove(cell);
                rubbleObjects.Add(go);
            }

            if (prefilledRenderers.TryGetValue(cell, out var pr) && pr != null)
            {
                prefilledRenderers.Remove(cell);
                rubbleObjects.Add(pr.gameObject);
            }

            if (targetRenderers.TryGetValue(cell, out var tr) && tr != null)
            {
                targetRenderers.Remove(cell);
                rubbleObjects.Add(tr.gameObject);
            }
        }

        TowerMiniPreview.Instance?.OnFloorDemolished(clearedY);

        // 2. Yıkılan blokların aşağı dökülme ve takla atma animasyonu (Fiziksel enkaz hissi)
        for (int i = 0; i < rubbleObjects.Count; i++)
        {
            var obj = rubbleObjects[i];
            if (obj == null) continue;

            obj.transform.SetParent(null); // Bağımsız dünya koordinatında düşsün

            Vector3 startPos = obj.transform.position;
            Vector3 randomScatter = new Vector3(
                Random.Range(-1.4f, 1.4f),
                Random.Range(0.4f, 1.2f),
                Random.Range(-1.4f, 1.4f)
            );
            Vector3 fallTarget = startPos + randomScatter + Vector3.down * 14f;
            Vector3 randomTumble = new Vector3(
                Random.Range(-400f, 400f),
                Random.Range(-400f, 400f),
                Random.Range(-400f, 400f)
            );

            float delay = Random.Range(0f, 0.12f);

            var seq = DOTween.Sequence();
            if (delay > 0) seq.AppendInterval(delay);

            // Pop & Çatlama kıvılcımı
            CreateShatterEffect(startPos, new Color(1f, 0.8f, 0.4f, 0.8f));

            // Yukarı hafif fırlayıp aşağı serbest düşüş (Gravity fall)
            seq.Append(obj.transform.DOMove(startPos + randomScatter * 0.35f, 0.15f).SetEase(Ease.OutQuad));
            seq.Append(obj.transform.DOMove(fallTarget, 0.75f).SetEase(Ease.InQuad));
            seq.Join(obj.transform.DORotate(randomTumble, 0.9f, RotateMode.FastBeyond360).SetEase(Ease.Linear));
            seq.Insert(0.5f + delay, obj.transform.DOScale(Vector3.zero, 0.35f).SetEase(Ease.InBack));
            seq.OnComplete(() =>
            {
                if (obj != null) Destroy(obj);
            });
        }

        // 1. Katmanın tamamen temizlenip yıkılması (~1.0s)
        // 2. Yıkım bittikten sonra 0.5s nefes alma / bekleme molası (~0.5s)
        // Toplam bekleme = 1.50s (Bu süre boyunca ActiveLayerY değiştirilmez, böylece ara RefreshLayerVisibility çağrıları 2. animasyonu tetiklemez)
        float totalPauseBeforeNextLayer = 1.50f;
        DOVirtual.DelayedCall(totalPauseBeforeNextLayer, () =>
        {
            // 3. Bir sonraki katmana geçişi tam bu anda yap
            ActiveLayerY++;
            TowerMiniPreview.Instance?.SetActiveFloor(ActiveLayerY);

            // Kalan şekil için sınırları güncelle
            if (allShapeCells.Count > 0)
            {
                gridMinY = allShapeCells.Min(c => c.y);
                gridMaxY = allShapeCells.Max(c => c.y);
            }

            SyncGridState();

            AnimateLayerEntrance(ActiveLayerY, () =>
            {
                IsExplodingLayer = false;
                LayerPanelController.Instance?.RefreshButtonColors();
                onComplete?.Invoke();
            });
        }).SetId(LEVEL_ANIM_ID);
    }

    public static bool ColorsApproxEqual(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.05f
        && Mathf.Abs(a.g - b.g) < 0.05f
        && Mathf.Abs(a.b - b.b) < 0.05f;

    public static Color GetMaterialColor(Material mat)
    {
        if (mat == null) return Color.white;
        if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
        return Color.white;
    }

    public void ClearAllCellObjects()
    {
        StopVisualFocus(null);
        ClearOccludingCells();
        foreach (var go in cellObjects.Values)
        {
            if (go == null) continue;
            DOTween.Kill(go.transform);
            Object.Destroy(go);
        }
        cellObjects.Clear();
        cellColors.Clear();
        ClearAllIceVisuals();
        // Yarıda kalan erime bayrakları sonraki seviyeye sızarsa o hücreler
        // görünürlük tazelemesinden kalıcı olarak muaf kalırdı.
        meltingIceCells.Clear();
    }

    private IEnumerator BumpAnimation(Transform target)
    {
        if (target == null) yield break;
        Vector3 originalScale = Vector3.one * CellSize;
        // Hızlı scale-up, sonra küçük overshoot ile geri dön
        DOTween.Kill(target);
        target.localScale = originalScale;
        target.DOScale(originalScale * 1.35f, 0.08f).SetEase(Ease.OutQuad)
              .OnComplete(() =>
              {
                  if (target != null)
                      target.DOScale(originalScale, 0.14f).SetEase(Ease.OutElastic);
              });
        yield break;
    }

    private static void AnimateAndDestroy(GameObject go, float delay, bool isBonus, System.Action onDone = null)
    {
        if (go == null) { onDone?.Invoke(); return; }

        var rend = go.GetComponentInChildren<Renderer>();
        Color col = Color.white;
        if (rend != null && rend.sharedMaterial != null)
        {
            col = GetMaterialColor(rend.sharedMaterial);
        }

        if (delay > 0f)
        {
            DOVirtual.DelayedCall(delay, () =>
            {
                if (go != null)
                {
                    IceBreakEffect.Play(go, col, () =>
                    {
                        if (go != null) Object.Destroy(go);
                        onDone?.Invoke();
                    }, hideTarget: true);
                }
                else
                {
                    onDone?.Invoke();
                }
            });
        }
        else
        {
            IceBreakEffect.Play(go, col, () =>
            {
                if (go != null) Object.Destroy(go);
                onDone?.Invoke();
            }, hideTarget: true);
        }
    }

    /// <summary>Kanca (ve taşıdığı katman) ekranın ÜST kenarını tamamen geçtiği (KAMERADAN
    /// ÇIKTIĞI) an onFinish'i çağırır — "kanca gitti, oynamaya devam edilebilir" anı. Önce
    /// kancanın görüş alanına indiğini bekler (dinlenme konumu ekran dışında olabilir), sonra
    /// tekrar üstten çıkışını yakalar. Algılama gerçekleşmezse ~3 sn güvenlik zaman aşımıyla yine
    /// de tetikler (asılı kalmaz). Retry/NextLevel'da CancelLevelAnimations → StopAllCoroutines
    /// ile durur, bu yüzden yeni seviyeye stale tetikleme sızmaz.</summary>
    private static void AnimateLayerDisappear(GameObject container, List<GameObject> blocks, Vector3 moveOffset, List<Renderer> renderersToFadeDuringPass = null, int clearedY = -1, float startDelay = 0f)
    {
        if (container == null) return;

        Vector3 centerPos = container.transform.position;

        if (blocks != null)
        {
            foreach (var block in blocks)
            {
                if (block == null) continue;
                var faceCam = block.GetComponentInChildren<FaceCamera>();
                if (faceCam != null) faceCam.enabled = false;
            }
        }

        Sequence seq = DOTween.Sequence().SetId(LEVEL_ANIM_ID);

        if (startDelay > 0f)
        {
            seq.AppendInterval(startDelay);
        }

        // 1. Parlama ve Sarsıntı (Impact / Pulse)
        seq.AppendCallback(() =>
        {
            AudioManager.Instance?.PlayPlacementSound();
            CameraOrbit.Instance?.Shake(0.35f, 0.2f);

            if (blocks != null)
            {
                foreach (var block in blocks)
                {
                    if (block == null) continue;
                    block.transform.DOPunchScale(Vector3.one * 0.22f, 0.18f, 8, 0.5f).SetId(LEVEL_ANIM_ID);
                }
            }
        });

        seq.AppendInterval(0.1f);

        // 2. Radyal 3D Dağılma & Parçalanma Animasyonu (Radial 3D Voxel Burst)
        seq.AppendCallback(() =>
        {
            if (blocks != null)
            {
                foreach (var block in blocks)
                {
                    if (block == null) continue;

                    Vector3 bPos = block.transform.position;
                    Vector3 radialDir = (bPos - centerPos);
                    radialDir.y = 0f;
                    if (radialDir.sqrMagnitude < 0.01f)
                    {
                        radialDir = Random.insideUnitSphere;
                        radialDir.y = 0f;
                    }
                    radialDir = radialDir.normalized * Random.Range(2.5f, 4.2f) + Vector3.up * Random.Range(0.6f, 1.8f);

                    Vector3 targetBurstPos = bPos + radialDir;
                    Vector3 randomRot = new Vector3(
                        Random.Range(-360f, 360f),
                        Random.Range(-360f, 360f),
                        Random.Range(-360f, 360f)
                    );

                    float animDuration = Random.Range(0.42f, 0.55f);

                    block.transform.DOMove(targetBurstPos, animDuration).SetEase(Ease.OutQuad).SetId(LEVEL_ANIM_ID);
                    block.transform.DORotate(randomRot, animDuration, RotateMode.FastBeyond360).SetEase(Ease.OutQuad).SetId(LEVEL_ANIM_ID);
                    block.transform.DOScale(Vector3.zero, animDuration).SetEase(Ease.InBack).SetId(LEVEL_ANIM_ID);

                    // Mini shatter parçacıkları
                    if (GridManager.Instance != null)
                    {
                        Color shardCol = Color.white;
                        var rend = block.GetComponentInChildren<Renderer>();
                        if (rend != null && rend.sharedMaterial != null)
                        {
                            shardCol = GetMaterialColor(rend.sharedMaterial);
                        }
                        GridManager.Instance.CreateShatterEffect(bPos, shardCol);
                    }
                }
            }
        });

        seq.AppendInterval(0.6f);
        seq.OnComplete(() =>
        {
            if (container != null)
                Object.Destroy(container);
            if (blocks != null)
            {
                foreach (var b in blocks)
                    if (b != null) Object.Destroy(b);
            }
        });
    }

    public Vector3 CellToWorld(Vector3Int cell)
    {
        Vector3 localPos = new Vector3(cell.x, cell.y, cell.z) * Step + Vector3.one * (CellSize * 0.5f);
        if (LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null)
        {
            return LevelManager.Instance.ActiveMainPiece.transform.TransformPoint(localPos);
        }
        return Origin + localPos;
    }

    public Vector3Int RootToOffset(Vector3 rootWorld)
    {
        if (LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null)
        {
            Vector3 local = LevelManager.Instance.ActiveMainPiece.transform.InverseTransformPoint(rootWorld) / Step;
            return new Vector3Int(
                Mathf.RoundToInt(local.x),
                Mathf.RoundToInt(local.y),
                Mathf.RoundToInt(local.z));
        }
        Vector3 globalLocal = (rootWorld - Origin) / Step;
        return new Vector3Int(
            Mathf.RoundToInt(globalLocal.x),
            Mathf.RoundToInt(globalLocal.y),
            Mathf.RoundToInt(globalLocal.z));
    }

    public Vector3 OffsetToRoot(Vector3Int offset)
    {
        Vector3 localPos = new Vector3(offset.x, offset.y, offset.z) * Step;
        if (LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null)
        {
            return LevelManager.Instance.ActiveMainPiece.transform.TransformPoint(localPos);
        }
        return Origin + localPos;
    }

    public bool TryFindSnapOffset(List<Vector3Int> cells, Ray ray, float maxDist, out Vector3Int result)
    {
        Transform boardTransform = LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null
            ? LevelManager.Instance.ActiveMainPiece.transform
            : null;
        Transform draggedTransform = DraggablePiece.activeDrag != null
            ? DraggablePiece.activeDrag.transform
            : null;

        SyncGridState();
        return PlacementValidator.TryFindSnapOffset(
            cells, ray, maxDist, State, ActiveLayerY, Camera.main, draggedTransform, boardTransform, out result);
    }

    public bool IsSupported(List<Vector3Int> cells, Vector3Int offset)
    {
        SyncGridState();
        return PlacementValidator.IsSupported(cells, offset, State);
    }

    public bool CanPlace(List<Vector3Int> cells, Vector3Int offset)
    {
        SyncGridState();
        return PlacementValidator.CanPlace(cells, offset, State, ActiveLayerY, IsExplodingLayer);
    }

    public bool TryPlace(List<Vector3Int> cells, Vector3Int offset)
    {
        if (!CanPlace(cells, offset)) return false;
        foreach (var c in cells) occupiedCells.Add(c + offset);
        return true;
    }

    public void Remove(List<Vector3Int> cells, Vector3Int offset)
    {
        foreach (var c in cells)
        {
            var cell = c + offset;
            occupiedCells.Remove(cell);
            if (targetRenderers.TryGetValue(cell, out var r) && r != null)
            {
                if (ShouldCellBeVisible(cell)) r.enabled = true;
            }
        }
    }

    private bool ShouldCellBeVisible(Vector3Int cell)
    {
        if (CameraOrbit.Instance != null && CameraOrbit.Instance.IsInPanelMode)
        {
            return cell.y == ActiveLayerY;
        }
        return true;
    }

    public void UpdateSnappedPreviewCells(List<Vector3Int> snappedCells)
    {
        var newSnapped = new HashSet<Vector3Int>(snappedCells);
        
        // 1. Önce eski gizlenmiş olanlardan artık snaplenmeyenleri geri göster
        foreach (var cell in temporarilyHiddenGridCells)
        {
            if (!newSnapped.Contains(cell))
            {
                if (!occupiedCells.Contains(cell))
                {
                    if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                    {
                        if (ShouldCellBeVisible(cell)) r.enabled = true;
                    }
                }
            }
        }

        // 2. Yeni snaplenen kılavuz hücrelerini gizle ki yerleştirilecek parça direkt gridin yerine geçsin
        foreach (var cell in newSnapped)
        {
            if (!occupiedCells.Contains(cell))
            {
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                {
                    r.enabled = false;
                }
            }
        }

        temporarilyHiddenGridCells = newSnapped;
    }

    public void ClearSnappedPreviewCells()
    {
        foreach (var cell in temporarilyHiddenGridCells)
        {
            if (!occupiedCells.Contains(cell))
            {
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                {
                    if (ShouldCellBeVisible(cell)) r.enabled = true;
                }
            }
        }
        temporarilyHiddenGridCells.Clear();
    }

    public bool IsComplete()
        => allShapeCells.Count == 0;

    // --- Smart Spawn Helpers ---

    public static List<Vector3Int> RotateCells(List<Vector3Int> cells, Quaternion q)
    {
        var result = new List<Vector3Int>(cells.Count);
        foreach (var c in cells)
        {
            Vector3 v = q * new Vector3(c.x, c.y, c.z);
            result.Add(new Vector3Int(
                Mathf.RoundToInt(v.x),
                Mathf.RoundToInt(v.y),
                Mathf.RoundToInt(v.z)));
        }

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        foreach (var c in result)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.z < minZ) minZ = c.z;
        }
        for (int i = 0; i < result.Count; i++)
            result[i] -= new Vector3Int(minX, minY, minZ);

        return result;
    }

    public List<Vector3Int> GetPossibleOffsets(List<Vector3Int> cells)
    {
        var valid = new List<Vector3Int>();
        var seen  = new HashSet<Vector3Int>();
        foreach (var t in targetCells)
        {
            if (occupiedCells.Contains(t)) continue;
            foreach (var c in cells)
            {
                var off = t - c;
                if (!seen.Add(off)) continue;
                if (CanPlace(cells, off)) valid.Add(off);
            }
        }
        return valid;
    }

    public List<Vector3Int> GetPossibleOffsetsOnLayer(List<Vector3Int> cells, int layerY)
    {
        SyncGridState();
        return PlacementValidator.GetPossibleOffsetsOnLayer(cells, layerY, State);
    }

    public bool CanPlaceOnLayer(List<Vector3Int> cells, Vector3Int offset, int layerY)
    {
        SyncGridState();
        return PlacementValidator.CanPlaceOnLayer(cells, offset, layerY, State);
    }

    public Color? GetMergeColor(List<Vector3Int> cells, Vector3Int offset)
    {
        var tempPlaced = new HashSet<Vector3Int>();
        foreach (var c in cells) tempPlaced.Add(c + offset);

        for (int y = gridMinY; y <= gridMaxY; y++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                var col = CheckLineForMerge(tempPlaced, y, z, true, false, false);
                if (col.HasValue) return col;
            }
        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                var col = CheckLineForMerge(tempPlaced, x, z, false, true, false);
                if (col.HasValue) return col;
            }
        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int y = gridMinY; y <= gridMaxY; y++)
            {
                var col = CheckLineForMerge(tempPlaced, x, y, false, false, true);
                if (col.HasValue) return col;
            }

        return null;
    }

    private Color? CheckLineForMerge(HashSet<Vector3Int> newCells, int a, int b, bool xAxis, bool yAxis, bool zAxis)
    {
        int lo = xAxis ? gridMinX : (yAxis ? gridMinY : gridMinZ);
        int hi = xAxis ? gridMaxX : (yAxis ? gridMaxY : gridMaxZ);

        bool hasNew = false;
        Color? foundCol = null;
        int targetCellCountInLine = 0;

        for (int v = lo; v <= hi; v++)
        {
            Vector3Int cell = xAxis ? new Vector3Int(v, a, b)
                            : yAxis  ? new Vector3Int(a, v, b)
                                     : new Vector3Int(a, b, v);

            if (targetCells.Contains(cell))
            {
                targetCellCountInLine++;
                if (newCells.Contains(cell))
                {
                    hasNew = true;
                }
                else if (occupiedCells.Contains(cell))
                {
                    if (cellColors.TryGetValue(cell, out Color c))
                    {
                        if (!foundCol.HasValue) foundCol = c;
                        else if (!ColorsApproxEqual(c, foundCol.Value)) return null;
                    }
                }
                else return null;
            }
        }

        if (targetCellCountInLine < 2) return null;
        if (!hasNew) return null;
        return foundCol ?? Color.white;
    }

    // --- X-Ray Occlusion (Drag Görüş Açıklığı) ---

    /// <summary>
    /// Sürüklenen parçanın snap konumuna göre, kamera ile parça arasındaki
    /// boş hedef grid hücrelerini gizler. Yerleştirilmiş parçalara dokunamaz.
    /// </summary>
    /// <summary>
    /// Suruklenen parcanin snap konumuna gore, kamera ile parca arasindaki
    /// bos hedef grid hucrelerini gizler. Yerlestirilmis parcalara dokunamaz.
    /// </summary>
    public void UpdateOccludingCells(Vector3 cameraPos, List<Vector3Int> pieceCells, Vector3Int snapOffset)
    {
        // Onceki turda gizlenmis hucreleri geri ac
        foreach (var cell in occludedGridCells)
        {
            if (!occupiedCells.Contains(cell) && !temporarilyHiddenGridCells.Contains(cell))
            {
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                    if (ShouldCellBeVisible(cell)) r.enabled = true;
            }
        }
        occludedGridCells.Clear();

        if (pieceCells == null || pieceCells.Count == 0) return;

        // Parcanin snap sonrasi kapladigi hucreleri hesapla
        var pieceBoardCells = new HashSet<Vector3Int>();
        foreach (var c in pieceCells) pieceBoardCells.Add(c + snapOffset);

        // Kamera isini ile capraz mesafe esigi
        float perpThreshold = Step * 0.75f;

        foreach (var targetCell in targetCells)
        {
            if (occupiedCells.Contains(targetCell)) continue;
            if (pieceBoardCells.Contains(targetCell)) continue;

            Vector3 targetWorld = CellToWorld(targetCell);
            bool shouldOcclude = false;

            foreach (var pieceCell in pieceBoardCells)
            {
                Vector3 pieceWorld = CellToWorld(pieceCell);
                Vector3 camToPiece = pieceWorld - cameraPos;
                float pieceDist = camToPiece.magnitude;
                if (pieceDist < 0.001f) continue;

                Vector3 camDir = camToPiece / pieceDist;

                float targetProj = Vector3.Dot(targetWorld - cameraPos, camDir);
                if (targetProj <= 0f || targetProj >= pieceDist) continue;

                Vector3 closestPt = cameraPos + camDir * targetProj;
                float perpDist = (targetWorld - closestPt).magnitude;

                if (perpDist < perpThreshold)
                {
                    shouldOcclude = true;
                    break;
                }
            }

            if (shouldOcclude)
            {
                occludedGridCells.Add(targetCell);
                if (targetRenderers.TryGetValue(targetCell, out var r) && r != null)
                    r.enabled = false;
            }
        }
    }
    /// <summary>
    /// Occlusion gizlemesini sıfırlar — sürükleme bittiğinde veya snap kaybolduğunda çağrılır.
    /// </summary>
    public void ClearOccludingCells()
    {
        foreach (var cell in occludedGridCells)
        {
            if (!occupiedCells.Contains(cell) && !temporarilyHiddenGridCells.Contains(cell))
            {
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                    if (ShouldCellBeVisible(cell)) r.enabled = true;
            }
        }
        occludedGridCells.Clear();
    }

    // --- Visual Focus System ---
    
    public enum VisualState
    {
        Normal,
        Darkened,
        HighlightedValid
    }

    private Dictionary<Renderer, Color> originalBaseColors = new Dictionary<Renderer, Color>();
    private Dictionary<Renderer, Color> originalEmissionColors = new Dictionary<Renderer, Color>();
    
    private Dictionary<Renderer, Color> pieceOriginalBaseColors = new Dictionary<Renderer, Color>();
    private Dictionary<Renderer, Color> pieceOriginalEmissionColors = new Dictionary<Renderer, Color>();
    private Dictionary<Renderer, bool> pieceOriginalEmissionEnabled = new Dictionary<Renderer, bool>();

    private Dictionary<Renderer, VisualState> activeStates = new Dictionary<Renderer, VisualState>();
    private bool isFocusModeActive = false;

    public void StartVisualFocus(DraggablePiece piece)
    {
        if (isFocusModeActive) StopVisualFocus(piece);

        isFocusModeActive = true;
        activeStates.Clear();
        pieceOriginalBaseColors.Clear();
        pieceOriginalEmissionColors.Clear();
        pieceOriginalEmissionEnabled.Clear();

        // 3. Save original colors for the dragged piece's renderers
        if (piece != null)
        {
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                
                // Eğer animasyon devam ediyorsa rengin açık kalmaması için tween'i bitir
                if (r.sharedMaterial != null) r.material.DOKill(true);
                
                Material mat = r.material; // Instantiate material
                
                Color baseCol = Color.white;
                if (mat.HasProperty("_BaseColor")) baseCol = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color")) baseCol = mat.GetColor("_Color");
                pieceOriginalBaseColors[r] = baseCol;

                Color emissiveCol = Color.clear;
                if (mat.HasProperty("_EmissionColor")) emissiveCol = mat.GetColor("_EmissionColor");
                pieceOriginalEmissionColors[r] = emissiveCol;

                pieceOriginalEmissionEnabled[r] = mat.IsKeywordEnabled("_EMISSION");
            }
        }
    }

    public void UpdateVisualFocus(DraggablePiece piece, bool isSnapped, Vector3Int snapOffset)
    {
        if (!isFocusModeActive || piece == null) return;

        var currentCells = piece.CurrentCells;
        if (currentCells == null || currentCells.Count == 0) return;

        // TryFindSnapOffset artık yalnızca gerçekten yerleştirilebilir konumlar için
        // isSnapped=true döndürür, bu yüzden burada geçersiz/kırmızı bir durum yok.
        VisualState pieceState = VisualState.Normal;

        foreach (var r in piece.GetComponentsInChildren<Renderer>())
        {
            if (r != null)
            {
                if (!activeStates.TryGetValue(r, out VisualState currentState) || currentState != pieceState)
                {
                    activeStates[r] = pieceState;
                    TransitionToState(r, pieceState, 0.2f);
                }
            }
        }
    }

    public void StopVisualFocus(DraggablePiece piece)
    {
        if (!isFocusModeActive) return;

        isFocusModeActive = false;
        float fadeDuration = 0.2f;

        if (piece != null)
        {
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                TransitionToState(r, VisualState.Normal, fadeDuration);
            }
        }
        activeStates.Clear();
        pieceOriginalBaseColors.Clear();
        pieceOriginalEmissionColors.Clear();
        pieceOriginalEmissionEnabled.Clear();
    }

    private void TransitionToState(Renderer r, VisualState state, float duration)
    {
        if (r == null) return;

        r.material.DOKill();

        Color targetBase = Color.white;
        Color targetEmission = Color.clear;
        bool enableEmission = false;

        bool isPieceRenderer = pieceOriginalBaseColors.ContainsKey(r);
        
        if (isPieceRenderer)
        {
            Color origBase = pieceOriginalBaseColors[r];
            Color origEmis = pieceOriginalEmissionColors[r];

            switch (state)
            {
                case VisualState.Normal:
                default:
                    targetBase = origBase;
                    targetEmission = origEmis;
                    if (pieceOriginalEmissionEnabled.TryGetValue(r, out bool wasEnabled))
                        enableEmission = wasEnabled;
                    else
                        enableEmission = origEmis != Color.clear && origEmis.maxColorComponent > 0.01f;
                    break;
            }
        }
        else
        {
            return;
        }

        if (enableEmission)
        {
            r.material.EnableKeyword("_EMISSION");
        }
        else
        {
            r.material.DisableKeyword("_EMISSION");
        }

        if (r.material.HasProperty("_BaseColor"))
        {
            r.material.DOColor(targetBase, "_BaseColor", duration).SetEase(Ease.OutQuad);
        }
        else if (r.material.HasProperty("_Color"))
        {
            r.material.DOColor(targetBase, "_Color", duration).SetEase(Ease.OutQuad);
        }

        if (r.material.HasProperty("_EmissionColor"))
        {
            r.material.DOColor(targetEmission, "_EmissionColor", duration).SetEase(Ease.OutQuad);
        }
    }

    // ─── FROZEN CELLS RESOLUTION ────────────────────────────────────────────────

    // Sürüklenen parçanın bağlı grubu ≥2 ise (tek 1'lik küp tek başına eritemez; 1+1 ≥2 olunca
    // eritir) ve parça buza DOĞRUDAN değiyorsa buz erir/kırılır. Yok olan yalnızca O AN SÜRÜKLENEN
    // parçanın TAMAMI (ör. L ise L'nin hepsi) — prefilled ve önceden yerleştirilmiş parçalar
    // (buza bağlı olsalar bile) KALIR; aksi halde katman boşalıp "hazır küp" mekaniği bozuluyordu.
    public bool CheckAndResolveFrozenCells(List<Vector3Int> newlyPlacedCells, System.Action<bool> onComplete)
    {
        if (newlyPlacedCells == null || newlyPlacedCells.Count == 0 || frozenCells.Count == 0)
        {
            onComplete?.Invoke(false);
            return false;
        }

        Vector3Int[] horizontalNeighbors = {
            Vector3Int.left, Vector3Int.right,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        HashSet<Vector3Int> cellsToThaw = new HashSet<Vector3Int>();   // kalan vuruş 0'a indi -> tam erime
        HashSet<Vector3Int> cellsToChip = new HashSet<Vector3Int>();   // hâlâ donuk, sadece sayaç azaldı
        HashSet<Vector3Int> cellsToDestroy = new HashSet<Vector3Int>(); // buzu kıran yerleştirilmiş bloklar
        HashSet<Vector3Int> hitFrozenThisCall = new HashSet<Vector3Int>(); // aynı hamlede bir buz yalnızca bir kez vurulsun

        // Min-2: buzu eritmek için buza değen küp YALNIZ olmamalı — sürüklenen parçanın bağlı
        // grubu (kendisi + yatay bağlı diğer dolular) en az 2 küp olmalı. Tek başına 1'lik küp
        // eritemez; 1+1 yan yana olunca (grup ≥2) eritir.
        var draggedGroup = GetConnectedOccupiedGroup(newlyPlacedCells[0], horizontalNeighbors);

        if (draggedGroup.Count >= 2)
        {
            // Yalnızca O AN SÜRÜKLENEN parçanın (newlyPlacedCells) buza DOĞRUDAN değen hücreleri
            // buzu vurur ("buza değen parça" = sürüklenen parça).
            foreach (var cell in newlyPlacedCells)
            {
                foreach (var offset in horizontalNeighbors)
                {
                    Vector3Int neighbor = cell + offset;
                    if (!frozenCells.Contains(neighbor)) continue;
                    if (!hitFrozenThisCall.Add(neighbor)) continue;

                    int currentHits = iceRemainingHits.TryGetValue(neighbor, out int h) ? h : 1;
                    currentHits--;
                    iceRemainingHits[neighbor] = currentHits;

                    if (currentHits <= 0)
                    {
                        cellsToThaw.Add(neighbor);
                    }
                    else
                    {
                        cellsToChip.Add(neighbor);
                    }
                }
            }
        }

        if (cellsToThaw.Count == 0 && cellsToChip.Count == 0)
        {
            onComplete?.Invoke(false);
            return false;
        }

        // Yok olan: SADECE bu hamlede SÜRÜKLENEN parçanın TAMAMI (ör. L ise L'nin hepsi patlar).
        // Prefilled ve daha önce yerleştirilen parçalar newlyPlacedCells'te OLMADIĞI için asla
        // yok olmaz — buzu eriten/kıran yalnızca şu an sürüklenen parça gider.
        foreach (var c in newlyPlacedCells)
            if (occupiedCells.Contains(c)) cellsToDestroy.Add(c);

        StartCoroutine(AnimateThawAndDestroy(cellsToThaw, cellsToChip, cellsToDestroy, () => onComplete?.Invoke(true)));
        return true;
    }

    private HashSet<Vector3Int> GetConnectedOccupiedGroup(Vector3Int start, Vector3Int[] horizontalNeighbors)
    {
        var visited = new HashSet<Vector3Int> { start };
        var stack = new Stack<Vector3Int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            foreach (var offset in horizontalNeighbors)
            {
                Vector3Int neighbor = cur + offset;
                if (visited.Contains(neighbor)) continue;
                if (!occupiedCells.Contains(neighbor)) continue;

                visited.Add(neighbor);
                stack.Push(neighbor);
            }
        }

        return visited;
    }

    // start hücresinden başlayarak, occupiedCells içinde 'speciesIndex' ile aynı türde olan ve
    // yatay komşuluk üzerinden birbirine bağlı tüm hücreleri (start dahil) döndürür.
    private HashSet<Vector3Int> FloodFillSameSpecies(Vector3Int start, int speciesIndex, Vector3Int[] horizontalNeighbors)
    {
        var visited = new HashSet<Vector3Int> { start };
        var stack = new Stack<Vector3Int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            foreach (var offset in horizontalNeighbors)
            {
                Vector3Int neighbor = cur + offset;
                if (visited.Contains(neighbor)) continue;
                if (!occupiedCells.Contains(neighbor)) continue;
                if (!cellMatIndex.TryGetValue(neighbor, out int idx) || idx != speciesIndex) continue;

                visited.Add(neighbor);
                stack.Push(neighbor);
            }
        }

        return visited;
    }

    private static readonly Vector3Int[] SixDirNeighbors = {
        Vector3Int.left, Vector3Int.right,
        Vector3Int.up, Vector3Int.down,
        new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
    };

    // Aynı türden (cellMatIndex) 3 veya daha fazla hücre birbirine bağlandığında oluşan
    // parıldama/parlama (SparkleEffect) efekti özelliği kaldırıldı.
    // Bu fonksiyon artık sadece var olan eski parıldama efektlerini durdurup temizler.
    public void RefreshSpeciesSparkle()
    {
        var toStop = new List<Vector3Int>(sparklingCells);
        foreach (var cell in toStop)
        {
            sparklingCells.Remove(cell);
            if (cellObjects.TryGetValue(cell, out var go) && go != null)
            {
                var sp = go.GetComponent<SparkleEffect>();
                if (sp != null)
                {
                    sp.StopAndRestore();
                    Object.Destroy(sp);
                }
            }
        }
    }

    // AttachSpeciesVisual (LevelManager.cs) tarafından prefilled hücrelere eklenen hayvan
    // modelinin (çocuk, "SpeciesVisual") ekleneceği ANDA sakladığı orijinal kutu mesh'i —
    // AttachSpeciesVisual, kutu+hayvan çakışıp çift görünmesin diye kutunun kendi MeshFilter'ını
    // null'lar; bu hücre daha sonra RestoreAsGhostTarget ile ghost'a döndürülürken kutuya
    // GERİ mesh verebilmek için burada tutuluyor (bkz. CacheOriginalPrefilledMesh).
    private Dictionary<Vector3Int, Mesh> prefilledOriginalMesh = new Dictionary<Vector3Int, Mesh>();

    public void CacheOriginalPrefilledMesh(Vector3Int cell, Mesh mesh)
    {
        if (mesh != null) prefilledOriginalMesh[cell] = mesh;
    }

    // Bir Renderer'ı (var olan bir hedef ghost'u ya da patlamış bir prefilled küpü) normal,
    // boş "hedef" (ghost) hücre görünümüne döndürür ve targetRenderers'a kaydeder. Prefilled
    // hücrelerde Initialize() hiç ayrı bir ghost objesi üretmediği için, o hücredeki küp
    // patladığında geriye gösterilecek hiçbir şey kalmıyordu — bu fonksiyon küpün kendi
    // Renderer'ını ghost olarak yeniden kullanır.
    private void RestoreAsGhostTarget(Vector3Int cell, Renderer rend)
    {
        if (rend == null) return;

        // Hayvan modeli çocuğunu (varsa) yok et — aksi halde kutu ghost'a dönüştürülse bile
        // ayrı bir Renderer'a sahip bu çocuk görsel olarak yerinde kalmaya devam ediyordu.
        var speciesVisual = rend.transform.Find("SpeciesVisual");
        if (speciesVisual != null) Object.Destroy(speciesVisual.gameObject);

        // Kutunun mesh'i AttachSpeciesVisual tarafından null'lanmış olabilir (bkz. yukarıdaki
        // not) — materyal değişikliğinin görünür olması için geri veriyoruz.
        var meshFilter = rend.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh == null &&
            prefilledOriginalMesh.TryGetValue(cell, out Mesh originalMesh))
        {
            meshFilter.sharedMesh = originalMesh;
        }

        rend.transform.localScale = Vector3.one * CellSize;
        rend.enabled = true;

        if (LevelManager.Instance != null && LevelManager.Instance.ghostTargetMaterial != null)
        {
            var mats = new Material[rend.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = LevelManager.Instance.ghostTargetMaterial;
            rend.sharedMaterials = mats;
        }

        targetRenderers[cell] = rend;
        RefreshLayerVisibility();
    }

    // Eriyen buz hücreleri normal boş hedef hücreye dönerken, cellsToDestroy içindeki 
    // hücreler de patlayıp boşalır. (Eski mantığa dönüldüğü için artık cellsToDestroy genelde boş gelir).
    private IEnumerator AnimateThawAndDestroy(HashSet<Vector3Int> cellsToThaw, HashSet<Vector3Int> cellsToChip, HashSet<Vector3Int> cellsToDestroy, System.Action onComplete)
    {
        if ((cellsToThaw != null && cellsToThaw.Count > 0) || (cellsToChip != null && cellsToChip.Count > 0))
        {
            AudioManager.Instance?.PlayIceMeltSound();
        }

        foreach (var cell in cellsToThaw)
        {
            frozenCells.Remove(cell);
            iceRemainingHits.Remove(cell);
            TowerMiniPreview.Instance?.OnCellRemoved(cell);
        }

        foreach (var cell in cellsToDestroy)
        {
            occupiedCells.Remove(cell);
            cellColors.Remove(cell);
            cellMatIndex.Remove(cell);
            TowerMiniPreview.Instance?.OnCellRemoved(cell);
        }

        int pendingEffects = cellsToThaw.Count + cellsToChip.Count + cellsToDestroy.Count;
        if (pendingEffects == 0)
        {
            onComplete?.Invoke();
            yield break;
        }

        System.Action onOneEffectDone = () =>
        {
            pendingEffects--;
            if (pendingEffects <= 0)
            {
                RefreshLayerVisibility();
                onComplete?.Invoke();
            }
        };

        foreach (var cell in cellsToThaw)
        {
            if (targetRenderers.TryGetValue(cell, out var rend) && rend != null)
            {
                // Erime boyunca bu hücreyi görünürlük tazelemesinden muaf tut; aksi
                // halde araya giren bir RefreshLayerVisibility, MaterialPropertyBlock
                // ile solma efektinin üzerine ghost rengini damgalıyor (bkz. oradaki not).
                meltingIceCells.Add(cell);

                var iceGo = GetIceVisual(cell);

                System.Action onThisIceDone = () => {
                    meltingIceCells.Remove(cell);
                    ForceRemoveIceVisual(cell);   // erime bitti, model gider
                    RestoreAsGhostTarget(cell, rend);
                    onOneEffectDone();
                };

                if (IceBreakEffect.Instance != null)
                {
                    // Erime BUZ MODELİNE oynatılır: hedef küpün renderer'ı buzluyken
                    // kapalı olduğu için ona oynatmak görünmez kalırdı. Model materyali
                    // saydam olduğu için PlayIceMelt'in alfa solması artık gerçekten çalışır.
                    IceBreakEffect.PlayIceMelt(iceGo != null ? iceGo : rend.gameObject, onThisIceDone);
                }
                else
                {
                    onThisIceDone();
                }
            }
            else
            {
                onOneEffectDone();
            }
        }

        // Kısmi erime (chip): buz hâlâ frozenCells'te kalır, sadece üzerindeki sayaç bir
        // azalır ve IceBreakEffect.PlayIceChip ile küçük bir "kırpılma" animasyonu oynar.
        foreach (var cell in cellsToChip)
        {
            var iceGo = GetIceVisual(cell);
            int remaining = iceRemainingHits.TryGetValue(cell, out int r) ? r : 0;
            var marker = iceGo != null ? iceGo.GetComponent<IceVisualMarker>() : null;
            if (marker != null)
            {
                marker.UpdateCount(remaining, true);
            }

            if (iceGo != null && IceBreakEffect.Instance != null)
            {
                IceBreakEffect.PlayIceChip(iceGo, remaining, marker != null ? marker.totalHits : remaining + 1, onOneEffectDone);
            }
            else
            {
                onOneEffectDone();
            }
        }

        foreach (var cell in cellsToDestroy)
        {
            if (cellObjects.TryGetValue(cell, out var go) && go != null)
            {
                cellObjects.Remove(cell);
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                {
                    if (ShouldCellBeVisible(cell)) r.enabled = true;
                }
                AnimateAndDestroy(go, 0f, true, onOneEffectDone);
            }
            else if (prefilledRenderers.TryGetValue(cell, out var prefRend) && prefRend != null)
            {
                // Prefilled hücrenin kendi ayrı bir "hedef ghost" objesi yok — kendi
                // renderer'ı yok edilmez, RestoreAsGhostTarget ile ghost'a dönüştürülür
                // (bkz. o fonksiyonun üstündeki not).
                //
                // GÖRSEL EŞİTLİK: oyuncu küpü yok edilirken AnimateAndDestroy oynuyor;
                // prefilled hücrede ise farklı bir kıvılcım efekti vardı ve etki ANINDA
                // bitiyordu. İkisi birebir aynı görünsün diye, prefilled hücrenin
                // görselinin GEÇİCİ BİR KOPYASI çıkarılıp ona AYNI animasyon oynatılıyor.
                // Gerçek hücre arkada hemen ghost'a dönüşüyor — bu da oyuncu küpü
                // yolundaki "alttaki ghost'u hemen aç" davranışının aynısı.
                prefilledRenderers.Remove(cell);

                GameObject visualClone = Instantiate(
                    prefRend.gameObject, prefRend.transform.position, prefRend.transform.rotation);
                visualClone.name = "PrefilledBurst";
                visualClone.transform.localScale = prefRend.transform.lossyScale;

                foreach (var cc in visualClone.GetComponentsInChildren<Collider>(true))
                    cc.enabled = false;
                // Görünürlük aynası kopyada kalırsa, orijinal renderer ghost'a dönüşünce
                // kopyanın hayvanı da onunla birlikte kaybolurdu.
                foreach (var mirror in visualClone.GetComponentsInChildren<RendererVisibilityMirror>(true))
                    Destroy(mirror);
                foreach (var rr in visualClone.GetComponentsInChildren<Renderer>(true))
                    rr.enabled = true;

                RestoreAsGhostTarget(cell, prefRend);
                AnimateAndDestroy(visualClone, 0f, true, onOneEffectDone);
            }
            else
            {
                onOneEffectDone();
            }
        }

        yield break;
    }

    public void CreateIceShatterEffect(Vector3 centerPosition)
    {
        int numShards = 16; // Rich shard density for ice
        Shader blockShader = null;
        if (LevelManager.Instance != null && LevelManager.Instance.ghostTargetMaterial != null)
        {
            blockShader = LevelManager.Instance.ghostTargetMaterial.shader;
        }
        if (blockShader == null)
        {
            blockShader = Shader.Find("Universal Render Pipeline/Lit");
        }

        for (int i = 0; i < numShards; i++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = shard.GetComponent<Collider>();
            if (col != null) Destroy(col);

            shard.transform.position = centerPosition + new Vector3(
                Random.Range(-0.2f, 0.2f),
                Random.Range(-0.2f, 0.2f),
                Random.Range(-0.2f, 0.2f)
            );
            
            // Varied crystal shapes (needles, slivers, blocks)
            float sx = Random.Range(0.08f, 0.35f);
            float sy = Random.Range(0.08f, 0.35f);
            float sz = Random.Range(0.08f, 0.35f);
            shard.transform.localScale = new Vector3(sx, sy, sz);

            var r = shard.GetComponent<Renderer>();
            if (r != null && blockShader != null)
            {
                r.material = new Material(blockShader);
                r.material.color = new Color(0.06f, 0.32f, 0.58f, 0.90f);
                
                r.material.EnableKeyword("_EMISSION");
                r.material.SetColor("_EmissionColor", new Color(0.01f, 0.06f, 0.15f));
            }

            Vector3 burstDir = Random.insideUnitSphere * Random.Range(1.8f, 3.2f);
            burstDir.y = Mathf.Abs(burstDir.y) + Random.Range(0.5f, 1.5f); // upward burst force

            Vector3 targetPosition = shard.transform.position + burstDir;

            float duration = Random.Range(0.45f, 0.65f);
            shard.transform.DOMove(targetPosition, duration).SetEase(Ease.OutQuad);
            shard.transform.DORotate(new Vector3(Random.Range(-360, 360), Random.Range(-360, 360), Random.Range(-360, 360)), duration);
            shard.transform.DOScale(Vector3.zero, duration).SetEase(Ease.InQuad).OnComplete(() => {
                if (r != null && r.material != null) Destroy(r.material);
                Destroy(shard);
            });
        }
    }

    private List<GameObject> SpawnProceduralCracks(Vector3 center, Transform parent)
    {
        List<GameObject> cracks = new List<GameObject>();
        int numLines = 5;
        
        Shader blockShader = null;
        if (LevelManager.Instance != null && LevelManager.Instance.ghostTargetMaterial != null)
        {
            blockShader = LevelManager.Instance.ghostTargetMaterial.shader;
        }
        if (blockShader == null)
        {
            blockShader = Shader.Find("Universal Render Pipeline/Lit");
        }

        for (int i = 0; i < numLines; i++)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = line.GetComponent<Collider>();
            if (col != null) Destroy(col);

            line.transform.SetParent(parent, true);

            // Thin crack line
            line.transform.localScale = new Vector3(
                Random.Range(0.015f, 0.03f),
                Random.Range(0.2f, 0.55f),
                Random.Range(0.015f, 0.03f)
            );

            // Positioned on the face of the cube
            Vector3 offset = new Vector3(
                Random.Range(-0.46f, 0.46f),
                Random.Range(-0.46f, 0.46f),
                Random.Range(-0.46f, 0.46f)
            );
            
            // Push one axis to the extreme to put it on the face
            int axis = Random.Range(0, 3);
            if (axis == 0) offset.x = Mathf.Sign(offset.x) * 0.47f;
            else if (axis == 1) offset.y = Mathf.Sign(offset.y) * 0.47f;
            else offset.z = Mathf.Sign(offset.z) * 0.47f;

            line.transform.position = center + offset;
            
            // Random rotation for organic cracks
            line.transform.localRotation = Quaternion.Euler(
                Random.Range(0f, 360f),
                Random.Range(0f, 360f),
                Random.Range(0f, 360f)
            );

            var r = line.GetComponent<Renderer>();
            if (r != null && blockShader != null)
            {
                r.material = new Material(blockShader);
                // Dark crack color
                r.material.color = new Color(0.12f, 0.18f, 0.28f, 0.92f);
                r.material.EnableKeyword("_EMISSION");
                r.material.SetColor("_EmissionColor", new Color(0.02f, 0.08f, 0.18f));
            }

            cracks.Add(line);
        }

        return cracks;
    }

    public void CreateShatterEffect(Vector3 centerPosition, Color shardColor)
    {
        int numShards = 8;
        Shader blockShader = null;
        if (LevelManager.Instance != null && LevelManager.Instance.ghostTargetMaterial != null)
        {
            blockShader = LevelManager.Instance.ghostTargetMaterial.shader;
        }
        if (blockShader == null)
        {
            blockShader = Shader.Find("Universal Render Pipeline/Lit");
        }

        for (int i = 0; i < numShards; i++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = shard.GetComponent<Collider>();
            if (col != null) Destroy(col);

            shard.transform.position = centerPosition + new Vector3(
                Random.Range(-0.25f, 0.25f),
                Random.Range(-0.25f, 0.25f),
                Random.Range(-0.25f, 0.25f)
            );
            shard.transform.localScale = Vector3.one * Random.Range(0.18f, 0.32f);

            var r = shard.GetComponent<Renderer>();
            if (r != null && blockShader != null)
            {
                r.material = new Material(blockShader);
                r.material.color = shardColor;
                
                // If it looks like ice (bluish-white), enable emission
                if (shardColor.r > 0.6f && shardColor.g > 0.8f)
                {
                    r.material.EnableKeyword("_EMISSION");
                    r.material.SetColor("_EmissionColor", new Color(0.1f, 0.4f, 0.7f) * 1.5f);
                }
            }

            Vector3 burstDir = new Vector3(
                Random.Range(-1.8f, 1.8f),
                Random.Range(0.5f, 2.2f),
                Random.Range(-1.8f, 1.8f)
            );
            Vector3 targetPosition = shard.transform.position + burstDir;

            shard.transform.DOMove(targetPosition, 0.55f).SetEase(Ease.OutQuad);
            shard.transform.DORotate(new Vector3(Random.Range(-270, 270), Random.Range(-270, 270), Random.Range(-270, 270)), 0.55f);
            shard.transform.DOScale(Vector3.zero, 0.55f).SetEase(Ease.InQuad).OnComplete(() => {
                if (r != null && r.material != null) Destroy(r.material);
                Destroy(shard);
            });
        }
    }

    private void SlideDownRemainingLayers(int clearedY)
    {
        foreach (var kvp in cellObjects)
        {
            if (kvp.Key.y >= clearedY && kvp.Value != null)
            {
                var t = kvp.Value.transform;
                float stagger = (Mathf.Abs(kvp.Key.x) + Mathf.Abs(kvp.Key.z)) * 0.03f;
                float targetY = t.localPosition.y - Step;

                Sequence dropSeq = DOTween.Sequence().SetDelay(stagger);
                dropSeq.Append(t.DOLocalMoveY(targetY, 0.38f).SetEase(Ease.OutBack, 1.15f));
                dropSeq.AppendCallback(() =>
                {
                    t.DOPunchScale(new Vector3(0.14f, -0.16f, 0.14f), 0.22f, 6, 0.5f);
                });
            }
        }

        foreach (var kvp in targetRenderers)
        {
            if (kvp.Key.y >= clearedY && kvp.Value != null)
            {
                var t = kvp.Value.transform;
                float stagger = (Mathf.Abs(kvp.Key.x) + Mathf.Abs(kvp.Key.z)) * 0.03f;
                float targetY = t.localPosition.y - Step;

                t.DOLocalMoveY(targetY, 0.38f).SetEase(Ease.OutBack, 1.15f).SetDelay(stagger);
            }
        }

        foreach (var kvp in prefilledRenderers)
        {
            if (kvp.Key.y >= clearedY && kvp.Value != null)
            {
                var t = kvp.Value.transform;
                float stagger = (Mathf.Abs(kvp.Key.x) + Mathf.Abs(kvp.Key.z)) * 0.03f;
                float targetY = t.localPosition.y - Step;

                Sequence dropSeq = DOTween.Sequence().SetDelay(stagger);
                dropSeq.Append(t.DOLocalMoveY(targetY, 0.38f).SetEase(Ease.OutBack, 1.15f));
                dropSeq.AppendCallback(() =>
                {
                    t.DOPunchScale(new Vector3(0.14f, -0.16f, 0.14f), 0.22f, 6, 0.5f);
                });
            }
        }
    }

    // ── İpucu (Hint) 3D Tahta Vurgulama Sistemi ─────────────────────────────
    public void SetHintGridHighlights(List<Vector3Int> targetCells)
    {
        highlightedCells.Clear();

        if (targetCells != null && targetCells.Count > 0)
        {
            foreach (var cell in targetCells)
            {
                highlightedCells.Add(cell);
            }
        }

        RefreshLayerVisibility();
    }

    public void ClearHintGridHighlights()
    {
        highlightedCells.Clear();
        RefreshLayerVisibility();
    }

    /// <summary>
    /// Belirtilen katman Y'sinde henüz yerleştirilmiş parçayla dolmamış hedef hücreleri döndürür.
    /// </summary>
    public List<Vector3Int> GetLayerUnfilledCells(int layerY)
    {
        List<Vector3Int> result = new List<Vector3Int>();
        if (targetCells == null) return result;

        foreach (var cell in targetCells)
        {
            if (cell.y == layerY && !occupiedCells.Contains(cell) && !IsCellPrefilled(cell))
            {
                result.Add(cell);
            }
        }
        return result;
    }

    private Material hintTransparentMaterial;

    private Material GetOrCreateHintTransparentMaterial()
    {
        if (hintTransparentMaterial != null) return hintTransparentMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Standard");

        hintTransparentMaterial = new Material(shader);
        hintTransparentMaterial.name = "HintTransparentMat";

        if (hintTransparentMaterial.HasProperty("_Surface")) hintTransparentMaterial.SetFloat("_Surface", 1f); // Transparent
        if (hintTransparentMaterial.HasProperty("_Blend")) hintTransparentMaterial.SetFloat("_Blend", 0f); // Alpha blend
        if (hintTransparentMaterial.HasProperty("_SrcBlend")) hintTransparentMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (hintTransparentMaterial.HasProperty("_DstBlend")) hintTransparentMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (hintTransparentMaterial.HasProperty("_ZWrite")) hintTransparentMaterial.SetFloat("_ZWrite", 0f);

        hintTransparentMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        hintTransparentMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        hintTransparentMaterial.EnableKeyword("_EMISSION");
        hintTransparentMaterial.SetColor("_EmissionColor", Color.white);
        hintTransparentMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;

        return hintTransparentMaterial;
    }
}