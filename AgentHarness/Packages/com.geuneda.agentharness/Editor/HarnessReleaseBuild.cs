using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>
    /// Keeps the harness out of release (non-development) Player builds. Harness.Runtime is left out by its define
    /// constraint (UNITY_EDITOR || DEVELOPMENT_BUILD || AGENTHARNESS_RUNTIME). The harness's Editor connection,
    /// com.unity.pipeline, also puts a runtime assembly (Unity.Pipeline.Attributes) and its Newtonsoft.Json into every
    /// build, and install.ps1 may have added com.unity.inputsystem for it ("installAdded"). They are filtered out here
    /// when only the harness brought their package into the project (Packages/packages-lock.json, not counting
    /// installAdded as the project's own) and no other assembly of the build references them.
    /// </summary>
    sealed class HarnessReleaseBuild : IFilterBuildAssemblies
    {
        // assembly -> the package it comes from, in removal order (Unity.Pipeline.Attributes references Newtonsoft.Json)
        static readonly (string assembly, string package)[] s_HarnessOnly =
        {
            ("Unity.Pipeline.Attributes", "com.unity.pipeline"),
            ("Newtonsoft.Json", "com.unity.nuget.newtonsoft-json"),
            ("Unity.InputSystem.ForUI", "com.unity.inputsystem"),
            ("Unity.InputSystem", "com.unity.inputsystem"),
        };

        public int callbackOrder => 0;

        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if ((buildOptions & BuildOptions.Development) != 0) return assemblies;
            var reachable = PackagesWithoutHarness();
            if (reachable == null) return assemblies;

            // Candidates: assemblies of packages only the harness brought in.
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (assembly, package) in s_HarnessOnly)
                if (!reachable.Contains(package) && Array.Exists(assemblies, a => NameOf(a).Equals(assembly, StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(assembly);
            if (candidates.Count == 0) return assemblies;

            // Keep a candidate that the rest of the build actually uses, and whatever a kept candidate uses (fixpoint).
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var users = new List<string>();
            foreach (var a in assemblies) if (!candidates.Contains(NameOf(a)) && !IsEngineOrFramework(NameOf(a))) users.Add(a);
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var c in candidates)
                {
                    if (kept.Contains(c)) continue;
                    var byUsers = References(users, c);
                    var byKept = false;
                    foreach (var k in kept) if (References(new List<string> { Array.Find(assemblies, a => NameOf(a).Equals(k, StringComparison.OrdinalIgnoreCase)) }, c)) { byKept = true; break; }
                    if (byUsers || byKept) { kept.Add(c); changed = true; }
                }
            }
            var removed = new List<string>();
            var result = new List<string>();
            foreach (var a in assemblies)
            {
                if (candidates.Contains(NameOf(a)) && !kept.Contains(NameOf(a))) removed.Add(Path.GetFileName(a));
                else result.Add(a);
            }
            if (removed.Count > 0)
                Debug.Log($"[Harness] release build: left out {string.Join(", ", removed)} (brought in only for {HarnessConfig.PackageName})" +
                          (kept.Count > 0 ? $"; kept {string.Join(", ", kept)} (used by the game)" : ""));
            return result.ToArray();
        }

        static string NameOf(string path) => Path.GetFileNameWithoutExtension(path);

        // Unity's engine modules and the .NET framework name Unity packages only in InternalsVisibleTo attributes: no dependency.
        static bool IsEngineOrFramework(string name) =>
            name.StartsWith("UnityEngine", StringComparison.Ordinal) || name.StartsWith("System", StringComparison.Ordinal) ||
            name == "mscorlib" || name == "netstandard" || name.StartsWith("Mono.", StringComparison.Ordinal);

        /// <summary>
        /// Does one of <paramref name="paths"/> reference <paramref name="assembly"/>? The compiler writes an assembly reference
        /// (name, UTF-8, NUL-ended in the metadata strings) only for what the code uses; compile-time references of
        /// Assembly-CSharp include every auto-referenced assembly and say nothing about use.
        /// </summary>
        static bool References(List<string> paths, string assembly)
        {
            var needle = Encoding.UTF8.GetBytes(assembly + "\0");
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                try { if (IndexOf(File.ReadAllBytes(p), needle) >= 0) return true; }
                catch { return true; }   // cannot tell: keep it
            }
            return false;
        }

        static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        /// <summary>
        /// Packages reachable from the project's own roots (every depth-0 entry of packages-lock.json but the harness),
        /// or null when the lock file cannot be read (then nothing is filtered).
        /// </summary>
        static HashSet<string> PackagesWithoutHarness()
        {
            string text;
            try { text = File.ReadAllText(Path.Combine(HarnessPaths.ProjectRoot, "Packages/packages-lock.json")); }
            catch { return null; }
            var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var roots = new List<string>();
            foreach (Match b in Regex.Matches(text, "(?m)^    \"(?<name>[^\"]+)\": \\{\\r?\\n(?<body>(?:.*\\r?\\n)*?)    \\},?\\r?$"))
            {
                var name = b.Groups["name"].Value;
                var body = b.Groups["body"].Value;
                var list = new List<string>();
                var d = Regex.Match(body, "\"dependencies\": \\{(?<list>[^}]*)\\}");
                if (d.Success) foreach (Match m in Regex.Matches(d.Groups["list"].Value, "\"(?<n>[^\"]+)\":")) list.Add(m.Groups["n"].Value);
                deps[name] = list;
                if (Regex.IsMatch(body, "\"depth\": 0\\b") && name != HarnessConfig.PackageName && Array.IndexOf(HarnessConfig.Current.installAdded, name) < 0) roots.Add(name);
            }
            if (deps.Count == 0) return null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(roots);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (!seen.Add(n) || !deps.TryGetValue(n, out var next)) continue;
                foreach (var x in next) if (!seen.Contains(x)) queue.Enqueue(x);
            }
            return seen;
        }
    }
}
