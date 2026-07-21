using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using System;
using System.Collections.Generic;

/// <summary>
/// Firestore tabanlı derin kullanıcı analitik sistemi.
/// Her kullanıcı için level bazlı tam geçmiş kaydeder.
///
/// Veri yapısı:
///   users/{deviceId}/profile          → genel kullanıcı özeti
///   users/{deviceId}/sessions/{id}    → oturum kayıtları
///   users/{deviceId}/level_history/{levelIndex} → level bazlı detay
/// </summary>
public class FirestoreAnalytics : MonoBehaviour
{
    public static FirestoreAnalytics Instance;

    // ── Sabitler ─────────────────────────────────────────────────
    private const string COLLECTION_USERS    = "users";
    private const string COLLECTION_SESSIONS = "sessions";
    private const string COLLECTION_LEVELS   = "level_history";
    private const string DOC_PROFILE         = "profile";

    // ── Özel Durum ────────────────────────────────────────────────
    private FirebaseFirestore db;
    private bool isReady = false;

    private string userId;
    private string sessionId;
    private DateTime sessionStartTime;
    private int levelsPlayedThisSession = 0;

    // Aktif level takibi
    private int currentLevelIndex = -1;
    private DateTime levelStartTime;
    private bool levelActive = false;

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
    /// FirebaseManager başarıyla init ettikten sonra bu metodu çağır.
    /// </summary>
    public void Initialize()
    {
        db = FirebaseFirestore.DefaultInstance;
        userId = SystemInfo.deviceUniqueIdentifier;
        sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + UnityEngine.Random.Range(1000, 9999);
        sessionStartTime = DateTime.UtcNow;
        isReady = true;

        EnsureUserProfile();
        StartSession();

        Debug.Log("[Firestore] Başlatıldı. UserID: " + userId + " | Session: " + sessionId);
    }

    // ─────────────────────────────────────────────────────────────
    // KULLANICI PROFİLİ
    // ─────────────────────────────────────────────────────────────

