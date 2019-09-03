using System;
using System.Collections;
using DG.Tweening;
using Proyecto26;
using UniRx.Async;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class GamePreparationScreen : Screen
{
    public const string Id = "GamePreparation";

    [GetComponentInChildrenName] public DepthCover cover;
    [GetComponentInChildren] public RankingContainer rankingContainer;
    [GetComponent] public AudioSource previewAudioSource;

    public Level Level { get; set; }
    
    public override string GetId() => Id;

    private bool willStart = false;

    public override void OnScreenBecameActive()
    {
        base.OnScreenBecameActive();

        if (Context.SelectedLevel == null)
        {
            Debug.LogWarning("Context.activeLevel is null");
            return;
        }
        Navigation.Instance.FadeOutLoopPlayer();

        var needReload = Level != Context.SelectedLevel;
        
        if (needReload)
        {
            Level = Context.SelectedLevel;
            UpdateRankings();
        }
        LoadCover(needReload);
        LoadPreview(needReload);
    }

    public void UpdateRankings()
    {
        RestClient.GetArray<RankingEntry>(new RequestHelper
        {
            Uri = Context.ApiBaseUrl + "/levels/" + Context.SelectedLevel.Meta.id + "/charts/" + Context.SelectedDifficulty.Id + "/ranking"
        }).Then(data => { rankingContainer.SetData(data); }).Catch(Debug.Log);
    }

    public async void LoadCover(bool load)
    {
        if (load)
        {
            var selectedLevel = Context.SelectedLevel;
            var path = "file://" + selectedLevel.Path + selectedLevel.Meta.background.path;

            Sprite sprite;
            using (var request = UnityWebRequestTexture.GetTexture(path))
            {
                await request.SendWebRequest();
                if (request.isNetworkError || request.isHttpError)
                {
                    Debug.LogError($"Failed to download cover from {path}");
                    Debug.LogError(request.error);
                    return;
                }

                sprite = DownloadHandlerTexture.GetContent(request).CreateSprite();
            }
            cover.OnCoverLoaded(sprite);
        }
        else
        {
            cover.OnCoverLoaded(null);
        }
    }

    public async void LoadPreview(bool load)
    {
        if (load)
        {
            var selectedLevel = Context.SelectedLevel;
            var path = "file://" + selectedLevel.Path + selectedLevel.Meta.music_preview.path;
            var loader = new AssetLoader(path);
            await loader.LoadAudioClip();
            if (loader.Error != null)
            {
                Debug.LogError($"Failed to download preview from {path}");
                Debug.LogError(loader.Error);
                return;
            }

            if (State == ScreenState.Active)
            {
                previewAudioSource.clip = loader.AudioClip;
            }
        }
        previewAudioSource.volume = 0;
        previewAudioSource.DOKill();
        previewAudioSource.DOFade(1, 1f).SetEase(Ease.Linear);
        previewAudioSource.loop = true;
        previewAudioSource.Play();
    }

    public override void OnScreenDestroyed()
    {
        base.OnScreenDestroyed();
        cover.image.color = Color.black;
    }

    public override void OnScreenBecameInactive()
    {
        base.OnScreenBecameInactive();
        previewAudioSource.DOFade(0, 1f).SetEase(Ease.Linear).onComplete = () => previewAudioSource.Stop();
        if (!willStart) Navigation.Instance.FadeInLoopPlayer();
    }

    public async void StartGame()
    {
        willStart = true;
        State = ScreenState.Inactive;

        cover.pulseElement.Pulse();
        ProfileWidget.Instance.FadeOut();
            
        Context.AudioManager.Get("LevelStart").Play(AudioTrackIndex.RoundRobin);

        StartCoroutine(LoadCoroutine());
        
        await UniTask.Delay(TimeSpan.FromSeconds(0.8f));

        cover.mask.DOFade(1, 0.4f);
        
        await UniTask.Delay(TimeSpan.FromSeconds(0.4f));

        if (loadOperation == null) await UniTask.WaitUntil(() => loadOperation != null);
        loadOperation.allowSceneActivation = true;
    }

    private AsyncOperation loadOperation;

    private IEnumerator LoadCoroutine()
    {
        loadOperation = SceneManager.LoadSceneAsync("Game");
        loadOperation.allowSceneActivation = false;
        yield return loadOperation;
    }
}