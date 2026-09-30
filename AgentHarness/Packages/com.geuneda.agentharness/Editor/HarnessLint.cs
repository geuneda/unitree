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
            "Rule checks: (static-reset) with Domain Reload off, mutable statics in module/harness runtime assemblies must live in a " +
            "type with a [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] reset; (module-asmdef) every .cs of a module under a " +
            "module root belongs to that module's asmdef; (module-boundary) modules under a module root reference no other module " +
            "except the contracts; (contract-file) a contracts file is <Module>Events.cs and holds the events that module publishes " +
            "(EventBus.Publish in its compiled code), one publishing module per event; (contract-name) no two contracts types share a " +
            "name. Modules and contracts: ProjectSettings/AgentHarness.json. Returns {ok, issues[], skipped[]}.",
            Tags = new[] { "harness", "scripts" })]
        public static object Lint()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var issues = new List<Issue>();
            var skipped = new List<string>();
            // With Domain Reload on (an attached project's default) statics are reset by the reload itself.
            if (HarnessSetup.DomainReloadDisabled())
                CheckStatics(issues);
            else skipped.Add("static-reset: Domain Reload is on, statics are reset on every play");
            CheckModuleAssemblies(issues);
            var contractsMs = sw.Elapsed.TotalMilliseconds;
            HarnessContracts.Lint(issues);
            contractsMs = sw.Elapsed.TotalMilliseconds - contractsMs;
            return new { ok = issues.Count == 0, issues, skipped, ms = Math.Round(sw.Elapsed.TotalMilliseconds, 1), contractsMs = Math.Round(contractsMs, 1) };
        }

        static void CheckStatics(List<Issue> issues)
        {
            var config = HarnessPaths.Config;
            var runtimeAsms = new Dictionary<string, string>(StringComparer.Ordinal); // name → first source
            foreach (var a in HarnessPaths.Assemblies(AssembliesType.Player))
            {
                var src = a.sourceFiles.FirstOrDefault();
                if (src == null) continue;
                src = src.Replace('\\', '/');
                // Assemblies of modules (asmdefs inside a module folder) and the harness runtime. Predefined assemblies
                // (Assembly-CSharp) of an attached project are not checked.
                if (a.name.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) continue;
                var module = config.ModuleOf(src);
                if (module.Length > 0 && (module != "Harness" || a.name == "Harness.Runtime"))
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
            // The module rules hold for modules under a module root (Assets/Game/<Module>), not for folders of existing code.
            var config = HarnessPaths.Config;
            var roots = config.moduleRoots.Where(AssetDatabase.IsValidFolder).ToArray();
            if (roots.Length == 0) return;
            var contracts = config.contracts.Length > 0 ? config.ModuleOf(config.contracts + "/x.cs") : "";

            // asmdef name → module (folder under a module root), and module → its asmdef names.
            var asmModule = new Dictionary<string, string>(StringComparer.Ordinal);
            var asmdefs = new List<(string path, AsmdefJson json, string module)>();
            foreach (var g in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", roots))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var module = config.ModuleOf(p, out _, out var fromRoot);
                if (!fromRoot) continue;
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
                    if (other == module || other == contracts) continue;
                    issues.Add(new Issue
                    {
                        rule = "module-boundary",
                        module = module,
                        file = path,
                        message = $"{json.name} references module '{other}' ({refName}). Modules talk through EventBus; put shared event types in {(config.contracts.Length > 0 ? config.contracts : "a contracts folder (\"contracts\" in " + HarnessConfig.FileName + ")")}.",
                    });
                }
            }

            foreach (var root in roots)
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var p = file.Replace('\\', '/');
                var module = config.ModuleOf(p, out var folder, out var fromRoot);
                if (!fromRoot) continue;
                var asm = CompilationPipeline.GetAssemblyNameFromScriptPath(p) ?? "";
                if (asm.StartsWith("Assembly-CSharp", StringComparison.Ordinal))
                    issues.Add(new Issue
                    {
                        rule = "module-asmdef",
                        module = module,
                        file = p,
                        message = $"compiled into {asm}: add an asmdef to {folder}/ (and Builders/) so the module recompiles alone",
                    });
            }
        }
    }
}
