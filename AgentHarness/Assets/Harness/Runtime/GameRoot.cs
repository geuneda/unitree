using System;
using System.Collections.Generic;
using UnityEngine;

namespace Harness
{
    /// <summary>Handed to <see cref="IGameModule.Init"/>.</summary>
    public sealed class GameContext
    {
        /// <summary>The scene root GameObject a builder created for <paramref name="module"/> (by convention named after the module), or null.</summary>
        public GameObject FindRoot(string module)
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == module) return go;
            return null;
        }

        /// <summary>Find a descendant of the module root by relative path ("Spinner" or "Props/Spinner").</summary>
        public Transform Find(string module, string relativePath)
        {
            var root = FindRoot(module);
            return root != null ? root.transform.Find(relativePath) : null;
        }
    }

    /// <summary>
    /// Owns the module list: registration → Init (after the first scene load) → Tick every frame → Dispose.
    /// Created automatically; nothing has to be placed in the scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameRoot : MonoBehaviour
    {
        static readonly List<IGameModule> s_Pending = new List<IGameModule>();
        static readonly List<IGameModule> s_Active = new List<IGameModule>();
        static GameRoot s_Instance;
        static GameContext s_Context;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Pending.Clear();
            s_Active.Clear();
            s_Instance = null;
            s_Context = null;
        }

        public static IReadOnlyList<IGameModule> Modules => s_Active;

        public static void Register(IGameModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            foreach (var m in s_Pending) if (m.Name == module.Name) throw new InvalidOperationException($"Module '{module.Name}' registered twice.");
            foreach (var m in s_Active) if (m.Name == module.Name) throw new InvalidOperationException($"Module '{module.Name}' registered twice.");

            if (s_Instance != null) InitModule(module); // late registration (e.g. additive content)
            else s_Pending.Add(module);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var stale = UnityCompat.FindObjects<GameRoot>(FindObjectsInactive.Include);
            if (stale.Length > 0)
                Debug.LogError($"[GameRoot] {stale.Length} GameRoot object(s) from a previous play session still exist - modules would tick more than once per frame");
            // No HideFlags.DontSave here: DontSave objects survive leaving play mode, and with Domain Reload off
            // every later session would tick the old roots too.
            var go = new GameObject("[GameRoot]");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<GameRoot>();
            s_Context = new GameContext();

            s_Pending.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
            var toInit = s_Pending.ToArray();
            s_Pending.Clear();
            foreach (var m in toInit) InitModule(m);

            HarnessProbe.MarkReady();
        }

        static void InitModule(IGameModule m)
        {
            try
            {
                m.Init(s_Context);
                s_Active.Add(m);
                s_Active.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
                HarnessProbe.InitializedModules.Add(m.Name);
            }
            catch (Exception e)
            {
                HarnessProbe.FailedModules.Add(m.Name);
                Debug.LogError($"[GameRoot] Init failed for module '{m.Name}'");
                Debug.LogException(e);
            }
        }

        void Update()
        {
            if (s_Instance != this) return;
            var dt = Time.deltaTime;
            for (var i = 0; i < s_Active.Count; i++)
            {
                try { s_Active[i].Tick(dt); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        void OnDestroy()
        {
            if (s_Instance != this) return;
            for (var i = s_Active.Count - 1; i >= 0; i--)
            {
                try { s_Active[i].Dispose(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            s_Active.Clear();
            EventBus.Clear();
            s_Instance = null;
        }
    }
}
