namespace Harness.Editor
{
    /// <summary>
    /// Project settings from code (G1-1, G1-5): the render pipeline and renderer assets, their features, and which pipeline the
    /// Graphics settings and each quality level use. Implementations live next to the build steps (&lt;module folder&gt;/Builders/)
    /// and need a public parameterless constructor.
    ///
    /// harness_build runs every settings step in <see cref="Order"/> order before the build steps (so a build step renders with
    /// the pipeline they set), and harness_setup runs them too (a new clone gets its pipeline before the first loop). Only in a
    /// harness project ("setup": "harness"); an attached project's settings are its own and are never touched.
    ///
    /// Each run starts from the pipeline's defaults, so an asset is exactly what the code says (a setting the code does not
    /// set is the default of this Unity version) and is rewritten only when that differs from what is on disk. The assets are
    /// generated (&lt;generatedRoot&gt;/&lt;Module&gt;/, not committed) with a GUID derived from their path, so the ProjectSettings
    /// that reference them stay the same on every machine. Settings values are part of build.fingerprint.
    ///
    /// ProjectSettings/ itself (G1-5) cannot be generated: the quality levels, player, time and physics values, layers and tags a
    /// step names (<see cref="SettingsContext.QualityLevels"/>, <see cref="SettingsContext.Player"/>, ...) are written on every run
    /// where they differ, and a value changed outside the code since the previous run is reported (build.settings.project.drift).
    /// What the code does not name stays as committed.
    /// </summary>
    public interface ISettingsStep
    {
        int Order { get; }

        void Apply(SettingsContext ctx);
    }
}
