# Sıralı Katman Doldurma Mekaniği — Tasarım Dokümanı

Durum: **Taslak / Tartışmaya Açık.** Bu doküman implementasyon planı değildir; mevcut sistemi
özetler, hedeflenen değişikliği tanımlar ve karara bağlanması gereken açık noktaları listeler.

---

## 1. Amaç

Şu anki oynanışta oyuncu, aktif (henüz patlamamış) katmanlardan **istediğini** seçip
doldurabiliyor (bkz. §2.2). Yeni mekanikte katmanlar **belirli bir sırayla** doldurulmak
zorunda olacak: sıradaki katman tamamlanıp patlamadan bir sonrakine geçilemeyecek.

Bununla birlikte, oyuncuya verilen **parçaların şekli** de değişecek: şu anda parçalar soyut
bir kütüphaneden (`PieceDefinition`) bir çözücü tarafından örneklenip yerleştiriliyor (bkz.
§2.3). Yeni sistemde parça şekilleri, **Level Builder (katman oluşturma) aracında** o katman
çizilirken/parçalara ayrılırken ortaya çıkan somut şekillerden referans alınacak.

---

## 2. Mevcut Sistem (Referans)

### 2.1 Katman/patlama mekaniği (`GridManager`)
- Oyun zaten "layer-by-layer" (`lineClearEnabled = false`) çalışıyor: bir katmanın tüm
  `targetCells`'i dolunca `ExplodeLayer(...)` tetikleniyor, katman patlıyor, üstündekiler bir
  aşağı kayıyor (claw/kanca varsa farklı gecikmeyle).
- `ActiveLayerY`, "oyuncunun şu an odaklandığı katman" anlamına geliyor — patlayan katmanın
  kendisi değil. Katman patladığında kamera odağı otomatik olarak `TryFindFirstIncompleteLayer`
  ile bir sonraki tamamlanmamış katmana kayıyor (bkz. `GridManager.cs:1163-1174`).
- `IsLayerCleared` her zaman `false` döner — "daralan grid" modelinde patlayan katman tamamen
  siliniyor, "temizlenmiş ama duran" bir katman kavramı yok.
- Seviye üretim tarafında (`SolutionFirstBuilder`) çözüm zaten **alttan üste, katman katman**
  backtracking ile kuruluyor (`GetLowestIncompleteLayer`) — yani "katmanlar sırayla dolar"
  fikri şu an sadece **çözümün kurulma şeklinde** var, **oynanışta** değil.

### 2.2 Serbest katman seçimi (`LayerPanelController`)
- `BuildLayerButtons()` patlamamış tüm katmanlar için birer buton üretiyor (alttan üste
  sıralı dizilmiş ama hepsi tıklanabilir).
- `OpenPanel(layerY)` — tek istisna: **tutorial** akışındayken sadece `GridMinY` açılabiliyor
  (`TutorialOverlay` kontrolü). Tutorial dışında oyuncu **herhangi bir aktif katmanı**
  seçip doldurabiliyor; sıra zorunluluğu yok.
- Yani "sıralı doldurma" bugün sadece öğreticide var, gerçek oynanışta yok.

### 2.3 Parça üretim hattı (level tasarım zamanı)
- `PieceDefinition` (ScriptableObject): `Assets/PieceDefinitions/` altında saklanan, **seviyeden
  bağımsız**, soyut bir parça şekli kütüphanesi (hücreler, izinli rotasyonlar, spawn ağırlığı,
  maksimum kopya sayısı vb.).
- `AILevelDesignerWindow`, bu kütüphaneden bir alt küme örnekler → `SolutionFirstBuilder`
  bu havuzla hedef hücreleri (katman katman, alttan üste) **tam olarak** döşemeye çalışır
  (backtracking) → başarılı olursa sonuç, çözümdeki rotasyonda prefab'lara "pişirilip"
  `LevelData.complementaryPieces` listesine yazılır.
- `LevelManager` çalışma zamanında `PieceDefinition`'ı hiç görmez — sadece önceden pişirilmiş
  prefab listesini kullanır. Yani **runtime'daki parçalar zaten belirli katmanlara ait somut
  şekillerdir**, ama bu şekiller soyut kütüphaneden geldiği için **o katmanın çizilmiş hâliyle
  görsel/tasarımsal bir bağı yoktur** — sadece geometrik olarak sığıyor olması yeterlidir.
- `LevelData.complementaryPieces` düz bir liste (`List<GameObject>`) — hangi parçanın hangi
  katmana ait olduğu bilgisi bu listede **saklanmıyor** (çözüm zamanında biliniyor ama
  export edilmiyor).

### 2.4 Level Builder / katman oluşturma aracı (`LevelBuilderWindow`)
- "2D Katman Editörü (Şekil Çizme + Export)" — tasarımcı her katmanı ayrı ayrı seçip
  (`activeLayer`) o katmandaki hücreleri `Shape / Prefilled / Ice / Erase / DisableCell`
  modlarıyla elle işaretliyor.
