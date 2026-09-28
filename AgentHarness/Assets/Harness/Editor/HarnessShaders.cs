using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Harness.Editor
{
    public static class HarnessShaders
    {
        public sealed class ShaderError
        {
            public string shader;
            public string file;
            public int line;
            public string msg;
            public string platform;
            public string module;
        }

        /// <summary>
        /// Shader errors are state, not events: a broken .shader that was imported earlier stays broken but logs
        /// nothing on later loops. Query the current compile messages of every shader under Assets/ instead.
        /// </summary>
        [CliCommand("harness_shaders",
            "Current compile errors of every shader under Assets/ (ShaderUtil), as {ok, shaders, errors:[{shader,file,line,msg,platform,module}], warningCount}.",
            Tags = new[] { "harness", "materials/shaders" })]
        public static object Check()
        {
            var errors = new List<ShaderError>();
            var seen = new HashSet<string>();
            var warnings = 0;
            var guids = AssetDatabase.FindAssets("t:Shader", new[] { "Assets" });
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null) continue;
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                {
                    if (m.severity != ShaderCompilerMessageSeverity.Error) { warnings++; continue; }
                    var file = string.IsNullOrEmpty(m.file) ? path : HarnessLogParse.ToProjectPath(m.file);
                    var msg = (m.message ?? "").Split('\n')[0].Trim();
                    if (!seen.Add(file + "|" + m.line + "|" + msg)) continue; // same error on several platforms/variants
                    errors.Add(new ShaderError
                    {
                        shader = shader.name,
                        file = file,
                        line = m.line,
                        msg = msg,
                        platform = m.platform.ToString(),
                        module = HarnessLogParse.ModuleOf(file),
                    });
                }
            }
            return new { ok = errors.Count == 0, shaders = guids.Length, errors, warningCount = warnings };
        }
    }
}
