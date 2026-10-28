
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using BMSLib;

namespace MusynxAutoplay
{
    [BepInPlugin(Guid, "MUSYNX Autoplay", "0.1.0")]
    public class AutoplayPlugin : BaseUnityPlugin
    {
        public const string Guid = "local.musynx.autoplay";
        internal static ManualLogSource Log;
        internal static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<string> CfgMode;
        internal static ConfigEntry<float> CfgOffsetMs;
        internal static ConfigEntry<KeyCode> CfgToggle;
        internal static ConfigEntry<bool> CfgBlockUpload;
        internal static ConfigEntry<bool> CfgVerbose;
        internal static ConfigEntry<bool> CfgDevTest;
        internal static ConfigEntry<bool> CfgIgnoreFocus;
        internal static ConfigEntry<int> CfgPort;
        internal static ConfigEntry<KeyCode> CfgFullscreenKey;

        internal static bool Enabled = true;
        static Harmony s_harmony;
        internal static Type[] PlayerTypes;
        static string s_lastScene = "";
        static bool s_summaryLogged;
        static bool s_devOpened;
        static bool s_devLaunched;
        static bool s_devQuit;
        static float s_lastResume;

        void Awake()
        {
            Log = Logger;
            Application.runInBackground = true;
            CfgEnabled = Config.Bind("General", "Enabled", true, "进入歌曲后自动打歌");
            CfgMode = Config.Bind("General", "Mode", "Native",
                "Native = 调用游戏自己的输入入口 AddButton(轨道)；Perfect = 直接以谱面时间触发判定");
            CfgOffsetMs = Config.Bind("General", "OffsetMs", 0f, "整体提前量(ms)，正值=更早触发");
            CfgToggle = Config.Bind("General", "ToggleKey", KeyCode.F9, "开关热键");
            CfgBlockUpload = Config.Bind("General", "BlockUpload", true, "拦截排行榜成绩上传");
            CfgVerbose = Config.Bind("General", "Verbose", false, "每次命中输出日志");
            CfgDevTest = Config.Bind("Dev", "AutoTest", false, "开发自测：自动进入一首歌并打完");
            CfgIgnoreFocus = Config.Bind("General", "IgnoreFocusPause", true, "切出窗口时不暂停（游戏默认失焦即暂停）");
            CfgPort = Config.Bind("General", "Port", 38091, "本机控制端口（工具端连接用）");
            CfgFullscreenKey = Config.Bind("General", "FullscreenKey", KeyCode.F11, "全屏/窗口切换热键");

            Enabled = CfgEnabled.Value;

            PlayerTypes = typeof(UI0_SongPlayer).Assembly.GetTypes()
                .Where(t => t.Name.EndsWith("_SongPlayer") && typeof(MonoBehaviour).IsAssignableFrom(t))
                .ToArray();
            Log.LogInfo("MUSYNX Autoplay: found " + PlayerTypes.Length + " song player types");
            CommandServer.Start(CfgPort.Value);

            try
            {
                s_harmony = new Harmony(Guid);
                s_harmony.PatchAll(typeof(PlayerUpdatePatch));
                s_harmony.PatchAll(typeof(FocusPatch));
                s_harmony.PatchAll(typeof(PausePatch));
                s_harmony.PatchAll(typeof(UploadPatch));
                Log.LogInfo("MUSYNX Autoplay: harmony patches applied");
            }
            catch (Exception e)
            {
                Log.LogError("patch failed: " + e);
            }
        }

