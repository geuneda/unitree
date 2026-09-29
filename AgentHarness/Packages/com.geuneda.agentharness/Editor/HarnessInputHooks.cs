using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>
    /// Finds the game's scenario input hooks for <see cref="InputHookReplay"/>: static <c>void M(string type, string key,
    /// Vector2 value)</c> methods marked with an attribute class named AgentHarnessInputAttribute. The game defines that
    /// attribute itself (Tools~/templates/HarnessInput.cs), so it needs no reference to the harness package and keeps
    /// compiling after uninstall. TypeCache makes the lookup cheap on every play.
    /// </summary>
    public static class HarnessInputHooks
    {
        public const string AttributeName = "AgentHarnessInputAttribute";

        /// <summary>All hooks as one delegate (null if none); <paramref name="names"/> lists them, <paramref name="errors"/> the ones with a wrong signature.</summary>
        public static Action<string, string, Vector2> Find(List<string> names, List<string> errors)
        {
            Action<string, string, Vector2> all = null;
            foreach (var attribute in TypeCache.GetTypesDerivedFrom<Attribute>())
            {
                if (attribute.Name != AttributeName) continue;
                foreach (var m in TypeCache.GetMethodsWithAttribute(attribute))
                {
                    var name = m.DeclaringType?.FullName + "." + m.Name;
                    var ps = m.GetParameters();
                    if (!m.IsStatic || m.ReturnType != typeof(void) || m.ContainsGenericParameters || ps.Length != 3 ||
                        ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(string) || ps[2].ParameterType != typeof(Vector2))
                    {
                        errors?.Add($"{name}: [AgentHarnessInput] needs static void {m.Name}(string type, string key, Vector2 value)");
                        continue;
                    }
                    all += (Action<string, string, Vector2>)Delegate.CreateDelegate(typeof(Action<string, string, Vector2>), m);
                    names?.Add(name);
                }
            }
            return all;
        }
    }
}
