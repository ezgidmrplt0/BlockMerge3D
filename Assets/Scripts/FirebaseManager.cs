using UnityEngine;
using System;
using System.Collections.Generic;
#if USE_FIREBASE
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Firebase.Extensions;
#endif

/// <summary>
/// Firebase Analytics + Crashlytics köprüsü.
///
/// Firebase init'i asenkron olduğu için (CheckAndFixDependenciesAsync) uygulamanın ilk
/// karesinde gelen olaylar SDK hazır olmadan tetikleniyor. Eskiden bunlar "if (!isReady)
/// return;" ile sessizce atılıyordu — yani HER açılışın ilk level_start'ı kayboluyordu.
/// Şimdi hazır olana kadar kuyruğa alınıp init biter bitmez sırayla gönderiliyor.
/// </summary>
public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance;

    private bool isReady = false;

    /// <summary>SDK hazır olana kadar bekleyen olaylar.</summary>
    private readonly List<Action> pendingEvents = new List<Action>();
    private const int MAX_PENDING_EVENTS = 64;

    // Aktif level ve oturum için zaman takibi (level_quit için)
    private int   activeLevel        = -1;
    private float levelStartRealtime = 0f;
    private bool  levelActive        = false;

    // Oturum bazlı sayaçlar
    private float sessionStartRealtime = 0f;
    private int   sessionLevelsPlayed  = 0;

    // Arka plan takibi — Time.realtimeSinceStartup arka planda da işlediği için
    // uygulama dışında geçen süre level ve oturum sayaçlarından düşülür.
    private float pauseStartRealtime = 0f;
    private bool  isPaused           = false;

    // Awake sırası garanti olmadığı için FirestoreAnalytics.Instance henüz atanmamış
    // olabilir; ilk erişimde sahneden çözülür ve önbelleğe alınır.
    private FirestoreAnalytics firestoreCache;
    private FirestoreAnalytics Firestore
    {
        get
        {
            if (firestoreCache == null)
            {
                firestoreCache = FirestoreAnalytics.Instance != null
                    ? FirestoreAnalytics.Instance
                    : FindObjectOfType<FirestoreAnalytics>();
            }
            return firestoreCache;
        }
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeFirebase();
    }

    void InitializeFirebase()
    {
#if USE_FIREBASE
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
                Crashlytics.ReportUncaughtExceptionsAsFatal = true;
                sessionStartRealtime = Time.realtimeSinceStartup;

                // Firestore analitiğini başlat, sonra bekleyen olayları boşalt
                Firestore?.Initialize();
                MarkReady();

                Debug.Log("[Firebase] Başarıyla başlatıldı.");
            }
            else
            {
                Debug.LogError("[Firebase] Bağımlılık hatası: " + task.Result);
                pendingEvents.Clear();
            }
        });
#else
        Debug.Log("[Firebase] USE_FIREBASE tanımlı değil veya SDK yüklü değil. Mock modunda çalışıyor.");
        sessionStartRealtime = Time.realtimeSinceStartup;
        Firestore?.Initialize();
        MarkReady();
