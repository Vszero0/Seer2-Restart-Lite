using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MissionContentView : Module
{
    [SerializeField] private IText titleText;
    [SerializeField] private Text contentText;
    [SerializeField] private List<PetItemBlockView> itemBlockViews;

    public void SetMission(Mission mission) {
        if (mission == null) {
            Clear();
            return;
        }
        titleText?.SetText(mission.info?.title ?? string.Empty);
        contentText?.SetText(mission.checkpointInfo?.description ?? string.Empty);
        var rewards = mission.info?.rewards ?? new List<Item>();
        for (int i = 0; i < itemBlockViews.Count; i++) {
            Item item = (i < rewards.Count) ? rewards[i] : null;
            itemBlockViews[i]?.SetItem(item);
        }
    }

    public void Clear() {
        titleText?.SetText(string.Empty);
        contentText?.SetText(string.Empty);
        foreach (var block in itemBlockViews) {
            block?.SetItem(null);
        }
    }

}
