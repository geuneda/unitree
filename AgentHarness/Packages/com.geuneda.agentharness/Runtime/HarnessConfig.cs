using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Harness
{
    /// <summary>One folder that is one module (e.g. existing code: {"name": "Gameplay", "path": "Assets/Scripts/Gameplay"}).</summary>
    [Serializable]
    public sealed class ModuleFolder
    {
        public string name;
        public string path;
    }

    /// <summary>
    /// ProjectSettings/AgentHarness.json: how the harness maps this project. Read by the Editor commands (harness_*) and by
    /// tools/*.ps1. A missing file or field takes the default below, and the defaults are those of a harness attached to an
    /// existing project: change nothing, own nothing, play the first scene of the Build Settings.
    /// </summary>
    [Serializable]
    public sealed class HarnessConfig
    {
        public const string FileName = "ProjectSettings/AgentHarness.json";
        public const string PackageName = "com.geuneda.agentharness";
        public const string PackageRoot = "Packages/" + PackageName;
        public const string GeneratedLabel = "AgentHarnessGenerated";

        /// <summary>
        /// "harness": a project built around the harness (the sample): harness_setup turns Domain Reload off, Run In Background
        /// on, removes URP template samples; harness_build owns buildScene and puts it first in the Build Settings.
        /// "attach": an existing project: harness_setup changes nothing unless asked ({"apply":[...]}), harness_build never
        /// touches the Build Settings nor a scene or asset it did not generate.
        /// </summary>
        public string setup = "attach";

        /// <summary>Folders whose subfolders are modules (Assets/Game/&lt;Module&gt;/: own asmdef, module rules, Builders/).</summary>
        public string[] moduleRoots = Array.Empty<string>();

        /// <summary>Single folders that are one module each (existing code): errors name them, submit/land move them.</summary>
        public ModuleFolder[] modules = Array.Empty<ModuleFolder>();

        /// <summary>Shared event types between modules, add-only for submit.ps1 ("" = none).</summary>
        public string contracts = "";

        /// <summary>Where BuildContext writes generated assets (&lt;generatedRoot&gt;/&lt;Module&gt;/...).</summary>
        public string generatedRoot = "Assets/AgentHarness/Generated";

        /// <summary>The scene harness_build generates from IBuildSteps.</summary>
        public string buildScene = "Assets/AgentHarness/Main.unity";

        /// <summary>What harness_play/harness_capture open: "build" (buildScene), "first" (first enabled Build Settings scene) or a scene path.</summary>
        public string playScene = "first";

        /// <summary>Packages install.ps1 added for the harness besides itself (e.g. com.unity.inputsystem); uninstall removes them, release builds leave them out.</summary>
        public string[] installAdded = Array.Empty<string>();

        [NonSerialized] public string loadError;
        [NonSerialized] public bool fromFile;

        public bool IsHarnessProject => string.Equals(setup, "harness", StringComparison.OrdinalIgnoreCase);

        // ---- Loading (Editor: the project's file, re-read when it changes; players: defaults) ------------------

        static readonly object s_Lock = new object();
        static HarnessConfig s_Current;
        static DateTime s_Stamp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lock (s_Lock) { s_Current = null; s_Stamp = default; }
        }

        /// <summary>Absolute project root with forward slashes (Editor), or "" in a player.</summary>
        public static string ProjectRoot
        {
            get
            {
#if UNITY_EDITOR
                return Directory.GetCurrentDirectory().Replace('\\', '/');
#else
                return "";
#endif
            }
        }

        public static HarnessConfig Current
        {
            get
            {
#if UNITY_EDITOR
                var path = Path.Combine(ProjectRoot, FileName);
                DateTime stamp;
                try { stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
                catch { stamp = DateTime.MinValue; }
                lock (s_Lock)
                {
                    if (s_Current == null || stamp != s_Stamp) { s_Current = Load(path); s_Stamp = stamp; }
                    return s_Current;
                }
#else
                lock (s_Lock) return s_Current ?? (s_Current = new HarnessConfig());
#endif
            }
        }

        public static HarnessConfig Load(string path)
        {
            var c = new HarnessConfig();
            try
            {
                if (File.Exists(path))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path), c);
                    c.fromFile = true;
                }
            }
            catch (Exception e)
            {
                c = new HarnessConfig { loadError = $"{FileName}: {e.Message}" };
            }
            c.Normalize();
            return c;
        }

        void Normalize()
        {
            static string N(string p) => string.IsNullOrWhiteSpace(p) ? "" : p.Trim().Replace('\\', '/').TrimEnd('/');
            moduleRoots = Array.FindAll(Array.ConvertAll(moduleRoots ?? Array.Empty<string>(), N), p => p.Length > 0);
            var list = new List<ModuleFolder>();
            foreach (var m in modules ?? Array.Empty<ModuleFolder>())
                if (m != null && !string.IsNullOrWhiteSpace(m.name) && N(m.path).Length > 0) list.Add(new ModuleFolder { name = m.name.Trim(), path = N(m.path) });
            modules = list.ToArray();
            contracts = N(contracts);
            installAdded = Array.FindAll(installAdded ?? Array.Empty<string>(), a => !string.IsNullOrWhiteSpace(a));
            generatedRoot = N(generatedRoot);
            buildScene = N(buildScene);
            playScene = string.IsNullOrWhiteSpace(playScene) ? "first" : playScene.Trim().Replace('\\', '/');
            setup = string.IsNullOrWhiteSpace(setup) ? "attach" : setup.Trim().ToLowerInvariant();
        }

        // ---- Modules ---------------------------------------------------------------------------------------

        /// <summary>
        /// Module of a project path: an explicit module folder (longest match), else the first folder below a module root,
        /// "Harness" for the harness package, else "". <paramref name="folder"/> is the module's folder.
        /// </summary>
        public string ModuleOf(string projectPath, out string folder, out bool fromRoot)
        {
            folder = ""; fromRoot = false;
            if (string.IsNullOrEmpty(projectPath)) return "";
            var p = projectPath.Replace('\\', '/');
            if (p.StartsWith(PackageRoot + "/", StringComparison.Ordinal) || p.StartsWith("Assets/Harness/", StringComparison.Ordinal))
            {
                folder = PackageRoot;
                return "Harness";
            }
            string best = null;
            foreach (var m in modules)
            {
                if ((p == m.path || p.StartsWith(m.path + "/", StringComparison.Ordinal)) && (best == null || m.path.Length > folder.Length))
                {
                    best = m.name;
                    folder = m.path;
                }
            }
            if (best != null) return best;
            foreach (var root in moduleRoots)
            {
                if (!p.StartsWith(root + "/", StringComparison.Ordinal)) continue;
                var rest = p.Substring(root.Length + 1);
                var slash = rest.IndexOf('/');
                // A file directly in the root (or its .meta) belongs to no module; a folder's .meta belongs to the folder.
                var name = slash > 0 ? rest.Substring(0, slash) : rest.EndsWith(".meta", StringComparison.Ordinal) ? rest.Substring(0, rest.Length - 5) : "";
                if (name.Length == 0 || (slash < 0 && !rest.EndsWith(".meta", StringComparison.Ordinal))) return "";
                folder = root + "/" + name;
                fromRoot = true;
                return name;
            }
            return "";
        }

        public string ModuleOf(string projectPath) => ModuleOf(projectPath, out _, out _);

        /// <summary>
        /// Code that belongs to this project rather than to Unity or a package it installed: Assets/, the harness package
        /// (a harness bug should be seen), and embedded packages (folders under Packages/).
        /// </summary>
        public bool IsProjectPath(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath)) return false;
            var p = projectPath.Replace('\\', '/');
            if (p.StartsWith("Assets/", StringComparison.Ordinal)) return true;
            if (!p.StartsWith("Packages/", StringComparison.Ordinal)) return false;
            var slash = p.IndexOf('/', "Packages/".Length);
            var name = slash > 0 ? p.Substring("Packages/".Length, slash - "Packages/".Length) : "";
            return name == PackageName || EmbeddedPackages.Contains(name);
        }

        HashSet<string> m_Embedded;

        /// <summary>Names of the packages embedded in this project (folders with a package.json under Packages/).</summary>
        public HashSet<string> EmbeddedPackages
        {
            get
            {
                if (m_Embedded != null) return m_Embedded;
                var set = new HashSet<string>(StringComparer.Ordinal);
#if UNITY_EDITOR
                try
                {
                    foreach (var d in Directory.GetDirectories(Path.Combine(ProjectRoot, "Packages")))
                        if (File.Exists(Path.Combine(d, "package.json"))) set.Add(Path.GetFileName(d));
                }
                catch { }
#endif
                return m_Embedded = set;
            }
        }
    }
}
