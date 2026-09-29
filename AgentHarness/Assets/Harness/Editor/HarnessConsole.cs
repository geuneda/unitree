using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Harness.Editor
{
    [Serializable]
    public sealed class CompileMessage
    {
        public string file;
        public int line;
        public int column;
        public string code;
        public string msg;
        public string module;
        public string assembly;
        public string type; // Error | Warning
    }

    [Serializable]
    sealed class CompileState
    {
        public int generation;     // +1 per compilation started (persisted in the file across reloads)
        public string startedAt;
        public string finishedAt;
        public bool finished;
        public List<CompileMessage> messages = new List<CompileMessage>();
    }

    [Serializable]
    sealed class LogLine
    {
        public int seq;
        public string type;
        public string message;
        public string stack;
    }

    /// <summary>
    /// Persistent console capture: every log line (with a monotonically increasing sequence number) goes to
    /// Library/Harness/console.ndjson, compiler messages of the latest compile to Library/Harness/compile.json.
    /// Both survive domain reloads, so "errors since mark" works across recompiles and play sessions.
    /// </summary>
    [InitializeOnLoad]
    public static class HarnessConsole
    {
        static readonly object s_Lock = new object();
        static int s_Seq;
        static CompileState s_Compile;
        const long MaxLogBytes = 8L * 1024 * 1024;
        static readonly Regex s_CompilerPrefix = new Regex(@"^(?<file>.*?)\((?<line>\d+),(?<col>\d+)\):\s*(?<kind>error|warning)\s+(?<code>\w+):\s*(?<msg>.*)$", RegexOptions.Singleline);

        public static int DomainReloads { get; private set; }

        static HarnessConsole()
        {
            // Asset import worker processes load the Editor assemblies too, each with its own (empty) SessionState. There
            // this looked like a new session: it deleted the log and wrote its own lines from seq 1 into the same file, so a
            // loop's "since <mark>" missed runtime errors logged afterwards (seen as a green loop with an exception thrown).
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            if (!SessionState.GetBool("Harness.ConsoleSession", false))
            {
                // New Editor session: start a fresh log.
                SessionState.SetBool("Harness.ConsoleSession", true);
                try { File.Delete(HarnessPaths.ConsoleFile); } catch { }
            }
            // The sequence must never go back within a session, even if the file cannot be read right now.
            s_Seq = Math.Max(ReadLastSeq(), SessionState.GetInt("Harness.ConsoleSeq", 0));
            AssemblyReloadEvents.beforeAssemblyReload += () => SessionState.SetInt("Harness.ConsoleSeq", Mark);
            DomainReloads = SessionState.GetInt("Harness.DomainReloads", 0) + 1;
            SessionState.SetInt("Harness.DomainReloads", DomainReloads);

            Application.logMessageReceivedThreaded += OnLog;
            CompilationPipeline.compilationStarted += _ =>
            {
                var gen = ReadCompile().generation + 1;
                lock (s_Lock) s_Compile = new CompileState { generation = gen, startedAt = HarnessPaths.UtcNow() };
                WriteCompile();
            };
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += _ =>
            {
                lock (s_Lock)
                {
                    if (s_Compile == null) s_Compile = new CompileState();
                    s_Compile.finished = true;
                    s_Compile.finishedAt = HarnessPaths.UtcNow();
                }
                WriteCompile();
            };
        }

        public static int Mark { get { lock (s_Lock) return s_Seq; } }

        static void OnLog(string message, string stack, LogType type)
        {
            try
            {
                var line = new LogLine
                {
                    type = type.ToString(),
                    message = message != null && message.Length > 4000 ? message.Substring(0, 4000) + "..." : message,
                    stack = type == LogType.Log ? "" : HarnessLogParse.TrimStack(stack),
                };
                lock (s_Lock)
                {
                    line.seq = ++s_Seq;
                    var fi = new FileInfo(HarnessPaths.ConsoleFile);
                    if (fi.Exists && fi.Length > MaxLogBytes) Rotate();
                    var json = JsonUtility.ToJson(line) + "\n";
                    // Another process (virus scanner, indexer) may hold the file for a moment: retry rather than lose the line.
                    for (var attempt = 0; ; attempt++)
                    {
                        try { File.AppendAllText(HarnessPaths.ConsoleFile, json); break; }
                        catch (IOException) when (attempt < 5) { System.Threading.Thread.Sleep(10); }
                    }
                }
            }
            catch { /* never throw from a log callback */ }
        }

        static void Rotate()
        {
            var lines = File.ReadAllLines(HarnessPaths.ConsoleFile);
            var keep = new List<string>();
            for (var i = lines.Length / 2; i < lines.Length; i++) keep.Add(lines[i]);
            File.WriteAllLines(HarnessPaths.ConsoleFile, keep);
        }

        static int ReadLastSeq()
        {
            try
            {
                if (!File.Exists(HarnessPaths.ConsoleFile)) return 0;
                var lines = File.ReadAllLines(HarnessPaths.ConsoleFile);
                for (var i = lines.Length - 1; i >= 0; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    return JsonUtility.FromJson<LogLine>(lines[i]).seq;
                }
            }
            catch { }
            return 0;
        }

        static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            lock (s_Lock)
            {
                if (s_Compile == null) s_Compile = new CompileState { generation = ReadCompile().generation + 1, startedAt = HarnessPaths.UtcNow() };
                foreach (var m in messages ?? Array.Empty<CompilerMessage>())
                {
                    var file = HarnessLogParse.ToProjectPath(m.file);
                    var cm = new CompileMessage
                    {
                        assembly = Path.GetFileNameWithoutExtension(assemblyPath),
                        type = m.type == CompilerMessageType.Error ? "Error" : "Warning",
                        file = file,
                        line = m.line,
                        column = m.column,
                        module = HarnessLogParse.ModuleOf(file),
                        msg = m.message,
                    };
                    var match = s_CompilerPrefix.Match(m.message ?? "");
                    if (match.Success) { cm.code = match.Groups["code"].Value; cm.msg = match.Groups["msg"].Value.Trim(); }
                    s_Compile.messages.Add(cm);
                }
            }
            WriteCompile();
        }

        static void WriteCompile()
        {
            try
            {
                string json;
                lock (s_Lock) json = JsonUtility.ToJson(s_Compile ?? new CompileState(), true);
                File.WriteAllText(HarnessPaths.CompileFile, json);
            }
            catch { }
        }

        internal static CompileState ReadCompile()
        {
            try
            {
                if (File.Exists(HarnessPaths.CompileFile))
                    return JsonUtility.FromJson<CompileState>(File.ReadAllText(HarnessPaths.CompileFile)) ?? new CompileState();
            }
            catch { }
            return new CompileState();
        }

        internal static List<LogLine> ReadSince(int since)
        {
            var list = new List<LogLine>();
            string[] lines;
            lock (s_Lock)
            {
                if (!File.Exists(HarnessPaths.ConsoleFile)) return list;
                lines = File.ReadAllLines(HarnessPaths.ConsoleFile);
            }
            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                try
                {
                    var e = JsonUtility.FromJson<LogLine>(l);
                    if (e != null && e.seq > since) list.Add(e);
                }
                catch { }
            }
            return list;
        }

        [CliCommand("harness_console",
            "Compile errors of the latest compile (file, line, msg, module) plus runtime errors/exceptions and warning counts " +
            "logged after --since <mark>. Every reply carries 'mark' (latest sequence number) to pass next time.",
            MainThreadRequired = false, Tags = new[] { "harness", "observability/console" })]
        public static object Console(
            [CliArg("since", "Only log entries with sequence number > since (0 = whole Editor session).")] int since = 0,
            [CliArg("include_logs", "Also return plain Debug.Log lines (default: errors/warnings only).")] bool includeLogs = false,
            [CliArg("limit", "Max entries returned per list.")] int limit = 100,
            [CliArg("compile_only", "Skip log entries (cheap poll of compile state).")] bool compileOnly = false)
        {
            var compile = ReadCompile();
            var compileErrors = new List<CompileMessage>();
            var compileWarnings = 0;
            foreach (var m in compile.messages)
            {
                if (m.type == "Error") compileErrors.Add(m);
                else compileWarnings++;
            }
            if (compileOnly)
                return new { ok = true, mark = Mark, compileGeneration = compile.generation, compileFinished = compile.finished, compileStartedAt = compile.startedAt, compileErrors, compileWarningCount = compileWarnings };

            var entries = ReadSince(since);
            var runtimeErrors = new List<LogEntry>();
            var editorErrors = new List<LogEntry>();
            var warnings = new List<LogEntry>();
            var logs = new List<LogEntry>();
            int errorCount = 0, exceptionCount = 0, assertCount = 0, warningCount = 0, logCount = 0, infraCount = 0, shaderLogCount = 0;
            foreach (var e in entries)
            {
                // Tooling noise (Pipeline server housekeeping) is counted separately so it never fails a loop.
                if (e.message != null && e.message.StartsWith("[Pipeline]", StringComparison.Ordinal)) { infraCount++; continue; }
                // Shader compile errors are reported as state by harness_shaders (file/line/module), not as log events.
                if (e.message != null && (e.message.StartsWith("Shader error in", StringComparison.Ordinal) || e.message.StartsWith("Shader warning in", StringComparison.Ordinal))) { shaderLogCount++; continue; }
                switch (e.type)
                {
                    case "Error": errorCount++; break;
                    case "Exception": exceptionCount++; break;
                    case "Assert": assertCount++; break;
                    case "Warning": warningCount++; break;
                    default: logCount++; break;
                }
                var target = e.type == "Warning" ? warnings : e.type == "Log" ? (includeLogs ? logs : null) : IsEditorInternal(e) ? editorErrors : runtimeErrors;
                if (target == null) continue;
                // Compiler errors are also logged as console errors; they are already reported above.
                if (e.type == "Error" && s_CompilerPrefix.IsMatch(e.message ?? "")) continue;
                Fold(target, e, limit);
            }

            return new
            {
                ok = true,
                mark = Mark,
                since,
                compileGeneration = compile.generation,
                compileFinished = compile.finished,
                compileStartedAt = compile.startedAt,
                compileErrors,
                compileWarningCount = compileWarnings,
                runtimeErrors,
                editorErrors,
                warnings,
                logs = includeLogs ? logs : null,
                counts = new { error = errorCount, exception = exceptionCount, assert = assertCount, warning = warningCount, log = logCount, infra = infraCount, shaderLog = shaderLogCount },
            };
        }

        /// <summary>
        /// Errors raised inside the Editor/packages themselves, e.g. UnityEditor.Search indexing on startup or a Burst
        /// JIT cache DLL blocked by Windows application control. Reported as editorErrors; they do not fail a loop.
        /// Anything that mentions Assets/ (message or stack) or has no stack and no Burst origin stays a runtime error.
        /// </summary>
        static bool IsEditorInternal(LogLine e)
        {
            // Frames come as "(at Assets/X.cs:12)" or, when Unity does not shorten the path, as Mono's
            // "in C:\...\Assets\X.cs:12": a project file either way (a backslash path once made a runtime error pass as internal).
            var msg = (e.message ?? "").Replace('\\', '/');
            if (msg.Contains("Assets/")) return false;
            if (!string.IsNullOrEmpty(e.stack))
                return !e.stack.Replace('\\', '/').Contains("Assets/");
            return msg.StartsWith("Unexpected error in Burst compilation", StringComparison.Ordinal);
        }

        static void Fold(List<LogEntry> list, LogLine e, int limit)
        {
            foreach (var x in list)
                if (x.type == e.type && x.message == e.message) { x.count++; return; }
            if (list.Count >= limit) return;
            var entry = new LogEntry { seq = e.seq, type = e.type, message = e.message, stack = e.stack };
            HarnessLogParse.FillLocation(entry);
            list.Add(entry);
        }

        [CliCommand("harness_ping",
            "Cheap readiness probe: domain reload counter, compiling/updating/playing flags, console mark, Editor version.",
            Tags = new[] { "harness" })]
        public static object Ping()
        {
            return new
            {
                ok = true,
                domainReloads = DomainReloads,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isPlaying = EditorApplication.isPlaying,
                willChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                compileFailed = EditorUtility.scriptCompilationFailed,
                mark = Mark,
                activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                unityVersion = Application.unityVersion,
            };
        }
    }
}
