using System;
using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  ElderholtBootstrap — the host process. It stands up the authoritative
    //  ZoneServer, the Thornmere world, the local player's client and the Fenn
    //  bot, connects them through a simulated-latency pipe, and then each frame:
    //  advances the 600 ms tick, delivers piped messages, interpolates avatars
    //  between the last two snapshots, drives the orbit camera, turns clicks into
    //  intents, and paints the monospace HUD/chat overlay.
    //
    //  Auto-boots on Play (no scene wiring required) via RuntimeInitialize.
    // ============================================================================
    public class ElderholtBootstrap : MonoBehaviour, IClientHost
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindObjectOfType<ElderholtBootstrap>() != null) return;
            new GameObject("Elderholt").AddComponent<ElderholtBootstrap>();
        }

        const float LatencyMs = 110f;

        Camera cam;
        ZoneServer server;
        ThornmereWorld world;
        GameClient gameClient;
        BotClient botClient;

        // camera orbit state (matches the prototype's yaw/pitch/dist)
        float yaw = 0.6f, pitch = 0.42f, dist = 16f;

        float tickAccum;
        float pingAccum;

        // input drag tracking
        bool pointerDown, dragging;
        Vector3 lastMouse;

        readonly Dictionary<string, Vector2> shown = new Dictionary<string, Vector2>
        {
            { "you", new Vector2(2, 4) }, { "fenn", new Vector2(-5, 8) },
        };

        class Sched { public double due; public Action a; }
        readonly List<Sched> scheduled = new List<Sched>();

        class Floaty { public string text; public Vector3 pos; public float life; }
        readonly List<Floaty> floats = new List<Floaty>();

        struct ChatMsg { public string from; public string text; public bool you; }
        readonly List<ChatMsg> chatLog = new List<ChatMsg>();
        string chatDraft = "";

        public double NowMs => Time.realtimeSinceStartupAsDouble * 1000.0;

        // ---------- IClientHost ----------
        public void Schedule(float delaySeconds, Action action)
        {
            scheduled.Add(new Sched { due = NowMs + delaySeconds * 1000.0, a = action });
        }

        public void PushChat(string from, string text, bool isYou)
        {
            chatLog.Add(new ChatMsg { from = from, text = text, you = isYou });
            if (chatLog.Count > 6) chatLog.RemoveRange(0, chatLog.Count - 6);
        }

        // ---------- setup ----------
        void Awake()
        {
            SetupEnvironment();
            server = new ZoneServer();
            world = new ThornmereWorld(server);
            ConnectClients();
        }

        void SetupEnvironment()
        {
            cam = Camera.main;
            if (cam == null)
            {
                GameObject camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Geo.Sky;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;

            Light sun = null;
            foreach (Light l in FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Geo.Sun;
            sun.intensity = 1.0f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(-25, -40, -15).normalized);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Geo.HemiSky;
            RenderSettings.ambientEquatorColor = Color.Lerp(Geo.HemiSky, Geo.HemiGround, 0.5f);
            RenderSettings.ambientGroundColor = Geo.HemiGround;

            RenderSettings.fog = true;
            RenderSettings.fogColor = Geo.Sky;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 110f;
        }

        // Simulated one-way latency, matching the prototype's lat() jitter.
        float LatSeconds() => Mathf.Max(0f, LatencyMs * (0.75f + UnityEngine.Random.value * 0.5f) / 2f) / 1000f;

        void ConnectClients()
        {
            gameClient = new GameClient(this, "you");
            server.Connect("you", "You", snap => Schedule(LatSeconds(), () => gameClient.OnSnapshot(snap)));

            botClient = new BotClient(this, server, BotSend, "fenn");
            server.Connect("fenn", "Fenn", snap => Schedule(LatSeconds(), () => botClient.OnSnapshot(snap)));
            botClient.Greet();
        }

        void SendToServer(Intent i) => Schedule(LatSeconds(), () => server.SubmitIntent("you", i));
        void BotSend(Intent i) => Schedule(LatSeconds(), () => server.SubmitIntent("fenn", i));

        // ---------- loop ----------
        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            RunScheduler();

            tickAccum += Time.deltaTime * 1000f;
            while (tickAccum >= ZoneServer.TickMs)
            {
                tickAccum -= ZoneServer.TickMs;
                server.Step();
            }

            pingAccum += Time.deltaTime;
            if (pingAccum >= 2f) { pingAccum = 0f; SendToServer(Intent.Ping(NowMs)); }

            HandleInput();
            RenderInterpolated(dt);
            UpdateFloats(dt);
            UpdateCamera();
        }

        void RunScheduler()
        {
            double now = NowMs;
            for (int i = scheduled.Count - 1; i >= 0; i--)
            {
                if (scheduled[i].due <= now)
                {
                    Action a = scheduled[i].a;
                    scheduled.RemoveAt(i);
                    a();
                }
            }
        }

        void RenderInterpolated(float dt)
        {
            GameClient g = gameClient;
            if (g.Next.snap == null) return;

            Snapshot b = g.Next.snap;
            float alpha = g.HasPrev ? Mathf.Clamp01((float)((NowMs - g.Next.at) / ZoneServer.TickMs)) : 1f;

            foreach (PlayerSnap pb in b.players)
            {
                if (!world.Avatars.TryGetValue(pb.id, out Avatar av)) continue;
                PlayerSnap pa = g.HasPrev ? g.Prev.snap.players.Find(p => p.id == pb.id) : null;
                float x = pa != null ? pa.x + (pb.x - pa.x) * alpha : pb.x;
                float z = pa != null ? pa.z + (pb.z - pa.z) * alpha : pb.z;
                shown[pb.id] = new Vector2(x, z);
                av.Place(x, z, pb.dir);
                av.Animate(pb.anim, dt);
            }

            foreach (NodeSnap n in b.nodes) world.SetRockVisible(n.id, n.ore > 0);

            // spawn floating XP at the interpolated miner position
            while (g.PendingXpFloats.Count > 0)
            {
                GameEvent ev = g.PendingXpFloats.Dequeue();
                Vector2 at = shown.TryGetValue(ev.who, out Vector2 s) ? s : new Vector2(ev.x, ev.z);
                floats.Add(new Floaty { text = "+" + ev.amount + " xp", pos = new Vector3(at.x, 2.4f, at.y), life = 1.2f });
            }
        }

        void UpdateFloats(float dt)
        {
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                floats[i].life -= dt;
                floats[i].pos += Vector3.up * dt * 1.4f;
                if (floats[i].life <= 0) floats.RemoveAt(i);
            }

            if (world.Marker.gameObject.activeSelf)
            {
                float s = 0.9f * (1f + Mathf.Sin(Time.time * 6f) * 0.12f);
                world.Marker.localScale = new Vector3(s, 0.02f, s);
            }
        }

        void UpdateCamera()
        {
            Vector2 me = shown["you"];
            float cx = me.x + dist * Mathf.Sin(yaw) * Mathf.Cos(pitch);
            float cz = me.y + dist * Mathf.Cos(yaw) * Mathf.Cos(pitch);
            cam.transform.position = new Vector3(cx, 1.2f + dist * Mathf.Sin(pitch), cz);
            cam.transform.LookAt(new Vector3(me.x, 1.2f, me.y));
        }

        // ---------- input -> intents ----------
        void HandleInput()
        {
            if (Input.mouseScrollDelta.y != 0f)
                dist = Mathf.Clamp(dist - Input.mouseScrollDelta.y * 1.5f, 6f, 34f);

            if (Input.GetMouseButtonDown(0))
            {
                pointerDown = true; dragging = false; lastMouse = Input.mousePosition;
            }
            else if (pointerDown && Input.GetMouseButton(0))
            {
                Vector3 d = Input.mousePosition - lastMouse;
                if (dragging || Mathf.Abs(d.x) + Mathf.Abs(d.y) > 6f)
                {
                    dragging = true;
                    yaw -= d.x * 0.008f;
                    pitch = Mathf.Clamp(pitch - d.y * 0.005f, 0.15f, 1.2f);
                    lastMouse = Input.mousePosition;
                }
            }
            else if (pointerDown && Input.GetMouseButtonUp(0))
            {
                if (!dragging && !PointerOverChat()) Pick();
                pointerDown = false;
            }
        }

        bool PointerOverChat()
        {
            // bottom-left chat input strip
            return Input.mousePosition.x < 340f && Input.mousePosition.y < 34f;
        }

        void Pick()
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f)) return;

            OreRockRef rock = hit.collider.GetComponentInParent<OreRockRef>();
            if (rock != null)
            {
                SendToServer(Intent.Interact(rock.id));
                ShowMarker(world.RockGroups[rock.id].transform.position);
                gameClient.status = "intent sent: interact " + rock.id;
                return;
            }
            if (hit.collider.gameObject == world.Ground)
            {
                SendToServer(Intent.Move(hit.point.x, hit.point.z));
                ShowMarker(hit.point);
                gameClient.status = "";
            }
        }

        void ShowMarker(Vector3 p)
        {
            world.Marker.position = new Vector3(p.x, p.y + 0.1f, p.z);
            world.Marker.gameObject.SetActive(true);
        }

        // ---------- UI ----------
        GUIStyle panelText, nameTagStyle, floatStyle, chatYou, chatOther;
        Texture2D panelBg;

        void EnsureStyles()
        {
            if (panelBg != null) return;
            panelBg = Solid(new Color(20 / 255f, 24 / 255f, 16 / 255f, 0.82f));
            Color txt = Geo.Hex(0xe8e4d0);
            panelText = Label(12, txt, false);
            nameTagStyle = Label(12, Color.white, true);
            floatStyle = Label(16, Geo.Marker, true);
            chatYou = Label(13, Geo.Hex(0xf0d060), false);
            chatOther = Label(13, Geo.Hex(0x9fd08a), false);
        }

        static GUIStyle Label(int size, Color c, bool center)
        {
            GUIStyle s = new GUIStyle();
            s.fontSize = size;
            s.normal.textColor = c;
            s.alignment = center ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft;
            s.fontStyle = FontStyle.Bold;
            return s;
        }

        static Texture2D Solid(Color c)
        {
            Texture2D t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawHud();
            DrawWorldLabels();
            DrawChat();
        }

        void DrawHud()
        {
            GameClient g = gameClient;
            int youXp = 0, fennXp = 0;
            if (g.Next.snap != null)
            {
                g.Next.snap.xp.TryGetValue("you", out youXp);
                g.Next.snap.xp.TryGetValue("fenn", out fennXp);
            }

            Rect box = new Rect(10, 10, 300, 132);
            GUI.DrawTexture(box, panelBg);
            GUILayout.BeginArea(new Rect(box.x + 10, box.y + 8, box.width - 20, box.height - 12));
            GUILayout.Label("THORNMERE REACH   tick " + g.tick + "   rtt " + g.rtt + "ms", panelText);
            GUILayout.Label("Mining  You  Lv " + XpCurve.Level(youXp) + "  (" + youXp.ToString("N0") + " xp)", panelText);
            GUILayout.Label("        Fenn Lv " + XpCurve.Level(fennXp) + "  (" + fennXp.ToString("N0") + " xp)", panelText);
            GUILayout.Label("saved " + g.savedAt, panelText);
            GUILayout.Label(g.status, panelText);
            GUILayout.EndArea();

            if (GUI.Button(new Rect(box.x, box.y + box.height + 6, 130, 24), "Restart server"))
                Restart();
        }

        void DrawWorldLabels()
        {
            foreach (KeyValuePair<string, Vector2> kv in shown)
            {
                DrawWorldLabel(new Vector3(kv.Value.x, 2.25f, kv.Value.y), kv.Key == "you" ? "You" : "Fenn", nameTagStyle);
            }
            foreach (Floaty f in floats)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1, 1, 1, Mathf.Clamp01(f.life / 1.2f));
                DrawWorldLabel(f.pos, f.text, floatStyle);
                GUI.color = prev;
            }
        }

        void DrawWorldLabel(Vector3 worldPos, string text, GUIStyle style)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0) return;
            GUI.Label(new Rect(sp.x - 80, Screen.height - sp.y - 12, 160, 24), text, style);
        }

        void DrawChat()
        {
            float y = Screen.height - 40 - chatLog.Count * 18;
            for (int i = 0; i < chatLog.Count; i++)
            {
                ChatMsg m = chatLog[i];
                GUI.Label(new Rect(12, y + i * 18, 420, 18), m.from + ": " + m.text, m.you ? chatYou : chatOther);
            }

            Event e = Event.current;
            GUI.SetNextControlName("chatInput");
            chatDraft = GUI.TextField(new Rect(12, Screen.height - 30, 320, 22), chatDraft, 120);
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                string t = chatDraft.Trim();
                if (t.Length > 0) { SendToServer(Intent.Chat(t)); chatDraft = ""; }
                e.Use();
            }
        }

        void Restart()
        {
            server.Save();
            server = new ZoneServer();
            ConnectClients();
            gameClient.status = "Server restarted — characters, XP and rocks reloaded.";
        }

        void OnApplicationQuit()
        {
            if (server != null) server.Save();
        }
    }
}