        void Update()
        {
            try
            {
                if (Input.GetKeyDown(CfgFullscreenKey.Value)) ToggleFullscreen();

                if (Input.GetKeyDown(CfgToggle.Value))
                {
                    Enabled = !Enabled;
                    Log.LogInfo("Autoplay " + (Enabled ? "ON" : "OFF"));
                }

                CommandServer.Pump();

                string scene = SceneManager.GetActiveScene().name;
                if (scene != s_lastScene)
                {
                    s_lastScene = scene;
                    s_summaryLogged = false;
                    Log.LogInfo("scene -> " + scene);
                    if (scene.IndexOf("Settlement", StringComparison.Ordinal) >= 0) GameBridge.CaptureResult();
                }
                if (CfgDevTest.Value)
                {
                    DevTestTick(scene);
                    DevHeartbeat(scene);
                    TryResume();
                }
                if (!s_summaryLogged && scene.Contains("Settlement"))
                {
                    s_summaryLogged = true;
                    Log.LogInfo(string.Format("result: EX={0} EXACT={1} GREAT={2} RIGHT={3} MISS={4} COMBO={5}",
                        JudgeGrade.TotalEx, JudgeGrade.TotalExact, JudgeGrade.TotalGreat,
                        JudgeGrade.TotalRight, JudgeGrade.TotalMiss, JudgeGrade.TotalCombo));
                    if (CfgDevTest.Value)
                    {
                        s_devQuit = true;
                        StartCoroutine(QuitSoon());
                    }
                }
            }
            catch (Exception e) { Log.LogError(e); }
        }

        System.Collections.IEnumerator QuitSoon()
        {
            yield return new WaitForSeconds(6f);
            Log.LogInfo("dev test finished, quitting");
            Application.Quit();
        }

        static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static object Field(object obj, string name)
        {
            if (obj == null) return null;
            FieldInfo f = obj.GetType().GetField(name, Any);
            return f == null ? null : f.GetValue(obj);
        }

        static object StaticProp(string typeName, string prop)
        {
            Type t = typeof(UI0_SongPlayer).Assembly.GetType(typeName);
            if (t == null) return null;
            PropertyInfo p = t.GetProperty(prop, Any);
            return p == null ? null : p.GetValue(null, null);
        }

        static void SetStaticProp(string typeName, string prop, object value)
        {
            Type t = typeof(UI0_SongPlayer).Assembly.GetType(typeName);
            PropertyInfo p = t == null ? null : t.GetProperty(prop, Any);
            if (p != null) p.SetValue(null, value, null);
        }

        static object Call(object obj, string name, params object[] args)
        {
            if (obj == null) return null;
            MethodInfo m = obj.GetType().GetMethod(name, Any);
            return m == null ? null : m.Invoke(obj, args);
        }

        static float s_nextBeat;

        void TryResume()
        {
            MonoBehaviour p = CurrentPlayer;
            if (p == null) return;
            if (Time.realtimeSinceStartup - s_lastResume < 2f) return;
            PlayerAccessor a = PlayerAccessor.For(p.GetType());
            if (a == null) return;
            PlayStatus st = (PlayStatus)a.PlayStatus.GetValue(p, null);
            if (st != PlayStatus.Paused) return;
            s_lastResume = Time.realtimeSinceStartup;
            Log.LogInfo("dev: auto resume (status paused)");
            if (a.PlayResume != null) a.PlayResume.Invoke(p, null);
        }


