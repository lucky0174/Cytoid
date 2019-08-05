using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

public class FreePlayButton : NavigationElement
{
    public RectTransform parent;
    
    public override void OnPointerClick(PointerEventData eventData)
    {
        var mainMenuScreen = (MainMenuScreen) this.GetOwningScreen();
        mainMenuScreen.translucentImage.DOFade(1, 0.4f);
        transitionFocus = parent.GetScreenSpaceCenter();
        base.OnPointerClick(eventData);
    }
    
    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);
        parent.DOScale(0.96f, 0.2f).SetEase(Ease.OutCubic);
    }
        
    public override void OnPointerUp(PointerEventData eventData)
    {
        base.OnPointerUp(eventData);
        parent.DOScale(1f, 0.2f).SetEase(Ease.OutCubic);
    }
}