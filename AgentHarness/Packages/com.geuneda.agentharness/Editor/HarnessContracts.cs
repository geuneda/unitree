using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Harness.Editor
{
    /// <summary>
    /// The contracts folder: event types shared by modules (G5-4). Parallel agents meet here, so it has rules:
    /// - one file per publishing module: an event type lives in &lt;contracts&gt;/&lt;Module&gt;Events.cs of the one module that publishes it
    ///   (one module = one agent, so no two agents edit one file);
    /// - names are unique in the contracts (play.events counts events by type name, and two agents adding the same name collide);
    /// - add-only: a type that landed never changes; new types are added (to the module's file).
    /// The lint (harness_lint) checks the first two on the compiled modules (which module publishes what: EventBus.Publish calls in
    /// their IL). submit.ps1 and land.ps1 check names and add-only changes before anything is copied or merged, from the sources
    /// (harness_contracts: what C# sources declare, with Roslyn).
    /// </summary>
    public static class HarnessContracts
    {
        /// <summary>A top-level type a C# source declares.</summary>
        [Serializable]
        public sealed class Decl
        {
            public string name;   // Type.Name: "SpinnerLap", "Pair`2"
            public string ns;     // "Game.Contracts"
            public string full;   // Type.FullName: "Game.Contracts.SpinnerLap"
            public string kind;   // struct | class | interface | record | enum | delegate
            public int line;      // of its name
            public string hash;   // of its tokens (attributes included; whitespace and comments left out)
        }

        [Serializable]
        sealed class SourceIn
        {
            public string id;
            public string path;
            public string text;
        }

        [Serializable]
        sealed class SourceList
        {
            public SourceIn[] items;
        }

        [CliCommand("harness_contracts",
            "Top-level types that C# sources declare, read with Roslyn (nothing is compiled): {ok, sources:[{id, types:[{name, ns, full, kind, " +
            "line, hash}], error}]}. hash covers the type's tokens (attributes included, whitespace and comments not): the same hash = the same " +
            "type. submit.ps1 / land.ps1 check the contracts folder with it (unique names, landed types unchanged).",
            Tags = new[] { "harness", "scripts" })]
        public static object Declared(
            [CliArg("sources", "JSON array of {id, path} (absolute or from the project root) or {id, text} (the source inline).")] string sources)
        {
            var sw = Stopwatch.StartNew();
            SourceIn[] items;
            try { items = JsonUtility.FromJson<SourceList>("{\"items\":" + (string.IsNullOrWhiteSpace(sources) ? "[]" : sources) + "}").items ?? Array.Empty<SourceIn>(); }
            catch (Exception e) { return new { ok = false, error = "sources is not a JSON array of {id, path | text}: " + e.Message }; }
            var result = new List<object>();
            foreach (var s in items)
            {
                string text = s.text, error = null;
                if (!string.IsNullOrEmpty(s.path))
                {
                    try { text = File.ReadAllText(HarnessPaths.Resolve(s.path)); }
                    catch (Exception e) { error = e.Message; }
                }
                result.Add(new { id = s.id, types = error == null ? Parse(text ?? "", s.path ?? "") : new List<Decl>(), error });
            }
            return new { ok = true, sources = result, ms = Math.Round(sw.Elapsed.TotalMilliseconds, 1) };
        }

        // ---- Declarations (Roslyn syntax only) -----------------------------------------------------------------------

        public static List<Decl> Parse(string text, string path)
        {
            var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None), path);
            var list = new List<Decl>();
            Collect(tree.GetRoot(), "", list);
            return list;
        }

        static void Collect(SyntaxNode parent, string ns, List<Decl> list)
        {
            foreach (var child in parent.ChildNodes())
            {
                switch (child)
                {
                    case NamespaceDeclarationSyntax n:
                        var name = string.Concat(n.Name.DescendantTokens().Select(t => t.Text));
                        Collect(n, ns.Length > 0 ? ns + "." + name : name, list);
                        break;
                    case EnumDeclarationSyntax e:
                        Add(e, e.Identifier, 0, "enum", ns, list);
                        break;
                    case TypeDeclarationSyntax t:
                        Add(t, t.Identifier, t.TypeParameterList?.Parameters.Count ?? 0, t.Keyword.Text, ns, list);
                        break;
                    case DelegateDeclarationSyntax d:
                        Add(d, d.Identifier, d.TypeParameterList?.Parameters.Count ?? 0, "delegate", ns, list);
                        break;
                }
            }
        }

        static void Add(SyntaxNode node, SyntaxToken identifier, int arity, string kind, string ns, List<Decl> list)
        {
            var name = identifier.Text + (arity > 0 ? "`" + arity : "");
            list.Add(new Decl
            {
                name = name,
                ns = ns,
                full = ns.Length > 0 ? ns + "." + name : name,
                kind = kind,
                line = identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                hash = Hash(node),
            });
        }

        static string Hash(SyntaxNode node)
        {
            var sb = new StringBuilder();
            foreach (var t in node.DescendantTokens()) sb.Append(t.Text).Append('\u0001');
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        // ---- Lint ------------------------------------------------------------------------------------------------------

        /// <summary>
        /// contract-file: every .cs of the contracts folder is &lt;Module&gt;Events.cs of a module, and an event type published by module M
        /// (EventBus.Publish in M's compiled code) is declared in MEvents.cs (so it has one publishing module). contract-name: no two
        /// types of the contracts share a name.
        /// </summary>
        internal static void Lint(List<HarnessLint.Issue> issues)
        {
            var config = HarnessPaths.Config;
            if (config.contracts.Length == 0 || !AssetDatabase.IsValidFolder(config.contracts)) return;
            var contractsModule = config.ModuleOf(config.contracts + "/x.cs");
            if (contractsModule.Length == 0) contractsModule = Path.GetFileName(config.contracts);

            var modules = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var root in config.moduleRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var d in Directory.GetDirectories(root))
                {
                    var n = Path.GetFileName(d);
                    if (n != contractsModule && !n.StartsWith(".", StringComparison.Ordinal) && !n.EndsWith("~", StringComparison.Ordinal)) modules.Add(n);
                }
            }
            foreach (var m in config.modules) modules.Add(m.name);

            // The module a contracts file belongs to by its name (<Module>Events.cs), or null.
            string FileModule(string file)
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (!stem.EndsWith("Events", StringComparison.Ordinal)) return null;
                var m = stem.Substring(0, stem.Length - "Events".Length);
                return modules.Contains(m) ? m : null;
            }
            var example = modules.FirstOrDefault() ?? "Foo";

            var decls = new List<(Decl d, string file)>();
            foreach (var f in Directory.GetFiles(config.contracts, "*.cs", SearchOption.AllDirectories).Select(f => f.Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (FileModule(f) == null)
                    issues.Add(new HarnessLint.Issue
                    {
                        rule = "contract-file",
                        module = contractsModule,
                        file = f,
                        message = $"{Path.GetFileName(f)}: a contracts file is <Module>Events.cs of the module that publishes its events " +
                                  $"(e.g. {example}Events.cs), so no two agents edit one file. Modules: {string.Join(", ", modules)}",
                    });
                foreach (var d in Parse(File.ReadAllText(f), f)) decls.Add((d, f));
            }

            foreach (var g in decls.GroupBy(x => x.d.name).Where(g => g.Count() > 1))
                foreach (var x in g)
                {
                    var others = g.Where(o => o.file != x.file || o.d.line != x.d.line).Select(o => $"{o.file}:{o.d.line} ({o.d.full})");
                    issues.Add(new HarnessLint.Issue
                    {
                        rule = "contract-name",
                        module = FileModule(x.file) ?? contractsModule,
                        file = x.file,
                        message = $"{x.d.full} (line {x.d.line}): the event name {x.d.name} is also declared in {string.Join(", ", others)}. " +
                                  "play.events counts events by type name and two agents adding one name collide: rename one.",
                    });
                }

            var byFull = new Dictionary<string, (Decl d, string file)>(StringComparer.Ordinal);
            foreach (var x in decls) if (!byFull.ContainsKey(x.d.full)) byFull[x.d.full] = x;
            foreach (var kv in Publishers(config, contractsModule).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!byFull.TryGetValue(kv.Key, out var x)) continue;   // not a contracts type (e.g. a module's own event)
                var owner = FileModule(x.file);
                foreach (var m in kv.Value.Where(m => m != owner))
                {
                    var also = kv.Value.Where(o => o != m).ToList();
                    issues.Add(new HarnessLint.Issue
                    {
                        rule = "contract-file",
                        module = m,
                        file = x.file,
                        message = also.Count > 0
                            ? $"{x.d.full} is published by modules {m} and {string.Join(", ", also)}: an event has one publishing module. Declare {m}'s own event in {config.contracts}/{m}Events.cs."
                            : $"{x.d.full} is published by module {m} but declared in {Path.GetFileName(x.file)}: declare it in {config.contracts}/{m}Events.cs (one file per publishing module).",
                    });
                }
            }
        }

        /// <summary>Contract types (full name) → the modules whose compiled code publishes them (EventBus.Publish&lt;T&gt; in the IL).</summary>
        static Dictionary<string, SortedSet<string>> Publishers(HarnessConfig config, string contractsModule)
        {
            // Runtime assemblies of modules under a module root (the rules are theirs, like module-boundary): assembly name → module.
            // Builders never publish (they run in the Editor); the Player list is also the one static-reset already asked for.
            var asmModule = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var a in HarnessPaths.Assemblies(AssembliesType.Player))
            {
                var src = a.sourceFiles.FirstOrDefault();
                if (src == null || a.name.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) continue;
                var module = config.ModuleOf(src.Replace('\\', '/'), out _, out var fromRoot);
                if (fromRoot && module.Length > 0 && module != contractsModule) asmModule[a.name] = module;
            }
            var result = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asmModule.TryGetValue(asm.GetName().Name, out var module)) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                {
                    const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                    IEnumerable<MethodBase> methods;
                    try { methods = t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)).ToList(); } catch { continue; }
                    foreach (var m in methods)
                        foreach (var e in PublishedBy(m))
                        {
                            var key = e.FullName;
                            if (key == null) continue;
                            if (!result.TryGetValue(key, out var set)) result[key] = set = new SortedSet<string>(StringComparer.Ordinal);
                            set.Add(module);
                        }
                }
            }
            return result;
        }

        static OpCode[] s_OneByte, s_TwoByte;

        /// <summary>The event types a method publishes: EventBus.Publish&lt;T&gt; calls (and method groups) with a closed T in its IL.</summary>
        static List<Type> PublishedBy(MethodBase method)
        {
            var found = new List<Type>();
            byte[] il;
            try { il = method.GetMethodBody()?.GetILAsByteArray(); } catch { return found; }
            if (il == null) return found;
            if (s_OneByte == null)
            {
                var one = new OpCode[256];
                var two = new OpCode[256];
                foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!(f.GetValue(null) is OpCode op)) continue;
                    var v = (ushort)op.Value;
                    if (op.Size == 1) one[v] = op;
                    else two[v & 0xFF] = op;
                }
                s_OneByte = one;
                s_TwoByte = two;
            }
            Type[] typeArgs = null, methodArgs = null;
            try
            {
                if (method.DeclaringType != null && method.DeclaringType.IsGenericType) typeArgs = method.DeclaringType.GetGenericArguments();
                if (method.IsGenericMethod) methodArgs = method.GetGenericArguments();
            }
            catch { }
            for (var i = 0; i < il.Length;)
            {
                OpCode op;
                if (il[i] == 0xFE && i + 1 < il.Length) { op = s_TwoByte[il[i + 1]]; i += 2; }
                else { op = s_OneByte[il[i]]; i += 1; }
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: i += 1; break;
                    case OperandType.InlineVar: i += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: i += 8; break;
                    case OperandType.InlineSwitch:
                        if (i + 4 > il.Length) return found;
                        i += 4 + 4 * BitConverter.ToInt32(il, i);
                        break;
                    case OperandType.InlineMethod:
                        if (i + 4 > il.Length) return found;
                        var token = BitConverter.ToInt32(il, i);
                        i += 4;
                        MethodBase target = null;
                        try { target = method.Module.ResolveMethod(token, typeArgs, methodArgs); } catch { }
                        if (target is MethodInfo mi && mi.IsGenericMethod && mi.Name == nameof(EventBus.Publish) && mi.DeclaringType == typeof(EventBus))
                        {
                            var t = mi.GetGenericArguments()[0];
                            if (!t.IsGenericParameter && !t.ContainsGenericParameters) found.Add(t);
                        }
                        break;
                    default: i += 4; break;
                }
            }
            return found;
        }
    }
}
