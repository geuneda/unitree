using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Harness.Editor
{
    /// <summary>
    /// Well-known paths. Everything the harness writes lives under Library/Harness (never committed) or HarnessOut/.
    /// Project layout (module folders, generated assets, scenes) comes from ProjectSettings/AgentHarness.json (<see cref="HarnessConfig"/>).
    /// </summary>
    [InitializeOnLoad]
    public static class HarnessPaths
    {
        public const string DefaultScenario = "tools/scenarios/default.json";

        /// <summary>Absolute project root with forward slashes. Cached on the main thread so background commands can use it.</summary>
        public static readonly string ProjectRoot;

        public static HarnessConfig Config => HarnessConfig.Current;
        public static string BuildScene => Config.buildScene;
        public static string GeneratedRoot => Config.generatedRoot;

        public static string StateDir => Combine(ProjectRoot, "Library/Harness");
        public static string PlayStateFile => Combine(StateDir, "play_state.json");
        public static string ConsoleFile => Combine(StateDir, "console.ndjson");
        public static string CompileFile => Combine(StateDir, "compile.json");

        static HarnessPaths()
        {
            ProjectRoot = Directory.GetCurrentDirectory().Replace('\\', '/');
            Directory.CreateDirectory(StateDir);
        }

        public static string Combine(string a, string b) => Path.Combine(a, b).Replace('\\', '/');

        /// <summary>Resolve a user path: absolute stays absolute, relative is taken from the project root.</summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return ProjectRoot;
            path = path.Replace('\\', '/');
            return Path.IsPathRooted(path) ? path : Combine(ProjectRoot, path);
        }

        public static string UtcNow() => DateTime.UtcNow.ToString("o");

        /// <summary>
        /// The scene to play or capture: <paramref name="requested"/> (a scenario's "scene") or the config's playScene:
        /// "build" = the generated buildScene, "first" = the first enabled scene in the Build Settings, else a scene path.
        /// </summary>
        public static string ResolvePlayScene(string requested, out string error)
        {
            error = null;
            var want = string.IsNullOrWhiteSpace(requested) ? Config.playScene : requested.Trim().Replace('\\', '/');
            if (string.Equals(want, "build", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(BuildScene)) return BuildScene;
                error = BuildScene + " does not exist; run harness_build";
                return null;
            }
            if (string.Equals(want, "first", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var s in EditorBuildSettings.scenes)
                    if (s.enabled && File.Exists(s.path)) return s.path;
                error = "no enabled scene in the Build Settings: set \"playScene\" in " + HarnessConfig.FileName + " (a scene path), or a scenario's \"scene\"";
                return null;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(want) != null) return want;
            error = "scene not found: " + want + " (playScene in " + HarnessConfig.FileName + " or the scenario's \"scene\")";
            return null;
        }

        /// <summary>
        /// Loaded scenes with unsaved changes that opening another scene would throw away. The generated buildScene is
        /// not counted (it is rebuilt from code); untitled scenes are counted only in an attached project.
        /// </summary>
        public static List<string> DirtyScenes()
        {
            var dirty = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isDirty || !s.isLoaded) continue;
                if (string.IsNullOrEmpty(s.path)) { if (!Config.IsHarnessProject) dirty.Add("(untitled)"); continue; }
                if (s.path == BuildScene) continue;
                dirty.Add(s.path);
            }
            return dirty;
        }

        /// <summary>Open <paramref name="scenePath"/> as the only scene unless it already is; refuse to discard unsaved edits.</summary>
        public static bool OpenScene(string scenePath, out string error)
        {
            error = null;
            if (SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == scenePath) return true;
            var dirty = DirtyScenes();
            if (dirty.Count > 0)
            {
                error = "unsaved changes in " + string.Join(", ", dirty) + ": save or discard them in the Editor first (the harness opens " + scenePath + " and would lose them)";
                return false;
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            return true;
        }
    }
}
