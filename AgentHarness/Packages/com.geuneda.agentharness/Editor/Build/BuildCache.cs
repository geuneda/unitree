using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>name → key map persisted in Library/Harness/buildcache.json (machine-local, never committed).</summary>
    [Serializable]
    sealed class BuildCache
    {
        [Serializable]
        sealed class Entry { public string name; public string key; }

        [SerializeField] List<Entry> entries = new List<Entry>();

        static string FilePath => HarnessPaths.Combine(HarnessPaths.StateDir, "buildcache.json");

        public static BuildCache Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonUtility.FromJson<BuildCache>(File.ReadAllText(FilePath)) ?? new BuildCache();
            }
            catch { }
            return new BuildCache();
        }

        public string Get(string name)
        {
            foreach (var e in entries) if (e.name == name) return e.key;
            return null;
        }

        public void Set(string name, string key)
        {
            foreach (var e in entries) if (e.name == name) { e.key = key; return; }
            entries.Add(new Entry { name = name, key = key });
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(this, true)); } catch { }
        }
    }
}
