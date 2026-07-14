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

    public int ActiveLayerY { get; private set; }

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
        occupiedCells.Clear();
        targetCells.Clear();
        allShapeCells.Clear();
        cellMatIndex.Clear();
        frozenCells.Clear();
        ClearAllCellObjects();

        targetRenderers.Clear();
        prefilledRenderers.Clear();
        float step = cellSize + spacing;

        if (mainShape != null)
        {
            var holder = mainShape.GetComponent<CubeShapeDataHolder>();
            List<Vector3Int> prefilled = holder != null && holder.prefilledCells != null
                ? holder.prefilledCells
                : new List<Vector3Int>();

            // Mantıksal grid'i yalnızca görünür Renderer'lardan kurarsak,
            // panelde gizli olan üst katmanlar hiç kaydedilmez.
            // Bu yüzden bütün hücreleri doğrudan CubeShapeDataHolder'dan alıyoruz.
            bool loadedLogicalCellsFromHolder =
                holder != null &&
                holder.occupiedCells != null &&
                holder.occupiedCells.Count > 0;

            if (loadedLogicalCellsFromHolder)
            {
                foreach (var cell in holder.occupiedCells)
                {
                    allShapeCells.Add(cell);

                    if (prefilled.Contains(cell))
                        occupiedCells.Add(cell);
                    
                    targetCells.Add(cell);
                }
            }

            // true: Kapalı/disabled çocuk Renderer'ları da bul.
            foreach (var r in mainShape.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;

                string name = r.gameObject.name;
                bool isCube = name.StartsWith("Cube_");
                bool isPrefilled = name.StartsWith("Prefilled_");
                if (!isCube && !isPrefilled) continue;

                Vector3 localPos = mainShape.transform.InverseTransformPoint(r.transform.position);
                int x = Mathf.RoundToInt(localPos.x / step);
                int y = Mathf.RoundToInt(localPos.y / step);
                int z = Mathf.RoundToInt(localPos.z / step);

                var cell = new Vector3Int(x, y, z);

                // Holder verisi yoksa eski Renderer tabanlı sistemi fallback olarak kullan.
                if (!loadedLogicalCellsFromHolder)
                {
                    allShapeCells.Add(cell);
                    targetCells.Add(cell);
                }

                if (isPrefilled || prefilled.Contains(cell))
                {
                    occupiedCells.Add(cell);
                    prefilledRenderers[cell] = r;

                    if (r.sharedMaterial != null)
                        cellColors[cell] = GetMaterialColor(r.sharedMaterial);

                    // "Prefilled_matIdx_x_y_z" -> parse matIdx
                    var parts = name.Split('_');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedIdx))
                        cellMatIndex[cell] = parsedIdx;
                }
                else
                {
                    targetRenderers[cell] = r;
                }
            }
        }

        var shapeHolder = mainShape != null ? mainShape.GetComponent<CubeShapeDataHolder>() : null;
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
            foreach (var cell in shapeHolder.frozenCells)
            {
                frozenCells.Add(cell);
            }
        }

        // İlk tamamlanmamış gerçek katmanı bul.
        // targetCells yerine allShapeCells kullanılır; böylece gizli/prefilled/frozen
        // hücreleri bulunan katmanlar da doğru şekilde hesaba katılır.
        if (!TryFindFirstIncompleteLayer(out int firstIncompleteLayer))
            ActiveLayerY = gridMinY;
        else
            ActiveLayerY = firstIncompleteLayer;
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

        // Hedef (ghost) renderer'ları kontrol et
        foreach (var kvp in targetRenderers)
        {
            Vector3Int cell = kvp.Key;
            Renderer r = kvp.Value;
            if (r != null)
            {
                if (occupiedCells.Contains(cell)) 
                {
                    r.enabled = false;
                }
                else if (isPanelMode)
                {
                    if (cell.y == ActiveLayerY)
                    {
                        r.enabled = true;
                        r.transform.localScale = Vector3.one * CellSize;
                    }
                    else if (cell.y < ActiveLayerY)
                    {
                        r.enabled = true;
                        r.transform.localScale = Vector3.one * CellSize;
                    }
                    else
                    {
                        r.enabled = false;
                    }
                }
                else
                {
                    r.enabled = true; // 3D modunda hepsi görünür
                    r.transform.localScale = Vector3.one * CellSize;
                }

                if (r.enabled)
                {
                    r.GetPropertyBlock(PropBlock);
                    if (frozenCells.Contains(cell))
                    {
                        Color iceColor = new Color(0.75f, 0.9f, 1.0f, 0.75f);
                        if (isPanelMode && cell.y < ActiveLayerY) iceColor.a = 0.15f; // Faded ice
                        PropBlock.SetColor("_BaseColor", iceColor);
                        PropBlock.SetColor("_Color", iceColor);
                        
                        Color emissionColor = new Color(0.1f, 0.4f, 0.7f) * (isPanelMode && cell.y < ActiveLayerY ? 0.2f : 1.8f);
                        PropBlock.SetColor("_EmissionColor", emissionColor);
                    }
                    else
                    {
                        Color defaultColor = new Color(0.41f, 0.57f, 0.35f, 0.53f);
                        if (LevelManager.Instance != null && LevelManager.Instance.ghostTargetMaterial != null)
                        {
                            defaultColor = LevelManager.Instance.ghostTargetMaterial.color;
                        }
                        if (isPanelMode && cell.y < ActiveLayerY) defaultColor.a = 0.30f; // Faded target base
                        PropBlock.SetColor("_BaseColor", defaultColor);
                        PropBlock.SetColor("_Color", defaultColor);
                        PropBlock.SetColor("_EmissionColor", Color.clear);
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
                        r.transform.localScale = Vector3.one * CellSize;
                        
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
                        r.transform.localScale = Vector3.one * CellSize;
                        
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
                    r.transform.localScale = Vector3.one * CellSize;
                    
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
                        cube.transform.localScale = Vector3.one * CellSize;
                        
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
                        cube.transform.localScale = Vector3.one * CellSize;
                        
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
                    cube.transform.localScale = Vector3.one * CellSize;
                    
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

    private bool TryFindFirstIncompleteLayer(out int layerY)
    {
        for (int y = gridMinY; y <= gridMaxY; y++)
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

        layerY = gridMinY;
        return false;
    }

    // layerY: patlatılacak (tamamlanmış) katman — artık her zaman ActiveLayerY olmak zorunda
    // değil, oyuncu herhangi bir katmanı tamamlamış olabilir (bkz. LevelManager.OnPiecePlaced).
    // Çökme matematiği zaten Y-göreceli (clearedY'nin üstündeki her şey 1 aşağı kayar), tek
    // fark ActiveLayerY'nin artık "hangi katman patladı" değil "kamera/panel hangi katmana
    // odaklanmış" anlamına gelmesi — bu yüzden patlamadan sonra ayrıca güncelleniyor (aşağıda).
    public void ExplodeLayer(int layerY, System.Action onLayerComplete, System.Action onLevelComplete)
    {
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

        // Yok edilen katmanın fiziksel merkezini hesapla
        Vector3 center = Vector3.zero;
        int count = 0;
        foreach (var cell in cellsToRemove)
        {
            if (cellObjects.TryGetValue(cell, out var go) && go != null)
            {
                center += go.transform.position;
                count++;
            }
            // Prefilled blokları da merkez hesabına dahil et
            else if (prefilledRenderers.TryGetValue(cell, out var prefilledRend) && prefilledRend != null)
            {
                center += prefilledRend.transform.position;
                count++;
            }
        }
        if (count > 0) center /= count;

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

        AnimateLayerDisappear(layerContainer, blocksToAnimate, moveOffset);

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
        foreach (var kvp in cellObjects)
        {
            if (kvp.Key.y >= clearedY) // Önceden > clearedY idi, artık 1 azaldıkları için >= clearedY oldu
            {
                var t = kvp.Value.transform;
                t.DOLocalMoveY(t.localPosition.y - Step, 0.45f).SetEase(Ease.OutQuad).SetDelay(0.45f);
            }
        }

        foreach (var kvp in targetRenderers)
        {
            if (kvp.Key.y >= clearedY)
            {
                var t = kvp.Value.transform;
                t.DOLocalMoveY(t.localPosition.y - Step, 0.45f).SetEase(Ease.OutQuad).SetDelay(0.45f);
            }
        }

        // Prefilled blokları da görsel olarak aşağı kaydır
        foreach (var kvp in prefilledRenderers)
        {
            if (kvp.Key.y >= clearedY)
            {
                var t = kvp.Value.transform;
                t.DOLocalMoveY(t.localPosition.y - Step, 0.45f).SetEase(Ease.OutQuad).SetDelay(0.45f);
            }
        }

        RefreshLayerVisibility();

        // Level yalnızca gerçekten hiçbir katman/hücre kalmadığında tamamlanır.
        // Sadece targetCells.Count kontrolü gizli üst katmanları yok sayabiliyordu.
        if (allShapeCells.Count == 0)
        {
            ActiveLayerY = gridMaxY + 1;
            onLevelComplete?.Invoke();
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
                if (TryFindFirstIncompleteLayer(out int nextLayer))
                    ActiveLayerY = nextLayer;
                else
                    ActiveLayerY = gridMinY;
            }
            else if (ActiveLayerY > clearedY)
            {
                ActiveLayerY--;
            }
            // ActiveLayerY < clearedY: değişmez.

            RefreshLayerVisibility();
            onLayerComplete?.Invoke();
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

    public void AddCell(Vector3Int cell, GameObject cube, Color color, int matIndex = -1)
    {
        occupiedCells.Add(cell);
        cellObjects[cell] = cube;
        cellColors[cell] = color;
        cellMatIndex[cell] = matIndex;

        if (targetRenderers.TryGetValue(cell, out var r) && r != null)
        {
            r.enabled = false;
        }

        StartCoroutine(BumpAnimation(cube.transform));
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

    private static void AnimateLayerDisappear(GameObject container, List<GameObject> blocks, Vector3 moveOffset)
    {
        if (container == null) return;
        var t = container.transform;

        var seq = DOTween.Sequence().SetLink(container);

        // 1. Aşama: Gitmeden önce zıt yöne hafif esneme (Anticipation)
        seq.Append(t.DOMove(t.position - (moveOffset.normalized * 0.3f), 0.15f).SetEase(Ease.OutQuad));

        // 2. Aşama: Topluca sağa/sola gitme ve şeffaflaşarak kaybolma
        seq.Append(t.DOMove(t.position + moveOffset, 0.45f).SetEase(Ease.InCubic));

        // Tüm blokların materyallerini şeffaflaştır
        foreach (var block in blocks)
        {
            if (block == null) continue;
            var r = block.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                Color originalColor = GetMaterialColor(r.material);
                Color transparentColor = new Color(originalColor.r, originalColor.g, originalColor.b, 0f);
                
                if (r.material.HasProperty("_BaseColor"))
                    seq.Join(r.material.DOColor(transparentColor, "_BaseColor", 0.4f));
                else if (r.material.HasProperty("_Color"))
                    seq.Join(r.material.DOColor(transparentColor, "_Color", 0.4f));
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
                        Vector3 cellWorldPos = CellToWorld(cells[i]);
                        float dist = Vector3.Distance(cellWorldPos, hit.point);
                        if (dist < minWorldDist)
                        {
                            minWorldDist = dist;
                            closestIndex = i;
                        }
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

        float bestValidD = 4.5f;
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
    // ActiveLayerY'ye izin veriyor. "Elimdeki hiçbir parça hiçbir katmana sığmıyor mu" sorusu
    // (fail kontrolü/kart önceliklendirme) ayrı bir sorgu — bkz. GetPossibleOffsetsOnLayer/
    // CanPlaceOnLayer ve LevelManager.CanShapeFitAnyOpenLayer.
    public bool CanPlace(List<Vector3Int> cells, Vector3Int offset)
    {
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

        // 2. Şimdi yeni snaplenen kılavuz hücrelerini gizle
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

    // CanPlace/GetPossibleOffsets'ten farklı olarak GERÇEK yerleştirme kuralını (ActiveLayerY
    // kısıtı) uygulamaz — "bu parça, layerY'ye GEÇİLSEYDİ oraya sığar mıydı" sorusuna cevap verir.
    // Sadece LevelManager.CanShapeFitAnyOpenLayer tarafından (fail kontrolü/kart önceliklendirme
    // için, panelden başka bir katmana geçilebileceğini hesaba katmak amacıyla) kullanılır —
    // gerçek yerleştirme HÂLÂ sadece ActiveLayerY'ye izin veriyor (bkz. CanPlace).
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
    // DÜZELTİLDİ (renksiz sisteme geçiş, 4 aşama):
    // 1) Önce katman monokromluğu tamamen kaldırılırken buz da "herhangi bir temas = erime"
    //    diye basitleştirilmişti (hiçbir renk şartı yoktu).
    // 2) Sonra ekip kararıyla buz için renk şartı KISMEN geri getirildi: buz hücresine komşu
    //    olan hücrelerden EN AZ İKİSİ aynı renkteyse buz erir.
    // 3) Erimeyi tetikleyen SADECE İKİ komşu hücre yok oluyordu (genel grup/patlama yoktu).
    // 4) [2026-07-14, ekip kararıyla, eski mantığa dönüş] Artık buza değen hücreden başlayarak
    //    BAĞLANTILI aynı renkteki TÜM grup hesaplanıyor (flood-fill/BFS) — grup büyüklüğü
    //    ≥2 ise grubun TAMAMI buzla birlikte patlıyor (yok oluyor, hücreleri yeniden
    //    doldurulmalı). Grup büyüklüğü artık sabit-2 değil, keyfi olabilir. Renk hâlâ
    //    rastgele atandığından bu mekanik tamamen şansa bağlı; LevelSolver KESİN garanti
    //    veremez, sadece best-effort simüle eder (bkz. LevelSolver.cs'teki ilgili not).

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

        // Buza değen (yeni yerleşip bir buz hücresine komşu olan) hücreleri ve etkilenen
        // buz hücrelerini bul.
        var touchingCells = new HashSet<Vector3Int>();
        var candidateFrozenCells = new HashSet<Vector3Int>();
        foreach (var cell in newlyPlacedCells)
        {
            foreach (var offset in horizontalNeighbors)
            {
                Vector3Int neighbor = cell + offset;
                if (frozenCells.Contains(neighbor))
                {
                    touchingCells.Add(cell);
                    candidateFrozenCells.Add(neighbor);
                }
            }
        }

        if (candidateFrozenCells.Count == 0)
        {
            onComplete?.Invoke(false);
            return false;
        }

        HashSet<Vector3Int> cellsToThaw = new HashSet<Vector3Int>();
        HashSet<Vector3Int> cellsToDestroy = new HashSet<Vector3Int>();

        foreach (var touchCell in touchingCells)
        {
            if (!GetCellColor(touchCell, out Color touchColor)) continue;

            var group = FloodFillSameColor(touchCell, touchColor, horizontalNeighbors);
            if (group.Count < 2) continue;

            cellsToDestroy.UnionWith(group);
            foreach (var offset in horizontalNeighbors)
            {
                Vector3Int neighbor = touchCell + offset;
                if (frozenCells.Contains(neighbor)) cellsToThaw.Add(neighbor);
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

    // start hücresinden başlayarak, occupiedCells içinde 'color' ile aynı renkte olan ve
    // yatay komşuluk üzerinden birbirine bağlı tüm hücreleri (start dahil) döndürür.
    private HashSet<Vector3Int> FloodFillSameColor(Vector3Int start, Color color, Vector3Int[] horizontalNeighbors)
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
                if (!GetCellColor(neighbor, out Color col)) continue;
                if (!ColorsApproxEqual(col, color)) continue;

                visited.Add(neighbor);
                stack.Push(neighbor);
            }
        }

        return visited;
    }

    // Bir Renderer'ı (var olan bir hedef ghost'u ya da patlamış bir prefilled küpü) normal,
    // boş "hedef" (ghost) hücre görünümüne döndürür ve targetRenderers'a kaydeder. Prefilled
    // hücrelerde Initialize() hiç ayrı bir ghost objesi üretmediği için, o hücredeki küp
    // patladığında geriye gösterilecek hiçbir şey kalmıyordu — bu fonksiyon küpün kendi
    // Renderer'ını ghost olarak yeniden kullanır.
    private void RestoreAsGhostTarget(Vector3Int cell, Renderer rend)
    {
        if (rend == null) return;

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

    // DÜZELTİLDİ (renksiz sisteme geçiş, 4. aşama): eriyen buz hücreleri normal boş hedef
    // hücreye dönerken, buza değen hücreden başlayan BAĞLANTILI aynı renk grubu (≥2 üyeli)
    // da ANINDA YOK OLUR — grubun tüm hücreleri boşalır ve tekrar bir parçayla doldurulması
    // gerekir (bkz. CheckAndResolveFrozenCells başındaki not / FloodFillSameColor).
    private IEnumerator AnimateThawAndDestroy(HashSet<Vector3Int> cellsToThaw, HashSet<Vector3Int> cellsToDestroy, System.Action onComplete)
    {
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
                System.Action onThisIceDone = () => {
                    RestoreAsGhostTarget(cell, rend);
                    onOneEffectDone();
                };

                if (IceBreakEffect.Instance != null)
                {
                    IceBreakEffect.PlayIceMelt(rend.gameObject, onThisIceDone);
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
                prefilledRenderers.Remove(cell);
                Color shardColor = prefRend.sharedMaterial != null ? GetMaterialColor(prefRend.sharedMaterial) : Color.white;
                CreateShatterEffect(prefRend.transform.position, shardColor);
                RestoreAsGhostTarget(cell, prefRend);
                onOneEffectDone();
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
                r.material.color = new Color(0.8f, 0.95f, 1.0f, 0.85f); // Frosty ice-blue
                
                r.material.EnableKeyword("_EMISSION");
                r.material.SetColor("_EmissionColor", new Color(0.15f, 0.5f, 0.85f) * 2.5f);
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
}