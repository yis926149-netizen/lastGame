using UnityEngine;
using UnityEngine.Audio;
using System.Collections.Generic;
using System.Linq;
using System;

namespace UIToolkitDemo
{
    // Serves two APIs:
    // 1) Static convenience methods for UI-toolkit sample screens (PlayDefaultButtonSound etc.)
    // 2) Instance name-based playback for the game (PlaySFX/PlayBGM/StopBGM) via clip lists

    public class AudioManager : MonoBehaviour
    {
        // AudioMixerGroup names
        public static string MusicGroup = "Music";
        public static string SfxGroup = "SFX";

        // parameter suffix
        const string k_Parameter = "Volume";

        [SerializeField] AudioMixer m_MainAudioMixer;

        // basic range of UI sound clips
        [Header("UI Sounds")]
        [Tooltip("General button click.")]
        [SerializeField] AudioClip m_DefaultButtonSound;
        [Tooltip("General button click.")]
        [SerializeField] AudioClip m_AltButtonSound;
        [Tooltip("General shop purchase clip.")]
        [SerializeField] AudioClip m_TransactionSound;
        [Tooltip("General error sound.")]
        [SerializeField] AudioClip m_DefaultWarningSound;

        [Header("Game Sounds")]
        [Tooltip("Level up or level win sound.")]
        [SerializeField] AudioClip m_VictorySound;
        [Tooltip("Level defeat sound.")]
        [SerializeField] AudioClip m_DefeatSound;
        [SerializeField] AudioClip m_PotionSound;

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

        void Start()
        {
            // 获取当前场景的名称
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentSceneName == "StartScene")
            {
                PlayBGM("Place_Village_Loop");
            }
        }

        void OnEnable()
        {
            SettingsEvents.SettingsUpdated += OnSettingsUpdated;

            GameplayEvents.SettingsUpdated += OnSettingsUpdated;
        }

        void OnDisable()
        {
            SettingsEvents.SettingsUpdated -= OnSettingsUpdated;

            GameplayEvents.SettingsUpdated -= OnSettingsUpdated;
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
        //  BGM (game API)
        // =========================

        public void PlayBGM(string name, bool loop = true)
        {
            if (string.IsNullOrEmpty(name) || bgmDict == null || !bgmDict.TryGetValue(name, out AudioClip clip))
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
            if (bgmSource != null)
                bgmSource.Stop();
        }

        // =========================
        // SFX (game API)
        // =========================

        public void PlaySFX(string name)
        {
            if (string.IsNullOrEmpty(name) || sfxDict == null || !sfxDict.TryGetValue(name, out AudioClip clip))
            {
                Debug.LogWarning("SFX 没找到: " + name);
                return;
            }

            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        // =========================
        //  音量 (game API)
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

        // =========================
        //  Static convenience API (sample screens)
        // =========================

        // plays one-shot audio
        public static void PlayOneSFX(AudioClip clip, Vector3 sfxPosition)
        {
            if (clip == null)
                return;

            GameObject sfxInstance = new GameObject(clip.name);
            sfxInstance.transform.position = sfxPosition;

            AudioSource source = sfxInstance.AddComponent<AudioSource>();
            source.clip = clip;
            source.Play();

            // set the mixer group (e.g. music, sfx, etc.)
            source.outputAudioMixerGroup = GetAudioMixerGroup(SfxGroup);

            // destroy after clip length
            Destroy(sfxInstance, clip.length);
        }

        // return an AudioMixerGroup by name
        public static AudioMixerGroup GetAudioMixerGroup(string groupName)
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();

            if (audioManager == null)
                return null;

            if (audioManager.m_MainAudioMixer == null)
                return null;

            AudioMixerGroup[] groups = audioManager.m_MainAudioMixer.FindMatchingGroups(groupName);

            foreach (AudioMixerGroup match in groups)
            {
                if (match.ToString() == groupName)
                    return match;
            }
            return null;

        }
        // convert linear value between 0 and 1 to decibels
        public static float GetDecibelValue(float linearValue)
        {
            // commonly used for linear to decibel conversion
            float conversionFactor = 20f;

            float decibelValue = (linearValue != 0) ? conversionFactor * Mathf.Log10(linearValue) : -144f;
            return decibelValue;
        }

        // convert decibel value to a range between 0 and 1
        public static float GetLinearValue(float decibelValue)
        {
            float conversionFactor = 20f;

            return Mathf.Pow(10f, decibelValue / conversionFactor);

        }

        // converts linear value between 0 and 1 into decibels and sets AudioMixer level
        public static void SetVolume(string groupName, float linearValue)
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager == null)
                return;

            float decibelValue = GetDecibelValue(linearValue);

            if (audioManager.m_MainAudioMixer != null)
            {
                audioManager.m_MainAudioMixer.SetFloat(groupName, decibelValue);
            }
        }

        // returns a value between 0 and 1 based on the AudioMixer's decibel value
        public static float GetVolume(string groupName)
        {

            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager == null)
                return 0f;

            float decibelValue = 0f;
            if (audioManager.m_MainAudioMixer != null)
            {
                audioManager.m_MainAudioMixer.GetFloat(groupName, out decibelValue);
            }
            return GetLinearValue(decibelValue);
        }

        // convenient methods for playing a range of pre-defined sounds
        public static void PlayDefaultButtonSound()
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager == null)
                return;

            PlayOneSFX(audioManager.m_DefaultButtonSound, Vector3.zero);
        }

        public static void PlayAltButtonSound()
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager == null)
                return;

            PlayOneSFX(audioManager.m_AltButtonSound, Vector3.zero);
        }

        public static void PlayDefaultTransactionSound()
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager == null)
                return;

            PlayOneSFX(audioManager.m_TransactionSound, Vector3.zero);
        }

        public static void PlayDefaultWarningSound()
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager == null)
                return;

            PlayOneSFX(audioManager.m_DefaultWarningSound, Vector3.zero);
        }
        public static void PlayVictorySound()
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager == null)
                return;

            PlayOneSFX(audioManager.m_VictorySound, Vector3.zero);
        }

        public static void PlayDefeatSound()
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager == null)
                return;

            PlayOneSFX(audioManager.m_DefeatSound, Vector3.zero);
        }

        public static void PlayPotionDropSound()
        {
            AudioManager audioManager = FindObjectOfType<AudioManager>();
            if (audioManager == null)
                return;

            PlayOneSFX(audioManager.m_PotionSound, Vector3.zero);
        }

        // event-handling methods
        void OnSettingsUpdated(GameData gameData)
        {
            // use the gameData to set the music and sfx volume
            SetVolume(MusicGroup + k_Parameter, gameData.musicVolume / 100f);
            SetVolume(SfxGroup + k_Parameter, gameData.sfxVolume / 100f);
        }
    }
}
