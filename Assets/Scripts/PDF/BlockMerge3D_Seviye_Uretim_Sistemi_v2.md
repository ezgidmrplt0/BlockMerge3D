# BlockMerge3D — Çözülebilir ve Dengeli 3D Puzzle Seviye Üretim Sistemi (v2)
### Gerçek Proje Koduna Göre Düzeltilmiş ve Birleştirilmiş Teknik Spesifikasyon

**Kapsam:** Bu doküman, önceki "Solution-First Generation" teknik rehberini temel alır, ancak `ezgidmrplt0/BlockMerge3D` reposundaki gerçek `GridManager.cs`, `LevelManager.cs`, `DraggablePiece.cs`, `GameManager.cs` ve `LayerPanelController.cs` kodları incelenerek düzeltilmiştir. v1'deki mimari önerisi genel olarak doğruydu (Solution-First yaklaşımı korunuyor), ancak üç noktada projenin gerçek davranışıyla çelişiyordu. Bu doküman o çelişkileri gideriyor ve Claude Code'a (veya başka bir AI'a) tek, tutarlı bir kaynak olarak verilebilecek şekilde yazıldı.

---

## 0. v1'e Göre Neyin Değiştiği (Özet)

| Konu | v1 Varsayımı | Gerçek Kod Davranışı | Bu Dokümanda Ne Yapıldı |
|---|---|---|---|
| Board boyutu | Sabit `boardSize {x,y,z}` | **Dinamik daralan grid** — her katman temizlenince üst katmanlar bir aşağı kayıyor, `gridMaxY--` | Board "başlangıç boyutu" olarak modellendi; Solution Builder collapse'ı simüle ediyor |
| Renk/katman kısıtı | Hiç yok | `IsLayerComplete()` hem doluluk hem **aynı renk/materyal** şartı arıyor | Bölüm 2'ye ana kural olarak eklendi |
| Piece Library | Var sayılıyor (Corner/Line/Flat tipleri) | **Yok** — parçalar `LevelData.complementaryPieces` içine elle atanan düz prefab'lar | Faz 1 artık "entegrasyon" değil, "sıfırdan inşa" olarak tanımlandı |
| Unique solution | Zor/Uzman'da isteniyor | — | Sadece **Uzman**'da isteniyor (performans nedeniyle) |
| Merge/hint sistemi | Hiç bahsi yok | `LevelManager.FindBestPieceIndex()` + `GridManager.GetMergeColor()` — otomatik ipucu/kolaylaştırma katmanı var | Bölüm 9'a yeni bölüm olarak eklendi |
| Puanlama | `cellsCleared * pointsPerCell` her temizlemede işliyor varsayıldı | `lineClearEnabled = false` layer-mode'da sürekli kapalı; `ExplodeActiveLayer()` hiç skor eklemiyor — **puanlama şu an kopuk** | Zorluk skoru ile oyun-içi puan ayrıştırıldı, Bölüm 11'de not edildi |
| Game Over / deadlock | Sadece aktif katman | `CheckGameOver()` **temizlenmemiş tüm katmanları** tarıyor, sadece görünen katmanı değil | Bölüm 8'e deadlock tanımı olarak eklendi |

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

## 2. Temel İlke: Çözümden Seviyeye Üretim + Renk/Katman Kısıtı

### 2.1. Solution-First Generation (korundu)
Seviye önce çözülmüş halde kurulur: parçalar hedef 3D ızgaraya backtracking ile yerleştirilir, çözüm izi kaydedilir, sonra parçalar karıştırılıp sunulur, bağımsız bir Solver ile doğrulanır.

### 2.2. ⭐ Zorunlu Ek Kural: Katman Monokromluğu
Oyunun asıl çekirdek mekaniği budur ve v1'de eksikti. Gerçek kod (`GridManager.IsLayerComplete()`):

```csharp
public bool IsLayerComplete()
{
    // 1. Katmandaki TÜM hücreler dolu olmalı
    // 2. VE tüm bloklar aynı cellMatIndex / aynı renkte olmalı (ColorsApproxEqual ile)
    // İkisi birden sağlanmazsa katman "tamamlanmamış" sayılır.
}
```

Bu, Solution Builder için şu anlama gelir:
- Bir katmana atanan hücreler backtracking sırasında **doldurulmuş olmakla kalmayıp aynı renk grubuna** ait olmalı.
- Piece Selector, bir katmana yerleştirilecek parçaları seçerken **o katmanın hedef rengini** önceden belirlemeli (katman başına 1 renk ataması).
- `TryFindFirstIncompleteLayer()` mantığı önemli bir detay içeriyor: bir katman **dolu ama tek renk değilse**, yine "tamamlanmamış" sayılıyor — yani üretim sırasında yanlışlıkla bir katmana iki renk sızarsa (örneğin prefilled bir engelin rengi farklıysa) o seviye Solver tarafından geçersiz sayılmalı.

### 2.3. Üretim Akışı (güncellendi)
1. Zorluk profili seçilir (Kolay/Orta/Zor/Uzman)
2. Başlangıç board boyutu (X, Y, Z) ve toplam hedef hücre sayısı belirlenir
3. **Her katmana bir hedef renk atanır** (Piece Selector bunu bilerek çalışır)
4. Aday parçalar seçilir, katman-renk kısıtına uyacak şekilde backtracking ile yerleştirilir
5. Yerleşim izi ("Çözüm İzi") + katman-renk haritası kaydedilir
6. Parçalar karıştırılır (yön, sıra) — **renk bilgisi korunur**, sadece pozisyon/rotasyon karışır
7. Bağımsız `LevelSolver`, **collapse-aware** kurallarla (bkz. Bölüm 3) çözülebilirliği doğrular

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

## 6. Seviye Üretim Algoritması (güncellendi: renk ataması eklendi)

**Aşama 1 — Hedef Alan Maskesi, Profil ve Renk Ataması:**
Zorluk profiline göre başlangıç hacim ve grid boyutu belirlenir. Her Y katmanına bir hedef renk/materyal indeksi atanır (palet, `LevelManager.pieceMaterials` ile uyumlu olmalı).

**Aşama 2 — 3D Uyumlu Parça Seçimi:**
Toplam hacmi hedefe eşitleyecek aday parçalar seçilir; ayrıca her parçanın **hangi katmana, hangi renkle** gideceği önceden planlanır.

**Aşama 3 — Çözülmüş Şeklin Kurulması:**
Backtracking, "en az yerleşim seçeneği olan büyük/düzensiz parçalar önce" kuralıyla çalışır. Ek kısıt: bir parça yalnızca kendisine atanan katman + renk kombinasyonuna yerleştirilebilir. İzole boşluk oluşursa dal erken kesilir.

**Aşama 4 — Başlangıç Karıştırması:**
Parçalar hedeften çıkarılır; dönüş açıları ve sunum sırası karıştırılır. **Renk ataması korunur** (bir parçanın rengi karıştırma sırasında değişmez — sadece pozisyon/rotasyon/sıra karışır).

---

## 7. Çözülebilirlik ve Benzersiz Çözüm Kontrolü (değişmedi, netleştirildi)

- **Kolay:** çoklu çözüm kabul edilebilir
- **Orta / Zor:** en az 1 çözüm yeterli (performans için unique-solution zorunlu tutulmuyor)
- **Uzman:** yalnızca burada tek (unique) çözüm hedeflenir — çünkü tüm çözüm uzayını sayıp tekliği kanıtlamak pahalıdır ve mobil "milisaniyeler" hedefiyle çelişebilir

---

## 8. Deadlock / Game Over Tanımı (Yeni Bölüm — kod davranışına göre)

Gerçek `LevelManager.CheckGameOver()` → `IsShapePlaceable()`:

```csharp
// TÜM temizlenmemiş katmanlar taranır (sadece aktif/görünen katman değil):
foreach (var layerY in activeLayers) // activeLayers = tüm IsLayerCleared(y)==false katmanlar
    foreach (var rot in 6 rotasyon) // 4×Y-ekseni + 2×X-ekseni
        if (GetPossibleOffsetsOnLayer(rotated, layerY).Count > 0) return true; // yerleştirilebilir
```

Yani bir parça, **şu an panelde görünmeyen** bir üst katmana teorik olarak sığıyorsa oyun "kilitlenmiş" sayılmıyor. Solver ve Difficulty Evaluator'ın "çözümsüz/deadlock" tanımı bunu birebir yansıtmalı:

> **Deadlock koşulu:** Eldeki tüm parçaların, 6 rotasyonun hiçbiri, temizlenmemiş katmanların hiçbirine (sadece aktif katmana değil) yerleşemiyor olması.

Bu, v1'deki Bölüm 8.3 "Katman Blokaj Kontrolü" kuralını gevşetiyor — bir üst katmana geçici olarak erişilememesi tek başına deadlock sebebi değil, çünkü oyuncu başka bir katmanda ilerleyebilir.

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

JSON eğitim veri formatına katman-renk ataması eklenmeli:

```json
{
  "levelId": "L_BM3D_HARD_104",
  "difficultyLabel": "Zor",
  "initialBoardSize": {"x": 5, "y": 2, "z": 5},
  "layerColorMap": {"0": "matIdx_2", "1": "matIdx_0"},
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
- **Faz 3 — Çözümden Seviye Üretimi:** Piece Selector + katman-renk ataması + Solution Builder + Scrambler; merge/hint sistemi kararı netleşmeden başlanmamalı (Bölüm 9)
- **Faz 4 — Zorluk Derecelendirmesi:** collapse sonrası metriklerle formül hayata geçirilir
- **Faz 5 — Editör Arayüzü:** görsel önizleme, çözüm oynatıcı, metrik dışa aktarma
- **Faz 6 — Telemetri ve Kalibrasyon:** puanlama kopukluğu çözülene kadar Score yerine olay-bazlı metrikler kullanılır

---

## 13. Hatalı Yaklaşımlar ve Koruma Kuralları (v1'den korundu + 2 yeni kural)

| Hatalı Yaklaşım | Doğru Yaklaşım |
|---|---|
| Hedef şekil ve parçaları rastgele üretmek | Önce çözümü kur; hedef şekli yerleşen parçalardan türet |
| Board'u sabit boyutlu varsaymak | Collapse-aware simülasyon; `initialBoardSize` ≠ sabit sınır |
| Katman doluluğunu tek başına yeterli saymak | Doluluk + monokromluk birlikte şart (`IsLayerComplete`) |
| Zorluğu sadece parça sayısıyla ölçmek | Dallanma, katman geçişi, gecikmeli hata oranını ölç |
| Deadlock'u sadece aktif katmana göre değerlendirmek | Tüm temizlenmemiş katmanları tara (Bölüm 8) |
| Solver doğrulaması olmadan seviyeyi onaylamak | Her seviye için collapse-aware Solver çalıştır |
| Timeout/hata durumunda seviyeyi yayına almak | Timeout'u "Belirsiz" kabul et, reddet |

**Zorunlu Koruma Kuralları:**
1. Doğrulanmamış (`validated == false`) hiçbir seviye oyuna dahil edilemez
2. Kütüphanede mükerrer (aynı imza) parça tespit edilirse uyarı verilmeli
3. Zorluk profilleri güncellendiğinde tüm kayıtlı seviyeler yeniden puanlanmalı
4. **(Yeni)** Bir katmana birden fazla renk ataması yapan hiçbir üretim çıktısı kabul edilmemeli
5. **(Yeni)** Merge/hint sistemi (Bölüm 9) hakkında karar verilmeden Faz 3 tamamlanmış sayılmaz

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

- [ ] Parçaların toplam hacmi hedef şeklin hacmine eşit mi?
- [ ] Her katmana atanan renk, o katmandaki tüm hücrelerde tutarlı mı? (`IsLayerComplete` monokromluk şartı)
- [ ] Kayıtlı çözüm izi, collapse davranışı dahil gerçek oyun kurallarıyla tamamlanabiliyor mu?
- [ ] Bağımsız Solver en az bir geçerli çözüm buluyor mu?
- [ ] (Yalnızca Uzman) Alternatif ikinci bir çözümün olmadığı onaylandı mı?
- [ ] Deadlock kontrolü tüm temizlenmemiş katmanlar üzerinden mi yapıldı (sadece aktif katman değil)?
- [ ] Seviyenin zorluk skoru (0-100) hedef profil sınırları içinde mi?
- [ ] Seed değeri kaydedildi ve tekrar üretilebiliyor mu?
- [ ] Mobil performans sınırları dahilinde doğrulama yapıldı mı?
- [ ] `LevelData` "validated" olarak işaretlendi mi?

**Önemli İlke:** Önce çözümü kur, katman-renk atamasını yap, sonra oyuncunun başlangıç durumunu üret — ve board'un collapse ile küçüleceğini asla unutma.
