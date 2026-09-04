using UnityEngine;
using System;
using System.Collections.Generic;

#if USE_FIREBASE
using Firebase.Firestore;
using Firebase.Extensions;

/// <summary>
/// Firestore tabanlı derin kullanıcı ve oynanış analitik sistemi.
/// Her kullanıcı için level bazlı tam geçmiş, hamle sayısı, katman bazlı churn (pes etme)
/// noktaları ve booster kullanımını kaydeder.
///
/// Veri yapısı:
///   users/{deviceId}                            → dashboard'un listelediği özet (root)
///   users/{deviceId}/meta/profile               → aynı özetin detaylı kopyası
///   users/{deviceId}/sessions/{sessionId}       → oturum kayıtları
///   users/{deviceId}/level_history/{levelIndex} → level bazlı detay
///
/// Yazma modeli: her yazma <see cref="Merge"/> üzerinden SetAsync(MergeAll) ile yapılır.
/// Böylece hedef doküman henüz oluşmamışsa bile yazma kaybolmaz — UpdateAsync bu durumda
/// NOT_FOUND fırlatıp sessizce veri kaybettiriyordu. Her yazmanın sonucu da loglanır.
///
/// Okuma YAPILMAZ: "en uzak level", "en iyi süre", "ilk deneme", "kaçıncı deneme" gibi
/// karşılaştırma gerektiren değerler PlayerPrefs'te yerel tutulur ve yalnızca değiştiğinde
/// yazılır. Eskiden level başına 2 okuma + 2 yazma vardı; her okuma telefonda ağ
/// gidiş-dönüşü ve Firestore kotası demekti.
/// </summary>
public class FirestoreAnalytics : MonoBehaviour
{
    public static FirestoreAnalytics Instance;

    // ── Sabitler ─────────────────────────────────────────────────
    private const string COLLECTION_USERS    = "users";
    private const string COLLECTION_SESSIONS = "sessions";
    private const string COLLECTION_LEVELS   = "level_history";
    private const string DOC_PROFILE         = "profile";

    // Yerel (okuma gerektirmeyen) karşılaştırma değerleri
    private const string PREF_INSTALL_DATE = "fs_install_date";
    private const string PREF_FARTHEST     = "fs_farthest_level";
    private const string PREF_LEVEL_SEEN   = "fs_level_seen_";
    private const string PREF_BEST_TIME    = "fs_best_time_";
    private const string PREF_ATTEMPTS     = "fs_attempts_";

    /// <summary>Tek bir olaya yazılabilecek en uzun süre. Time/DateTime uygulama arka
    /// plandayken de ilerlediği için kırpılmazsa "bu levelde 3 saat geçirdi" gibi
    /// kayıtlar oluşuyor.</summary>
    private const double MAX_EVENT_SECONDS = 3600;

    /// <summary>Bu süreden kısa arka plan kesintisi YENİ oturum sayılmaz — bildirim
    /// çubuğunu açıp kapatmak oturum sayısını şişirmesin.</summary>
    private const double NEW_SESSION_AFTER_SECONDS = 30;

    // ── Özel Durum ────────────────────────────────────────────────
    private FirebaseFirestore db;
    private bool isReady = false;

    private string userId;
    private string sessionId;
    private DateTime sessionStartTime;
    private int levelsPlayedThisSession = 0;

    // Aktif level & sayaç takibi
    private int currentLevelIndex = -1;
    private DateTime levelStartTime;
    private bool levelActive = false;

    // Arka plan takibi
    private DateTime pausedAt;
    private bool levelActiveBeforePause = false;

    /// <summary>Aktif level denemesinde yapılan geçerli parça yerleştirme hamle sayısı.</summary>
    public int CurrentLevelMoves { get; private set; } = 0;

    /// <summary>Aktif denemede tahtanın kaç kez döndürüldüğü.</summary>
    public int CurrentLevelRotations { get; private set; } = 0;

    /// <summary>Aktif denemede patlatılan satır/sütun sayısı.</summary>
    public int CurrentLevelMatches { get; private set; } = 0;

    /// <summary>Aktif levelin bu cihazdaki kaçıncı denemesi (1'den başlar).</summary>
    public int CurrentAttemptNumber { get; private set; } = 1;

