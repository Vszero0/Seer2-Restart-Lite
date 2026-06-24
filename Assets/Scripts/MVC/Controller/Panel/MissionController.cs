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
            return;
        
        missionModel.SetFilterType(type);
        OnSetMissionList();
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
            if (missionInfo != null && missionInfo.type == MissionType.Mod)
                OpenModMissionError("找不到对应的任务节点");

            return;
        }

        if (checkpoint.hasStory) {
            if (!StoryPanel.CanOpenStory(checkpoint.storyId, out string error))
            {
                if (missionInfo != null && missionInfo.type == MissionType.Mod)
                    OpenModMissionError(error);

                return;
            }

            StoryPanel.Open(checkpoint.storyId, checkpoint.mapId);
            return;
        }

        if (missionInfo != null && missionInfo.type == MissionType.Mod)
        {
            OpenModMissionError("该Mod任务未配置剧情脚本");
            return;
        }

        TeleportHandler.Teleport(checkpoint.mapId);
    }

    private void OpenModMissionError(string detail)
    {
        string message = "加载Mod任务失败，可能为未加载Mod、该Mod未制作对应剧情，或剧情文件存在错误";
        if (!string.IsNullOrEmpty(detail))
            message += "\n" + detail;

        Hintbox.OpenHintboxWithContent(message, 16).SetSize(640, 320);
    }

}
