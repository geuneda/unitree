using Harness.Editor;
using UnityEngine;

namespace Game.Stage.Builders
{
    /// <summary>
    /// ProjectSettings as code: the layers the build steps put objects on, the standalone window, time and physics. Every build
    /// writes these where ProjectSettings/ differs and reports a value changed outside the code (build.settings.project.drift).
    /// What is not named here stays as committed.
    /// </summary>
    public sealed class StageProjectSettingsStep : ISettingsStep
    {
        public const int Ground = 8;   // the terrain: its MeshCollider is what a raycast against LayerMask.GetMask("Ground") hits
        public const int Props = 9;    // standing stones, rocks, arch

        public int Order => 10;

        public void Apply(SettingsContext ctx)
        {
            ctx.Layer(Ground, "Ground");
            ctx.Layer(Props, "Props");
            ctx.Player(p =>
            {
                // A Player started by hand opens like the loop's captures (tools/player.ps1 passes the capture size anyway).
                p.DefaultScreenWidth = 1280;
                p.DefaultScreenHeight = 720;
                p.FullScreenMode = FullScreenMode.Windowed;
            });
            ctx.Time(t =>
            {
                t.FixedTimestep = 0.02f;
                t.MaximumAllowedTimestep = 1f / 3f;
            });
            ctx.Physics(p => p.Gravity = new Vector3(0f, -9.81f, 0f));
        }
    }
}
