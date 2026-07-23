using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MissionPanel : Panel
{
    [SerializeField] private MissionController missionController;

    public override void Init()
    {
        base.Init();
        SetMissionStorage();
    }

    public void SetMissionStorage() {
        Database.instance.ReloadStoryMod();
        Mission.VersionUpdate();
        var storage = Mission.GetStorage().FindAll(x => x.info != null && (!x.isDone || x.info.replayable));
        missionController.SetMissionStorage(storage);
    }
}
