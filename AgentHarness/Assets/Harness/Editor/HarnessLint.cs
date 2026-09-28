using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>Mechanical checks for the harness rules that the compiler cannot enforce.</summary>
    public static class HarnessLint
    {
        [Serializable]
        public sealed class Issue
        {
            public string rule;
            public string module;
            public string file;
            public string message;
        }

        [Serializable]
        sealed class AsmdefJson
        {
            public string name;
            public string[] references = Array.Empty<string>();
        }

        [CliCommand("harness_lint",
            "Rule checks: (static-reset) mutable statics in game/harness runtime code must live in a type with a " +
            "[RuntimeInitializeOnLoadMethod(SubsystemRegistration)] reset (Domain Reload is off); (module-asmdef) every .cs under " +
            "Assets/Game/<Module> belongs to that module's asmdef; (module-boundary) modules reference no other module except Game.Contracts.",
            Tags = new[] { "harness", "scripts" })]
        public static object Lint()
        {
            var issues = new List<Issue>();
            CheckStatics(issues);
            CheckModuleAssemblies(issues);
            return new { ok = issues.Count == 0, issues };
        }

        static void CheckStatics(List<Issue> issues)
        {
            var runtimeAsms = new Dictionary<string, string>(StringComparer.Ordinal); // name → first source
            foreach (var a in CompilationPipeline.GetAssemblies(AssembliesType.Player))
            {
                var src = a.sourceFiles.FirstOrDefault();
                if (src == null) continue;
                src = src.Replace('\\', '/');
                if (src.StartsWith(HarnessPaths.GameRoot + "/", StringComparison.Ordinal) || src.StartsWith("Assets/Harness/Runtime/", StringComparison.Ordinal))
                    runtimeAsms[a.name] = src;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (!runtimeAsms.TryGetValue(name, out var anySource)) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                {
                    if (t.IsDefined(typeof(CompilerGeneratedAttribute), false)) continue;
                    var offenders = new List<string>();
                    foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsLiteral) continue;
                        if (f.IsInitOnly && !IsMutableContainer(f.FieldType)) continue;
                        offenders.Add(f.Name);
                    }
                    if (offenders.Count == 0 || HasSubsystemReset(t)) continue;
                    issues.Add(new Issue
                    {
                        rule = "static-reset",
                        module = HarnessLogParse.ModuleOf(anySource),
                        file = FindScript(t) ?? anySource,
                        message = $"{t.FullName}: static {string.Join(", ", offenders)} not reset - add [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics()",
                    });
                }
            }
        }

        static bool IsMutableContainer(Type t)
        {
            if (!t.IsGenericType) return false;
            var d = t.GetGenericTypeDefinition();
            return d == typeof(List<>) || d == typeof(Dictionary<,>) || d == typeof(HashSet<>) || d == typeof(Queue<>) || d == typeof(Stack<>);
        }

        static bool HasSubsystemReset(Type t)
        {
            for (var cur = t; cur != null; cur = cur.DeclaringType)
            {
                foreach (var m in cur.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var a = m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                    if (a != null && a.loadType == RuntimeInitializeLoadType.SubsystemRegistration) return true;
                }
            }
            return false;
        }

        static string FindScript(Type t)
        {
            var guids = AssetDatabase.FindAssets(t.Name + " t:MonoScript");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == t.Name) return p;
            }
            return null;
        }

        static void CheckModuleAssemblies(List<Issue> issues)
        {
            if (!AssetDatabase.IsValidFolder(HarnessPaths.GameRoot)) return;

            // asmdef name → module (folder under Assets/Game), and module → its asmdef names.
            var asmModule = new Dictionary<string, string>(StringComparer.Ordinal);
            var asmdefs = new List<(string path, AsmdefJson json, string module)>();
            foreach (var g in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { HarnessPaths.GameRoot }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var module = HarnessLogParse.ModuleOf(p);
                var json = JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(p));
                asmdefs.Add((p, json, module));
                asmModule[json.name] = module;
            }

            foreach (var (path, json, module) in asmdefs)
            {
                foreach (var raw in json.references ?? Array.Empty<string>())
                {
                    var refName = raw;
                    if (raw.StartsWith("GUID:", StringComparison.Ordinal))
                    {
                        var rp = AssetDatabase.GUIDToAssetPath(raw.Substring(5));
                        refName = string.IsNullOrEmpty(rp) ? raw : JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(rp)).name;
                    }
                    if (!asmModule.TryGetValue(refName, out var other)) continue; // not a game module
                    if (other == module || other == "Contracts") continue;
                    issues.Add(new Issue
                    {
                        rule = "module-boundary",
                        module = module,
                        file = path,
                        message = $"{json.name} references module '{other}' ({refName}). Modules talk through EventBus; put shared event types in Assets/Game/Contracts.",
                    });
                }
            }

            foreach (var file in Directory.GetFiles(HarnessPaths.GameRoot, "*.cs", SearchOption.AllDirectories))
            {
                var p = file.Replace('\\', '/');
                var asm = CompilationPipeline.GetAssemblyNameFromScriptPath(p) ?? "";
                if (asm.StartsWith("Assembly-CSharp", StringComparison.Ordinal))
                    issues.Add(new Issue
                    {
                        rule = "module-asmdef",
                        module = HarnessLogParse.ModuleOf(p),
                        file = p,
                        message = $"compiled into {asm}: add an asmdef to Assets/Game/{HarnessLogParse.ModuleOf(p)}/ (and Builders/) so the module recompiles alone",
                    });
            }
        }
    }
}
