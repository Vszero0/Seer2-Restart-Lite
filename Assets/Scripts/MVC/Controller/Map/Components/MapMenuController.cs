using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class MapMenuController : UIModule
{
    [SerializeField] private Image mailNewIcon;

    /*
    private const string SettingsAreaName = "Settings Area";
    private const string MissionAreaName = "Mission Area";
    private const string MissionPanelName = "Mission";
    private const string MissionPromptText = "\u4EFB\u52A1";
    [SerializeField] private Sprite missionButtonIcon;
    [SerializeField] private Vector2 missionButtonOffset = new Vector2(0, 65);
    */

    public override void Init()
    {
        base.Init();
        mailNewIcon?.gameObject.SetActive(Player.instance.gameData.mailStorage.Any(x => !x.isRead));
        // CreateMissionButton();
    }

    public void SetInfoPromptText(string content)
    {
        infoPrompt.SetInfoPromptWithAutoSize(content, TextAnchor.MiddleCenter);
        infoPrompt.SetPositionOffset(new Vector2(2, 2));
    }

    public void GoToStation()
    {
        GoToMap(Player.instance.currentMap.worldId * 10000 + 81);
    }

    public void OpenSPTPanel()
    {
        var panel = Panel.OpenPanel<SPTBossPanel>();
        var page = Player.instance.currentMap.worldId + 1;

        if (page <= 1)
            page = Player.instance.currentMap.categoryId <= 10 ? 0 : 1;

        panel?.SetPage(page);
    }

    public void ToggleShootMode()
    {
        Player.instance.isShootMode = !Player.instance.isShootMode;
    }

    /*
    private void CreateMissionButton()
    {
        if (transform.Find(MissionAreaName) != null)
            return;

        RectTransform settingsArea = transform.Find(SettingsAreaName) as RectTransform;
        if (settingsArea == null)
            return;

        GameObject missionAreaObject = Instantiate(settingsArea.gameObject, settingsArea.parent);
        missionAreaObject.name = MissionAreaName;

        RectTransform missionArea = missionAreaObject.GetComponent<RectTransform>();
        Vector2 offset = missionButtonOffset == Vector2.zero ? new Vector2(0, 65) : missionButtonOffset;
        missionArea.anchoredPosition = settingsArea.anchoredPosition + offset;
        missionArea.SetSiblingIndex(settingsArea.GetSiblingIndex());

        ConfigureMissionButton(missionAreaObject);
    }

    private void ConfigureMissionButton(GameObject missionAreaObject)
    {
        IButton button = missionAreaObject.GetComponentInChildren<IButton>(true);
        if (button != null)
        {
            button.onPointerClickEvent = new UnityEvent();
            button.onPointerOverEvent = new UnityEvent();
            button.onPointerEnterEvent = new UnityEvent();
            button.onPointerExitEvent = new UnityEvent();
            button.onPointerHoldEvent = new UnityEvent();

            button.onPointerClickEvent.AddListener(OpenMissionPanel);
            button.onPointerOverEvent.AddListener(SetMissionPromptText);
            button.onPointerEnterEvent.AddListener(ShowInfoPrompt);
            button.onPointerExitEvent.AddListener(HideInfoPrompt);
        }

        Image icon = missionAreaObject.transform.Find("Setting Icon")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.gameObject.name = "Mission Icon";
            if (missionButtonIcon != null)
                icon.sprite = missionButtonIcon;
            icon.preserveAspect = true;

            icon.rectTransform.anchoredPosition = Vector2.zero;
            icon.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 50);
            icon.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 40);
        }

        Image background = missionAreaObject.transform.Find("Background Button")?.GetComponent<Image>();
        if (background != null)
        {
            background.color = new Color(background.color.r, background.color.g, background.color.b, 0);
        }

        Shadow backgroundShadow = missionAreaObject.transform.Find("Background Button")?.GetComponent<Shadow>();
        if (backgroundShadow != null)
            backgroundShadow.enabled = false;

        Text label = missionAreaObject.GetComponentsInChildren<Text>(true).FirstOrDefault();
        if (label != null)
            label.gameObject.SetActive(false);
    }

    private void OpenMissionPanel()
    {
        OpenPanel(MissionPanelName);
    }

    private void SetMissionPromptText()
    {
        SetInfoPromptText(MissionPromptText);
    }

    private void ShowInfoPrompt()
    {
        SetInfoPromptActive(true);
    }

    private void HideInfoPrompt()
    {
        SetInfoPromptActive(false);
    }
    */
}
