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
    //  between the last two snapshots, drives the orbit camera, turns clicks and
    //  keys into intents, and paints the monospace HUD/chat/panel overlay.
    //
    //  Phase 2 adds: depth-band ambience, the strike-rhythm indicator, wedge via
    //  right-click, shoring, the bag, and the stall / furnace / anvil / notice-
    //  board context panels. Keys: Space strike/hammer · W pump · Q quench ·
    //  T shore · B bag · E descend · R ascend.
    //
    //  Auto-boots on Play (no scene wiring required) via RuntimeInitialize.
    // ============================================================================
    public class ElderholtBootstrap : MonoBehaviour, IClientHost
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindAnyObjectByType<ElderholtBootstrap>() != null) return;
            new GameObject("Elderholt").AddComponent<ElderholtBootstrap>();
        }

        const float LatencyMs = 110f;

        Camera cam;
        ZoneServer server;
        ThornmereWorld world;
        GameClient gameClient;
        BotClient botClient;

        // camera orbit state (movement doc §6: yaw free, pitch 9°–69°, zoom 6–24 m)
        float yaw = 0.6f, pitch = 0.42f, dist = 14f;
        const float PitchMin = 0.16f, PitchMax = 1.2f;
        bool compassReset;              // easing back to north/default
        Vector3 camFocus;               // follow-eased look target
        bool camFocusInit;
        readonly Dictionary<Renderer, Material> fadedRenderers = new Dictionary<Renderer, Material>();

        // movement input state
        bool runPref;                   // the client's run toggle (server enforces energy)
        float wasdTimer;
        bool wasdWasHeld;
        Renderer markerRend;

        float tickAccum;
        float pingAccum;

        // input drag tracking
        bool pointerDown, dragging;
        Vector3 lastMouse;
        bool chatFocused;
        bool bagOpen;
        string mineTargetId;    // node we last sent interact for (strike indicator)
        int shownBand;          // band whose ambience is currently applied

        readonly Dictionary<string, Vector2> shown = new Dictionary<string, Vector2>
        {
            { "you", new Vector2(0, 2) }, { "fenn", new Vector2(-3, 4) },
        };

        class Sched { public double due; public Action a; }
        readonly List<Sched> scheduled = new List<Sched>();

        class Floaty { public string text; public Vector3 pos; public float life; }
        readonly List<Floaty> floats = new List<Floaty>();

        struct ChatMsg { public string from; public string text; public bool you; }
        readonly List<ChatMsg> chatLog = new List<ChatMsg>();
        string chatDraft = "";

        // Rects the IMGUI drew last frame, so world clicks don't fire through panels.
        readonly List<Rect> guiRects = new List<Rect>();

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
            shownBand = 0;
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
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
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

            ApplySurfaceAtmosphere();
        }

        void ApplySurfaceAtmosphere()
        {
            cam.backgroundColor = Geo.Sky;
            RenderSettings.fog = true;
            RenderSettings.fogColor = Geo.Sky;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 110f;
            RenderSettings.ambientIntensity = 1f;
        }

        void ApplyUndergroundAtmosphere(int band)
        {
            Color dark = Geo.Hex(0x14120e);
            cam.backgroundColor = dark;
            RenderSettings.fog = true;
            RenderSettings.fogColor = dark;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = band >= 2 ? 8f : 12f;
            RenderSettings.fogEndDistance = band >= 2 ? 34f : 48f;
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

        // ---------- snapshot helpers ----------
        Snapshot Snap => gameClient.Next.snap;

        PlayerSnap Me()
        {
            Snapshot s = Snap;
            return s?.players.Find(p => p.id == "you");
        }

        BagSnap MyBag()
        {
            Snapshot s = Snap;
            return s != null && s.bags.TryGetValue("you", out BagSnap b) ? b : null;
        }

        ForgeSnap MyForge()
        {
            Snapshot s = Snap;
            return s != null && s.forges.TryGetValue("you", out ForgeSnap f) ? f : null;
        }

        bool MeNear(Vector2 pos, float reach)
        {
            Vector2 me = shown["you"];
            return (me - pos).sqrMagnitude <= reach * reach;
        }

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
            UpdateAtmosphere();
            UpdateCamera(dt);
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
                // Don't interpolate across a band teleport.
                bool jump = pa != null && (Mathf.Abs(pb.x - pa.x) > 30f || Mathf.Abs(pb.z - pa.z) > 30f);
                float x = pa != null && !jump ? pa.x + (pb.x - pa.x) * alpha : pb.x;
                float z = pa != null && !jump ? pa.z + (pb.z - pa.z) * alpha : pb.z;
                shown[pb.id] = new Vector2(x, z);
                av.Place(x, z, pb.dir);
                av.Animate(pb.anim, dt);
            }

            foreach (NodeSnap n in b.nodes)
            {
                world.SetRockVisible(n.id, n.ore > 0);
                world.SetSeamHint(n.id, n.seamHint && n.ore > 0);
            }

            // floating text at the interpolated position
            while (g.PendingXpFloats.Count > 0)
            {
                GameEvent ev = g.PendingXpFloats.Dequeue();
                Vector2 at = shown.TryGetValue(ev.who, out Vector2 s) ? s : new Vector2(ev.x, ev.z);
                floats.Add(new Floaty { text = "+" + ev.amount + " xp", pos = new Vector3(at.x, 2.4f, at.y), life = 1.2f });
            }
            while (g.PendingNoteFloats.Count > 0)
            {
                GameEvent ev = g.PendingNoteFloats.Dequeue();
                Vector2 at = shown.TryGetValue(ev.who, out Vector2 s) ? s : new Vector2(ev.x, ev.z);
                string text = ev.type == EventType.OreGained ? "+" + ev.qty + " " + Items.Pretty(ev.item) : Items.Pretty(ev.item);
                floats.Add(new Floaty { text = text, pos = new Vector3(at.x, 2.9f, at.y), life = 1.5f });
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

        void UpdateAtmosphere()
        {
            PlayerSnap me = Me();
            int band = me != null ? me.band : 0;
            if (band == shownBand) return;
            shownBand = band;
            if (band == 0) ApplySurfaceAtmosphere();
            else ApplyUndergroundAtmosphere(band);
        }

        void UpdateCamera(float dt)
        {
            // Compass reset: ease yaw to north, pitch/zoom to defaults.
            if (compassReset)
            {
                yaw = Mathf.MoveTowardsAngle(yaw * Mathf.Rad2Deg, 0f, 300f * dt) * Mathf.Deg2Rad;
                pitch = Mathf.MoveTowards(pitch, 0.42f, 1.6f * dt);
                dist = Mathf.MoveTowards(dist, 14f, 30f * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, 0f)) < 0.5f && Mathf.Abs(pitch - 0.42f) < 0.01f && Mathf.Abs(dist - 14f) < 0.1f)
                    compassReset = false;
            }

            // Deep shafts clamp max zoom to tunnel scale.
            float maxDist = shownBand > 0 ? 16f : 24f;
            dist = Mathf.Clamp(dist, 6f, maxDist);

            // Follow, don't weld: the rig chases the character with a ~0.15 s ease.
            Vector2 me = shown["you"];
            Vector3 focusTarget = new Vector3(me.x, 1.2f, me.y);
            if (!camFocusInit) { camFocus = focusTarget; camFocusInit = true; }
            float k = 1f - Mathf.Exp(-dt / 0.15f);
            camFocus = Vector3.Lerp(camFocus, focusTarget, k);
            // Band teleports shouldn't ease across the world.
            if ((camFocus - focusTarget).sqrMagnitude > 900f) camFocus = focusTarget;

            Vector3 pos = camFocus + new Vector3(
                dist * Mathf.Sin(yaw) * Mathf.Cos(pitch),
                dist * Mathf.Sin(pitch),
                dist * Mathf.Cos(yaw) * Mathf.Cos(pitch));

            if (gameClient.shake > 0f)
            {
                gameClient.shake = Mathf.Max(0f, gameClient.shake - dt);
                float a = gameClient.shake * 0.35f;
                pos += new Vector3((UnityEngine.Random.value - 0.5f) * a, (UnityEngine.Random.value - 0.5f) * a, (UnityEngine.Random.value - 0.5f) * a);
            }

            cam.transform.position = pos;
            cam.transform.LookAt(camFocus);

            UpdateOcclusionFade(pos);
        }

        // Occlusion: fade, don't jump. Anything between camera and character goes
        // translucent; the boom never auto-shortens.
        void UpdateOcclusionFade(Vector3 camPos)
        {
            HashSet<Renderer> hitNow = new HashSet<Renderer>();
            Vector3 dir = camPos - camFocus;
            float len = dir.magnitude;
            if (len > 1.2f)
            {
                Avatar self = world.Avatars.TryGetValue("you", out Avatar a) ? a : null;
                foreach (RaycastHit h in Physics.RaycastAll(camFocus + dir.normalized * 0.5f, dir.normalized, len - 1.0f))
                {
                    if (h.collider.gameObject == world.Ground) continue;
                    if (world.Walkable.Contains(h.collider.gameObject)) continue;
                    if (self != null && h.collider.transform.IsChildOf(self.root)) continue;
                    foreach (Renderer r in h.collider.GetComponentsInChildren<Renderer>())
                        hitNow.Add(r);
                }
            }

            foreach (Renderer r in hitNow)
            {
                if (r == null || fadedRenderers.ContainsKey(r)) continue;
                fadedRenderers[r] = r.sharedMaterial;
                r.sharedMaterial = Geo.Faded(fadedRenderers[r]);
            }

            List<Renderer> restore = null;
            foreach (KeyValuePair<Renderer, Material> kv in fadedRenderers)
            {
                if (hitNow.Contains(kv.Key)) continue;
                (restore = restore ?? new List<Renderer>()).Add(kv.Key);
            }
            if (restore != null)
            {
                foreach (Renderer r in restore)
                {
                    if (r != null) r.sharedMaterial = fadedRenderers[r];
                    fadedRenderers.Remove(r);
                }
            }
        }

        // ---------- input -> intents ----------
        void HandleInput()
        {
            if (Input.mouseScrollDelta.y != 0f && !PointerOverUI())
            {
                compassReset = false;
                dist = Mathf.Clamp(dist - Input.mouseScrollDelta.y * 1.5f, 6f, shownBand > 0 ? 16f : 24f);
            }

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
                    compassReset = false;
                    yaw -= d.x * 0.008f;
                    pitch = Mathf.Clamp(pitch - d.y * 0.005f, PitchMin, PitchMax);
                    lastMouse = Input.mousePosition;
                }
            }
            else if (pointerDown && Input.GetMouseButtonUp(0))
            {
                if (!dragging && !PointerOverUI()) Pick(false);
                pointerDown = false;
            }

            if (Input.GetMouseButtonUp(1) && !PointerOverUI()) Pick(true);

            if (chatFocused) return;   // typing — keys stay out of the world

            // Arrow keys are the camera's (§6): ←/→ yaw, ↑/↓ pitch.
            float dt = Time.deltaTime;
            if (Input.GetKey(KeyCode.LeftArrow)) { yaw += 2.2f * dt; compassReset = false; }
            if (Input.GetKey(KeyCode.RightArrow)) { yaw -= 2.2f * dt; compassReset = false; }
            if (Input.GetKey(KeyCode.UpArrow)) { pitch = Mathf.Clamp(pitch + 1.1f * dt, PitchMin, PitchMax); compassReset = false; }
            if (Input.GetKey(KeyCode.DownArrow)) { pitch = Mathf.Clamp(pitch - 1.1f * dt, PitchMin, PitchMax); compassReset = false; }

            PlayerSnap me = Me();
            ForgeSnap forge = MyForge();

            HandleWasd(me, forge != null);

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (forge != null) SendToServer(Intent.Hammer());
                else if (me != null && me.anim == "mine") SendToServer(Intent.Strike());
            }
            if (Input.GetKeyDown(KeyCode.F) && forge != null) SendToServer(Intent.Pump());
            if (Input.GetKeyDown(KeyCode.Q) && forge != null) SendToServer(Intent.Quench());
            if (Input.GetKeyDown(KeyCode.T) && me != null && me.band > 0) SendToServer(Intent.Shore());
            if (Input.GetKeyDown(KeyCode.B)) bagOpen = !bagOpen;
            if (Input.GetKeyDown(KeyCode.E)) TryBandMove(+1);
            if (Input.GetKeyDown(KeyCode.R)) TryBandMove(-1);
            if (Input.GetKeyDown(KeyCode.X)) ToggleRun();
        }

        void ToggleRun()
        {
            runPref = !runPref;
            SendToServer(Intent.SetRun(runPref));
            gameClient.status = runPref ? "running when energy allows" : "walking (energy regenerates)";
        }

        // WASD is sugar over the same protocol (§4): held keys synthesize a
        // camera-relative move() a few tiles ahead each half-second; release
        // stops at the current tile. The server can't tell keyboard from mouse.
        void HandleWasd(PlayerSnap me, bool forging)
        {
            if (me == null) return;
            float h = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float v = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            bool held = Mathf.Abs(h) + Mathf.Abs(v) > 0.5f;
            if (forging && held) return;   // don't cancel a live forge by nudging keys

            wasdTimer += Time.deltaTime;
            if (held)
            {
                if (wasdWasHeld && wasdTimer < 0.45f) return;
                wasdTimer = 0f;
                wasdWasHeld = true;

                // Camera-relative on the ground plane.
                Vector2 fwd = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
                Vector2 right = new Vector2(fwd.y, -fwd.x);
                Vector2 dir = (fwd * v + right * h).normalized;
                Vector2 my = shown["you"];
                Vector2 target = my + dir * 2.5f;
                SendToServer(Intent.Move(target.x, target.y));
                world.Marker.gameObject.SetActive(false);   // keyboard moves don't need the click marker
            }
            else if (wasdWasHeld)
            {
                wasdWasHeld = false;
                Vector2 my = shown["you"];
                SendToServer(Intent.Move(my.x, my.y));   // stop at the current tile
            }
        }

        void TryBandMove(int delta)
        {
            PlayerSnap me = Me();
            if (me == null) return;
            if (delta > 0)
            {
                bool near = me.band == 0
                    ? MeNear(ZoneServer.EntrancePos, ZoneServer.StationReach)
                    : MeNear(new Vector2(Bands.All[me.band].originX, Bands.All[me.band].originZ), ZoneServer.StationReach + 2f);
                if (near && me.band < Bands.Count - 1) SendToServer(Intent.Descend());
            }
            else if (me.band > 0 && MeNear(new Vector2(Bands.All[me.band].originX, Bands.All[me.band].originZ), ZoneServer.StationReach + 2f))
            {
                SendToServer(Intent.Ascend());
            }
        }

        bool PointerOverUI()
        {
            Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            foreach (Rect r in guiRects) if (r.Contains(m)) return true;
            return false;
        }

        void Pick(bool wedge)
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f)) return;

            OreRockRef rock = hit.collider.GetComponentInParent<OreRockRef>();
            if (rock != null)
            {
                mineTargetId = rock.id;
                SendToServer(wedge ? Intent.Wedge(rock.id) : Intent.Interact(rock.id));
                ShowMarker(world.RockGroups[rock.id].transform.position, true);
                gameClient.status = wedge ? "bracing the wedge at " + rock.id : "intent sent: interact " + rock.id;
                return;
            }

            StationRef station = hit.collider.GetComponentInParent<StationRef>();
            if (station != null)
            {
                Vector3 sp = station.transform.position;
                SendToServer(Intent.Move(sp.x + 1.4f, sp.z - 1.4f));
                ShowMarker(sp, false);
                gameClient.status = station.kind == "entrance" ? "the shaft mouth — E to descend"
                    : station.kind == "shaft" ? "the ladder — E down, R up"
                    : station.kind == "vault" ? "walking to the Vault"
                    : station.kind == "board" ? "walking to the guildhall board"
                    : "walking to the " + station.kind;
                return;
            }

            foreach (GameObject walk in world.Walkable)
            {
                if (hit.collider.gameObject == walk)
                {
                    SendToServer(Intent.Move(hit.point.x, hit.point.z));
                    ShowMarker(hit.point, false);
                    gameClient.status = "";
                    return;
                }
            }
        }

        // Yellow marker for ground, red for interact — "did my click land?"
        void ShowMarker(Vector3 p, bool interact)
        {
            world.Marker.position = new Vector3(p.x, p.y + 0.1f, p.z);
            world.Marker.gameObject.SetActive(true);
            if (markerRend == null) markerRend = world.Marker.GetComponent<Renderer>();
            if (markerRend != null) markerRend.sharedMaterial.color = interact ? Geo.Hex(0xd05040) : Geo.Marker;
        }

        // ---------- UI ----------
        GUIStyle panelText, nameTagStyle, floatStyle, chatYou, chatOther, warnText, headText;
        Texture2D panelBg, barBg, barFill;

        void EnsureStyles()
        {
            if (panelBg != null) return;
            panelBg = Solid(new Color(20 / 255f, 24 / 255f, 16 / 255f, 0.82f));
            barBg = Solid(new Color(0.1f, 0.1f, 0.08f, 0.9f));
            barFill = Solid(Color.white);
            Color txt = Geo.Hex(0xe8e4d0);
            panelText = Label(12, txt, false);
            headText = Label(12, Geo.Hex(0xf0d060), false);
            warnText = Label(12, Geo.Hex(0xe07840), false);
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
            guiRects.Clear();

            DrawHud();
            DrawContextPanel();
            if (bagOpen) DrawBag();
            DrawWorldLabels();
            DrawChat();

            chatFocused = GUI.GetNameOfFocusedControl() == "chatInput";
        }

        Rect Panel(float x, float y, float w, float h)
        {
            Rect r = new Rect(x, y, w, h);
            GUI.DrawTexture(r, panelBg);
            guiRects.Add(r);
            return r;
        }

        void DrawHud()
        {
            GameClient g = gameClient;
            Snapshot s = Snap;
            PlayerSnap me = Me();
            BagSnap bag = MyBag();
            int youXp = 0, youSm = 0, fennXp = 0;
            if (s != null)
            {
                s.xp.TryGetValue("you", out youXp);
                s.smithXp.TryGetValue("you", out youSm);
                s.xp.TryGetValue("fenn", out fennXp);
            }
            int band = me != null ? me.band : 0;

            Vector2 myPos = shown["you"];
            Rect box = Panel(10, 10, 320, 196);
            GUILayout.BeginArea(new Rect(box.x + 10, box.y + 8, box.width - 20, box.height - 12));
            GUILayout.Label(Areas.Name(myPos.x, myPos.y, band).ToUpperInvariant() + "   tick " + g.tick + "   rtt " + g.rtt + "ms", headText);
            GUILayout.Label("Mining Lv " + XpCurve.Level(youXp) + " (" + youXp.ToString("N0") + ")   Smithing Lv " + XpCurve.Level(youSm) + " (" + youSm.ToString("N0") + ")", panelText);
            GUILayout.Label("Fenn: Mining Lv " + XpCurve.Level(fennXp), panelText);
            if (bag != null)
                GUILayout.Label("gold " + bag.gold + "g   ·   " + Items.Pretty(bag.pickaxe) + "   ·   bag (B)", panelText);
            if (band > 0 && s != null)
            {
                int inst = s.instability[band];
                GUILayout.Label("the rock: " + Bands.Tell(inst) + "  [" + inst + "]", inst >= 40 ? warnText : panelText);
                GUILayout.Label("T — shore with timber (" + BagCount(bag, Items.Timber) + " held)", panelText);
            }
            GUILayout.Label("saved " + g.savedAt, panelText);
            GUILayout.Label(g.status, panelText);
            GUILayout.EndArea();

            // Run energy orb (movement doc §5): toggle + a weight-aware meter.
            int energy = me != null ? me.energy : 100;
            Rect orb = new Rect(box.x + 10, box.y + box.height - 26, box.width - 20, 16);
            GUI.DrawTexture(orb, barBg);
            Color prevC = GUI.color;
            GUI.color = energy > 30 ? Geo.Hex(0xe8c840) : Geo.Hex(0xd05040);
            GUI.DrawTexture(new Rect(orb.x + 2, orb.y + 2, (orb.width - 4) * (energy / 100f), orb.height - 4), barFill);
            GUI.color = prevC;
            GUI.Label(new Rect(orb.x + 4, orb.y - 1, orb.width, 18), "energy " + energy, panelText);

            Rect btn = new Rect(box.x, box.y + box.height + 6, 130, 24);
            guiRects.Add(btn);
            if (GUI.Button(btn, "Restart server")) Restart();

            Rect runBtn = new Rect(box.x + 136, box.y + box.height + 6, 100, 24);
            guiRects.Add(runBtn);
            if (GUI.Button(runBtn, runPref ? "Run: ON (X)" : "Run: off (X)")) ToggleRun();

            // Compass (§6): eases yaw to north, pitch/zoom to defaults.
            Rect compass = new Rect(Screen.width - 46, 10, 36, 36);
            guiRects.Add(compass);
            if (GUI.Button(compass, "N")) compassReset = true;

            DrawStrikeIndicator(me);
        }

        // The verb: rock hardness sets the rhythm; strike the weak point (Space)
        // for clean ore. The indicator predicts where the armed strike lands.
        void DrawStrikeIndicator(PlayerSnap me)
        {
            if (me == null || me.anim != "mine" || Snap == null || mineTargetId == null) return;
            NodeSnap node = Snap.nodes.Find(n => n.id == mineTargetId);
            if (node == null || node.ore <= 0) return;

            long cur = Snap.tick;
            // An armed strike lands on the next swing (~1 tick out); the server
            // grants the weak tick itself and one tick of grace.
            bool window = (cur + 1) % node.tempo == node.phase || (cur + 2) % node.tempo == node.phase;
            int wait = 0;
            if (!window)
            {
                long t = cur + 1;
                while ((t + wait) % node.tempo != node.phase) wait++;
            }

            Rect r = Panel(Screen.width / 2f - 120, Screen.height - 120, 240, 34);
            string msg = window ? "WEAK POINT — Space to strike!" : "listen for the crack…  " + wait;
            GUI.Label(new Rect(r.x + 12, r.y + 8, r.width - 24, 20), msg, window ? headText : panelText);
        }

        static int BagCount(BagSnap bag, string item)
        {
            if (bag == null) return 0;
            foreach (ItemStack it in bag.items) if (it.item == item) return it.qty;
            return 0;
        }

        // Context panel: whatever camp station (or ladder) is in reach.
        void DrawContextPanel()
        {
            PlayerSnap me = Me();
            if (me == null) return;

            if (MyForge() != null) { DrawForgePanel(); return; }

            if (me.band == 0)
            {
                if (MeNear(ZoneServer.StallPos, ZoneServer.StationReach)) DrawStallPanel();
                else if (MeNear(ZoneServer.FurnacePos, ZoneServer.StationReach)) DrawFurnacePanel();
                else if (MeNear(ZoneServer.AnvilPos, ZoneServer.StationReach)) DrawAnvilPanel();
                else if (MeNear(ZoneServer.BoardPos, ZoneServer.StationReach)) DrawBoardPanel();
                else if (MeNear(ZoneServer.VaultPos, ZoneServer.StationReach)) DrawVaultPanel();
                else if (MeNear(ZoneServer.EntrancePos, ZoneServer.StationReach)) DrawLadderPanel(me, true, false);
            }
            else if (MeNear(new Vector2(Bands.All[me.band].originX, Bands.All[me.band].originZ), ZoneServer.StationReach + 2f))
            {
                DrawLadderPanel(me, me.band < Bands.Count - 1, true);
            }
        }

        Rect ContextBox(int rows)
        {
            float h = 34 + rows * 26;
            return Panel(Screen.width - 320, Screen.height - 60 - h, 300, h);
        }

        void DrawStallPanel()
        {
            BagSnap bag = MyBag();
            Rect r = ContextBox(1 + Items.StallStock.Length);
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 20), "MARKET SQUARE · trade post — " + (bag != null ? bag.gold + "g" : ""), headText);
            float y = r.y + 30;
            if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), "Sell everything the keeper wants"))
                SendToServer(Intent.Sell("ALL", 0));
            y += 26;
            foreach (string item in Items.StallStock)
            {
                if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), "Buy " + Items.Pretty(item) + " — " + Items.PriceToBuy(item) + "g"))
                    SendToServer(Intent.Buy(item));
                y += 26;
            }
        }

        void DrawFurnacePanel()
        {
            Rect r = ContextBox(Recipes.Smelting.Length);
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 20), "FURNACE — smelt (grade carries into the bar)", headText);
            float y = r.y + 30;
            foreach (SmeltRecipe rec in Recipes.Smelting)
            {
                if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), rec.label))
                    SendToServer(Intent.Smelt(rec.id));
                y += 26;
            }
        }

        void DrawAnvilPanel()
        {
            Rect r = ContextBox(Recipes.Forging.Length);
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 20), "ANVIL — forge (bar grade sets the ceiling)", headText);
            float y = r.y + 30;
            foreach (ForgeRecipe rec in Recipes.Forging)
            {
                if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), rec.label))
                    SendToServer(Intent.Forge(rec.id));
                y += 26;
            }
        }

        // Live forge session: heat shown as colour (read the steel, not a gauge).
        void DrawForgePanel()
        {
            ForgeSnap f = MyForge();
            if (f == null) return;
            Rect r = ContextBox(4);
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 20), "AT THE ANVIL — " + f.strikes + "/" + ZoneServer.ForgeStrikesNeeded + " strikes, " + f.flaws + " flaws", headText);

            // Steel colour: black -> dull red -> orange -> yellow -> searing white.
            float t = f.heat / 100f;
            Color steel = t < 0.4f ? Color.Lerp(Geo.Hex(0x1a1210), Geo.Hex(0x7a2a18), t / 0.4f)
                : t < 0.7f ? Color.Lerp(Geo.Hex(0x7a2a18), Geo.Hex(0xe8a030), (t - 0.4f) / 0.3f)
                : t < 0.9f ? Color.Lerp(Geo.Hex(0xe8a030), Geo.Hex(0xf8e8a0), (t - 0.7f) / 0.2f)
                : Color.Lerp(Geo.Hex(0xf8e8a0), Color.white, (t - 0.9f) / 0.1f);
            GUI.DrawTexture(new Rect(r.x + 10, r.y + 30, r.width - 20, 18), barBg);
            Color prev = GUI.color;
            GUI.color = steel;
            GUI.DrawTexture(new Rect(r.x + 12, r.y + 32, (r.width - 24) * Mathf.Clamp01(t), 14), barFill);
            GUI.color = prev;

            float y = r.y + 54;
            if (f.awaitingQuench)
            {
                GUI.Label(new Rect(r.x + 10, y, r.width - 20, 20), "IT'S DONE — QUENCH NOW (Q)!", warnText);
                y += 26;
                if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), "Quench (Q)"))
                    SendToServer(Intent.Quench());
            }
            else
            {
                if (GUI.Button(new Rect(r.x + 10, y, (r.width - 30) / 2f, 22), "Pump (F)"))
                    SendToServer(Intent.Pump());
                if (GUI.Button(new Rect(r.x + 20 + (r.width - 30) / 2f, y, (r.width - 30) / 2f, 22), "Hammer (Space)"))
                    SendToServer(Intent.Hammer());
                y += 26;
                GUI.Label(new Rect(r.x + 10, y, r.width - 20, 20), "hammer in the orange; white burns the billet", panelText);
            }
        }

        void DrawBoardPanel()
        {
            Snapshot s = Snap;
            ContractSnap c = s != null && s.contracts.TryGetValue("you", out ContractSnap cc) ? cc : null;
            Rect r = ContextBox(c == null ? 1 : 3);
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 20), "WAYFARERS' GUILDHALL — work orders", headText);
            float y = r.y + 30;
            if (c == null)
            {
                GUI.Label(new Rect(r.x + 10, y, r.width - 20, 20), "no orders posted — the caravan rolls in soon", panelText);
                return;
            }
            long left = c.deadline - (s != null ? s.tick : 0);
            GUI.Label(new Rect(r.x + 10, y, r.width - 20, 20), c.qty + "× " + Items.Pretty(c.item) + " — " + c.gold + "g  (" + left + " ticks)", panelText);
            y += 26;
            if (!c.accepted)
            {
                if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), "Sign the contract"))
                    SendToServer(Intent.AcceptContract());
            }
            else if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), "Deliver the order"))
            {
                SendToServer(Intent.DeliverContract());
            }
        }

        // The Vault: banked goods live in character truth, out of cave-in reach.
        void DrawVaultPanel()
        {
            BagSnap bag = MyBag();
            int banked = 0;
            if (bag != null) foreach (ItemStack it in bag.vault) banked += it.qty;
            Rect r = ContextBox(3);
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 20), "THE VAULT — bank · " + banked + " items in your box", headText);
            float y = r.y + 30;
            if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), "Deposit valuables (ore · bars · blades · gems)"))
                SendToServer(Intent.VaultDeposit());
            y += 26;
            if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), "Withdraw everything"))
                SendToServer(Intent.VaultWithdraw());
            y += 26;
            GUI.Label(new Rect(r.x + 10, y, r.width - 20, 20), "banked goods are safe from cave-ins", panelText);
        }

        void DrawLadderPanel(PlayerSnap me, bool canDown, bool canUp)
        {
            int rows = (canDown ? 1 : 0) + (canUp ? 1 : 0);
            if (rows == 0) return;
            Rect r = ContextBox(rows);
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 20), me.band == 0 ? "MINE ENTRANCE" : "THE SHAFT — " + Bands.All[me.band].name, headText);
            float y = r.y + 30;
            if (canDown)
            {
                string below = Bands.All[me.band + 1].name;
                if (GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), "Descend to " + below + " (E)"))
                    SendToServer(Intent.Descend());
                y += 26;
            }
            if (canUp && GUI.Button(new Rect(r.x + 10, y, r.width - 20, 22), "Climb up (R)"))
                SendToServer(Intent.Ascend());
        }

        void DrawBag()
        {
            BagSnap bag = MyBag();
            int rows = bag != null ? Mathf.Max(1, bag.items.Count) : 1;
            float h = 40 + rows * 18;
            Rect r = Panel(Screen.width - 250, 54, 240, h);
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 20), "BAG — " + (bag != null ? bag.gold + "g" : ""), headText);
            float y = r.y + 28;
            if (bag == null || bag.items.Count == 0)
            {
                GUI.Label(new Rect(r.x + 10, y, r.width - 20, 18), "empty — the rocks await", panelText);
                return;
            }
            foreach (ItemStack it in bag.items)
            {
                GUI.Label(new Rect(r.x + 10, y, r.width - 20, 18), it.qty + "× " + Items.Pretty(it.item), panelText);
                y += 18;
            }
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

            // District and landmark signs.
            foreach (KeyValuePair<string, Vector3> sign in world.Signs)
                DrawWorldLabel(sign.Value, sign.Key, panelText);

            // Prospect notes float over their rocks while known.
            GameClient g = gameClient;
            foreach (KeyValuePair<string, string> kv in g.ProspectNotes)
            {
                if (!world.RockGroups.TryGetValue(kv.Key, out GameObject rock) || !rock.activeSelf) continue;
                Vector3 p = rock.transform.position;
                DrawWorldLabel(new Vector3(p.x, 2.0f, p.z), kv.Value, panelText);
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
            Rect input = new Rect(12, Screen.height - 30, 320, 22);
            guiRects.Add(input);
            chatDraft = GUI.TextField(input, chatDraft, 120);
            if (e.type == UnityEngine.EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
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
            if (runPref) SendToServer(Intent.SetRun(true));   // re-sync the toggle
            gameClient.status = "Server restarted — characters, bags, gold and rocks reloaded.";
        }

        void OnApplicationQuit()
        {
            if (server != null) server.Save();
        }
    }
}
