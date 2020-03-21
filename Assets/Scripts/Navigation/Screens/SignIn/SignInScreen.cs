using System.Globalization;
using Proyecto26;
using UniRx.Async;
using UnityEngine.UI;

public class SignInScreen : Screen
{
    public const string Id = "SignIn";

    public InputField uidInput;
    public InputField passwordInput;
    public TransitionElement closeButton;

    public override string GetId() => Id;

    public override void OnScreenInitialized()
    {
        base.OnScreenInitialized();
        uidInput.text = Context.OnlinePlayer.Uid;
    }

    public async UniTask SignIn()
    {
        if (uidInput.text == "")
        {
            Toast.Next(Toast.Status.Failure, "TOAST_ENTER_ID".Get());
            return;
        }
        if (passwordInput.text == "")
        {
            Toast.Next(Toast.Status.Failure, "TOAST_ENTER_PASSWORD".Get());
            return;
        }

        uidInput.text = uidInput.text.ToLower(CultureInfo.InvariantCulture);

        var completed = false;

        Context.OnlinePlayer.Uid = uidInput.text.Trim();
        Context.OnlinePlayer.Authenticate(passwordInput.text)
            .Then(profile =>
            {
                Toast.Next(Toast.Status.Success, "TOAST_SUCCESSFULLY_SIGNED_IN".Get());
                ProfileWidget.Instance.SetSignedIn(profile);
                Context.AudioManager.Get("ActionSuccess").Play();
                Context.ScreenManager.ChangeScreen(Context.ScreenManager.PopAndPeekHistory(), ScreenTransition.In, addTargetScreenToHistory: false);
            })
            .CatchRequestError(error =>
            {
                if (error.IsNetworkError)
                {
                    Toast.Next(Toast.Status.Failure, "TOAST_CHECK_NETWORK_CONNECTION".Get());
                }
                else
                {
                    switch (error.StatusCode)
                    {
                        case 401:
                            Toast.Next(Toast.Status.Failure, "TOAST_INCORRECT_ID_OR_PASSWORD".Get());
                            break;
                        case 404:
                            Toast.Next(Toast.Status.Failure, "TOAST_ID_NOT_FOUND".Get());
                            break;
                        default:
                            Toast.Next(Toast.Status.Failure, "TOAST_STATUS_CODE".Get(error.StatusCode));
                            break;
                    }
                }
            })
            .Finally(() => completed = true);

        closeButton.Leave();
        await UniTask.WaitUntil(() => completed);
        closeButton.Enter();
    }
}