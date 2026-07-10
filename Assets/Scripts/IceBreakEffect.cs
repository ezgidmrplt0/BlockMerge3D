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
    public int shardsPerBreak = 6;
    public float explosionForce = 3.5f;
    public float effectDuration = 0.35f;

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
            
            // Eğer URP veya standart material vermek isterseniz:
            Renderer r = shardPrefab.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader != null) r.material = new Material(shader);
        }

        // Havuzu önceden doldur (Pre-warm)
        for (int i = 0; i < 40; i++)
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

        // 2. Burst (Patlama)
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

            // Kamera Titreşimi (GridManager.cs içindeki CameraOrbit entegrasyonu)
            if (CameraOrbit.Instance != null)
            {
                CameraOrbit.Instance.Shake(0.15f, 0.1f);
            }

            // Shard'ları (Kırıkları) fırlat
            for (int i = 0; i < shardsPerBreak; i++)
            {
                GameObject shard = GetShard();
                shard.transform.position = center + UnityEngine.Random.insideUnitSphere * 0.2f;
                
                // Rastgele boyut
                float s = UnityEngine.Random.Range(0.15f, 0.35f);
                shard.transform.localScale = Vector3.one * s;

                // Renk ata
                Renderer sr = shard.GetComponent<Renderer>();
                if (sr != null && sr.material != null)
                {
                    sr.material.color = color;
                    if (sr.material.HasProperty("_EmissionColor"))
                    {
                        sr.material.EnableKeyword("_EMISSION");
                        sr.material.SetColor("_EmissionColor", color * 0.5f);
                    }
                }

                // Rastgele Yön ve DOTween Animasyonu
                Vector3 dir = UnityEngine.Random.insideUnitSphere.normalized;
                dir.y = Mathf.Abs(dir.y) + 0.6f; // Yukarı doğru fırlama eğilimi
                
                Vector3 targetPos = shard.transform.position + dir * explosionForce * UnityEngine.Random.Range(0.7f, 1.3f);
                Vector3 randomRot = new Vector3(UnityEngine.Random.Range(-360, 360), UnityEngine.Random.Range(-360, 360), UnityEngine.Random.Range(-360, 360));

                shard.transform.DOMove(targetPos, effectDuration).SetEase(Ease.OutCubic);
                shard.transform.DORotate(randomRot, effectDuration, RotateMode.FastBeyond360).SetEase(Ease.Linear);
                shard.transform.DOScale(Vector3.zero, effectDuration * 0.8f).SetDelay(effectDuration * 0.2f).SetEase(Ease.InQuad).OnComplete(() =>
                {
                    ReturnShard(shard);
                });
            }
        });

        // 3. Cleanup / Callback
        seq.AppendInterval(effectDuration);
        seq.OnComplete(() =>
        {
            onComplete?.Invoke();
            // Not: Hedef obje dışarıdan yönetiliyor (örn. GridManager tarafından Destroy edilecek).
            // Eğer sadece görsel kapatma lazımsa, görünmez yapıldı (enabled = false).
        });
    }
}
