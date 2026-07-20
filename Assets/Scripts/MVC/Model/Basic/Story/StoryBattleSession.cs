using System;

/// <summary>
/// 跨 Battle / Map 场景保存一次剧情战斗的内存状态。不会写入玩家存档。
/// </summary>
public sealed class StoryBattleSession
{
    public static StoryBattleSession current;

    public StoryScript story;
    public int commandIndex;
    public int fallbackMapId;
    public int boundMissionId;
    public bool boundMissionCompleted;
    public string pointId;
    public string commandId;
    public AudioSystem.MusicPlaybackSnapshot musicSnapshot;
    public bool hasChangedMusicIdentity;
    public bool hasRestartedMusic;

    public static bool TryConsumeBattleResult(Battle battle)
    {
        if (current == null || battle?.result == null || !battle.result.isBattleEnd)
            return false;

        StoryBattleSession session = current;
        current = null;
        session.story?.battleHistory.Add(new StoryBattleHistoryEntry
        {
            pointId = session.pointId,
            commandId = session.commandId,
            result = battle.result.isMyWin ? "win" : battle.result.isOpWin ? "lose" : "other",
        });
        battle.info = null;
        Player.instance.currentNpcId = 0;
        StoryPanel.OpenResume(session);
        return true;
    }
}
