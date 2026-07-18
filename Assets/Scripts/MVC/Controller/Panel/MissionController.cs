using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MissionController : Module
{
    [SerializeField] private MissionModel missionModel;
    [SerializeField] private MissionListView missionListView;
    [SerializeField] private MissionContentView missionContentView;

    public void SetMissionStorage(List<Mission> missions) {
        missionModel.SetStorage(missions, MissionType.Main);
        OnSetMissionList();
    }

    public void OnSetMissionList() {
        missionListView.SetStorage(missionModel.selections, Select);
        Select(0);
    }

    public void SetType(int index) {
        MissionType type = (MissionType)index;
        if (type == missionModel.type)
        {
            if (type == MissionType.Mod && IsCurrentMissionListEmpty())
                OpenModMissionError("未找到可用的Mod剧情任务");

            return;
        }
        
        missionModel.SetFilterType(type);
        OnSetMissionList();
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
            StoryPanel.Open(checkpoint.storyId, checkpoint.mapId, boundMissionId);
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

}