#endif
    }

    // ─────────────────────────────────────────────────────────────
    // OLAY KUYRUĞU
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// SDK hazır değilse olayı kuyruğa alır ve true döner (çağıran metot çıkmalıdır).
    /// </summary>
    private bool Defer(Action action)
    {
        if (isReady) return false;

        if (pendingEvents.Count < MAX_PENDING_EVENTS)
            pendingEvents.Add(action);
        else
            Debug.LogWarning("[Firebase] Olay kuyruğu doldu, olay atlandı.");

        return true;
    }

    private void MarkReady()
    {
        isReady = true;

        if (pendingEvents.Count > 0)
            Debug.Log("[Firebase] " + pendingEvents.Count + " bekleyen olay gönderiliyor.");

        // Replay sırasında kuyruğa yeni olay eklenmesin diye kopya üzerinden ilerle
        var queued = new List<Action>(pendingEvents);
        pendingEvents.Clear();

        foreach (var action in queued)
        {
            try { action(); }
            catch (Exception e) { Debug.LogWarning("[Firebase] Bekleyen olay gönderilemedi: " + e.Message); }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // LEVEL OLAYLARI
    // ─────────────────────────────────────────────────────────────

    public void LogLevelStart(int levelIndex, LevelContext ctx = default)
    {
        // Yerel durum SDK'dan bağımsız olarak hemen güncellenir —
        // aksi halde init sırasında başlayan level "aktif değil" sanılır.
        activeLevel        = levelIndex;
        levelStartRealtime = Time.realtimeSinceStartup;
        levelActive        = true;
        sessionLevelsPlayed++;

        if (Defer(() => SendLevelStart(levelIndex, ctx))) return;
        SendLevelStart(levelIndex, ctx);
    }

    private void SendLevelStart(int levelIndex, LevelContext ctx)
    {
#if USE_FIREBASE
        FirebaseAnalytics.LogEvent("level_start",
            new Parameter("level_index",         levelIndex),
            new Parameter("session_level_count", sessionLevelsPlayed),
            new Parameter("level_type",          ctx.levelType),
            new Parameter("attempt_number",      ctx.attemptNumber),
            new Parameter("tutorial_shown",      ctx.tutorialShown ? 1 : 0));
#endif
        Firestore?.LogLevelStart(levelIndex, ctx);
    }

    public void LogLevelComplete(int levelIndex, float durationSeconds, float timeRemaining = 0f,
                                 LevelContext ctx = default)
    {
        levelActive = false;

        if (Defer(() => SendLevelComplete(levelIndex, durationSeconds, timeRemaining, ctx))) return;
        SendLevelComplete(levelIndex, durationSeconds, timeRemaining, ctx);
    }

    private void SendLevelComplete(int levelIndex, float durationSeconds, float timeRemaining,
                                   LevelContext ctx)
    {
#if USE_FIREBASE
        FirebaseAnalytics.LogEvent("level_complete",
            new Parameter("level_index",      levelIndex),
            new Parameter("duration_seconds", durationSeconds),
            new Parameter("time_remaining",   timeRemaining),
            new Parameter("level_type",       ctx.levelType),
            new Parameter("moves_made",       ctx.movesMade),
            new Parameter("rotations_used",   ctx.rotationsUsed),
            new Parameter("attempt_number",   ctx.attemptNumber));
#endif
        Firestore?.LogLevelComplete(levelIndex, durationSeconds, ctx);
    }

    public void LogLevelFail(int levelIndex, float durationSeconds, LevelContext ctx = default,
                             int failedLayerY = -1)
    {
        levelActive = false;

        if (Defer(() => SendLevelFail(levelIndex, durationSeconds, ctx, failedLayerY))) return;
        SendLevelFail(levelIndex, durationSeconds, ctx, failedLayerY);
    }

    private void SendLevelFail(int levelIndex, float durationSeconds, LevelContext ctx, int failedLayerY)
    {
#if USE_FIREBASE
        FirebaseAnalytics.LogEvent("level_fail",
            new Parameter("level_index",      levelIndex),
            new Parameter("duration_seconds", durationSeconds),
            new Parameter("level_type",       ctx.levelType),
            new Parameter("moves_made",       ctx.movesMade),
            new Parameter("matches_made",     ctx.matchesMade),
            new Parameter("pieces_remaining", ctx.piecesRemaining),
            new Parameter("failed_layer",     failedLayerY),
            new Parameter("attempt_number",   ctx.attemptNumber));
#endif
        Firestore?.LogLevelFail(levelIndex, durationSeconds, ctx, failedLayerY);
    }

    public void LogLevelRetry(int levelIndex, LevelContext ctx = default)
    {
        levelStartRealtime = Time.realtimeSinceStartup;
        levelActive        = true;

        if (Defer(() => SendLevelRetry(levelIndex, ctx))) return;
        SendLevelRetry(levelIndex, ctx);
    }

    private void SendLevelRetry(int levelIndex, LevelContext ctx)
    {
#if USE_FIREBASE
        FirebaseAnalytics.LogEvent("level_retry",
            new Parameter("level_index",      levelIndex),
            new Parameter("level_type",       ctx.levelType),
            new Parameter("moves_made",       ctx.movesMade),
            new Parameter("matches_made",     ctx.matchesMade),
            new Parameter("pieces_remaining", ctx.piecesRemaining),
            new Parameter("attempt_number",   ctx.attemptNumber));
#endif
        Firestore?.LogLevelRetry(levelIndex, ctx);
    }

    public void LogLevelReset(int levelIndex, LevelContext ctx = default)
    {
        levelStartRealtime = Time.realtimeSinceStartup;
        levelActive        = true;

        if (Defer(() => SendLevelReset(levelIndex, ctx))) return;
        SendLevelReset(levelIndex, ctx);
    }

    private void SendLevelReset(int levelIndex, LevelContext ctx)
    {
#if USE_FIREBASE
        // moves_made burada kilit: 0 ise oyuncu tahtayı anlamadan sıfırladı,
        // yüksekse denedi ama çözemedi. İkisi farklı tasarım problemi.
        FirebaseAnalytics.LogEvent("level_reset",
            new Parameter("level_index",      levelIndex),
            new Parameter("level_type",       ctx.levelType),
            new Parameter("moves_made",       ctx.movesMade),
            new Parameter("matches_made",     ctx.matchesMade),
            new Parameter("rotations_used",   ctx.rotationsUsed),
            new Parameter("pieces_remaining", ctx.piecesRemaining),
            new Parameter("attempt_number",   ctx.attemptNumber));
#endif
        Firestore?.LogLevelReset(levelIndex, ctx);
    }

    /// <summary>
    /// Kullanıcı level ortasında uygulamayı kapattığında tetiklenir.
    /// </summary>
    private void LogLevelQuit(int levelIndex, float durationSeconds)
    {
        if (levelIndex < 0) return;

        LevelContext ctx = GameManager.Instance != null
            ? GameManager.Instance.BuildContext() : default;

        if (Defer(() => SendLevelQuit(levelIndex, durationSeconds, ctx))) return;
        SendLevelQuit(levelIndex, durationSeconds, ctx);
    }

    private void SendLevelQuit(int levelIndex, float durationSeconds, LevelContext ctx)
    {
#if USE_FIREBASE
        FirebaseAnalytics.LogEvent("level_quit",
            new Parameter("level_index",      levelIndex),
            new Parameter("duration_seconds", durationSeconds),
            new Parameter("level_type",       ctx.levelType),
            new Parameter("moves_made",       ctx.movesMade),
            new Parameter("matches_made",     ctx.matchesMade),
            new Parameter("pieces_remaining", ctx.piecesRemaining),
            new Parameter("attempt_number",   ctx.attemptNumber));
#endif
        Firestore?.LogLevelQuit(levelIndex, durationSeconds, ctx);
    }

    /// <summary>
    /// Booster veya power-up kullanıldığında tetiklenir.
    /// </summary>
    public void LogBoosterUsed(string boosterType, int levelIndex = -1, int layerY = -1)
    {
        if (Defer(() => SendBoosterUsed(boosterType, levelIndex, layerY))) return;
        SendBoosterUsed(boosterType, levelIndex, layerY);
    }

    private void SendBoosterUsed(string boosterType, int levelIndex, int layerY)
    {
#if USE_FIREBASE
        FirebaseAnalytics.LogEvent("booster_used",
            new Parameter("booster_type", boosterType),
            new Parameter("level_index",  levelIndex),
            new Parameter("layer_index",  layerY));
#endif
        Firestore?.LogBoosterUsed(boosterType, levelIndex, layerY);
    }

    // ─────────────────────────────────────────────────────────────
    // USER PROPERTIES
    // ─────────────────────────────────────────────────────────────

    public void SetCurrentLevel(int levelIndex)
    {
        if (Defer(() => SetCurrentLevel(levelIndex))) return;
#if USE_FIREBASE
        FirebaseAnalytics.SetUserProperty("current_level", levelIndex.ToString());
        FirebaseAnalytics.SetUserProperty("last_level_quit", levelIndex.ToString());
#endif
    }

    public void SetTotalLevelsCompleted(int count)
    {
        if (Defer(() => SetTotalLevelsCompleted(count))) return;
#if USE_FIREBASE
        FirebaseAnalytics.SetUserProperty("total_levels_completed", count.ToString());
#endif
    }

    public void SetFarthestLevel(int levelIndex)
    {
        if (Defer(() => SetFarthestLevel(levelIndex))) return;
#if USE_FIREBASE
        FirebaseAnalytics.SetUserProperty("farthest_level_reached", levelIndex.ToString());
#endif
    }

    public void SetTotalPlayTimeMinutes(int minutes)
    {
        if (Defer(() => SetTotalPlayTimeMinutes(minutes))) return;
#if USE_FIREBASE
        FirebaseAnalytics.SetUserProperty("total_play_time_minutes", minutes.ToString());
#endif
    }

    // ─────────────────────────────────────────────────────────────
    // UYGULAMA ARKA PLAN / KAPATMA
    // ─────────────────────────────────────────────────────────────

    void OnApplicationPause(bool pauseStatus)
    {
        if (!isReady) return;

        if (pauseStatus)
        {
            pauseStartRealtime = Time.realtimeSinceStartup;
            isPaused           = true;

            // Uygulama arka plana alındı
            if (levelActive && activeLevel >= 0)
            {
                float timeOnLevel = Time.realtimeSinceStartup - levelStartRealtime;
                LogLevelQuit(activeLevel, timeOnLevel);
                levelActive = false;
            }

            // Oturum süresi kaydı
            float sessionDuration = Time.realtimeSinceStartup - sessionStartRealtime;
#if USE_FIREBASE
            FirebaseAnalytics.LogEvent("session_end",
                new Parameter("duration_seconds", sessionDuration),
                new Parameter("levels_played",    sessionLevelsPlayed));
#endif
        }
        else
        {
            // Gerçekten arka plana düşülmediyse (bazı platformlar açılışta focus
            // olayı gönderir) sayaçlara dokunma.
            if (!isPaused) return;
            isPaused = false;

            // Arka planda geçen süreyi sayaçlardan DÜŞ — eskiden sayaçlar sıfırlanıyordu,
            // bu hem oturum sayısını şişiriyor hem levels_played'i sıfırlıyordu.
            float awaySeconds = Time.realtimeSinceStartup - pauseStartRealtime;
            sessionStartRealtime += awaySeconds;
            levelStartRealtime   += awaySeconds;
        }
    }

    void OnApplicationQuit()
    {
        if (!isReady) return;

        if (levelActive && activeLevel >= 0)
        {
            float timeOnLevel = Time.realtimeSinceStartup - levelStartRealtime;
            LogLevelQuit(activeLevel, timeOnLevel);
        }

        float sessionDuration = Time.realtimeSinceStartup - sessionStartRealtime;
#if USE_FIREBASE
        FirebaseAnalytics.LogEvent("session_end",
            new Parameter("duration_seconds", sessionDuration),
            new Parameter("levels_played",    sessionLevelsPlayed));
#endif
    }
}
