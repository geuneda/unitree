using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Harness.UI
{
    /// <summary>
    /// A bar filled to <see cref="value"/> / <see cref="max"/> (UI kit, HarnessKit.uss: .ah-gauge, .ah-gauge__fill,
    /// .ah-gauge--good/--warn/--bad). In UXML: <c>&lt;Harness.UI.Gauge value="0.4" max="1" /&gt;</c>; both are bindable
    /// (<c>&lt;ui:DataBinding property="value" data-source-path="Health" /&gt;</c>).
    /// </summary>
    [UxmlElement]
    public partial class Gauge : VisualElement
    {
        public const string UssClassName = "ah-gauge";
        public const string FillUssClassName = "ah-gauge__fill";

        readonly VisualElement m_Fill;
        readonly VisualElement m_Rest;
        float m_Value;
        float m_Max = 1f;

        [UxmlAttribute, CreateProperty]
        public float value
        {
            get => m_Value;
            set { if (m_Value == value) return; m_Value = value; Refresh(); }
        }

        [UxmlAttribute, CreateProperty]
        public float max
        {
            get => m_Max;
            set { if (m_Max == value) return; m_Max = value; Refresh(); }
        }

        /// <summary>value / max in 0..1.</summary>
        public float fraction => m_Max > 0f ? Mathf.Clamp01(m_Value / m_Max) : 0f;

        public Gauge()
        {
            AddToClassList(UssClassName);
            style.flexDirection = FlexDirection.Row;
            m_Fill = new VisualElement { name = "fill", pickingMode = PickingMode.Ignore };
            m_Fill.AddToClassList(FillUssClassName);
            m_Rest = new VisualElement { name = "rest", pickingMode = PickingMode.Ignore };
            Add(m_Fill);
            Add(m_Rest);
            Refresh();
        }

        // Fill and rest share the width by flex-grow (value / max : the rest). Layout snaps to the panel's pixel grid: in a small
        // Game view (scale 0.24, a pixel = 4 units) 0.25 lays out as 0.235; a capture lays out again at its own size.
        void Refresh()
        {
            var f = fraction;
            m_Fill.style.flexBasis = 0f;
            m_Fill.style.flexGrow = f;
            m_Rest.style.flexBasis = 0f;
            m_Rest.style.flexGrow = 1f - f;
        }
    }
}
