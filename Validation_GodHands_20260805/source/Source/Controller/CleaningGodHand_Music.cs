using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    public static partial class CleaningGodHandController
    {
        private static AudioSource currentMusicSource;
        private static List<AudioClip> musicPlaylist = new List<AudioClip>();
        private static List<AudioClip> wowSounds = new List<AudioClip>();

        private static int currentMusicIndex = -1;
        private static bool isMusicPlaying = false;
        private static bool isInitialized = false;
        private static bool wowSoundsLoaded = false;

        private const float WOW_SOUND_CHANCE = 0.02f;
        private static float currentWowChance = WOW_SOUND_CHANCE;
        private const float WOW_CHANCE_INCREMENT = 0.02f;

        // 初始化背景音乐列表
        public static void InitializeMusicPlaylist()
        {
            if (isInitialized) return;
            try
            {
                string modRoot = GetModRootDirectory();
                if (modRoot != null)
                {
                    LoadNormalMusic(modRoot);
                    LoadWowSound(modRoot);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[清洁] 初始化音乐失败 {e.Message}");
            }
            isInitialized = true;
        }

        // 定位Mod根目录
        private static string GetModRootDirectory()
        {
            return LoadedModManager.RunningMods.FirstOrDefault(
                m => m.Name.Contains("神之手") || m.PackageId.Contains("godhands"))?.RootDir;
        }

        // 加载常规音乐文件
        private static void LoadNormalMusic(string modRoot)
        {
            string path = Path.Combine(modRoot, "Sounds", "CleaningMusic");
            if (!Directory.Exists(path)) return;

            foreach (string file in ScanAudioFiles(path))
                LoadAudioClip(file, "CleaningMusic/", musicPlaylist);
        }

        // 加载彩蛋音效文件
        private static void LoadWowSound(string modRoot)
        {
            string path = Path.Combine(modRoot, "Sounds", "CleaningMusic", "wow");
            if (!Directory.Exists(path)) return;

            foreach (string file in ScanAudioFiles(path))
                LoadAudioClip(file, "CleaningMusic/wow/", wowSounds);
            wowSoundsLoaded = wowSounds.Count > 0;
        }

        // 检索支持的音频格式
        private static List<string> ScanAudioFiles(string folder)
        {
            List<string> results = new List<string>();
            string[] exts = { "*.mp3", "*.ogg", "*.wav" };
            foreach (var ext in exts) results.AddRange(Directory.GetFiles(folder, ext));
            return results;
        }

        // 加载单个音频片段
        private static void LoadAudioClip(string path, string prefix, List<AudioClip> list)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            AudioClip clip = ContentFinder<AudioClip>.Get(prefix + name, false);
            if (clip != null) list.Add(clip);
        }

        // 开始播放逻辑
        public static void StartMusic()
        {
            if (!GodHandModMain.Settings.cleaningPlayMusic) return;
            InitializeMusicPlaylist();

            if (musicPlaylist.Count == 0) return;
            if (isMusicPlaying && currentMusicSource?.isPlaying == true) return;

            PlayNextTrack(true);
        }

        // 停止并清理播放器
        public static void StopMusic()
        {
            if (currentMusicSource != null)
            {
                currentMusicSource.Stop();
                GameObject.Destroy(currentMusicSource.gameObject);
                currentMusicSource = null;
            }
            isMusicPlaying = false;
            CleanupProcessedThings();
            IncrementWowChance();
        }

        // 轮询更新播放状态
        public static void UpdateMusic()
        {
            if (!GodHandModMain.Settings.cleaningPlayMusic)
            {
                if (isMusicPlaying) StopMusic();
                return;
            }
            if (!isMusicPlaying || currentMusicSource == null) return;
            if (!currentMusicSource.isPlaying) PlayNextTrack(false);
        }

        // 切换下一首音轨
        private static void PlayNextTrack(bool firstPlay)
        {
            AudioClip clip = SelectNextClip();
            if (clip != null) PlayClip(clip);
        }

        // 随机选择曲目
        private static AudioClip SelectNextClip()
        {
            if (ShouldPlayWowSound())
            {
                currentWowChance = WOW_SOUND_CHANCE;
                return wowSounds[UnityEngine.Random.Range(0, wowSounds.Count)];
            }

            IncrementWowChance();
            if (musicPlaylist.Count == 0) return null;

            int nextIndex;
            do
            {
                nextIndex = UnityEngine.Random.Range(0, musicPlaylist.Count);
            } while (musicPlaylist.Count > 1 && nextIndex == currentMusicIndex);

            currentMusicIndex = nextIndex;
            return musicPlaylist[currentMusicIndex];
        }

        // 是否触发特效音
        private static bool ShouldPlayWowSound() => wowSoundsLoaded && wowSounds.Count > 0 && Rand.Chance(currentWowChance);

        // 递增彩蛋触发率
        private static void IncrementWowChance()
        {
            if (wowSoundsLoaded)
                currentWowChance = Mathf.Min(1.0f, currentWowChance + WOW_CHANCE_INCREMENT);
        }

        // 实例化组件并播放
        private static void PlayClip(AudioClip clip)
        {
            if (currentMusicSource == null)
            {
                // 查找是否已存在同名播放器（防止存档加载残留）
                GameObject oldGo = GameObject.Find("CleaningMusicPlayer");
                if (oldGo != null) GameObject.Destroy(oldGo);

                GameObject go = new GameObject("CleaningMusicPlayer");
                GameObject.DontDestroyOnLoad(go);
                currentMusicSource = go.AddComponent<AudioSource>();
            }

            // 硬编码音量
            currentMusicSource.clip = clip;
            currentMusicSource.volume = 0.4f;
            currentMusicSource.mute = false;
            currentMusicSource.loop = false;
            currentMusicSource.Play();
            isMusicPlaying = true;
        }
    }
}