    // ─────────────────────────────────────────────────────────────
    // BAŞLANGIÇ
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// FirebaseManager başarıyla init ettikten sonra bu metodu çağırır.
    /// </summary>
    public void Initialize()
    {
        db = FirebaseFirestore.DefaultInstance;
        userId = SystemInfo.deviceUniqueIdentifier;
        sessionId = NewSessionId();
        sessionStartTime = DateTime.UtcNow;
        isReady = true;

        EnsureUserProfile();
        StartSession();

        Debug.Log("[Firestore] Başlatıldı. UserID: " + userId + " | Session: " + sessionId);
    }

    private static string NewSessionId() =>
        DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + UnityEngine.Random.Range(1000, 9999);

    // ─────────────────────────────────────────────────────────────
    // OYUNDAN GELEN SAYAÇLAR
    // SDK hazır olmasa da çalışırlar — yerel durum init'ten bağımsızdır.
    // ─────────────────────────────────────────────────────────────

    /// <summary>Oyuncu tahtaya her parça yerleştirdiğinde çağrılır.</summary>
    public void IncrementMoveCount() => CurrentLevelMoves++;

    /// <summary>Tahta her döndürüldüğünde (swipe veya A/D) çağrılır.</summary>
    public void IncrementRotationCount() => CurrentLevelRotations++;

    /// <summary>Satır/sütun patladığında, patlayan çizgi sayısı kadar çağrılır.</summary>
    public void IncrementMatchCount(int count = 1)
    {
        if (count > 0) CurrentLevelMatches += count;
    }

    private void ResetAttemptCounters()
    {
        CurrentLevelMoves     = 0;
        CurrentLevelRotations = 0;
        CurrentLevelMatches   = 0;
    }

    // ─────────────────────────────────────────────────────────────
    // YAZMA YARDIMCILARI
    // ─────────────────────────────────────────────────────────────

    private DocumentReference RootRef =>
        db.Collection(COLLECTION_USERS).Document(userId);

    private DocumentReference ProfileRef =>
        RootRef.Collection("meta").Document(DOC_PROFILE);

    private DocumentReference GetLevelRef(int levelIndex) =>
        RootRef.Collection(COLLECTION_LEVELS).Document(levelIndex.ToString());

