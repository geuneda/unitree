using Unity.Properties;

namespace Game.Smoke
{
    /// <summary>
    /// What the HUD shows. SmokeHud.uxml binds to these by name (<c>&lt;ui:DataBinding property="text" data-source-path="Laps" /&gt;</c>);
    /// the module only sets values. [CreateProperty] makes a property visible to UI Toolkit's data binding.
    /// </summary>
    public sealed class SmokeHudData
    {
        [CreateProperty] public int Laps { get; set; }
        [CreateProperty] public string Spin { get; set; } = "CW";
        /// <summary>How far the current lap is, 0..1 (the gauge).</summary>
        [CreateProperty] public float LapProgress { get; set; }
    }
}
