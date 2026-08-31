using UnityEngine;
using DG.Tweening;
using System;
using System.Collections.Generic;

/// <summary>
/// Bu component, buzlardan ve bloklardan yayılan patlama efektini yönetir.
/// Object pooling kullanarak performansı optimize eder.
/// </summary>
public class IceBreakEffect : MonoBehaviour
{
    private static IceBreakEffect _instance;
    public static IceBreakEffect Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("[IceBreakEffect_Manager]");
                _instance = go.AddComponent<IceBreakEffect>();
                _instance.InitializePools();
            }
            return _instance;
        }
    }

    [Header("Settings")]
    public int shardsPerBreak = 12;
    public float explosionForce = 4.2f;
    public float effectDuration = 0.45f;

    [Header("References (Opsiyonel, Inspector'dan eklenebilir)")]
    public GameObject shardPrefab;
    public ParticleSystem sparkleParticles;
    public AudioClip breakSound;
    public AudioSource audioSource;

    private Queue<GameObject> shardPool = new Queue<GameObject>();

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            InitializePools();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void InitializePools()
    {
        if (shardPrefab == null)
        {
            // Eğer prefab yoksa, basit bir primitif küp üretelim
            shardPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shardPrefab.name = "IceShard_Prefab";
            Destroy(shardPrefab.GetComponent<Collider>());
            shardPrefab.transform.SetParent(transform);
            shardPrefab.SetActive(false);
            
            // URP Lit shader ile parlak materyal oluştur
            Renderer r = shardPrefab.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader != null) r.material = new Material(shader);
        }

        // Havuzu önceden doldur (Pre-warm - 80 parçacık)
        for (int i = 0; i < 80; i++)
        {
            CreateNewShard();
        }
    }

    private GameObject CreateNewShard()
    {
        GameObject shard = Instantiate(shardPrefab, transform);
        shard.SetActive(false);
        shardPool.Enqueue(shard);
        return shard;
    }

    private GameObject GetShard()
    {
        if (shardPool.Count > 0)
        {
            GameObject s = shardPool.Dequeue();
            s.SetActive(true);
            return s;
        }
        GameObject newS = Instantiate(shardPrefab, transform);
        newS.SetActive(true);
        return newS;
    }

    private void ReturnShard(GameObject shard)
    {
        shard.transform.DOKill(); // Güvenlik amaçlı
        shard.SetActive(false);
        shardPool.Enqueue(shard);
    }

    /// <summary>
    /// Patlama efektini başlatır.
    /// targetBlock: Squash & Stretch uygulanacak ve gizlenecek asıl obje.
    /// color: Parçaların alacağı renk (Buz için açık mavi vs.)
    /// onComplete: Efekt bittiğinde çalışacak callback.
    /// </summary>
    public static void Play(GameObject targetBlock, Color color, Action onComplete = null, bool hideTarget = true)
    {
        Instance.PlayEffect(targetBlock, color, onComplete, hideTarget);
    }

    private void PlayEffect(GameObject targetBlock, Color color, Action onComplete, bool hideTarget)
    {
        if (targetBlock == null)
        {
            onComplete?.Invoke();
            return;
        }

        Vector3 center = targetBlock.transform.position;
        Renderer targetRend = targetBlock.GetComponentInChildren<Renderer>();

        // Bloğun kendi rengini materyalinden çek (eğer varsayılan/beyaz geldiyse)
        if (targetRend != null && targetRend.sharedMaterial != null)
        {
            Material m = targetRend.sharedMaterial;
            if (color == default || color == Color.cyan || color == Color.white)
            {
                if (m.HasProperty("_BaseColor")) color = m.GetColor("_BaseColor");
                else if (m.HasProperty("_Color")) color = m.GetColor("_Color");
            }
        }

        // Aynı obje için birden fazla çağrı gelmemesi adına DOTween'i temizle
        targetBlock.transform.DOKill();
        if (targetRend != null && targetRend.material != null) targetRend.material.DOKill();

        Sequence seq = DOTween.Sequence();

        // 1. Anticipation (Squash & Stretch)
        Vector3 origScale = targetBlock.transform.localScale;
        
        // Önce hafif küçül (squash), sonra şiş (stretch)
        seq.Append(targetBlock.transform.DOScale(origScale * 0.85f, 0.04f).SetEase(Ease.OutQuad));
        seq.Append(targetBlock.transform.DOScale(origScale * 1.25f, 0.06f).SetEase(Ease.OutQuad));
        
        // Emissive (Parlaklık) artışı
        if (targetRend != null && targetRend.material != null)
        {
            if (targetRend.material.HasProperty("_EmissionColor"))
            {
                targetRend.material.EnableKeyword("_EMISSION");
                seq.Join(targetRend.material.DOColor(Color.white * 2.5f, "_EmissionColor", 0.1f));
            }
        }

        // 2. Burst (Parçalanma & Partiküllere Ayrılma)
        seq.AppendCallback(() =>
        {
            if (hideTarget)
            {
                // Orijinal bloğu gizle
                if (targetRend != null) targetRend.enabled = false;
                
                // Eğer yok edilecek bir objeyse (örneğin patlayan bloklar), scale'i 0'da tutabiliriz
                targetBlock.transform.localScale = Vector3.zero;
            }
            else
            {
                // Gizleme yapma, animasyon sonrası scale'i normale döndür
                targetBlock.transform.localScale = origScale;
            }

            // Particle System
            if (sparkleParticles != null)
            {
                sparkleParticles.transform.position = center;
                var main = sparkleParticles.main;
                main.startColor = color;
                sparkleParticles.Play();
            }

            // Audio Playback
            if (audioSource != null && breakSound != null)
            {
                audioSource.PlayOneShot(breakSound);
            }

            // Kamera Titreşimi (Impact shake)
            if (CameraOrbit.Instance != null)
            {
                CameraOrbit.Instance.Shake(0.18f, 0.12f);
            }

            // Shard'ları (3D Voxel Kırıklarını) her yöne fırlat
            int shardCount = Mathf.Max(8, shardsPerBreak);
            for (int i = 0; i < shardCount; i++)
            {
                GameObject shard = GetShard();
                shard.transform.position = center + UnityEngine.Random.insideUnitSphere * 0.25f;
                
                // Rastgele 3D kristal/küp boyutu
                float sx = UnityEngine.Random.Range(0.12f, 0.32f);
                float sy = UnityEngine.Random.Range(0.12f, 0.32f);
                float sz = UnityEngine.Random.Range(0.12f, 0.32f);
                shard.transform.localScale = new Vector3(sx, sy, sz);

                // Renk ve parlaklık ata
                Renderer sr = shard.GetComponent<Renderer>();
                if (sr != null && sr.material != null)
                {
                    sr.material.color = color;
                    if (sr.material.HasProperty("_EmissionColor"))
                    {
                        sr.material.EnableKeyword("_EMISSION");
                        sr.material.SetColor("_EmissionColor", color * 0.8f);
                    }
                }

                // Radyal Yön (Yukarı doğru canlı fırlama açısı ile)
                Vector3 dir = UnityEngine.Random.insideUnitSphere.normalized;
                dir.y = Mathf.Abs(dir.y) * 1.35f + 0.35f; // Canlı yukarı fırlama
                
                Vector3 targetPos = shard.transform.position + dir * explosionForce * UnityEngine.Random.Range(0.75f, 1.45f);
                Vector3 randomRot = new Vector3(
                    UnityEngine.Random.Range(-360f, 360f),
                    UnityEngine.Random.Range(-360f, 360f),
                    UnityEngine.Random.Range(-360f, 360f)
                );

                shard.transform.DOMove(targetPos, effectDuration).SetEase(Ease.OutQuad);
                shard.transform.DORotate(randomRot, effectDuration, RotateMode.FastBeyond360).SetEase(Ease.Linear);
                shard.transform.DOScale(Vector3.zero, effectDuration * 0.65f).SetDelay(effectDuration * 0.35f).SetEase(Ease.InQuad).OnComplete(() =>
                {
                    ReturnShard(shard);
                });
            }
        });

        // 3. Cleanup / Callback
        seq.AppendInterval(effectDuration);

        // Parlaklık patlaması hiç geri söndürülmüyordu; hideTarget:true olan çağrılarda obje
        // hemen gizlendiği için fark edilmiyordu ama hideTarget:false ile (obje görünür kalınca)
        // materyal efekt boyunca beyaz/aşırı parlak takılı kalıyordu. Burst'ten hemen sonraki
        // 0.18s'lik dilime Insert ederek toplam süreyi uzatmadan normale söndürüyoruz.
        if (targetRend != null && targetRend.material != null && targetRend.material.HasProperty("_EmissionColor"))
        {
            seq.Insert(0.10f, targetRend.material.DOColor(Color.clear, "_EmissionColor", 0.18f).SetEase(Ease.OutQuad));
        }

        seq.OnComplete(() =>
        {
            onComplete?.Invoke();
            // Not: Hedef obje dışarıdan yönetiliyor (örn. GridManager tarafından Destroy edilecek).
            // Eğer sadece görsel kapatma lazımsa, görünmez yapıldı (enabled = false).
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BUZ ERİME EFEKTİ: Isı transferi metaforu — sıcak blok soğuk buza değer,
    // buz zamanla (ani kırılma değil) organik biçimde erir. Shader/ses/doku
    // asseti gerektirmeyen, tamamen DOTween + MaterialPropertyBlock tabanlı
    // bir yaklaşım (gerçek dissolve shader'ın kod-only yaklaşımı).
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Color ContactFlashColor = new Color(1f, 0.98f, 0.863f);   // sıcak-soğuk temas anı
    private static readonly Color EdgeGlowColor      = new Color(0.863f, 0.98f, 1f);   // "erime hattı" parıltısı
    private static readonly Color WaterDropColor     = new Color(0.678f, 0.847f, 0.902f);
    private static readonly Color SparkleColor       = Color.white;

    /// <summary>
    /// Temas anında (parça buza değdiği ilk kare) çağrılır: kısa bir buhar
    /// patlaması + (varsa) hafif mobil titreşim. Ateşle-unut niteliğindedir,
    /// asıl erime sekansını (PlayIceMelt) engellemez/beklemez.
    /// </summary>
    public static void PlayContactHeatFlash(GameObject targetBlock)
    {
        if (targetBlock == null) return;
        Instance.PlayContactHeatFlashEffect(targetBlock);
    }

    private void PlayContactHeatFlashEffect(GameObject targetBlock)
    {
        Vector3 center = targetBlock.transform.position;
        Renderer rend = targetBlock.GetComponentInChildren<Renderer>();

        if (rend != null && rend.material != null && rend.material.HasProperty("_EmissionColor"))
        {
            rend.material.EnableKeyword("_EMISSION");
            rend.material.DOKill();
            rend.material.DOColor(ContactFlashColor * 1.5f, "_EmissionColor", 0.08f).SetLoops(2, LoopType.Yoyo);
        }

        SpawnBurstParticles(center, 4, ContactFlashColor, radiating: true);

#if UNITY_ANDROID || UNITY_IOS
        if (GameManager.Instance == null || GameManager.Instance.IsVibrationEnabled)
        {
            Handheld.Vibrate();
        }
#endif
    }

    /// <summary>
    /// Buzun kırılma yerine erime animasyonu: alpha "dissolve" illüzyonu
    /// (yavaş başlayıp hızlanan ease-in eğrisi), kenarda kısa bir "erime hattı"
    /// parıltısı, düşen su damlaları, sonda hafif bir mesh çöküşü ve kapanışta
    /// bir final parıltısı. targetBlock'un gerçek grid durumu (occupiedCells)
    /// bu görsel sekanstan önce, çağıran taraf (GridManager) tarafından zaten
    /// güncellenmiş olmalı — burada sadece görsel oynatılır.
    /// </summary>
    public static void PlayIceMelt(GameObject targetBlock, Action onComplete = null)
    {
        Instance.PlayIceMeltEffect(targetBlock, onComplete);
    }

    public void StopAllEffects()
    {
        DOTween.Kill("IceMelt");
    }

    private void PlayIceMeltEffect(GameObject targetBlock, Action onComplete)
    {
        if (targetBlock == null)
        {
            onComplete?.Invoke();
            return;
        }

        Vector3 center = targetBlock.transform.position;
        Vector3 origScale = targetBlock.transform.localScale;
        Renderer targetRend = targetBlock.GetComponentInChildren<Renderer>();

        targetBlock.transform.DOKill();
        if (targetRend != null && targetRend.sharedMaterial != null) targetRend.sharedMaterial.DOKill();

        const float meltDuration = 0.55f;
        bool hasEmission = targetRend != null && targetRend.sharedMaterial != null && targetRend.sharedMaterial.HasProperty("_EmissionColor");
        string colorProp = targetRend != null && targetRend.sharedMaterial != null
            ? (targetRend.sharedMaterial.HasProperty("_BaseColor") ? "_BaseColor"
                : targetRend.sharedMaterial.HasProperty("_Color") ? "_Color" : null)
            : null;
        Color origColor = colorProp != null ? targetRend.sharedMaterial.GetColor(colorProp) : Color.white;

        Sequence seq = DOTween.Sequence();
        seq.SetId("IceMelt");

        // 0. Temas anı: erime başlar başlamaz temas noktasında kısa bir buhar patlaması
        seq.InsertCallback(0f, () => SpawnBurstParticles(center, 4, ContactFlashColor, radiating: true));

        // 1. Ana erime ve 2. Erime hattı parıltısı: MaterialPropertyBlock ile animasyon
        if (colorProp != null)
        {
            MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
            targetRend.GetPropertyBlock(propBlock);

            DOVirtual.Float(0f, 1f, meltDuration, t =>
            {
                if (targetRend == null) return;
                
                // Alpha fade
                float alpha = Mathf.Lerp(1f, 0f, EaseOutQuad(t)); // Ease InQuad için 1'den 0'a
                Color c = origColor;
                c.a = origColor.a * alpha;
                propBlock.SetColor(colorProp, c);

                // Emission glow
                if (hasEmission)
                {
                    if (t > 0.45f && t < 0.85f)
                    {
                        float emT = (t - 0.45f) / 0.4f; // 0 to 1
                        float yoyo = Mathf.Sin(emT * Mathf.PI * 2f); // 2 loops
                        Color emC = Color.Lerp(Color.clear, EdgeGlowColor * 1.4f, yoyo);
                        propBlock.SetColor("_EmissionColor", emC);
                    }
                    else if (t >= 0.85f)
                    {
                        propBlock.SetColor("_EmissionColor", Color.clear);
                    }
                }

                targetRend.SetPropertyBlock(propBlock);
            }).SetEase(Ease.Linear).SetId("IceMelt");
        }
        else
        {
            seq.AppendInterval(meltDuration);
        }

        // 3. Su damlaları: erime sırasında iki dalga halinde düşen damlalar
        seq.InsertCallback(meltDuration * 0.15f, () => SpawnWaterDroplets(center, origScale, 3));
        seq.InsertCallback(meltDuration * 0.55f, () => SpawnWaterDroplets(center, origScale, 3));

        // 4. Mesh çöküşü: sürecin son %30'unda hafifçe çöker (tamamen kozmetik,
        //    grid mantığını etkilemez)
        seq.Insert(meltDuration * 0.7f, targetBlock.transform.DOScale(
            new Vector3(origScale.x, origScale.y * 0.85f, origScale.z), meltDuration * 0.3f).SetEase(Ease.OutQuad));

        // 5. Final parıltı: erime bitince kısa, parlak bir "chime" ışığı
        seq.InsertCallback(meltDuration, () => SpawnFinalSparkle(center));

        seq.OnComplete(() =>
        {
            // Görünüm dışarıdan (GridManager) sıfırlanacak; burada sadece transformu toparlıyoruz.
            targetBlock.transform.localScale = origScale;
            onComplete?.Invoke();
        });
    }

    private float EaseOutQuad(float x)
    {
        return 1f - (1f - x) * (1f - x);
    }

    /// <summary>
    /// Buz birden fazla vuruşa dayanıyorsa (bkz. IceVisualMarker.totalHits), her nitelikli
    /// temasta TAM erime yerine bu vuruş efekti oynar: buz KENDİ BOYUTUNDA KALIR (küçülmez),
    /// sadece kısa bir sıkışma/geri sekme (hit feedback), bir buhar/parıltı patlaması ve
    /// birkaç su damlası oynar; kalan vuruş sayısı üzerindeki etikette (IceVisualMarker)
    /// güncellenir. Buzun tamamen erimesi yalnızca kalan vuruş 0'a indiğinde PlayIceMelt
    /// ile gerçekleşir (bkz. GridManager.AnimateThawAndDestroy).
    /// </summary>
    public static void PlayIceChip(GameObject targetBlock, int remainingHits, int totalHits, Action onComplete = null)
    {
        Instance.PlayIceChipEffect(targetBlock, remainingHits, totalHits, onComplete);
    }

    private void PlayIceChipEffect(GameObject targetBlock, int remainingHits, int totalHits, Action onComplete)
    {
        if (targetBlock == null)
        {
            onComplete?.Invoke();
            return;
        }

        Vector3 center = targetBlock.transform.position;
        Renderer targetRend = targetBlock.GetComponentInChildren<Renderer>();
        var marker = targetBlock.GetComponent<IceVisualMarker>();
        Vector3 baseScale = marker != null ? marker.baseScale : targetBlock.transform.localScale;

        targetBlock.transform.DOKill();
        if (targetRend != null && targetRend.sharedMaterial != null) targetRend.sharedMaterial.DOKill();

        const float chipDuration = 0.3f;

        Sequence seq = DOTween.Sequence();
        // Kısa bir sıkışma ardından TAM orijinal boyuta geri sekme — buz kalıcı olarak
        // küçülmüyor, sadece bir vuruş aldığını hissettiriyor. Boyut yerine, kaç vuruş
        // kaldığını gösteren sayaç (IceVisualMarker.UpdateCount) azalıyor.
        seq.Append(targetBlock.transform.DOScale(baseScale * 0.9f, 0.05f).SetEase(Ease.OutQuad));
        seq.AppendCallback(() => SpawnBurstParticles(center, 3, EdgeGlowColor, radiating: true));
        seq.Append(targetBlock.transform.DOScale(baseScale, chipDuration * 0.75f).SetEase(Ease.OutBack));
        seq.InsertCallback(chipDuration * 0.25f, () => SpawnWaterDroplets(center, baseScale, 2));

        // Kısa bir kenar-parıltısı yanıp söner (MaterialPropertyBlock ile — PlayIceMeltEffect'teki
        // gibi, gereksiz materyal instance'ı oluşturmamak için).
        if (targetRend != null && targetRend.sharedMaterial != null && targetRend.sharedMaterial.HasProperty("_EmissionColor"))
        {
            MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
            targetRend.GetPropertyBlock(propBlock);
            DOVirtual.Float(0f, 1f, 0.16f, t =>
            {
                if (targetRend == null) return;
                float yoyo = Mathf.Sin(t * Mathf.PI);
                propBlock.SetColor("_EmissionColor", Color.Lerp(Color.clear, EdgeGlowColor * 1.4f, yoyo));
                targetRend.SetPropertyBlock(propBlock);
            }).SetEase(Ease.Linear);
        }

        seq.OnComplete(() => onComplete?.Invoke());
    }

    /// <summary>
    /// Düşen (yerçekimi hissi veren) su damlaları — buz kırıklarının aksine
    /// dışa doğru patlamaz, aşağı süzülüp hafif sönerek kaybolur.
    /// </summary>
    private void SpawnWaterDroplets(Vector3 center, Vector3 iceScale, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject drop = GetShard();
            Vector3 edgeOffset = new Vector3(
                UnityEngine.Random.Range(-0.5f, 0.5f) * iceScale.x,
                -iceScale.y * 0.4f,
                UnityEngine.Random.Range(-0.5f, 0.5f) * iceScale.z);
            drop.transform.position = center + edgeOffset;

            float s = UnityEngine.Random.Range(0.05f, 0.1f);
            drop.transform.localScale = Vector3.one * s;

            Renderer dr = drop.GetComponent<Renderer>();
            if (dr != null && dr.material != null)
            {
                dr.material.color = WaterDropColor;
                if (dr.material.HasProperty("_EmissionColor"))
                {
                    dr.material.EnableKeyword("_EMISSION");
                    dr.material.SetColor("_EmissionColor", WaterDropColor * 0.3f);
                }
            }

            float dur = UnityEngine.Random.Range(0.3f, 0.45f);
            Vector3 fallTarget = drop.transform.position + Vector3.down * UnityEngine.Random.Range(0.4f, 0.7f);

            drop.transform.DOMove(fallTarget, dur).SetEase(Ease.InQuad);
            drop.transform.DOScale(Vector3.zero, dur * 0.5f).SetDelay(dur * 0.5f).SetEase(Ease.InQuad).OnComplete(() =>
            {
                ReturnShard(drop);
            });
        }
    }

    /// <summary>
    /// Temas anındaki buhar patlaması ve benzeri kısa, dışa doğru genişleyen
    /// parçacık patlamaları için ortak yardımcı.
    /// </summary>
    private void SpawnBurstParticles(Vector3 center, int count, Color color, bool radiating)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject p = GetShard();
            p.transform.position = center + UnityEngine.Random.insideUnitSphere * 0.1f;

            float s = UnityEngine.Random.Range(0.05f, 0.12f);
            p.transform.localScale = Vector3.one * s;

            Renderer pr = p.GetComponent<Renderer>();
            if (pr != null && pr.material != null)
            {
                pr.material.color = color;
                if (pr.material.HasProperty("_EmissionColor"))
                {
                    pr.material.EnableKeyword("_EMISSION");
                    pr.material.SetColor("_EmissionColor", color * 1.5f);
                }
            }

            Vector3 dir = radiating ? UnityEngine.Random.insideUnitSphere.normalized : Vector3.up;
            float dur = UnityEngine.Random.Range(0.2f, 0.3f);
            Vector3 targetPos = p.transform.position + dir * UnityEngine.Random.Range(0.3f, 0.5f);

            p.transform.DOMove(targetPos, dur).SetEase(Ease.OutQuad);
            p.transform.DOScale(Vector3.zero, dur).SetEase(Ease.InQuad).OnComplete(() =>
            {
                ReturnShard(p);
            });
        }
    }

    /// <summary>
    /// Erimenin bittiğini işaret eden kısa, parlak final parıltısı.
    /// </summary>
    private void SpawnFinalSparkle(Vector3 center)
    {
        GameObject sparkle = GetShard();
        sparkle.transform.position = center;
        sparkle.transform.localScale = Vector3.zero;

        Renderer sr = sparkle.GetComponent<Renderer>();
        if (sr != null && sr.material != null)
        {
            sr.material.color = SparkleColor;
            if (sr.material.HasProperty("_EmissionColor"))
            {
                sr.material.EnableKeyword("_EMISSION");
                sr.material.SetColor("_EmissionColor", SparkleColor * 2.5f);
            }
        }

        const float dur = 0.15f;
        Sequence seq = DOTween.Sequence();
        seq.Append(sparkle.transform.DOScale(Vector3.one * 0.4f, dur * 0.4f).SetEase(Ease.OutQuad));
        seq.Append(sparkle.transform.DOScale(Vector3.zero, dur * 0.6f).SetEase(Ease.InQuad));
        seq.OnComplete(() => ReturnShard(sparkle));
    }
}
