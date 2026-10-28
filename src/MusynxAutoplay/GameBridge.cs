using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using BMSLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusynxAutoplay
{
    public static class GameBridge
    {
        public const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static readonly string[] Lists4T = { "songInfoCoreEasyList4T", "songInfoCoreHardList4T", "songInfoCoreInList4T" };
        static readonly string[] Lists6T = { "songInfoCoreEasyList6T", "songInfoCoreHardList6T", "songInfoCoreInList6T" };

        public static SongInfoCore CurrentCore;
        public static string CurrentTitle = "";
        public static string LastResultJson = "null";
        public static bool LastResultFresh;

        public static Type T(string name)
        {
            return typeof(UI0_SongPlayer).Assembly.GetType(name);
        }

        static object Prop(object o, string name)
        {
            if (o == null) return null;
            Type t = o.GetType();
            PropertyInfo p = t.GetProperty(name, Any);
            if (p != null) return p.GetValue(o, null);
            FieldInfo f = t.GetField(name, Any);
            return f == null ? null : f.GetValue(o);
        }

        static string S(object o, string name) { return Prop(o, name) as string; }
        static bool Bool(object o, string name)
        {
            object v = Prop(o, name);
            return v is bool && (bool)v;
        }

        static int I(object o, string name)
        {
            object v = Prop(o, name);
            return v == null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        static void SetStatic(string typeName, string member, object value)
        {
            Type t = T(typeName);
            if (t == null) return;
            PropertyInfo p = t.GetProperty(member, Any);
            if (p != null) { p.SetValue(null, value, null); return; }
            FieldInfo f = t.GetField(member, Any);
            if (f != null) f.SetValue(null, value);
        }

        // ---------- 场景 ----------

        public static string Scene() { return SceneManager.GetActiveScene().name; }
        public static bool InSelect() { return Scene().IndexOf("SelectSong", StringComparison.Ordinal) >= 0; }

        public static string GoSelect()
        {
            if (InSelect()) return Ok("already");
            try
            {
                SetStatic("UserMemory", "SceneName", "SelectSongScene");
                SceneManager.LoadScene("SelectSongScene");
                return Ok("loading");
            }
            catch (Exception e) { return Err(e.Message); }
        }

        public static string Prepare()
        {
            string scene = Scene();
            if (InSelect()) return Ok("select");
            if (scene == "OpeningScene")
            {
                Type t = T("OpeningSpectrumScript");
                UnityEngine.Object opener = t == null ? null : UnityEngine.Object.FindObjectOfType(t);
                if (opener == null) return Err("找不到开场脚本");
                MethodInfo m = t.GetMethod("PlayGame", Any);
                if (m == null) return Err("找不到 PlayGame");
                m.Invoke(opener, null);
                return Ok("opening");
            }
            return Err("当前场景：" + scene + "，请先进到选歌界面");
        }

        // ---------- 歌曲 ----------

        static object Controller()
        {
            Type t = T("SelectSongController");
            return t == null ? null : UnityEngine.Object.FindObjectOfType(t);
        }

        static IList GetList(object controller, string field)
        {
            if (controller == null) return null;
            FieldInfo f = controller.GetType().GetField(field, Any);
            return f == null ? null : f.GetValue(controller) as IList;
        }

        static string SceneOf(SongInfoCore c)
        {
            string scene = null;
            // 优先用游戏自己的场景解析（会考虑玩家当前主题/已拥有内容）
            try
            {
                Type adv = T("AdvSettingScript");
                MethodInfo gm = adv == null ? null : adv.GetMethod("GetPlaySceneName", Any);
                if (gm != null) scene = gm.Invoke(null, new object[] { c }) as string;
            }
            catch (Exception) { }
            if (string.IsNullOrEmpty(scene)) scene = S(c, "playingSceneName");
            if (string.IsNullOrEmpty(scene) || !Application.CanStreamedLevelBeLoaded(scene)) scene = "UIB_PlayingScene";
            return scene;
        }

        public static string Songs()
        {
            object ctrl = Controller();
            if (ctrl == null) return Err("请先进入选歌界面");

            var sb = new StringBuilder("[");
            bool first = true;
            var seen = new HashSet<string>();
            foreach (var pair in new[] { new { L = Lists4T, K = 4 }, new { L = Lists6T, K = 6 } })
            {
                foreach (string field in pair.L)
                {
                    IList list = GetList(ctrl, field);
                    if (list == null) continue;
                    foreach (object o in list)
                    {
                        var core = o as SongInfoCore;
                        if (core == null) continue;
                        string name = S(core, "RealName");
                        string bms = S(core, "bmsCoreName");
                        string hard = S(core, "hardMode");
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(bms)) continue;
                        if (bms == "shop") continue;
                        string id = bms + "|" + hard + "|" + pair.K;
                        if (!seen.Add(id)) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append('{')
                          .Append("\"id\":").Append(Json.Q(id)).Append(',')
                          .Append("\"name\":").Append(Json.Q(name)).Append(',')
                          .Append("\"hard\":").Append(Json.Q(hard)).Append(',')
                          .Append("\"keys\":").Append(pair.K).Append(',')
                          .Append("\"level\":").Append(I(core, "level")).Append(',')
                          .Append("\"bms\":").Append(Json.Q(bms)).Append(',')
                          .Append("\"sync\":").Append(I(core, "SyncNumber")).Append(',')
                          .Append("\"needDownload\":").Append(Bool(core, "NeedDownloadAssetBundle") ? "true" : "false").Append(',')
                          .Append("\"needUpdate\":").Append(Bool(core, "NeedUpdateAssetBundle") ? "true" : "false").Append(',')
                          .Append("\"useBundle\":").Append(Bool(core, "UseAssetBundle") ? "true" : "false").Append(',')
                          .Append("\"productId\":").Append(Json.Q(S(core, "ProductId") ?? "")).Append(',')
                          .Append("\"scene\":").Append(Json.Q(SceneOf(core)))
                          .Append('}');
                    }
                }
            }
            sb.Append(']');
            return "{\"ok\":true,\"data\":" + sb + "}";
        }

        public static SongInfoCore FindCore(string bms, string hard, int keys)
        {
            object ctrl = Controller();
            if (ctrl == null) return null;
            foreach (string field in (keys == 6 ? Lists6T : Lists4T))
            {
                IList list = GetList(ctrl, field);
                if (list == null) continue;
                foreach (object o in list)
                {
                    var core = o as SongInfoCore;
                    if (core == null) continue;
                    if (S(core, "bmsCoreName") == bms && S(core, "hardMode") == hard) return core;
                }
            }
            return null;
        }

        public static string Play(string bms, string hard, int keys, double ratio)
        {
            if (!InSelect()) return Err("当前不在选歌界面");
            SongInfoCore core = FindCore(bms, hard, keys);
            if (core == null) return Err("找不到该谱面");

            object pkg = null;
            object ctrl = Controller();
            if (ctrl != null) pkg = Prop(ctrl, "selectSongInfo");
            if (pkg == null)
            {
                Type pt = T("SongInfoPackageScript");
                if (pt != null)
                {
                    var all = Resources.FindObjectsOfTypeAll(pt);
                    if (all != null && all.Length > 0) pkg = all[0];
                }
            }
            if (pkg == null) return Err("找不到选歌条目");

            try
            {
                SetStatic("UserMemory", "ButtonNumber", keys);
                SetStatic("UserMemory", "IsHard", hard == "hard" ? 1 : 0);
                SetStatic("UserMemory", "DisplayAdjustment", 0);
                SetStatic("UserMemory", "JudgeCompensate", 0);
                SetStatic("UserMemory", "SceneName", "SelectSongScene");

                pkg.GetType().GetMethod("Reset", Any).Invoke(pkg, new object[] { core });
                pkg.GetType().GetMethod("CleanToPlay", Any).Invoke(pkg, null);

                string scene = SceneOf(core);
                CurrentCore = core;
                CurrentTitle = S(core, "RealName");
                LastResultJson = "null";
                LastResultFresh = false;
                AutoplayPlugin.HitRatio = Math.Max(0.0, Math.Min(1.0, ratio));
                SceneManager.LoadScene(scene);
                return "{\"ok\":true,\"data\":{\"scene\":" + Json.Q(scene) + ",\"title\":" + Json.Q(CurrentTitle ?? "") + "}}";
            }
            catch (Exception e) { return Err(e.Message); }
        }

        // ---------- 状态 ----------

        static string DebugInfo(MonoBehaviour player)
        {
            try
            {
                Type um = T("UserMemory");
                PropertyInfo bn = um == null ? null : um.GetProperty("ButtonNumber", Any);
                object bnVal = bn == null ? null : bn.GetValue(null, null);
                int lanes = 0;
                var trackCounts = new StringBuilder();
                if (player != null)
                {
                    PlayerAccessor pa = PlayerAccessor.For(player.GetType());
                    IList[] tr = pa == null ? null : pa.NoteByTrack.GetValue(player) as IList[];
                    if (tr != null)
                    {
                        lanes = tr.Length;
                        for (int i = 0; i < tr.Length; i++) trackCounts.Append(tr[i] == null ? 0 : tr[i].Count).Append(',');
                    }
                }
                int trackX = 0;
                if (player != null)
                {
                    object tx = Prop(player, "TrackX");
                    var arr = tx as Array;
                    if (arr != null) trackX = arr.Length;
                }
                return ",\"buttonNumber\":" + (bnVal == null ? "null" : bnVal.ToString()) +
                       ",\"trackX\":" + trackX +
                       ",\"playerType\":" + Json.Q(player == null ? "" : player.GetType().Name) +
                       ",\"lanes\":" + lanes +
                       ",\"trackNotes\":" + Json.Q(trackCounts.ToString());
            }
            catch (Exception) { return ""; }
        }

        public static string State()
        {
            string scene = Scene();
            MonoBehaviour player = AutoplayPlugin.CurrentPlayer;
            bool playing = false;
            double progress = 0;
            if (player != null)
            {
                PlayerAccessor a = PlayerAccessor.For(player.GetType());
                if (a != null)
                {
                    PlayStatus st = (PlayStatus)a.PlayStatus.GetValue(player, null);
                    playing = st == PlayStatus.Playing;
                    long ticks = (long)a.PlayTicks.GetValue(player, null);
                    FieldInfo len = player.GetType().GetField("bgmLength", Any);
                    long total = len == null ? 0 : Convert.ToInt64(len.GetValue(player));
                    if (total > 0) progress = Math.Max(0, Math.Min(1, ticks / (double)total));
                }
            }
            string extra = DebugInfo(player);
            return "{\"ok\":true,\"data\":{\"scene\":" + Json.Q(scene) +
                   ",\"playing\":" + (playing ? "true" : "false") +
                   ",\"progress\":" + Json.Num(progress) +
                   ",\"title\":" + Json.Q(CurrentTitle ?? "") +
                   ",\"result\":" + (LastResultFresh ? LastResultJson : "null") + extra + "}}";
        }

        public static void CaptureResult()
        {
            if (CurrentCore == null) return;
            var sb = new StringBuilder();
            sb.Append('{')
              .Append("\"title\":").Append(Json.Q(S(CurrentCore, "RealName") ?? "")).Append(',')
              .Append("\"sync\":").Append(I(CurrentCore, "SyncNumberThis")).Append(',')
              .Append("\"combo\":").Append(I(CurrentCore, "MaxComboThis")).Append(',')
              .Append("\"ex\":").Append(JudgeGrade.TotalEx).Append(',')
              .Append("\"exact\":").Append(JudgeGrade.TotalExact).Append(',')
              .Append("\"great\":").Append(JudgeGrade.TotalGreat).Append(',')
              .Append("\"right\":").Append(JudgeGrade.TotalRight).Append(',')
              .Append("\"miss\":").Append(JudgeGrade.TotalMiss)
              .Append('}');
            LastResultJson = sb.ToString();
            LastResultFresh = true;
        }

        public static string Ok(string msg) { return "{\"ok\":true,\"data\":" + Json.Q(msg) + "}"; }
        public static string Err(string msg) { return "{\"ok\":false,\"error\":" + Json.Q(msg) + "}"; }
    }
}
