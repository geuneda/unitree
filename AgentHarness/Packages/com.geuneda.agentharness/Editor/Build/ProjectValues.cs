using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Harness.Editor
{
    /// <summary>
    /// Settings in ProjectSettings/ that a settings step owns (<see cref="SettingsContext.Player"/>, <see cref="SettingsContext.Time"/>,
    /// <see cref="SettingsContext.QualityLevels"/>, ...). Only what is set is applied, on every build: null = the project's own
    /// value (the committed YAML). A field without a property: <c>Set</c> with its serialized name (as in the YAML).
    /// </summary>
    public abstract class ProjectValues
    {
        internal readonly List<KeyValuePair<string, object>> Extra = new List<KeyValuePair<string, object>>();

        private protected void Add(string propertyPath, object value) => Extra.Add(new KeyValuePair<string, object>(propertyPath, value));
    }

    /// <summary>Player settings (ProjectSettings/ProjectSettings.asset), through the PlayerSettings API.</summary>
    public sealed class PlayerValues : ProjectValues
    {
        /// <summary>Changing it reimports every texture.</summary>
        public ColorSpace? ColorSpace;
        public string CompanyName;
        public string ProductName;
        /// <summary>Standalone window size (tools/player.ps1 runs the Player at the capture size anyway).</summary>
        public int? DefaultScreenWidth;
        public int? DefaultScreenHeight;
        public FullScreenMode? FullScreenMode;
        public bool? ResizableWindow;
        /// <summary>Mobile screen orientation.</summary>
        public UIOrientation? DefaultOrientation;

        /// <summary>Any other field of ProjectSettings.asset by its serialized name (e.g. "useHDRDisplay").</summary>
        public void Set(string propertyPath, object value) => Add(propertyPath, value);
    }

    /// <summary>Time settings (ProjectSettings/TimeManager.asset).</summary>
    public sealed class TimeValues : ProjectValues
    {
        /// <summary>Time.fixedDeltaTime. Unity 6.3 stores it as a fraction: 0.02 reads back as 0.019999992 (the same within 1e-6).</summary>
        public float? FixedTimestep;
        public float? MaximumAllowedTimestep;
        public float? MaximumParticleTimestep;
        public float? TimeScale;

        /// <summary>Any other field of TimeManager.asset by its serialized name.</summary>
        public void Set(string propertyPath, object value) => Add(propertyPath, value);
    }

#if AGENTHARNESS_PHYSICS
    /// <summary>3D physics settings (ProjectSettings/DynamicsManager.asset), through the Physics API.</summary>
    public sealed class PhysicsValues : ProjectValues
    {
        public Vector3? Gravity;
        public int? DefaultSolverIterations;
        public int? DefaultSolverVelocityIterations;
        public float? BounceThreshold;
        public float? SleepThreshold;
        public float? DefaultContactOffset;
        public bool? QueriesHitTriggers;
        public bool? QueriesHitBackfaces;
        public SimulationMode? SimulationMode;

        internal List<(string a, string b)> Ignored;

        /// <summary>
        /// Layers <paramref name="layerA"/> and <paramref name="layerB"/> (names; the same name twice = within the layer) do not collide.
        /// Once any pair is declared, the layer collision matrix is exactly "everything collides but these pairs".
        /// </summary>
        public void IgnoreCollision(string layerA, string layerB) => (Ignored ??= new List<(string, string)>()).Add((layerA, layerB));

        /// <summary>Any other field of DynamicsManager.asset by its serialized name (e.g. "m_AutoSyncTransforms").</summary>
        public void Set(string propertyPath, object value) => Add(propertyPath, value);
    }
#endif

    /// <summary>
    /// One quality level for <see cref="SettingsContext.QualityLevels"/>. Levels are matched by name; a new one starts as a copy of
    /// the level before it (like the Quality settings' "Add Quality Level"). URP takes shadows, anti-aliasing and render scale
    /// from the level's pipeline asset, not from here.
    /// </summary>
    public sealed class QualityLevelValues : ProjectValues
    {
        public QualityLevelValues(string name) { Name = name; }

        public string Name { get; }
        /// <summary>The level's render pipeline (<see cref="SettingsContext.UsePipeline"/> for one level).</summary>
        public RenderPipelineAsset Pipeline;
        /// <summary>Platforms (build target names: "Standalone", "Android", "iPhone", "WebGL", ...) that start with this level.</summary>
        public string[] DefaultFor;
        /// <summary>Platforms that do not offer this level (the Quality settings' check boxes); the whole list.</summary>
        public string[] ExcludedPlatforms;
        public int? VSyncCount;
        public float? LodBias;
        public int? MaximumLodLevel;
        public AnisotropicFiltering? AnisotropicTextures;
        public int? GlobalTextureMipmapLimit;
        public SkinWeights? SkinWeights;
        public bool? RealtimeReflectionProbes;
        public bool? BillboardsFaceCameraPosition;
        public bool? SoftParticles;
        public int? ParticleRaycastBudget;
        public bool? StreamingMipmaps;

        /// <summary>Any other field of the level by its serialized name (e.g. "terrainPixelError"; Built-in shadows: "shadowDistance").</summary>
        public QualityLevelValues Set(string field, object value)
        {
            Add(field, value);
            return this;
        }
    }
}