        void DevHeartbeat(string scene)
        {
            if (Time.realtimeSinceStartup < s_nextBeat) return;
            s_nextBeat = Time.realtimeSinceStartup + 4f;
            try
            {
                Type cType = typeof(UI0_SongPlayer).Assembly.GetType("UI0_SongPlayer");
                MonoBehaviour p = UnityEngine.Object.FindObjectOfType(typeof(UI0_SongPlayer)) as MonoBehaviour;
                if (p == null)
                {
                    p = UnityEngine.Object.FindObjectOfType(typeof(UIB_SongPlayer)) as MonoBehaviour;
                }
                if (p == null)
                {
                    Log.LogInfo(string.Format("beat scene={0} player=none timeScale={1}", scene, Time.timeScale));
                    return;
                }
                PlayerAccessor a = PlayerAccessor.For(p.GetType());
                if (a == null) { Log.LogInfo("beat accessor null"); return; }
                object st = a.PlayStatus.GetValue(p, null);
                long ticks = (long)a.PlayTicks.GetValue(p, null);
                IList[] tracks = a.NoteByTrack.GetValue(p) as IList[];
                int[] ki = a.KnockIndex.GetValue(p) as int[];
                int total = 0, idx = 0;
                if (tracks != null)
                {
                    for (int i = 0; i < tracks.Length; i++)
                    {
                        IList l = tracks[i];
                        if (l == null) continue;
                        total += l.Count;
                        if (ki != null && i < ki.Length) idx += ki[i];
                    }
                }
                Log.LogInfo(string.Format("beat scene={0} type={1} status={2} ticks={3} ({4:0.0}s) notes={5} idx={6} ts={7}",
                    scene, p.GetType().Name, st, ticks, ticks / 10000000.0, total, idx, Time.timeScale));
            }
            catch (Exception e) { Log.LogError("beat: " + e.Message); }
        }

        static bool ValidCore(object core)
        {
            if (core == null) return false;
            string scene = (string)Field(core, "playingSceneName");
            if (string.IsNullOrEmpty(scene)) return false;
            if (scene.IndexOf("PlayingScene", StringComparison.Ordinal) < 0) return false;
            if (!Application.CanStreamedLevelBeLoaded(scene)) return false;
            return true;
        }

        void DevTestTick(string scene)
        {
            try
            {
                if (!s_devOpened && scene == "OpeningScene")
                {
                    Type openerType = typeof(UI0_SongPlayer).Assembly.GetType("OpeningSpectrumScript");
                    UnityEngine.Object opener = openerType == null ? null : UnityEngine.Object.FindObjectOfType(openerType);
                    if (opener != null)
                    {
                        s_devOpened = true;
                        Log.LogInfo("dev: opening -> PlayGame");
                        Call(opener, "PlayGame");
                    }
                }

                if (!s_devLaunched && scene.Contains("SelectSong"))
                {
                    Type cType = typeof(UI0_SongPlayer).Assembly.GetType("SelectSongController");
                    UnityEngine.Object c = cType == null ? null : UnityEngine.Object.FindObjectOfType(cType);
                    if (c == null) return;

                    object pkg = Field(c, "selectSongInfo");
                    if (pkg == null)
                    {
                        IList pkgs = Field(c, "songInfoPackageEasyList") as IList;
                        if (pkgs != null && pkgs.Count > 0) pkg = pkgs[0];
                    }
                    if (pkg == null) { Log.LogWarning("dev: no package"); return; }

                    object core = Field(pkg, "songInfoCore");
                    if (!ValidCore(core))
                    {
                        core = null;
                        IList cores = Field(c, "songInfoCoreEasyList4T") as IList;
                        int seen = 0;
                        if (cores != null)
                        {
                            for (int k = 0; k < cores.Count; k++)
                            {
                                object cand = cores[k];
                                if (seen < 12)
                                {
                                    seen++;
                                    Log.LogInfo(string.Format("dev candidate[{0}] name='{1}' bms={2} scene={3}",
                                        k, Field(cand, "RealName"), Field(cand, "bmsCoreName"), Field(cand, "playingSceneName")));
                                }
                                if (ValidCore(cand)) { core = cand; break; }
                            }
                        }
                    }
                    if (core == null) { Log.LogWarning("dev: no playable core"); return; }

                    SetStaticProp("UserMemory", "ButtonNumber", 4);
                    SetStaticProp("UserMemory", "IsHard", 0);
                    SetStaticProp("UserMemory", "DisplayAdjustment", 0);
                    SetStaticProp("UserMemory", "JudgeCompensate", 0);
                    SetStaticProp("UserMemory", "SceneName", "SelectSongScene");

                    Call(pkg, "Reset", core);
                    Call(pkg, "CleanToPlay");
                    string target = (string)Field(core, "playingSceneName");
                    if (string.IsNullOrEmpty(target)) target = "UI0B_PlayingScene";
                    Log.LogInfo(string.Format("dev: song={0} bms={1} hard={2} len={3} scene={4}",
                        Field(core, "RealName"), Field(core, "bmsCoreName"), Field(core, "hardMode"),
                        Field(core, "songLength"), target));
                    if (!Application.CanStreamedLevelBeLoaded(target))
                    {
                        Log.LogError("dev: scene not loadable: " + target);
                        return;
                    }
                    s_devLaunched = true;
                    SceneManager.LoadScene(target);
                }
            }
            catch (Exception e)
            {
                Log.LogError("dev test failed: " + e);
                s_devLaunched = true;
            }
        }

