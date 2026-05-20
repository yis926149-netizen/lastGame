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
        InitDictionary();
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
        foreach (var clip in bgmClips)
            bgmDict[clip.name] = clip;

        sfxDict = new Dictionary<string, AudioClip>();
        foreach (var clip in sfxClips)
            sfxDict[clip.name] = clip;
    }

    // =========================
    //  BGM
    // =========================

    public void PlayBGM(string name, bool loop = true)
    {
        if (!bgmDict.ContainsKey(name))
        {
            Debug.LogWarning("BGM 没找到: " + name);
            return;
        }

        bgmSource.clip = bgmDict[name];
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
        if (!sfxDict.ContainsKey(name))
        {
            Debug.LogWarning("SFX 没找到: " + name);
            return;
        }

        sfxSource.PlayOneShot(sfxDict[name], sfxVolume);
    }

    // =========================
    //  音量
    // =========================

    public void SetBGMVolume(float v)
    {
        bgmVolume = v;
        bgmSource.volume = v;
    }

    public void SetSFXVolume(float v)
    {
        sfxVolume = v;
    }
}
