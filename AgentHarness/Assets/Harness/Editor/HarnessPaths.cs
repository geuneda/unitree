using System;
using System.IO;
using UnityEditor;

namespace Harness.Editor
{
    /// <summary>Well-known paths. Everything the harness writes lives under Library/Harness (never committed) or HarnessOut/.</summary>
    [InitializeOnLoad]
    public static class HarnessPaths
    {
        public const string MainScene = "Assets/Scenes/Main.unity";
        public const string GameRoot = "Assets/Game";
        public const string GeneratedRoot = "Assets/Generated";
        public const string DefaultScenario = "tools/scenarios/default.json";

        /// <summary>Absolute project root with forward slashes. Cached on the main thread so background commands can use it.</summary>
        public static readonly string ProjectRoot;

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
    }
}
