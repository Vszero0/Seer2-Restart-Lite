using System;
using System.Xml.Serialization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[XmlRoot("mission")]
public class MissionInfo
{
    [XmlAttribute] public int id;
    [XmlAttribute("type")] public int typeId;
    [XmlIgnore] public MissionType type => (MissionType)typeId;
    [XmlAttribute] public bool replayable = false;
    [XmlAttribute] public string rewardMode = "once";
    [XmlAttribute] public bool autoCompleteStory = false;
    [XmlIgnore] public bool rewardEveryCompletion => string.Equals(
        rewardMode, "always", StringComparison.OrdinalIgnoreCase);

    [XmlElement("preMission")] public string preMissionId;    // 前置任務
    [XmlElement("nextMission")] public string nextMissionId;  // 解鎖任務

    [XmlIgnore] public List<int> preMissions => preMissionId.ToIntList();
    [XmlIgnore] public List<int> nextMissions => nextMissionId.ToIntList();


    [XmlElement("title")] public string title;
    
    [XmlArray("reward"), XmlArrayItem(typeof(Item), ElementName = "item")] 
    public List<Item> rewards = new List<Item>();

    [XmlArray("checkpoint"), XmlArrayItem(typeof(MissionCheckpoint), ElementName = "branch")] 
    public List<MissionCheckpoint> checkpoints = new List<MissionCheckpoint>();
}

public class MissionCheckpoint {
    [XmlAttribute] public string id;
    [XmlElement("map")] public int mapId;
    [XmlElement("story")] public string storyId;
    [XmlElement("description")] public string intro;
    [XmlIgnore] public string description => intro.GetDescription();
    [XmlIgnore] public bool hasStory => !string.IsNullOrEmpty(storyId);
}

public enum MissionType {
    None = -1,
    Main = 0,
    Side = 1,
    Daily = 2,
    Event = 3,
    Mod = 4,
}
