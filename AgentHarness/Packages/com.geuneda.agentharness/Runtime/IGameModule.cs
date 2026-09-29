namespace Harness
{
    /// <summary>
    /// A self-contained gameplay module. Modules never reference each other; they talk only
    /// through <see cref="EventBus"/> (event types that cross modules live in the contracts folder, e.g. Assets/Game/Contracts).
    ///
    /// Register from the module's own assembly so no shared file has to change:
    /// <code>
    /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    /// static void Register() => GameRoot.Register(new MyModule());
    /// </code>
    /// </summary>
    public interface IGameModule
    {
        /// <summary>Stable name. By convention equals the module's folder name (e.g. under Assets/Game/) and the scene root the builder creates.</summary>
        string Name { get; }

        /// <summary>Lower runs first (Init and Tick). Dispose runs in reverse.</summary>
        int Order => 0;

        void Init(GameContext ctx);
        void Tick(float dt);
        void Dispose();
    }
}