        internal static MonoBehaviour CurrentPlayer;
        internal static double HitRatio = 1.0;

        internal static string ToggleFullscreen()
        {
            try
            {
                if (Screen.fullScreen)
                {
                    Screen.SetResolution(1280, 720, false);
                    Log.LogInfo("display -> windowed 1280x720");
                    return "{\"ok\":true,\"data\":\"windowed\"}";
                }
                int w = Display.main.systemWidth;
                int h = Display.main.systemHeight;
                Screen.SetResolution(w, h, true);
                Log.LogInfo("display -> fullscreen " + w + "x" + h);
                return "{\"ok\":true,\"data\":\"fullscreen\"}";
            }
            catch (Exception e)
            {
                Log.LogError("fullscreen toggle failed: " + e.Message);
                return "{\"ok\":false,\"error\":" + Json.Q(e.Message) + "}";
            }
        }

        internal static void OnPlayerUpdate(MonoBehaviour player)
        {
            if (!Enabled || player == null) return;
            CurrentPlayer = player;
            try
            {
                Tick(player);
            }
            catch (Exception e)
            {
                Enabled = false;
                Log.LogError("autoplay disabled after error: " + e);
            }
        }

        static void Tick(MonoBehaviour player)
        {
            PlayerAccessor a = PlayerAccessor.For(player.GetType());
            if (a == null) return;

            PlayStatus status = (PlayStatus)a.PlayStatus.GetValue(player, null);
            if (status == PlayStatus.Paused)
            {
                if (CfgDevTest.Value && a.PlayResume != null && Time.realtimeSinceStartup - s_lastResume > 2f)
                {
                    s_lastResume = Time.realtimeSinceStartup;
                    Log.LogInfo("dev: auto resume");
                    a.PlayResume.Invoke(player, null);
                }
                return;
            }
            if (status != PlayStatus.Playing) return;

            IList[] tracks = a.NoteByTrack.GetValue(player) as IList[];
            int[] knockIndex = a.KnockIndex.GetValue(player) as int[];
            if (tracks == null || knockIndex == null) return;

            long now = (long)a.PlayTicks.GetValue(player, null);
            double look = CfgOffsetMs.Value * 10000.0 + Time.deltaTime * 0.5 * 1e7;
            bool perfect = string.Equals(CfgMode.Value, "Perfect", StringComparison.OrdinalIgnoreCase);

            for (int t = 1; t < tracks.Length; t++)
            {
                IList list = tracks[t];
                if (list == null || list.Count == 0) continue;

                int scan = knockIndex[t];
                while (scan < list.Count && ((NoteEntity)a.NoteEntity.GetValue(list[scan])).Knocked) scan++;
                if (scan >= list.Count) { knockIndex[t] = scan; continue; }

                NoteEntity note = (NoteEntity)a.NoteEntity.GetValue(list[scan]);
                long delta = note.InsertTime - now;
                if (delta > look) continue;
                if (delta < -3000000) { knockIndex[t] = scan + 1; continue; }
                if (HitRatio < 0.999 && UnityEngine.Random.value > HitRatio) { knockIndex[t] = scan + 1; continue; }

                if (perfect)
                {
                    a.SheathKnock.Invoke(list[scan], new object[] { note.InsertTime });
                    knockIndex[t] = scan + 1;
                }
                else
                {
                    a.AddButton.Invoke(player, new object[] { t });
                }
                if (CfgVerbose.Value)
                    Log.LogInfo(string.Format("{0} t{1} d={2} scan={3}/{4}", perfect ? "perfect" : "native", t, delta, scan, list.Count));
            }
        }
    }

