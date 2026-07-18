using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MissionModel : Module
{
    private List<Mission> storage;
    private readonly Dictionary<MissionType, int> selectedIndices = new Dictionary<MissionType, int>();
    public List<Mission> selections { get; private set; }
    public MissionType type { get; private set; }
    public int id { get; private set; }
    public Mission currentMission => selections != null && id.IsInRange(0, selections.Count) ? selections[id] : null;

    public void SetStorage(List<Mission> storage, MissionType type = MissionType.Main) {
        this.storage = storage;
        SetFilterType(type);
    }

    public void SetFilterType(MissionType type) {
        if (selections != null)
            selectedIndices[this.type] = id;

        this.type = type;
        selections = storage != null
            ? storage.FindAll(x => x.info != null && x.info.type == type)
            : new List<Mission>();

        int rememberedIndex = selectedIndices.TryGetValue(type, out int index) ? index : 0;
        id = selections.Count > 0 ? Mathf.Clamp(rememberedIndex, 0, selections.Count - 1) : -1;
    }

    public void Select(int index) {
        id = selections != null && index.IsInRange(0, selections.Count) ? index : -1;
        selectedIndices[type] = id;
    }

}
