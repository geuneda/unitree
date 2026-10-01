using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using TextElement = UnityEngine.UIElements.TextElement;

namespace Harness
{
    /// <summary>
    /// The kerning pairs a dynamic font asset gets from FontEngine (FontAsset.UpdateGlyphAdjustmentRecords) carry
    /// uninitialized featureLookupFlags on macOS (Unity 6.3 and 6.6: about a quarter of the records, other values every
    /// session), and TextCore's text generator drops the letter spacing of a pair whose flags have IgnoreSpacingAdjustments (0x100)
    /// (UI Toolkit's standard generator, the default in 6.0-6.4; 6.6's default Advanced Text Generator does not read the pairs).
    /// Text with letter-spacing was a fraction of a pixel narrower after such pairs, differently from session to session
    /// (G3-17: the HUD title's "AG" and "GE" pairs, 0.27 pixels each at 1280x720). A font file has no such flag - it is set by
    /// hand on a font asset - so before a capture the flags of the dynamic font assets' pairs are cleared, and if any were set
    /// the UI Toolkit text is marked to be generated again (the capture then lays the panels out and draws them). Static font
    /// assets are left as they are. Internal API (FontFeatureTable's pair list and lookup, FontAsset.UpdateFontAssetsInUpdateQueue): without it
    /// nothing changes.
    /// </summary>
    internal static class KerningFlags
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly MethodInfo FlushQueue = typeof(FontAsset).GetMethod("UpdateFontAssetsInUpdateQueue", Any, null, System.Type.EmptyTypes, null);
        static readonly PropertyInfo Records = typeof(FontFeatureTable).GetProperty("glyphPairAdjustmentRecords", Any);
        static readonly FieldInfo Lookup = typeof(FontFeatureTable).GetField("m_GlyphPairAdjustmentRecordLookup", Any);

        /// <summary>Clear the flags of the dynamic font assets' kerning pairs.</summary>
        public static void Clear()
        {
            if (Records == null || Lookup == null) return;
            FlushQueue?.Invoke(null, null);   // the pairs of glyphs added since the last text update
            var cleared = 0;
            foreach (var font in Resources.FindObjectsOfTypeAll<FontAsset>())
            {
                if (font.atlasPopulationMode == AtlasPopulationMode.Static || font.fontFeatureTable == null) continue;
                // Only fonts that text has used (the generator reads the lookup): the Editor keeps every play's runtime font
                // assets, and after ~400 plays their pair lists held 2.1 million records (77 ms a capture to go through).
                if (!(Lookup.GetValue(font.fontFeatureTable) is Dictionary<uint, GlyphPairAdjustmentRecord> lookup) || lookup.Count == 0) continue;
                var keys = new List<uint>();
                foreach (var kv in lookup)
                    if (kv.Value.featureLookupFlags != FontFeatureLookupFlags.None) keys.Add(kv.Key);
                foreach (var key in keys) lookup[key] = WithoutFlags(lookup[key]);
                cleared += keys.Count;
                if (Records.GetValue(font.fontFeatureTable) is List<GlyphPairAdjustmentRecord> list)
                    for (var i = 0; i < list.Count; i++)
                        if (list[i].featureLookupFlags != FontFeatureLookupFlags.None) list[i] = WithoutFlags(list[i]);
            }
            if (cleared > 0)
                foreach (var doc in UnityCompat.PanelDocuments())
                    doc.root?.Query<TextElement>().ForEach(t => t.MarkDirtyText());
        }

        static GlyphPairAdjustmentRecord WithoutFlags(GlyphPairAdjustmentRecord r)
        {
            r.featureLookupFlags = FontFeatureLookupFlags.None;
            return r;
        }
    }
}
