using UnityEngine.UIElements;

namespace Harness.UI
{
    /// <summary>
    /// Short messages stacked at the top of the screen (UI kit, HarnessKit.uss: .ah-toast-stack, .ah-toast,
    /// .ah-toast--shown, .ah-toast--good/--warn/--bad). In UXML: <c>&lt;Harness.UI.ToastStack name="toasts" /&gt;</c>;
    /// in code: <c>root.Q&lt;ToastStack&gt;("toasts").Show("Level up", 1.5f, "good")</c>. A toast fades and slides in, stays
    /// <c>seconds</c>, fades out and is removed - USS transitions and panel timers, which follow the frames while a scenario
    /// plays with a fixed time step (a capture during a fade is the same every run).
    /// </summary>
    [UxmlElement]
    public partial class ToastStack : VisualElement
    {
        public const string UssClassName = "ah-toast-stack";
        public const string ToastUssClassName = "ah-toast";
        public const string ShownUssClassName = "ah-toast--shown";

        /// <summary>Fade-out time before a toast is removed (HarnessKit.uss --ah-fade).</summary>
        public const long FadeMs = 250;

        public ToastStack()
        {
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;
        }

        /// <summary>Show <paramref name="text"/> for <paramref name="seconds"/>; <paramref name="kind"/> "good", "warn", "bad" or null.</summary>
        public Label Show(string text, float seconds = 2f, string kind = null)
        {
            var toast = new Label(text) { pickingMode = PickingMode.Ignore };
            toast.AddToClassList(ToastUssClassName);
            if (!string.IsNullOrEmpty(kind)) toast.AddToClassList(ToastUssClassName + "--" + kind);
            Add(toast);
            // Shown after its first layout: a transition needs the hidden style to have been applied once.
            toast.RegisterCallbackOnce<GeometryChangedEvent>(_ => toast.AddToClassList(ShownUssClassName));
            var ms = (long)(seconds * 1000f);
            toast.schedule.Execute(() => toast.RemoveFromClassList(ShownUssClassName)).StartingIn(ms);
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(ms + FadeMs);
            return toast;
        }
    }
}
