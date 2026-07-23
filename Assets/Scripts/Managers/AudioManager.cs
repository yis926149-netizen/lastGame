using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    [Header("Audio Sources")]
    public AudioSource bgmSource;
    public AudioSource sfxSource;

    [Header("音频Clips")]
    public List<AudioClip> bgmClips;
    public List<AudioClip> sfxClips;

    private Dictionary<string, AudioClip> bgmDict;
    private Dictionary<string, AudioClip> sfxDict;

    [Header("音量")]
    [Range(0, 1)] public float bgmVolume = 0.7f;
    [Range(0, 1)] public float sfxVolume = 1f;

    void Awake()
    {
        EnsureSeparateAudioSources();
        InitDictionary();
    }

    private void EnsureSeparateAudioSources()
    {
        if (bgmSource == null)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            Debug.LogWarning("[AudioManager] BGM AudioSource was missing and has been created.", this);
        }

        if (sfxSource == null || sfxSource == bgmSource)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            Debug.LogWarning("[AudioManager] SFX requires a separate AudioSource; a dedicated source has been created.", this);
        }

        bgmSource.playOnAwake = false;
        sfxSource.playOnAwake = false;
    }

    private void Start()
    {
        // 获取当前场景的名称
        string currentSceneName = SceneManager.GetActiveScene().name;
        if (currentSceneName == "StartScene")
        {
            PlayBGM("Place_Village_Loop");
        }

    }

    void InitDictionary()
    {
        bgmDict = new Dictionary<string, AudioClip>();
        RegisterClips(bgmClips, bgmDict, "BGM");

        sfxDict = new Dictionary<string, AudioClip>();
        RegisterClips(sfxClips, sfxDict, "SFX");
    }

    private void RegisterClips(List<AudioClip> clips, Dictionary<string, AudioClip> target, string channel)
    {
        if (clips == null)
        {
            Debug.LogWarning($"[AudioManager] {channel} clip list is not configured.", this);
            return;
        }

        foreach (AudioClip clip in clips)
        {
            if (clip == null)
            {
                Debug.LogWarning($"[AudioManager] {channel} clip list contains a missing reference.", this);
                continue;
            }

            if (target.ContainsKey(clip.name))
            {
                Debug.LogWarning($"[AudioManager] Duplicate {channel} clip name: {clip.name}. The last clip will be used.", this);
            }

            target[clip.name] = clip;
        }
    }

    // =========================
    //  BGM
    // =========================

    public void PlayBGM(string name, bool loop = true)
    {
        if (string.IsNullOrEmpty(name) || !bgmDict.TryGetValue(name, out AudioClip clip))
        {
            Debug.LogWarning("BGM 没找到: " + name);
            return;
        }

        bgmSource.clip = clip;
        bgmSource.loop = loop;
        bgmSource.volume = bgmVolume;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        bgmSource.Stop();
    }

    // =========================
    // SFX
    // =========================

    public void PlaySFX(string name)
    {
        if (string.IsNullOrEmpty(name) || !sfxDict.TryGetValue(name, out AudioClip clip))
        {
            Debug.LogWarning("SFX 没找到: " + name);
            return;
        }

        sfxSource.PlayOneShot(clip, sfxVolume);
    }

    // =========================
    //  音量
    // =========================

    public void SetBGMVolume(float v)
    {
        bgmVolume = Mathf.Clamp01(v);
        bgmSource.volume = bgmVolume;
    }

    public void SetSFXVolume(float v)
    {
        sfxVolume = Mathf.Clamp01(v);
    }
}
