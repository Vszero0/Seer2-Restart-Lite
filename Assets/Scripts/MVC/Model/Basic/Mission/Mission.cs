using System.Linq;
using System.Xml.Serialization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class Mission
{
    public MissionInfo info => GetMissionInfo(id);
    public MissionCheckpoint checkpointInfo
    {
        get
        {
            if (info == null || info.checkpoints == null)
                return null;

            MissionCheckpoint checkpoint = info.checkpoints.Find(x => x.id == checkPointId);
            if (checkpoint == null && info.replayable)
                checkpoint = info.checkpoints.Find(x => x.id == "default");

            return checkpoint;
        }
    }

    [XmlAttribute] public int id;
    [XmlAttribute("checkpoint")] public string checkPointId;
    [XmlAttribute("done")] public bool isDone = false;

    public Mission() {}

    public Mission(int id) {
        this.id = id;
        checkPointId = "default";
        isDone = false;
    }

    public static MissionInfo GetMissionInfo(int id) {
        return Database.instance.GetMissionInfo(id);
    }

    public static List<Mission> Filter(Predicate<Mission> pred) {
        return Player.instance.gameData.missionStorage.FindAll(pred);
    }

    public static Mission Find(int id = 0) {
        id = (id == 0) ? Player.instance.currentMissionId : id;
        Mission mission = Player.instance.gameData.missionStorage.Find(x => x.id == id);
        return mission;
    }

    public static Mission Start(int id) {
        Mission mission = new Mission(id);
        MissionInfo missionInfo = mission.info;
        if (missionInfo == null)
            return null;

        if (!string.IsNullOrEmpty(missionInfo.preMissionId)) {
            foreach (var preId in missionInfo.preMissions) {
                Mission preMission = Mission.Find(preId);
                if ((preMission == null) || (!preMission.isDone)) {
                    return null;
                }
            }
        }
        Player.instance.gameData.missionStorage.Add(mission);
        return mission;
    }

    public static void Checkpoint(int id, string checkpointId) {
        Mission mission = Mission.Find(id);
        if (mission == null)
            return;

        mission.checkPointId = checkpointId;
    }

    public static List<Item> Complete(int id = 0) {
        List<Item> grantedRewards = new List<Item>();
        Mission mission = Mission.Find(id);
        if (mission == null)
            return grantedRewards;

        bool wasDone = mission.isDone;
        mission.isDone = true;
        mission.checkPointId = "complete";

        MissionInfo missionInfo = mission.info;
        if (missionInfo == null)
            return grantedRewards;

        if (!wasDone || missionInfo.rewardEveryCompletion)
        {
            foreach (Item reward in missionInfo.rewards ?? new List<Item>())
            {
                if (Item.IsNullOrEmpty(reward) || reward.info == null)
                    continue;

                Item granted = new Item(reward.info.getId, reward.num);
                Item.AddTo(granted, Item.itemStorage);
                grantedRewards.Add(granted);
            }
        }

        if (string.IsNullOrEmpty(missionInfo.nextMissionId))
            return grantedRewards;

        foreach (var nextId in missionInfo.nextMissions) {
            Mission.Start(nextId);
        }

        return grantedRewards;
    }

    public static void VersionUpdate() {
        var mainMission = Mission.Filter(x => x.info != null && x.info.type == MissionType.Main);
        if (mainMission.Count == 0)
            Mission.Start(1);
        else {
            Mission maxMainMission = mainMission.Aggregate((x, y) => x.id > y.id ? x : y);
            if (maxMainMission.isDone && GameManager.versionData.missionData.mainMissionCount > maxMainMission.id) {
                Mission.Start(maxMainMission.id + 1);
            }   
        }

        var sideMissions = Database.instance.missionInfos.FindAll(x =>
            ((x.type == MissionType.Side) || (x.type == MissionType.Daily)
                || (x.type == MissionType.Event) || (x.type == MissionType.Mod))
            && (Mission.Find(x.id) == null));

        foreach (var mission in sideMissions)
            Mission.Start(mission.id);
    }

    public static void DailyLogin() {
        var missionStorage = Player.instance.gameData.missionStorage;
        for (int i = missionStorage.Count - 1; i >= 0; i--) {
            var missionInfo = missionStorage[i].info;
            if (missionInfo == null) {
                missionStorage.RemoveAt(i);
                continue;
            }

            if (missionInfo.type != MissionType.Daily)
                continue;

            missionStorage[i] = new Mission(missionStorage[i].id);
        }
    }

}
