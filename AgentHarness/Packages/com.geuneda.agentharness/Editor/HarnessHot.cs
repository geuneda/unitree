using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Pipeline.CodeReload;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityPipeline.Microsoft.CodeAnalysis;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp;
using UnityPipeline.Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityPipeline.Microsoft.CodeAnalysis.Text;

namespace Harness.Editor
{
    /// <summary>
    /// Hot loop (loop.ps1 -Hot, G2-1): when the only edits since the last compile are bodies of [CodeReload] methods, apply them
    /// in place (the Pipeline's interpreter backend: no compile, no domain reload) and play the scenario as usual.
    ///
    /// What was compiled: every full loop snapshots the project's source files right before it compiles ("prepare": size, time,
    /// and the text of each .cs file that has [CodeReload]) and keeps the snapshot when the compile succeeded ("commit"). A hot
    /// loop compares the files with it: a .cs file whose tokens differ only inside [CodeReload] method bodies is reloadable; any
    /// other change (fields, signatures, another method, a new or deleted file, a shader, a scene, an asmdef) needs the full loop.
    /// </summary>
    [InitializeOnLoad]
    public static class HarnessHot
    {
        /// <summary>
        /// A project with [CodeReload] methods: compile a few lines with the Pipeline's Roslyn on a worker thread after every domain
        /// reload, so the first hot loop does not pay for loading and JIT-compiling the compiler (reload 1.46 s cold, 0.6 s after it).
        /// Plain .NET, no Unity API, nothing of this assembly's own statics: safe off the main thread.
        /// </summary>
        static HarnessHot()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess() || Application.isBatchMode) return;
            if (TypeCache.GetMethodsWithAttribute<CodeReloadAttribute>().Count == 0) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var tree = CSharpSyntaxTree.ParseText("using UnityEngine; static class HarnessWarmup { static Vector3 F(Transform t, float x) { var s = Vector3.zero; for (var i = 0; i < 3; i++) s += t.position * (x * i) + Vector3.up * Mathf.Sin(x); return s; } }");
                    var refs = new[] { typeof(object), typeof(Vector3), typeof(GameRoot) }
                        .Select(t => t.Assembly.Location).Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "netstandard").Select(a => a.Location))
                        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
                    var compilation = CSharpCompilation.Create("HarnessWarmup", new[] { tree }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                    using (var ms = new MemoryStream()) compilation.Emit(ms);
                }
                catch { /* only a warm-up */ }
            });
        }

        [Serializable]
        sealed class FileState
        {
            public string path;
            public long size;
            public long time;
            public bool reloadable;   // has [CodeReload]: text holds what was compiled
            public string text;
        }

        [Serializable]
        sealed class Snapshot
        {
            public long session;
            public int domainReloads;
            public string takenAt;
            public List<FileState> files = new List<FileState>();
        }

        const string Tag = "CodeReload";
        static string Dir => HarnessPaths.Combine(HarnessPaths.StateDir, "hot");
        static string CompiledFile => HarnessPaths.Combine(Dir, "compiled.json");
        static string PendingFile => HarnessPaths.Combine(Dir, "pending.json");

        [CliCommand("harness_hot",
            "Hot loop support (loop.ps1 -Hot). mode=apply (default): compare the project's files with the last compiled snapshot; when only " +
            "[CodeReload] method bodies changed, clear earlier overrides and reload those files through the Pipeline interpreter (no compile, " +
            "no domain reload) -> {hot:true, applied}; otherwise {hot:false, reason, changes}. mode=check: the same without applying. " +
            "mode=prepare (before a full loop compiles: clears overrides, snapshots the sources) / commit (after it compiled).",
            Tags = new[] { "harness", "scripts/codereload" })]
        public static object Hot([CliArg("mode", "apply | check | prepare | commit")] string mode = "apply")
        {
            var sw = Stopwatch.StartNew();
            mode = (mode ?? "apply").Trim().ToLowerInvariant();
            switch (mode)
            {
                case "prepare":
                {
                    var cleared = ClearOverrides();
                    var snap = Take(Read(PendingFile) ?? Read(CompiledFile));
                    Write(PendingFile, snap);
                    return new { ok = true, mode = "prepare", files = snap.files.Count, reloadable = snap.files.Count(f => f.reloadable), overridesCleared = cleared, ms = Ms(sw) };
                }
                case "commit":
                {
                    var pending = Read(PendingFile);
                    if (pending == null) return new { ok = false, mode = "commit", error = "no pending snapshot (harness_hot prepare runs before the compile)" };
                    Commit(pending);
                    Write(CompiledFile, pending);
                    try { File.Delete(PendingFile); } catch { }
                    return new { ok = true, mode = "commit", files = pending.files.Count, reloadable = pending.files.Count(f => f.reloadable), ms = Ms(sw) };
                }
                case "check":
                case "apply":
                    return Apply(mode == "apply", sw);
                default:
                    return new { ok = false, error = $"unknown mode '{mode}' (apply | check | prepare | commit)" };
            }
        }

        static double Ms(Stopwatch sw) => Math.Round(sw.Elapsed.TotalMilliseconds, 1);

        // ---- Classify and apply ---------------------------------------------------------------------------------------

        static object Apply(bool apply, Stopwatch sw)
        {
            object NotHot(string reason, List<object> changes = null) => new { ok = true, hot = false, reason, changes = changes ?? new List<object>(), ms = Ms(sw) };

            if (EditorApplication.isPlayingOrWillChangePlaymode) return new { ok = false, error = "Editor is in play mode; stop it first." };
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return NotHot("the Editor is compiling or importing");
            if (!HarnessSetup.DomainReloadDisabled())
                return NotHot("Domain Reload on entering Play Mode is on: reloaded methods would not survive entering play mode (harness_setup {\"apply\":\"domainReload\"})");
            var compiled = Read(CompiledFile);
            if (compiled == null) return NotHot("no compiled snapshot yet: run the full loop once");
            if (compiled.session != EditorAnalyticsSessionInfo.id)
                return NotHot("the Editor restarted since the last full loop: run the full loop once");
            if (compiled.domainReloads != HarnessConsole.DomainReloads)
                return NotHot("scripts were reloaded outside the loop since the last full loop (a compile in the Editor, a settings step): run the full loop once");

            // What changed since the compile.
            var now = Scan();
            var before = compiled.files.ToDictionary(f => f.path, StringComparer.Ordinal);
            var changes = new List<object>();
            var reload = new List<(string path, List<string> methods)>();
            var blocking = new List<string>();
            foreach (var f in now.Values.OrderBy(f => f.path, StringComparer.Ordinal))
            {
                if (!before.TryGetValue(f.path, out var old)) { blocking.Add("added " + f.path); changes.Add(new { file = f.path, kind = "added" }); continue; }
                if (old.size == f.size && old.time == f.time) continue;
                if (!old.reloadable)
                {
                    blocking.Add("changed " + f.path + (f.path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? " (no [CodeReload] method when it was compiled)" : ""));
                    changes.Add(new { file = f.path, kind = "changed" });
                    continue;
                }
                var text = ReadText(f.path);
                if (text == old.text) continue;
                var diff = Diff(f.path, old.text, text);
                if (diff.reason != null)
                {
                    blocking.Add(f.path + ": " + diff.reason);
                    changes.Add(new { file = f.path, kind = "context", line = diff.line, reason = diff.reason });
                    continue;
                }
                if (diff.methods.Count == 0) continue;   // whitespace or comments
                reload.Add((f.path, diff.methods));
                changes.Add(new { file = f.path, kind = "body", methods = diff.methods });
            }
            foreach (var old in compiled.files)
                if (!now.ContainsKey(old.path)) { blocking.Add("deleted " + old.path); changes.Add(new { file = old.path, kind = "deleted" }); }

            if (blocking.Count > 0)
                return NotHot(blocking.Count == 1 ? blocking[0] : blocking[0] + $" (+{blocking.Count - 1} more, see changes)", changes);
            if (!apply) return new { ok = true, hot = true, changes, ms = Ms(sw) };

            // Earlier hot loops' overrides first: a method edited back to what was compiled must run compiled again.
            var cleared = ClearOverrides();
            var applied = new List<object>();
            foreach (var (path, methods) in reload)
            {
                var r = Reload(path);
                if (!r.ok || methods.Any(m => !r.registered.Contains(m)))
                {
                    ClearOverrides();
                    var missing = methods.Where(m => !r.registered.Contains(m)).ToArray();
                    // The error details already list the diagnostics.
                    var why = r.error != null ? r.error.Replace("\n- ", " - ").Replace("\n", " ")
                        : "not applied: " + string.Join(", ", missing) + (r.diagnostics.Count > 0 ? " - " + string.Join("; ", r.diagnostics.Take(5)) : "");
                    return new { ok = true, hot = false, reason = path + ": the Pipeline interpreter could not reload it: " + why, changes, overridesCleared = cleared, ms = Ms(sw) };
                }
                applied.Add(new { file = path, methods, reloadMs = r.ms, diagnostics = r.diagnostics });
            }
            return new { ok = true, hot = true, changes, applied, overridesCleared = cleared, ms = Ms(sw) };
        }

        /// <summary>
        /// Tokens of <paramref name="after"/> against <paramref name="before"/> (trivia - whitespace, comments, inactive #if code - left out):
        /// the [CodeReload] methods whose bodies differ, or why the change is not a body-only one (and its line).
        /// </summary>
        static (List<string> methods, string reason, int line) Diff(string path, string before, string after)
        {
            var options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular, Defines(path));
            var a = CSharpSyntaxTree.ParseText(before, options, path);
            var b = CSharpSyntaxTree.ParseText(after, options, path);
            var error = b.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (error != null) return (null, "syntax error: " + error.GetMessage(), error.Location.GetLineSpan().StartLinePosition.Line + 1);

            var ma = Tagged(a.GetRoot());
            var mb = Tagged(b.GetRoot());
            var ca = Context(a.GetRoot(), ma.Values.Select(m => m.body.FullSpan));
            var cb = Context(b.GetRoot(), mb.Values.Select(m => m.body.FullSpan));
            var n = Math.Min(ca.Count, cb.Count);
            for (var i = 0; i <= n; i++)
            {
                if (i < n && ca[i].ToString() == cb[i].ToString()) continue;
                if (i == n && ca.Count == cb.Count) break;
                var at = i < cb.Count ? cb[i] : cb.Count > 0 ? cb[cb.Count - 1] : default;
                var line = at.RawKind == 0 ? 0 : at.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                return (null, $"changed outside [{Tag}] method bodies (line {line}): needs a compile", line);
            }
            var methods = new List<string>();
            foreach (var kv in mb)
                if (!ma.TryGetValue(kv.Key, out var old) || Tokens(old.body) != Tokens(kv.Value.body)) methods.Add(kv.Key);
            return (methods, null, 0);
        }

        /// <summary>[CodeReload] methods by the Pipeline's id ("Type.Method") with their body (block or expression body).</summary>
        static Dictionary<string, (MethodDeclarationSyntax method, SyntaxNode body)> Tagged(SyntaxNode root)
        {
            var result = new Dictionary<string, (MethodDeclarationSyntax, SyntaxNode)>(StringComparer.Ordinal);
            foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                SyntaxNode body = m.Body;
                if (body == null) body = m.ExpressionBody;
                if (body == null || !m.AttributeLists.SelectMany(l => l.Attributes).Any(IsTag)) continue;
                if (!(m.Parent is TypeDeclarationSyntax type)) continue;
                result[type.Identifier.Text + "." + m.Identifier.Text] = (m, body);
            }
            return result;
        }

        static bool IsTag(AttributeSyntax a)
        {
            var name = a.Name is QualifiedNameSyntax q ? q.Right.Identifier.Text : a.Name is SimpleNameSyntax s ? s.Identifier.Text : a.Name.ToString();
            return name == Tag || name == Tag + "Attribute";
        }

        static List<SyntaxToken> Context(SyntaxNode root, IEnumerable<TextSpan> bodies)
        {
            var skip = bodies.OrderBy(s => s.Start).ToList();
            var list = new List<SyntaxToken>();
            var k = 0;
            foreach (var t in root.DescendantTokens())
            {
                while (k < skip.Count && skip[k].End <= t.SpanStart) k++;
                if (k < skip.Count && skip[k].Contains(t.Span)) continue;
                list.Add(t);
            }
            return list;
        }

        static string Tokens(SyntaxNode node)
        {
            var sb = new StringBuilder();
            foreach (var t in node.DescendantTokens()) sb.Append(t.ToString()).Append('\u0001');
            return sb.ToString();
        }

        /// <summary>The scripting defines of the assembly that compiles <paramref name="path"/> (so #if regions read as compiled).</summary>
        static string[] Defines(string path)
        {
            foreach (var asm in HarnessPaths.Assemblies(AssembliesType.Editor))
                foreach (var f in asm.sourceFiles)
                    if (string.Equals(f.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase)) return asm.defines;
            return Array.Empty<string>();
        }

        // ---- Pipeline --------------------------------------------------------------------------------------------------

        static int ClearOverrides()
        {
            var n = CodeReloadRegistry.GetStats().ActiveOverrideCount;
            if (n > 0) CodeReloadRegistry.ClearAllOverrides();
            return n;
        }

        /// <summary>
        /// reload_file_editor_interpreter on one file (the interpreter reaches private members; the Assembly.Load backend of
        /// reload_file allows public members only, and module code keeps its state in private fields).
        /// </summary>
        static (bool ok, List<string> registered, List<string> diagnostics, string error, double ms) Reload(string path)
        {
            var sw = Stopwatch.StartNew();
            var cmd = CommandRegistry.DiscoverCommands().FirstOrDefault(c => c.Name == "reload_file_editor_interpreter");
            if (cmd == null) return (false, new List<string>(), new List<string>(), "the Pipeline has no reload_file_editor_interpreter command", 0);
            var args = cmd.Method.GetParameters().Select(p => p.Name == "filename" ? path : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            object r;
            try { r = cmd.Method.Invoke(cmd.Target, args); }
            catch (Exception e) { return (false, new List<string>(), new List<string>(), e.GetBaseException().Message, Ms(sw)); }
            var ok = Get(r, "Success") is bool b && b;
            var error = ok ? null : $"{Get(r, "Error")}: {Get(r, "ErrorDetails")}";
            return (ok, Strings(Get(r, "Items")), Strings(Get(r, "Diagnostics")), error, Ms(sw));
        }

        static object Get(object o, string property) => o?.GetType().GetProperty(property)?.GetValue(o);

        static List<string> Strings(object o) => o is IEnumerable e ? e.Cast<object>().Select(x => x?.ToString()).Where(x => x != null).ToList() : new List<string>();

        // ---- Snapshots ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The project's source files: Assets/ (less the generated assets and the build scene: every build rewrites them), ProjectSettings/
        /// and Packages/ (manifest, embedded packages). Folders Unity does not import (hidden, "~") are left out.
        /// </summary>
        static Dictionary<string, FileState> Scan()
        {
            var config = HarnessPaths.Config;
            var skip = new[] { config.generatedRoot, config.buildScene }.Where(s => !string.IsNullOrEmpty(s)).ToArray();
            var files = new Dictionary<string, FileState>(StringComparer.Ordinal);
            foreach (var root in new[] { "Assets", "ProjectSettings", "Packages" })
            {
                var dir = new DirectoryInfo(HarnessPaths.Combine(HarnessPaths.ProjectRoot, root));
                if (dir.Exists) Walk(dir, root, skip, files);
            }
            return files;
        }

        static void Walk(DirectoryInfo dir, string rel, string[] skip, Dictionary<string, FileState> files)
        {
            foreach (var f in dir.EnumerateFiles())
            {
                if (f.Name.StartsWith(".", StringComparison.Ordinal)) continue;
                var path = rel + "/" + f.Name;
                if (Skipped(path, skip)) continue;
                files[path] = new FileState { path = path, size = f.Length, time = f.LastWriteTimeUtc.Ticks };
            }
            foreach (var d in dir.EnumerateDirectories())
            {
                if (d.Name.StartsWith(".", StringComparison.Ordinal) || d.Name.EndsWith("~", StringComparison.Ordinal)) continue;
                var path = rel + "/" + d.Name;
                if (Skipped(path, skip)) continue;
                Walk(d, path, skip, files);
            }
        }

        static bool Skipped(string path, string[] skip)
        {
            foreach (var s in skip)
                if (path == s || path == s + ".meta" || path.StartsWith(s + "/", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>The files now, with the text of each .cs file that has [CodeReload] (unchanged files reuse <paramref name="last"/>).</summary>
        static Snapshot Take(Snapshot last)
        {
            var known = last?.files.ToDictionary(f => f.path, StringComparer.Ordinal) ?? new Dictionary<string, FileState>();
            var snap = new Snapshot { takenAt = HarnessPaths.UtcNow() };
            foreach (var f in Scan().Values.OrderBy(f => f.path, StringComparer.Ordinal))
            {
                if (f.path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    if (known.TryGetValue(f.path, out var k) && k.size == f.size && k.time == f.time) { f.reloadable = k.reloadable; f.text = k.text; }
                    else
                    {
                        var text = ReadText(f.path);
                        if (text != null && text.Contains(Tag)) { f.reloadable = true; f.text = text; }
                    }
                }
                snap.files.Add(f);
            }
            return snap;
        }

        /// <summary>
        /// After a successful compile: the loaded code is the prepared sources. Files that appeared meanwhile (the .meta of a new file,
        /// written by the import) and other non-script files the import rewrote take their current state; a script that changed meanwhile
        /// keeps its prepared state, so the next hot loop sees it as changed.
        /// </summary>
        static void Commit(Snapshot pending)
        {
            var now = Scan();
            var have = new HashSet<string>(pending.files.Select(f => f.path), StringComparer.Ordinal);
            foreach (var f in pending.files)
                if (!f.path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && now.TryGetValue(f.path, out var cur)) { f.size = cur.size; f.time = cur.time; }
            foreach (var cur in now.Values)
                if (!have.Contains(cur.path) && !cur.path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) pending.files.Add(cur);
            pending.session = EditorAnalyticsSessionInfo.id;
            pending.domainReloads = HarnessConsole.DomainReloads;
        }

        static string ReadText(string path)
        {
            try { return File.ReadAllText(HarnessPaths.Combine(HarnessPaths.ProjectRoot, path)).Replace("\r\n", "\n"); }
            catch { return null; }
        }

        static Snapshot Read(string file)
        {
            try { return File.Exists(file) ? JsonUtility.FromJson<Snapshot>(File.ReadAllText(file)) : null; }
            catch { return null; }
        }

        static void Write(string file, Snapshot s)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(file, JsonUtility.ToJson(s));
        }
    }
}
