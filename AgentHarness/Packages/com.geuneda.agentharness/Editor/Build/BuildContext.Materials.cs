using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Harness.Editor
{
    /// <summary>Surface settings for <see cref="BuildContext.LitMaterial"/> (URP Lit, metallic workflow). Unset = the shader's default.</summary>
    public sealed class LitSettings
    {
        public Color BaseColor = Color.white;
        public Texture BaseMap;
        public Vector2 Tiling = Vector2.one;
        public Vector2 Offset = Vector2.zero;
        public float Metallic;
        public float Smoothness = 0.5f;
        /// <summary>Metallic in R, smoothness in A (times <see cref="Smoothness"/>). Import it linear (sRGB off).</summary>
        public Texture MetallicGlossMap;
        /// <summary>A texture imported as a normal map (<c>ctx.SaveTexture(tex, name, normalMap: true)</c>).</summary>
        public Texture NormalMap;
        public float NormalScale = 1f;
        public Texture OcclusionMap;
        public float OcclusionStrength = 1f;
        /// <summary>HDR emission color; anything but black turns emission on. <see cref="EmissionMap"/> multiplies it.</summary>
        public Color Emission = Color.black;
        public Texture EmissionMap;
        /// <summary>Alpha-blended in the transparent queue instead of opaque.</summary>
        public bool Transparent;
        /// <summary>Alpha clipping threshold (0..1); null = no clipping.</summary>
        public float? AlphaClip;
        public CullMode Cull = CullMode.Back;
        public bool ReceiveShadows = true;
    }

    public sealed partial class BuildContext
    {
        const string UrpLit = "Universal Render Pipeline/Lit";

        /// <summary>
        /// A URP Lit material from typed settings. Keywords, queue and blend state follow from them (normal map -> _NORMALMAP,
        /// Emission -> _EMISSION and the GI flag the Inspector's Emission toggle sets, Transparent, AlphaClip, ...), so nothing
        /// has to be enabled by hand and a misspelled property cannot silently do nothing.
        /// </summary>
        public Material LitMaterial(string name, Action<LitSettings> setup)
        {
            var s = new LitSettings();
            setup?.Invoke(s);
            var shader = Shader.Find(UrpLit);
            if (shader == null) throw new InvalidOperationException($"LitMaterial needs URP (shader '{UrpLit}' not found); use ctx.Material with the pipeline's shader");
            CheckTexture(name, "NormalMap", s.NormalMap, normalMap: true);
            CheckTexture(name, "MetallicGlossMap", s.MetallicGlossMap, normalMap: false);
            CheckTexture(name, "OcclusionMap", s.OcclusionMap, normalMap: false);
            return Material(name, shader, m =>
            {
                m.SetColor("_BaseColor", s.BaseColor);
                m.SetTexture("_BaseMap", s.BaseMap);
                m.SetTextureScale("_BaseMap", s.Tiling);
                m.SetTextureOffset("_BaseMap", s.Offset);
                m.SetFloat("_Metallic", s.Metallic);
                m.SetFloat("_Smoothness", s.Smoothness);
                m.SetTexture("_MetallicGlossMap", s.MetallicGlossMap);
                m.SetTexture("_BumpMap", s.NormalMap);
                m.SetFloat("_BumpScale", s.NormalScale);
                m.SetTexture("_OcclusionMap", s.OcclusionMap);
                m.SetFloat("_OcclusionStrength", s.OcclusionStrength);
                m.SetColor("_EmissionColor", s.Emission);
                m.SetTexture("_EmissionMap", s.EmissionMap);
                // URP's validation turns _EMISSION on only when the GI flags are not EmissiveIsBlack (the Emission toggle);
                // enabling the keyword by hand is undone.
                m.globalIlluminationFlags = s.Emission.maxColorComponent > 0f ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                m.SetFloat("_Surface", s.Transparent ? 1f : 0f);
                m.SetFloat("_AlphaClip", s.AlphaClip.HasValue ? 1f : 0f);
                if (s.AlphaClip.HasValue) m.SetFloat("_Cutoff", s.AlphaClip.Value);
                m.SetFloat("_Cull", (float)s.Cull);
                m.SetFloat("_ReceiveShadows", s.ReceiveShadows ? 1f : 0f);
            });
        }

        void CheckTexture(string material, string slot, Texture texture, bool normalMap)
        {
            if (texture == null || !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter imp)) return;
            if (normalMap && imp.textureType != TextureImporterType.NormalMap)
                Warn($"material '{material}': {slot} '{texture.name}' is not imported as a normal map (ctx.SaveTexture(..., normalMap: true)); its colors are read as normals");
            if (!normalMap && imp.sRGBTexture)
                Warn($"material '{material}': {slot} '{texture.name}' is imported as sRGB; mask data should be linear (ctx.SaveTexture(..., sRGB: false))");
        }

        // URP keeps these for upgrading Built-in materials; the Lit shaders never read them.
        static readonly Dictionary<string, string> s_ObsoleteUrp = new Dictionary<string, string>
        {
            { "_MainTex", "_BaseMap" }, { "_Color", "_BaseColor" }, { "_Glossiness", "_Smoothness" },
            { "_GlossMapScale", "_Smoothness" }, { "_GlossyReflections", "_EnvironmentReflections" },
        };

        /// <summary>
        /// Report what a material setup got wrong without an error: properties the shader does not declare (typos, another
        /// pipeline's names), values the shader's validation replaced (derived properties), and an emission color that stays off.
        /// </summary>
        void CheckMaterial(string name, Material m, Dictionary<string, string> defaults, Dictionary<string, string> set)
        {
            var shader = m.shader;
            var after = MaterialValues(m);
            var urp = shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);
            foreach (var kv in set)
            {
                if (defaults.TryGetValue(kv.Key, out var d) && d == kv.Value) continue;   // left at its default
                if (IsDerivedName(shader, kv.Key)) continue;                               // follows its texture
                if (shader.FindPropertyIndex(kv.Key) < 0)
                    Warn($"material '{name}': shader '{shader.name}' has no property '{kv.Key}'{SimilarProperty(shader, kv.Key)}");
                else if (urp && s_ObsoleteUrp.TryGetValue(kv.Key, out var instead))
                    Warn($"material '{name}': '{kv.Key}' is an obsolete URP property the shader never reads; set '{instead}'");
                else if (after.TryGetValue(kv.Key, out var a) && a != kv.Value)
                    Warn($"material '{name}': the shader's validation replaced '{kv.Key}' ({kv.Value} -> {a}); it is derived from other properties");
            }
            if (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0f
                && Array.IndexOf(shader.keywordSpace.keywordNames, "_EMISSION") >= 0 && !m.IsKeywordEnabled("_EMISSION"))
                Warn($"material '{name}': _EmissionColor is set but emission is off - the Emission toggle is m.globalIlluminationFlags (not EmissiveIsBlack), or use ctx.LitMaterial");
        }

        static string SimilarProperty(Shader shader, string wanted)
        {
            var key = wanted.TrimStart('_').ToLowerInvariant();
            var hits = new List<string>();
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                var n = shader.GetPropertyName(i);
                var k = n.TrimStart('_').ToLowerInvariant();
                if (k.Length >= 3 && key.Length >= 3 && (k.Contains(key) || key.Contains(k) || k.Substring(0, 3) == key.Substring(0, 3))) hits.Add(n);
            }
            return hits.Count == 0 ? "" : " (similar: " + string.Join(", ", hits) + ")";
        }

        /// <summary>
        /// Property values of a material by name, including names the shader does not declare (Material.SetFloat stores those
        /// too) and texture scale/offset (&lt;name&gt;_ST).
        /// </summary>
        static Dictionary<string, string> MaterialValues(Material m)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var n in m.GetPropertyNames(MaterialPropertyType.Float)) values[n] = m.GetFloat(n).ToString("R");
            foreach (var n in m.GetPropertyNames(MaterialPropertyType.Int)) values[n] = m.GetInteger(n).ToString();
            foreach (var n in m.GetPropertyNames(MaterialPropertyType.Vector)) values[n] = m.GetVector(n).ToString("R");
            foreach (var n in m.GetPropertyNames(MaterialPropertyType.Texture)) { var t = m.GetTexture(n); values[n] = t == null ? "null" : t.name; }
            return values;
        }

        /// <summary>A name the engine derives from a declared property (texture _ST/_TexelSize/_HDR) or provides itself (unity_*).</summary>
        static bool IsDerivedName(Shader shader, string name)
        {
            if (name.StartsWith("unity_", StringComparison.Ordinal)) return true;
            foreach (var suffix in new[] { "_ST", "_TexelSize", "_HDR" })
                if (name.EndsWith(suffix, StringComparison.Ordinal) && shader.FindPropertyIndex(name.Substring(0, name.Length - suffix.Length)) >= 0) return true;
            return false;
        }
    }
}