    sealed class PlayerAccessor
    {
        public FieldInfo NoteByTrack;
        public FieldInfo KnockIndex;
        public FieldInfo NoteEntity;
        public PropertyInfo PlayTicks;
        public PropertyInfo PlayStatus;
        public MethodInfo AddButton;
        public MethodInfo SheathKnock;
        public MethodInfo PlayResume;

        static readonly Dictionary<Type, PlayerAccessor> Cache = new Dictionary<Type, PlayerAccessor>();

        public static PlayerAccessor For(Type t)
        {
            PlayerAccessor a;
            if (Cache.TryGetValue(t, out a)) return a;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            a = new PlayerAccessor();
            a.NoteByTrack = t.GetField("noteByTrack", flags);
            a.KnockIndex = t.GetField("knockIndex", flags);
            a.PlayTicks = t.GetProperty("PlayTicks", flags);
            a.PlayStatus = t.GetProperty("playStatus", flags);
            a.AddButton = t.GetMethod("AddButton", flags, null, new[] { typeof(int) }, null);
            a.PlayResume = t.GetMethod("PlayResume", flags, null, Type.EmptyTypes, null);
            if (a.NoteByTrack == null || a.KnockIndex == null || a.PlayTicks == null ||
                a.PlayStatus == null || a.AddButton == null)
            {
                BepInEx.Logging.Logger.CreateLogSource("MusynxAutoplay").LogError("unexpected player layout: " + t.FullName);
                a = null;
            }
            else
            {
                Type sheath = a.NoteByTrack.FieldType.GetElementType().GetGenericArguments()[0];
                a.NoteEntity = sheath.GetField("noteEntity", flags);
                a.SheathKnock = sheath.GetMethod("Knock", flags, null, new[] { typeof(long) }, null);
                if (a.NoteEntity == null || a.SheathKnock == null) a = null;
            }
            Cache[t] = a;
            return a;
        }
    }

    [HarmonyPatch]
    static class PlayerUpdatePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type t in AutoplayPlugin.PlayerTypes)
            {
                MethodInfo m = t.GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (m != null) yield return m;
            }
        }

        static void Postfix(MonoBehaviour __instance)
        {
            AutoplayPlugin.OnPlayerUpdate(__instance);
        }
    }

    [HarmonyPatch]
    static class FocusPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type t in AutoplayPlugin.PlayerTypes)
            {
                MethodInfo focus = t.GetMethod("OnApplicationFocus", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
                if (focus != null) yield return focus;
                MethodInfo pause = t.GetMethod("OnApplicationPause", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
                if (pause != null) yield return pause;
            }
        }

        static bool Prefix()
        {
            return !(AutoplayPlugin.Enabled && AutoplayPlugin.CfgIgnoreFocus.Value);
        }
    }

    [HarmonyPatch]
    static class PausePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type t in AutoplayPlugin.PlayerTypes)
            {
                MethodInfo m = t.GetMethod("PlayPause", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (m != null) yield return m;
            }
        }

        static bool Prefix()
        {
            if (!AutoplayPlugin.Enabled || !AutoplayPlugin.CfgIgnoreFocus.Value) return true;
            if (UnityEngine.Application.isFocused) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(SongInfoCore), "UploadScore")]
    static class UploadPatch
    {
        static bool Prefix()
        {
            return !AutoplayPlugin.CfgBlockUpload.Value;
        }
    }
}
