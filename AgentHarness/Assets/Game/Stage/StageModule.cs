using System;
using Game.Contracts;
using Harness;
using Unity.Pipeline.CodeReload;
using UnityEngine;

namespace Game.Stage
{
    /// <summary>Environment behaviour: the beacon light flashes whenever Smoke reports a spinner lap (via EventBus only).</summary>
    public sealed class StageModule : IGameModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => GameRoot.Register(new StageModule());

        public string Name => "Stage";
        public int Order => -10;

        Light m_Beacon;
        float m_BaseIntensity;
        float m_Pulse;
        IDisposable m_LapSub;

        public void Init(GameContext ctx)
        {
            var beacon = ctx.Find("Stage", "Beacon");
            if (beacon == null) throw new InvalidOperationException("Stage/Beacon not found - run harness_build");
            m_Beacon = beacon.GetComponent<Light>();
            m_BaseIntensity = m_Beacon.intensity;
            m_LapSub = EventBus.Subscribe<SpinnerLap>(_ => m_Pulse = 1f);
        }

        [CodeReload]
        public void Tick(float dt)
        {
            m_Pulse = Mathf.MoveTowards(m_Pulse, 0f, dt * 1.5f);
            m_Beacon.intensity = m_BaseIntensity * (1f + 12f * m_Pulse * m_Pulse);
        }

        public void Dispose()
        {
            m_LapSub?.Dispose();
            m_LapSub = null;
            m_Beacon = null;
        }
    }
}
