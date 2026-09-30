#if AGENTHARNESS_ANIMATION
using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Harness
{
    /// <summary>
    /// Plays AnimationClips on this GameObject through a PlayableGraph - no AnimatorController asset, no state machine to
    /// author in a window. Clip paths are relative to this GameObject. Builders add it with <c>ctx.Animate(go, clips)</c>;
    /// module code switches clips with <see cref="Play"/>. Time is game time (Time.deltaTime), so a scenario with a fixed
    /// time step plays it the same way every run.
    /// Animation events of the clips (<c>ClipBuilder.Event</c>) are published on the <see cref="EventBus"/> as
    /// <see cref="ClipEvent"/> and counted in play.events as "ClipEvent:&lt;name&gt;".
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class ClipPlayer : MonoBehaviour
    {
        [Tooltip("Clips this player can play, by name. Their curve paths are relative to this GameObject.")]
        public AnimationClip[] clips = Array.Empty<AnimationClip>();

        [Tooltip("Clip played from the start when the object is enabled; empty = none until Play is called.")]
        public string playOnEnable;

        [Tooltip("Playback speed of every clip (1 = as authored).")]
        public float speed = 1f;

        PlayableGraph m_Graph;
        AnimationMixerPlayable m_Mixer;
        AnimationClip[] m_Built = Array.Empty<AnimationClip>();   // the clips the graph was made for
        int m_Current = -1, m_Previous = -1;
        float m_Fade, m_FadeDuration;

        /// <summary>Name of the clip playing (fading in), or null.</summary>
        public string Current => m_Current >= 0 ? m_Built[m_Current].name : null;

        /// <summary>Seconds into the current clip (not wrapped for looping clips), or 0.</summary>
        public float Time => m_Current >= 0 && m_Graph.IsValid() ? (float)m_Mixer.GetInput(m_Current).GetTime() : 0f;

        /// <summary>True once a clip that does not loop has played to its end (it holds its last frame).</summary>
        public bool IsDone => m_Current >= 0 && !m_Built[m_Current].isLooping && Time >= m_Built[m_Current].length;

        void OnEnable()
        {
            Build();
            if (!string.IsNullOrEmpty(playOnEnable) && IndexOf(playOnEnable) >= 0) Play(playOnEnable);
        }

        void OnDisable()
        {
            if (m_Graph.IsValid()) m_Graph.Destroy();
            m_Current = m_Previous = -1;
        }

        /// <summary>A graph for the current <see cref="clips"/>: an output to the Animator, a mixer, a paused input per clip.</summary>
        void Build()
        {
            if (m_Graph.IsValid()) m_Graph.Destroy();
            m_Built = (AnimationClip[])(clips ?? Array.Empty<AnimationClip>()).Clone();
            m_Graph = PlayableGraph.Create($"ClipPlayer {name}");
            m_Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            m_Mixer = AnimationMixerPlayable.Create(m_Graph, m_Built.Length);
            for (var i = 0; i < m_Built.Length; i++)
            {
                if (m_Built[i] == null) continue;
                var p = AnimationClipPlayable.Create(m_Graph, m_Built[i]);
                p.Pause();
                m_Graph.Connect(p, 0, m_Mixer, i);
                m_Mixer.SetInputWeight(i, 0f);
            }
            m_Mixer.SetSpeed(speed);
            AnimationPlayableOutput.Create(m_Graph, "Animation", GetComponent<Animator>()).SetSourcePlayable(m_Mixer);
            m_Current = m_Previous = -1;
            m_Graph.Play();
        }

        bool Stale()
        {
            if (clips == null || clips.Length != m_Built.Length) return true;
            for (var i = 0; i < clips.Length; i++) if (clips[i] != m_Built[i]) return true;
            return false;
        }

        /// <summary>
        /// Play <paramref name="clip"/> from its start, blending from the clip before it over <paramref name="fade"/> seconds
        /// (0 = switch at once). Throws for a name this player does not have. Clips assigned after the object was enabled
        /// (AddComponent, then <c>clips = ...</c>) are picked up here.
        /// </summary>
        public void Play(string clip, float fade = 0f)
        {
            var index = IndexOf(clip);
            if (index < 0) throw new ArgumentException($"ClipPlayer '{name}' has no clip '{clip}' (has: {string.Join(", ", Array.ConvertAll(clips ?? Array.Empty<AnimationClip>(), c => c == null ? "null" : c.name))})");
            if (!isActiveAndEnabled) { playOnEnable = clip; return; }
            if (!m_Graph.IsValid() || Stale()) Build();
            if (m_Previous >= 0 && m_Previous != index) Stop(m_Previous);
            m_Previous = m_Current != index ? m_Current : -1;
            m_Current = index;
            var p = m_Mixer.GetInput(index);
            p.SetTime(0);
            p.SetTime(0);   // twice: a single SetTime still fires the events between the old and the new time
            p.Play();
            m_FadeDuration = m_Previous >= 0 ? Mathf.Max(0f, fade) : 0f;
            m_Fade = 0f;
            ApplyWeights();
        }

        /// <summary>Stop all clips; the object keeps the pose of the last evaluated frame.</summary>
        public void Stop()
        {
            for (var i = 0; i < m_Built.Length; i++) Stop(i);
            m_Current = m_Previous = -1;
        }

        public int IndexOf(string clip)
        {
            if (clips == null) return -1;
            for (var i = 0; i < clips.Length; i++)
                if (clips[i] != null && clips[i].name == clip) return i;
            return -1;
        }

        void Stop(int index)
        {
            if (!m_Graph.IsValid() || index < 0) return;
            var p = m_Mixer.GetInput(index);
            if (p.IsValid()) p.Pause();
            m_Mixer.SetInputWeight(index, 0f);
        }

        void Update()
        {
            if (!m_Graph.IsValid()) return;
            if (!Mathf.Approximately((float)m_Mixer.GetSpeed(), speed)) m_Mixer.SetSpeed(speed);
            if (m_Previous < 0) return;
            m_Fade += UnityEngine.Time.deltaTime * Mathf.Abs(speed);
            ApplyWeights();
        }

        void ApplyWeights()
        {
            var w = m_FadeDuration > 0f ? Mathf.Clamp01(m_Fade / m_FadeDuration) : 1f;
            if (m_Previous >= 0)
            {
                m_Mixer.SetInputWeight(m_Previous, 1f - w);
                if (w >= 1f) { Stop(m_Previous); m_Previous = -1; }
            }
            if (m_Current >= 0) m_Mixer.SetInputWeight(m_Current, w);
        }

        /// <summary>Receiver of the clips' animation events (ClipBuilder.Event): publishes a <see cref="ClipEvent"/>.</summary>
        void OnClipEvent(string eventName)
        {
            EventBus.Publish(new ClipEvent(eventName, gameObject), nameof(ClipEvent) + ":" + eventName);
        }
    }

    /// <summary>An animation event of a clip played by a <see cref="ClipPlayer"/> (<c>ClipBuilder.Event(t, name)</c>).</summary>
    public readonly struct ClipEvent
    {
        public readonly string Name;
        public readonly GameObject Source;
        public ClipEvent(string name, GameObject source) { Name = name; Source = source; }
    }
}
#endif
