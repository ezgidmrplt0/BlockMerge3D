using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

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
        if (go.GetComponent<IceVisualMarker>() == null)
        {
            go.AddComponent<IceVisualMarker>();
        }

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

    private void Awake() { Instance = this; }

    public HashSet<Vector3Int> highlightedCells = new HashSet<Vector3Int>();

    /// <summary>Bir önceki karede vurgulanan hücreler. Vurgudan ÇIKAN hücrenin sarı
    /// damgasının geri alınabilmesi için tutulur (bkz. Update).</summary>
    private readonly HashSet<Vector3Int> lastHighlightedCells = new HashSet<Vector3Int>();

    private void Update()
    {
        // Grid hücre parlatma (sarı yerleştirme vurgusu) sadece ilk 5 tutorial seviyesinde aktiftir
        bool isTutorialLevel = GameManager.Instance == null || GameManager.Instance.CurrentLevelNumber <= 5;

        // 1. Dinamik parlatma güncellemesi (sürüklenen parça varsa)
        if (DraggablePiece.activeDrag != null && isTutorialLevel)
        {
            highlightedCells.Clear();
            var drag = DraggablePiece.activeDrag;
            if (drag.IsBeingDragged && !drag.IsPlaced)
            {
                var tut = TutorialOverlay.Instance;
                bool isLevel3 = (GameManager.Instance != null && GameManager.Instance.CurrentLevelNumber == 3)
                    || (LevelManager.Instance != null && LevelManager.Instance.currentLevel != null && 
                       (LevelManager.Instance.currentLevel.levelName.StartsWith("Tutorial_3") || LevelManager.Instance.currentLevel.levelName == "LEVEL 3"));

                if (tut != null && tut.RestrictDragHighlights && tut.DragHighlightCells.Count > 0)
                {
                    foreach (var c in tut.DragHighlightCells)
                    {
                        highlightedCells.Add(c);
                    }
                }
                else if (isLevel3)
                {
                    // SADECE LEVEL 3: 3'lü tüm geçerli konumları parlatma!
                    // Yalnızca tek bir doğru konuma (2 hücreye) yerleşecek yer sarı parlasın.
                    var cells = drag.CurrentCells;
                    var offsets = GetPossibleOffsetsOnLayer(cells, ActiveLayerY);
                    if (offsets != null && offsets.Count > 0)
                    {
                        Vector3Int targetOff = offsets[0];
                        foreach (var c in cells)
                        {
                            highlightedCells.Add(c + targetOff);
                        }
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

        // 2. Parıldayan hücrelerin animasyonu (altın sarısı puls efekti)
        if (highlightedCells.Count > 0)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
            Color highlightColor = Color.Lerp(new Color(1.0f, 0.85f, 0.2f, 0.85f), new Color(1.0f, 1.0f, 0.5f, 0.95f), pulse);
            Color emissionColor = new Color(0.9f, 0.7f, 0.1f) * (0.8f + 1.2f * pulse);

            foreach (var cell in highlightedCells)
            {
                if (targetRenderers.TryGetValue(cell, out Renderer r) && r != null && r.enabled)
                {
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

    public void SetActiveLayer(int y)
    {
        ActiveLayerY = Mathf.Clamp(y, gridMinY, gridMaxY);
        lineClearEnabled = false;
        RefreshLayerVisibility();
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
        }

        if (shapeHolder != null && shapeHolder.gridSize != Vector3Int.zero)
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
            gridMinX = gridMinY = gridMinZ = int.MaxValue;
            gridMaxX = gridMaxY = gridMaxZ = int.MinValue;
            foreach (var c in allShapeCells)
            {
                if (c.x < gridMinX) gridMinX = c.x; if (c.x > gridMaxX) gridMaxX = c.x;
                if (c.y < gridMinY) gridMinY = c.y; if (c.y > gridMaxY) gridMaxY = c.y;
                if (c.z < gridMinZ) gridMinZ = c.z; if (c.z > gridMaxZ) gridMaxZ = c.z;
            }
        }

        // Buz hücreleri yalnızca seviyenin kendi CubeShapeDataHolder.frozenCells listesinden
        // yüklenir. Liste boşsa seviyede hiç buz yoktur — rastgele/otomatik buzlama YAPILMAZ
        // (önceden burada boş listeyi "tanımlanmamış" sayıp katman başına rastgele %25 hücreyi
        // buzlayan bir fallback vardı; bu, buzsuz tasarlanan seviyeleri de oynanamaz hale
        // getirebiliyordu — özellikle tek parçanın tüm tahtayı kapladığı küçük seviyelerde).
        if (shapeHolder != null && shapeHolder.frozenCells != null)
        {
            Debug.Log($"[GridManager] Initialize: shapeHolder.frozenCells.Count = {shapeHolder.frozenCells.Count}");
            foreach (var cell in shapeHolder.frozenCells)
            {
                frozenCells.Add(cell);
            }
        }
        else
        {
            Debug.Log("[GridManager] Initialize: shapeHolder or shapeHolder.frozenCells is NULL!");
        }

        // Doldurulması gereken ilk katmanı bul (üstten alta — bkz. TryFindNextRequiredLayer).
        // targetCells yerine allShapeCells kullanılır; böylece gizli/prefilled/frozen
        // hücreleri bulunan katmanlar da doğru şekilde hesaba katılır.
        if (!TryFindNextRequiredLayer(out int nextRequiredLayer))
            ActiveLayerY = gridMaxY;
        else
            ActiveLayerY = nextRequiredLayer;
        lineClearEnabled = false; // Layer-by-layer mode
        RefreshLayerVisibility();
    }

    private static int ParseCoordinate(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        string clean = "";
        foreach (char c in s)
        {
            if (char.IsDigit(c) || c == '-') clean += c;
            else break;
        }
        if (int.TryParse(clean, out int val)) return val;
        return 0;
    }

    private static MaterialPropertyBlock _propBlock;
    private static MaterialPropertyBlock PropBlock => _propBlock ??= new MaterialPropertyBlock();

    public void RefreshLayerVisibility()
    {
        bool isPanelMode = false;
        if (CameraOrbit.Instance != null && CameraOrbit.Instance.IsInPanelMode)
        {
            isPanelMode = true;
        }

        bool applyScale = !IsExplodingLayer;

        // Hedef (ghost) renderer'ları kontrol et
        foreach (var kvp in targetRenderers)
        {
            Vector3Int cell = kvp.Key;
            Renderer r = kvp.Value;

            // ERİME SÜRERKEN DOKUNMA. IceBreakEffect.PlayIceMelt solma efektini
            // renderer.material üzerinden yapıyor; buradaki renk yazımı ise
            // MaterialPropertyBlock ile ve o materyal özelliklerini EZİYOR.
            // Erime ortasında araya giren bir tazeleme (ör. prefilled hücre
            // patlayınca RestoreAsGhostTarget'ın senkron çağırdığı tazeleme)
            // solmakta olan buzun üzerine ghost rengini damgalıyor ve buz
            // gri/tuhaf bir renge bürünüyordu. Erime bitince callback zaten
            // hücreyi ghost'a çevirip tazelemeyi kendisi tetikliyor.
            if (meltingIceCells.Contains(cell)) continue;

            if (r != null)
            {
                if (isPanelMode)
                {
                    if (cell.y == ActiveLayerY)
                    {
                        r.enabled = true;
                        if (applyScale) r.transform.localScale = Vector3.one * CellSize;
                    }
                    else if (cell.y < ActiveLayerY)
                    {
                        r.enabled = true;
                        if (applyScale) r.transform.localScale = Vector3.one * CellSize;
                    }
                    else
                    {
                        r.enabled = false;
                    }
                }
                else
                {
                    r.enabled = true; // 3D modunda hepsi görünür
                    if (applyScale) r.transform.localScale = Vector3.one * CellSize;
                }

                // Hücre gerçek (opak) bir parça ile doluysa hedef/ghost küpünü TAMAMEN gizle.
                // Öncesinde sadece şeffaflaştırılıyordu (a = 0.12), ama ZWrite kapalı transparan
                // ghost materyali opak yerleştirilmiş parçanın üzerinde/içinde aynı hacimde
                // render edildiğinden çift görüntü / hatalı derinlik sıralaması (görsel "tuhaflık")
                // oluşuyordu. Buz (frozen) hücreleri bu kuraldan muaf: onlar henüz kırılmadıysa
                // görünür kalmalı.
                if (occupiedCells.Contains(cell) && !frozenCells.Contains(cell))
                {
                    r.enabled = false;
                }

                // Buz artık kendi 3D modeliyle gösteriliyor: modeli hücreye ekle ve
                // altındaki küpün renderer'ını kapat ki buzun içinden sırıtmasın.
                // Panel modunda alt katmanlar soluklaşırken model gizlenir.
                bool isFrozenHere = frozenCells.Contains(cell);
                if (isFrozenHere)
                {
                    EnsureIceVisual(cell, r);
                    var iceGo = GetIceVisual(cell);
                    if (iceGo != null)
                    {
                        bool hideIce = isPanelMode && cell.y != ActiveLayerY;
                        iceGo.SetActive(!hideIce);
                        Debug.Log($"[GridManager] RefreshLayerVisibility: cell={cell}, hideIce={hideIce}, iceGo.activeSelf={iceGo.activeSelf}, scale={iceGo.transform.localScale}");
                        if (!hideIce) r.enabled = false;
                    }
                    else
                    {
                        Debug.Log($"[GridManager] RefreshLayerVisibility: cell={cell}, iceGo is NULL!");
                    }
                }
                else
                {
                    RemoveIceVisual(cell);
                }

                if (r.enabled)
                {
                    r.GetPropertyBlock(PropBlock);
                    if (isFrozenHere)
                    {
                        Color iceColor = new Color(0.06f, 0.32f, 0.58f, 0.90f);
                        if (isPanelMode && cell.y < ActiveLayerY) iceColor.a = 0.2f; // Faded ice
                        PropBlock.SetColor("_BaseColor", iceColor);
                        PropBlock.SetColor("_Color", iceColor);

                        Color emissionColor = new Color(0.01f, 0.06f, 0.15f) * (isPanelMode && cell.y < ActiveLayerY ? 0.2f : 1.0f);
                        PropBlock.SetColor("_EmissionColor", emissionColor);
                    }
                    else
                    {
                        if (highlightedCells.Contains(cell))
                        {
                            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
                            Color highlightColor = Color.Lerp(new Color(1.0f, 0.85f, 0.2f, 0.85f), new Color(1.0f, 1.0f, 0.5f, 0.95f), pulse);
                            Color emissionColor = new Color(0.9f, 0.7f, 0.1f) * (0.8f + 1.2f * pulse);

                            PropBlock.SetColor("_BaseColor", highlightColor);
                            PropBlock.SetColor("_Color", highlightColor);
                            PropBlock.SetColor("_EmissionColor", emissionColor);
                        }
                        else
                        {
                            Color defaultColor = new Color(0.41f, 0.57f, 0.35f, 0.53f);
                            if (LevelManager.Instance != null && LevelManager.Instance.ghostTargetMaterial != null)
                            {
                                defaultColor = LevelManager.Instance.ghostTargetMaterial.color;
                            }

                            if (isPanelMode && cell.y < ActiveLayerY) defaultColor.a *= 0.33f; // Faded target base
                            PropBlock.SetColor("_BaseColor", defaultColor);
                            PropBlock.SetColor("_Color", defaultColor);
                            PropBlock.SetColor("_EmissionColor", Color.clear);
                        }
                    }
                    r.SetPropertyBlock(PropBlock);
                }
            }
        }

        // Prefilled blokları kontrol et (katman görünürlüğü)
        foreach (var kvp in prefilledRenderers)
        {
            Vector3Int cell = kvp.Key;
            Renderer r = kvp.Value;
            if (r != null)
            {
                if (isPanelMode)
                {
                    if (cell.y == ActiveLayerY)
                    {
                        r.enabled = true;
                        if (applyScale) r.transform.localScale = Vector3.one * CellSize;
                        
                        r.GetPropertyBlock(PropBlock);
                        Color c = r.sharedMaterial != null ? GetMaterialColor(r.sharedMaterial) : Color.white;
                        c.a = 1.0f;
                        PropBlock.SetColor("_BaseColor", c);
                        PropBlock.SetColor("_Color", c);
                        r.SetPropertyBlock(PropBlock);
                    }
                    else if (cell.y < ActiveLayerY)
                    {
                        r.enabled = true;
                        if (applyScale) r.transform.localScale = Vector3.one * CellSize;
                        
                        r.GetPropertyBlock(PropBlock);
                        Color c = r.sharedMaterial != null ? GetMaterialColor(r.sharedMaterial) : Color.white;
                        c.a = 0.35f; // faded prefilled base
                        PropBlock.SetColor("_BaseColor", c);
                        PropBlock.SetColor("_Color", c);
                        r.SetPropertyBlock(PropBlock);
                    }
                    else
                    {
                        r.enabled = false;
                    }
                }
                else
                {
                    r.enabled = true; // 3D modunda hepsi görünür
                    if (applyScale) r.transform.localScale = Vector3.one * CellSize;
                    
                    r.GetPropertyBlock(PropBlock);
                    Color c = r.sharedMaterial != null ? GetMaterialColor(r.sharedMaterial) : Color.white;
                    c.a = 1.0f;
                    PropBlock.SetColor("_BaseColor", c);
                    PropBlock.SetColor("_Color", c);
                    r.SetPropertyBlock(PropBlock);
                }
            }
        }

        foreach (var kvp in cellObjects)
        {
            Vector3Int cell = kvp.Key;
            GameObject cube = kvp.Value;
            if (cube != null)
            {
                if (isPanelMode)
                {
                    if (cell.y == ActiveLayerY)
                    {
                        cube.SetActive(true);
                        if (applyScale) cube.transform.localScale = Vector3.one * CellSize;
                        
                        Renderer r = cube.GetComponentInChildren<Renderer>();
                        if (r != null)
                        {
                            r.GetPropertyBlock(PropBlock);
                            Color c = cellColors.TryGetValue(cell, out Color col) ? col : Color.white;
                            c.a = 1.0f;
                            PropBlock.SetColor("_BaseColor", c);
                            PropBlock.SetColor("_Color", c);
                            r.SetPropertyBlock(PropBlock);
                        }
                    }
                    else if (cell.y < ActiveLayerY)
                    {
                        cube.SetActive(true);
                        if (applyScale) cube.transform.localScale = Vector3.one * CellSize;
                        
                        Renderer r = cube.GetComponentInChildren<Renderer>();
                        if (r != null)
                        {
                            r.GetPropertyBlock(PropBlock);
                            Color c = cellColors.TryGetValue(cell, out Color col) ? col : Color.white;
                            c.a = 0.35f; // faded occupied base
                            PropBlock.SetColor("_BaseColor", c);
                            PropBlock.SetColor("_Color", c);
                            r.SetPropertyBlock(PropBlock);
                        }
                    }
                    else
                    {
                        cube.SetActive(false);
                    }
                }
                else
                {
                    cube.SetActive(true);
                    if (applyScale) cube.transform.localScale = Vector3.one * CellSize;
                    
                    Renderer r = cube.GetComponentInChildren<Renderer>();
                    if (r != null)
                    {
                        r.GetPropertyBlock(PropBlock);
                        Color c = cellColors.TryGetValue(cell, out Color col) ? col : Color.white;
                        c.a = 1.0f;
                        PropBlock.SetColor("_BaseColor", c);
                        PropBlock.SetColor("_Color", c);
                        r.SetPropertyBlock(PropBlock);
                    }
                }
            }
        }
    }

    // DÜZELTİLDİ (renksiz sisteme geçiş): eskiden bir katmanın tamamlanması için tüm hücrelerin
    // dolu OLMASI YETMEZ, hepsi aynı renk/materyal olması da şarttı. Bu monokromluk şartı
    // kaldırıldı — katman artık sadece doluluğa göre tamamlanır. Renk artık tamamen kozmetik.
    // layerY parametresi alır — artık SADECE ActiveLayerY değil, oyuncunun az önce yerleştirdiği
    // parçanın gerçekte bulunduğu katman kontrol edilmeli (bkz. LevelManager.OnPiecePlaced),
    // çünkü artık herhangi bir katmana yerleştirme yapılabiliyor.
    public bool IsLayerComplete(int layerY)
    {
        int cellsInLayer    = 0;
        int occupiedInLayer = 0;

        foreach (var c in allShapeCells)
        {
            if (c.y == layerY)
            {
                cellsInLayer++;
                if (occupiedCells.Contains(c)) occupiedInLayer++;
            }
        }

        return cellsInLayer > 0 && occupiedInLayer >= cellsInLayer;
    }

    // Sıralı katman mekaniği artık ÜSTTEN ALTA çalışıyor — oyuncunun doldurması gereken bir
    // sonraki katman her zaman en YÜKSEK tamamlanmamış katman (gridMaxY'den gridMinY'ye doğru
    // taranır). Bkz. Docs/SiraliKatmanMekanigi_Tasarim.md.
    private bool TryFindNextRequiredLayer(out int layerY)
    {
        for (int y = gridMaxY; y >= gridMinY; y--)
        {
            bool hasCells = false;
            bool layerFull = true;

            foreach (var cell in allShapeCells)
            {
                if (cell.y != y) continue;

                hasCells = true;
                if (!occupiedCells.Contains(cell))
                {
                    layerFull = false;
                    break;
                }
            }

            if (!hasCells) continue;

            if (!layerFull)
            {
                layerY = y;
                return true;
            }
        }

        layerY = gridMaxY;
        return false;
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

        // Katman tamamlandığı o an tebrik yazısını fırlat (ekrana fırlatma efektiyle) ve ekranı sars
        UIManager.Instance?.PlayFloatingPraise(center);
        CameraOrbit.Instance?.Shake(0.22f, 0.1f);

        AnimateLayerDisappear(layerContainer, blocksToAnimate, moveOffset, renderersAboveClearedLayer, clearedY);

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
            if (kvp.Key.y == clearedY) { Destroy(kvp.Value); continue; }   // patlayan katmanin buzu gider
            newIce[kvp.Key.y > clearedY ? new Vector3Int(kvp.Key.x, kvp.Key.y - 1, kvp.Key.z) : kvp.Key] = kvp.Value;
        }
        iceVisuals.Clear();
        foreach (var kvp in newIce) iceVisuals[kvp.Key] = kvp.Value;

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

        // --- GÖRSEL ÇÖKME (VISUAL COLLAPSE) ---
        float collapseDelay = 0.45f;
        GameObject claw = GameObject.Find("Claw");
        if (claw == null) claw = GameObject.Find("ToyMachine/Claw");
        if (claw != null)
        {
            collapseDelay = 3.65f;
        }

        if (claw == null)
        {
            foreach (var kvp in cellObjects)
            {
                if (kvp.Key.y >= clearedY) // Önceden > clearedY idi, artık 1 azaldıkları için >= clearedY oldu
                {
                    var t = kvp.Value.transform;
                    t.DOLocalMoveY(t.localPosition.y - Step, 0.45f).SetEase(Ease.OutQuad).SetDelay(collapseDelay);
                }
            }

            foreach (var kvp in targetRenderers)
            {
                if (kvp.Key.y >= clearedY)
                {
                    var t = kvp.Value.transform;
                    t.DOLocalMoveY(t.localPosition.y - Step, 0.45f).SetEase(Ease.OutQuad).SetDelay(collapseDelay);
                }
            }

            // Prefilled blokları da görsel olarak aşağı kaydır
            foreach (var kvp in prefilledRenderers)
            {
                if (kvp.Key.y >= clearedY)
                {
                    var t = kvp.Value.transform;
                    t.DOLocalMoveY(t.localPosition.y - Step, 0.45f).SetEase(Ease.OutQuad).SetDelay(collapseDelay);
                }
            }
        }

        RefreshLayerVisibility();
        RefreshSpeciesSparkle();

        // Level yalnızca gerçekten hiçbir katman/hücre kalmadığında tamamlanır.
        // Sadece targetCells.Count kontrolü gizli üst katmanları yok sayabiliyordu.
        if (allShapeCells.Count == 0)
        {
            ActiveLayerY = gridMaxY + 1;

            // Win paneli, katmanın görsel çökme/kanca animasyonu (collapseDelay + 0.45s hareket)
            // bitmeden açılmasın — önceden burada anında tetikleniyordu ve 3.65s'lik kanca
            // animasyonunun üzerine hemen biniyordu.
            DOVirtual.DelayedCall(collapseDelay + 0.45f, () => {
                IsExplodingLayer = false;
                onLevelComplete?.Invoke();
            }).SetId(LEVEL_ANIM_ID);
        }
        else
        {
            // ActiveLayerY artık "hangi katman patladı" değil, "kamera/panel şu an hangi
            // katmana odaklanmış" anlamına geliyor — patlayan katman (clearedY) bunun ÜSTÜNDEYSE
            // kamera odağı hiç etkilenmez; AYNISIYSA kamera izlediği katman az önce yok oldu,
            // eski davranışla aynı şekilde bir sonraki tamamlanmamış katmana düşülür; ALTINDAYSA
            // (kamera daha alçak/farklı bir katmana bakıyorken başka bir katman patladıysa) sadece
            // üstündeki her şeyin 1 aşağı kaydığı çökmeye göre indeks 1 azaltılır.
            if (ActiveLayerY == clearedY)
            {
                if (TryFindNextRequiredLayer(out int nextLayer))
                {
                    ActiveLayerY = nextLayer;
                    // Bir üst katman claw ile alındı → kilidi KAVRAMA anında değil, kanca
                    // layer'ı KALDIRIP GÖTÜRDÜKTEN SONRA (collapseDelay: kanca varsa 3.65s,
                    // yoksa 0.45s) aç + düşür. SetId ile Retry/NextLevel'da iptal edilir.
                    int nl = nextLayer;
                    DOVirtual.DelayedCall(collapseDelay,
                        () => LayerLockManager.Instance?.UnlockLayer(nl)).SetId(LEVEL_ANIM_ID);
                }
                else
                    ActiveLayerY = gridMaxY;
            }
            else if (ActiveLayerY > clearedY)
            {
                ActiveLayerY--;
            }
            // ActiveLayerY < clearedY: değişmez.

            RefreshLayerVisibility();
            
            DOVirtual.DelayedCall(collapseDelay + 0.45f, () => {
                IsExplodingLayer = false;
                onLayerComplete?.Invoke();
            }).SetId(LEVEL_ANIM_ID);
        }
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
    }

    public void RemoveCellAnimated(Vector3Int cell, float delay)
    {
        occupiedCells.Remove(cell);
        cellColors.Remove(cell);
        cellMatIndex.Remove(cell);

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

    public (int cleared, int bonusLines) CheckAndClearLines(System.Action onComplete = null)
    {
        if (!lineClearEnabled) { onComplete?.Invoke(); return (0, 0); }

        var allLines = new List<List<Vector3Int>>();

        for (int y = gridMinY; y <= gridMaxY; y++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                var line = BuildLine(y, z, true, false, false);
                if (line != null) allLines.Add(line);
            }
        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                var line = BuildLine(x, z, false, true, false);
                if (line != null) allLines.Add(line);
            }
        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int y = gridMinY; y <= gridMaxY; y++)
            {
                var line = BuildLine(x, y, false, false, true);
                if (line != null) allLines.Add(line);
            }

        if (allLines.Count == 0) { onComplete?.Invoke(); return (0, 0); }

        int bonusLineCount = 0;
        var toClear = new HashSet<Vector3Int>();

        foreach (var line in allLines)
        {
            if (!IsLineMonochrome(line)) continue;
            bonusLineCount++;
            foreach (var cell in line) toClear.Add(cell);
        }

        if (toClear.Count == 0) { onComplete?.Invoke(); return (0, 0); }

        var sorted = new List<Vector3Int>(toClear);
        sorted.Sort((a, b) => (a.x + a.y + a.z).CompareTo(b.x + b.y + b.z));

        int pendingCount = sorted.Count;
        System.Action onOneDone = null;
        onOneDone = () =>
        {
            pendingCount--;
            if (pendingCount <= 0) onComplete?.Invoke();
        };

        for (int i = 0; i < sorted.Count; i++)
        {
            var cell = sorted[i];
            occupiedCells.Remove(cell);
            cellColors.Remove(cell);

            // Hücre boşaldığında kılavuz saydam görseli geri göster
            if (targetRenderers.TryGetValue(cell, out var r) && r != null)
            {
                r.enabled = true;
            }

            bool objectFound = false;

            // Oyuncu tarafından yerleştirilen parçaları kontrol et
            if (cellObjects.TryGetValue(cell, out var go))
            {
                cellObjects.Remove(cell);
                AnimateAndDestroy(go, i * 0.03f, true, onOneDone);
                objectFound = true;
            }
            
            // Prefilled blokları kontrol et
            if (prefilledRenderers.TryGetValue(cell, out var prefilledRenderer) && prefilledRenderer != null)
            {
                prefilledRenderers.Remove(cell);
                var prefilledGo = prefilledRenderer.gameObject;
                if (prefilledGo != null)
                {
                    AnimateAndDestroy(prefilledGo, i * 0.03f, true, objectFound ? null : onOneDone);
                    objectFound = true;
                }
            }

            if (!objectFound)
            {
                // Hiç nesne yoksa yine de sayacı düşür
                onOneDone();
            }
        }

        return (sorted.Count, bonusLineCount);
    }

    private List<Vector3Int> BuildLine(int a, int b, bool xAxis, bool yAxis, bool zAxis)
    {
        var line = new List<Vector3Int>();
        int lo = xAxis ? gridMinX : (yAxis ? gridMinY : gridMinZ);
        int hi = xAxis ? gridMaxX : (yAxis ? gridMaxY : gridMaxZ);
        int targetCellCountInLine = 0;

        for (int v = lo; v <= hi; v++)
        {
            Vector3Int cell = xAxis ? new Vector3Int(v, a, b)
                            : yAxis  ? new Vector3Int(a, v, b)
                                     : new Vector3Int(a, b, v);

            if (targetCells.Contains(cell))
            {
                targetCellCountInLine++;
                if (!occupiedCells.Contains(cell)) return null;
                line.Add(cell);
            }
        }

        if (targetCellCountInLine < 2) return null;
        return line.Count > 0 ? line : null;
    }

    private bool IsLineMonochrome(List<Vector3Int> line)
    {
        bool hasFirstMatIdx = cellMatIndex.TryGetValue(line[0], out int firstMatIdx);
        bool hasFirstColor = cellColors.TryGetValue(line[0], out Color firstColor);

        if (!hasFirstMatIdx && !hasFirstColor) return false;

        for (int i = 1; i < line.Count; i++)
        {
            bool hasMatIdx = cellMatIndex.TryGetValue(line[i], out int matIdx);
            bool hasColor = cellColors.TryGetValue(line[i], out Color color);

            if (!hasMatIdx && !hasColor) return false;

            if (hasFirstMatIdx && hasMatIdx && (firstMatIdx != -1 || matIdx != -1))
            {
                if (firstMatIdx != matIdx)
                {
                    return false;
                }
            }
            else if (hasFirstColor && hasColor)
            {
                if (!ColorsApproxEqual(color, firstColor))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
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
        var t = go.transform;
        Vector3 origin = t.position;

        // --- Merge (bonus) efekti: flash → radyal patlama → merkeze çekim (implode) ---
        if (isBonus)
        {
            // Flash için tüm renderer'lara erişelim
            var renderers = go.GetComponentsInChildren<Renderer>();
            Color[] originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var mat = renderers[i].material;
                originalColors[i] = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                                  : mat.HasProperty("_Color")     ? mat.GetColor("_Color")
                                  : Color.white;
            }

            // Rastgele radyal patlama yönü
            Vector3 blastDir = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(0.2f, 1f),
                Random.Range(-1f, 1f)).normalized;
            float blastDist = Random.Range(0.55f, 1.0f);
            Vector3 blastTarget = origin + blastDir * blastDist;

            var seq = DOTween.Sequence().SetLink(go);
            if (delay > 0f) seq.AppendInterval(delay);

            // 1. Flash: hızlı scale-up + emit parlaması
            seq.Append(t.DOScale(t.localScale * 1.5f, 0.07f).SetEase(Ease.OutQuad));
            seq.Join(t.DOMove(blastTarget, 0.12f).SetEase(Ease.OutQuad));

            // 2. Kısa tutunma
            seq.AppendInterval(0.04f);

            // 3. Implode: merkeze geri çekilip sıfırla
            seq.Append(t.DOMove(origin, 0.18f).SetEase(Ease.InCubic));
            seq.Join(t.DOScale(Vector3.zero, 0.18f).SetEase(Ease.InBack));

            seq.OnComplete(() =>
            {
                if (go != null) Object.Destroy(go);
                onDone?.Invoke();
            });
        }
        else
        {
            // Normal (non-bonus) yerleştirme geri alma efekti — mevcut sade animasyon
            float drift = 0.35f;
            Vector3 d = new Vector3(
                Random.Range(-drift, drift),
                Random.Range(0.1f, 0.45f),
                Random.Range(-drift, drift));

            var seq = DOTween.Sequence().SetLink(go);
            if (delay > 0f) seq.AppendInterval(delay);
            seq.Append(t.DOScale(t.localScale * 1.3f, 0.07f).SetEase(Ease.OutBack));
            seq.Join(t.DOMove(t.position + d, 0.22f).SetEase(Ease.OutCubic));
            seq.Append(t.DOScale(Vector3.zero, 0.14f).SetEase(Ease.InBack));
            seq.OnComplete(() =>
            {
                if (go != null) Object.Destroy(go);
                onDone?.Invoke();
            });
        }
    }

    private static void AnimateLayerDisappear(GameObject container, List<GameObject> blocks, Vector3 moveOffset, List<Renderer> renderersToFadeDuringPass = null, int clearedY = -1)
    {
        if (container == null) return;

        // Kancayı sahneden bulmaya çalış
        GameObject claw = GameObject.Find("Claw");
        if (claw == null) claw = GameObject.Find("ToyMachine/Claw");

        // EĞER KANCA VARSA: Kanca ile yukarı çekme animasyonu
        if (claw != null)
        {
            Vector3 clawStartPos = claw.transform.position;
            Quaternion clawStartRot = claw.transform.rotation;

            // Kancanın dinlenme konumunu bir kez saklıyoruz: seviye yeniden
            // yüklendiğinde animasyon yarıda kesilirse buraya döndürülecek
            // (bkz. CancelLevelAnimations).
            if (!clawHomeCaptured)
            {
                clawHomePos = clawStartPos;
                clawHomeRot = clawStartRot;
                clawHomeCaptured = true;
            }
            // layerCenter artık ExplodeLayer'da sınırlayıcı kutu (bounding box) ortası olarak
            // kusursuz/grid-kesin hesaplanıyor (bkz. ExplodeLayer) — kanca her zaman katmanın
            // TAM geometrik merkezine iner, ortalamadan kaynaklanan kaymalar olmaz.
            Vector3 layerCenter = container.transform.position;

            // Kancanın ucundaki Collider'dan gerçek DEĞME anını yakalamak için sensör.
            // Bloklar yerleştirilirken Colliderları DraggablePiece tarafından kapatıldığı için
            // (bkz. DraggablePiece.cs), temas algılanabilsin diye burada geçici olarak açıyoruz.
            var sensor = claw.GetComponent<ClawTouchSensor>();
            if (sensor == null) sensor = claw.AddComponent<ClawTouchSensor>();
            var clawCollider = claw.GetComponent<Collider>();
            if (clawCollider != null) clawCollider.isTrigger = true;

            // Kancanın iniş/çıkış sırasında içinden geçeceği üst katman blokları için
            // geçici gizleme durumu (bkz. ExplodeLayer çağrı noktası). Blok materyalleri
            // Opaque (_Surface: 0, One/Zero blend) olduğundan _BaseColor/_Color alfası
            // GPU tarafından tamamen yok sayılıyor — property block ile saydamlaştırma
            // hiçbir görsel etki yapmıyordu. Bunun yerine, materyalden bağımsız kesin
            // çalışan bir yöntem olan transform scale ile küçültüp gizliyoruz.
            List<(Transform t, Vector3 originalScale)> passFadeState = null;
            if (renderersToFadeDuringPass != null && renderersToFadeDuringPass.Count > 0)
            {
                passFadeState = new List<(Transform, Vector3)>();
                foreach (var fr in renderersToFadeDuringPass)
                {
                    if (fr == null) continue;
                    passFadeState.Add((fr.transform, fr.transform.localScale));
                }
            }

            const float passFadeDuration = 0.18f;

            void SetPassFade(float t) // t: 0 = orijinal boyut, 1 = tamamen küçülmüş/gizli
            {
                if (passFadeState == null) return;
                foreach (var (tr, originalScale) in passFadeState)
                {
                    if (tr == null) continue;
                    tr.localScale = Vector3.Lerp(originalScale, Vector3.zero, t);
                }
            }

            // Kancanın pivot kaymasını otomatik hesapla — GERÇEK yakalama noktası (Collider'ın
            // merkezi, kancanın ucuna göre konumlandırılmıştır) referans alınır. Önceden tüm
            // mesh'in bounding box'ının merkezi kullanılıyordu; bu, kancanın şaftı uzunsa hayvanların
            // ucun değil, kancanın ORTASINA doğru toplanmasına sebep oluyordu (bkz. kanca küçültme).
            float clawVisualYOffset = 0f;
            if (clawCollider != null)
            {
                clawVisualYOffset = clawCollider.bounds.center.y - clawStartPos.y;
            }
            else
            {
                var clawRenderers = claw.GetComponentsInChildren<Renderer>();
                if (clawRenderers != null && clawRenderers.Length > 0)
                {
                    Bounds clawBounds = clawRenderers[0].bounds;
                    for (int j = 1; j < clawRenderers.Length; j++)
                    {
                        clawBounds.Encapsulate(clawRenderers[j].bounds);
                    }
                    clawVisualYOffset = clawBounds.center.y - clawStartPos.y;
                }
            }

            // Yakalanacak blokların gerçek üst sınırını hesapla (sadece güvenlik/hedef mesafesi
            // için kullanılır — GERÇEK duruş noktasını artık aşağıdaki Collider teması belirler).
            bool hasBlockTop = false;
            float blocksTopY = layerCenter.y;
            foreach (var block in blocks)
            {
                if (block == null) continue;
                var r = block.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                if (!hasBlockTop) { blocksTopY = r.bounds.max.y; hasBlockTop = true; }
                else blocksTopY = Mathf.Max(blocksTopY, r.bounds.max.y);
            }

            float skyY = layerCenter.y + 6.0f; // Gridin üstünde gökyüzünde bir yükseklik garanti edilir
            Vector3 aboveTargetPos = new Vector3(layerCenter.x, skyY - clawVisualYOffset, layerCenter.z);
            // İniş hedefi bilerek blokların İÇİNE doğru fazladan iner (overshoot) — kanca gerçekte
            // bu noktaya hiç ulaşmaz, çünkü Collider teması anında iniş anında durdurulur.
            Vector3 overshootTargetPos = new Vector3(layerCenter.x, (blocksTopY - 0.6f) - clawVisualYOffset, layerCenter.z);

            // Kamera bakışını kapat ki kanca yakalarken bloklar kendi rotasyonunu korusun.
            foreach (var block in blocks)
            {
                if (block == null) continue;
                var faceCam = block.GetComponentInChildren<FaceCamera>();
                if (faceCam != null) faceCam.enabled = false;
            }
            foreach (var block in blocks)
            {
                if (block == null) continue;
                foreach (var col in block.GetComponentsInChildren<Collider>()) col.enabled = true;
            }

            // Toplanma küresinin yarıçapını kancanın GERÇEK iç genişliğinden (Collider'ının
            // X/Z boyutundan) türet — ama hayvanlar birbirine YAKIN/sıkı dursun diye
            // Collider'ın tam genişliği değil, küçük bir kesri kullanılır.
            float clusterRadius = 0.18f;
            if (clawCollider != null)
            {
                Bounds cb = clawCollider.bounds;
                clusterRadius = Mathf.Max(cb.extents.x, cb.extents.z) * 0.4f;
            }
            clusterRadius = Mathf.Max(clusterRadius, 0.18f);
            float clusterYHalfRange = clusterRadius * 0.6f;

            const float ballDuration = 0.45f;
            bool advanced = false;
            Tween descendTween = null;

            void RunGrabAndLift()
            {
                var seq2 = DOTween.Sequence().SetLink(claw).SetId(LEVEL_ANIM_ID);

                // Uç katmana değdi: item'leri kavradı — kavrama sesi + telefon titreşimi.
                AudioManager.Instance?.PlayClawGrabSound();
                if (GameManager.Instance == null || GameManager.Instance.IsVibrationEnabled)
                {
                    Handheld.Vibrate();
                }

                // Uç katmana değdi: pençeler hayvanlar toplanırken kapansın.
                AnimateClawGrip(claw, 0f, 1f, ballDuration);

                // Kancanın ucu katmana TAM DEĞDİĞİ AN: hayvanlar merkezde sıkı bir 3D küre
                // (top gibi) oluşturacak şekilde toplansın (Fibonacci küresel dağılımı).
                for (int i = 0; i < blocks.Count; i++)
                {
                    var block = blocks[i];
                    if (block == null) continue;

                    Vector3 ballOffset = Vector3.zero;
                    if (blocks.Count > 1)
                    {
                        // Fibonacci küresel dağılımı ile hayvanları, kancanın içini dolduran
                        // BÜYÜK bir top gibi bir araya getiriyoruz (yarıçap: clusterRadius)
                        float y = -clusterYHalfRange + (2f * clusterYHalfRange * i) / (blocks.Count - 1);
                        float rRadius = Mathf.Sqrt(Mathf.Max(0f, clusterRadius * clusterRadius - y * y)); // Bu yükseklikteki küre yarıçapı
                        float theta = i * 2.39996f; // Altın açı (radyan)
                        float x = Mathf.Cos(theta) * rRadius;
                        float z = Mathf.Sin(theta) * rRadius;
                        ballOffset = new Vector3(x, y, z);
                    }
                    Vector3 grabCenter = new Vector3(layerCenter.x, claw.transform.position.y + clawVisualYOffset + 0.3f, layerCenter.z);
                    Vector3 targetPos = grabCenter + ballOffset;

                    block.transform.DOMove(targetPos, ballDuration).SetEase(Ease.OutBack);

                    // Prefilled bloklar (isim: "Prefilled_...") hayvan mesh'i değil, merkezi olmayan
                    // pivot'lu düz bir kutu (+ içine gizlenmiş ghost hayvan çocuğu) — hayvanlar için
                    // tasarlanmış "dışa bak + büyüt" küresel yönelimi bu kutuları döndürünce içiçe
                    // geçmiş/bozuk görünüyordu. Prefilled bloklar kendi orijinal rotasyon ve
                    // ölçeğinde, sadece küme pozisyonuna taşınır.
                    bool isPrefilled = block.name.StartsWith("Prefilled_");
                    if (!isPrefilled)
                    {
                        // Bloklar birbirine daha sıkı/dolgun görünsün diye hafifçe büyütülür
                        block.transform.DOScale(block.transform.localScale * 1.2f, ballDuration).SetEase(Ease.OutBack);

                        // Dışarıya doğru baksınlar (küresel yönelim)
                        Vector3 lookDir = ballOffset.normalized;
                        if (lookDir == Vector3.zero) lookDir = Vector3.forward;
                        Quaternion targetRot = Quaternion.LookRotation(lookDir, Vector3.up);
                        block.transform.DORotateQuaternion(targetRot, ballDuration).SetEase(Ease.OutBack);
                    }
                }

                // Toplanma animasyonunun bitmesini bekle, sonra hayvanları (artık top haldeyken) kancaya bağla
                seq2.AppendInterval(ballDuration + 0.15f);
                seq2.AppendCallback(() =>
                {
                    foreach (var block in blocks)
                    {
                        if (block != null) block.transform.SetParent(claw.transform, true);
                    }
                    if (Instance != null && clearedY != -1)
                    {
                        Instance.SlideDownRemainingLayers(clearedY);
                    }
                });

                // Kancayı yavaşça yukarı (aboveTargetPos) geri çek (1.2 saniye) — hayvanlar
                // küçülüp solmadan, kancaya SABİTLENMİŞ haldeyken olduğu gibi taşınır.
                // Bu hareket de üst katmanların içinden geçer, saydamlık iniş boyunca sürer.
                seq2.Append(claw.transform.DOMove(aboveTargetPos, 1.2f).SetEase(Ease.InOutQuad));

                // Kanca artık üst katmanların üstünde/dışında (aboveTargetPos) — saydamlığı geri al.
                seq2.AppendCallback(() => DOVirtual.Float(1f, 0f, passFadeDuration, SetPassFade).SetEase(Ease.InQuad));

                // Kancayı başlangıç konumuna (clawStartPos) geri götür (0.5 saniye)
                seq2.Append(claw.transform.DOMove(clawStartPos, 0.5f).SetEase(Ease.InOutQuad));

                // Evine ulaştı: pençeleri AÇIP yükünü bıraksın (0.4 saniye) ve hayvanları küçülterek delikten düşme efekti ver
                seq2.Append(DOVirtual.Float(1f, 0f, 0.4f, v => SetClawGrip(claw, v)).SetEase(Ease.OutQuad));
                seq2.Join(DOVirtual.Float(1.2f, 0f, 0.35f, scale => {
                    foreach (var block in blocks)
                    {
                        if (block != null) block.transform.localScale = Vector3.one * scale;
                    }
                }).SetEase(Ease.InQuad));

                // Bir sonraki tur için pençeleri tekrar kapatıp dinlenme moduna geçsin (0.3 saniye)
                seq2.Append(DOVirtual.Float(0f, 1f, 0.3f, v => SetClawGrip(claw, v)).SetEase(Ease.OutQuad));

                // Temizlik
                seq2.OnComplete(() =>
                {
                    if (claw != null)
                    {
                        // DetachChildren() KULLANILMAZ: eski kanca tek parça mesh'ti ve
                        // çocuğu yoktu, ama eklemli modelin çocukları kancanın KENDİ
                        // gövdesidir (mil, kasa, menteşeler). Hepsini söküp ortada
                        // bırakıyordu — kanca ilk katmandan sonra görünmez oluyordu.
                        // Yalnızca taşınmak üzere kancaya bağlanan blokları ayırıyoruz.
                        foreach (var block in blocks)
                        {
                            if (block != null && block.transform.parent == claw.transform)
                                block.transform.SetParent(null, true);
                        }

                        claw.transform.position = clawStartPos;
                        claw.transform.rotation = clawStartRot;
                        // Yükü bıraktı: pençeler bir sonraki tur için kapalı (varsayılan durum) kalsın.
                        SetClawGrip(claw, 1f);
                    }
                    if (container != null) Object.Destroy(container);
                    foreach (var block in blocks)
                    {
                        if (block != null) Object.Destroy(block);
                    }
                });
            }

            void OnTouchOrTimeout()
            {
                if (advanced) return;
                advanced = true;
                sensor.Disarm();
                descendTween?.Kill();
                RunGrabAndLift();
            }

            // 1. Kancayı yatay olarak hedef katmanın ÜSTÜNE getir (0.5 saniye)
            // İniş sırasında üst katmanların içinden geçileceği için, bu katmanları hemen/hareket başlar başlamaz küçültüyoruz
            DOVirtual.Float(0f, 1f, passFadeDuration, SetPassFade).SetEase(Ease.OutQuad);

            // Başlangıçta kanca kapalıdır.
            SetClawGrip(claw, 1f);

            // Kanca hareket ederken pençeleri AÇILIR (1'den 0'a).
            AnimateClawGrip(claw, 1f, 0f, 0.5f);

            claw.transform.DOMove(aboveTargetPos, 0.5f).SetEase(Ease.InOutQuad).OnComplete(() =>
            {
                // 2. İniş: kancanın ucundaki Collider bloklara GERÇEKTEN değene kadar aşağı in.
                // Değme anında (OnTouchOrTimeout) tween erken kesilir ve kanca olduğu yerde durur.
                // Kanca artık İLK TEMASTA durmuyor; iniş hedefine (blokların içine
                // ayarlanmış overshootTargetPos) kadar tam süre boyunca iniyor.
                //
                // Neden: duruş noktası eskiden Collider temasına bağlıydı ve bu, blok
                // collider'ı OLAN seviyelerde kancanın hayvanların ÜSTÜNDE, erken ve
                // hızlıca durmasına yol açıyordu. Collider'ı olmayan seviyelerde ise
                // temas hiç gerçekleşmediği için kanca emniyet yolundan tam derine
                // iniyordu — ve istenen görüntü buydu. Artık her seviyede aynı:
                // derinliği HEDEF belirliyor, collider'lar oynanış için (snap
                // hassasiyeti, parçaya tıklama, joystick ayrımı) serbest kalıyor.
                //
                // sensor BİLEREK Arm edilmiyor; erken kesme yok.
                descendTween = claw.transform.DOMove(overshootTargetPos, 1.2f)
                    .SetEase(Ease.InOutQuad)
                    .OnComplete(OnTouchOrTimeout);
            });
            return;
        }

        // EĞER KANCA YOKSA: Varsayılan hızlı dökülme/düşme animasyonu (Fallback)
        var seq = DOTween.Sequence().SetLink(container).SetId(LEVEL_ANIM_ID);
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block == null) continue;

            float delay = i * 0.07f;
            var blockTransform = block.transform;

            var faceCam = block.GetComponentInChildren<FaceCamera>();
            if (faceCam != null) faceCam.enabled = false;

            seq.Join(blockTransform.DOMoveY(blockTransform.position.y - 15.0f, 0.25f)
                .SetEase(Ease.InQuad)
                .SetDelay(delay));

            Vector3 randomTumble = new Vector3(Random.Range(90f, 180f), Random.Range(-180f, 180f), Random.Range(-90f, 90f));
            seq.Join(blockTransform.DORotate(randomTumble, 0.25f, RotateMode.LocalAxisAdd)
                .SetEase(Ease.InQuad)
                .SetDelay(delay));

            seq.Join(blockTransform.DOScale(Vector3.zero, 0.17f)
                .SetEase(Ease.InQuad)
                .SetDelay(delay + 0.08f));

            var r = block.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                Color originalColor = GetMaterialColor(r.material);
                Color transparentColor = new Color(originalColor.r, originalColor.g, originalColor.b, 0f);

                if (r.material.HasProperty("_BaseColor"))
                    seq.Join(r.material.DOColor(transparentColor, "_BaseColor", 0.15f).SetDelay(delay + 0.1f));
                else if (r.material.HasProperty("_Color"))
                    seq.Join(r.material.DOColor(transparentColor, "_Color", 0.15f).SetDelay(delay + 0.1f));
            }
        }

        seq.OnComplete(() =>
        {
            if (container != null) Object.Destroy(container);
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
        result = Vector3Int.zero;
        
        // 1. Raycast-based precision target alignment (hitCell + hitNormal)
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            Ray mouseRay = mainCam.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(mouseRay, 100f);
            
            // Sort hits by distance
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            
            foreach (var hit in hits)
            {
                // Ignore hits on the dragged piece itself or its children
                if (hit.collider == null) continue;
                if (DraggablePiece.activeDrag != null && 
                    (hit.transform.IsChildOf(DraggablePiece.activeDrag.transform) || hit.transform == DraggablePiece.activeDrag.transform))
                {
                    continue;
                }
                
                // Get the grid coordinate of the hit block
                Vector3Int hitCell = RootToOffset(hit.collider.transform.position);
                
                // Verify the hit cell is a valid grid cell (guide cell or occupied cell)
                if (targetCells.Contains(hitCell) || occupiedCells.Contains(hitCell))
                {
                    // Convert hit normal to Vector3Int cardinal direction
                    Vector3Int normalInt = new Vector3Int(
                        Mathf.RoundToInt(hit.normal.x),
                        Mathf.RoundToInt(hit.normal.y),
                        Mathf.RoundToInt(hit.normal.z)
                    );
                    
                    Vector3Int targetAnchorCell = occupiedCells.Contains(hitCell) ? (hitCell + normalInt) : hitCell;
                    
                    // Find which block of the dragged piece is closest in world space to the hit point
                    int closestIndex = 0;
                    float minWorldDist = float.MaxValue;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        // Sürüklenen parçanın bloklarının gerçek dünya koordinatlarını kontrol ediyoruz.
                        // Eskiden kullanılan CellToWorld(cells[i]) board referansına göre sıfır noktasını
                        // baz alıyordu ve parça henüz yuvadayken bile snaplenmesine yol açıyordu.
                        Vector3 blockWorldPos = (DraggablePiece.activeDrag != null && i < DraggablePiece.activeDrag.transform.childCount)
                            ? DraggablePiece.activeDrag.transform.GetChild(i).position
                            : CellToWorld(cells[i]);

                        float dist = Vector3.Distance(blockWorldPos, hit.point);
                        if (dist < minWorldDist)
                        {
                            minWorldDist = dist;
                            closestIndex = i;
                        }
                    }

                    // Hassasiyet eşiği: Sürüklenen parça hit noktasına yeterince yakın değilse snap yapma.
                    // maxDist değeri parça boyutu (grid.Step) kadardır. 2 katı (yaklaşık 2 hücre mesafe) makul bir eşiktir.
                    if (minWorldDist > maxDist * 2.0f)
                    {
                        continue;
                    }
                    
                    Vector3Int snapOff = targetAnchorCell - cells[closestIndex];

                    // Check if this snap offset keeps the entire piece within target boundaries
                    bool outOfBounds = false;
                    foreach (var cell in cells)
                    {
                        Vector3Int g = cell + snapOff;
                        if (!targetCells.Contains(g) || g.y != ActiveLayerY)
                        {
                            outOfBounds = true;
                            break;
                        }
                    }
                    
                    if (!outOfBounds && CanPlace(cells, snapOff))
                    {
                        result = snapOff;
                        return true;
                    }
                }
            }
        }

        // 2. Proximity-based Snapping Fallback (when dragging in empty space near the grid)
        // Yalnızca gerçekten yerleştirilebilir (dolu olmayan) konumlar aday olur;
        // dolu bir yere yakınsa parça oraya "yapışmaz", sürüklenen elde kalır.
        var seen = new HashSet<Vector3Int>();

        // Hassasiyet eşiği: Boşlukta sürüklerken aşırı uzaktan yapışmayı önlemek için 1.8 katı bir mesafe kullanıyoruz
        float bestValidD = maxDist * 1.8f;
        Vector3Int bestValidOff = Vector3Int.zero;
        bool foundValid = false;

        foreach (var t in targetCells)
        {
            foreach (var c in cells)
            {
                var off = t - c;
                if (!seen.Add(off)) continue;

                // Check if all snapped cells are within the target grid shape boundaries
                bool outOfBounds = false;
                foreach (var cell in cells)
                {
                    Vector3Int g = cell + off;
                    if (!targetCells.Contains(g) || g.y != ActiveLayerY)
                    {
                        outOfBounds = true;
                        break;
                    }
                }
                if (outOfBounds) continue;
                if (!CanPlace(cells, off)) continue;

                // Visual Center of the snapped piece cells
                Vector3 snappedCenter = Vector3.zero;
                foreach (var cell in cells)
                {
                    snappedCenter += CellToWorld(cell + off);
                }
                snappedCenter /= cells.Count;

                // Distance from center to the drag ray
                float d = Vector3.Cross(ray.direction, snappedCenter - ray.origin).magnitude;

                if (d < bestValidD)
                {
                    bestValidD = d;
                    bestValidOff = off;
                    foundValid = true;
                }
            }
        }

        if (foundValid)
        {
            result = bestValidOff;
            return true;
        }

        return false;
    }

    public bool IsSupported(List<Vector3Int> cells, Vector3Int offset)
    {
        // A rigid piece is structurally supported if at least one of its blocks is supported underneath (either floor or occupied cell)
        foreach (var c in cells)
        {
            var g = c + offset;
            // 1. Supported by floor
            if (g.y <= 0 || g.y <= gridMinY) return true;

            // 2. Supported by an occupied cell directly underneath
            if (occupiedCells.Contains(new Vector3Int(g.x, g.y - 1, g.z))) return true;
        }

        // If no blocks have any support underneath, the entire piece is floating in mid-air!
        return false;
    }

    // [GERİ ALINDI] "Herhangi bir katmana yerleştir" denemesi kötü bir UX'e yol açtı: sürükle-bırak,
    // o an bakılan katmana sığmayan bir parçayı sessizce BAŞKA (uzak, görünmeyen) bir katmana
    // "ışınlıyordu". Gerçek istek bu değildi — oyuncu HANGİ katmandaysa SADECE o katmanda işlem
    // yapabilmeli, başka bir katmanda çalışmak için panelden o katmana GEÇMELİ (bu zaten serbestti,
    // bkz. LayerPanelController.OpenPanel). Bu yüzden gerçek yerleştirme kuralı yine SADECE
    // ActiveLayerY'ye izin veriyor. "Elimdeki hiçbir parça şu an gerekli katmana sığmıyor mu"
    // sorusu (fail kontrolü/kart önceliklendirme) ayrı bir sorgu — bkz. GetPossibleOffsetsOnLayer
    // ve LevelManager.CanShapeFitRequiredLayer.
    public bool CanPlace(List<Vector3Int> cells, Vector3Int offset)
    {
        if (IsExplodingLayer) return false;
        foreach (var c in cells)
        {
            var g = c + offset;
            if (!targetCells.Contains(g) || g.y != ActiveLayerY) return false;
            if (occupiedCells.Contains(g)) return false;
            if (frozenCells.Contains(g)) return false;
            if (cellObjects.ContainsKey(g) && cellObjects[g] != null) return false;
        }

        return true;
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

        // 2. Şimdi yeni snaplenen kılavuz hücrelerini gizlemeyip açık bırakıyoruz ki parçalar içindeymiş gibi gözüksün
        foreach (var cell in newSnapped)
        {
            if (!occupiedCells.Contains(cell))
            {
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                {
                    // r.enabled = false;
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

    // CanPlace/GetPossibleOffsets'ten farklı olarak GERÇEK yerleştirme kuralını (ActiveLayerY
    // kısıtı) uygulamaz — "bu parça, layerY'ye GEÇİLSEYDİ oraya sığar mıydı" sorusuna cevap verir.
    // Sadece LevelManager.CanShapeFitRequiredLayer tarafından (fail kontrolü/kart önceliklendirme
    // için, layerY = ActiveLayerY verilerek) kullanılır — gerçek yerleştirme HÂLÂ sadece
    // ActiveLayerY'ye izin veriyor (bkz. CanPlace).
    public List<Vector3Int> GetPossibleOffsetsOnLayer(List<Vector3Int> cells, int layerY)
    {
        var valid = new List<Vector3Int>();
        var seen  = new HashSet<Vector3Int>();
        foreach (var t in targetCells)
        {
            if (t.y != layerY) continue;
            if (occupiedCells.Contains(t)) continue;
            foreach (var c in cells)
            {
                var off = t - c;
                if (!seen.Add(off)) continue;
                if (CanPlaceOnLayer(cells, off, layerY)) valid.Add(off);
            }
        }
        return valid;
    }

    public bool CanPlaceOnLayer(List<Vector3Int> cells, Vector3Int offset, int layerY)
    {
        foreach (var c in cells)
        {
            var g = c + offset;
            if (!targetCells.Contains(g) || g.y != layerY) return false;
            if (occupiedCells.Contains(g)) return false;
            if (frozenCells.Contains(g)) return false;
            if (cellObjects.ContainsKey(g) && cellObjects[g] != null) return false;
        }

        return true;
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

    // newlyPlacedCells parametresi ALINMAZ: artık sadece yeni yerleşen parçaya değil, TÜM
    // buzlu hücrelere bakılıyor (bkz. aşağıdaki not) — bu yüzden hangi hücrelerin yeni
    // yerleştiğinin bilinmesine gerek yok, sadece BİR yerleştirme olduğunu (yeniden kontrol
    // zamanı geldiğini) bilmek yeterli.
    public bool CheckAndResolveFrozenCells(System.Action<bool> onComplete)
    {
        if (frozenCells.Count == 0)
        {
            onComplete?.Invoke(false);
            return false;
        }

        Vector3Int[] horizontalNeighbors = {
            Vector3Int.left, Vector3Int.right,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        HashSet<Vector3Int> cellsToThaw = new HashSet<Vector3Int>();
        HashSet<Vector3Int> cellsToDestroy = new HashSet<Vector3Int>();

        // TÜM buzlu hücreler taranır — sadece yeni yerleşen parçaya değil. Aksi halde, buza
        // ÖNCEDEN değen bir parçanın yanına şimdi ikinci bir parça yerleşip "iki obje dip dibe"
        // şartını YENİ parça değil ESKİ parça üzerinden tamamlaması durumu kaçırılıyordu (yeni
        // parça buza doğrudan dokunmadığı için hiç kontrol edilmiyordu).
        foreach (var frozenCell in frozenCells)
        {
            foreach (var offset in horizontalNeighbors)
            {
                Vector3Int touchCell = frozenCell + offset;
                if (!occupiedCells.Contains(touchCell)) continue;

                // Buz eritme artık AYNI TÜR şartı aramıyor: buza değen hücrenin, türüne
                // bakılmaksızın yatayda en az bir dolu komşusu ("iki obje dip dibe") varsa erime
                // tetiklenir. Yok olan grup kasıtlı olarak SADECE bu 2 hücre — bağlı olduğu daha
                // büyük bir kütle (ör. tüm katman) varsa bile onun tamamı değil.
                Vector3Int? partner = null;
                foreach (var offset2 in horizontalNeighbors)
                {
                    Vector3Int neighbor = touchCell + offset2;
                    if (occupiedCells.Contains(neighbor))
                    {
                        partner = neighbor;
                        break;
                    }
                }
                if (partner == null) continue;

                cellsToThaw.Add(frozenCell);
                cellsToDestroy.Add(touchCell);
                cellsToDestroy.Add(partner.Value);
            }
        }

        if (cellsToThaw.Count == 0)
        {
            onComplete?.Invoke(false);
            return false;
        }

        StartCoroutine(AnimateThawAndDestroy(cellsToThaw, cellsToDestroy, () => onComplete?.Invoke(true)));
        return true;
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
    private IEnumerator AnimateThawAndDestroy(HashSet<Vector3Int> cellsToThaw, HashSet<Vector3Int> cellsToDestroy, System.Action onComplete)
    {
        if (cellsToThaw != null && cellsToThaw.Count > 0)
        {
            AudioManager.Instance?.PlayIceMeltSound();
        }

        foreach (var cell in cellsToThaw)
        {
            frozenCells.Remove(cell);
        }

        foreach (var cell in cellsToDestroy)
        {
            occupiedCells.Remove(cell);
            cellColors.Remove(cell);
            cellMatIndex.Remove(cell);
        }

        int pendingEffects = cellsToThaw.Count + cellsToDestroy.Count;
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
            if (kvp.Key.y >= clearedY)
            {
                var t = kvp.Value.transform;
                t.DOLocalMoveY(t.localPosition.y - Step, 0.45f).SetEase(Ease.OutQuad);
            }
        }

        foreach (var kvp in targetRenderers)
        {
            if (kvp.Key.y >= clearedY)
            {
                var t = kvp.Value.transform;
                t.DOLocalMoveY(t.localPosition.y - Step, 0.45f).SetEase(Ease.OutQuad);
            }
        }

        foreach (var kvp in prefilledRenderers)
        {
            if (kvp.Key.y >= clearedY)
            {
                var t = kvp.Value.transform;
                t.DOLocalMoveY(t.localPosition.y - Step, 0.45f).SetEase(Ease.OutQuad);
            }
        }
    }
}