    /// <summary>
    /// Doküman yoksa oluşturur, varsa alanları birleştirir. Hata olursa konsola yazar —
    /// eskiden task sonucu atıldığı için başarısız yazmalar hiçbir yerde görünmüyordu.
    /// </summary>
    private void Merge(DocumentReference doc, Dictionary<string, object> data, string tag)
    {
        if (!isReady || db == null) return;

        doc.SetAsync(data, SetOptions.MergeAll).ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogWarning("[Firestore] '" + tag + "' yazılamadı: " +
                                 task.Exception?.GetBaseException().Message);
            }
        });
    }

    /// <summary>Aynı özeti hem root dokümana (dashboard listesi) hem meta/profile'a yazar.</summary>
    private void MergeProfileAndRoot(Dictionary<string, object> updates, string tag)
    {
        if (!isReady || db == null) return;

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        string todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var profileData = new Dictionary<string, object>(updates);
        profileData["last_seen"] = now;
        profileData["last_active_date"] = todayStr;
        Merge(ProfileRef, profileData, tag + " → profile");

        var rootData = new Dictionary<string, object>(updates);
        rootData["device_id"] = userId;
        rootData["last_seen"] = now;
        rootData["last_active_date"] = todayStr;
        Merge(RootRef, rootData, tag + " → root");
    }

    /// <summary>Anormal süreleri kırpar (arka planda geçen zaman, saat değişimi vb.).</summary>
    private static long SaneSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || seconds <= 0) return 0;
        return (long)Math.Min(seconds, MAX_EVENT_SECONDS);
    }

    // ─────────────────────────────────────────────────────────────
    // KULLANICI PROFİLİ
    // ─────────────────────────────────────────────────────────────

    private void EnsureUserProfile()
    {
        if (!isReady) return;

        string todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Kurulum tarihi yerel tutulur — Firestore okuması gerekmez.
        string installDate = PlayerPrefs.GetString(PREF_INSTALL_DATE, "");
        bool isNewUser = string.IsNullOrEmpty(installDate);
        if (isNewUser)
        {
            installDate = todayStr;
            PlayerPrefs.SetString(PREF_INSTALL_DATE, installDate);
            PlayerPrefs.Save();
        }

        int retentionDay = 0;
        if (DateTime.TryParse(installDate, out DateTime insDate))
            retentionDay = Mathf.Max(0, (DateTime.UtcNow.Date - insDate.Date).Days);

        var summary = new Dictionary<string, object>
        {
            { "install_date",   installDate },
            { "retention_day",  retentionDay },
            { "platform",       Application.platform.ToString() },
            { "app_version",    Application.version },
            { "device_model",   SystemInfo.deviceModel },
            { "farthest_level", PlayerPrefs.GetInt(PREF_FARTHEST, 0) }
        };

        if (isNewUser)
            summary["first_open"] = Timestamp.FromDateTime(DateTime.UtcNow);

        MergeProfileAndRoot(summary, "profile");
    }

    // ─────────────────────────────────────────────────────────────
    // OTURUM YÖNETİMİ
    // ─────────────────────────────────────────────────────────────

    private void StartSession()
    {
        if (!isReady) return;

        Merge(RootRef.Collection(COLLECTION_SESSIONS).Document(sessionId), new Dictionary<string, object>
        {
            { "start_time",       Timestamp.FromDateTime(sessionStartTime) },
            { "duration_seconds", 0 },
            { "levels_played",    0 },
            { "device_model",     SystemInfo.deviceModel },
            { "os_version",       SystemInfo.operatingSystem }
        }, "session_start");

        MergeProfileAndRoot(new Dictionary<string, object>
        {
            { "total_sessions", FieldValue.Increment(1) }
        }, "session_count");
    }

    private void EndSession(bool fromPause = false)
    {
        if (!isReady) return;

        // Açık bir level varsa önce onu kaydet
        if (levelActive)
        {
            float timeOnLevel = (float)(DateTime.UtcNow - levelStartTime).TotalSeconds;
            LogLevelQuit(currentLevelIndex, timeOnLevel);
        }

        long duration = SaneSeconds((DateTime.UtcNow - sessionStartTime).TotalSeconds);

        Merge(RootRef.Collection(COLLECTION_SESSIONS).Document(sessionId), new Dictionary<string, object>
        {
            { "end_time",         Timestamp.FromDateTime(DateTime.UtcNow) },
            { "duration_seconds", duration },
            { "levels_played",    levelsPlayedThisSession },
            { "ended_by_pause",   fromPause }
        }, "session_end");
    }

    private void AddPlayTime(double seconds)
    {
        if (!isReady || seconds <= 0) return;

        double minutes = SaneSeconds(seconds) / 60.0;
        if (minutes <= 0) return;

        MergeProfileAndRoot(new Dictionary<string, object>
        {
            { "total_play_minutes", FieldValue.Increment(minutes) }
        }, "play_time");
    }

    // ─────────────────────────────────────────────────────────────
    // LEVEL OLAYLARI
    // ─────────────────────────────────────────────────────────────

    public void LogLevelStart(int levelIndex, LevelContext ctx = default)
    {
        // Yerel durum SDK'dan bağımsız — init sırasında başlayan level "aktif değil" sanılmasın.
        currentLevelIndex = levelIndex;
        levelStartTime    = DateTime.UtcNow;
        levelActive       = true;
        ResetAttemptCounters();
        levelsPlayedThisSession++;

        // Kaçıncı deneme — yerel sayaç, okuma gerektirmez.
        string attemptKey = PREF_ATTEMPTS + levelIndex;
        CurrentAttemptNumber = PlayerPrefs.GetInt(attemptKey, 0) + 1;
        PlayerPrefs.SetInt(attemptKey, CurrentAttemptNumber);

        if (!isReady) return;

        var levelData = new Dictionary<string, object>
        {
            { "level_index",         levelIndex },
            { "level_type",          ctx.levelType },
            { "attempts",            FieldValue.Increment(1) },
            { "last_attempt_number", CurrentAttemptNumber },
            { "tutorial_shown",      ctx.tutorialShown },
            { "last_played",         Timestamp.FromDateTime(DateTime.UtcNow) }
        };

        // first_attempt yalnızca bu cihazda level ilk kez açıldığında yazılır.
        string seenKey = PREF_LEVEL_SEEN + levelIndex;
        if (PlayerPrefs.GetInt(seenKey, 0) == 0)
        {
            levelData["first_attempt"] = Timestamp.FromDateTime(DateTime.UtcNow);
            PlayerPrefs.SetInt(seenKey, 1);
        }
        PlayerPrefs.Save();

        Merge(GetLevelRef(levelIndex), levelData, "level_start");

        // En uzak level — Firestore okumadan, yerel maksimumla karşılaştırılır.
        if (levelIndex > PlayerPrefs.GetInt(PREF_FARTHEST, -1))
        {
            PlayerPrefs.SetInt(PREF_FARTHEST, levelIndex);
            PlayerPrefs.Save();

            MergeProfileAndRoot(new Dictionary<string, object>
            {
                { "farthest_level", levelIndex }
            }, "farthest_level");
        }
    }

    public void LogLevelComplete(int levelIndex, float durationSeconds, LevelContext ctx = default)
    {
        levelActive = false;
        long safeDuration = SaneSeconds(durationSeconds);

        if (!isReady) return;

        var updates = new Dictionary<string, object>
        {
            { "level_index",      levelIndex },
            { "completions",      FieldValue.Increment(1) },
            { "total_time_spent", FieldValue.Increment(safeDuration) },
            { "total_moves",      FieldValue.Increment(ctx.movesMade) },
            { "total_rotations",  FieldValue.Increment(ctx.rotationsUsed) },
            { "last_solve_moves", ctx.movesMade },
            { "last_played",      Timestamp.FromDateTime(DateTime.UtcNow) }
        };

        // En iyi süre de yerel takip edilir — okuma gerekmez.
        string bestKey = PREF_BEST_TIME + levelIndex;
        int previousBest = PlayerPrefs.GetInt(bestKey, 0);
        if (safeDuration > 0 && (previousBest == 0 || safeDuration < previousBest))
        {
            PlayerPrefs.SetInt(bestKey, (int)safeDuration);
            PlayerPrefs.Save();
            updates["best_time"] = safeDuration;
        }

        Merge(GetLevelRef(levelIndex), updates, "level_complete");

        MergeProfileAndRoot(new Dictionary<string, object>
        {
            { "total_completions", FieldValue.Increment(1) },
            { "last_level_completed", levelIndex }
        }, "completion_count");

        AddPlayTime(safeDuration);
    }

    public void LogLevelFail(int levelIndex, float durationSeconds, LevelContext ctx = default,
                             int failedLayerY = -1)
    {
        levelActive = false;
        long safeDuration = SaneSeconds(durationSeconds);

        if (!isReady) return;

        if (failedLayerY < 0 && GridManager.Instance != null) failedLayerY = GridManager.Instance.ActiveLayerY;
        if (failedLayerY < 0) failedLayerY = 0;

        Merge(GetLevelRef(levelIndex), new Dictionary<string, object>
        {
            { "level_index",                        levelIndex },
            { "fails",                              FieldValue.Increment(1) },
            { "total_time_spent",                   FieldValue.Increment(safeDuration) },
            { "total_moves",                        FieldValue.Increment(ctx.movesMade) },
            { "total_rotations",                    FieldValue.Increment(ctx.rotationsUsed) },
            { "last_fail_moves",                    ctx.movesMade },
            { "last_fail_matches",                  ctx.matchesMade },
            { "last_fail_pieces",                   ctx.piecesRemaining },
            { "last_fail_layer",                    failedLayerY },
            // Sıfır hamleli fail = tahta anlaşılmadı; yüksek hamleli fail = anlaşıldı ama çözülemedi.
            { "zero_move_fails",                    FieldValue.Increment(ctx.movesMade == 0 ? 1 : 0) },
            { $"churn_layers.layer_{failedLayerY}", FieldValue.Increment(1) },
            { "last_played",                        Timestamp.FromDateTime(DateTime.UtcNow) }
        }, "level_fail");

        MergeProfileAndRoot(new Dictionary<string, object>
        {
            { "total_fails",      FieldValue.Increment(1) },
            { "last_churn_level", levelIndex },
            { "last_churn_layer", failedLayerY },
            { "last_fail_moves",  ctx.movesMade }
        }, "fail_count");

        AddPlayTime(safeDuration);
    }

    public void LogLevelRetry(int levelIndex, LevelContext ctx = default)
    {
        double elapsedSeconds = levelActive ? (DateTime.UtcNow - levelStartTime).TotalSeconds : 0;
        long safeElapsed = SaneSeconds(elapsedSeconds);

        if (isReady)
        {
            var updates = new Dictionary<string, object>
            {
                { "level_index",        levelIndex },
                { "retries",            FieldValue.Increment(1) },
                { "last_retry_moves",   ctx.movesMade },
                { "zero_move_retries",  FieldValue.Increment(ctx.movesMade == 0 ? 1 : 0) },
                { "last_played",        Timestamp.FromDateTime(DateTime.UtcNow) }
            };
            if (safeElapsed > 0) updates["total_time_spent"] = FieldValue.Increment(safeElapsed);

            Merge(GetLevelRef(levelIndex), updates, "level_retry");

            MergeProfileAndRoot(new Dictionary<string, object>
            {
                { "total_retries", FieldValue.Increment(1) }
            }, "retry_count");

            AddPlayTime(safeElapsed);
        }

        // Yeni deneme başlıyor
        levelStartTime = DateTime.UtcNow;
        levelActive    = true;
        ResetAttemptCounters();
    }

    public void LogLevelReset(int levelIndex, LevelContext ctx = default)
    {
        double elapsedSeconds = levelActive ? (DateTime.UtcNow - levelStartTime).TotalSeconds : 0;
        long safeElapsed = SaneSeconds(elapsedSeconds);

        if (isReady)
        {
            var updates = new Dictionary<string, object>
            {
                { "level_index",       levelIndex },
                { "resets",            FieldValue.Increment(1) },
                { "last_reset_moves",  ctx.movesMade },
                { "last_reset_matches",ctx.matchesMade },
                // En değerli ayrım: sıfır hamleyle reset → level anlaşılmıyor.
                { "zero_move_resets",  FieldValue.Increment(ctx.movesMade == 0 ? 1 : 0) },
                { "last_played",       Timestamp.FromDateTime(DateTime.UtcNow) }
            };
            if (safeElapsed > 0) updates["total_time_spent"] = FieldValue.Increment(safeElapsed);

            Merge(GetLevelRef(levelIndex), updates, "level_reset");

            MergeProfileAndRoot(new Dictionary<string, object>
            {
                { "total_resets", FieldValue.Increment(1) }
            }, "reset_count");

            AddPlayTime(safeElapsed);
        }

        // Reset = tekrar başlıyor
        levelStartTime = DateTime.UtcNow;
        levelActive    = true;
        ResetAttemptCounters();
    }

    public void LogLevelQuit(int levelIndex, float durationSeconds, LevelContext ctx = default,
                             int quitLayerY = -1)
    {
        if (levelIndex < 0) return;
        levelActive = false;
        long safeDuration = SaneSeconds(durationSeconds);

        if (!isReady) return;

        if (quitLayerY < 0 && GridManager.Instance != null) quitLayerY = GridManager.Instance.ActiveLayerY;
        if (quitLayerY < 0) quitLayerY = 0;

        Merge(GetLevelRef(levelIndex), new Dictionary<string, object>
        {
            { "level_index",                      levelIndex },
            { "quits",                            FieldValue.Increment(1) },
            { "total_time_spent",                 FieldValue.Increment(safeDuration) },
            { "last_quit_moves",                  ctx.movesMade },
            { "last_quit_pieces",                 ctx.piecesRemaining },
            { "last_quit_layer",                  quitLayerY },
            { $"churn_layers.layer_{quitLayerY}", FieldValue.Increment(1) },
            { "last_played",                      Timestamp.FromDateTime(DateTime.UtcNow) }
        }, "level_quit");

        MergeProfileAndRoot(new Dictionary<string, object>
        {
            { "last_level_quit",  levelIndex },
            { "last_churn_layer", quitLayerY }
        }, "quit_info");

        AddPlayTime(safeDuration);
    }

    // ─────────────────────────────────────────────────────────────
    // BOOSTER & POWER-UP KULLANIMI
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Oyuncu bir booster (Hammer, Undo, Shuffle, Magnet vb.) kullandığında çağrılır.
    /// </summary>
    public void LogBoosterUsed(string boosterType, int levelIndex = -1, int layerY = -1)
    {
        if (!isReady) return;
        if (levelIndex < 0) levelIndex = currentLevelIndex;
        if (layerY < 0 && GridManager.Instance != null) layerY = GridManager.Instance.ActiveLayerY;
        if (layerY < 0) layerY = 0;
        if (string.IsNullOrEmpty(boosterType)) boosterType = "unknown";

        string cleanBooster = boosterType.Trim().Replace(" ", "_");

        if (levelIndex >= 0)
        {
            Merge(GetLevelRef(levelIndex), new Dictionary<string, object>
            {
                { "level_index",                   levelIndex },
                { "total_boosters",                FieldValue.Increment(1) },
                { $"boosters_used.{cleanBooster}", FieldValue.Increment(1) },
                { "last_played",                   Timestamp.FromDateTime(DateTime.UtcNow) }
            }, "booster_level");
        }

        MergeProfileAndRoot(new Dictionary<string, object>
        {
            { "total_boosters_used",      FieldValue.Increment(1) },
            { $"boosters.{cleanBooster}", FieldValue.Increment(1) }
        }, "booster_profile");
    }

    // ─────────────────────────────────────────────────────────────
    // UYGULAMA ARKA PLAN / KAPATMA
    // ─────────────────────────────────────────────────────────────

    void OnApplicationPause(bool pauseStatus)
    {
        if (!isReady) return;

        if (pauseStatus)
        {
            pausedAt = DateTime.UtcNow;
            levelActiveBeforePause = levelActive;

            if (levelActive)
            {
                float timeOnLevel = (float)(DateTime.UtcNow - levelStartTime).TotalSeconds;
                LogLevelQuit(currentLevelIndex, timeOnLevel, BuildLocalContext());
            }
            EndSession(fromPause: true);
        }
        else
        {
            double awaySeconds = (DateTime.UtcNow - pausedAt).TotalSeconds;

            // Arka planda geçen süre level süresine yazılmasın
            if (levelActiveBeforePause)
            {
                levelActive    = true;
                levelStartTime = DateTime.UtcNow;
            }

            if (awaySeconds >= NEW_SESSION_AFTER_SECONDS)
            {
                sessionId               = NewSessionId();
                sessionStartTime        = DateTime.UtcNow;
                levelsPlayedThisSession = 0;
                StartSession();
            }
            else
            {
                // Kısa kesinti — aynı oturum devam eder, arka plan süresi düşülür.
                sessionStartTime = sessionStartTime.AddSeconds(awaySeconds);
            }
        }
    }

    void OnApplicationQuit()
    {
        EndSession(fromPause: false);
    }

    /// <summary>
    /// Arka plana geçiş gibi, oyun tarafından bağlam gelmeyen anlarda kullanılan
    /// yedek bağlam — en azından yerel sayaçlar kaydedilsin.
    /// </summary>
    private LevelContext BuildLocalContext()
    {
        return new LevelContext
        {
            movesMade       = CurrentLevelMoves,
            rotationsUsed   = CurrentLevelRotations,
            matchesMade     = CurrentLevelMatches,
            attemptNumber   = CurrentAttemptNumber,
            piecesRemaining = -1
        };
    }
}
#else
/// <summary>
/// Firebase SDK yokken (veya USE_FIREBASE tanımlı değilken) kullanılan boş sürüm.
/// Yerel sayaçlar burada da çalışır — oyun mantığı analitiğin varlığına bağlı olmasın.
/// </summary>
public class FirestoreAnalytics : MonoBehaviour
{
    public static FirestoreAnalytics Instance;

