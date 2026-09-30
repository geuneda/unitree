using System;
using Game.Contracts;
using Harness;
using Harness.UI;
using Unity.Pipeline.CodeReload;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Smoke
{
    /// <summary>
    /// Spins and bobs the hero object; Space, the HUD's REVERSE button (or gamepad South) reverses the spin.
    /// Publishes SpinnerLap / SpinDirectionChanged; the UI Toolkit HUD binds to SmokeHudData and shows a toast on reverse.
    /// </summary>
    public sealed class SmokeModule : IGameModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => GameRoot.Register(new SmokeModule());

        public string Name => "Smoke";

        const float DegreesPerSecond = 240f;

        Transform m_Spinner;
        Vector3 m_BasePos;
        InputAction m_Reverse;
        readonly SmokeHudData m_Hud = new SmokeHudData();
        ToastStack m_Toasts;
        float m_Angle;
        float m_Traveled;
        float m_Time;
        int m_Laps;
        int m_Direction = 1;

        public void Init(GameContext ctx)
        {
            m_Spinner = ctx.Find("Smoke", "Spinner");
            if (m_Spinner == null) throw new InvalidOperationException("Smoke/Spinner not found - run harness_build");
            m_BasePos = m_Spinner.localPosition;

            m_Reverse = new InputAction("Reverse", InputActionType.Button, "<Keyboard>/space");
            m_Reverse.AddBinding("<Gamepad>/buttonSouth");
            m_Reverse.Enable();

            var hud = ctx.Find("Smoke", "HUD");
            // No ?. on UnityEngine.Object: a missing component is a "fake null" in the Editor.
            var root = hud != null && hud.TryGetComponent<UIDocument>(out var doc) ? doc.rootVisualElement : null;
            if (root != null)
            {
                root.dataSource = m_Hud;   // the labels and the gauge bind to its properties (SmokeHud.uxml)
                m_Toasts = root.Q<ToastStack>("toasts");
                var button = root.Q<Button>("reverse");
                if (button != null) button.clicked += Reverse;
            }
            UpdateHud();
        }

        [CodeReload]
        public void Tick(float dt)
        {
            if (m_Reverse.WasPressedThisFrame()) Reverse();

            m_Time += dt;
            m_Angle += DegreesPerSecond * m_Direction * dt;
            m_Traveled += DegreesPerSecond * dt;
            var laps = Mathf.FloorToInt(m_Traveled / 360f);
            if (laps != m_Laps)
            {
                m_Laps = laps;
                EventBus.Publish(new SpinnerLap(laps));
                UpdateHud();
            }
            m_Hud.LapProgress = m_Traveled / 360f - laps;

            m_Spinner.localRotation = Quaternion.Euler(18f, m_Angle, 8f * Mathf.Sin(m_Time * 0.7f));
            m_Spinner.localPosition = m_BasePos + Vector3.up * (Mathf.Sin(m_Time * 1.6f) * 0.3f);
        }

        void Reverse()
        {
            m_Direction = -m_Direction;
            EventBus.Publish(new SpinDirectionChanged(m_Direction));
            UpdateHud();
            m_Toasts?.Show(m_Direction > 0 ? "CLOCKWISE" : "COUNTER-CLOCKWISE", 1.2f);
        }

        void UpdateHud()
        {
            m_Hud.Laps = m_Laps;
            m_Hud.Spin = m_Direction > 0 ? "CW" : "CCW";
        }

        public void Dispose()
        {
            m_Reverse?.Disable();
            m_Reverse?.Dispose();
            m_Reverse = null;
            m_Spinner = null;
            m_Toasts = null;
        }
    }
}
