# BlockMerge3D — Çözülebilir ve Dengeli 3D Puzzle Seviye Üretim Sistemi (v2)
### Gerçek Proje Koduna Göre Düzeltilmiş ve Birleştirilmiş Teknik Spesifikasyon

**Kapsam:** Bu doküman, önceki "Solution-First Generation" teknik rehberini temel alır, ancak `ezgidmrplt0/BlockMerge3D` reposundaki gerçek `GridManager.cs`, `LevelManager.cs`, `DraggablePiece.cs`, `GameManager.cs` ve `LayerPanelController.cs` kodları incelenerek düzeltilmiştir. v1'deki mimari önerisi genel olarak doğruydu (Solution-First yaklaşımı korunuyor), ancak üç noktada projenin gerçek davranışıyla çelişiyordu. Bu doküman o çelişkileri gideriyor ve Claude Code'a (veya başka bir AI'a) tek, tutarlı bir kaynak olarak verilebilecek şekilde yazıldı.

---

## 0. v1'e Göre Neyin Değiştiği (Özet)

| Konu | v1 Varsayımı | Gerçek Kod Davranışı | Bu Dokümanda Ne Yapıldı |
|---|---|---|---|
| Board boyutu | Sabit `boardSize {x,y,z}` | **Dinamik daralan grid** — her katman temizlenince üst katmanlar bir aşağı kayıyor, `gridMaxY--` | Board "başlangıç boyutu" olarak modellendi; Solution Builder collapse'ı simüle ediyor |
| Renk/katman kısıtı | Hiç yok | `IsLayerComplete()` hem doluluk hem **aynı renk/materyal** şartı arıyordu | Önce Bölüm 2'ye ana kural olarak eklendi, sonra **2026-07-13'te TAMAMEN kaldırıldı** (bkz. satır altı ve Bölüm 2.2) — katman artık sadece doluluğa göre tamamlanıyor, renk kozmetik |
| Piece Library | Var sayılıyor (Corner/Line/Flat tipleri) | **Yok** — parçalar `LevelData.complementaryPieces` içine elle atanan düz prefab'lar | Faz 1 artık "entegrasyon" değil, "sıfırdan inşa" olarak tanımlandı |
| Unique solution | Zor/Uzman'da isteniyor | — | Sadece **Uzman**'da isteniyor (performans nedeniyle) |
| Merge/hint sistemi | Hiç bahsi yok | `LevelManager.FindBestPieceIndex()` + `GridManager.GetMergeColor()` — otomatik ipucu/kolaylaştırma katmanı var | Bölüm 9'a yeni bölüm olarak eklendi |
| Puanlama | `cellsCleared * pointsPerCell` her temizlemede işliyor varsayıldı | `lineClearEnabled = false` layer-mode'da sürekli kapalı; `ExplodeActiveLayer()` hiç skor eklemiyor — **puanlama şu an kopuk** | Zorluk skoru ile oyun-içi puan ayrıştırıldı, Bölüm 11'de not edildi |
| Game Over / deadlock | Sadece aktif katman | `CheckGameOver()` **temizlenmemiş tüm katmanları** tarıyor, sadece görünen katmanı değil | Bölüm 8'e deadlock tanımı olarak eklendi |
| **(2026-07-13/14, 4 aşamalı evrim)** Buz eritme | "Bedava" — sadece erir, hiçbir şey kaybolmaz varsayılıyordu | (1) `AnimateExplodeAndThaw`'ın ≥2 hücrelik aynı-renk grubu PATLATTIĞI bulundu, Solver'a eklendi. (2) Renk/monokromluk tamamen kaldırılınca buz "herhangi bir temas = erime"ye basitleştirildi. (3) Ekip kararıyla buz için renk şartı KISMEN geri getirildi: ≥2 komşu aynı renkteyse erir (patlama hâlâ yok, şansa bağlı). (4) Ekip kararıyla erimeyi tetikleyen o 2 komşu da ANINDA YOK OLUR — hücreleri yeniden doldurulmalı; üretim tarafı buz başına 2 yedek parça ekler (bkz. 2.2) | Bölüm 2.2'de güncel (4. aşama) kural olarak belgeleniyor |

---

## 1. Problem Tanımı ve Tasarım Varsayımları

Oyun alanı 3D voxel grid'den oluşur; her tamamlayıcı parça (`CubeShapeDataHolder` bileşenli prefab) bir veya daha fazla bağlı küpten meydana gelir. Oyuncu bu parçaları sürükleyip döndürerek (Y ekseninde serbest, X ekseninde de yerleştirme kontrolünde denenen) hedef şekli doldurur.

**Mevcut üretimdeki sorunlar (değişmedi):**
- Hedef şekil ve parçalar bağımsız/rastgele üretiliyor
- Gerçek bir Solver çalıştırılmadığı için çözümsüz seviyeler mümkün
- Katman geçişleri ve görünürlük bağımlılıkları hesaba katılmıyor
- Zorluk sadece parça sayısına dayanıyor

**Yeni tespit edilen ek kısıt:**
- Board, oyun sırasında **küçülüyor**. Bir üretim sistemi board'u sabit bir dikdörtgen prizma gibi düşünürse, katman temizlendikten sonra üstteki katmanların aşağı kaydığı gerçeğini kaçırır ve zorluk/çözülebilirlik hesapları yanlış çıkar.

**Başarı ölçütleri (değişmedi):** çözülebilirlik garantisi, zorluk tutarlılığı, seed determinizmi, mobilde milisaniye seviyesinde performans.

---

## 2. Temel İlke: Çözümden Seviyeye Üretim (Renksiz Sistem)

### 2.1. Solution-First Generation (korundu)
Seviye önce çözülmüş halde kurulur: parçalar hedef 3D ızgaraya backtracking ile yerleştirilir, çözüm izi kaydedilir, sonra parçalar karıştırılıp sunulur, bağımsız bir Solver ile doğrulanır.

### 2.2. ⭐ Katman Tamamlama Kuralı: Sadece Doluluk [2026-07-13 renksiz sisteme geçişle güncellendi]

**v1/v2'nin önceki hallerinde** katman tamamlama hem doluluk HEM monokromluk (tüm hücrelerin aynı
renk/materyal olması) şartına bağlıydı, ve buz eritme de "aynı renkli ≥2 hücrelik grup" tanımına
dayanan, eriyen grubu da patlatıp yok eden bir mekanikti ("buz vergisi"). Bu iki kural birbirine
zincirlenerek üretim ve doğrulamayı ciddi şekilde zorlaştırıyordu: zorluk arttıkça (3x3x3+) Solver
ve AI üretimi pratikte çözüm bulamaz hale geliyordu, ve renk ataması küçük bir tutarsızlıkta
(spawn sırası, buz vergisi hesabı) oyunda sessiz kilitlenmelere yol açıyordu.