    private void EnsureUserProfile()
    {
        if (!isReady) return;

        DocumentReference profileRef = db
            .Collection(COLLECTION_USERS).Document(userId)
            .Collection("meta").Document(DOC_PROFILE);

        profileRef.GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (!task.IsCompleted || task.IsFaulted) return;

            DocumentSnapshot snap = task.Result;
            var now = Timestamp.FromDateTime(DateTime.UtcNow);

            if (!snap.Exists)
            {
                // İlk kez açılış — profil oluştur
                var data = new Dictionary<string, object>
                {
                    { "first_open",         now },
                    { "install_date",       DateTime.UtcNow.ToString("yyyy-MM-dd") },
                    { "platform",           Application.platform.ToString() },
                    { "app_version",        Application.version },
                    { "total_play_minutes", 0 },
                    { "total_sessions",     0 },
                    { "farthest_level",     0 },
                    { "total_fails",        0 },
                    { "total_resets",       0 },
                    { "total_retries",      0 },
                    { "last_seen",          now }
                };
                profileRef.SetAsync(data);

                // Dashboard için root document oluştur (users koleksiyonu listelenebilir olsun)
                WriteRootDoc(new Dictionary<string, object>
                {
                    { "device_id",          userId },
                    { "first_open",         now },
                    { "platform",           Application.platform.ToString() },
                    { "app_version",        Application.version },
                    { "farthest_level",     0 },
                    { "total_sessions",     0 },
                    { "total_fails",        0 },
                    { "total_resets",       0 },
                    { "total_retries",      0 },
                    { "total_play_minutes", 0.0 },
                    { "last_seen",          now }
                });
            }
            else
            {
                // Geri dönen kullanıcı — son görülme zamanını güncelle
                profileRef.UpdateAsync("last_seen", now);
                UpdateRootDoc(new Dictionary<string, object> { { "last_seen", now } });
            }
        });
    }

    /// <summary>
    /// Dashboard'un users koleksiyonunu listeleyebilmesi için root document'i oluşturur.
    /// </summary>
    private void WriteRootDoc(Dictionary<string, object> data)
    {
        db.Collection(COLLECTION_USERS).Document(userId)
          .SetAsync(data, SetOptions.MergeAll);
    }

    /// <summary>
    /// Root document'daki özet istatistikleri günceller.
    /// </summary>
    private void UpdateRootDoc(Dictionary<string, object> updates)
    {
        updates["last_seen"] = Timestamp.FromDateTime(DateTime.UtcNow);
        db.Collection(COLLECTION_USERS).Document(userId).UpdateAsync(updates);
    }

    // ─────────────────────────────────────────────────────────────
    // OTURUM YÖNETİMİ
    // ─────────────────────────────────────────────────────────────

    private void StartSession()
    {
        if (!isReady) return;

        var data = new Dictionary<string, object>
        {
            { "start_time",         Timestamp.FromDateTime(sessionStartTime) },
            { "end_time",           FieldValue.ServerTimestamp },
            { "duration_seconds",   0 },
            { "levels_played",      0 },
            { "device_model",       SystemInfo.deviceModel },
            { "os_version",         SystemInfo.operatingSystem }
        };

        db.Collection(COLLECTION_USERS).Document(userId)
          .Collection(COLLECTION_SESSIONS).Document(sessionId)
          .SetAsync(data);

        // Profilde toplam oturum sayısını artır
        db.Collection(COLLECTION_USERS).Document(userId)
          .Collection("meta").Document(DOC_PROFILE)
          .UpdateAsync("total_sessions", FieldValue.Increment(1));
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

        double durationSeconds = (DateTime.UtcNow - sessionStartTime).TotalSeconds;

        var updates = new Dictionary<string, object>
        {
            { "end_time",         Timestamp.FromDateTime(DateTime.UtcNow) },
            { "duration_seconds", (long)durationSeconds },
            { "levels_played",    levelsPlayedThisSession },
            { "ended_by_pause",   fromPause }
        };

        db.Collection(COLLECTION_USERS).Document(userId)
          .Collection(COLLECTION_SESSIONS).Document(sessionId)
          .UpdateAsync(updates);
    }

    private void AddPlayTime(double seconds)
    {
        if (!isReady || seconds <= 0) return;
        
        double minutes = seconds / 60.0;
        db.Collection(COLLECTION_USERS).Document(userId)
          .Collection("meta").Document(DOC_PROFILE)
          .UpdateAsync("total_play_minutes", FieldValue.Increment(minutes));
    }

    // ─────────────────────────────────────────────────────────────
    // LEVEL OLAYLARI
    // ─────────────────────────────────────────────────────────────

    public void LogLevelStart(int levelIndex)
    {
        if (!isReady) return;

        currentLevelIndex   = levelIndex;
        levelStartTime      = DateTime.UtcNow;
        levelActive         = true;
        levelsPlayedThisSession++;

        DocumentReference levelRef = GetLevelRef(levelIndex);

        // İlk kez mi bu level?
        levelRef.GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (!task.IsCompleted || task.IsFaulted) return;

            if (!task.Result.Exists)
            {
                // Level kaydı yok → oluştur
                var data = new Dictionary<string, object>
                {
                    { "level_index",      levelIndex },
                    { "attempts",         1 },
                    { "completions",      0 },
                    { "fails",            0 },
                    { "resets",           0 },
                    { "retries",          0 },
                    { "quits",            0 },
                    { "best_time",        0 },
                    { "total_time_spent", 0 },
                    { "first_attempt",    Timestamp.FromDateTime(DateTime.UtcNow) },
                    { "last_played",      Timestamp.FromDateTime(DateTime.UtcNow) }
                };
                levelRef.SetAsync(data);
            }
            else
            {
                // Var → attempt sayısını artır
                levelRef.UpdateAsync(new Dictionary<string, object>
                {
                    { "attempts",    FieldValue.Increment(1) },
                    { "last_played", Timestamp.FromDateTime(DateTime.UtcNow) }
                });
            }
        });

        // Farthest level güncelle
        db.Collection(COLLECTION_USERS).Document(userId)
          .Collection("meta").Document(DOC_PROFILE)
          .GetSnapshotAsync().ContinueWithOnMainThread(task =>
          {
              if (!task.IsCompleted || task.IsFaulted) return;
              long farthest = 0;
              if (task.Result.Exists && task.Result.TryGetValue("farthest_level", out farthest))
              {
                  if (levelIndex > farthest)
                      db.Collection(COLLECTION_USERS).Document(userId)
                        .Collection("meta").Document(DOC_PROFILE)
                        .UpdateAsync("farthest_level", levelIndex);
              }
          });
    }

    public void LogLevelComplete(int levelIndex, float durationSeconds)
    {
        if (!isReady) return;
        levelActive = false;

        var updates = new Dictionary<string, object>
        {
            { "completions",      FieldValue.Increment(1) },
            { "total_time_spent", FieldValue.Increment((long)durationSeconds) },
            { "last_played",      Timestamp.FromDateTime(DateTime.UtcNow) }
        };

        // Best time güncelle (ilk kez veya daha hızsa)
        DocumentReference levelRef = GetLevelRef(levelIndex);
        levelRef.GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (!task.IsCompleted || task.IsFaulted) return;

            long bestTime = 0;
            if (task.Result.Exists)
                task.Result.TryGetValue("best_time", out bestTime);

            if (bestTime == 0 || durationSeconds < bestTime)
                updates["best_time"] = (long)durationSeconds;

            levelRef.UpdateAsync(updates);
        });

        // Root doc'u güncelle
        UpdateRootDoc(new Dictionary<string, object>
        {
            { "farthest_level", FieldValue.Increment(0) } // last_seen zaten UpdateRootDoc'ta güncelleniyor
        });

        AddPlayTime(durationSeconds);
    }

    public void LogLevelFail(int levelIndex, float durationSeconds)
    {
        if (!isReady) return;
        levelActive = false;

        GetLevelRef(levelIndex).UpdateAsync(new Dictionary<string, object>
        {
            { "fails",            FieldValue.Increment(1) },
            { "total_time_spent", FieldValue.Increment((long)durationSeconds) }
        });

        db.Collection(COLLECTION_USERS).Document(userId)
          .Collection("meta").Document(DOC_PROFILE)
          .UpdateAsync("total_fails", FieldValue.Increment(1));

        // Root doc güncelle
        UpdateRootDoc(new Dictionary<string, object>
        {
            { "total_fails", FieldValue.Increment(1) }
        });

        AddPlayTime(durationSeconds);
    }

    public void LogLevelRetry(int levelIndex)
    {
        if (!isReady) return;

        double elapsedSeconds = 0;
        if (levelActive)
        {
            elapsedSeconds = (DateTime.UtcNow - levelStartTime).TotalSeconds;
        }

        var updates = new Dictionary<string, object>
        {
            { "retries",           FieldValue.Increment(1) }
        };
        if (elapsedSeconds > 0)
        {
            updates["total_time_spent"] = FieldValue.Increment((long)elapsedSeconds);
            AddPlayTime(elapsedSeconds);
        }

        GetLevelRef(levelIndex).UpdateAsync(updates);

        db.Collection(COLLECTION_USERS).Document(userId)
          .Collection("meta").Document(DOC_PROFILE)
          .UpdateAsync("total_retries", FieldValue.Increment(1));

        // Yeni attempt başlıyor
        levelStartTime = DateTime.UtcNow;
        levelActive    = true;
    }

    public void LogLevelReset(int levelIndex)
    {
        if (!isReady) return;

        double elapsedSeconds = 0;
        if (levelActive)
        {
            elapsedSeconds = (DateTime.UtcNow - levelStartTime).TotalSeconds;
        }

        var updates = new Dictionary<string, object>
        {
            { "resets",           FieldValue.Increment(1) }
        };
        if (elapsedSeconds > 0)
        {
            updates["total_time_spent"] = FieldValue.Increment((long)elapsedSeconds);
            AddPlayTime(elapsedSeconds);
        }

        GetLevelRef(levelIndex).UpdateAsync(updates);

        db.Collection(COLLECTION_USERS).Document(userId)
          .Collection("meta").Document(DOC_PROFILE)
          .UpdateAsync("total_resets", FieldValue.Increment(1));

        // Reset = tekrar başlıyor
        levelStartTime = DateTime.UtcNow;
        levelActive    = true;
    }

    public void LogLevelQuit(int levelIndex, float durationSeconds)
    {
        if (!isReady || levelIndex < 0) return;
        levelActive = false;

        GetLevelRef(levelIndex).UpdateAsync(new Dictionary<string, object>
        {
            { "quits",            FieldValue.Increment(1) },
            { "total_time_spent", FieldValue.Increment((long)durationSeconds) }
        });

        // Profilde "son çıkılan level"
        db.Collection(COLLECTION_USERS).Document(userId)
          .Collection("meta").Document(DOC_PROFILE)
          .UpdateAsync("last_level_quit", levelIndex);

        AddPlayTime(durationSeconds);
    }

    // ─────────────────────────────────────────────────────────────
    // UYGULAMA ARKA PLAN / KAPATMA
    // ─────────────────────────────────────────────────────────────

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            // Uygulama arka plana alındı
            if (levelActive)
            {
                float timeOnLevel = (float)(DateTime.UtcNow - levelStartTime).TotalSeconds;
                LogLevelQuit(currentLevelIndex, timeOnLevel);
            }
            EndSession(fromPause: true);
        }
        else
        {
            // Uygulama ön plana döndü → yeni oturum başlat
            sessionId        = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_") + UnityEngine.Random.Range(1000, 9999);
            sessionStartTime = DateTime.UtcNow;
            levelsPlayedThisSession = 0;

            // Aktif levelden devam ediyorsa timer'ı sıfırla
            if (levelActive) levelStartTime = DateTime.UtcNow;

            StartSession();
        }
    }

    void OnApplicationQuit()
    {
        EndSession(fromPause: false);
    }

    // ─────────────────────────────────────────────────────────────
    // YARDIMCI
    // ─────────────────────────────────────────────────────────────

    private DocumentReference GetLevelRef(int levelIndex)
    {
        return db.Collection(COLLECTION_USERS).Document(userId)
                 .Collection(COLLECTION_LEVELS).Document(levelIndex.ToString());
    }
}
