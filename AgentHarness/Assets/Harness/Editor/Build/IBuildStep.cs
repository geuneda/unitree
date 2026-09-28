namespace Harness.Editor
{
    /// <summary>
    /// One step of scene generation. Implementations live in Assets/Game/&lt;Module&gt;/Builders/ (an Editor-only
    /// asmdef named &lt;Module&gt;.Builders) and need a public parameterless constructor.
    /// harness_build runs every step in <see cref="Order"/> order (ties: module, then type name) on a fresh
    /// empty scene and saves it as Assets/Scenes/Main.unity.
    ///
    /// Rules: deterministic (seeded RNG only — <c>Harness.Procedural.Noise.Rng</c>), no reading of the previous
    /// scene, all persistent assets through <see cref="BuildContext"/> so reruns overwrite in place.
    /// </summary>
    public interface IBuildStep
    {
        /// <summary>Suggested bands: 0–99 environment (camera, light, sky, post), 100–899 content, 900+ finishing (shots, validation).</summary>
        int Order { get; }

        void Build(BuildContext ctx);
    }
}