**Karar (kullanıcı onaylı):** renk/monokromluk kısıtı TAMAMEN kaldırıldı. Gerçek kod artık:

```csharp
// GridManager.IsLayerComplete()
public bool IsLayerComplete()
{
    // Katmandaki TÜM hücreler dolu mu? Tek şart bu — renk/materyal hiç kontrol edilmiyor.
}
```

Renk hâlâ var ama katman tamamlama açısından tamamen **kozmetik**: her parça spawn edilirken
paletten düz rastgele bir renk alır (`LevelManager.PickCosmeticPieceColor`), katmanın dolup
dolmadığı kararını hiç etkilemez.

**Buz — TEK istisna [2026-07-14, ekip kararıyla güncellendi, 2 aşamada]:** Katman tamamlama
renksiz olsa da, buz erimesi kasıtlı olarak KISMEN renk-bağımlı bırakıldı: bir buz hücresinin
yatay komşularından **EN AZ İKİSİ aynı renkteyse** buz erir:

```csharp
// GridManager.CheckAndResolveFrozenCells (güncel)
// Buz hücresinin ≤4 yatay komşusu arasında, aynı renkte olan ≥2 tanesi varsa buz erir.
```

**[2026-07-14, ikinci güncelleme] Tetikleyen çift de anında yok olur:** erimeyi tetikleyen o 2
aynı renkli komşu hücre (parça ya da prefilled blok) **ANINDA YOK OLUR** — hücreleri boşalır ve
tekrar bir parçayla doldurulması gerekir. Bu, "buz vergisi" kavramını KISMEN geri getiriyor, ama
eski genel patlama/grup mekaniğinden farklı: sadece bu 2 spesifik tetikleyici hücre etkilenir,
başka hiçbir hücre kaybolmaz. Sonuç: buz içeren bir seviyede toplam parça hacmi artık ham hedef
hücre sayısına TAM eşit olmak ZORUNDA değil — üretim tarafı (`AILevelDesignerWindow.
SplitShapeWithSolutionFirstLibrary`) her buz hücresi için **2 fazladan tek-hücrelik "yedek"
parça** ekler (her buz hücresi en fazla BİR kez erir ve en fazla 2 hücre yok eder varsayımıyla).
Hiç yok-olma yaşanmazsa bu yedek parçalar seviye tamamlanmadan önce hiç çekilmez, zararsız kalır.

