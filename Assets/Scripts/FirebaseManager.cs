using UnityEngine;
#if USE_FIREBASE
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Firebase.Extensions;
#endif

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance;

    private bool isReady = false;

    // Aktif level ve oturum için zaman takibi (level_quit için)
    private int  activeLevel      = -1;
    private float levelStartRealtime = 0f;
    private bool levelActive      = false;

    // Oturum bazlı sayaçlar
    private float sessionStartRealtime = 0f;
    private int   sessionLevelsPlayed  = 0;

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
                isReady = true;

                // Firestore analitiği de başlat
                FirestoreAnalytics.Instance?.Initialize();

                Debug.Log("[Firebase] Başarıyla başlatıldı.");
            }
            else
            {
                Debug.LogError("[Firebase] Bağımlılık hatası: " + task.Result);
            }
        });
#else
        Debug.LogWarning("[Firebase] SDK is not installed or USE_FIREBASE define is missing. Analytics disabled in Editor.");
#endif
    }

    // ─────────────────────────────────────────────────────────────
    // LEVEL OLAYLARI
    // ─────────────────────────────────────────────────────────────

    public void LogLevelStart(int levelIndex)
    {
        activeLevel           = levelIndex;
        levelStartRealtime    = Time.realtimeSinceStartup;
        levelActive           = true;
        sessionLevelsPlayed++;

#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.LogEvent("level_start",
            new Parameter("level_index",        levelIndex),
            new Parameter("session_level_count", sessionLevelsPlayed));
#endif

        FirestoreAnalytics.Instance?.LogLevelStart(levelIndex);
    }

    public void LogLevelComplete(int levelIndex, float durationSeconds, float timeRemaining = 0f)
    {
        levelActive = false;

#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.LogEvent("level_complete",
            new Parameter("level_index",      levelIndex),
            new Parameter("duration_seconds", durationSeconds),
            new Parameter("time_remaining",   timeRemaining));
#endif

        FirestoreAnalytics.Instance?.LogLevelComplete(levelIndex, durationSeconds);
    }

    public void LogLevelFail(int levelIndex, float durationSeconds)
    {
        levelActive = false;

#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.LogEvent("level_fail",
            new Parameter("level_index",      levelIndex),
            new Parameter("duration_seconds", durationSeconds));
#endif

        FirestoreAnalytics.Instance?.LogLevelFail(levelIndex, durationSeconds);
    }

    public void LogLevelRetry(int levelIndex)
    {
        levelStartRealtime = Time.realtimeSinceStartup;
        levelActive        = true;

#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.LogEvent("level_retry",
            new Parameter("level_index", levelIndex));
#endif

        FirestoreAnalytics.Instance?.LogLevelRetry(levelIndex);
    }

    public void LogLevelReset(int levelIndex)
    {
        levelStartRealtime = Time.realtimeSinceStartup;
        levelActive        = true;

#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.LogEvent("level_reset",
            new Parameter("level_index", levelIndex));
#endif

        FirestoreAnalytics.Instance?.LogLevelReset(levelIndex);
    }

    /// <summary>
    /// Kullanıcı level ortasında uygulamayı kapattığında tetiklenir.
    /// </summary>
    private void LogLevelQuit(int levelIndex, float durationSeconds)
    {
        if (levelIndex < 0) return;

#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.LogEvent("level_quit",
            new Parameter("level_index",      levelIndex),
            new Parameter("duration_seconds", durationSeconds));
#endif

        FirestoreAnalytics.Instance?.LogLevelQuit(levelIndex, durationSeconds);
    }

    // ─────────────────────────────────────────────────────────────
    // USER PROPERTIES
    // ─────────────────────────────────────────────────────────────

    public void SetCurrentLevel(int levelIndex)
    {
#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.SetUserProperty("current_level", levelIndex.ToString());
        FirebaseAnalytics.SetUserProperty("last_level_quit", levelIndex.ToString());
#endif
    }

    public void SetTotalLevelsCompleted(int count)
    {
#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.SetUserProperty("total_levels_completed", count.ToString());
#endif
    }

    public void SetFarthestLevel(int levelIndex)
    {
#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.SetUserProperty("farthest_level_reached", levelIndex.ToString());
#endif
    }

    public void SetTotalPlayTimeMinutes(int minutes)
    {
#if USE_FIREBASE
        if (!isReady) return;
        FirebaseAnalytics.SetUserProperty("total_play_time_minutes", minutes.ToString());
#endif
    }

    // ─────────────────────────────────────────────────────────────
    // UYGULAMA ARKA PLAN / KAPATMA
    // ─────────────────────────────────────────────────────────────

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
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
            if (isReady)
            {
                FirebaseAnalytics.LogEvent("session_end",
                    new Parameter("duration_seconds",  sessionDuration),
                    new Parameter("levels_played",     sessionLevelsPlayed));
            }
#endif
        }
        else
        {
            // Ön plana döndü → oturum sayaçlarını sıfırla
            sessionStartRealtime = Time.realtimeSinceStartup;
            sessionLevelsPlayed  = 0;

            // Aktif leveldan devam ediyorsa timer sıfırla
            if (levelActive) levelStartRealtime = Time.realtimeSinceStartup;
        }
    }

    void OnApplicationQuit()
    {
        if (levelActive && activeLevel >= 0)
        {
            float timeOnLevel = Time.realtimeSinceStartup - levelStartRealtime;
            LogLevelQuit(activeLevel, timeOnLevel);
        }

        float sessionDuration = Time.realtimeSinceStartup - sessionStartRealtime;
#if USE_FIREBASE
        if (isReady)
        {
            FirebaseAnalytics.LogEvent("session_end",
                new Parameter("duration_seconds", sessionDuration),
                new Parameter("levels_played",    sessionLevelsPlayed));
        }
#endif
    }
}
