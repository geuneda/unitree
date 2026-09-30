#if AGENTHARNESS_URP
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Harness.Editor
{
    public sealed partial class BuildContext
    {
        const string UrpDecalShader = "Shader Graphs/Decal";
        readonly List<(string module, string path)> m_Decals = new List<(string, string)>();

        /// <summary>
        /// A material for URP decals (URP's Decal shader graph): <paramref name="baseMap"/>'s color paints the surfaces under the
        /// projector, its alpha is the coverage; <paramref name="normalMap"/> (imported as a normal map) bends their normals by
        /// <paramref name="normalBlend"/>.
        /// </summary>
        public Material DecalMaterial(string name, Texture baseMap, Texture normalMap = null, float normalBlend = 0.5f)
        {
            var shader = Shader.Find(UrpDecalShader);
            if (shader == null) throw new InvalidOperationException($"DecalMaterial needs URP (shader '{UrpDecalShader}' not found)");
            if (normalMap != null) CheckTexture(name, "normalMap", normalMap, normalMap: true);
            return Material(name, shader, m =>
            {
                m.SetTexture("Base_Map", baseMap);
                if (normalMap != null) m.SetTexture("Normal_Map", normalMap);
                m.SetFloat("Normal_Blend", normalBlend);
                m.enableInstancing = true;   // URP draws decals instanced
            });
        }

        /// <summary>
        /// A URP DecalProjector at <paramref name="path"/> projecting <paramref name="material"/> onto whatever is inside its box:
        /// <paramref name="size"/> = (width, height, depth) with the depth along the projection. By default it points down (onto the
        /// ground below the object). Needs a DecalRendererFeature on the pipeline's renderer
        /// (<c>SettingsContext.AddRendererFeature&lt;DecalRendererFeature&gt;</c>) - checked after the build.
        /// </summary>
        public DecalProjector Decal(string path, Material material, Vector3 size, bool pointDown = true)
        {
            var go = Create(path, typeof(DecalProjector));
            if (pointDown) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var decal = go.GetComponent<DecalProjector>();
            decal.material = material;
            decal.size = size;
            decal.pivot = new Vector3(0f, 0f, size.z * 0.5f);   // the box starts at the object and reaches depth along the projection
            decal.scaleMode = DecalScaleMode.InheritFromHierarchy;
            m_Decals.Add((Module, path));
            return decal;
        }

        /// <summary>After the build: decals are drawn only by a renderer with a DecalRendererFeature.</summary>
        void CheckDecals()
        {
            if (m_Decals.Count == 0) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset rp)) return;
            using (var so = new SerializedObject(rp))
            {
                var list = so.FindProperty("m_RendererDataList");
                var index = so.FindProperty("m_DefaultRendererIndex")?.intValue ?? 0;
                var data = list != null && index >= 0 && index < list.arraySize ? list.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData : null;
                if (data == null) return;
                foreach (var f in data.rendererFeatures)
                    if (f is DecalRendererFeature && f.isActive) return;
                foreach (var (module, path) in m_Decals)
                    Warnings.Add($"[{module}] decal '{path}': the renderer '{data.name}' has no active DecalRendererFeature - the decal is not drawn (SettingsContext.AddRendererFeature<DecalRendererFeature>)");
            }
        }
    }
}
#endif