**Önemli kısıt:** parça rengi RASTGELE atandığı için bu mekanik kasıtlı olarak **şansa bağlı** —
Solver bunu KESİN garanti EDEMEZ, sadece `pieceIndex`-tabanlı bir vekil (proxy) renkle best-effort
simüle eder (bkz. `LevelSolver.currentMatIndex`). Prefilled hücrelerin rengi ise tasarımcı
tarafından SABİT belirlendiği için (rastgele değil) bu konuda gerçek oyunla birebir tutarlıdır —
buza komşu bir prefilled hücre, solver'ın hesabına güvenilir şekilde katılır (ve tetikleyici
olursa, prefilled blok da diğerleri gibi yok olabilir). Ayrıca: proxy renk artık `pieceIndex`'e
bağlı olduğundan, aynı geometrik şekle sahip iki parça artık FONKSİYONEL OLARAK ÖZDEŞ DEĞİL (farklı
erime/yok-olma sonucu doğurabilirler) — bu yüzden `BacktrackingSolve`'daki "aynı şekli tek kez
dene" performans budaması, buz içeren seviyelerde OTOMATİK OLARAK DEVRE DIŞI kalır (daha pahalı
ama doğru arama; buzsuz seviyelerde hâlâ aktif). Sonuç: buzsuz bir seviyenin "solvable" raporu
KESİN bir garantidir, ama buz içeren bir seviyenin raporu SADECE "uygun renk şansı tutarsa ve
yedek parçalar yeterli gelirse çözülebilir" anlamına gelir.

### 2.3. Üretim Akışı (güncellendi)
1. Zorluk profili seçilir (Kolay/Orta/Zor/Uzman)
2. Başlangıç board boyutu (X, Y, Z) ve toplam hedef hücre sayısı belirlenir
3. Aday parçalar seçilir, saf geometrik backtracking ile yerleştirilir (renk/katman-renk kısıtı yok)
4. Yerleşim izi ("Çözüm İzi") kaydedilir
5. Parçalar karıştırılır (yön, sıra); renk paletten rastgele/kozmetik olarak atanır
6. Bağımsız `LevelSolver`, **collapse-aware** kurallarla (bkz. Bölüm 3) çözülebilirliği doğrular

---

## 3. Collapse-Aware Grid Modeli (Yeni Bölüm)

Gerçek `GridManager.ExplodeActiveLayer()` şunu yapıyor:

```csharp
// Katman temizlenince:
// 1. cellsToRemove'daki hücreler occupiedCells/targetCells'ten silinir
// 2. clearedY'nin ÜSTÜNDEKİ tüm hücreler y-1'e kaydırılır (mantıksal + görsel)
// 3. gridMaxY-- (tavan iner)
// 4. TryFindFirstIncompleteLayer() ile yeni ActiveLayerY belirlenir
```

Yani board **statik bir kutu değil, üstten aşağı çöken bir sistem**. Bu, üretim sistemi için üç somut gereksinim doğuruyor:

1. **Solution Builder**, katmanları alttan üste doğru sırayla planlamalı; üst bir katmandaki parça yerleşimi, alt katman temizlenip collapse olduktan SONRA oyuncunun göreceği pozisyonla eşleşmeli. Board'un "görünen" boyutu oyun ilerledikçe küçüldüğü için, JSON'daki `boardSize` alanı artık **"başlangıç boyutu"** anlamına gelmeli, sabit bir sınır değil.
2. **Difficulty Evaluator**, `Y_LayerComplexity` hesaplarken collapse sonrası oluşacak yeni katman sınırlarını da simüle etmeli (bir üst katmandaki parça, alttaki katman temizlenmeden yerleştirilemiyorsa bu gerçek bir "order dependency", ama collapse sonrası mı yoksa collapse öncesi mi değerlendiriliyor, net olmalı).
3. **Solver**, katman blokaj kontrolünü (Bölüm 8.3) collapse davranışına göre güncellemeli: "üst katman alt katmanı erişilmez kılıyor mu" sorusu artık "üst katman TEMİZLENMEDEN önce alt katmana erişimi kapatıyor mu" şeklinde, çünkü üst katman zaten alt katman temizlenince otomatik olarak ona doğru kayacak.

---

## 4. Piece Library — Sıfırdan İnşa (v1'de "entegrasyon" deniyordu, YANLIŞ)

Gerçek kodda parça altyapısı şu an bundan ibaret:
- `LevelData.complementaryPieces`: elle atanmış `GameObject` prefab listesi
- Her prefab bir `CubeShapeDataHolder` taşıyor (`occupiedCells`, `cellSize`, `spacing`)
- `spawnWeight`, `difficultyTags`, `maxCopiesPerLevel`, kanonik imza — **hiçbiri yok**

Yani v1'in Bölüm 4'ü ("Manuel Parça Kütüphanesinin Yapısı") **doğru bir hedef**, ama "mevcut sisteme entegre edilecek" değil, **sıfırdan yazılacak yeni bir katman**. Bu, Faz 1'in kapsamını büyütüyor:

**Faz 1 (güncellenmiş kapsam):**
- Yeni `PieceDefinition` ScriptableObject'i tanımla (id, cells, allowedRotations, volume, difficultyTags, spawnWeight, maxCopiesPerLevel)
- Mevcut `CubeShapeDataHolder` tabanlı prefab'ları tara, `occupiedCells` listesinden otomatik `PieceDefinition` üret (migration script)
- Kanonik imza normalizasyonu ile mükerrer parçaları tespit et
- `LevelData.complementaryPieces` (List\<GameObject\>) yerine/yanında `PieceDefinition` referansları kullanacak şekilde veri modelini genişlet (geriye dönük uyumluluk için GameObject alanı korunabilir)

---

## 5. Parça ve Seviye Zorluk Metrikleri (v1'den korundu, değişmedi)

Voxel parça metrikleri (küp sayısı, bounding box, doluluk oranı, yüzey alanı, kompaktlık, 3D simetri, dikey yayılım, uç nokta sayısı) ve formülü aynen geçerli:

```
pieceDifficulty = 0.25*(1-compactness3D) + 0.20*(1-symmetryScore3D) + 0.20*densityDeviation
                  + 0.20*uniqueRotationCountScore + 0.15*verticalSpreadingScore
```

Seviye zorluk formülü de aynen korunuyor, ama artık **collapse sonrası** katman sayısı üzerinden hesaplanmalı:

```
levelDifficulty = 0.20*pieceCountScore + 0.18*Y_LayerComplexity + 0.18*delayedFailureRatio
                  + 0.16*orderDependencyScore + 0.14*branchingFactorScore + 0.14*pieceSimilarityRatio
```

Aralıklar: 0-24 Kolay · 25-49 Orta · 50-74 Zor · 75-100 Uzman

---

## 6. Seviye Üretim Algoritması (renksiz — saf geometri)

**Aşama 1 — Hedef Alan Maskesi ve Profil:**
Zorluk profiline göre başlangıç hacim ve grid boyutu belirlenir. Renk/materyal ataması bu aşamada YOK — tamamen kozmetik, üretim mantığını etkilemiyor.