    public int CurrentLevelMoves     { get; private set; } = 0;
    public int CurrentLevelRotations { get; private set; } = 0;
    public int CurrentLevelMatches   { get; private set; } = 0;
    public int CurrentAttemptNumber  { get; private set; } = 1;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void ResetAttemptCounters()
    {
        CurrentLevelMoves     = 0;
        CurrentLevelRotations = 0;
        CurrentLevelMatches   = 0;
    }

    public void Initialize() {}
    public void IncrementMoveCount()     => CurrentLevelMoves++;
    public void IncrementRotationCount() => CurrentLevelRotations++;
    public void IncrementMatchCount(int count = 1) { if (count > 0) CurrentLevelMatches += count; }

    public void LogLevelStart(int levelIndex, LevelContext ctx = default)
    {
        ResetAttemptCounters();
        string key = "fs_attempts_" + levelIndex;
        CurrentAttemptNumber = PlayerPrefs.GetInt(key, 0) + 1;
        PlayerPrefs.SetInt(key, CurrentAttemptNumber);
    }
    public void LogLevelComplete(int levelIndex, float durationSeconds, LevelContext ctx = default) {}
    public void LogLevelFail(int levelIndex, float durationSeconds, LevelContext ctx = default, int failedLayerY = -1) {}
    public void LogLevelRetry(int levelIndex, LevelContext ctx = default) => ResetAttemptCounters();
    public void LogLevelReset(int levelIndex, LevelContext ctx = default) => ResetAttemptCounters();
    public void LogLevelQuit(int levelIndex, float durationSeconds, LevelContext ctx = default, int quitLayerY = -1) {}
    public void LogBoosterUsed(string boosterType, int levelIndex = -1, int layerY = -1) {}
}
#endif