- Burada üretilen veri: `occupiedCells` (hedef şekil), `prefilledCells`, `frozenCells` (buz),
  `disabledCells`. **Parça** (hangi hücre grubu tek bir oynanabilir parça olacak) kavramı bu
  araçta şu an **tanımlanmıyor** — sadece "bu hücre dolu olacak" bilgisi var.
- `PieceSplitterWindow` adında ayrı (şu an menüden kapalı/legacy görünen) bir araç var; bir
  şekli parçalara bölme fikri kod tabanında zaten mevcut ama aktif hatta bağlı değil.

---

## 3. Hedeflenen Değişiklik #1: Sıralı Katman Doldurma

- Oyuncu, sıradaki (bir alttaki tamamlanmamış) katman dışında bir katmanı **açamayacak**.
  Bu, tutorial'da zaten var olan kısıtlamanın (§2.2) **tüm oyuna** genelleştirilmesi demek.
- Katman tamamlanma/patlama (`ExplodeLayer`) mantığı muhtemelen **aynen korunur** — değişen,
  `LayerPanelController.OpenPanel` / `BuildLayerButtons`'ın hangi katmanlara izin verdiği.
- Sıra yönü bugünkü "alttan üste" mi kalacak, yoksa Level Builder'da katman bazında
  **özel bir sıra** mı tanımlanabilecek (örn. üstten alta, ya da tasarımcının belirlediği
  keyfi bir dizilim)? → §5'te açık soru.

## 4. Hedeflenen Değişiklik #2: Parça Şekillerinin Kaynağı

- Şu anki "soyut kütüphaneden örnekle + backtracking ile döşe" modeli yerine, parça şekilleri
  **doğrudan Level Builder'da o katman için tanımlanan/ortaya çıkan parçalardan** referans
  alınacak.
- Bunun pratik anlamı: Level Builder'a (veya `PieceSplitterWindow`'a) katmanın hedef
  hücrelerini **somut parçalara bölme** adımı eklenmesi — bu bölme otomatik (bir algoritma
  hücre grubunu parçalara ayırır) mı, elle (tasarımcı fare ile "bu hücreler bir parça"
  diye işaretler) mi, yoksa ikisinin karışımı mı olacak? → §5'te açık soru.
- `PieceDefinition` kütüphanesi tamamen devre dışı mı kalacak, yoksa "izin verilen şekil
  havuzu" olarak (örn. otomatik bölmenin sadece kütüphanedeki şekillere izin vermesi için)
  hâlâ bir rolü olacak mı?

---

## 5. Mantıksal Durumlar / Karar Noktaları (Tartışılacak)

Aşağıdakiler doküman yazılırken bilinçli olarak **cevaplanmadı** — birlikte konuşup
şekillendirmek için açık bırakıldı.

1. **Sıra yönü**: Her zaman alttan üste sabit mi, yoksa katman başına (Level Builder'da)
   tanımlanabilir keyfi bir sıra mı?
2. **Parçalara bölme yöntemi**: Otomatik algoritma / elle işaretleme / hibrit? Elle ise,
   Level Builder'a mı yoksa `PieceSplitterWindow`'a mı entegre edilecek?
3. **Kütüphanenin rolü**: `PieceDefinition` + `SolutionFirstBuilder` hattı tamamen mi
   kalkıyor, yoksa yeni sistemle birlikte (ör. otomatik bölme modunda şekil kısıtı olarak)
   yaşamaya mı devam ediyor?
4. **Veri modeli değişikliği**: `LevelData.complementaryPieces` düz liste yerine katman
   bazlı gruplanmış bir yapıya (`List<LayerPieces>` gibi) mı geçmeli? Bu, mevcut ~30
   seviyenin (`Assets/Levels/...`) yeniden export edilmesini/migrate edilmesini gerektirir.
5. **Önizleme davranışı**: Oyuncuya her zaman sadece aktif katmanın parçaları mı
   gösterilecek, yoksa `NextPiecePreviewPanel` gibi ileriye dönük bir önizleme sıradaki
   katmanın parçalarını da gösterecek mi?
6. **Buz/prefilled hücreler**: Bu hücreler zaten belirli bir katmana ait — sıralı sistemde
   davranışları değişir mi (ör. bir üst katmandaki buz, alt katman tamamlanmadan hiç
   görünmeyecek mi)?
7. **Skor/hedef sistemi**: `targetScore` ve zaman sınırı (`timeLimit`) sıralı katman
   sistemiyle nasıl etkileşecek — katman başına ayrı bir hedef mi, yoksa toplam mı?
8. **Geriye dönük uyumluluk**: Mevcut seviyeler yeni formatla oynanabilir kalacak mı
   (otomatik migration), yoksa yeni sistem sadece yeni tasarlanan seviyelerde mi geçerli
   olacak?

---

## 6. Sonraki Adım

Bu doküman bir **implementasyon planı değil**. §5'teki açık noktalar konuşulup karara
bağlandıktan sonra, hangi dosyaların (`GridManager`, `LayerPanelController`, `LevelData`,
`LevelBuilderWindow`, `PieceSplitterWindow`, `SolutionFirstBuilder`, `AILevelDesignerWindow`)
nasıl değişeceğini adım adım planlayacağız.