**Aşama 2 — 3D Uyumlu Parça Seçimi:**
Toplam hacmi hedefe TAM eşitleyecek aday parçalar seçilir (Solution-First backtracking). Buz varsa
bunun ÜZERİNE, her buz hücresi için 2 fazladan tek-hücrelik "yedek" parça eklenir — tetikleyen
çiftin yok olup yeniden doldurulması ihtimaline karşı (bkz. Bölüm 2.2, "Tetikleyen çift de anında
yok olur"). Buzsuz seviyelerde hacim hâlâ TAM eşit.

**Aşama 3 — Çözülmüş Şeklin Kurulması:**
Backtracking, "en az yerleşim seçeneği olan büyük/düzensiz parçalar önce" kuralıyla çalışır. İzole boşluk oluşursa dal erken kesilir. Katman-renk kombinasyonu diye bir kısıt yok — sadece geometri.

**Aşama 4 — Başlangıç Karıştırması:**
Parçalar hedeften çıkarılır; dönüş açıları ve sunum sırası karıştırılır. Kozmetik renk, spawn anında paletten rastgele atanır (`LevelManager.PickCosmeticPieceColor`).

---

## 7. Çözülebilirlik ve Benzersiz Çözüm Kontrolü (değişmedi, netleştirildi)

- **Kolay:** çoklu çözüm kabul edilebilir
- **Orta / Zor:** en az 1 çözüm yeterli (performans için unique-solution zorunlu tutulmuyor)
- **Uzman:** yalnızca burada tek (unique) çözüm hedeflenir — çünkü tüm çözüm uzayını sayıp tekliği kanıtlamak pahalıdır ve mobil "milisaniyeler" hedefiyle çelişebilir

---

## 8. Deadlock / Game Over Tanımı [2026-07-13'te gerçek bug bulunup düzeltildi]

**Eski davranış (hatalıydı):** `LevelManager.IsShapePlaceable()` TÜM temizlenmemiş katmanları
tarıyordu (sadece aktif katmanı değil), "şu an panelde görünmeyen bir üst katmana teorik olarak
sığıyorsa oynanabilir say" mantığıyla. Bu varsayım YANLIŞTI: `GridManager.CanPlace()` gerçekte
SADECE `ActiveLayerY`'ye yerleştirmeye izin veriyor — katmanlar kesin sırayla (alttan üste)
işleniyor, "oyuncu başka bir katmanda ilerleyebilir" diye bir şey yok. Sonuç: oyuncunun elindeki
TÜM kartlar (rastgele dağıtım yüzünden) henüz aktif olmayan bir katmana ait olabiliyordu — bu
durumda gerçekte hiçbir hamle yapılamıyordu ama `CheckGameOver()` bunu "hâlâ oynanabilir" sanıp
asla "Kaybettin" göstermiyordu. Oyun **sessizce, hiçbir geri bildirim vermeden kilitleniyordu**.

**Düzeltildi:** `IsShapePlaceable()` artık SADECE `gridManager.ActiveLayerY`'ye bakıyor —
`GridManager.CanPlace` ile birebir tutarlı. Ayrıca kilitlenmeyi kökünden önlemek için
`LevelManager.PrepareNextPieceIndex()` artık, havuzda varsa, aktif katmana GERÇEKTEN sığan
parçaları önceliklendiriyor (`CanShapeFitActiveLayer`) — rastgelelik korunuyor ama oyuncuya asla
"şu an kullanılamaz" bir kart yığını dağıtılmıyor.

> **Deadlock koşulu (güncel):** Eldeki hiçbir kartın, 6 rotasyonun hiçbirinde, SADECE aktif
> katmana yerleşememesi.

---

## 9. Merge/Hint Sistemi (Yeni Bölüm — v1'de hiç yoktu)

`LevelManager.FindBestPieceIndex()` şu an oyunda aktif olmayan ama kodda duran bir "akıllı öneri" sistemi içeriyor:
1. Önce **merge fırsatı** arar (`GridManager.GetMergeColor` — büyük olasılıkla aynı renkli komşu hücrelerle otomatik birleşme/uyum)
2. Yoksa "en çok komşu renk eşleşmesi" sağlayan parça+rotasyon+renk kombinasyonunu puanlayarak seçer (`CalculateMatchScore`)

Bu fonksiyon şu an `SpawnRandomPiece()` tarafından çağrılmıyor (`SpawnRandomPiece` basitçe `nextPieceIndex`'i kullanıyor), yani **muhtemelen kullanılmayan/geliştirme aşamasında bir özellik**. Üretim sistemi tasarlanırken:
- Eğer bu sistem ileride aktif edilecekse, üretilen seviyelerin bu "akıllı öneri" ile çözülebilir kalması ayrıca doğrulanmalı
- Eğer terk edilecekse, dokümantasyondan tamamen çıkarılmalı ve `FindBestPieceIndex`/`GetMergeColor` kod tabanından temizlenmeli (aksi halde AI ajanlarına çelişkili sinyal verir)

**Öneri:** Bu konuyu netleştirmeden Faz 3'e geçilmemeli.

---

## 10. Yapay Zekaya Öğretme (v1'den korundu, alan eklendi)

JSON eğitim veri formatı (renk artık kozmetik olduğu için katman-renk haritası içermiyor):

```json
{
  "levelId": "L_BM3D_HARD_104",
  "difficultyLabel": "Zor",
  "initialBoardSize": {"x": 5, "y": 2, "z": 5},
  "pieceIds": ["P_Corner_3D_A", "P_Line_3D_B", "P_Flat_T_C"],
  "solution": [
    {"pieceId": "P_Corner_3D_A", "pos": {"x": 0, "y": 0, "z": 0}, "rot": 90},
    {"pieceId": "P_Line_3D_B", "pos": {"x": 2, "y": 1, "z": 0}, "rot": 180}
  ],
  "metrics": {
    "layerComplexity": 0.65,
    "delayedFailureRatio": 0.42,
    "solutionCount": 1,
    "difficultyScore": 72
  }
}
```

`initialBoardSize` alan adı bilinçli olarak `boardSize`'dan değiştirildi — collapse sonrası board'un küçüleceğini net şekilde ifade etsin diye.

---

## 11. Test, Telemetri ve Dengeleme (v1'den korundu + not)

Telemetri tabloları (medyan çözüm süresi, restart oranı, ipucu kullanımı) aynen geçerli.

**⚠️ Bilinen kopukluk:** `GameManager.OnLinesCleared()` üzerinden işleyen puanlama (`pointsPerCell`), şu an ana oynanış döngüsünde (`ExplodeActiveLayer`) tetiklenmiyor çünkü `lineClearEnabled` layer-mode'da kalıcı olarak `false`. Telemetri toplarken **"Score" alanına güvenilmemeli** — bunun yerine `level_complete`, `piece_placed`, `undo`, `restart/fail` gibi olay bazlı metrikler zorluk kalibrasyonunun asıl kaynağı olmalı. Puanlama sistemi ayrı bir mühendislik görevi olarak ele alınmalı (bu doküman kapsamı dışında).

---

## 12. Uygulama Yol Haritası (güncellendi)

- **Faz 1 — Parça Altyapısı (genişletilmiş kapsam):** `PieceDefinition` sıfırdan yazılacak; mevcut prefab'lardan migration script ile otomatik üretim; kanonik imza kontrolü
- **Faz 2 — Deterministik Çözücü:** `LevelSolver.cs`; collapse-aware simülasyon; deadlock tanımı Bölüm 8'e göre
- **Faz 3 — Çözümden Seviye Üretimi:** Piece Selector + Solution Builder + Scrambler (renksiz, saf geometrik); merge/hint sistemi kararı netleşmeden başlanmamalı (Bölüm 9)
- **Faz 4 — Zorluk Derecelendirmesi:** collapse sonrası metriklerle formül hayata geçirilir
- **Faz 5 — Editör Arayüzü:** görsel önizleme, çözüm oynatıcı, metrik dışa aktarma
- **Faz 6 — Telemetri ve Kalibrasyon:** puanlama kopukluğu çözülene kadar Score yerine olay-bazlı metrikler kullanılır

---

## 13. Hatalı Yaklaşımlar ve Koruma Kuralları (v1'den korundu + 2 yeni kural)

| Hatalı Yaklaşım | Doğru Yaklaşım |
|---|---|
| Hedef şekil ve parçaları rastgele üretmek | Önce çözümü kur; hedef şekli yerleşen parçalardan türet |
| Board'u sabit boyutlu varsaymak | Collapse-aware simülasyon; `initialBoardSize` ≠ sabit sınır |
| Katman doluluğunu yetersiz sayıp ayrıca renk/monokromluk aramak | **Sadece doluluk şart** (`IsLayerComplete`) — renk kozmetik, 2026-07-13'te kaldırıldı (Bölüm 2.2) |
| Zorluğu sadece parça sayısıyla ölçmek | Dallanma, katman geçişi, gecikmeli hata oranını ölç |
| Deadlock'u tüm temizlenmemiş katmanlara göre değerlendirmek (gerçekte sadece aktif katmana erişilebiliyor) | Sadece aktif katmana göre değerlendir (Bölüm 8 — 2026-07-13'te düzeltilen gerçek bug) |
| Solver doğrulaması olmadan seviyeyi onaylamak | Her seviye için collapse-aware Solver çalıştır |
| Timeout/hata durumunda seviyeyi yayına almak | Timeout'u "Belirsiz" kabul et, reddet |
| Buzlu bir seviyenin Solver "solvable" raporunu buzsuz bir seviyeninkiyle AYNI güvenilirlikte saymak | Buz erimesi renk şansına bağlı VE erimeyi tetikleyen çift yok olup yeniden doldurulması gerekebilir (Bölüm 2.2) — buzlu seviyelerin raporu "kesin" değil, "uygun şans + yeterli yedek parça varsa" anlamına gelir |
| Buzlu bir seviyede parça hacminin hedefe TAM eşit olması gerektiğini varsaymak (buzsuz seviyelerdeki kural) | Buz varsa üretim, her buz hücresi için 2 fazladan tek-hücrelik yedek parça ekler — hacim hedeften FAZLA olabilir, bu kasıtlı (Bölüm 2.2) |

**Zorunlu Koruma Kuralları:**
1. Doğrulanmamış (`validated == false`) hiçbir seviye oyuna dahil edilemez
2. Kütüphanede mükerrer (aynı imza) parça tespit edilirse uyarı verilmeli
3. Zorluk profilleri güncellendiğinde tüm kayıtlı seviyeler yeniden puanlanmalı
4. Merge/hint sistemi (Bölüm 9) hakkında karar verilmeden Faz 3 tamamlanmış sayılmaz

---

## Ek A. Varsayılan 3D Zorluk Profilleri (v1'den korundu)

| Parametre | Kolay | Orta | Zor | Uzman |
|---|---|---|---|---|
| Parça Adedi | 3-5 | 5-8 | 7-11 | 9-14 |
| Toplam Hacim (Küp) | 12-24 | 20-40 | 32-60 | 45-80 |
| Ortalama Parça Zorluğu | 0-30 | 20-50 | 40-70 | 60-100 |
| Dikey Katman Sayısı (başlangıç) | 1-2 | 2-3 | 3-4 | 4-5 |
| Alternatif Çözüm Sayısı | 3+ | 1-3 | Mümkünse 1 | Kesinlikle 1 |
| Hedef Zorluk Skoru | 0-24 | 25-49 | 50-74 | 75-100 |

## Ek B. Seviye Kabul Kontrol Listesi (güncellendi)

- [ ] Parçaların toplam hacmi hedef şeklin hacmine TAM eşit mi? (buzsuz seviyelerde her zaman tam eşit; buzlu seviyelerde hedef + buz-başına-2-yedek üst sınırı içinde kalmalı — bkz. Bölüm 2.2)
- [ ] Kayıtlı çözüm izi, collapse davranışı dahil gerçek oyun kurallarıyla tamamlanabiliyor mu?
- [ ] Bağımsız Solver en az bir geçerli çözüm buluyor mu?
- [ ] (Yalnızca Uzman) Alternatif ikinci bir çözümün olmadığı onaylandı mı?
- [ ] Deadlock kontrolü SADECE aktif katman üzerinden mi yapıldı (Bölüm 8)?
- [ ] Seviyenin zorluk skoru (0-100) hedef profil sınırları içinde mi?
- [ ] Seed değeri kaydedildi ve tekrar üretilebiliyor mu?
- [ ] Mobil performans sınırları dahilinde doğrulama yapıldı mı?
- [ ] `LevelData` "validated" olarak işaretlendi mi?

**Önemli İlke:** Önce çözümü kur, sonra oyuncunun başlangıç durumunu üret — board'un collapse ile küçüleceğini ve deadlock'un sadece aktif katmana göre değerlendiğini asla unutma. Renk artık sadece kozmetik.
