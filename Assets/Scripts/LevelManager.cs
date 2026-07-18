using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

[DefaultExecutionOrder(-10)] // GameManager'dan önce Awake çalışır, Instance hazır olur
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("Level Configuration")]
    public LevelData currentLevel;

    [Header("Scene Locations")]
    public Transform mainCubeLocation;

    [Header("UI Kart Slotları")]
    public List<PieceCardUI> pieceCards = new List<PieceCardUI>();

    [Header("Visual Settings")]
    public int   maxVisiblePieces = 2;
    [Range(0f, 1f)] public float smartSpawnProbability = 0.35f;
    
    private List<bool> activeIsSmart = new List<bool>();

    private GameObject activeMainPiece;
    public GameObject ActiveMainPiece => activeMainPiece;
    private List<GameObject> activePieces = new List<GameObject>();
    private List<GameObject> placedPieces = new List<GameObject>();
    private bool isJokerUsedThisLevel = false;
    public bool CanUseJoker => !isJokerUsedThisLevel && placedPieces != null && placedPieces.Count > 0;
    private GridManager gridManager;
    private Material ghostTargetMat;

    // Hangi parçanın hangi kartta olduğunu takip eder
    private Dictionary<GameObject, PieceCardUI> pieceToCard = new Dictionary<GameObject, PieceCardUI>();

    private List<GameObject> allPiecePrefabs = new List<GameObject>();
    private List<float>      allPieceWidths  = new List<float>();
    private List<float>      allPieceHeights = new List<float>();
    private List<float>      allPieceDepths  = new List<float>();
    private List<int>        activePieceDataIndices = new List<int>();
    private List<int>        spawnedPieceIndices = new List<int>();

    [Header("Next Piece Preview Settings")]
    private int nextPieceIndex = -1;
    // "Sıradaki parça" önizlemesinde gösterilen tür, o parça gerçekten spawn edilirken AYNI türü
    // kullansın diye önizleme oluşturulduğu anda BİR KEZ seçilip burada saklanır. nextPieceSpeciesIndex
    // artık asıl (otoriter) değer — buz erime/grup eşleşmesi mekaniği buna göre çalışır (bkz.
    // PickPieceSpeciesIndex, GridManager.FloodFillSameSpecies). nextPieceMaterial sadece bu türün
    // kozmetik materyalidir, önizleme render'ı için tutulur.
    private int nextPieceSpeciesIndex = -1;
    private Material nextPieceMaterial;

    private GameObject nextPiecePreviewParent;
    private Camera nextPiecePreviewCam;
    private GameObject nextPiecePreview3D;
    private RawImage nextPiecePreviewRawImage;
    private float nextPieceVisualRadius = 1f;
    private Vector3 nextPieceVisualCenter = Vector3.zero;
    private float previewRotationAngle = 0f;

    private void Awake()
    {
        Instance    = this;
        gridManager = GetComponent<GridManager>();
        if (gridManager == null) gridManager = gameObject.AddComponent<GridManager>();
    }

    private void Start()
    {
        // Material assets will be assigned via inspector by the AestheticSetupTool
    }

    public void LoadLevel(LevelData level)
    {
        ClearCurrentLevel();

        // currentLevel alanı vardı ama HİÇ atanmıyordu (yalnızca inspector'daki değer
        // duruyordu) — seviyeye özel ayarların (bkz. randomizeSpawnRotation) doğru
        // seviyeden okunması için burada set ediliyor.
        currentLevel = level;

        // Önceki seviyeden kalan swipe döndürmesini yeni ana şekil pivot'a
        // parent'lanmadan ÖNCE nötrle (aksi halde SetParent eski açıyı telafi
        // eden bir local rotasyon kilitler ve şekil kalıcı olarak yanlış görünür).
        CameraOrbit.Instance?.ResetBoardRotation();

        if (level.mainShapePrefab != null)
        {
            var prefabHolder = level.mainShapePrefab.GetComponent<CubeShapeDataHolder>();
            Vector3 bc = Vector3.zero;
            float step = 1f;

            if (prefabHolder != null)
            {
                step = prefabHolder.cellSize + prefabHolder.spacing;
                bc = BoundsCenter(prefabHolder.occupiedCells, step);
            }

            activeMainPiece = Instantiate(level.mainShapePrefab, mainCubeLocation);
            activeMainPiece.name = "Main_Shape";
            activeMainPiece.transform.localPosition = -bc;

            if (CameraOrbit.Instance != null && CameraOrbit.Instance.pivot != null)
            {
                activeMainPiece.transform.SetParent(CameraOrbit.Instance.pivot, true);
                CameraOrbit.Instance.cube = activeMainPiece.transform;
            }

            DisableShadows(activeMainPiece);

            var holder = activeMainPiece.GetComponent<CubeShapeDataHolder>();
            if (holder != null)
            {
                // Dunya pozisyonu ile baslatmaya geri döndük
                gridManager.Initialize(activeMainPiece, holder.cellSize, holder.spacing, activeMainPiece.transform.position);
                ApplyTargetGhost(activeMainPiece);
            }
        }

        allPiecePrefabs.Clear();
        if (level.complementaryPieces != null && level.complementaryPieces.Count > 0)
        {
            foreach (var p in level.complementaryPieces)
            {
                if (p != null)
                {
                    allPiecePrefabs.Add(p);
                    var holder2 = p.GetComponent<CubeShapeDataHolder>();
                    if (holder2 != null && holder2.occupiedCells.Count > 0)
                    {
                        float st = holder2.cellSize + holder2.spacing;
                        float minX = holder2.occupiedCells.Min(c => c.x) * st;
                        float maxX = holder2.occupiedCells.Max(c => c.x) * st + holder2.cellSize;
                        float minY = holder2.occupiedCells.Min(c => c.y) * st;
                        float maxY = holder2.occupiedCells.Max(c => c.y) * st + holder2.cellSize;
                        float minZ = holder2.occupiedCells.Min(c => c.z) * st;
                        float maxZ = holder2.occupiedCells.Max(c => c.z) * st + holder2.cellSize;
                        allPieceWidths.Add(maxX - minX);
                        allPieceHeights.Add(maxY - minY);
                        allPieceDepths.Add(maxZ - minZ);
                    }
                    else
                    {
                        allPieceWidths.Add(2f);
                        allPieceHeights.Add(2f);
                        allPieceDepths.Add(2f);
                    }
                }
            }
        }

        // Kart UI'larını başlat (idempotent)
        for (int i = 0; i < pieceCards.Count; i++)
            pieceCards[i]?.Init(i);

        InitNextPiecePreviewSystem();

        for (int i = 0; i < maxVisiblePieces; i++)
            SpawnRandomPiece();
        FitCameraToScene();

        var lpc = FindObjectOfType<LayerPanelController>();
        if (lpc != null) lpc.ResetPanel();

        // Öğretici adımları (varsa) burada başlar — kartlar spawn edilip panel
        // sıfırlandıktan SONRA, çünkü gösterge kart/buton konumlarını okuyor.
        TutorialOverlay.Instance?.BeginForLevel(level);
    }

    [Header("Color Palette Settings (Material-Based)")]
    [Tooltip("Sürüklenen oyun parçaları için kullanılacak Material (Malzeme) paleti — her indeks bir TÜR temsil eder (bkz. pieceSpeciesPrefabs, aynı indeks sırasıyla eşleşir)")]
    public Material[] pieceMaterials;
    public Material[] PieceMaterials => pieceMaterials;

    [Tooltip("pieceMaterials ile PARALEL, aynı uzunlukta dizi — indeks i'deki tür için (varsa) rigli hayvan " +
             "prefabı. Boş/null bırakılan indeksler sadece düz renkli küp olarak kalır (görsel eksik olsa da " +
             "mekanik/eşleşme etkilenmez — bkz. GridManager.cellMatIndex). Şu an sadece Fox mevcut.")]
    public GameObject[] pieceSpeciesPrefabs;

    [Tooltip("Yarı saydam hedef kılavuz küpü için kullanılacak Material")]
    public Material ghostTargetMaterial;

    private void SpawnRandomPiece()
    {
        if (allPiecePrefabs.Count == 0) return;

        // Boş bir kart bul
        PieceCardUI targetCard = (pieceCards != null && pieceCards.Count > 0)
            ? pieceCards.FirstOrDefault(c => c != null && !c.HasPiece)
            : null;

        // Kart sistemi varsa ama boş kart yoksa çık
        if (pieceCards != null && pieceCards.Count > 0 && targetCard == null) return;

        if (nextPieceIndex < 0)
        {
            PrepareNextPieceIndex();
        }

        int indexToSpawn = nextPieceIndex;

        // Bu indeksi spawn edilmiş olarak işaretle
        spawnedPieceIndices.Add(indexToSpawn);
        activeIsSmart.Add(false); // Kolaylaştırma yardımı kapalı

        // Bu parçanın türü, önizlemesi gösterildiği anda (PrepareNextPieceIndex içinde) zaten
        // kararlaştırılıp nextPieceSpeciesIndex'e kaydedilmişti — burada YENİDEN seçilmez, aksi
        // halde önizlemede gösterilen türle spawn edilen tür tutmayabilir.
        int matchingSpeciesIndex = nextPieceSpeciesIndex;

        // Parçayı spawn et
        SpawnPieceAtIndex(indexToSpawn, GetRandom90DegreeRotation(), matchingSpeciesIndex, targetCard);

        // Get the new next piece index
        PrepareNextPieceIndex();
    }

    private Quaternion GetRandom90DegreeRotation()
    {
        // Öğretici seviyeler parçanın YÖNÜNÜ tasarımın parçası olarak kullanır (bkz.
        // LevelData.randomizeSpawnRotation) — orada rastgelelik kapalıdır.
        if (currentLevel != null && !currentLevel.randomizeSpawnRotation)
            return Quaternion.identity;

        float ry = Random.Range(0, 4) * 90f;
        return Quaternion.Euler(0, ry, 0);
    }

    private int FindSmallestPieceIndex()
    {
        int bestIdx = 0;
        int minCells = int.MaxValue;
        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            var h = allPiecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (h != null && h.occupiedCells.Count < minCells)
            {
                minCells = h.occupiedCells.Count;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    // Katmanın "baskın türle uyuşma" zorunluluğu yok, bu yüzden paletten düz rastgele bir TÜR
    // (pieceMaterials'a paralel indeks) seçiyoruz. Bu artık asıl seçim — Material değil, indeks
    // döner, çünkü indeks hem kozmetik materyali (pieceMaterials[i]) hem de varsa görsel rozeti
    // (pieceSpeciesPrefabs[i]) hem de buz erime/grup eşleşmesi mekaniğini (GridManager.cellMatIndex)
    // AYNI ANDA ve tutarlı şekilde belirler (eskiden Material seçilip matIndex sonradan, yerleştirme
    // anında, materyali karşılaştırarak YENİDEN bulunmaya çalışılıyordu — kırılgandı).
    private int PickPieceSpeciesIndex()
    {
        if (pieceMaterials == null || pieceMaterials.Length == 0) return -1;
        var validIndices = new List<int>();
        for (int i = 0; i < pieceMaterials.Length; i++)
        {
            if (pieceMaterials[i] != null) validIndices.Add(i);
        }
        if (validIndices.Count == 0) return -1;
        return validIndices[Random.Range(0, validIndices.Count)];
    }

    private int FindBestPieceIndex(out Quaternion rotation, out Color? recommendedColor, out bool foundMerge)
    {
        rotation = Quaternion.identity;
        recommendedColor = null;
        foundMerge = false;

        if (allPiecePrefabs.Count == 0) return -1;

        Quaternion[] possibleRotations = new Quaternion[]
        {
            Quaternion.identity,
            Quaternion.Euler(0, 90, 0),
            Quaternion.Euler(0, 180, 0),
            Quaternion.Euler(0, 270, 0)
        };

        List<int> placeableIndices = new List<int>();
        var mergeOpportunities = new List<(int index, Quaternion rot, Color col)>();

        // Tum prefablari ve rotasyonlari tara
        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            var h = allPiecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (h == null) continue;

            foreach (var rot in possibleRotations)
            {
                var rotatedCells = GridManager.RotateCells(h.occupiedCells, rot);
                var offsets = gridManager.GetPossibleOffsets(rotatedCells);
                
                if (offsets.Count > 0)
                {
                    if (!placeableIndices.Contains(i)) placeableIndices.Add(i);

                    foreach (var off in offsets)
                    {
                        var mCol = gridManager.GetMergeColor(rotatedCells, off);
                        if (mCol.HasValue)
                        {
                            mergeOpportunities.Add((i, rot, mCol.Value));
                        }
                    }
                }
            }
        }

        // 1. Merge firsati varsa onu kullan
        if (mergeOpportunities.Count > 0)
        {
            var choice = mergeOpportunities[Random.Range(0, mergeOpportunities.Count)];
            rotation = choice.rot;
            recommendedColor = choice.col;
            foundMerge = true;
            return choice.index;
        }

        // 2. Merge bulunamadiysa, "En cok komsu renk eslesmesi" saglayani bul (Progress score)
        int bestMatchScore = -1;
        var bestOptions = new List<(int index, Quaternion rot, Color col)>();

        var paletteColors = new List<Color>();
        if (pieceMaterials != null)
        {
            foreach (var m in pieceMaterials)
            {
                if (m != null) paletteColors.Add(GridManager.GetMaterialColor(m));
            }
        }

        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            var h = allPiecePrefabs[i].GetComponent<CubeShapeDataHolder>();
            if (h == null) continue;
            foreach (var rot in possibleRotations)
            {
                var rotatedCells = GridManager.RotateCells(h.occupiedCells, rot);
                var offsets = gridManager.GetPossibleOffsets(rotatedCells);
                if (offsets.Count == 0) continue;

                foreach (var paletteCol in paletteColors)
                {
                    foreach (var off in offsets)
                    {
                        int score = CalculateMatchScore(rotatedCells, off, paletteCol);
                        if (score > bestMatchScore)
                        {
                            bestMatchScore = score;
                            bestOptions.Clear();
                            bestOptions.Add((i, rot, paletteCol));
                        }
                        else if (score == bestMatchScore)
                        {
                            bestOptions.Add((i, rot, paletteCol));
                        }
                    }
                }
            }
        }

        if (bestOptions.Count > 0 && bestMatchScore > 0)
        {
            var choice = bestOptions[Random.Range(0, bestOptions.Count)];
            rotation = choice.rot;
            recommendedColor = choice.col;
            foundMerge = true;
            return choice.index;
        }

        if (placeableIndices.Count > 0)
        {
            return placeableIndices[Random.Range(0, placeableIndices.Count)];
        }

        return Random.Range(0, allPiecePrefabs.Count);
    }

    private int CalculateMatchScore(List<Vector3Int> cells, Vector3Int offset, Color color)
    {
        int score = 0;
        foreach (var c in cells)
        {
            Vector3Int pos = c + offset;
            Vector3Int[] neighbors = { Vector3Int.left, Vector3Int.right, Vector3Int.up, Vector3Int.down, new Vector3Int(0,0,1), new Vector3Int(0,0,-1) };
            foreach (var n in neighbors)
            {
                if (gridManager.GetCellColor(pos + n, out Color neighborCol))
                {
                    if (GridManager.ColorsApproxEqual(color, neighborCol)) score++;
                }
            }
        }
        return score;
    }

    private void SpawnPieceAtIndex(int index, Quaternion? initialRot = null, int speciesIndex = -1, PieceCardUI targetCard = null)
    {
        if (index < 0 || index >= allPiecePrefabs.Count) return;

        GameObject piece = Instantiate(allPiecePrefabs[index], new Vector3(0, -100, 0), initialRot ?? Quaternion.identity);
        piece.name = $"Piece_{index + 1}";
        DisableShadows(piece);

        // Tür belirtilmemişse (çağıran zorunlu kılmadıysa) rastgele bir tanesini seç
        if (speciesIndex < 0) speciesIndex = PickPieceSpeciesIndex();

        Material mat = (speciesIndex >= 0 && pieceMaterials != null && speciesIndex < pieceMaterials.Length)
            ? pieceMaterials[speciesIndex]
            : null;
        ApplyMaterialToPiece(piece, mat);

        var drag = piece.AddComponent<DraggablePiece>();
        drag.InitialRotation = initialRot ?? Quaternion.identity;
        drag.SpeciesIndex = speciesIndex;
        activePieces.Add(piece);
        activePieceDataIndices.Add(index);

        ApplySpeciesVisual(piece, speciesIndex);

        // Kart sistemine bağla
        if (targetCard != null)
        {
            drag.onDragCancelled = () => targetCard.ReturnToPreview();
            targetCard.AssignPiece(piece);
            pieceToCard[piece] = targetCard;
        }
    }

    // speciesIndex için özel bir hayvan prefabı tanımlıysa (pieceSpeciesPrefabs[speciesIndex] !=
    // null), parçanın HER küp hücresini o türün tam boy görseliyle değiştirir — küp kendi
    // Renderer'ı gizlenir (görünmez olur), üstüne türün modeli eklenir. Küpün Transform/Collider/
    // isim yapısına DOKUNULMAZ — DraggablePiece.EndDrag/UpdateChildPositions doğrudan piece
    // kökünün çocuklarını currentCells ile 1:1 eşleştirip sayıyor (children.Count !=
    // visualCells.Count guard'ı); tür görseli bu yüzden KÖK'e değil, MEVCUT küp çocuğunun altına
    // ekleniyor (kök seviyesine eklenen fazladan bir çocuk bu eşleşmeyi kırıp sürükleme sırasında
    // parçayı görsel olarak dondurur ve yerleştirme anında son hücrenin grid kaydını düşürür).
    // Tür prefabı tanımlı değilse (bugün Duck/Fox dışındaki tüm indeksler) dokunulmaz, hücre
    // eskisi gibi düz renkli küp olarak kalır.
    private void ApplySpeciesVisual(GameObject piece, int speciesIndex)
    {
        if (pieceSpeciesPrefabs == null || speciesIndex < 0 || speciesIndex >= pieceSpeciesPrefabs.Length) return;
        GameObject speciesPrefab = pieceSpeciesPrefabs[speciesIndex];
        if (speciesPrefab == null) return;

        foreach (Transform child in piece.transform)
        {
            if (!child.name.StartsWith("Cube_")) continue;
            AttachSpeciesVisual(child, speciesPrefab);
        }
    }

    /// <summary>Bir küp hücresinin kendi görselinin yerine belirtilen türün 3D modelini ekler ve
    /// hücrenin ortasına hizalar (bkz. ApplySpeciesVisual, ApplyTargetGhost). Küp (normal parça da,
    /// prefilled hedef hücresi de) tamamen görünmez yapılır — hücrede sadece hayvan görünür.
    /// targetCell verilirse (prefilled hedef hücreleri): hayvan, GridManager.CellToWorld ile
    /// GERÇEK grid hücresinin merkezine yerleştirilir — parçalar yerleştirilirken kullanılan
    /// AYNI (kanıtlanmış doğru) konumlandırma. Küpün kendi mesh bounds'undan/rotasyonundan
    /// türetilen ortalama denemeleri (şekil aracının Cube_/Prefilled_ örneklerine bastığı rastgele
    /// rotasyon yüzünden) hayvanı hücrenin dışına fırlatıyordu — bkz. eski AttachSpeciesVisual.
    /// targetCell verilmezse (elde/tahtada olan normal parça): küpün kendi mesh bounds'una göre
    /// ortalanır (bu obje henüz sabit bir grid hücresine bağlı değil, serbestçe sürükleniyor).</summary>
    private void AttachSpeciesVisual(Transform cubeTransform, GameObject speciesPrefab, Vector3Int? targetCell = null)
    {
        if (speciesPrefab == null) return;

        // Küpün kendi renderer'ının .enabled durumu GridManager.RefreshLayerVisibility tarafından
        // sürekli yönetiliyor (katman gizle/göster, panel reset vb.) — SpeciesVisual da AYNI
        // renderer'ın enabled durumunu her karede takip ediyor (RendererVisibilityMirror), böylece
        // katman gizlenince hayvan da gizlenir.
        var cubeRenderers = cubeTransform.GetComponentsInChildren<Renderer>();

        // Hayvan modelinin kendi pivot-taban mesafesini (pivotu ayakta mı, gövde merkezinde mi —
        // türden türe değişir), HERHANGİ bir parent/rotasyon etkisi olmadan, ayrı/temiz bir
        // "probe" örnek üzerinde ölçüyoruz. Bu ölçüm parent'tan tamamen bağımsız olduğu için
        // targetCell verilmeyen (elde/tahtada sürüklenen) parçalarda LOCAL bir ofset olarak
        // uygulanabilir — bkz. aşağıdaki else dalı.
        float pivotToBottom = 0f;
        {
            GameObject probe = Instantiate(speciesPrefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            probe.transform.localScale = Vector3.one;
            var probeRenderers = probe.GetComponentsInChildren<Renderer>();
            if (probeRenderers != null && probeRenderers.Length > 0)
            {
                Bounds probeBounds = probeRenderers[0].bounds;
                for (int j = 1; j < probeRenderers.Length; j++) probeBounds.Encapsulate(probeRenderers[j].bounds);
                pivotToBottom = -probeBounds.min.y;
            }
            Destroy(probe);
        }

        foreach (var rend in cubeRenderers)
        {
            var mf = rend.GetComponent<MeshFilter>();
            if (mf == null) continue;

            // targetCell varsa (prefilled hücre) mesh'i null'lamadan ÖNCE sakla — buz erirken bu
            // hücre grup halinde yok edilirse (bkz. GridManager.RestoreAsGhostTarget), kutuyu
            // tekrar bir ghost hedef olarak göstermek için orijinal mesh'e geri ihtiyacımız var.
            if (targetCell.HasValue && mf.sharedMesh != null)
                GridManager.Instance?.CacheOriginalPrefilledMesh(targetCell.Value, mf.sharedMesh);

            mf.sharedMesh = null;
        }

        GameObject speciesVisual = Instantiate(speciesPrefab, cubeTransform);
        speciesVisual.name = "SpeciesVisual";
        speciesVisual.transform.localPosition = Vector3.zero;
        speciesVisual.transform.localRotation = Quaternion.identity;
        speciesVisual.transform.localScale = Vector3.one;
        foreach (var col in speciesVisual.GetComponentsInChildren<Collider>()) col.enabled = false;
        speciesVisual.AddComponent<FaceCamera>();

        if (cubeRenderers.Length > 0)
        {
            var mirror = speciesVisual.AddComponent<RendererVisibilityMirror>();
            mirror.source = cubeRenderers[0];
        }

        // FaceCamera bir sonraki LateUpdate'te WORLD rotasyonunu kameraya bakacak şekilde EZECEK —
        // ölçüm/konumlandırmayı, küpün rastgele rotasyonundan değil, nihai (dik) rotasyondan
        // bağımsız şekilde yapalım diye burada da sıfırlıyoruz.
        speciesVisual.transform.rotation = Quaternion.identity;

        if (targetCell.HasValue && GridManager.Instance != null)
        {
            // Prefilled hedef hücreleri: küp bir daha ASLA dönmeyecek (statik level parçası),
            // bu yüzden dünya-uzayında, GridManager.CellToWorld ile (parçalar yerleştirilirken
            // kullanılan AYNI, kanıtlanmış metod) bir kerelik konumlandırma yeterli ve doğru.
            Vector3 cellCenter = GridManager.Instance.CellToWorld(targetCell.Value);
            float cellFloorY = cellCenter.y - GridManager.Instance.CellSize * 0.5f;
            speciesVisual.transform.position = cellCenter;

            var animalRenderers = speciesVisual.GetComponentsInChildren<Renderer>();
            if (animalRenderers != null && animalRenderers.Length > 0)
            {
                Bounds animalBounds = animalRenderers[0].bounds;
                for (int j = 1; j < animalRenderers.Length; j++) animalBounds.Encapsulate(animalRenderers[j].bounds);
                speciesVisual.transform.position += Vector3.up * (cellFloorY - animalBounds.min.y);
            }
        }
        else
        {
            // Elde/tahtada sürüklenen normal parça: küp, yerleştirme anında (bkz.
            // DraggablePiece.EndDrag) DORotateQuaternion ile YENİDEN döndürülüyor — dünya-uzayı
            // bir ofset ölçüp bir kerelik uygulasak, küp o dönüşten SONRA hayvanı yanlış yöne
            // taşımış olurdu (sürüklerken doğru, bıraktığında yanlış görünmesinin sebebi buydu).
            // Bunun yerine SAF bir LOCAL ofset kullanıyoruz: küpün pivotu bir hücrenin merkezinde
            // kabul edilip (CellSize/2 aşağısı taban), hayvanın kendi pivot-taban mesafesi
            // (yukarıda temiz bir probe ile ölçüldü) buna göre telafi ediliyor. LOCAL uzayda
            // tanımlandığı için küp SONRADAN dönse/taşınsa bile hayvan onunla birlikte doğru
            // şekilde hareket eder.
            float halfCell = GridManager.Instance != null ? GridManager.Instance.CellSize * 0.5f : 0.5f;
            speciesVisual.transform.localPosition = new Vector3(0f, -halfCell + pivotToBottom, 0f);
        }
    }

    // "Cube_" filtresi: parçaya bir tür görseli (bkz. ApplySpeciesVisual) eklendiğinde, o türün
    // KENDİ (kürk/tüy) materyali bu düz renk atamasıyla ezilmesin diye sadece küp geometrisine
    // uygulanır — UpdateNextPiecePreviewVisuals'daki AYNI filtreyle tutarlı (bkz. ~satır 1226).
    private static void ApplyMaterialToPiece(GameObject piece, Material material)
    {
        if (piece == null || material == null) return;
        foreach (var r in piece.GetComponentsInChildren<Renderer>())
        {
            if (!r.gameObject.name.StartsWith("Cube_")) continue;
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
        }
    }

    private void RecomputeHomePositions() { } // Kart sistemiyle artık kullanılmıyor

    public void OnPiecePlaced(DraggablePiece piece)
    {
        int idx = activePieces.IndexOf(piece.gameObject);
        if (idx < 0) return;

        TutorialEvents.RaisePiecePlaced();

        // İlgili kartı boşalt
        if (pieceToCard.TryGetValue(piece.gameObject, out var card))
        {
            card.ClearPiece();
            pieceToCard.Remove(piece.gameObject);
        }

        piece.transform.SetParent(null);

        placedPieces.Add(activePieces[idx]);
        activePieces.RemoveAt(idx);
        activePieceDataIndices.RemoveAt(idx);
        activeIsSmart.RemoveAt(idx);

        // Calculate placed coordinates for the newly placed piece
        List<Vector3Int> newlyPlacedCells = new List<Vector3Int>();
        Vector3Int boardOffset = gridManager.RootToOffset(piece.transform.position);
        foreach (var cell in piece.CurrentCells)
        {
            newlyPlacedCells.Add(cell + boardOffset);
        }

        // Parçanın gerçekte hangi katmana yerleştiği — artık ActiveLayerY ile aynı olmak zorunda
        // değil (herhangi bir katmana yerleştirilebiliyor, bkz. GridManager.CanPlace). Tüm
        // hücreler aynı Y'yi paylaşır (GridManager.CanPlace bunu zorunlu kılıyor).
        int placedLayerY = newlyPlacedCells.Count > 0 ? newlyPlacedCells[0].y : gridManager.ActiveLayerY;

        // Check if there are frozen cells to thaw
        gridManager.CheckAndResolveFrozenCells(newlyPlacedCells, onComplete: (iceResolved) =>
        {
            var lpc = FindObjectOfType<LayerPanelController>();

            if (gridManager.IsLayerComplete(placedLayerY))
            {
                gridManager.IsExplodingLayer = true;
                if (lpc != null)
                {
                    lpc.ClosePanel(() =>
                    {
                        lpc.SetButtonsVisible(false);
                        gridManager.ExplodeLayer(placedLayerY,
                            onLayerComplete: () => {
                                lpc.BuildLayerButtons();
                                HandlePostPiecePlaced(placedLayerY);
                            },
                            onLevelComplete: () => {
                                GameManager.Instance?.CheckWin();
                            }
                        );
                    });
                }
                else
                {
                    gridManager.ExplodeLayer(placedLayerY,
                        onLayerComplete: () => {
                            HandlePostPiecePlaced(placedLayerY);
                        },
                        onLevelComplete: () => {
                            GameManager.Instance?.CheckWin();
                        }
                    );
                }
            }
            else if (iceResolved)
            {
                // Buz kırıldı ama katman henüz tamamlanmadı
                // Panel modda kalıyoruz, sadece piece spawn'ı yapıyoruz
                HandlePostPiecePlaced(placedLayerY);
            }
            else
            {
                HandlePostPiecePlaced(placedLayerY);
            }
        });
    }

    private void HandlePostPiecePlaced(int placedLayerY)
    {
        // Yeni yerleştirilen hücrelerin hedef/ghost küpleri gizlensin diye görünürlüğü hemen
        // tazele — katman tamamlanmadığı (patlama olmadığı) sürece bu daha önce hiç
        // çağrılmıyordu, bu yüzden ghost küp yerleştirilen parçanın içinde/üzerinde görünmeye
        // devam edip "tuhaf/yarı saydam" bir görünüme yol açıyordu.
        gridManager.RefreshLayerVisibility();

        // Aynı türden 3+ hayvan birbirine bağlandıysa (yan yana/üst üste) parıldama efektini
        // güncelle; bağlantı koptuysa ilgili bloklardaki efekt burada otomatik durur.
        gridManager.RefreshSpeciesSparkle();

        // Tüketilen parçanın (boşalan kartın) yerine hemen yenisini getir
        SpawnRandomPiece();

        CheckGameOver(placedLayerY);
    }

    private void CheckGameOver(int placedLayerY)
    {
        if (gridManager == null) return;

        // Level tamamlanmışsa kayıp değil, kazanma/katman geçişi akışı çalışmalıdır.
        if (gridManager.IsComplete() || gridManager.IsLayerComplete(placedLayerY))
        {
            return;
        }

        // Kartlar henüz oluşturulmadıysa veya geçici olarak boşsa Retry açma.
        if (activePieces == null || activePieces.Count == 0)
        {
            return;
        }

        foreach (var pieceGO in activePieces)
        {
            if (pieceGO == null) continue;

            if (IsShapePlaceable(pieceGO))
            {
                return;
            }
        }

        // Eldeki HİÇBİR parça sığmıyor. Ama bu, seviyenin bittiği anlamına GELMEZ: eldeki üç
        // kart havuzdan rastgele dağıtıldı ve dağıtıldıkları anda sığıyorlardı — tahta doldukça
        // sığmaz oldular (bkz. PrepareNextPieceIndex, filtre yalnızca dağıtım anında bakıyor).
        // Havuzda hâlâ sığan bir parça varsa bu bir KAYIP değil, kötü bir dağıtımdır; oyuncuyu
        // cezalandırmak yerine ölü eli yeniliyoruz.
        if (TryRefreshDeadHand())
        {
            Debug.Log("♻️ Eldeki parçaların hiçbiri sığmıyordu, ama havuzda sığan parça var — " +
                      "el yenilendi (game over yerine).");
            return;
        }

        // GEÇİCİ TEŞHİS LOGU: gerçek "level fail" tetiklenmeden hemen önce TÜM katmanların tam
        // durumunu döker — özellikle hangi katmanların buzla ne kadar kaplı olduğunu görmek için
        // (bkz. buz/renk patlama mekanizmasının "kesin garanti değildir" notu, GridManager.cs).
        // Teşhis sonrası kaldırılabilir.
        LogGameOverDiagnostics();

        // Buraya yalnızca eldeki parçaların hiçbiri, izin verilen dönüşlerin hiçbirinde HİÇBİR
        // katmandaki boş hücrelere yerleşemiyorsa gelir (bkz. CanShapeFitAnyOpenLayer).
        GameManager.Instance?.GameOver();
    }

    /// <summary>
    /// Eldeki parçaların hiçbiri sığmadığında çağrılır. Seviyenin parça HAVUZUNDA
    /// (allPiecePrefabs) hâlâ yerleştirilebilir bir parça varsa, ölü kartları atıp eli
    /// yeniden dağıtır ve true döner — böylece kazanılabilir bir tahtada kötü dağıtım
    /// yüzünden kaybedilmez. Havuzda da sığan parça yoksa false döner ve çağıran taraf
    /// gerçek game over'ı tetikler.
    ///
    /// Bu gerekliydi çünkü seviyeler SolutionFirstBuilder ile TAM döşenecek şekilde
    /// üretiliyor, ama çalışma zamanı bu kümeyi rastgele dağıtıyor: örn. DENEME2'nin
    /// havuzunda üç adet 1 hücrelik parça varken oyuncuya 2/4/2 hücrelik üç ölü kart
    /// dağıtılıp tahtada iki adet tek hücrelik delik kalınca oyun "kaybettin" diyordu.
    /// </summary>
    private bool TryRefreshDeadHand()
    {
        if (allPiecePrefabs == null || allPiecePrefabs.Count == 0) return false;

        bool poolHasPlaceable = false;
        foreach (var prefab in allPiecePrefabs)
        {
            if (prefab != null && CanShapeFitAnyOpenLayer(prefab)) { poolHasPlaceable = true; break; }
        }
        if (!poolHasPlaceable) return false;

        // Ölü kartları boşalt.
        for (int i = activePieces.Count - 1; i >= 0; i--)
        {
            var go = activePieces[i];
            if (go != null)
            {
                // ClearPiece, yerleştirilmemiş parçayı kendisi yok eder (bkz. PieceCardUI) —
                // bu yüzden ayrıca Destroy çağırmıyoruz. Kartı olmayan parça varsa (spawn
                // sırasında boş kart bulunamamış olabilir) onu elle temizliyoruz.
                if (pieceToCard.TryGetValue(go, out var card))
                {
                    card.ClearPiece();
                    pieceToCard.Remove(go);
                }
                else
                {
                    Destroy(go);
                }
            }
            activePieces.RemoveAt(i);
            if (i < activePieceDataIndices.Count) activePieceDataIndices.RemoveAt(i);
            if (i < activeIsSmart.Count)          activeIsSmart.RemoveAt(i);
        }

        // Torbayı sıfırla: kurtarıcı parçalar (ör. 1x1 dolgular) bu seviyede zaten bir kez
        // dağıtılmış olabilir ve spawnedPieceIndices onları availableIndices'ten dışlıyor
        // olurdu — o zaman yeniden dağıtım da ölü kartlar üretirdi.
        spawnedPieceIndices.Clear();
        nextPieceIndex = -1;
        PrepareNextPieceIndex();

        for (int i = 0; i < maxVisiblePieces; i++)
            SpawnRandomPiece();

        return true;
    }

    // Artık fail HERHANGİ bir katmana sığmamaya bağlı olduğu için, tek bir katman değil TÜM
    // katmanlar (gridMinY..gridMaxY) taranıyor.
    private void LogGameOverDiagnostics()
    {
        Debug.LogWarning("🚫 GAME OVER TEŞHİSİ — Tüm katmanların durumu:");

        for (int y = gridManager.GridMinY; y <= gridManager.GridMaxY; y++)
        {
            int totalInLayer = 0, occupiedInLayer = 0, emptyFrozen = 0, emptyOpen = 0;
            var openCells = new List<Vector3Int>();
            var frozenCellsInLayer = new List<Vector3Int>();
            var occupiedCellsInLayer = new List<Vector3Int>();
            foreach (var c in gridManager.targetCells)
            {
                if (c.y != y) continue;
                totalInLayer++;
                if (gridManager.occupiedCells.Contains(c)) { occupiedInLayer++; occupiedCellsInLayer.Add(c); continue; }
                if (gridManager.frozenCells.Contains(c)) { emptyFrozen++; frozenCellsInLayer.Add(c); }
                else { emptyOpen++; openCells.Add(c); }
            }

            if (totalInLayer == 0) continue; // bu Y'de hiç hedef hücre yok

            Debug.LogWarning($"   Katman Y={y} | toplam={totalInLayer}, dolu={occupiedInLayer}, " +
                              $"boş+buzlu(yerleştirilemez)={emptyFrozen}, boş+açık(teorik yerleştirilebilir)={emptyOpen} — " +
                              $"Açık(X,Z): {string.Join(" | ", openCells.Select(c => $"({c.x},{c.z})"))}");

            if (emptyOpen == 0 && emptyFrozen > 0)
            {
                Debug.LogWarning($"   ⚠️ TEŞHİS: Katman Y={y}'deki TÜM boş hücreler buzlu — hiçbir parça buz eritilmeden yerleştirilemez.");
            }
        }

        for (int i = 0; i < activePieces.Count; i++)
        {
            var pieceGO = activePieces[i];
            if (pieceGO == null) continue;
            var holder = pieceGO.GetComponent<CubeShapeDataHolder>();
            if (holder == null) continue;
            string cellsStr = string.Join(" | ", holder.occupiedCells.Select(c => $"({c.x},{c.y},{c.z})"));
            Debug.LogWarning($"   Elde parça #{i}: {pieceGO.name}, hücre sayısı={holder.occupiedCells.Count}, " +
                              $"yerel şekil=[{cellsStr}], yerleştirilebilir=HAYIR (hiçbir açık katmana göre)");
        }
    }

    private bool CanPlacementThawIce(List<Vector3Int> rotatedCells, Vector3Int offset)
    {
        if (gridManager == null || gridManager.frozenCells.Count == 0) return false;

        Vector3Int[] neighbors = {
            Vector3Int.left, Vector3Int.right,
            Vector3Int.up, Vector3Int.down,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        List<Vector3Int> placedPositions = new List<Vector3Int>();
        foreach (var c in rotatedCells)
        {
            placedPositions.Add(c + offset);
        }

        // Artık ActiveLayerY değil, BU yerleştirmenin gerçek katmanı esas alınıyor — parçalar
        // herhangi bir katmana yerleştirilebiliyor (bkz. GridManager.CanPlace).
        int placementLayerY = placedPositions.Count > 0 ? placedPositions[0].y : gridManager.ActiveLayerY;
        foreach (var frozenCell in gridManager.frozenCells)
        {
            if (frozenCell.y != placementLayerY) continue;
            List<Vector3Int> inLayer = new List<Vector3Int>();
            foreach (var cell in placedPositions)
            {
                if (cell.y == frozenCell.y)
                {
                    inLayer.Add(cell);
                }
            }

            if (inLayer.Count < 2) continue;

            bool isAdjacent = false;
            foreach (var cell in inLayer)
            {
                foreach (var nOff in neighbors)
                {
                    if (cell + nOff == frozenCell)
                    {
                        isAdjacent = true;
                        break;
                    }
                }
                if (isAdjacent) break;
            }

            if (isAdjacent)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Öğreticinin joker adımı jokeri TÜKETMEMELİ: joker seviye başına tek
    /// kullanımlıktır ve oyuncunun onu asıl hata yaptığı anda elinde bulması gerekir.
    /// Öğretici adımı tamamlanınca hakkı geri veriyoruz (bkz. TutorialOverlay).</summary>
    public void RestoreJoker()
    {
        isJokerUsedThisLevel = false;
        foreach (var btn in FindObjectsOfType<ControlButton>())
            if (btn != null) btn.ResetJoker();
    }

    public void UndoLastPlace()
    {
        if (!CanUseJoker) return;
        isJokerUsedThisLevel = true;
        TutorialEvents.RaiseJokerUsed();

        GameObject lastPieceObj = placedPieces[placedPieces.Count - 1];
        placedPieces.RemoveAt(placedPieces.Count - 1);

        DraggablePiece piece = lastPieceObj.GetComponent<DraggablePiece>();
        if (piece != null)
        {
            Vector3Int offset = piece.PlacedOffset;
            var cells = piece.CurrentCells;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector3Int gridCell = cells[i] + offset;
                gridManager?.RemoveCellAnimated(gridCell, i * 0.05f);
            }

            // Animasyonlar tamamlandıktan sonra ana parça nesnesini yok et
            Destroy(lastPieceObj, cells.Count * 0.05f + 0.3f);
        }
        else
        {
            Destroy(lastPieceObj);
        }
    }

    public void ClearCurrentLevel()
    {
        // Devam eden katman/kanca animasyonlarını ve bekleyen gecikmeli çağrılarını
        // İLK İŞ olarak iptal et — aksi halde Retry'dan sonra önceki seviyenin
        // animasyonu oynamaya devam ediyor ve callback'leri yeni seviyeye sızıyor.
        gridManager?.CancelLevelAnimations();

        isJokerUsedThisLevel = false;

        var controlButtons = FindObjectsOfType<ControlButton>();
        foreach (var btn in controlButtons)
        {
            if (btn != null) btn.ResetJoker();
        }

        gridManager?.ClearAllCellObjects();
        if (activeMainPiece != null)
        {
            Destroy(activeMainPiece);
            activeMainPiece = null;
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.cube = null;
        }
        foreach (var p in activePieces) if (p != null) Destroy(p);
        activePieces.Clear();
        activePieceDataIndices.Clear();
        foreach (var p in placedPieces) if (p != null) Destroy(p);
        placedPieces.Clear();
        if (ghostTargetMat != null) { Destroy(ghostTargetMat); ghostTargetMat = null; }
        allPiecePrefabs.Clear();
        allPieceWidths.Clear();
        allPieceHeights.Clear();
        allPieceDepths.Clear();

        // Kartları sıfırla
        foreach (var c in pieceCards) c?.ClearPiece();
        pieceToCard.Clear();
        activeIsSmart.Clear();
        spawnedPieceIndices.Clear();

        // NEXT PIECE PREVIEW RESET
        nextPieceIndex = -1;
        nextPieceSpeciesIndex = -1;
        nextPieceMaterial = null;
        ClearNextPiecePreview();
    }

    private void FitCameraToScene()
    {
        if (CameraOrbit.Instance == null) return;

        bool first = true;
        Bounds total = new Bounds();

        void Include(GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { total = r.bounds; first = false; }
                else total.Encapsulate(r.bounds);
            }
        }

        // Sadece ana tahta dahil edilir.
        // Aktif parçalar artık x=-10000 civarında olduğundan dahil edilmez.
        Include(activeMainPiece);

        if (!first) CameraOrbit.Instance.FitInView(total);
    }

    private static Vector3 BoundsCenter(List<Vector3Int> cells, float step)
    {
        if (cells.Count == 0) return Vector3.zero;
        int minX = cells.Min(c => c.x), maxX = cells.Max(c => c.x);
        int minY = cells.Min(c => c.y), maxY = cells.Max(c => c.y);
        int minZ = cells.Min(c => c.z), maxZ = cells.Max(c => c.z);
        return new Vector3(
            (minX + maxX + 1) * 0.5f,
            (minY + maxY + 1) * 0.5f,
            (minZ + maxZ + 1) * 0.5f) * step;
    }

    private void DisableShadows(GameObject go)
    {
        if (go == null) return;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    private void ApplyTargetGhost(GameObject shape)
    {
        if (ghostTargetMaterial == null) return;

        var holder = shape.GetComponent<CubeShapeDataHolder>();

        // Hücreler yapıdan okunur (şeklin her doğrudan çocuğu bir hücredir), hücrenin
        // prefilled olup olmadığı ve TÜRÜ (matIdx) holder'ın kendi listelerinden gelir —
        // aynı liste LevelSolver'ın çözülebilirlik kontrolünde kullandığı listedir, böylece
        // ekranda görünen hayvan ile oyun mantığının türü ayrışamaz. İsme gömülü
        // "Prefilled_matIdx_..." yalnızca holder verisi olmayan eski prefab'lar için fallback.
        foreach (Transform cellRoot in shape.transform)
        {
            var renderers = cellRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;

            string cubeName = cellRoot.gameObject.name;
            bool nameIsPrefilled = cubeName.StartsWith("Prefilled_");
            bool nameIsCube      = cubeName.StartsWith("Cube_");
            if (holder == null && !nameIsPrefilled && !nameIsCube) continue;

            Vector3Int cell = holder != null
                ? holder.WorldToCell(cellRoot.position)
                : WorldToCellFallback(shape, cellRoot.position);

            int   matIdx         = -1;
            Color prefilledColor = Color.white;
            bool  isPrefilled    = false;
            if (holder != null)
                isPrefilled = holder.TryGetPrefilledInfo(cell, out matIdx, out prefilledColor);

            if (!isPrefilled && nameIsPrefilled)
            {
                // Eski veri: tür indeksi isimde kodlanmış.
                isPrefilled = true;
                var parts = cubeName.Split('_');
                if (parts.Length >= 2) int.TryParse(parts[1], out matIdx);
            }

            if (isPrefilled)
            {
                Material prefilledMat = null;
                if (pieceMaterials != null && matIdx >= 0 && matIdx < pieceMaterials.Length)
                    prefilledMat = pieceMaterials[matIdx];

                // Tür indeksi yok/geçersizse (bozuk veri) en yakın renge göre bul.
                if (prefilledMat == null && holder != null)
                    prefilledMat = FindClosestMaterial(prefilledColor);

                if (prefilledMat == null)
                    prefilledMat = pieceMaterials != null && pieceMaterials.Length > 0 ? pieceMaterials[0] : null;

                if (prefilledMat != null)
                {
                    foreach (var r in renderers)
                    {
                        var mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = prefilledMat;
                        r.sharedMaterials = mats;
                    }
                }

                GridManager.Instance.occupiedCells.Add(cell);

                if (prefilledMat != null)
                {
                    GridManager.Instance.SetCellColor(cell, GridManager.GetMaterialColor(prefilledMat));
                    // matIdx bilinmiyorsa (eski/bozuk veri) materyalden geri türet;
                    // pieceMaterials ve pieceSpeciesPrefabs aynı indekslemeyi paylaşır.
                    if (matIdx < 0) matIdx = FindMaterialIndex(prefilledMat);
                    GridManager.Instance.SetCellMatIndex(cell, matIdx);
                }

                if (pieceSpeciesPrefabs != null && matIdx >= 0 && matIdx < pieceSpeciesPrefabs.Length)
                    AttachSpeciesVisual(cellRoot, pieceSpeciesPrefabs[matIdx], cell);
            }
            else // Normal hedef (Ghost)
            {
                bool frozen = holder != null && holder.IsCellFrozen(cell);
                foreach (var r in renderers)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    if (frozen) continue;

                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = ghostTargetMaterial;
                    r.sharedMaterials = mats;
                }
            }
        }

        foreach (var col in shape.GetComponentsInChildren<Collider>())
            col.enabled = true;
    }

    /// <summary>CubeShapeDataHolder'ı olmayan eski şekiller için hücre hesabı.</summary>
    private static Vector3Int WorldToCellFallback(GameObject shape, Vector3 worldPos)
    {
        Vector3 lp = shape.transform.InverseTransformPoint(worldPos);
        return new Vector3Int(
            Mathf.RoundToInt(lp.x),
            Mathf.RoundToInt(lp.y),
            Mathf.RoundToInt(lp.z));
    }


    // Gerçek yerleştirme HÂLÂ sadece o an odaklanılan katmana (ActiveLayerY) izin veriyor —
    // oyuncu başka bir katmanda çalışmak için panelden o katmana GEÇMELİ (bkz. GridManager.CanPlace
    // notu). Ama "yerleştirilebilir mi" (deadlock/fail) sorusu farklı: oyuncu istediği zaman
    // panelden başka bir açık katmana geçebildiği için, elindeki bir parça o an odaklanılan
    // katmana sığmasa bile BAŞKA bir katmana sığıyorsa gerçekten "sıkışmış" sayılmamalı.
    private bool IsShapePlaceable(GameObject shapePrefabOrGO)
    {
        return CanShapeFitAnyOpenLayer(shapePrefabOrGO);
    }

    // PrepareNextPieceIndex (uygun parçaları önceliklendirmek için) ve IsShapePlaceable (gerçek
    // deadlock kontrolü için) tarafından paylaşılan tek doğru kaynak. Panelden geçilebilecek HER
    // katmanı tek tek dener (GetPossibleOffsetsOnLayer) — GetPossibleOffsets/CanPlace KULLANMAZ,
    // çünkü onlar gerçek yerleştirme kuralına (sadece ActiveLayerY) bağlı.
    private bool CanShapeFitAnyOpenLayer(GameObject shapePrefabOrGO)
    {
        if (shapePrefabOrGO == null || gridManager == null) return false;
        var h = shapePrefabOrGO.GetComponent<CubeShapeDataHolder>();
        if (h == null) return false;

        Quaternion[] rots = {
            Quaternion.identity,
            Quaternion.Euler(0, 90, 0),
            Quaternion.Euler(0, 180, 0),
            Quaternion.Euler(0, 270, 0),
            Quaternion.Euler(90, 0, 0),
            Quaternion.Euler(270, 0, 0)
        };

        for (int layerY = gridManager.GridMinY; layerY <= gridManager.GridMaxY; layerY++)
        {
            foreach (var r in rots)
            {
                var rotated = GridManager.RotateCells(h.occupiedCells, r);
                var offsets = gridManager.GetPossibleOffsetsOnLayer(rotated, layerY);
                if (offsets.Count > 0) return true;
            }
        }

        return false;
    }

    private Material FindClosestMaterial(Color targetColor)
    {
        if (pieceMaterials == null || pieceMaterials.Length == 0) return null;
        
        Material closest = null;
        float minDistance = float.MaxValue;
        
        foreach (var mat in pieceMaterials)
        {
            if (mat == null) continue;
            Color matColor = GridManager.GetMaterialColor(mat);
            float distance = ColorDistance(targetColor, matColor);
            
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = mat;
            }
        }
        
        return closest;
    }
    
    private int FindMaterialIndex(Material mat)
    {
        if (pieceMaterials == null || mat == null) return -1;
        
        for (int i = 0; i < pieceMaterials.Length; i++)
        {
            if (pieceMaterials[i] == mat) return i;
        }
        
        return -1;
    }
    
    private float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return dr * dr + dg * dg + db * db;
    }

    private void LateUpdate()
    {
        if (nextPiecePreview3D != null && nextPiecePreviewCam != null)
        {
            // Rotate slowly over time
            previewRotationAngle += Time.deltaTime * 30f;
            UpdateNextPiecePreviewTransform();
        }
    }

    private void OnDestroy()
    {
        ClearNextPiecePreview();
        if (nextPiecePreviewCam != null && nextPiecePreviewCam.targetTexture != null)
        {
            var rt = nextPiecePreviewCam.targetTexture;
            nextPiecePreviewCam.targetTexture = null;
            rt.Release();
            Destroy(rt);
        }
        if (nextPiecePreviewParent != null)
        {
            Destroy(nextPiecePreviewParent);
        }
    }

    private void ClearNextPiecePreview()
    {
        if (nextPiecePreview3D != null)
        {
            Destroy(nextPiecePreview3D);
            nextPiecePreview3D = null;
        }
    }

    private void InitNextPiecePreviewSystem()
    {
        // 1. Find UICanvas
        var canvas = GameObject.Find("UICanvas");
        if (canvas == null) return;

        // Remove old next piece preview if exists (for safety)
        var oldPanel = canvas.transform.Find("NextPiecePreviewPanel");
        if (oldPanel != null) Destroy(oldPanel.gameObject);

        // Get sprites directly from existing cards to guarantee identical design
        Sprite cardSprite = null;
        Sprite insetSprite = null;
        if (pieceCards != null && pieceCards.Count > 0 && pieceCards[0] != null)
        {
            var cardImgComp = pieceCards[0].GetComponent<Image>();
            if (cardImgComp != null) cardSprite = cardImgComp.sprite;

            var overlay = pieceCards[0].emptyOverlay;
            if (overlay != null)
            {
                var overlayImg = overlay.GetComponent<Image>();
                if (overlayImg != null) insetSprite = overlayImg.sprite;
            }
        }
        if (cardSprite == null) cardSprite = GetSpriteFromAtlas("GUI_52");
        if (insetSprite == null) insetSprite = GetSpriteFromAtlas("GUI_53");

        // 2. Create Next Piece Card (Perfect square matching GUI_52 styling)
        GameObject panelGO = new GameObject("NextPiecePreviewPanel", typeof(RectTransform));
        panelGO.transform.SetParent(canvas.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        
        panelRT.anchorMin = new Vector2(1f, 0f);
        panelRT.anchorMax = new Vector2(1f, 0f);
        panelRT.pivot = new Vector2(1f, 0f);
        panelRT.sizeDelta = new Vector2(140f, 140f);

        // Konum, BottomPiecePanel'e GÖRELİ hesaplanır — sabit (-40, 100) değeri
        // panel yukarı alınınca önizlemeyi aşağıda bırakıyor ve joker butonunun
        // üstüne biniyordu. Panelin altındaki özgün boşluğu (≈152 birim) koruyoruz,
        // böylece panel nereye taşınırsa önizleme de onunla birlikte gider.
        const float GAP_BELOW_PANEL = 152f;
        float previewY = 100f;
        var bottomPanel = canvas.transform.Find("BottomPiecePanel") as RectTransform;
        var canvasRT    = canvas.transform as RectTransform;
        if (bottomPanel != null && canvasRT != null)
        {
            float panelBottom = bottomPanel.anchorMin.y * canvasRT.rect.height
                                + bottomPanel.anchoredPosition.y;
            previewY = Mathf.Max(20f, panelBottom - GAP_BELOW_PANEL);
        }
        panelRT.anchoredPosition = new Vector2(-40f, previewY);

        var panelImg = panelGO.AddComponent<Image>();
        panelImg.sprite = cardSprite;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = Color.white;

        // Shadow/Outline matching bottom cards
        var shadow = panelGO.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.4f);
        shadow.effectDistance = new Vector2(3f, -3f);

        // 3. Create Preview Area Inset Background (GUI_53)
        GameObject bgGO = new GameObject("PreviewAreaBg", typeof(RectTransform));
        bgGO.transform.SetParent(panelGO.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0.05f, 0.05f);
        bgRT.anchorMax = new Vector2(0.95f, 0.95f);
        bgRT.sizeDelta = Vector2.zero;

        var bgImg = bgGO.AddComponent<Image>();
        bgImg.sprite = insetSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(1f, 1f, 1f, 0.15f);

        // 4. Create RawImage for RenderTexture
        GameObject rawGO = new GameObject("PreviewRawImage", typeof(RectTransform));
        rawGO.transform.SetParent(bgGO.transform, false);
        var rawRT = rawGO.GetComponent<RectTransform>();
        rawRT.anchorMin = Vector2.zero;
        rawRT.anchorMax = Vector2.one;
        rawRT.sizeDelta = Vector2.zero;

        nextPiecePreviewRawImage = rawGO.AddComponent<RawImage>();
        nextPiecePreviewRawImage.color = Color.white;

        // Create a real-time silhouette drop shadow for the next piece preview
        var oldShadow = bgGO.transform.Find("PreviewImageShadow");
        if (oldShadow != null) Destroy(oldShadow.gameObject);

        GameObject shadowGO = new GameObject("PreviewImageShadow", typeof(RectTransform), typeof(RawImage));
        shadowGO.transform.SetParent(bgGO.transform, false);
        shadowGO.transform.SetSiblingIndex(rawGO.transform.GetSiblingIndex()); // Place it behind the main raw image
        
        var shadowRT = shadowGO.GetComponent<RectTransform>();
        shadowRT.anchorMin = rawRT.anchorMin;
        shadowRT.anchorMax = rawRT.anchorMax;
        shadowRT.pivot = rawRT.pivot;
        shadowRT.sizeDelta = rawRT.sizeDelta;
        // Offset to the right and down (consistent with card shadow and top-left key light)
        shadowRT.anchoredPosition = new Vector2(10f, -10f);
        shadowRT.localScale = Vector3.one;

        var shadowImg = shadowGO.GetComponent<RawImage>();
        shadowImg.color = new Color(0f, 0f, 0f, 0.38f); // Distinct drop shadow matching light source

        // 5. Create Camera and Camera Root
        if (nextPiecePreviewParent != null) Destroy(nextPiecePreviewParent);
        nextPiecePreviewParent = new GameObject("NextPiecePreviewCameraRoot");
        nextPiecePreviewParent.transform.position = new Vector3(-20000f, 100f, -5000f);

        GameObject camGO = new GameObject("NextPiecePreviewCam");
        camGO.transform.SetParent(nextPiecePreviewParent.transform, false);
        camGO.transform.localPosition = new Vector3(0f, 3.5f, -5.5f);
        
        nextPiecePreviewCam = camGO.AddComponent<Camera>();
        nextPiecePreviewCam.clearFlags = CameraClearFlags.SolidColor;
        nextPiecePreviewCam.backgroundColor = Color.clear;
        nextPiecePreviewCam.orthographic = true;
        nextPiecePreviewCam.orthographicSize = 2f;
        nextPiecePreviewCam.nearClipPlane = 0.1f;
        nextPiecePreviewCam.farClipPlane = 30f;
        nextPiecePreviewCam.depth = -3;

        // Add studio key and fill lights to next piece preview
        var keyLight = new GameObject("NextPreviewKeyLight", typeof(Light));
        keyLight.transform.SetParent(nextPiecePreviewCam.transform, false);
        keyLight.transform.localPosition = new Vector3(-4f, 4f, 1f);
        var kL = keyLight.GetComponent<Light>();
        kL.type = LightType.Point;
        kL.range = 25f;
        kL.intensity = 3.5f;
        kL.shadows = LightShadows.Soft;
        kL.shadowStrength = 0.85f;
        kL.color = new Color(1f, 0.98f, 0.95f);

        var fillLight = new GameObject("NextPreviewFillLight", typeof(Light));
        fillLight.transform.SetParent(nextPiecePreviewCam.transform, false);
        fillLight.transform.localPosition = new Vector3(4f, -4f, 1f);
        var fL = fillLight.GetComponent<Light>();
        fL.type = LightType.Point;
        fL.range = 25f;
        fL.intensity = 1.2f;
        fL.color = new Color(0.85f, 0.9f, 1f);

        // Create RenderTexture
        var rt = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 2;
        rt.Create();
        nextPiecePreviewCam.targetTexture = rt;
        nextPiecePreviewRawImage.texture = rt;
        shadowImg.texture = rt;
    }

    private Sprite GetSpriteFromAtlas(string spriteName)
    {
        if (pieceCards != null && pieceCards.Count > 0)
        {
            foreach (var card in pieceCards)
            {
                if (card != null)
                {
                    var img = card.GetComponent<Image>();
                    if (img != null && img.sprite != null && img.sprite.name == spriteName)
                    {
                        return img.sprite;
                    }
                    var emptyOverlay = card.emptyOverlay;
                    if (emptyOverlay != null)
                    {
                        var eImg = emptyOverlay.GetComponent<Image>();
                        if (eImg != null && eImg.sprite != null && eImg.sprite.name == spriteName)
                        {
                            return eImg.sprite;
                        }
                    }
                }
            }
        }
        return Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == spriteName);
    }

    private void AssignDefaultFontAsset(TextMeshProUGUI tmp)
    {
        var otherText = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>().FirstOrDefault(t => t.font != null);
        if (otherText != null && otherText.font != null)
        {
            tmp.font = otherText.font;
        }
    }

    private void PrepareNextPieceIndex()
    {
        if (allPiecePrefabs.Count == 0)
        {
            nextPieceIndex = -1;
            nextPieceSpeciesIndex = -1;
            nextPieceMaterial = null;
            return;
        }

        List<int> availableIndices = new List<int>();
        for (int i = 0; i < allPiecePrefabs.Count; i++)
        {
            if (!spawnedPieceIndices.Contains(i))
            {
                availableIndices.Add(i);
            }
        }

        if (availableIndices.Count == 0)
        {
            spawnedPieceIndices.Clear();
            for (int i = 0; i < allPiecePrefabs.Count; i++)
            {
                availableIndices.Add(i);
            }
        }

        // DÜZELTİLDİ: eskiden availableIndices içinden TAMAMEN rastgele seçiliyordu — hiçbir
        // katmana sığmayan kartlar da dağıtılabiliyordu. Şimdi, mevcut havuzdan HERHANGİ bir açık
        // katmana GERÇEKTEN sığan parçalar varsa seçim onların arasından yapılıyor — hâlâ rastgele
        // (çeşitlilik korunuyor), ama artık "boşa" bir kart asla dağıtılmıyor. Hiçbir açık katmana
        // sığan parça kalmadıysa (tüm gerekli parçalar zaten dağıtılmış/yerleştirilmiş demektir)
        // eski tam-rastgele davranışa düşülür.
        List<int> fitsAnyLayer = new List<int>();
        foreach (int i in availableIndices)
        {
            if (CanShapeFitAnyOpenLayer(allPiecePrefabs[i]))
            {
                fitsAnyLayer.Add(i);
            }
        }
        List<int> pickFrom = fitsAnyLayer.Count > 0 ? fitsAnyLayer : availableIndices;

        nextPieceIndex = pickFrom[Random.Range(0, pickFrom.Count)];
        // Bu parçanın TÜRÜ ŞİMDİ, tam bu anda kararlaştırılır — hem önizlemede hem de parça
        // gerçekten spawn edilirken (bkz. SpawnRandomPiece) AYNI tür kullanılacak. nextPieceMaterial
        // sadece bu türün kozmetik materyali (önizleme render'ı için).
        nextPieceSpeciesIndex = PickPieceSpeciesIndex();
        nextPieceMaterial = nextPieceSpeciesIndex >= 0 ? pieceMaterials[nextPieceSpeciesIndex] : null;
        UpdateNextPiecePreviewVisuals();
    }

    private void UpdateNextPiecePreviewVisuals()
    {
        ClearNextPiecePreview();

        if (nextPieceIndex < 0 || allPiecePrefabs.Count == 0) return;
        if (nextPiecePreviewCam == null) return;

        var prefab = allPiecePrefabs[nextPieceIndex];
        nextPiecePreview3D = Instantiate(prefab, nextPiecePreviewParent.transform);

        foreach (var col in nextPiecePreview3D.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }
        var drag = nextPiecePreview3D.GetComponent<DraggablePiece>();
        if (drag != null) drag.enabled = false;

        Material matchingMaterial = nextPieceMaterial;
        if (matchingMaterial != null)
        {
            var renderers = nextPiecePreview3D.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (r.gameObject.name.StartsWith("Cube_"))
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = matchingMaterial;
                    r.sharedMaterials = mats;
                }
            }
        }

        // Önizleme, gerçekten spawn edilecek parçayla (bkz. SpawnPieceAtIndex) AYNI türü göstersin.
        ApplySpeciesVisual(nextPiecePreview3D, nextPieceSpeciesIndex);

        nextPiecePreview3D.transform.position = Vector3.zero;
        nextPiecePreview3D.transform.rotation = Quaternion.identity;
        nextPiecePreview3D.transform.localScale = Vector3.one;

        var rends = nextPiecePreview3D.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            nextPieceVisualCenter = nextPiecePreview3D.transform.InverseTransformPoint(b.center);
            nextPieceVisualRadius = b.extents.magnitude;
            if (nextPieceVisualRadius < 0.001f) nextPieceVisualRadius = 1f;
        }
        else
        {
            nextPieceVisualCenter = Vector3.zero;
            nextPieceVisualRadius = 1f;
        }

        UpdateNextPiecePreviewTransform();
    }

    private void UpdateNextPiecePreviewTransform()
    {
        if (nextPiecePreview3D == null || nextPiecePreviewCam == null) return;

        float depth = 3.5f;
        float viewH = 2f * nextPiecePreviewCam.orthographicSize;
        float scale = (viewH * 0.90f * 0.5f) / Mathf.Max(nextPieceVisualRadius, 0.001f);

        Vector3 center = nextPiecePreviewCam.transform.position + nextPiecePreviewCam.transform.forward * depth;

        float elev = 90f;
        float azim = 0f;
        Quaternion baseIso = Quaternion.Euler(elev, azim, 0f);

        Quaternion targetRot = nextPiecePreviewCam.transform.rotation * Quaternion.Inverse(baseIso) * Quaternion.Euler(0f, previewRotationAngle, 0f);

        nextPiecePreview3D.transform.rotation = targetRot;
        nextPiecePreview3D.transform.position = center - (targetRot * nextPieceVisualCenter * scale);
        nextPiecePreview3D.transform.localScale = Vector3.one * scale;
    }
}