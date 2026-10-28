using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MusynxAutoplay
{
    /// <summary>本机 TCP 命令行服务：工具端连上来发一行 JSON，收一行 JSON。</summary>
    public static class CommandServer
    {
        sealed class Job
        {
            public Func<string> Work;
            public string Result;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        static TcpListener s_listener;
        static Thread s_thread;
        static readonly ConcurrentQueue<Job> s_jobs = new ConcurrentQueue<Job>();

        public static int Port { get; private set; }

        public static void Start(int port)
        {
            if (s_listener != null) return;
            Port = port;
            try
            {
                s_listener = new TcpListener(IPAddress.Loopback, port);
                s_listener.Start();
                s_thread = new Thread(Loop);
                s_thread.IsBackground = true;
                s_thread.Name = "MusynxAutoplay.Cmd";
                s_thread.Start();
                AutoplayPlugin.Log.LogInfo("command server on 127.0.0.1:" + port);
            }
            catch (Exception e)
            {
                AutoplayPlugin.Log.LogError("command server failed: " + e.Message);
            }
        }

        static void Loop()
        {
            while (true)
            {
                try
                {
                    TcpClient client = s_listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(Handle, client);
                }
                catch (Exception)
                {
                    Thread.Sleep(200);
                }
            }
        }

        static void Handle(object state)
        {
            var client = state as TcpClient;
            if (client == null) return;
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    string line = reader.ReadLine();
                    if (line == null) return;
                    string response = Dispatch(line);
                    writer.WriteLine(response);
                }
            }
            catch (Exception e)
            {
                AutoplayPlugin.Log.LogWarning("cmd error: " + e.Message);
            }
        }

        static string Dispatch(string line)
        {
            try
            {
                var req = Json.Parse(line) as System.Collections.Generic.Dictionary<string, object>;
                if (req == null) return GameBridge.Err("bad json");
                string cmd = Json.Str(Get(req, "cmd")) ?? "";
                switch (cmd)
                {
                    case "ping":
                        return "{\"ok\":true,\"data\":\"pong\"}";
                    case "prepare":
                        return RunOnMain(GameBridge.Prepare);
                    case "select":
                        return RunOnMain(GameBridge.GoSelect);
                    case "songs":
                        return RunOnMain(GameBridge.Songs);
                    case "fullscreen":
                        return RunOnMain(AutoplayPlugin.ToggleFullscreen);
                    case "state":
                        return RunOnMain(GameBridge.State);
                    case "play":
                        {
                            string bms = Json.Str(Get(req, "bms")) ?? "";
                            string hard = Json.Str(Get(req, "hard")) ?? "easy";
                            int keys = Json.Int(Get(req, "keys"));
                            double ratio = Get(req, "ratio") == null ? 1.0 : Json.Dbl(Get(req, "ratio"));
                            return RunOnMain(() => GameBridge.Play(bms, hard, keys <= 0 ? 4 : keys, ratio));
                        }
                    default:
                        return GameBridge.Err("unknown cmd: " + cmd);
                }
            }
            catch (Exception e)
            {
                return GameBridge.Err(e.Message);
            }
        }

        static object Get(System.Collections.Generic.Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) ? v : null;
        }

        static string RunOnMain(Func<string> work)
        {
            var job = new Job { Work = work };
            s_jobs.Enqueue(job);
            if (!job.Done.Wait(15000))
                return GameBridge.Err("主线程超时（游戏可能未响应）");
            return job.Result ?? GameBridge.Err("empty");
        }

        /// <summary>由主线程每帧调用。</summary>
        public static void Pump()
        {
            Job job;
            while (s_jobs.TryDequeue(out job))
            {
                try { job.Result = job.Work(); }
                catch (Exception e) { job.Result = GameBridge.Err(e.Message); }
                finally { job.Done.Set(); }
            }
        }
    }
}
