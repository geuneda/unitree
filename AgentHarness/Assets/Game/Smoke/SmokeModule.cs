using System;
using Game.Contracts;
using Harness;
using Unity.Pipeline.CodeReload;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Smoke
{
    /// <summary>
    /// Spins and bobs the hero object; Space (or gamepad South) reverses the spin.
    /// Publishes SpinnerLap / SpinDirectionChanged and mirrors both on the UI Toolkit HUD.
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
        Label m_LapsLabel;
        Label m_DirLabel;
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
            m_LapsLabel = root?.Q<Label>("laps");
            m_DirLabel = root?.Q<Label>("dir");
            UpdateHud();
        }

        [CodeReload]
        public void Tick(float dt)
        {
            if (m_Reverse.WasPressedThisFrame())
            {
                m_Direction = -m_Direction;
                EventBus.Publish(new SpinDirectionChanged(m_Direction));
                UpdateHud();
            }

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

            m_Spinner.localRotation = Quaternion.Euler(18f, m_Angle, 8f * Mathf.Sin(m_Time * 0.7f));
            m_Spinner.localPosition = m_BasePos + Vector3.up * (Mathf.Sin(m_Time * 1.6f) * 0.3f);
        }

        void UpdateHud()
        {
            if (m_LapsLabel != null) m_LapsLabel.text = m_Laps.ToString();
            if (m_DirLabel != null) m_DirLabel.text = m_Direction > 0 ? "CW" : "CCW";
        }

        public void Dispose()
        {
            m_Reverse?.Disable();
            m_Reverse?.Dispose();
            m_Reverse = null;
            m_Spinner = null;
            m_LapsLabel = m_DirLabel = null;
        }
    }
}
