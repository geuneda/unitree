using System;
using System.Text.RegularExpressions;

namespace Harness
{
    [Serializable]
    public sealed class LogEntry
    {
        public int seq;
        public string type;     // Error | Exception | Assert | Warning | Log
        public string message;
        public string stack;    // first few frames only
        public string file;     // Assets/... (or Packages/...) of the first project frame, if any
        public int line;
        public string module;   // module of that file (ProjectSettings/AgentHarness.json); "Harness" for the harness package; else ""
        public int count = 1;   // identical consecutive/duplicate entries are folded
        public float t;         // seconds (scenario clock when recorded by the play runner)
    }

    /// <summary>Maps log stack traces / compiler paths to project files and modules.</summary>
    public static class HarnessLogParse
    {
        // "(at Assets/X.cs:12)", "(at ./Packages/com.x/Y.cs:3)", "(at ./Library/PackageCache/com.x@1a2b/Y.cs:3)", and in a Player
        // the project's absolute paths "(at C:/Proj/Assets/X.cs:12)" (made project-relative by ToProjectPath)
        static readonly Regex s_AtFrame = new Regex(@"\(at (?<file>(?:[A-Za-z]:)?(?:[^:\)\n]*[\\/])?(?:Assets|Packages|Library[\\/]PackageCache)[\\/][^:\)\n]+):(?<line>\d+)\)", RegexOptions.Compiled);
        static readonly Regex s_ExceptionFrame = new Regex(@"in (?<file>[A-Za-z]:[\\/][^:]+|/[^:]+):(?<line>\d+)", RegexOptions.Compiled);

        /// <summary>Module of a file (see <see cref="HarnessConfig.ModuleOf(string)"/>); absolute paths are made project-relative first.</summary>
        public static string ModuleOf(string path) => HarnessConfig.Current.ModuleOf(ToProjectPath(path));

        /// <summary>
        /// Normalize an absolute or relative path to "Assets/..." / "Packages/&lt;name&gt;/..." when it lies inside the project.
        /// Package cache paths (Library/PackageCache/&lt;name&gt;@&lt;hash&gt;/...) become Packages/&lt;name&gt;/....
        /// </summary>
        public static string ToProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var p = path.Replace('\\', '/');
            var root = HarnessConfig.ProjectRoot;
            if (root.Length > 0 && p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) p = p.Substring(root.Length + 1);
            if (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            var cache = p.IndexOf("Library/PackageCache/", StringComparison.Ordinal);
            if (cache >= 0)
            {
                var rest = p.Substring(cache + "Library/PackageCache/".Length);
                var slash = rest.IndexOf('/');
                var head = slash >= 0 ? rest.Substring(0, slash) : rest;
                var at = head.IndexOf('@');
                if (at > 0) head = head.Substring(0, at);
                return "Packages/" + head + (slash >= 0 ? rest.Substring(slash) : "");
            }
            if (p.StartsWith("Assets/", StringComparison.Ordinal) || p.StartsWith("Packages/", StringComparison.Ordinal)) return p;
            var i = p.IndexOf("/Assets/", StringComparison.Ordinal);
            if (i >= 0) return p.Substring(i + 1);
            var j = p.IndexOf("/Packages/", StringComparison.Ordinal);
            return j >= 0 ? p.Substring(j + 1) : p;
        }

        // Which frame names the error: module code first, then other project code, the harness, then packages.
        static int Rank(string file)
        {
            var cfg = HarnessConfig.Current;
            var module = cfg.ModuleOf(file);
            if (module.Length > 0 && module != "Harness") return 0;
            if (file.StartsWith("Assets/", StringComparison.Ordinal)) return 1;
            if (module == "Harness") return 3;
            return cfg.IsProjectPath(file) ? 2 : 4;
        }

        public static void FillLocation(LogEntry e)
        {
            if (string.IsNullOrEmpty(e.stack)) return;
            string best = null; int bestLine = 0; int bestRank = int.MaxValue;
            foreach (Match m in s_AtFrame.Matches(e.stack))
            {
                var f = ToProjectPath(m.Groups["file"].Value);
                var rank = Rank(f);
                if (rank < bestRank) { bestRank = rank; best = f; int.TryParse(m.Groups["line"].Value, out bestLine); }
                if (rank == 0) break;
            }
            if (best == null)
            {
                var m = s_ExceptionFrame.Match(e.stack);
                if (m.Success) { best = ToProjectPath(m.Groups["file"].Value); int.TryParse(m.Groups["line"].Value, out bestLine); }
            }
            e.file = best ?? "";
            e.line = bestLine;
            e.module = ModuleOf(e.file);
        }

        public static string TrimStack(string stack, int maxLines = 8)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            var lines = stack.Split('\n');
            if (lines.Length <= maxLines) return stack.TrimEnd();
            return string.Join("\n", lines, 0, maxLines).TrimEnd() + "\n...";
        }
    }
}
