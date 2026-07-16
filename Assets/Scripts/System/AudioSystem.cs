using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioSystem : Singleton<AudioSystem>
{
    public sealed class MusicPlaybackSnapshot
    {
        internal AudioClip clip;
        internal float time;
        internal float volume;
        internal bool isPlaying;
        internal string identity;
    }

    private SettingsData settingsData => Player.instance.gameData.settingsData;
    [SerializeField] private AudioSource musicSource = null;
    [SerializeField] private AudioSource soundSource = null;
    [SerializeField] private AudioSource effectSource = null;
    private string currentMusicIdentity;

    public string CurrentMusicIdentity => currentMusicIdentity;
    
    public float GetVolume(AudioVolumeType volumeType) {
        float volume = volumeType switch {
            AudioVolumeType.BGM => settingsData.BGMVolume,
            AudioVolumeType.UI => settingsData.UIVolume,
            AudioVolumeType.BattleBGM => settingsData.battleBGMVolume,
            AudioVolumeType.BattleSE => settingsData.battleSEVolume,
            _ => 10f,
        };

        return volume / 10f;
    }

    // BGM
    public void PlayMusic(AudioClip clip, AudioVolumeType volumeType = AudioVolumeType.BGM) {
        PlayMusicTracked(clip, volumeType, null);
    }

    public void PlayMusic(AudioClip clip, AudioVolumeType volumeType, string identity) {
        PlayMusicTracked(clip, volumeType, identity);
    }

    public bool PlayMusicTracked(AudioClip clip, AudioVolumeType volumeType = AudioVolumeType.BGM, string identity = null) {
        if (clip == null) {
            bool changed = musicSource.clip != null || musicSource.isPlaying;
            musicSource.Stop();
            currentMusicIdentity = null;
            return changed;
        }
        var volume = GetVolume(volumeType);
        string resolvedIdentity = string.IsNullOrWhiteSpace(identity) ? BuildClipMusicIdentity(clip) : identity;
        if ((clip == musicSource.clip) && (volume == musicSource.volume) && musicSource.isPlaying) {
            currentMusicIdentity = resolvedIdentity;
            return false;
        }
    
        musicSource.clip = clip;
        musicSource.volume = volume;
        currentMusicIdentity = resolvedIdentity;
        musicSource.Play();
        return true;
    }

    public MusicPlaybackSnapshot CaptureMusic()
    {
        if (musicSource == null)
            return null;

        return new MusicPlaybackSnapshot
        {
            clip = musicSource.clip,
            time = musicSource.clip == null ? 0f : musicSource.time,
            volume = musicSource.volume,
            isPlaying = musicSource.isPlaying,
            identity = currentMusicIdentity,
        };
    }

    public bool TryRestoreMusic(MusicPlaybackSnapshot snapshot, string expectedCurrentIdentity, bool restorePlayback = true)
    {
        if (snapshot == null || musicSource == null)
            return false;
        if (!string.IsNullOrWhiteSpace(expectedCurrentIdentity)
            && !string.Equals(currentMusicIdentity, expectedCurrentIdentity, System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!restorePlayback)
        {
            currentMusicIdentity = snapshot.identity;
            return true;
        }

        musicSource.Stop();
        musicSource.clip = snapshot.clip;
        musicSource.volume = snapshot.volume;
        currentMusicIdentity = snapshot.identity;
        if (snapshot.clip == null)
            return true;

        musicSource.time = Mathf.Clamp(snapshot.time, 0f, Mathf.Max(0f, snapshot.clip.length - .01f));
        if (snapshot.isPlaying)
            musicSource.Play();
        return true;
    }

    public static string BuildMapMusicIdentity(Map map)
    {
        if (map?.music != null && !string.IsNullOrWhiteSpace(map.music.bgm))
            return "map-bgm:" + map.music.category + "/" + map.music.bgm.Trim().ToLowerInvariant();
        return BuildClipMusicIdentity(map?.resources?.bgm);
    }

    public static string BuildResourceMusicIdentity(string source, string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
            return null;
        string normalizedSource = string.IsNullOrWhiteSpace(source) ? "auto" : source.Trim().ToLowerInvariant();
        string normalizedPath = resourcePath.Trim().Replace('\\', '/').ToLowerInvariant();
        return "resource:" + normalizedSource + ":" + normalizedPath;
    }

    private static string BuildClipMusicIdentity(AudioClip clip)
    {
        return clip == null ? null : "clip:" + (clip.name ?? string.Empty).Trim().ToLowerInvariant();
    }

    public void StopMusic() {
        if (musicSource == null)
            return;

        if (musicSource.clip != null || musicSource.isPlaying)
            musicSource.Stop();
        currentMusicIdentity = null;
    }

    // 一次性音效
    public void PlaySound(AudioClip clip, AudioVolumeType volumeType = AudioVolumeType.UI) {
        if (clip == null)
            return;
        
        soundSource.clip = clip;
        soundSource.volume = GetVolume(volumeType);
        soundSource.PlayOneShot(clip);
    }

    // 环境音效等特别音效
    public void PlayEffect(AudioClip clip, AudioVolumeType volumeType = AudioVolumeType.BGM) {
        if (clip == null) {
            StopEffect();
            return;
        }

        effectSource.clip = clip;
        effectSource.volume = GetVolume(volumeType);
        effectSource.Play();
    }

    public void StopEffect() {
        if ((effectSource == null) || (effectSource.clip == null))
            return;

        effectSource.Stop();
    }
}

public enum AudioVolumeType {
    None, BGM, UI, BattleBGM, BattleSE,
}
