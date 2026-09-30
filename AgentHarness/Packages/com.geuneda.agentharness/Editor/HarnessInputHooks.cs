using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>
    /// Finds the game's scenario input hooks for <see cref="InputHookReplay"/> (<see cref="InputHooks"/>): static
    /// <c>void M(string type, string key, Vector2 value)</c> methods marked with an attribute class named
    /// AgentHarnessInputAttribute. TypeCache makes the lookup cheap on every play (a Player run looks them up by reflection).
    /// </summary>
    public static class HarnessInputHooks
    {
        public const string AttributeName = InputHooks.AttributeName;

        /// <summary>All hooks as one delegate (null if none); <paramref name="names"/> lists them, <paramref name="errors"/> the ones with a wrong signature.</summary>
        public static Action<string, string, Vector2> Find(List<string> names, List<string> errors)
        {
            var methods = new List<MethodInfo>();
            foreach (var attribute in TypeCache.GetTypesDerivedFrom<Attribute>())
                if (attribute.Name == AttributeName) methods.AddRange(TypeCache.GetMethodsWithAttribute(attribute));
            return InputHooks.Combine(methods, names, errors);
        }
    }
}
