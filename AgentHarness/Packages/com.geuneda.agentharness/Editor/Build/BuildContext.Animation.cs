#if AGENTHARNESS_ANIMATION
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>How a <see cref="ClipTrack"/> moves between its keys.</summary>
    public enum ClipTangents
    {
        /// <summary>Eases through the keys without overshooting (the Animation window's default, Clamped Auto).</summary>
        Smooth,
        /// <summary>Straight lines between keys: constant speed (a spin from 0 to 360).</summary>
        Linear,
        /// <summary>Holds each key's value until the next key.</summary>
        Constant,
    }

    /// <summary>One animated property of a clip (one curve per channel). <see cref="Linear"/>/<see cref="Constant"/> change how it moves between keys.</summary>
    public sealed class ClipTrack
    {
        internal readonly string Path;
        internal readonly Type Type;
        internal readonly string[] Properties;
        internal readonly float[] Times;
        internal readonly float[][] Values;   // [channel][key]
        internal ClipTangents Tangents;

        internal ClipTrack(string path, Type type, string[] properties, float[] times, float[][] values, ClipTangents tangents)
        {
            Path = path ?? ""; Type = type; Properties = properties; Times = times; Values = values; Tangents = tangents;
        }

        public ClipTrack Smooth() { Tangents = ClipTangents.Smooth; return this; }
        public ClipTrack Linear() { Tangents = ClipTangents.Linear; return this; }
        public ClipTrack Constant() { Tangents = ClipTangents.Constant; return this; }
    }

    /// <summary>
    /// Keys of an AnimationClip for <see cref="BuildContext.AnimationClip"/>. Paths are relative to the GameObject that plays the clip
    /// ("" = that object, "Ring" = its child). Keys are (seconds, value) in increasing time.
    /// </summary>
    public sealed class ClipBuilder
    {
        /// <summary>Loop the clip (Loop Time). A looping clip should end where it starts.</summary>
        public bool Loop = true;
        public float FrameRate = 60f;

        internal readonly List<ClipTrack> Tracks = new List<ClipTrack>();
        internal readonly List<AnimationEvent> Events = new List<AnimationEvent>();

        /// <summary>Local position.</summary>
        public ClipTrack Position(string path, params (float t, Vector3 v)[] keys) => Vector3Track(path, typeof(Transform), "m_LocalPosition", keys);

        /// <summary>Local rotation as Euler angles in degrees, interpolated as numbers: (0, 0°) → (2, 360°) spins once (use <c>.Linear()</c> for constant speed).</summary>
        public ClipTrack Rotation(string path, params (float t, Vector3 euler)[] keys) =>
            Add(path, typeof(Transform), new[] { "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z" }, keys.Select(k => k.t),
                keys.Select(k => new[] { k.euler.x, k.euler.y, k.euler.z }).ToArray());

        /// <summary>Local scale.</summary>
        public ClipTrack Scale(string path, params (float t, Vector3 v)[] keys) => Vector3Track(path, typeof(Transform), "m_LocalScale", keys);

        /// <summary>
        /// A float of a component by its serialized name (Light "m_Intensity", "m_Range"; a renderer's material property
        /// "material._Smoothness"). Unknown names are build warnings with the animatable names of that object.
        /// </summary>
        public ClipTrack Float(string path, Type component, string property, params (float t, float v)[] keys) =>
            Add(path, component, new[] { property }, keys.Select(k => k.t), keys.Select(k => new[] { k.v }).ToArray());

        /// <summary>
        /// A color of a component: <paramref name="property"/> without a channel ("m_Color" of a Light, "material._BaseColor" or
        /// "material._EmissionColor" of a MeshRenderer). Values are what <c>Material.SetColor</c> / the Inspector take
        /// (the same numbers as <c>LitSettings.BaseColor</c>/<c>Emission</c>).
        /// </summary>
        public ClipTrack Color(string path, Type component, string property, params (float t, Color c)[] keys) =>
            Add(path, component, new[] { property + ".r", property + ".g", property + ".b", property + ".a" }, keys.Select(k => k.t),
                keys.Select(k => new[] { k.c.r, k.c.g, k.c.b, k.c.a }).ToArray());

        /// <summary>GameObject active state; switches exactly at the keys. Not on the playing object itself (it would stop its own player).</summary>
        public ClipTrack Active(string path, params (float t, bool on)[] keys) =>
            Add(path, typeof(GameObject), new[] { "m_IsActive" }, keys.Select(k => k.t), keys.Select(k => new[] { k.on ? 1f : 0f }).ToArray()).Constant();

        /// <summary>
        /// An animation event at <paramref name="t"/> seconds: the <see cref="ClipPlayer"/> publishes a <see cref="ClipEvent"/>
        /// (counted in play.events as "ClipEvent:&lt;name&gt;"), each time a loop passes it.
        /// </summary>
        public void Event(float t, string name) =>
            Events.Add(new AnimationEvent { time = t, functionName = "OnClipEvent", stringParameter = name });

        ClipTrack Vector3Track(string path, Type type, string property, (float t, Vector3 v)[] keys) =>
            Add(path, type, new[] { property + ".x", property + ".y", property + ".z" }, keys.Select(k => k.t), keys.Select(k => new[] { k.v.x, k.v.y, k.v.z }).ToArray());

        ClipTrack Add(string path, Type type, string[] properties, IEnumerable<float> times, float[][] keyValues)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var t = times.ToArray();
            if (t.Length == 0) throw new ArgumentException($"no keys for {type.Name}.{string.Join("/", properties)} of '{path}'");
            for (var i = 1; i < t.Length; i++)
                if (!(t[i] > t[i - 1])) throw new ArgumentException($"keys of {type.Name}.{properties[0]} of '{path}' are not in increasing time ({t[i - 1]} then {t[i]})");
            var values = new float[properties.Length][];
            for (var c = 0; c < properties.Length; c++)
            {
                values[c] = new float[t.Length];
                for (var k = 0; k < t.Length; k++) values[c][k] = keyValues[k][c];
            }
            var track = new ClipTrack(path, type, properties, t, values, ClipTangents.Smooth);
            Tracks.Add(track);
            return track;
        }
    }

    public sealed partial class BuildContext
    {
        readonly List<(string module, GameObject target, AnimationClip[] clips)> m_Animated = new List<(string, GameObject, AnimationClip[])>();

        /// <summary>
        /// An AnimationClip from keys in code (<see cref="ClipBuilder"/>), saved as &lt;name&gt;.anim. Play it with <see cref="Animate"/>.
        /// </summary>
        public AnimationClip AnimationClip(string name, Action<ClipBuilder> setup)
        {
            var b = new ClipBuilder();
            setup(b);
            var clip = new AnimationClip { frameRate = b.FrameRate };
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            var mode = new Dictionary<ClipTangents, AnimationUtility.TangentMode>
            {
                { ClipTangents.Smooth, AnimationUtility.TangentMode.ClampedAuto },
                { ClipTangents.Linear, AnimationUtility.TangentMode.Linear },
                { ClipTangents.Constant, AnimationUtility.TangentMode.Constant },
            };
            foreach (var track in b.Tracks)
                for (var c = 0; c < track.Properties.Length; c++)
                {
                    var curve = new AnimationCurve(track.Times.Select((t, k) => new Keyframe(t, track.Values[c][k])).ToArray());
                    for (var k = 0; k < curve.length; k++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, k, mode[track.Tangents]);
                        AnimationUtility.SetKeyRightTangentMode(curve, k, mode[track.Tangents]);
                    }
                    bindings.Add(EditorCurveBinding.FloatCurve(track.Path, track.Type, track.Properties[c]));
                    curves.Add(curve);
                }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            if (b.Events.Count > 0) AnimationUtility.SetAnimationEvents(clip, b.Events.OrderBy(e => e.time).ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = b.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return SaveAsset(clip, name + ".anim");
        }

        /// <summary>
        /// Play <paramref name="clips"/> on <paramref name="target"/> through Playables (<see cref="ClipPlayer"/> + an Animator without a
        /// controller, no root motion). The first clip plays when the scene starts (<c>ClipPlayer.playOnEnable</c>); module code
        /// switches with <c>ClipPlayer.Play(name, fade)</c>. After the build, every curve is checked against the objects under
        /// <paramref name="target"/>: a path, component or property that is not there is a build warning (a clip would silently do nothing).
        /// The scene is saved in the first clip's pose at 0 s: what the clip animates, it sets (a builder's own value is overwritten).
        /// </summary>
        public ClipPlayer Animate(GameObject target, params AnimationClip[] clips)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!target.TryGetComponent<Animator>(out var animator)) animator = target.AddComponent<Animator>();
            if (animator.runtimeAnimatorController != null)
                Warn($"'{target.name}': the Animator's controller '{animator.runtimeAnimatorController.name}' is removed - the ClipPlayer plays the clips");
            animator.runtimeAnimatorController = null;   // the avatar stays: a humanoid model's clips need it
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
            if (!target.TryGetComponent<ClipPlayer>(out var player)) player = target.AddComponent<ClipPlayer>();
            player.clips = clips ?? Array.Empty<AnimationClip>();
            player.playOnEnable = player.clips.Length > 0 && player.clips[0] != null ? player.clips[0].name : "";
            m_Animated.Add((Module, target, player.clips));
            return player;
        }

        /// <summary>
        /// After all steps: the curves of <see cref="Animate"/>d clips against the objects they animate, and the scene saved in the
        /// first clip's pose at 0 s (what play mode starts from; edit-mode captures show it).
        /// </summary>
        void CheckAnimations()
        {
            foreach (var (module, target, clips) in m_Animated)
            {
                if (target == null) continue;
                if (clips.Length > 0 && clips[0] != null) clips[0].SampleAnimation(target, 0f);
                foreach (var clip in clips)
                {
                    if (clip == null) { Warnings.Add($"[{module}] '{target.name}': ClipPlayer has a null clip"); continue; }
                    var seen = new HashSet<string>();   // one warning per track, not per channel
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        var problem = CheckBinding(target, b);
                        if (problem != null && seen.Add(problem)) Warnings.Add($"[{module}] clip '{clip.name}' on '{target.name}': {problem} - the curve does nothing");
                    }
                }
            }
        }

        static string CheckBinding(GameObject root, EditorCurveBinding b)
        {
            var t = b.path.Length == 0 ? root.transform : root.transform.Find(b.path);
            if (t == null) return $"no child '{b.path}'";
            var where = b.path.Length == 0 ? "the object" : $"'{b.path}'";
            var property = Stem(b.propertyName);
            var animatable = AnimationUtility.GetAnimatableBindings(t.gameObject, root);
            // Material properties always "resolve", whatever the name: check them against the shaders of the renderer's materials.
            if (b.propertyName.StartsWith("material.", StringComparison.Ordinal))
            {
                if (!(t.GetComponent(b.type) is Renderer)) return $"{where} has no {b.type.Name}";
                if (animatable.Any(a => a.type == b.type && (a.propertyName == b.propertyName || a.propertyName == OtherChannel(b.propertyName)))) return null;
                var names = animatable.Where(a => a.type == b.type && a.propertyName.StartsWith("material.", StringComparison.Ordinal))
                    .Select(a => Stem(a.propertyName)).Distinct().ToArray();
                return $"{b.type.Name}.{property} is not a property of the materials of {where}{Similar(property, names)}";
            }
            if (AnimationUtility.GetEditorCurveValueType(root, b) != null) return null;
            if (b.type == typeof(GameObject) && b.path.Length == 0) return "GameObject.m_IsActive of the object that plays the clip (it would stop its own player)";
            if (b.type != typeof(GameObject) && t.GetComponent(b.type) == null) return $"{where} has no {b.type.Name}";
            var own = animatable.Where(a => a.type == b.type).Select(a => Stem(a.propertyName)).Distinct().ToArray();
            return $"{where} has no animatable {b.type.Name}.{property}{Similar(property, own)}";
        }

        static readonly string[] s_Channels = { "r", "g", "b", "a", "x", "y", "z", "w" };

        /// <summary>The property without its channel: "m_LocalPosition.y" -> "m_LocalPosition", "material._BaseColor.r" -> "material._BaseColor".</summary>
        static string Stem(string property)
        {
            var dot = property.LastIndexOf('.');
            return dot > 0 && Array.IndexOf(s_Channels, property.Substring(dot + 1)) >= 0 ? property.Substring(0, dot) : property;
        }

        /// <summary>Colors are animated per channel as .r/.g/.b/.a (HDR colors are listed as .x/.y/.z/.w; both work).</summary>
        static string OtherChannel(string property)
        {
            var dot = property.LastIndexOf('.');
            if (dot < 0) return property;
            var i = Array.IndexOf(s_Channels, property.Substring(dot + 1));
            return i < 0 ? property : property.Substring(0, dot + 1) + s_Channels[(i + 4) % 8];
        }

        /// <summary>The names sharing the longest start with <paramref name="wanted"/> (a typo), else the first few.</summary>
        static string Similar(string wanted, string[] names)
        {
            string Key(string n) => n.Replace("material.", "").Replace("m_", "").TrimStart('_').ToLowerInvariant();
            int Common(string a, string b) { var i = 0; while (i < a.Length && i < b.Length && a[i] == b[i]) i++; return i; }
            var key = Key(wanted);
            var ranked = names.Select(n => (n, common: Common(Key(n), key))).Where(x => x.common >= 3)
                .OrderByDescending(x => x.common).ThenBy(x => x.n, StringComparer.Ordinal).Select(x => x.n).Take(5).ToArray();
            var hits = ranked.Length > 0 ? ranked : names.Take(5).ToArray();
            return hits.Length == 0 ? "" : $" ({(ranked.Length > 0 ? "similar" : "animatable")}: " + string.Join(", ", hits) + ")";
        }
    }
}
#endif
