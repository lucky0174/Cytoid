using System;
using System.Linq.Expressions;
using System.Threading;
using DG.Tweening;
using UniRx.Async;
using UnityEngine;
using UnityEngine.EventSystems;

public class NavigationElement : InteractableMonoBehavior
{
    public bool navigateToLastScreen;
    public string targetScreenId;
    public ScreenTransition transition;
    public float duration;
    public float currentScreenDelay;
    public float newScreenDelay;
    public Vector2 transitionFocus;
    public string soundName = "Navigate1";

    public override async void OnPointerClick(PointerEventData eventData)
    {
        base.OnPointerClick(eventData);
        if (Context.ScreenManager.ChangingToScreenId != null)
        {
            var cancellationSource = new CancellationTokenSource();
            cancellationSource.CancelAfter(TimeSpan.FromSeconds(1));
            await UniTask.WaitUntil(() => Context.ScreenManager.ChangingToScreenId == null,
                cancellationToken: cancellationSource.Token);
        }
        if (!string.IsNullOrWhiteSpace(soundName)) Context.AudioManager.Get(soundName).Play(ignoreDsp: true);
        Context.ScreenManager.ChangeScreen(
            navigateToLastScreen ? Context.ScreenManager.PopHistoryAndPeek() : targetScreenId, transition,
            duration, currentScreenDelay, newScreenDelay, transitionFocus, OnScreenChanged, addToHistory: !navigateToLastScreen);
    }

    protected virtual void OnScreenChanged(Screen screen) => Expression.Empty();
}