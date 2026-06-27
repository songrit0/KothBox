// ============================================================================
//  KothBox UI generator (Unturned / KothBox RocketMod plugin)
//
//  Put this file in your Unturned-SDK Unity project under:  Assets/Editor/
//  Then run the menu:
//      Unturned KothUI / 1. Generate Loadout Panel
//      Unturned KothUI / 2. Generate HUD
//  Both prefabs are tagged into the "kothui.masterbundle" asset bundle; build
//  it with the Master Bundle Tool and publish to the Steam Workshop. Put the
//  two resulting EffectAsset ids into KothBox.configuration.xml
//  (LoadoutUIEffectId, HudEffectId).
//
//  Element names MUST match KothUI.cs:
//    Loadout panel  : buttons Loadout_0..Loadout_5, KothClose;
//                     texts  Loadout_0_Name..Loadout_5_Name, Koth_PickTitle
//    HUD            : texts  Koth_Rank, Koth_MyTime, Koth_Countdown
//
//  CRITICAL: each root GameObject is named "Effect" (Unturned UI effect contract).
//  Unity 2021.3.x => MasterBundle Asset_Bundle_Version 5 (old 2021 bundles still load).
// ============================================================================
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KothBoxEditor
{
    public static class BuildKothUI
    {
        private const string BundleName = "kothui.masterbundle";
        private const string Root       = "Assets/KothUI";
        private const string LoadoutPrefab = Root + "/Loadout/Effect.prefab";
        private const string HudPrefab     = Root + "/Hud/Effect.prefab";

        private static readonly Color Panel   = new Color(0f, 0f, 0f, 0.45f); // faint translucent black
        private static readonly Color BtnDark  = new Color(0.11f, 0.12f, 0.15f, 1f);
        private static readonly Color XDark    = new Color(0.22f, 0.10f, 0.10f, 1f);
        private static readonly Color Outline  = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color Green    = new Color(0.35f, 0.92f, 0.50f);
        private static readonly Color Red      = new Color(0.95f, 0.40f, 0.40f);
        private static readonly Color Gold     = new Color(0.97f, 0.82f, 0.30f);
        private static Sprite _round;

        // ---- 1. Loadout pick panel ------------------------------------------
        [MenuItem("Unturned KothUI/1. Generate Loadout Panel")]
        public static void GenerateLoadout()
        {
            var dir = Path.GetDirectoryName(LoadoutPrefab);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _round = MakeRoundedSprite();

            var root = NewCanvas();

            const float W = 360f, H = 420f;
            var panel = NewRound("Panel", root.transform, Panel);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(W, H);
            prt.anchoredPosition = Vector2.zero;

            var title = NewText("Koth_PickTitle", panel.transform, "PICK YOUR LOADOUT", 47,
                FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            Anchor(title.rectTransform, 0.05f, 0.90f, 0.95f, 0.99f);

            // 6 stacked loadout buttons: icon (left) + name (right). Server hides unused ones.
            // ASSIGN the gun sprite to each "Loadout_N_Icon" Image in Unity (dynamic item icons
            // can't be pushed to a workshop UI at runtime).
            const int n = 6;
            const float top = 0.88f, bot = 0.18f, gap = 0.012f;
            float slot = (top - bot) / n;
            for (int i = 0; i < n; i++)
            {
                float hi = top - i * slot - gap;
                float lo = top - (i + 1) * slot + gap;

                var img = NewRound("Loadout_" + i, panel.transform, BtnDark);
                AddOutline(img, Outline);
                var btn = img.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                Anchor(img.rectTransform, 0.06f, lo, 0.94f, hi);

                // Icon slot (left) — server pushes URL via sendUIEffectImageURL (RawImage required).
                var iconGo = new GameObject("Loadout_" + i + "_Icon",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                iconGo.transform.SetParent(img.transform, false);
                var ri = iconGo.GetComponent<RawImage>();
                ri.color = new Color(1f, 1f, 1f, 0.85f);
                ri.raycastTarget = false;
                Anchor(iconGo.GetComponent<RectTransform>(), 0.02f, 0.12f, 0.26f, 0.88f);

                var name = NewText("Loadout_" + i + "_Name", img.transform, "Loadout " + i, 43,
                    FontStyle.Bold, TextAnchor.MiddleLeft, Green);
                Anchor(name.rectTransform, 0.30f, 0f, 0.97f, 1f);
            }

            // Instruction (how to use the mouse overlay).
            var hint = NewText("Hint", panel.transform,
                "TO INTERACT WITH THE UI OVERLAY\nHOLD 'C' AND CLICK THE BUTTONS", 37,
                FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.95f, 0.9f));
            Anchor(hint.rectTransform, 0.05f, 0.072f, 0.95f, 0.19f);
            hint.resizeTextForBestFit = true;
            hint.resizeTextMinSize = 8;

            var close = MakeButton("KothClose", panel.transform, "CLOSE", XDark, Red, 41, "Label", Red);
            Anchor((RectTransform)close.transform, 0.30f, 0.002f, 0.70f, 0.058f);

            SavePrefab(root, LoadoutPrefab);
            Debug.Log("[KothUI] Loadout panel -> " + LoadoutPrefab + " (bundle '" + BundleName + "').");
        }

        // ---- 2. HUD ----------------------------------------------------------
        [MenuItem("Unturned KothUI/2. Generate HUD")]
        public static void GenerateHud()
        {
            var dir = Path.GetDirectoryName(HudPrefab);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _round = MakeRoundedSprite();

            var root = NewCanvas();

            // Panel pinned top-LEFT (so it doesn't cover the centre compass) — stats + scoreboard.
            var panel = NewRound("Panel", root.transform, new Color(0f, 0f, 0f, 0.4f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.sizeDelta = new Vector2(230f, 250f);
            prt.anchoredPosition = new Vector2(12f, -447f);

            var rank = NewText("Koth_Rank", panel.transform, "#1 / 1", 43, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            Anchor(rank.rectTransform, 0.04f, 0.90f, 0.96f, 0.99f);
            var time = NewText("Koth_MyTime", panel.transform, "0s", 39, FontStyle.Bold, TextAnchor.MiddleCenter, Green);
            Anchor(time.rectTransform, 0.04f, 0.82f, 0.50f, 0.89f);
            var cd = NewText("Koth_Countdown", panel.transform, "WARMUP 0s", 37, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Anchor(cd.rectTransform, 0.50f, 0.82f, 0.96f, 0.89f);
            var pool = NewText("Koth_Pool", panel.transform, "Pool 0", 37, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            Anchor(pool.rectTransform, 0.04f, 0.74f, 0.50f, 0.81f);
            var fee = NewText("Koth_Fee", panel.transform, "Fee 0", 37, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Anchor(fee.rectTransform, 0.50f, 0.74f, 0.96f, 0.81f);

            // Scoreboard: top-5 kills (server pushes multi-line string, 5 rows max).
            var sbCap = NewText("ScoreCap", panel.transform, "PLAYERS — KILLS", 36, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.8f,0.8f,0.85f,1f));
            Anchor(sbCap.rectTransform, 0.06f, 0.67f, 0.96f, 0.73f);
            var board = NewText("Koth_Scoreboard", panel.transform, "", 38, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
            board.horizontalOverflow = HorizontalWrapMode.Overflow;
            board.verticalOverflow = VerticalWrapMode.Overflow;
            board.resizeTextForBestFit = false;
            board.lineSpacing = 1.05f;
            Anchor(board.rectTransform, 0.06f, 0.20f, 0.96f, 0.66f);

            // Kill streak section: dark sub-panel + 2-line text ("N kill\n>X: item").
            var streakBg = NewRound("Streak_Bg", panel.transform, new Color(0.08f, 0.10f, 0.13f, 0.85f));
            AddOutline(streakBg, new Color(1f, 0.75f, 0.15f, 0.25f)); // faint gold border
            Anchor(streakBg.rectTransform, 0.04f, 0.01f, 0.96f, 0.18f);

            var streak = NewText("Koth_Streak", streakBg.transform, "", 34, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            streak.horizontalOverflow = HorizontalWrapMode.Wrap;
            streak.verticalOverflow   = VerticalWrapMode.Overflow;
            streak.resizeTextForBestFit = true;
            streak.resizeTextMinSize = 8;
            streak.resizeTextMaxSize = 34;
            Stretch(streak.rectTransform);

            // Prep room button — inside streakBg, same space as Koth_Streak (server swaps visibility).
            var prepBtn = MakeButton("Koth_PrepBtn", streakBg.transform, "GO TO PREP ROOM",
                new Color(0.10f, 0.22f, 0.12f, 1f), new Color(0.30f, 0.92f, 0.40f, 1f),
                37, "Label", new Color(0.30f, 0.92f, 0.40f, 1f));
            Stretch((RectTransform)prepBtn.transform);

            // ---- Kill flash (hidden by default, shown 2.5s after each kill) ----
            var kcGo = new GameObject("Koth_KillCounter", typeof(RectTransform));
            kcGo.transform.SetParent(root.transform, false);
            kcGo.SetActive(false); // hidden until server shows it
            var kcRt = (RectTransform)kcGo.transform;
            kcRt.anchorMin = kcRt.anchorMax = new Vector2(0.5f, 0.5f);
            kcRt.pivot = new Vector2(0.5f, 0.5f);
            kcRt.sizeDelta = new Vector2(100f, 36f);
            kcRt.anchoredPosition = new Vector2(0f, -72f);

            // Skull icon.
            var skullGo = new GameObject("Koth_KillIcon",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            skullGo.transform.SetParent(kcGo.transform, false);
            var skullImg = skullGo.GetComponent<Image>();
            var skullSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/KnockdownUI/Icons/skull.png");
            if (skullSprite != null) skullImg.sprite = skullSprite;
            skullImg.color = new Color(1f, 1f, 1f, 0.9f);
            skullImg.raycastTarget = false;
            Anchor((RectTransform)skullGo.transform, 0f, 0.05f, 32f/100f, 0.95f);

            // Kill count text.
            var killCt = NewText("Koth_KillCount", kcGo.transform, "0",
                43, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            AddOutline(killCt, new Color(0f, 0f, 0f, 0.85f));
            Anchor(killCt.rectTransform, 35f/100f, 0f, 1f, 1f);

            SavePrefab(root, HudPrefab);
            Debug.Log("[KothUI] HUD -> " + HudPrefab + " (bundle '" + BundleName + "').");
        }

        // ---- 4. Game menu / lobby -------------------------------------------
        [MenuItem("Unturned KothUI/4. Generate Game Menu")]
        public static void GenerateGameMenu()
        {
            var path = Root + "/Menu/Effect.prefab";
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _round = MakeRoundedSprite();

            var root = NewCanvas();
            var panel = NewRound("Panel", root.transform, Panel);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(420f, 320f);
            prt.anchoredPosition = Vector2.zero;

            var title = NewText("Title", panel.transform, "PVP DEATHMATCH", 47, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            Anchor(title.rectTransform, 0.05f, 0.88f, 0.95f, 0.98f);

            // (No JOIN button — players join via the GameMenu KOTH tile -> /jkoth, or /jkoth.)

            // Host row (server toggles visible for admins): fee presets + selected + START.
            var hostRow = NewRound("Koth_HostRow", panel.transform, new Color(0f, 0f, 0f, 0.3f));
            Anchor(hostRow.rectTransform, 0.05f, 0.13f, 0.95f, 0.85f);
            var hostCap = NewText("HostCap", hostRow.transform, "HOST — entry fee", 37, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            Anchor(hostCap.rectTransform, 0.05f, 0.80f, 0.95f, 0.97f);
            // -100 / -10 | [price] | +10 / +100  — free-input via step buttons
            var bM100 = MakeButton("Koth_Fee_M100", hostRow.transform, "-100", BtnDark, Red,    38, "Label", Outline);
            var bM10  = MakeButton("Koth_Fee_M10",  hostRow.transform, "-10",  BtnDark, Red,    38, "Label", Outline);
            var bP10  = MakeButton("Koth_Fee_P10",  hostRow.transform, "+10",  BtnDark, Green,  38, "Label", Outline);
            var bP100 = MakeButton("Koth_Fee_P100", hostRow.transform, "+100", BtnDark, Green,  38, "Label", Outline);
            Anchor((RectTransform)bM100.transform, 0.03f, 0.45f, 0.22f, 0.78f);
            Anchor((RectTransform)bM10.transform,  0.24f, 0.45f, 0.43f, 0.78f);
            Anchor((RectTransform)bP10.transform,  0.57f, 0.45f, 0.76f, 0.78f);
            Anchor((RectTransform)bP100.transform, 0.78f, 0.45f, 0.97f, 0.78f);

            var sel = NewText("Koth_SelFee", hostRow.transform, "50", 41, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            Anchor(sel.rectTransform, 0.43f, 0.45f, 0.57f, 0.78f);
            var start = MakeButton("Koth_Start", hostRow.transform, "START", BtnDark, Green, 43, "Label", Green);
            Anchor((RectTransform)start.transform, 0.50f, 0.06f, 0.95f, 0.42f);

            var close = MakeButton("KothMenuClose", panel.transform, "CLOSE", XDark, Red, 39, "Label", Red);
            Anchor((RectTransform)close.transform, 0.35f, 0.02f, 0.65f, 0.11f);

            SavePrefab(root, path);
            Debug.Log("[KothUI] Game menu -> " + path);
        }

        // ---- 5. Gacha result panel ------------------------------------------
        [MenuItem("Unturned KothUI/5. Generate Gacha Result")]
        public static void GenerateGachaResult()
        {
            var path = Root + "/Gacha/Effect.prefab";
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _round = MakeRoundedSprite();

            var root = NewCanvas();
            var panel = NewRound("Panel", root.transform, new Color(0f, 0f, 0f, 0.45f));
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(420f, 96f);
            prt.anchoredPosition = new Vector2(0f, -130f);

            var title = NewText("Title", panel.transform, "LUCKY DRAW", 41, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            Anchor(title.rectTransform, 0.05f, 0.68f, 0.95f, 0.95f);
            var winner = NewText("Koth_GachaWinner", panel.transform, "Winner", 43, FontStyle.Bold, TextAnchor.MiddleCenter, Green);
            Anchor(winner.rectTransform, 0.05f, 0.38f, 0.95f, 0.66f);
            var prize = NewText("Koth_GachaPrize", panel.transform, "Prize", 41, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Anchor(prize.rectTransform, 0.05f, 0.05f, 0.95f, 0.36f);

            SavePrefab(root, path);
            Debug.Log("[KothUI] Gacha result -> " + path);
        }

        // ---- 3. Dome shell (white + green) -----------------------------------
        //  Baked at this radius. Unturned can't scale a world effect at runtime, so
        //  set your box to the SAME radius (e.g. /setkothbox arena 50) — or change
        //  DomeRadius below and re-run for a different arena size.
        private const float DomeRadius = 50f;

        [MenuItem("Unturned KothUI/3. Generate Dome (white + green)")]
        public static void GenerateDome()
        {
            string domeDir = Root + "/Dome";
            if (!Directory.Exists(domeDir)) Directory.CreateDirectory(domeDir);

            // PARTICLE dome: a static-mesh sphere does NOT render as an Unturned world effect
            // (confirmed via /testdome — asset loads but nothing shows). Particle systems DO render
            // (same as TeamPing markers), so the shell is a sphere of billboarded dots.
            var dot = MakeDotTexture(domeDir);
            var matWhite = MakeParticleMaterial(domeDir, "DomeWhite_Mat", dot);
            var matGreen = MakeParticleMaterial(domeDir, "DomeGreen_Mat", dot);

            BuildDomePrefab(domeDir + "/DomeWhite/Effect.prefab", matWhite, new Color(0.85f, 0.92f, 1f, 0.85f));
            BuildDomePrefab(domeDir + "/DomeGreen/Effect.prefab", matGreen, new Color(0.30f, 1f, 0.45f, 0.85f));

            Debug.Log("[KothUI] Particle dome (white+green) generated at radius " + DomeRadius +
                      " into '" + BundleName + "'. Set your box radius to " + DomeRadius + ".");
        }

        // Sprite with only left or right corners rounded (for first/last progress bar segment).
        private static Sprite MakeSideRoundedSprite(string assetPath, bool roundLeft, bool roundRight)
        {
            int size = 48, r = 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = 1f;
                    int cx = -1, cy = -1;
                    if (roundLeft  && x < r       && y < r)            { cx = r;          cy = r; }
                    else if (roundRight && x >= size - r && y < r)     { cx = size-r-1;   cy = r; }
                    else if (roundLeft  && x < r       && y >= size-r) { cx = r;          cy = size-r-1; }
                    else if (roundRight && x >= size-r && y >= size-r) { cx = size-r-1;   cy = size-r-1; }
                    if (cx >= 0)
                    {
                        float d = Mathf.Sqrt((x-cx)*(x-cx) + (y-cy)*(y-cy));
                        a = Mathf.Clamp01(r - d + 0.5f);
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spriteBorder = Vector4.zero; // simple (not sliced)
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
            var ti = AssetImporter.GetAtPath(assetPath);
            if (ti != null) { ti.assetBundleName = BundleName; ti.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Soft white dot for the particles (radial alpha falloff).
        private static Texture2D MakeDotTexture(string dir)
        {
            string path = dir + "/dot.png";
            int s = 32; float c = (s - 1) / 2f;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a; // softer edge
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // PROVEN: TeamPing's particles render with "Legacy Shaders/Particles/Additive" (the shader
        // family Unturned's own native effects use). Alpha Blended did NOT render in our bundle.
        // Material saved as a real .mat asset so the bundle build doesn't strip the shader.
        private static Material MakeParticleMaterial(string dir, string name, Texture2D dot)
        {
            string path = dir + "/" + name + ".mat";
            AssetDatabase.DeleteAsset(path); // re-run safe (old mesh-era material at same path)
            var sh = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive");
            var mat = new Material(sh) { mainTexture = dot };
            mat.enableInstancing = true;
            AssetDatabase.CreateAsset(mat, path);
            var ti = AssetImporter.GetAtPath(path);
            if (ti != null) { ti.assetBundleName = BundleName; ti.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        // Root "Effect" IS the ParticleSystem (matches TeamPing Pulse, which renders). Bursts a
        // sphere SHELL of additive billboarded dots at DomeRadius. No collider/physics.
        private static void BuildDomePrefab(string prefabPath, Material mat, Color tint)
        {
            var dir = Path.GetDirectoryName(prefabPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var root = new GameObject("Effect");
            var ps = root.AddComponent<ParticleSystem>();   // on the ROOT, like TeamPing

            var main = ps.main;
            main.loop = false;
            main.duration = 2f;
            main.startLifetime = 2f;
            main.startSpeed = 0f;
            main.startSize = DomeRadius * 0.12f;            // ~6m dots at r=50
            main.startColor = tint;
            main.maxParticles = 3000;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1200) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = DomeRadius;
            shape.radiusThickness = 0f;                     // emit from the shell surface only

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sharedMaterial = mat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            var importer = AssetImporter.GetAtPath(prefabPath);
            importer.assetBundleName = BundleName;
            importer.SaveAndReimport();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
        }

        // ---- 7. Kill-streak progress panel ---------------------------------
        //
        //  Bottom-centre of screen. Shows:
        //   - Up to 6 milestone boxes (Streak_N): item name + "N kill" label + green Done overlay.
        //   - 10 fill segments (Streak_Seg_0..9): server shows K of them for kill progress.
        //   - Kill-count text (Streak_KillText): "2 kill".
        //   - AutoEquip toggle button (Streak_AutoEquip) + on-indicator (Streak_AE_On).
        //
        //  Element names MUST match KothUI.cs:
        //    Streak_N (Image)         visibility = has milestone at slot N
        //    Streak_N_Name (Text)     item name
        //    Streak_N_Kills (Text)    "3 kill"
        //    Streak_N_Done (Image)    shown when milestone N is achieved
        //    Streak_Seg_0..9 (Image)  progress bar fill
        //    Streak_KillText (Text)   current kills
        //    Streak_AutoEquip (Button) toggle
        //    Streak_AE_On (Image)     visible when auto-equip is ON
        private const int MaxStreakSlots = 6;

        [MenuItem("Unturned KothUI/7. Generate Streak Panel")]
        public static void GenerateStreakPanel()
        {
            const string path = Root + "/Streak/Effect.prefab";
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _round = MakeRoundedSprite();

            var root = NewCanvas();

            // Panel: anchored bottom-right, 15px from right edge, 10px from bottom.
            const float PW = 540f, PH = 190f;
            var panel = NewRound("Panel", root.transform, new Color(0f, 0f, 0f, 0f)); // fully transparent
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(1f, 0f);
            prt.sizeDelta = new Vector2(PW, PH);
            prt.anchoredPosition = new Vector2(-15f, 147f);

            // ---- milestone boxes ----
            // 6 slots of equal width, spanning top 60% of the panel.
            const float slotW = 82f, slotH = 105f;
            float totalSlotsW = MaxStreakSlots * slotW + (MaxStreakSlots - 1) * 4f; // 4px gap
            float startX = (PW - totalSlotsW) / 2f; // left edge of first slot in panel-local px

            for (int i = 0; i < MaxStreakSlots; i++)
            {
                float slotCx = startX + i * (slotW + 4f) + slotW / 2f; // centre-x in panel px

                // Convert to anchors
                float ax0 = (slotCx - slotW / 2f) / PW;
                float ax1 = (slotCx + slotW / 2f) / PW;
                float ay0 = (PH - slotH - 6f) / PH; // top-aligned with 6px margin
                float ay1 = (PH - 6f) / PH;

                var box = NewRound("Streak_" + i, panel.transform, new Color(0.18f, 0.20f, 0.24f, 1f));
                AddOutline(box, new Color(1f, 1f, 1f, 0.12f));
                Anchor(box.rectTransform, ax0, ay0, ax1, ay1);

                // Green achieved overlay (hidden by default, server shows it on milestone reached).
                var done = NewRound("Streak_" + i + "_Done", box.transform, new Color(0.25f, 0.85f, 0.35f, 0.30f));
                Stretch(done.rectTransform);

                // Item icon (RawImage — server pushes URL via sendUIEffectImageURL).
                var iconGo = new GameObject("Streak_" + i + "_Name",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                iconGo.transform.SetParent(box.transform, false);
                var ri = iconGo.GetComponent<RawImage>();
                ri.color = Color.white;
                ri.raycastTarget = false;
                Anchor(iconGo.GetComponent<RectTransform>(), 0.08f, 0.44f, 0.92f, 0.96f);

                // Kill threshold — lower strip with darker bg.
                var killBg = NewRound("KillBg_" + i, box.transform, new Color(0f, 0f, 0f, 0.35f));
                Anchor(killBg.rectTransform, 0f, 0f, 1f, 0.42f);
                var kills = NewText("Streak_" + i + "_Kills", killBg.transform, "0 kill",
                    36, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
                Stretch(kills.rectTransform);
            }

            // ---- progress bar ----
            const float barH = 28f, barY0 = 32f, barMx = 16f;
            float barAy0 = barY0 / PH, barAy1 = (barY0 + barH) / PH;

            // Background.
            var barBg = NewRound("Streak_BarBg", panel.transform, new Color(0.12f, 0.13f, 0.15f, 1f));
            Anchor(barBg.rectTransform, barMx / PW, barAy0, (PW - barMx) / PW, barAy1);

            // 10 fill segments — seamless; Seg_0 rounds left only, Seg_9 rounds right only, rest flat.
            float segTotalW = PW - 2f * barMx;
            float segW = segTotalW / 10f;
            var sprLeft  = MakeSideRoundedSprite(Root + "/round_left.png",  roundLeft: true,  roundRight: false);
            var sprRight = MakeSideRoundedSprite(Root + "/round_right.png", roundLeft: false, roundRight: true);
            var greenSeg = new Color(0.25f, 0.88f, 0.35f, 1f);
            for (int s = 0; s < 10; s++)
            {
                float sx0 = (barMx + s       * segW) / PW;
                float sx1 = (barMx + (s + 1) * segW) / PW; // no gap — seamless
                var segGo = new GameObject("Streak_Seg_" + s,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                segGo.transform.SetParent(panel.transform, false);
                var img = segGo.GetComponent<Image>();
                img.color = greenSeg;
                img.raycastTarget = false;
                if (s == 0)          { img.sprite = sprLeft;  img.type = Image.Type.Simple; }
                else if (s == 9)     { img.sprite = sprRight; img.type = Image.Type.Simple; }
                // else: sprite = null → plain filled rect, no rounding
                Anchor(segGo.GetComponent<RectTransform>(), sx0, barAy0 + 0.01f, sx1, barAy1 - 0.01f);
            }

            // Kill-count text (centred on the bar).
            var killTxt = NewText("Streak_KillText", panel.transform, "0 kill",
                38, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Anchor(killTxt.rectTransform, barMx / PW, barAy0, (PW - barMx) / PW, barAy1);

            // ---- AutoEquip toggle switch ----
            const float aeY0 = 6f, aeH = 22f;
            const float trackX0 = 158f, trackW = 60f, knobSz = 18f;

            // Static label.
            var aeLabel = NewText("Streak_AE_Label", panel.transform, "AUTO EQUIP", 35,
                FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.85f, 0.85f, 0.90f, 1f));
            Anchor(aeLabel.rectTransform, 10f/PW, aeY0/PH, 150f/PW, (aeY0+aeH)/PH);

            // Track background (gray pill, always visible).
            var aeTrack = NewRound("Streak_AE_Track", panel.transform, new Color(0.20f, 0.21f, 0.24f, 1f));
            Anchor(aeTrack.rectTransform, trackX0/PW, aeY0/PH, (trackX0+trackW)/PW, (aeY0+aeH)/PH);

            // Green overlay (shown when ON).
            var aeOn = NewRound("Streak_AE_On", panel.transform, new Color(0.25f, 0.78f, 0.35f, 1f));
            Anchor(aeOn.rectTransform, trackX0/PW, aeY0/PH, (trackX0+trackW)/PW, (aeY0+aeH)/PH);

            // Knob — left position, shown when OFF.
            var aeKnobOff = NewRound("Streak_AE_KnobOff", panel.transform, Color.white);
            Anchor(aeKnobOff.rectTransform,
                (trackX0+2f)/PW, (aeY0+2f)/PH,
                (trackX0+2f+knobSz)/PW, (aeY0+2f+knobSz)/PH);

            // Knob — right position, shown when ON.
            var aeKnob = NewRound("Streak_AE_Knob", panel.transform, Color.white);
            Anchor(aeKnob.rectTransform,
                (trackX0+trackW-2f-knobSz)/PW, (aeY0+2f)/PH,
                (trackX0+trackW-2f)/PW, (aeY0+2f+knobSz)/PH);

            // Transparent hit-area button (topmost — captures clicks on label+track row).
            var aeBtn = MakeButton("Streak_AutoEquip", panel.transform, "",
                new Color(0f,0f,0f,0.01f), new Color(0f,0f,0f,0.01f),
                35, "Label", new Color(0f,0f,0f,0f));
            Anchor((RectTransform)aeBtn.transform, 6f/PW, (aeY0-2f)/PH, (trackX0+trackW+4f)/PW, (aeY0+aeH+2f)/PH);

            SavePrefab(root, path);
            Debug.Log("[KothUI] Streak panel -> " + path + " (bundle '" + BundleName + "').");
        }

        // ---- 6. Floating open-menu button ----------------------------------
        [MenuItem("Unturned KothUI/6. Generate Menu Button")]
        public static void GenerateMenuButton()
        {
            var path = Root + "/Button/Effect.prefab";
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _round = MakeRoundedSprite();

            var root = NewCanvas();
            // Small button pinned to the right edge, vertically centred.
            var btn = MakeButton("Koth_OpenMenu", root.transform, "PVP", new Color(0f, 0f, 0f, 0.5f), Gold, 41, "Label", Gold);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(70f, 44f);
            rt.anchoredPosition = new Vector2(-8f, 0f);

            SavePrefab(root, path);
            Debug.Log("[KothUI] Menu button -> " + path);
        }

        // ---- builders --------------------------------------------------------
        private static GameObject NewCanvas()
        {
            var root = new GameObject("Effect",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return root;
        }

        private static void SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            var importer = AssetImporter.GetAtPath(path);
            importer.assetBundleName = BundleName;
            importer.SaveAndReimport();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
        }

        private static Image NewRound(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = _round;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        // Plain image with NO sprite — a placeholder slot. Assign the item icon sprite in Unity.
        private static Image NewPlainImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        private static void AddOutline(Graphic g, Color c)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = c;
            o.effectDistance = new Vector2(1.6f, -1.6f);
        }

        // Load Icons/loadout_N.png as a Sprite (import as Sprite + tag into the bundle if needed).
        private static Sprite LoadIconSprite(int i)
        {
            string path = Root + "/Icons/loadout_" + i + ".png";
            if (!File.Exists(path)) return null;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool dirty = false;
                if (imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; dirty = true; }
                if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; dirty = true; }
                if (imp.assetBundleName != BundleName) { imp.assetBundleName = BundleName; dirty = true; }
                if (dirty) imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Text NewText(string name, Transform parent, string content, int size, FontStyle style,
                                    TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = content;
            t.font = GetFont();
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 2;
            t.resizeTextMaxSize = size;
            return t;
        }

        private static Button MakeButton(string name, Transform parent, string label, Color bg, Color textColor,
                                         int fontSize, string labelName, Color? outline = null)
        {
            var img = NewRound(name, parent, bg);
            var o = img.gameObject.AddComponent<Outline>();
            o.effectColor = outline ?? Outline;
            o.effectDistance = new Vector2(1.6f, -1.6f);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            c.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            c.fadeDuration = 0.06f;
            btn.colors = c;
            var txt = NewText(labelName, img.transform, label, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, textColor);
            Stretch(txt.rectTransform);
            return btn;
        }

        private static void Anchor(RectTransform rt, float minX, float minY, float maxX, float maxY)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Sprite MakeRoundedSprite()
        {
            const string path = Root + "/round.png";
            if (!Directory.Exists(Root)) Directory.CreateDirectory(Root);
            int size = 48, r = 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int xx = 0; xx < size; xx++)
                {
                    float a = 1f; int cx = -1, cy = -1;
                    if (xx < r && y < r) { cx = r; cy = r; }
                    else if (xx >= size - r && y < r) { cx = size - r - 1; cy = r; }
                    else if (xx < r && y >= size - r) { cx = r; cy = size - r - 1; }
                    else if (xx >= size - r && y >= size - r) { cx = size - r - 1; cy = size - r - 1; }
                    if (cx >= 0)
                    {
                        float d = Mathf.Sqrt((xx - cx) * (xx - cx) + (y - cy) * (y - cy));
                        a = Mathf.Clamp01(r - d + 0.5f);
                    }
                    px[y * size + xx] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spriteBorder = new Vector4(r, r, r, r);
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Font _font;
        private static Font GetFont()
        {
            if (_font != null) return _font;

            // Use 2005_iannnnnAMD as the primary font (normal digits — Pixelify renders "5" as "$" —
            // and full Thai support). It must ride in THIS bundle so it syncs on the dedicated server.
            string fontDir = Root + "/Fonts";
            string thPath = fontDir + "/2005_iannnnnAMD.ttf";
            EnsureFont(thPath, "2005_iannnnnAMD.ttf");

            Font th = File.Exists(thPath) ? AssetDatabase.LoadAssetAtPath<Font>(thPath) : null;
            if (th != null)
            {
                try { var ti = AssetImporter.GetAtPath(thPath); if (ti != null && ti.assetBundleName != BundleName) { ti.assetBundleName = BundleName; ti.SaveAndReimport(); } } catch { }
                return _font = AssetDatabase.LoadAssetAtPath<Font>(thPath);
            }
#if UNITY_2022_2_OR_NEWER
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            return _font;
        }

        // Copy a font from any existing UI bundle's Fonts folder into KothUI/Fonts.
        private static void EnsureFont(string destAssetPath, string fileName)
        {
            if (File.Exists(destAssetPath)) return;
            var dir = Path.GetDirectoryName(destAssetPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            foreach (var src in new[] { "SortUI", "KnockdownUI", "GameMenuUI", "OilStationUI" })
            {
                string srcPath = "Assets/" + src + "/Fonts/" + fileName;
                if (File.Exists(srcPath))
                {
                    AssetDatabase.CopyAsset(srcPath, destAssetPath);
                    return;
                }
            }
            Debug.LogWarning("[KothUI] font not found to copy: " + fileName + " (Thai may not render).");
        }
    }
}
#endif
