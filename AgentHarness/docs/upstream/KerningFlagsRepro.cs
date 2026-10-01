using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

// Kerning pairs that a dynamic FontAsset gets from FontEngine carry uninitialized featureLookupFlags.
// Run (three times, compare the lines):
//   Unity -batchmode -quit -projectPath <this project> -executeMethod KerningFlagsRepro.Run -logFile -
public static class KerningFlagsRepro
{
    public static void Run()
    {
        // A dynamic font asset of an OS font with kerning (Arial is on Windows and macOS).
        var asset = FontAsset.CreateFontAsset("Arial", "Regular", 90);
        asset.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz", out _, includeFontFeatures: true);

        var records = (List<GlyphPairAdjustmentRecord>)typeof(FontFeatureTable)
            .GetProperty("glyphPairAdjustmentRecords", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(asset.fontFeatureTable);
        int nonZero = 0, ignoreSpacing = 0;
        var samples = new List<string>();
        foreach (var r in records)
        {
            var flags = (int)r.featureLookupFlags;
            if (flags == 0) continue;
            nonZero++;
            if ((r.featureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) != 0) ignoreSpacing++;
            if (samples.Count < 8) samples.Add("0x" + flags.ToString("X"));
        }
        Debug.Log($"KERNING-FLAGS unity={Application.unityVersion} platform={Application.platform} pairs={records.Count} " +
                  $"flagsNotNone={nonZero} withIgnoreSpacingAdjustments={ignoreSpacing} samples={string.Join(",", samples)}");
    }
}
