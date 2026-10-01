using UnityEngine;

namespace Harness
{
    /// <summary>
    /// The clock of the harness sky's clouds (Shaders/HarnessSky.shader, made by the builder helper ctx.Sky): every frame it sets
    /// the global shader value _HarnessSkyTime to the game time since the scene loaded, and back to 0 when it stops. Game time,
    /// not real time: a scenario with a fixed time step shows the same clouds at the same t (a real-time clock made captures differ,
    /// G3-10), and edit-mode captures and the cubemaps a build renders see the clouds at time 0.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyClock : MonoBehaviour
    {
        public static readonly int TimeId = Shader.PropertyToID("_HarnessSkyTime");

        void Update() => Shader.SetGlobalFloat(TimeId, Time.timeSinceLevelLoad);

        void OnDisable() => Shader.SetGlobalFloat(TimeId, 0f);
    }
}
