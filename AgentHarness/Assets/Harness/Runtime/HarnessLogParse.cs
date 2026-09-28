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
        public string file;     // Assets/... of the first project frame, if any
        public int line;
        public string module;   // Assets/Game/<Module>/... → <Module>; "Harness" for Assets/Harness; else ""
        public int count = 1;   // identical consecutive/duplicate entries are folded
        public float t;         // seconds (scenario clock when recorded by the play runner)
    }

    /// <summary>Maps log stack traces / compiler paths to project files and modules.</summary>
    public static class HarnessLogParse
    {
        static readonly Regex s_AtFrame = new Regex(@"\(at (?<file>(?:Assets|Packages)/[^:\)]+):(?<line>\d+)\)", RegexOptions.Compiled);
        static readonly Regex s_ExceptionFrame = new Regex(@"in (?<file>[A-Za-z]:[\\/][^:]+|/[^:]+):(?<line>\d+)", RegexOptions.Compiled);

        public static string ModuleOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "";
            var p = assetPath.Replace('\\', '/');
            var i = p.IndexOf("Assets/Game/", StringComparison.Ordinal);
            if (i >= 0)
            {
                var rest = p.Substring(i + "Assets/Game/".Length);
                var slash = rest.IndexOf('/');
                return slash > 0 ? rest.Substring(0, slash) : rest;
            }
            if (p.Contains("Assets/Harness/")) return "Harness";
            return "";
        }

        /// <summary>Normalize an absolute or relative path to "Assets/..." when it lies inside the project.</summary>
        public static string ToProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var p = path.Replace('\\', '/');
            var i = p.IndexOf("/Assets/", StringComparison.Ordinal);
            if (p.StartsWith("Assets/", StringComparison.Ordinal)) return p;
            return i >= 0 ? p.Substring(i + 1) : p;
        }

        public static void FillLocation(LogEntry e)
        {
            if (string.IsNullOrEmpty(e.stack)) return;
            // Prefer the first frame inside Assets/Game, then Assets/, then Packages/.
            string best = null; int bestLine = 0; int bestRank = int.MaxValue;
            foreach (Match m in s_AtFrame.Matches(e.stack))
            {
                var f = m.Groups["file"].Value;
                var rank = f.StartsWith("Assets/Game/", StringComparison.Ordinal) ? 0 : f.StartsWith("Assets/", StringComparison.Ordinal) ? 1 : 2;
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
