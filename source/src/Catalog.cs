using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KF2Tweaker
{
    public static class Cat
    {
        public const string Overview = "Overview";
        public const string Display = "Display";
        public const string Graphics = "Graphics";
        public const string Performance = "Performance";
        public const string Gore = "Gore & physics";
        public const string Audio = "Audio";
        public const string Input = "Mouse & keys";
        public const string Hud = "HUD & game";
        public const string Tools = "Tools";

        public static readonly string[] Pages = { Overview, Display, Graphics, Performance, Gore, Audio, Input, Hud, Tools };

        public static string Blurb(string c)
        {
            switch (c)
            {
                case Display: return "Field of view, frame rate limit and how the game fills your screen.";
                case Graphics: return "In-game graphics changes can overwrite these. Lock KFSystemSettings.ini in Tools to keep them.";
                case Performance: return "Engine behaviour behind stutter, loading and frame pacing. Try one change at a time.";
                case Gore: return "How much blood and how many bodies stay on the map, and how they behave.";
                case Audio: return "Sound channels and memory. More channels means fewer sounds cut off in big fights.";
                case Input: return "Mouse feel, console key and handy key binds.";
                case Hud: return "HUD elements, quality-of-life options and the intro videos.";
                default: return "";
            }
        }
    }

    public static class Catalog
    {
        // Short names for the builder helpers (keeps this file readable and compiles with the C# 5 compiler built into Windows).
        static Tweak New(string id, string cat, string group, string title, string desc) { return Build.New(id, cat, group, title, desc); }
        static Opt O(string value, string label) { return Build.O(value, label); }
        static Opt P(string value, string label, params string[] values) { return Build.P(value, label, values); }
        static Bind B(KF f, string section, string key) { return Build.B(f, section, key); }

        // Sections
        const string EEngine = "Engine.Engine", ELocal = "Engine.LocalPlayer", EClient = "Engine.Client",
                     EStream = "TextureStreaming", ECore = "Core.System", EAudio = "XAudio2.XAudio2Device",
                     ENet = "IpDrv.TcpNetDriver", EMovie = "FullScreenMovie";
        const string GEngine = "KFGame.KFGameEngine", GGore = "KFGame.KFGoreManager", GHud = "KFGame.KFHUDBase",
                     GEHud = "Engine.HUD", GPC = "KFGame.KFPlayerController", GEPC = "Engine.PlayerController",
                     GFlash = "KFGame.KFFlashlightAttachment", GPawn = "KFGame.KFPawn";
        const string IPI = "Engine.PlayerInput", IKF = "KFGame.KFPlayerInput", ICon = "Engine.Console";
        const string S = "SystemSettings";

        static Bind Sys(string key) { return B(KF.System, S, key); }

        static string Pct(double d) { return d.ToString("0") + "%"; }
        static string X(double d) { return d.ToString("0.##", IniValue.Inv) + "\u00D7"; }
        static string Fixed2(double d) { return d.ToString("0.00", IniValue.Inv); }

        public static List<Tweak> All()
        {
            var L = new List<Tweak>();

            // =================================================================== DISPLAY
            L.Add(New("fov", Cat.Display, "View", "Field of view",
                    "Same setting as the in-game slider, with finer steps. 1.00\u00D7 is 90\u00B0 horizontal and 1.25\u00D7 is 112.5\u00B0.")
                .Slider(1.0, 1.25, 0.01, 2, "1.00", d => d.ToString("0.00", IniValue.Inv) + "\u00D7  " + (90 * d).ToString("0.#", IniValue.Inv) + "\u00B0",
                    B(KF.Game, GEngine, "FOVOptionsPercentageValue")).Tag("fov field of view zoom"));

            L.Add(New("horplus", Cat.Display, "View", "Wider view on widescreen (Hor+)",
                    "Keeps the vertical view fixed and widens the sides, which is how most modern shooters handle 16:9 and ultrawide screens. Gives a noticeably wider picture than the FOV slider alone.")
                .Toggle(false, B(KF.Engine, ELocal, "AspectRatioAxisConstraint").Vals("AspectRatio_MaintainYFOV", "AspectRatio_MaintainXFOV"))
                .Info("If the game resets it, lock KFEngine.ini on the Tools page.").Tag("fov ultrawide 21:9 aspect"));

            var capOpts = new List<Opt> { O("var", "Limiter off (multiplayer caps at 150)") };
            foreach (var n in new[] { 30, 60, 62, 75, 90, 100, 120, 144, 165, 180, 200, 240, 280, 360, 500 })
                capOpts.Add(O(n.ToString(), n == 62 ? "62 FPS (game default)" : n + " FPS"));
            capOpts.Add(O("999", "Uncapped (999)"));
            var capT = New("framecap", Cat.Display, "Frame rate", "Frame rate limit",
                    "Uses the engine's own frame limiter, which also works in multiplayer, where the game otherwise stops at 150 FPS.")
                .Custom(Kind.Choice,
                    s =>
                    {
                        string sm = s.Get(KF.Game, GEngine, "bSmoothFrameRate") ?? s.Get(KF.Engine, EEngine, "bSmoothFrameRate");
                        if (IniValue.TryBool(sm) == false) return "var";
                        string mx = s.Get(KF.Game, GEngine, "MaxSmoothedFrameRate") ?? s.Get(KF.Engine, EEngine, "MaxSmoothedFrameRate");
                        double d;
                        return IniValue.TryNumber(mx, out d) ? ((int)Math.Round(d)).ToString() : "62";
                    },
                    (s, v) =>
                    {
                        bool smooth = v != "var";
                        s.Set(KF.Engine, EEngine, "bSmoothFrameRate", smooth ? "True" : "False");
                        s.Set(KF.Game, GEngine, "bSmoothFrameRate", smooth ? "True" : "False");
                        if (!smooth) return;
                        s.Set(KF.Engine, EEngine, "MaxSmoothedFrameRate", v);
                        s.Set(KF.Game, GEngine, "MaxSmoothedFrameRate", v);
                    })
                .Shows(B(KF.Engine, EEngine, "bSmoothFrameRate"), B(KF.Engine, EEngine, "MaxSmoothedFrameRate"),
                       B(KF.Game, GEngine, "bSmoothFrameRate"), B(KF.Game, GEngine, "MaxSmoothedFrameRate"))
                .Def("62").Tag("fps cap limit uncap 144 240 smooth framerate variable");
            capT.Options.AddRange(capOpts);
            capT.CustomLabel = v => v + " FPS";
            capT.Info("For G-Sync/FreeSync, a cap a few frames under your refresh rate avoids tearing.");
            L.Add(capT);

            L.Add(New("minsmooth", Cat.Display, "Frame rate", "Frame smoothing floor",
                    "Lowest frame rate the limiter plans for. Leave it at 22 unless a guide you trust says otherwise.")
                .Number(1, 500, 0, "22", d => d.ToString("0") + " FPS",
                    B(KF.Game, GEngine, "MinSmoothedFrameRate"), B(KF.Engine, EEngine, "MinSmoothedFrameRate")).Tag("MinSmoothedFrameRate"));

            L.Add(New("vsync", Cat.Display, "Screen", "Vertical sync",
                    "Stops tearing by syncing to your monitor, at the cost of some input delay. Also in the in-game video menu.")
                .Toggle(null, Sys("UseVsync")).Tag("vsync tearing"));

            L.Add(New("windowmode", Cat.Display, "Screen", "Display mode",
                    "Borderless makes alt-tabbing instant. Handy if the game gets stuck on launch in a mode your screen doesn't like.")
                .Preset(null, new[] { Sys("Fullscreen"), Sys("Borderless") },
                    P("full", "Fullscreen", "True", "False"),
                    P("borderless", "Borderless window", "False", "True"),
                    P("window", "Windowed", "False", "False")).Tag("fullscreen borderless windowed"));

            L.Add(ResolutionTweak());

            L.Add(New("renderscale", Cat.Display, "Screen", "Render scale",
                    "Draws the 3D world at a lower resolution and scales it up, while the HUD stays sharp. A big FPS win on weak GPUs. 100% is native.")
                .Slider(50, 100, 5, 0, "100", Pct, Sys("ScreenPercentage"))
                .Tag("resolution scale upscale screen percentage"));
            var rs = L.Last();
            var rsWrite = rs.Write;
            rs.Write = (s, v) => { rsWrite(s, v); s.Set(KF.System, S, "UpscaleScreenPercentage", "True"); };

            L.Add(New("aniso", Cat.Display, "Screen", "Texture filtering",
                    "Anisotropic filtering keeps floors and walls sharp at an angle. The in-game menu only offers 4\u00D7 and 16\u00D7; 16\u00D7 costs almost nothing on modern GPUs.")
                .Pick(null, new[] { Sys("MaxAnisotropy") },
                    O("1", "Off"), O("2", "2\u00D7"), O("4", "4\u00D7"), O("8", "8\u00D7"), O("16", "16\u00D7"))
                .Info("The game will label this \"INI override\" in its menu. That's expected.").Tag("anisotropic af"));

            // =================================================================== GRAPHICS
            L.Add(New("motionblur", Cat.Graphics, "Post-processing", "Motion blur",
                    "Blurs the screen when you turn quickly. Most players switch it off for a clearer image.")
                .Toggle(null, Sys("MotionBlur"), Sys("MotionBlurPause"), Sys("MotionBlurSkinning").Vals("1", "0")));
            L.Add(New("motionblurscale", Cat.Graphics, "Post-processing", "Motion blur strength",
                    "How strong the blur is when it's on. The game uses 0.8; around 0.2 keeps a hint of it.")
                .Slider(0, 1.5, 0.05, 2, "0.80", Fixed2, Sys("MotionBlurStaticScale")));
            L.Add(New("dof", Cat.Graphics, "Post-processing", "Depth of field",
                    "Blurs the background while aiming down sights and in menus.")
                .Toggle(null, Sys("DepthOfField")).Tag("dof blur"));
            L.Add(New("bloom", Cat.Graphics, "Post-processing", "Bloom",
                    "Glow around bright lights. Turning it off makes explosions and lamps less washed out.")
                .Toggle(null, Sys("Bloom")));
            L.Add(New("lensflares", Cat.Graphics, "Post-processing", "Lens flares",
                    "Flares from bright light sources.")
                .Toggle(null, Sys("LensFlares")));
            L.Add(New("lightshafts", Cat.Graphics, "Post-processing", "Light shafts",
                    "God rays through windows and fog.")
                .Toggle(null, Sys("bAllowLightShafts")).Tag("god rays"));
            L.Add(New("distortion", Cat.Graphics, "Post-processing", "Heat haze and refraction",
                    "Screen distortion from fire, explosions and glass. Hidden in the in-game menu.")
                .Toggle(true, Sys("Distortion"), Sys("FilteredDistortion")).Tag("distortion stalker cloak")
                .Warn("Keep this on: it's what makes cloaked Stalkers visible as a shimmer, even on low settings."));
            L.Add(New("radialblur", Cat.Graphics, "Post-processing", "Radial blur",
                    "Zoom-style blur used by some explosions and effects. Hidden in the in-game menu.")
                .Toggle(true, Sys("AllowRadialBlur")));
            L.Add(New("grain", Cat.Graphics, "Post-processing", "Film grain",
                    "Strength of the grain overlay. 0 removes it for a cleaner image.")
                .Slider(0, 1, 0.05, 2, "1.00", Fixed2, Sys("ImageGrainScaler")).Tag("noise"));

            L.Add(New("ao", Cat.Graphics, "Lighting and shadows", "Ambient occlusion",
                    "Soft contact shadows in corners. HBAO+ looks best and costs the most.")
                .Preset(null, new[] { Sys("AmbientOcclusion"), Sys("HBAO") },
                    P("off", "Off", "False", null),
                    P("ssao", "SSAO", "True", "False"),
                    P("hbao", "HBAO+", "True", "True")).Tag("ssao hbao"));
            L.Add(New("ssr", Cat.Graphics, "Lighting and shadows", "Realtime reflections",
                    "Screen-space reflections on wet floors and glossy surfaces. Expensive.")
                .Toggle(null, Sys("AllowScreenSpaceReflections")).Tag("ssr"));
            L.Add(New("imgrefl", Cat.Graphics, "Lighting and shadows", "Image reflections",
                    "The cheaper, baked reflections most surfaces use.")
                .Toggle(true, Sys("AllowImageReflections"), Sys("AllowImageReflectionShadowing")));
            L.Add(New("dynshadows", Cat.Graphics, "Lighting and shadows", "Dynamic shadows",
                    "Shadows from characters, zeds and moving lights.")
                .Toggle(null, Sys("DynamicShadows"))
                .Warn("Off is a large FPS gain but characters can look flat and dark."));
            L.Add(New("shadowdetail", Cat.Graphics, "Lighting and shadows", "Shadow resolution boost",
                    "Sharper shadows with smoother edges than the in-game Ultra preset. Pick the in-game shadow setting again to undo.")
                .Preset(null, new[] { Sys("MaxShadowResolution"), Sys("MaxWholeSceneDominantShadowResolution"), Sys("ShadowFilterQualityBias") },
                    P("high", "High+ (2048)", "2048", "2048", "1"),
                    P("ultra", "Ultra+ (4096)", "4096", "4096", "1")).Tag("shadow quality"));
            L.Last().CustomLabel = v => "Set by in-game option";
            L.Last().OriginalNote = "Based on \"Ultra high quality shadows\" from the old KF2 Tweaker.";
            L.Add(New("shadowdist", Cat.Graphics, "Lighting and shadows", "Shadow draw distance",
                    "How far away dynamic shadows are drawn. Higher values fix shadows popping in ahead of you.")
                .Pick("1", new[] { Sys("GlobalShadowDistanceScale") },
                    O("1", "Default"), O("1.5", "1.5\u00D7"), O("2", "2\u00D7"), O("3", "3\u00D7"), O("4", "4\u00D7")));
            L.Last().OriginalNote = "Replaces \"Long distance detailed shadows\" from the old KF2 Tweaker.";
            L.Add(New("selfshadow", Cat.Graphics, "Lighting and shadows", "Weapon self-shadowing",
                    "Lets your weapon cast shadows onto itself.")
                .Toggle(false, Sys("bEnableForegroundSelfShadowing"))
                .Warn("Tripwire blocked this in 2016, so the game will probably ignore it."));
            L.Add(New("conservshadow", Cat.Graphics, "Lighting and shadows", "Conservative shadow bounds",
                    "Culls shadows less aggressively. Can fix shadows flickering or vanishing, for a small cost.")
                .Toggle(false, Sys("bUseConservativeShadowBounds")));
            L.Add(New("lightfunc", Cat.Graphics, "Lighting and shadows", "Light functions",
                    "Flickering and patterned lights. Off gives a small FPS gain on some maps.")
                .Toggle(true, Sys("AllowLightFunctions")));

            L.Add(New("drawdist", Cat.Graphics, "World detail", "Draw distance",
                    "How far away grass, bushes and small objects render. Higher values stop them popping in, most visibly on Zed Landing and Black Forest.")
                .Pick("1", new[] { Sys("MaxDrawDistanceScale") },
                    O("0.25", "Ultra low (0.25\u00D7)"), O("0.5", "Low (0.5\u00D7)"), O("0.75", "Medium (0.75\u00D7)"),
                    O("1", "Normal (default)"), O("2", "High (2\u00D7)"), O("4", "Ultra (4\u00D7)"),
                    O("6", "Extreme (6\u00D7)"), O("8", "Insane (8\u00D7)"))
                .Tag("render distance view distance pop-in lod"));
            L.Last().OriginalNote = "Same presets as the old KF2 Tweaker's Rendering distance.";
            L.Add(New("foliage", Cat.Graphics, "World detail", "Tree leaves and fronds",
                    "SpeedTree foliage detail. Off helps on outdoor maps.")
                .Toggle(true, Sys("SpeedTreeLeaves"), Sys("SpeedTreeFronds")).Tag("trees speedtree"));
            L.Add(New("decals", Cat.Graphics, "World detail", "Decals",
                    "Bullet holes, scorch marks and other surface marks.")
                .Toggle(true, Sys("StaticDecals"), Sys("DynamicDecals"),
                    B(KF.Engine, EEngine, "bStaticDecalsEnabled"), B(KF.Engine, EEngine, "bDynamicDecalsEnabled")));
            L.Add(New("decaldist", Cat.Graphics, "World detail", "Decal distance",
                    "How far away decals stay visible. Lower is faster.")
                .Slider(0.1, 3, 0.1, 1, "1.0", X, Sys("DecalCullDistanceScale")));
            L.Add(New("glass", Cat.Graphics, "World detail", "Breakable glass and debris",
                    "Shatterable windows and fractured objects.")
                .Toggle(true, Sys("bAllowFracturedDamage")));
            L.Add(New("fog", Cat.Graphics, "World detail", "Fog",
                    "Distance fog and fog volumes.")
                .Toggle(true, Sys("DistanceFog"), Sys("FogVolumes"))
                .Warn("Some maps look flat or too bright without it."));
            L.Add(New("meshlod", Cat.Graphics, "World detail", "Character model detail",
                    "Lower values use simpler zed and player models sooner.")
                .Pick("0", new[] { Sys("SkeletalMeshLODBias") }, O("0", "Full"), O("1", "Reduced"), O("2", "Low")));
            L.Add(New("particlelod", Cat.Graphics, "World detail", "Particle detail",
                    "Fewer, simpler particles for fire, smoke and explosions.")
                .Pick("0", new[] { Sys("ParticleLODBias") }, O("0", "Full"), O("1", "Reduced"), O("2", "Low")));
            L.Add(New("bulletholes", Cat.Graphics, "World detail", "Bullet holes",
                    "How many bullet hole and impact marks stay on walls at once.")
                .Pick("20", new[] { B(KF.Game, "KFGame.KFImpactEffectManager", "MaxImpactEffectDecals") },
                    O("0", "Off"), O("10", "10"), O("20", "20 (default)"), O("40", "40"), O("80", "80")).Tag("impact decals"));
            L.Add(ParticleTweak());

            // =================================================================== PERFORMANCE
            L.Add(New("threadlag", Cat.Performance, "Frame pacing", "Render one frame ahead",
                    "The CPU prepares the next frame while the GPU draws. Off can make aiming feel more direct but usually costs FPS.")
                .Toggle(true, Sys("OneFrameThreadLag")).Tag("input lag multi-threading").Tag("stutter"));
            L.Last().OriginalNote = "Called \"Multi-threading\" in the old KF2 Tweaker.";
            L.Add(New("framesleep", Cat.Performance, "Frame pacing", "Frame moderation",
                    "Lets the engine pause briefly between frames to even out pacing. Off may feel more responsive; on is smoother on weaker CPUs.")
                .Toggle(true, Sys("AllowPerFrameSleep"), Sys("AllowPerFrameYield")).Tag("stutter pacing smooth"));
            L.Add(New("mindesired", Cat.Performance, "Frame pacing", "Minimum desired frame rate",
                    "Below this frame rate the engine starts cutting dynamic effects to recover.")
                .Number(0, 240, 0, "35", d => d.ToString("0") + " FPS", B(KF.Engine, EClient, "MinDesiredFrameRate")));
            L.Add(New("instanced", Cat.Performance, "Frame pacing", "Instanced rendering",
                    "Draws many identical objects in one go, saving CPU draw calls.")
                .Toggle(false, Sys("InstancedRendering")));
            L.Add(New("compute", Cat.Performance, "Frame pacing", "Compute bloom and SSAO",
                    "Runs bloom and ambient occlusion as GPU compute work. Faster on most modern cards.")
                .Toggle(false, Sys("UseComputeBloom"), Sys("UseComputeSSAO")));
            L.Add(New("tickrate", Cat.Performance, "Frame pacing", "Solo and host tick rate",
                    "How often the game simulates the world when you play solo or host. 60 makes hit registration and movement feel tighter offline.")
                .Preset("30", new[] { B(KF.Engine, ENet, "NetServerMaxTickRate"), B(KF.Engine, ENet, "LanServerMaxTickRate") },
                    P("30", "30 (default)", "30", "35"), P("60", "60", "60", "60"), P("90", "90", "90", "90"))
                .Info("Doesn't change anything on other people's servers."));

            L.Add(New("streaming", Cat.Performance, "Texture streaming", "Texture streaming",
                    "Off loads every texture into video memory when the map loads, which stops texture pop-in and streaming hitches. \"Everything\" also stops lightmap streaming.")
                .Preset("on", new[] { B(KF.Engine, EEngine, "bUseTextureStreaming"), B(KF.Engine, EStream, "AllowStreamingLightmaps"), B(KF.Engine, EStream, "UsePriorityStreaming") },
                    P("on", "On (default)", "True", "True", "True"),
                    P("off", "Off", "False", "True", "True"),
                    P("full", "Off (everything)", "False", "False", "False"))
                .Warn("Needs a lot of VRAM. 6 GB or more is recommended for Off.").Tag("stutter pop-in vram"));
            L.Add(New("poolsize", Cat.Performance, "Texture streaming", "Streaming memory pool",
                    "Video memory reserved for streamed textures. A bigger pool means less swapping. Stay well under your card's VRAM.")
                .Pick("0", new[] { B(KF.Engine, EStream, "PoolSize") },
                    O("0", "Automatic (default)"), O("512", "512 MB"), O("1024", "1 GB"), O("2048", "2 GB"),
                    O("3072", "3 GB"), O("4096", "4 GB"), O("6144", "6 GB"), O("8192", "8 GB"))
                .Tag("vram texture memory").Tag("stutter hitch"));
            L.Last().CustomLabel = v => v.Replace("custom:", "") + " MB";
            L.Add(New("bgstream", Cat.Performance, "Texture streaming", "Background level loading",
                    "Keeps loading parts of the map after you spawn. Off loads everything up front: longer loading, fewer hitches. Good for SSDs.")
                .Toggle(true, B(KF.Engine, EEngine, "bUseBackgroundLevelStreaming"), B(KF.Game, GEngine, "bUseBackgroundLevelStreaming")).Tag("stutter hitch loading ssd"));
            L.Add(New("streamin", Cat.Performance, "Texture streaming", "Only stream textures in",
                    "Never drops textures back out of memory once loaded. Fewer reloads if you have spare VRAM.")
                .Toggle(false, Sys("OnlyStreamInTextures")).Tag("stutter hitch vram"));
            L.Add(New("dynstream", Cat.Performance, "Texture streaming", "Dynamic streaming",
                    "Streams textures for moving objects based on where they are. Off loads them more fully.")
                .Toggle(false, B(KF.Engine, EStream, "UseDynamicStreaming")));
            L.Add(New("defrag", Cat.Performance, "Texture streaming", "Background memory defrag",
                    "Defragments and resizes texture memory in the background instead of mid-frame.")
                .Toggle(false, B(KF.Engine, EStream, "bEnableAsyncDefrag"), B(KF.Engine, EStream, "bEnableAsyncReallocation")).Tag("stutter hitch"));
            L.Add(New("texcache", Cat.Performance, "Texture streaming", "Texture file cache",
                    "Reads streamed textures through a cache file for faster access.")
                .Toggle(true, B(KF.Engine, EStream, "UseTextureFileCache")));
            L.Add(New("switchstream", Cat.Performance, "Texture streaming", "Adaptive streaming system",
                    "Lets the engine switch streaming strategies on the fly.")
                .Toggle(false, B(KF.Engine, EStream, "bAllowSwitchingStreamingSystem")));

            L.Add(New("gclimit", Cat.Performance, "Stability", "Modded server crash fix",
                    "The well-known fix for crashes on Controlled Difficulty, Zedternal and other modded servers, and for BugSplat crashes when joining. Helps stability everywhere.")
                .Preset(null, new[] { B(KF.Engine, ECore, "MaxObjectsNotConsideredByGC"), B(KF.Engine, ECore, "SizeOfPermanentObjectPool") },
                    P("33476", "Applied (33476)", "33476", "0"),
                    P("20480", "Original tweaker value (20480)", "20480", null))
                .Tag("crash bugsplat compatibility patch controlled difficulty cd zedternal gc"));
            L.Last().CustomLabel = v => "Not applied";
            L.Last().OriginalNote = "Replaces the old KF2 Tweaker's Compatibility patches.";
            L.Add(New("physx", Cat.Performance, "Stability", "Larger PhysX memory",
                    "Gives PhysX more working memory (256 MB heap, 64 MB mesh cache) for ragdolls and debris.")
                .Toggle(false, B(KF.Engine, EEngine, "PhysXGpuHeapSize").Vals("256", "32"), B(KF.Engine, EEngine, "PhysXMeshCacheSize").Vals("64", "8"))
                .Warn("Much larger values than these have been linked to dead zeds falling through floors."));
            L.Add(New("tess", Cat.Performance, "Stability", "Terrain tessellation checks",
                    "Raises how many terrain patches are checked per frame from 6 to 30. Experimental.")
                .Toggle(false, B(KF.Engine, EEngine, "TerrainTessellationCheckCount").Vals("30", "6")));
            L.Add(New("combinemaps", Cat.Performance, "Stability", "Combine similar light maps",
                    "Merges near-identical light and shadow maps. Experimental.")
                .Toggle(false, B(KF.Engine, EEngine, "bCombineSimilarMappings")));

            L.Add(New("dynlights", Cat.Performance, "Low-end rescue", "Dynamic lights",
                    "Muzzle flashes, fire, flashlights and most moving lights.")
                .Toggle(true, Sys("DynamicLights"))
                .Danger("Off is only for very weak PCs: some maps turn pitch black and your flashlight stops working."));
            L.Add(New("hqmaterials", Cat.Performance, "Low-end rescue", "High quality materials",
                    "Detailed surface shaders. Off looks a little flatter but is lighter on the GPU.")
                .Toggle(true, Sys("bAllowHighQualityMaterials")));
            L.Add(New("septrans", Cat.Performance, "Low-end rescue", "Separate translucency",
                    "Renders glass, smoke and fire in their own pass for better sorting.")
                .Toggle(true, Sys("bAllowSeparateTranslucency")));
            L.Add(New("envshadows", Cat.Performance, "Low-end rescue", "Light environment shadows",
                    "Cheap shadows that characters cast from baked lighting.")
                .Toggle(true, Sys("LightEnvironmentShadows")));
            L.Add(New("complights", Cat.Performance, "Low-end rescue", "Composite dynamic lights",
                    "Merges small dynamic lights into character lighting. Off rarely changes the look.")
                .Toggle(true, Sys("CompositeDynamicLights")));

            // =================================================================== GORE
            // Gore levels and values are the original KF2 Tweaker's Gore Control, value for value.
            var goreKeys = new[] { "GoreFXLifetimeMultiplier", "BodyWoundDecalLifetime", "BloodSplatterLifetime", "BloodPoolLifetime",
                "GibletLifetime", "MaxBodyWoundDecals", "MaxBloodSplatterDecals", "MaxBloodPoolDecals", "BloodSplatSize", "BloodPoolSize",
                "MaxDeadBodies", "MaxBloodEffects", "MaxGoreEffects", "AllowBloodSplatterDecals", "bAllowBloodSplatterDecals",
                "PersistentSplatTraceLength", "MaxPersistentSplatsPerFrame" };
            var gore = New("gore", Cat.Gore, "Gore", "Amount of persistent gore",
                    "How much blood, gibs and corpses stay on the map, and for how long.")
                .Preset("high", goreKeys.Select(k => B(KF.Game, GGore, k)).ToArray(),
                    P("none", "None", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", "False", "False", "0", "0"),
                    P("minimal", "Minimal", "0.1", "1", "1", "1", "1", "3", "1", "1", "1", "1", "4", "1", "1", "True", "True", "0.5", "1"),
                    P("normal", "Normal (less persistent blood)", "1.2", "30", "10", "20", "10", "5", "20", "20", "100", "125", "15", "40", "15", "True", "True", "0.5", "100"),
                    P("high", "High (default)", "1.2", "30", "10", "20", "10", "5", "20", "20", "100", "125", "15", "40", "15", "True", "True", "1000", "100"),
                    P("veryhigh", "Very high", "2.4", "60", "10", "20", "20", "10", "20", "20", "100", "125", "30", "40", "30", "True", "True", "1000", "100"),
                    P("insane", "Insane", "120", "3000", "10", "20", "1000", "500", "20", "20", "100", "125", "1500", "40", "1500", "True", "True", "1000", "100"))
                .Warn("Very high and Insane keep far more bodies around and can cost a lot of FPS late in long waves.")
                .Tag("blood gibs corpses bodies gore control persistent");
            gore.Radio = true;
            gore.CustomLabel = v => "Custom";
            gore.OptionNotes = new Dictionary<string, string>
            {
                { "none", "No persistent gore." },
                { "minimal", "Just a few giblets and some blood. You may still stumble across a corpse here and there." },
                { "normal", "Exactly the same as High, just with less persistent blood decals." },
                { "high", "The game's default gore settings." },
                { "veryhigh", "Double the corpses, gibs and guts. Default amount of blood. Expect some performance hit." },
                { "insane", "Meat mountain! 100\u00D7 corpses, gibs and guts. Default amount of blood. Expect a heavy performance hit." },
            };
            gore.OriginalNote = "Same levels and values as the old KF2 Tweaker's Gore Control.";
            var goreWrite = gore.Write;
            var levelNames = new Dictionary<string, string> { { "none", "OFF" }, { "minimal", "LOW" }, { "normal", "NORMAL" }, { "high", "HIGH" }, { "veryhigh", "VERYHIGH" }, { "insane", "INSANE" } };
            gore.Write = (st, v) =>
            {
                goreWrite(st, v);
                string lvl;
                if (levelNames.TryGetValue(v, out lvl)) st.Set(KF.Game, GGore, "KF2TweakerLevel", lvl); // lets the old tweaker recognise the level
            };
            L.Add(gore);
            L.Add(New("deadbodies", Cat.Gore, "Gore", "Dead bodies kept",
                    "How many corpses stay on the map before the oldest disappear.")
                .Number(4, 2000, 0, "15", d => d.ToString("0"), B(KF.Game, GGore, "MaxDeadBodies")).Tag("corpses")
                .Info("4 is the real minimum; lower numbers don't reduce it further."));
            L.Add(New("splats", Cat.Gore, "Gore", "Permanent blood on the map",
                    "Blood painted into the level itself, which stays for the whole match.")
                .Toggle(true, Sys("AllowPersistentSplats")));
            L.Add(New("secblood", Cat.Gore, "Gore", "Secondary blood effects",
                    "Extra drips and sprays on hits.")
                .Toggle(true, Sys("AllowSecondaryBloodEffects")));
            L.Add(New("pile", Cat.Gore, "Physics", "Bodies pile up",
                    "Corpses collide with each other and stack instead of clipping through. Looks great with a high body count.")
                .Toggle(false, Sys("FlexRigidBodiesCollisionAtHighLevel")).Tag("ragdoll collision"));
            L.Add(New("deadgore", Cat.Gore, "Physics", "Shoot corpses",
                    "Dead bodies keep reacting to hits and can still be dismembered.")
                .Toggle(true, B(KF.Game, GPawn, "bAllowRagdollAndGoreOnDeadBodies")));
            L.Add(New("gravity", Cat.Gore, "Physics", "Ragdoll and drop gravity",
                    "Makes bodies, dropped weapons and dosh fall faster, so kills stop flinging corpses across the map. 1\u00D7 is normal.")
                .Slider(1, 5, 0.25, 2, "1.00", X, B(KF.Game, "Engine.WorldInfo", "RBPhysicsGravityScaling"))
                .Warn("Above about 3\u00D7, dropped weapons can fall through the floor in solo games.").Tag("ragdoll physics"));

            // =================================================================== AUDIO
            L.Add(New("channels", Cat.Audio, "Sound", "3D sound channels",
                    "How many sounds can play at once. More channels stop gunfire and zed sounds cutting out in big waves.")
                .Pick("32", new[] { B(KF.Engine, EAudio, "MaxChannels"), B(KF.Game, GEPC, "MaxConcurrentHearSounds") },
                    O("32", "32 (default)"), O("64", "64"), O("96", "96"), O("128", "128"), O("192", "192 (experimental)"), O("256", "256 (experimental)"))
                .Info("If the game crashes after raising this, lower it again."));
            L.Last().OriginalNote = "From the old KF2 Tweaker's 3D Audio.";
            L.Add(New("audiopool", Cat.Audio, "Sound", "Audio memory",
                    "RAM set aside for sound effects. More helps with stuttering or skipping audio.")
                .Pick("0", new[] { B(KF.Engine, EAudio, "CommonAudioPoolSize") },
                    O("0", "Default"), O("32", "32 MB"), O("64", "64 MB"), O("128", "128 MB"), O("256", "256 MB")).Tag("stutter crackle skipping"));
            L.Add(New("occlusion", Cat.Audio, "Sound", "Dynamic audio occlusion",
                    "Muffles sounds behind walls. Off saves a little CPU.")
                .Toggle(true, Sys("EnableDynamicAudioOcclusion")));
            L.Add(New("mutefocus", Cat.Audio, "Sound", "Mute when in the background",
                    "Silences the game while you're alt-tabbed.")
                .Toggle(true, B(KF.Game, GEngine, "bMuteOnLossOfFocus")).Tag("alt-tab"));
            L.Add(New("chatter", Cat.Audio, "Sound", "Minimal battle chatter",
                    "Your character talks less during fights.")
                .Toggle(false, B(KF.Game, GEngine, "bMinimalChatter")).Tag("voice"));
            const string GInfo = "KFGame.KFGameInfo";
            const string HostNote = "Host setting: applies to solo games and servers you host, not other people's servers.";
            L.Add(New("voip", Cat.Audio, "Voice chat", "Voice chat",
                    "Lets players talk over voice chat in games you host.")
                .Toggle(true, B(KF.Game, GInfo, "bDisableVOIP").Vals("False", "True")).Info(HostNote).Tag("voip mic microphone"));
            L.Add(New("deadvoip", Cat.Audio, "Voice chat", "Dead players can talk to the living",
                    "Players waiting to respawn can still be heard by the team that's alive.")
                .Toggle(true, B(KF.Game, GInfo, "bEnableDeadToVOIP")).Info(HostNote).Tag("voip spectator"));
            L.Add(New("publicvoip", Cat.Audio, "Voice chat", "Public voice channel",
                    "The channel every player on the server can hear, as well as team-only voice.")
                .Toggle(true, B(KF.Game, GInfo, "bDisablePublicVOIPChannel").Vals("False", "True")).Info(HostNote).Tag("voip versus"));

            // =================================================================== INPUT
            L.Add(New("smoothing", Cat.Input, "Mouse", "Mouse smoothing",
                    "Averages mouse movement over several frames. On by default; off gives direct, one-to-one aim.")
                .Toggle(true, B(KF.Input, IPI, "bEnableMouseSmoothing"), B(KF.Input, IKF, "bEnableMouseSmoothing").IfPresent())
                .Tag("acceleration raw input lag"));
            L.Add(New("viewaccel", Cat.Input, "Mouse", "Controller view acceleration",
                    "Speeds up turning the longer you hold a stick. Doesn't affect the mouse.")
                .Toggle(true, B(KF.Input, IKF, "bViewAccelerationEnabled")).Tag("gamepad acceleration"));
            L.Last().OriginalNote = "The old KF2 Tweaker labelled this as mouse acceleration.";
            L.Add(New("sens", Cat.Input, "Mouse", "Mouse sensitivity",
                    "Exact value, for matching sensitivity between games. Same as the in-game slider.")
                .Number(0.01, 200, 3, null, d => d.ToString("0.###", IniValue.Inv),
                    B(KF.Input, IPI, "MouseSensitivity"), B(KF.Input, IKF, "MouseSensitivity").IfPresent()));
            L.Add(New("adssens", Cat.Input, "Mouse", "Aim-down-sights sensitivity",
                    "Multiplier applied while aiming down sights or scoped.")
                .Slider(0.1, 1.5, 0.05, 2, null, X, B(KF.Input, IKF, "ZoomedSensitivityScale")).Tag("zoom scope ads"));
            L.Add(VerticalRatioTweak(IKF));
            L.Add(New("invert", Cat.Input, "Mouse", "Invert mouse",
                    "Pushing the mouse forward looks down.")
                .Toggle(null, B(KF.Input, IPI, "bInvertMouse")));
            L.Add(TurnRepairTweak(IPI, IKF));

            L.Add(New("consolekey", Cat.Input, "Keys", "Console key",
                    "Key that opens the console. Change it if ~ doesn't work on your keyboard layout (common on German, French and Nordic keyboards).")
                .Pick("Tilde", new[] { B(KF.Input, ICon, "ConsoleKey") },
                    O("Tilde", "~ (default)"), O("F10", "F10"), O("Insert", "Insert"), O("Home", "Home"), O("Backslash", "\\")));
            L.Add(DoshTweak(IKF));
            L.Add(AdsHudTweak(IKF));

            // =================================================================== HUD & GAME
            L.Add(New("crosshair", Cat.Hud, "HUD", "Crosshair",
                    "Shows a crosshair when hip-firing.")
                .Toggle(false, B(KF.Game, GEngine, "bShowCrossHair")));
            L.Add(New("friendlyscale", Cat.Hud, "HUD", "Teammate info size",
                    "Size of the names, health and armor bars over teammates.")
                .Slider(0.25, 1.5, 0.05, 2, "1.00", X, B(KF.Game, GHud, "FriendlyHudScale")).Tag("friendly hud"));
            L.Add(New("friendlyvis", Cat.Hud, "HUD", "Teammate info",
                    "Show names and health over teammates.")
                .Toggle(true, B(KF.Game, GHud, "bFriendlyHUDVisible")).Tag("friendly hud"));
            L.Add(New("classicinfo", Cat.Hud, "HUD", "Classic player info",
                    "Older, more compact layout for teammate info.")
                .Toggle(false, B(KF.Game, GHud, "ClassicPlayerInfo")));
            L.Add(New("traderpath", Cat.Hud, "HUD", "Trader path",
                    "The glowing trail that leads you to the trader.")
                .Toggle(true, B(KF.Game, GPC, "bHideTraderPaths").Vals("False", "True")));
            L.Add(New("chatlines", Cat.Hud, "HUD", "Chat lines",
                    "How many chat and console messages stay on screen.")
                .Number(1, 12, 0, "4", d => d.ToString("0"), B(KF.Game, GHud, "ConsoleMessageCount"), B(KF.Game, GEHud, "ConsoleMessageCount")));

            L.Add(New("flashlights", Cat.Hud, "Gameplay", "See every teammate's flashlight",
                    "By default the game only draws the most useful teammate flashlight. On draws all of them.")
                .Toggle(false, B(KF.Game, GFlash, "bSkipBestFlashlightCheck")));
            L.Add(New("welder", Cat.Hud, "Gameplay", "Welder in weapon list",
                    "Shows the welder when cycling weapons.")
                .Toggle(true, B(KF.Game, GEngine, "bShowWelderInInv")));
            L.Add(New("altdual", Cat.Hud, "Gameplay", "Alternate dual-wield aiming",
                    "Same as the in-game alternate aim option for dual pistols.")
                .Toggle(false, B(KF.Game, GEngine, "bUseAltAimOnDual")));
            L.Add(New("motionsick", Cat.Hud, "Gameplay", "Reduce motion sickness",
                    "Tones down camera bob and sway.")
                .Toggle(false, B(KF.Game, GEngine, "bAntiMotionSickness")));
            L.Add(IntroTweak());

            return L;
        }

        // ------------------------------------------------------------------- special tweaks

        static Tweak ResolutionTweak()
        {
            var t = New("resolution", Cat.Display, "Screen", "Resolution",
                "Resolution the game starts with. Useful when it launches at the wrong size or off-screen.");
            t.Binds.Add(Sys("ResX")); t.Binds.Add(Sys("ResY"));
            t.Kind = Kind.Choice;
            var list = new List<string> { "1280x720", "1366x768", "1600x900", "1920x1080", "2560x1080", "2560x1440", "3440x1440", "3840x1600", "3840x2160", "5120x1440" };
            try
            {
                var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                string native = sc.Width + "x" + sc.Height;
                if (!list.Contains(native)) list.Add(native);
            }
            catch { }
            list = list.OrderBy(r => int.Parse(r.Split('x')[0])).ThenBy(r => int.Parse(r.Split('x')[1])).ToList();
            foreach (var r in list) t.Options.Add(O(r, r.Replace("x", " \u00D7 ")));
            t.CustomLabel = v => v.Replace("x", " \u00D7 ");
            t.Read = s =>
            {
                double w, h;
                if (IniValue.TryNumber(s.Get(KF.System, S, "ResX"), out w) && IniValue.TryNumber(s.Get(KF.System, S, "ResY"), out h))
                    return (int)w + "x" + (int)h;
                return "1920x1080";
            };
            t.Write = (s, v) =>
            {
                var p = v.Split('x');
                if (p.Length != 2) return;
                s.Set(KF.System, S, "ResX", p[0]);
                s.Set(KF.System, S, "ResY", p[1]);
            };
            return t;
        }

        static Tweak VerticalRatioTweak(string sec)
        {
            var t = New("vratio", Cat.Input, "Mouse", "Vertical sensitivity",
                "Vertical mouse speed relative to horizontal. 100% makes both axes equal.");
            t.Kind = Kind.Slider; t.Min = 50; t.Max = 150; t.Step = 5; t.Decimals = 0; t.Format = Pct; t.Default = "100";
            t.Binds.Add(B(KF.Input, sec, "MouseLookUpScale"));
            t.Binds.Add(B(KF.Input, sec, "MouseLookRightScale"));
            t.Binds.Add(B(KF.Input, sec, "bUseDefaultLookScales"));
            t.Read = s =>
            {
                if (IniValue.TryBool(s.Get(KF.Input, sec, "bUseDefaultLookScales")) == true) return "100";
                double up, right;
                if (!IniValue.TryNumber(s.Get(KF.Input, sec, "MouseLookUpScale"), out up) ||
                    !IniValue.TryNumber(s.Get(KF.Input, sec, "MouseLookRightScale"), out right) || right == 0) return "100";
                return IniValue.Num(Math.Round(Math.Abs(up) / Math.Abs(right) * 100), 0);
            };
            t.Write = (s, v) =>
            {
                double pct, up;
                if (!IniValue.TryNumber(v, out pct)) return;
                bool negative = !IniValue.TryNumber(s.Get(KF.Input, sec, "MouseLookUpScale"), out up) || up <= 0;
                s.Set(KF.Input, sec, "bUseDefaultLookScales", "False");
                s.Set(KF.Input, sec, "MouseLookRightScale", "100.000000");
                s.Set(KF.Input, sec, "MouseLookUpScale", ((negative ? -1 : 1) * pct).ToString("F6", IniValue.Inv));
            };
            return t;
        }

        static Tweak TurnRepairTweak(string pi, string kf)
        {
            var t = New("turnscale", Cat.Input, "Mouse", "Keyboard and controller turning",
                "Turn speed for arrow keys and controller sticks. The old KF2 Tweaker's \"mouse movement scale\" set these to 0, which switches that turning off without changing the mouse.");
            t.Kind = Kind.Toggle; t.Default = "1";
            t.Binds.Add(B(KF.Input, pi, "LookRightScale")); t.Binds.Add(B(KF.Input, pi, "LookUpScale"));
            t.Binds.Add(B(KF.Input, kf, "LookRightScale")); t.Binds.Add(B(KF.Input, kf, "LookUpScale"));
            t.Read = s =>
            {
                double d;
                if (!IniValue.TryNumber(s.Get(KF.Input, pi, "LookRightScale"), out d)) return "1";
                return Math.Abs(d) < 1e-6 ? "0" : "1";
            };
            t.Write = (s, v) =>
            {
                string r = v == "1" ? "300" : "0", u = v == "1" ? "-250" : "0";
                s.Set(KF.Input, pi, "LookRightScale", r);
                s.Set(KF.Input, pi, "LookUpScale", u);
                s.SetIfPresent(KF.Input, kf, "LookRightScale", r);
                s.SetIfPresent(KF.Input, kf, "LookUpScale", u);
            };
            t.Info("Turn this back on if you used the old tweaker and play with a controller.");
            return t;
        }

        // ---- Dosh bind: Bindings=(Name="M",Command="tossmoney | tossmoney | …",…)

        static readonly Regex NameRx = new Regex("Name=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        static readonly Regex CmdRx = new Regex("Command=\"([^\"]*)\"", RegexOptions.IgnoreCase);

        static bool ParseBinding(string line, out string name, out string cmd)
        {
            name = cmd = null;
            string k, v;
            if (!IniDocument.TryParseKV(line, out k, out v) || !k.Equals("Bindings", StringComparison.OrdinalIgnoreCase)) return false;
            var n = NameRx.Match(v); var c = CmdRx.Match(v);
            if (!n.Success || !c.Success) return false;
            name = n.Groups[1].Value; cmd = c.Groups[1].Value;
            return true;
        }

        // Bindings the tool replaces are commented out with the marker (not deleted) so they can be restored exactly.
        // UE3 uses the LAST binding for a key, so new binds are appended at the end of the section.

        static bool IsPlain(string line)
        {
            string l = line.Replace(" ", "");
            return l.IndexOf("Control=True", StringComparison.OrdinalIgnoreCase) < 0 &&
                   l.IndexOf("Shift=True", StringComparison.OrdinalIgnoreCase) < 0 &&
                   l.IndexOf("Alt=True", StringComparison.OrdinalIgnoreCase) < 0;
        }

        static bool PlainBindFor(string line, string key, out string cmd)
        {
            string name;
            cmd = null;
            return ParseBinding(line, out name, out cmd) && name.Equals(key, StringComparison.OrdinalIgnoreCase) && IsPlain(line);
        }

        static string EffectiveCommand(IniDocument doc, string sec, string key)
        {
            string found = null, cmd;
            foreach (var l in doc.SectionLines(sec)) if (PlainBindFor(l, key, out cmd)) found = cmd;
            return found;
        }

        static void DisableBinds(IniDocument doc, string sec, string key)
        {
            doc.MapLines(sec, l => { string c; return PlainBindFor(l, key, out c) ? Mark + l.TrimStart() : null; });
        }

        static int RestoreBinds(IniDocument doc, string sec, string key)
        {
            return doc.MapLines(sec, l =>
            {
                if (!l.StartsWith(Mark)) return null;
                string c, rest = l.Substring(Mark.Length);
                return PlainBindFor(rest, key, out c) ? rest : null;
            });
        }

        static string BindLine(string key, string command)
        {
            return "Bindings=(Name=\"" + key + "\",Command=\"" + command +
                   "\",Control=False,Shift=False,Alt=False,bIgnoreCtrl=False,bIgnoreShift=False,bIgnoreAlt=False)";
        }

        static bool IsTossBind(string line, out string name, out int count)
        {
            string cmd;
            count = 0;
            if (!ParseBinding(line, out name, out cmd) || name.StartsWith("GBA_", StringComparison.OrdinalIgnoreCase) || !IsPlain(line)) return false;
            count = TossCount(cmd);
            return count > 0;
        }

        static int TossCount(string cmd)
        {
            var parts = cmd.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length < 1 || parts.Any(p => !p.Equals("tossmoney", StringComparison.OrdinalIgnoreCase))) return 0;
            return parts.Length;
        }

        static readonly string[] WheelKeys = { "MouseScrollUp", "MouseScrollDown" };
        static readonly string[] WheelDefaults = { "GBA_NextWeapon", "GBA_PrevWeapon" };

        static Tweak DoshTweak(string sec)
        {
            var t = New("dosh", Cat.Input, "Keys", "Quick dosh throw",
                "One press throws a stack of dosh (the game throws 50 at a time). On the mouse wheel, every scroll notch throws.");
            t.Kind = Kind.Pair; t.Default = "0|M";
            t.Options.AddRange(new[] { O("0", "Off"), O("1", "50 per press"), O("5", "250 per press"), O("20", "1,000 per press"), O("100", "5,000 per press") });
            t.Options2.AddRange(new[] { O("M", "M key"), O("N", "N key"), O("J", "J key"), O("K", "K key"), O("L", "L key"), O("O", "O key"), O("Wheel", "Mouse wheel") });
            t.Binds.Add(B(KF.Input, sec, "Bindings"));
            t.Info("The key's previous binding is kept and comes back when you switch this off. The mouse wheel stops switching weapons while this is on.");
            t.OriginalNote = "Enhanced dosh throwing from the old KF2 Tweaker.";
            t.Tag("money tossmoney bind scroll wheel");
            t.Read = s =>
            {
                foreach (var line in s.Doc(KF.Input).SectionLines(sec))
                {
                    string name; int n;
                    if (!IsTossBind(line, out name, out n)) continue;
                    if (WheelKeys.Any(w => w.Equals(name, StringComparison.OrdinalIgnoreCase))) return n + "|Wheel";
                    return n + "|" + name.ToUpperInvariant();
                }
                return "0|M";
            };
            t.Write = (s, v) =>
            {
                var doc = s.Doc(KF.Input);
                if (!doc.Exists) return;
                var p = v.Split('|');
                int count; int.TryParse(p[0], out count);
                string key = p.Length > 1 ? p[1] : "M";

                // 1. Remove any quick-throw binds we (or the old KF2 Tweaker) added, restoring what they replaced.
                var names = new List<string>();
                doc.RemoveLines(sec, line =>
                {
                    string name; int n;
                    if (!IsTossBind(line, out name, out n)) return false;
                    names.Add(name);
                    return true;
                });
                foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (RestoreBinds(doc, sec, name) > 0 || EffectiveCommand(doc, sec, name) != null) continue;
                    int w = Array.FindIndex(WheelKeys, k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (w >= 0) doc.InsertLine(sec, BindLine(WheelKeys[w], WheelDefaults[w]), null);
                }
                if (count <= 0) return;

                // 2. Park the key's current binding and add the new one last, so it wins.
                string command = string.Join(" | ", Enumerable.Repeat("TossMoney", count).ToArray());
                var keys = key == "Wheel" ? WheelKeys : new[] { key };
                foreach (var k in keys)
                {
                    DisableBinds(doc, sec, k);
                    doc.InsertLine(sec, BindLine(k, command), null);
                }
            };
            return t;
        }

        const string AdsCommand = "SpectateNextPlayer | GBA_IronsightsHold | ShowHUD | OnRelease ShowHUD";

        static Tweak AdsHudTweak(string sec)
        {
            var t = New("adshud", Cat.Input, "Keys", "Hide HUD while aiming",
                "Hides the HUD while you hold right-click to aim, for a clean sight picture. Right-click becomes hold-to-aim.");
            t.Kind = Kind.Toggle; t.Default = "0";
            t.Binds.Add(B(KF.Input, sec, "Bindings"));
            t.Info("If the HUD ever stays hidden, tap right-click once. Your previous right-click binding comes back when you switch this off.");
            t.Tag("ads ironsights scope clean hud bind");
            t.Read = s =>
            {
                string c = EffectiveCommand(s.Doc(KF.Input), sec, "RightMouseButton");
                return c != null && c.IndexOf("ShowHUD", StringComparison.OrdinalIgnoreCase) >= 0 ? "1" : "0";
            };
            t.Write = (s, v) =>
            {
                var doc = s.Doc(KF.Input);
                if (!doc.Exists) return;
                // Drop our bind(s) first.
                doc.RemoveLines(sec, l =>
                {
                    string c;
                    return PlainBindFor(l, "RightMouseButton", out c) && c.IndexOf("ShowHUD", StringComparison.OrdinalIgnoreCase) >= 0;
                });
                if (v == "1")
                {
                    DisableBinds(doc, sec, "RightMouseButton");
                    doc.InsertLine(sec, BindLine("RightMouseButton", AdsCommand), null);
                }
                else if (RestoreBinds(doc, sec, "RightMouseButton") == 0 && EffectiveCommand(doc, sec, "RightMouseButton") == null)
                {
                    doc.InsertLine(sec, BindLine("RightMouseButton", "SpectateNextPlayer | GBA_IronsightsHold"), null);
                }
            };
            return t;
        }

        // ---- Intro videos: comment out the logo StartupMovies lines so they can be restored exactly.

        const string Mark = ";KFTool;";
        static readonly string[] DefaultLogos = { "LogoTripwire", "LogoHardsuit", "LogoUE3", "LogoGA" };

        static bool IsLogoLine(string line, out string movie)
        {
            movie = null;
            string k, v;
            if (!IniDocument.TryParseKV(line, out k, out v) || !k.Equals("StartupMovies", StringComparison.OrdinalIgnoreCase)) return false;
            movie = v.Trim();
            return movie.StartsWith("Logo", StringComparison.OrdinalIgnoreCase);
        }

        static Tweak IntroTweak()
        {
            var t = New("intro", Cat.Hud, "Startup", "Intro and loading videos",
                "Skipping the intros goes straight to the main menu. Skipping everything also removes the loading-screen videos; the menu background still plays.");
            t.Kind = Kind.Choice; t.Default = "play";
            t.Options.AddRange(new[] { O("play", "Play all (default)"), O("skipintro", "Skip intros"), O("skipall", "Skip intros and loading videos") });
            t.Binds.Add(B(KF.Engine, EMovie, "StartupMovies"));
            t.Binds.Add(B(KF.Engine, EMovie, "bForceNoMovies"));
            t.OriginalNote = "Skip intros works like the old KF2 Tweaker's redesigned intro skip.";
            t.Tag("movies logos startup nostartupmovies bforcenomovies");
            t.Read = s =>
            {
                var doc = s.Doc(KF.Engine);
                if (IniValue.TryBool(doc.Get(EMovie, "bForceNoMovies")) == true) return "skipall";
                string m;
                return doc.SectionLines(EMovie).Any(l => IsLogoLine(l, out m)) ? "play" : "skipintro";
            };
            t.Write = (s, v) =>
            {
                var doc = s.Doc(KF.Engine);
                if (!doc.Exists) return;
                s.Set(KF.Engine, EMovie, "bForceNoMovies", v == "skipall" ? "True" : "False");
                if (v != "play")
                {
                    doc.MapLines(EMovie, l => { string m; return IsLogoLine(l, out m) ? Mark + l.TrimStart() : null; });
                    return;
                }
                int restored = doc.MapLines(EMovie, l =>
                {
                    if (!l.StartsWith(Mark)) return null;
                    string m;
                    return IsLogoLine(l.Substring(Mark.Length), out m) ? l.Substring(Mark.Length) : null;
                });
                string mm;
                if (restored == 0 && !doc.SectionLines(EMovie).Any(l => IsLogoLine(l, out mm)))
                {
                    // Lines were deleted by another tool: put the stock logos back ahead of the menu movie.
                    foreach (var logo in DefaultLogos)
                        doc.InsertLine(EMovie, "StartupMovies=" + logo, l =>
                        {
                            string k, val;
                            return IniDocument.TryParseKV(l, out k, out val) && k.Equals("StartupMovies", StringComparison.OrdinalIgnoreCase)
                                   && !val.Trim().StartsWith("Logo", StringComparison.OrdinalIgnoreCase);
                        });
                }
            };
            return t;
        }

        // ---- Particle clutter (Less Cancerous KF2 Settings guide): MaxParticleVertexMemory etc.

        static Tweak ParticleTweak()
        {
            var t = New("particles", Cat.Graphics, "World detail", "Particle clutter",
                "Caps the memory particles can use, which thins out the screen-filling clouds from freeze grenades, fire and explosions.");
            t.Kind = Kind.Choice; t.Default = "normal";
            t.Options.AddRange(new[] { O("normal", "Normal (default)"), O("reduced", "Reduced"), O("minimal", "Minimal") });
            t.CustomLabel = v => "Custom (" + v.Replace("custom:", "") + ")";
            t.Binds.Add(B(KF.Engine, EEngine, "MaxParticleVertexMemory"));
            t.Binds.Add(B(KF.Game, GEngine, "MaxParticleVertexMemory"));
            t.Binds.Add(B(KF.Engine, EEngine, "MaxFluidNumVerts"));
            t.Binds.Add(B(KF.Engine, EEngine, "FluidSimulationTimeLimit"));
            t.Danger("Minimal can make Husk fireballs hard or impossible to see.");
            t.Tag("freeze grenade firebug smoke visibility");
            t.Read = s =>
            {
                string v = s.Get(KF.Game, GEngine, "MaxParticleVertexMemory") ?? s.Get(KF.Engine, EEngine, "MaxParticleVertexMemory");
                double d;
                if (!IniValue.TryNumber(v, out d)) return "normal";
                if (d >= 100000) return "normal";
                if (Math.Abs(d - 4096) < 1) return "reduced";
                if (Math.Abs(d - 384) < 1) return "minimal";
                return "custom:" + (int)d;
            };
            t.Write = (s, v) =>
            {
                string mem = v == "minimal" ? "384" : v == "reduced" ? "4096" : "131972";
                string verts = v == "minimal" ? "1" : "1048576", time = v == "minimal" ? "0" : "30";
                s.Set(KF.Engine, EEngine, "MaxParticleVertexMemory", mem);
                s.SetIfPresent(KF.Game, GEngine, "MaxParticleVertexMemory", mem);
                if (v == "reduced") return; // leave fluid settings as they are
                s.Set(KF.Engine, EEngine, "MaxFluidNumVerts", verts);
                s.SetIfPresent(KF.Game, GEngine, "MaxFluidNumVerts", verts);
                s.Set(KF.Engine, EEngine, "FluidSimulationTimeLimit", time);
                s.SetIfPresent(KF.Game, GEngine, "FluidSimulationTimeLimit", time);
            };
            return t;
        }

        // ------------------------------------------------------------------- presets

        public sealed class QuickPreset
        {
            public string Title, Description;
            public KeyValuePair<string, string>[] Values;
        }

        static KeyValuePair<string, string> V(string id, string v) { return new KeyValuePair<string, string>(id, v); }

        public static List<QuickPreset> Presets()
        {
            return new List<QuickPreset>
            {
                new QuickPreset
                {
                    Title = "Clear picture",
                    Description = "Turns off blur, grain, lens flares and mouse smoothing. Keeps everything else as it is.",
                    Values = new[] { V("motionblur", "0"), V("dof", "0"), V("grain", "0.00"), V("radialblur", "0"),
                                     V("lensflares", "0"), V("smoothing", "0") }
                },
                new QuickPreset
                {
                    Title = "More FPS",
                    Description = "Cuts the expensive extras (post effects, reflections, glass, extra gore) while keeping shadows and lighting.",
                    Values = new[] { V("motionblur", "0"), V("dof", "0"), V("ao", "off"), V("bloom", "0"), V("lensflares", "0"),
                                     V("lightshafts", "0"), V("ssr", "0"), V("glass", "0"), V("secblood", "0"),
                                     V("decaldist", "0.5"), V("bulletholes", "10"), V("gore", "minimal"), V("drawdist", "0.75"), V("pile", "0"),
                                     V("flashlights", "0"), V("instanced", "1") }
                },
                new QuickPreset
                {
                    Title = "Low-end rescue",
                    Description = "Everything in More FPS plus lower render scale, no dynamic shadows and simpler models. For PCs below minimum spec.",
                    Values = new[] { V("motionblur", "0"), V("dof", "0"), V("ao", "off"), V("bloom", "0"), V("lensflares", "0"),
                                     V("lightshafts", "0"), V("ssr", "0"), V("imgrefl", "0"), V("glass", "0"),
                                     V("secblood", "0"), V("decaldist", "0.3"), V("bulletholes", "0"), V("gore", "none"), V("drawdist", "0.5"),
                                     V("pile", "0"), V("flashlights", "0"), V("instanced", "1"), V("renderscale", "80"),
                                     V("dynshadows", "0"), V("hqmaterials", "0"), V("septrans", "0"), V("meshlod", "1"),
                                     V("particlelod", "1"), V("aniso", "2"), V("foliage", "0") }
                },
                new QuickPreset
                {
                    Title = "High-end extras",
                    Description = "Sharper and longer shadows, longer draw distance, 16\u00D7 filtering, more gore that piles up and more sound channels.",
                    Values = new[] { V("shadowdetail", "ultra"), V("shadowdist", "2"), V("drawdist", "2"), V("aniso", "16"),
                                     V("gore", "veryhigh"), V("pile", "1"), V("flashlights", "1"), V("channels", "64"),
                                     V("audiopool", "64"), V("conservshadow", "1"), V("bulletholes", "80") }
                },
            };
        }
    }
}
