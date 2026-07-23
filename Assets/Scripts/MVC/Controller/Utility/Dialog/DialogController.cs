using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DialogController : Module
{
    [SerializeField] private DialogModel dialogModel;
    [SerializeField] private DialogView dialogView;

    private Action backgroundClickHandler;

    public void OpenDialog(DialogInfo info, bool animateStoryText = true) {
        dialogModel.OpenDialog(info);
        dialogView.OpenDialog(info, animateStoryText);
    }

    public void SetBackgroundClickHandler(Action handler) {
        backgroundClickHandler = handler;
    }

    public void SetReplyClickHandler(Action<NpcButtonHandler> handler) {
        dialogView.SetReplyClickHandler(handler);
    }

    public void SetStorySpeakerIconClickHandler(Action handler) {
        dialogView.SetStorySpeakerIconClickHandler(handler);
    }

    public void SetStorySpeakerHint(string hint) {
        dialogView.SetStorySpeakerHint(hint);
    }

    public bool RunAfterStoryTextReveal(Action handler) {
        return dialogView.RunAfterStoryTextReveal(handler);
    }

    public void CancelStoryTextReveal() {
        dialogView.CancelStoryTextReveal();
    }

    public void OnBackgroundClick() {
        if (dialogView.CompleteStoryTextReveal())
            return;

        if (backgroundClickHandler != null)
        {
            backgroundClickHandler.Invoke();
            return;
        }

        if (dialogModel.info?.replyHandler == null)
            return;

        if (dialogModel.info.replyHandler.Count != 1)
            return;

        var handler = dialogModel.info.replyHandler[0];
        if (handler.action != NpcAction.OpenDialog)
            return;

        if ((handler.param == null) || (handler.param.Count != 1))
            return;

        if (handler.param[0] != "null")
            return;

        DialogManager.instance.CloseDialog();
    }
}
