using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MissionController : Module
{
    [SerializeField] private MissionModel missionModel;
    [SerializeField] private MissionListView missionListView;
    [SerializeField] private MissionContentView missionContentView;

    private readonly IButton[] typeTabs = new IButton[5];
    private static readonly Color NormalTabTextColor = new Color32(82, 229, 249, 255);
    private static readonly Color SelectedTabTextColor = new Color32(255, 232, 71, 255);

    public override void Init() {
        CacheTypeTabs();
        RefreshTypeTabs();
    }

    public void SetMissionStorage(List<Mission> missions) {
        missionModel.SetStorage(missions, MissionType.Main);
        OnSetMissionList();
        RefreshTypeTabs();
    }

    public void OnSetMissionList() {
        missionListView.SetStorage(missionModel.selections, Select);
        if (missionModel.selections != null && missionModel.selections.Count > 0)
            Select(missionModel.id);
        else
        {
            missionModel.Select(-1);
            missionContentView?.SetEmptyState(GetEmptyTitle(missionModel.type), "当前分类还没有可用任务。");
        }
    }

    public void SetType(int index) {
        MissionType type = (MissionType)index;
        if (type == missionModel.type)
        {
            RefreshTypeTabs();
            if (type == MissionType.Mod && IsCurrentMissionListEmpty())
                OpenModMissionError("未找到可用的Mod剧情任务");

            return;
        }
        
        missionModel.SetFilterType(type);
        OnSetMissionList();
        RefreshTypeTabs();
        if (type == MissionType.Mod && IsCurrentMissionListEmpty())
            OpenModMissionError("未找到可用的Mod剧情任务");
    } 

    public void Select(int index) {
        missionModel.Select(index);
        missionContentView?.SetMission(missionModel.currentMission);
    }

    public void MissionStart() {
        if (missionModel.currentMission == null)
            return;

        var missionInfo = missionModel.currentMission.info;
        var checkpoint = missionModel.currentMission.checkpointInfo;
        if (checkpoint == null)
        {
            OpenStoryError("找不到对应的任务节点");
            return;
        }

        if (checkpoint.hasStory) {
            if (!StoryPanel.CanOpenStory(checkpoint.storyId, out string error))
            {
                OpenStoryError(error);
                return;
            }

            int boundMissionId = missionInfo != null && missionInfo.autoCompleteStory ? missionInfo.id : 0;
            StoryPanel.Open(checkpoint.storyId, fallbackMapId: 0, missionId: boundMissionId);
            return;
        }

        if (missionInfo != null && missionInfo.type == MissionType.Mod)
        {
            OpenModMissionError("该Mod任务未配置剧情脚本");
            return;
        }

        TeleportHandler.Teleport(checkpoint.mapId);
    }

    private void OpenStoryError(string detail)
    {
        string message = "加载剧情失败，可能是剧情文件不存在或格式错误";
        if (!string.IsNullOrEmpty(detail))
            message += "\n" + detail;

        Hintbox.OpenHintboxWithContent(message, 16).SetSize(640, 320);
    }

    private void OpenModMissionError(string detail)
    {
        string message = "加载Mod任务失败，可能为未加载Mod、该Mod未制作对应剧情，或剧情文件存在错误";
        if (!string.IsNullOrEmpty(detail))
            message += "\n" + detail;

        Hintbox.OpenHintboxWithContent(message, 16).SetSize(640, 320);
    }

    private bool IsCurrentMissionListEmpty()
    {
        return missionModel.selections == null || missionModel.selections.Count == 0;
    }

    private void CacheTypeTabs()
    {
        Transform menu = transform.parent != null ? transform.parent.Find("Menu") : null;
        if (menu == null)
            return;

        typeTabs[(int)MissionType.Main] = menu.Find("Main")?.GetComponent<IButton>();
        typeTabs[(int)MissionType.Side] = menu.Find("Side")?.GetComponent<IButton>();
        typeTabs[(int)MissionType.Daily] = menu.Find("Daily")?.GetComponent<IButton>();
        typeTabs[(int)MissionType.Event] = menu.Find("Event")?.GetComponent<IButton>();
        typeTabs[(int)MissionType.Mod] = menu.Find("Mod")?.GetComponent<IButton>();
    }

    private void RefreshTypeTabs()
    {
        if (typeTabs[0] == null)
            CacheTypeTabs();

        for (int i = 0; i < typeTabs.Length; i++)
        {
            IButton tab = typeTabs[i];
            if (tab == null)
                continue;

            bool selected = i == (int)missionModel.type;
            Button button = tab.button;
            button.transition = selected ? Selectable.Transition.None : Selectable.Transition.SpriteSwap;
            tab.image.sprite = selected && button.spriteState.highlightedSprite != null
                ? button.spriteState.highlightedSprite
                : tab.initSprite;

            TMP_Text label = tab.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.color = selected ? SelectedTabTextColor : NormalTabTextColor;
        }
    }

    private static string GetEmptyTitle(MissionType type)
    {
        return type switch
        {
            MissionType.Main => "暂无主线任务",
            MissionType.Side => "暂无支线任务",
            MissionType.Daily => "暂无日常任务",
            MissionType.Event => "暂无活动任务",
            MissionType.Mod => "暂无 Mod 任务",
            _ => "暂无任务",
        };
    }

}
