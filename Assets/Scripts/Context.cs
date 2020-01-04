using System;
using System.Collections.Generic;
using System.IO;
using DG.Tweening;
using UniRx.Async;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Context : SingletonMonoBehavior<Context>
{
    public const string ApiBaseUrl = "https://api.cytoid.io";
    public const string WebsiteUrl = "https://cytoid.io";
    
    public const int ReferenceWidth = 1920;
    public const int ReferenceHeight = 1080;

    public static string DataPath;
    public static int InitialWidth;
    public static int InitialHeight;
    
    public static AudioManager AudioManager;
    public static ScreenManager ScreenManager;
    public static LevelManager LevelManager = new LevelManager();
    public static SpriteCache SpriteCache = new SpriteCache();
    
    public static Level SelectedLevel;
    public static Difficulty SelectedDifficulty = Difficulty.Easy;
    public static Difficulty PreferredDifficulty = Difficulty.Easy;
    public static HashSet<Mod> SelectedMods = new HashSet<Mod>();

    public static GameResult LastGameResult;

    public static LocalPlayer LocalPlayer = new LocalPlayer();
    public static OnlinePlayer OnlinePlayer = new OnlinePlayer();

    protected override void Awake()
    {
        base.Awake();

        if (GameObject.FindGameObjectsWithTag("Context").Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);

        InitializeApplication();
    }

    private async void InitializeApplication()
    {
        InitialWidth = UnityEngine.Screen.width;
        InitialHeight = UnityEngine.Screen.height;
        
        DOTween.defaultEaseType = Ease.OutCubic;
        UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Application.targetFrameRate = 120;

        DataPath = Application.persistentDataPath;
        print("Data path: " + DataPath);

#if !UNITY_EDITOR
        // On Android...
		if (Application.platform == RuntimePlatform.Android)
		{
			var dir = GetAndroidStoragePath();
			if (dir == null)
			{
				Application.Quit();
				return;
			}
			DataPath = dir + "/Cytoid";
			// Create an empty folder if it doesn't already exist
			Directory.CreateDirectory(DataPath);
		}
#endif

#if UNITY_EDITOR
        Application.runInBackground = true;
#endif

        SelectedMods = new HashSet<Mod>(LocalPlayer.EnabledMods);

        if (SceneManager.GetActiveScene().name == "Game")
        {
            // Load test level
            await LevelManager.LoadFromMetadataFiles(new List<string> { DataPath + "/player/level.json" });
            SelectedLevel = LevelManager.LoadedLevels[0];
            SelectedDifficulty = Difficulty.Parse(SelectedLevel.Meta.charts[0].type);
        }
        else
        {
            await UniTask.WaitUntil(() => ScreenManager != null);
            if (true)
            {
                ScreenManager.ChangeScreen("MainMenu", ScreenTransition.None);
            }
            
            if (false)
            {
                // Load f.fff
                await LevelManager.LoadFromMetadataFiles(new List<string> { DataPath + "/f.fff/level.json" });
                SelectedLevel = LevelManager.LoadedLevels[0];
                SelectedDifficulty = Difficulty.Parse(SelectedLevel.Meta.charts[0].type);
                ScreenManager.ChangeScreen("GamePreparation", ScreenTransition.None);
            }

            if (false)
            {
                // Load result
                await LevelManager.LoadFromMetadataFiles(new List<string> { DataPath + "/playeralice/level.json" });
                SelectedLevel = LevelManager.LoadedLevels[0];
                SelectedDifficulty = Difficulty.Hard;
                ScreenManager.ChangeScreen("Result", ScreenTransition.None);
            }
        }
    }

    public static void OnSceneChanged(string prev, string next)
    {
        if (prev == "Game" && next == "Navigation")
        {
            if (LastGameResult != null)
            {
                // Show result screen
                ScreenManager.ChangeScreen("Result", ScreenTransition.None);
            }
            else
            {
                // Show game preparation screen
                ScreenManager.ChangeScreen("GamePreparation", ScreenTransition.None);
            }
        }
    }
    
    public static void SetAutoRotation(bool autoRotation)
    {
        if (autoRotation)
        {
            UnityEngine.Screen.autorotateToLandscapeLeft = true;
            UnityEngine.Screen.autorotateToLandscapeRight = true;
        }
        else
        {
            if (UnityEngine.Screen.orientation != ScreenOrientation.LandscapeLeft)
                UnityEngine.Screen.autorotateToLandscapeLeft = false;
            if (UnityEngine.Screen.orientation != ScreenOrientation.LandscapeRight)
                UnityEngine.Screen.autorotateToLandscapeRight = false;
        }
    }

    private string GetAndroidStoragePath()
    {
        var path = "";
        if (Application.platform == RuntimePlatform.Android)
        {
            try
            {
                using (var javaClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    using (var activityClass = javaClass.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        path = activityClass.Call<AndroidJavaObject>("getAndroidStorageFile")
                            .Call<string>("getAbsolutePath");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Could not get Android storage path: " + e.Message);
            }
        }

        return path;
    }
}