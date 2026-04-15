using UnityEngine;
using System.Collections.Generic;

namespace HoiAnLantern
{
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Settings")]
    public int sfxPoolSize = 10;  // số AudioSource cho SFX
    public AudioClip[] sfxClips;  // gán clip trong Inspector
    public AudioClip bgmClip;     // nhạc nền

    private Dictionary<string, AudioClip> sfxDict;
    private List<AudioSource> sfxPool;
    private AudioSource musicSource;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // DontDestroyOnLoad requires a root GameObject
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
            InitAudioManager();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void InitAudioManager()
    {
        // Tạo dictionary
        sfxDict = new Dictionary<string, AudioClip>();
        foreach (var clip in sfxClips)
        {
            if (clip != null && !sfxDict.ContainsKey(clip.name))
                sfxDict.Add(clip.name, clip);
        }

        // Tạo AudioSource cho music
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        if (bgmClip != null)
        {
            musicSource.clip = bgmClip;
            musicSource.Play();
        }

        // Tạo pool AudioSource cho SFX
        sfxPool = new List<AudioSource>();
        for (int i = 0; i < sfxPoolSize; i++)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            sfxPool.Add(src);
        }
    }

    /// <summary>
    /// Play SFX theo tên
    /// </summary>
    public void PlaySFX(string sfxName, float volume = 1f)
    {
        if (!sfxDict.ContainsKey(sfxName)) return;

        AudioClip clip = sfxDict[sfxName];

        // Tìm AudioSource rảnh
        foreach (var src in sfxPool)
        {
            if (!src.isPlaying)
            {
                src.clip = clip;
                src.volume = volume;
                src.Play();
                return;
            }
        }

        // Nếu tất cả bận → ghi đè AudioSource đầu tiên
        sfxPool[0].clip = clip;
        sfxPool[0].volume = volume;
        sfxPool[0].Play();
    }

    public void StopSFX(string sfxName)
    {
        foreach (var src in sfxPool)
        {
            if (src.isPlaying && src.clip != null && src.clip.name == sfxName)
                src.Stop();
        }
    }

    public void PlayMusic(AudioClip clip)
    {
        if (clip == null) return;
        musicSource.clip = clip;
        musicSource.Play();
    }

    public void SetMusicVolume(float volume)
    {
        musicSource.volume = volume;
    }

    public void SetSFXVolume(float volume)
    {
        foreach (var src in sfxPool)
            src.volume = volume;
    }
}
} // namespace HoiAnLantern
