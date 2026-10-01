using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>
    /// Visual regression (G3-4): shots compared with golden images - &lt;root&gt;/&lt;Unity version&gt;/&lt;key&gt;/&lt;shot file&gt;.png,
    /// one folder per Unity version (renders differ between versions, P-4) and OS (Direct3D on Windows, Metal on macOS:
    /// &lt;version&gt;-macos, &lt;version&gt;-linux; P-3), key = the scenario's name. A pixel counts as
    /// changed when a channel differs by more than <see cref="PixelThreshold"/>; a shot is "same" while at most
    /// <see cref="SameRatio"/> of its pixels changed (isolated edge pixels) and the mean difference is at most
    /// <see cref="SameMean"/> (a small shift of the whole image, e.g. exposure, changes no pixel by much).
    /// The same machine renders the same pixels (fixed scenario time step: measured 0 difference); goldens made on another
    /// GPU or driver may differ by a little - the tolerances are for that, not measured yet.
    /// </summary>
    public static class HarnessGolden
    {
        public const int PixelThreshold = 24;
        public const float SameRatio = 0.0001f;   // ~90 pixels of 1280x720
        public const float SameMean = 0.5f;

        [Serializable]
        sealed class ShotIn
        {
            public string path;
            public string name;
            public ShotRect[] ignore = Array.Empty<ShotRect>();
        }

        [Serializable]
        sealed class ShotList
        {
            public ShotIn[] items = Array.Empty<ShotIn>();
        }

        public sealed class Result
        {
            public string name;
            public string path;
            public string golden;       // the golden image compared with (or written)
            public string status;       // same | changed | size | missing | error | updated
            public float meanDiff;      // mean |channel difference| (0..255) over the compared pixels
            public int maxDiff;         // largest channel difference
            public float changedRatio;  // pixels with a channel off by more than PixelThreshold
            public float ssim;          // structural similarity of the luma (8x8 blocks), 1 = same
            public int[] rect;          // [x, y, w, h] pixels from the top left: where the changed pixels are
            public string diff;         // image of the changes (the shot dimmed, changed pixels red, ignored regions blue)
            public string error;
        }

        [CliCommand("harness_golden",
            "Compare shots (PNG) with golden images <golden>/<Unity version>/<key>/<file>, or write them (update=true). " +
            "Per shot: status same|changed|size|missing, meanDiff, changedRatio, ssim, rect (where it changed) and a diff image in <out>/golden/. " +
            "A version without goldens uses those of the nearest version with the same major.minor. macOS and Linux Editors keep their own " +
            "version folders (<version>-macos, <version>-linux). tools/loop.ps1 calls it after every loop.",
            Tags = new[] { "harness", "capture" })]
        public static object Golden(
            [CliArg("shots", "JSON array of {path, name, ignore:[{x,y,w,h}]} (ignore: fractions from the top left).")] string shots,
            [CliArg("golden", "Golden root (relative to the project root). Default: goldenRoot of ProjectSettings/AgentHarness.json.")] string golden = "",
            [CliArg("key", "Folder for this set of shots under <golden>/<Unity version>/ (the scenario's name).")] string key = "default",
            [CliArg("out", "Where the diff images go (<out>/golden/). Default: next to the first shot.")] string @out = "",
            [CliArg("update", "Write the shots as the golden images of this Unity version instead (replaces that folder's PNGs).")] bool update = false)
        {
            ShotIn[] items;
            try { items = JsonUtility.FromJson<ShotList>("{\"items\":" + (string.IsNullOrWhiteSpace(shots) ? "[]" : shots) + "}").items ?? Array.Empty<ShotIn>(); }
            catch (Exception e) { return new { ok = false, error = "shots: invalid JSON: " + e.Message }; }
            var root = HarnessPaths.Resolve(string.IsNullOrWhiteSpace(golden) ? HarnessConfig.Current.goldenRoot : golden);
            var version = Application.unityVersion + PlatformSuffix;
            var folder = Sanitize(string.IsNullOrWhiteSpace(key) ? "default" : key);
            var results = new List<Result>();

            if (update)
            {
                var dir = HarnessPaths.Combine(HarnessPaths.Combine(root, version), folder);
                Directory.CreateDirectory(dir);
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in items)
                {
                    var file = Path.GetFileName(s.path ?? "");
                    var r = new Result { name = s.name, path = s.path, golden = HarnessPaths.Combine(dir, file) };
                    try
                    {
                        File.Copy(HarnessPaths.Resolve(s.path), r.golden, true);
                        keep.Add(file);
                        r.status = "updated";
                    }
                    catch (Exception e) { r.status = "error"; r.error = e.GetType().Name + ": " + e.Message; }
                    results.Add(r);
                }
                var removed = new List<string>();
                foreach (var f in Directory.GetFiles(dir, "*.png"))
                {
                    if (keep.Contains(Path.GetFileName(f))) continue;
                    File.Delete(f);
                    removed.Add(Path.GetFileName(f));
                }
                return new { ok = results.TrueForAll(r => r.status == "updated"), update = true, root, version, dir, removed, results };
            }

            var from = PickVersion(root, version, folder);
            var goldenDir = from == null ? null : HarnessPaths.Combine(HarnessPaths.Combine(root, from), folder);
            var outDir = !string.IsNullOrWhiteSpace(@out) ? HarnessPaths.Resolve(@out)
                : items.Length > 0 && !string.IsNullOrEmpty(items[0].path) ? Path.GetDirectoryName(HarnessPaths.Resolve(items[0].path)).Replace('\\', '/') : HarnessPaths.Resolve("HarnessOut");
            var diffDir = HarnessPaths.Combine(outDir, "golden");
            if (Directory.Exists(diffDir)) foreach (var f in Directory.GetFiles(diffDir, "*.png")) File.Delete(f);
            foreach (var s in items)
            {
                var file = Path.GetFileName(s.path ?? "");
                var r = new Result { name = s.name, path = s.path };
                results.Add(r);
                if (goldenDir == null || !File.Exists(HarnessPaths.Combine(goldenDir, file)))
                {
                    r.status = "missing";
                    continue;
                }
                r.golden = HarnessPaths.Combine(goldenDir, file);
                try { Compare(HarnessPaths.Resolve(s.path), r.golden, s.ignore ?? Array.Empty<ShotRect>(), HarnessPaths.Combine(diffDir, Path.GetFileNameWithoutExtension(file) + ".diff.png"), r); }
                catch (Exception e) { r.status = "error"; r.error = e.GetType().Name + ": " + e.Message; }
            }
            int Count(string status) => results.FindAll(r => r.status == status).Count;
            return new
            {
                ok = true, update = false, root, version, from, dir = goldenDir,
                same = Count("same"), changed = Count("changed") + Count("size"), missing = Count("missing"), errors = Count("error"),
                pixelThreshold = PixelThreshold, sameRatio = SameRatio, sameMean = SameMean, results,
            };
        }

        [Serializable]
        sealed class PairIn
        {
            public string name;
            public string path;
            public string against;
            public string diff;      // file name of the diff image in <out>/
            public ShotRect[] ignore = Array.Empty<ShotRect>();
        }

        [Serializable]
        sealed class PairList
        {
            public PairIn[] items = Array.Empty<PairIn>();
        }

        [CliCommand("harness_compare",
            "Compare pairs of images with the golden-image rules (G3-4): per pair status same|changed|size|error, meanDiff, changedRatio, " +
            "ssim, maxDiff, rect and a diff image <out>/<diff> when it changed. tools/player.ps1 compares a Player run's shots with the " +
            "Editor's and with the Player's screen (W8).",
            MainThreadRequired = true, Tags = new[] { "harness", "capture" })]
        public static object ComparePairs(
            [CliArg("pairs", "JSON array of {name, path, against, diff, ignore:[{x,y,w,h}]} (paths absolute or from the project root).")] string pairs,
            [CliArg("out", "Folder for the diff images.")] string @out = "HarnessOut/compare",
            [CliArg("same_mean", "Largest mean difference (0..255) of a 'same' pair (default the golden rule's 0.5; two renders with URP dithering differ by ~0.5).")] float sameMean = SameMean)
        {
            PairIn[] items;
            try { items = JsonUtility.FromJson<PairList>("{\"items\":" + (string.IsNullOrWhiteSpace(pairs) ? "[]" : pairs) + "}").items ?? Array.Empty<PairIn>(); }
            catch (Exception e) { return new { ok = false, error = "pairs: invalid JSON: " + e.Message }; }
            var outDir = HarnessPaths.Resolve(@out);
            var results = new List<Result>();
            foreach (var p in items)
            {
                var r = new Result { name = p.name, path = p.path, golden = p.against };
                results.Add(r);
                try
                {
                    var diffName = string.IsNullOrWhiteSpace(p.diff) ? Path.GetFileNameWithoutExtension(p.path ?? "shot") + ".diff.png" : p.diff;
                    Compare(HarnessPaths.Resolve(p.path), HarnessPaths.Resolve(p.against), p.ignore ?? Array.Empty<ShotRect>(), HarnessPaths.Combine(outDir, diffName), r, sameMean);
                }
                catch (Exception e) { r.status = "error"; r.error = e.GetType().Name + ": " + e.Message; }
            }
            int Count(string status) => results.FindAll(r => r.status == status).Count;
            return new { ok = true, same = Count("same"), changed = Count("changed") + Count("size"), errors = Count("error"), sameMean, sameRatio = SameRatio, results };
        }

        static string Sanitize(string s)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Trim();
        }

        // ---- versions -----------------------------------------------------------------------------------------------

        static readonly Regex VersionPattern = new Regex(@"^(\d+)\.(\d+)\.(\d+)([abfp])(\d+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// The version folder's suffix for this OS: shots rendered with another graphics API and fonts differ by more than the
        /// tolerances (a macOS shot against the Windows goldens: ~0.1% of the pixels, the HUD's text and edges; P-3).
        /// </summary>
        static string PlatformSuffix =>
            Application.platform == RuntimePlatform.OSXEditor ? "-macos" : Application.platform == RuntimePlatform.LinuxEditor ? "-linux" : "";

        static long[] ParseVersion(string v)
        {
            var m = VersionPattern.Match(v ?? "");
            if (!m.Success) return null;
            return new[] { long.Parse(m.Groups[1].Value), long.Parse(m.Groups[2].Value), long.Parse(m.Groups[3].Value), "abfp".IndexOf(m.Groups[4].Value[0]), long.Parse(m.Groups[5].Value) };
        }

        static int CompareVersion(long[] a, long[] b)
        {
            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return 0;
        }

        /// <summary>
        /// The version folder with goldens for <paramref name="key"/>: this version's, else the nearest of the same
        /// major.minor on this OS (the newest older one, else the oldest newer one) - a patch release usually renders the same.
        /// </summary>
        static string PickVersion(string root, string version, string key)
        {
            if (Directory.Exists(HarnessPaths.Combine(HarnessPaths.Combine(root, version), key))) return version;
            var me = ParseVersion(version);
            if (me == null || !Directory.Exists(root)) return null;
            string below = null, above = null;
            long[] belowV = null, aboveV = null;
            foreach (var d in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(d);
                var v = ParseVersion(name);
                if (v == null || name.Substring(VersionPattern.Match(name).Length) != PlatformSuffix) continue;   // another OS's goldens
                if (v[0] != me[0] || v[1] != me[1] || !Directory.Exists(Path.Combine(d, key))) continue;
                if (CompareVersion(v, me) < 0) { if (belowV == null || CompareVersion(v, belowV) > 0) { below = name; belowV = v; } }
                else if (aboveV == null || CompareVersion(v, aboveV) < 0) { above = name; aboveV = v; }
            }
            return below ?? above;
        }

        // ---- comparison ---------------------------------------------------------------------------------------------

        static Color32[] Load(string path, out int width, out int height)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path), false)) throw new IOException("not a PNG/JPG: " + path);
                width = tex.width;
                height = tex.height;
                return tex.GetPixels32();
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>Fill <paramref name="r"/> with how <paramref name="shot"/> differs from <paramref name="golden"/>; writes the diff image when it changed.</summary>
        public static void Compare(string shot, string golden, ShotRect[] ignore, string diffPath, Result r, float sameMean = SameMean)
        {
            var a = Load(shot, out var w, out var h);
            var b = Load(golden, out var gw, out var gh);
            if (w != gw || h != gh)
            {
                r.status = "size";
                r.error = $"shot {w}x{h}, golden {gw}x{gh}";
                r.changedRatio = 1f;
                return;
            }
            // Ignored pixels (rows bottom to top in the arrays, the rects are from the top).
            var skip = new bool[w * h];
            foreach (var q in ignore)
            {
                if (q == null) continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(q.x * w), 0, w), x1 = Mathf.Clamp(Mathf.CeilToInt((q.x + q.w) * w), 0, w);
                int top = Mathf.Clamp(Mathf.FloorToInt(q.y * h), 0, h), bottom = Mathf.Clamp(Mathf.CeilToInt((q.y + q.h) * h), 0, h);
                for (var y = top; y < bottom; y++)
                    for (var x = x0; x < x1; x++) skip[(h - 1 - y) * w + x] = true;
            }
            long sum = 0, compared = 0, changed = 0;
            var max = 0;
            int minX = w, minY = h, maxX = -1, maxY = -1;   // image coordinates (from the top)
            var diff = new int[w * h];
            for (var i = 0; i < a.Length; i++)
            {
                if (skip[i]) continue;
                Color32 p = a[i], q = b[i];
                int dr = Math.Abs(p.r - q.r), dg = Math.Abs(p.g - q.g), db = Math.Abs(p.b - q.b);
                var d = Math.Max(dr, Math.Max(dg, db));
                diff[i] = d;
                sum += dr + dg + db;
                compared++;
                if (d > max) max = d;
                if (d <= PixelThreshold) continue;
                changed++;
                int x = i % w, y = h - 1 - i / w;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            r.meanDiff = compared == 0 ? 0f : (float)Math.Round(sum / (3.0 * compared), 3);
            r.maxDiff = max;
            r.changedRatio = compared == 0 ? 0f : (float)Math.Round((double)changed / compared, 6);
            r.ssim = (float)Math.Round(Ssim(a, b, skip, w, h), 5);
            r.status = r.changedRatio <= SameRatio && r.meanDiff <= sameMean ? "same" : "changed";
            if (changed > 0) r.rect = new[] { minX, minY, maxX - minX + 1, maxY - minY + 1 };
            if (r.status == "same") return;
            WriteDiff(a, diff, skip, w, h, r.rect, diffPath);
            r.diff = diffPath;
        }

        /// <summary>Mean SSIM of the luma over 8x8 blocks (blocks with an ignored pixel are left out).</summary>
        static double Ssim(Color32[] a, Color32[] b, bool[] skip, int w, int h)
        {
            const double c1 = 6.5025, c2 = 58.5225;   // (0.01*255)^2, (0.03*255)^2
            double total = 0;
            var blocks = 0;
            for (var by = 0; by + 8 <= h; by += 8)
            {
                for (var bx = 0; bx + 8 <= w; bx += 8)
                {
                    double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
                    var skipped = false;
                    for (var y = by; y < by + 8 && !skipped; y++)
                    {
                        for (var x = bx; x < bx + 8; x++)
                        {
                            var i = y * w + x;
                            if (skip[i]) { skipped = true; break; }
                            double la = 0.2126 * a[i].r + 0.7152 * a[i].g + 0.0722 * a[i].b;
                            double lb = 0.2126 * b[i].r + 0.7152 * b[i].g + 0.0722 * b[i].b;
                            sa += la; sb += lb; saa += la * la; sbb += lb * lb; sab += la * lb;
                        }
                    }
                    if (skipped) continue;
                    const double n = 64;
                    double ma = sa / n, mb = sb / n;
                    double va = saa / n - ma * ma, vb = sbb / n - mb * mb, cov = sab / n - ma * mb;
                    total += (2 * ma * mb + c1) * (2 * cov + c2) / ((ma * ma + mb * mb + c1) * (va + vb + c2));
                    blocks++;
                }
            }
            return blocks == 0 ? 1.0 : total / blocks;
        }

        /// <summary>The shot dimmed to gray, changed pixels red (brighter = more), ignored regions blue, the changed area outlined yellow.</summary>
        static void WriteDiff(Color32[] shot, int[] diff, bool[] skip, int w, int h, int[] rect, string path)
        {
            var px = new Color32[shot.Length];
            for (var i = 0; i < px.Length; i++)
            {
                var c = shot[i];
                var g = (byte)((0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) * 0.3f);
                if (skip[i]) px[i] = new Color32(g, g, (byte)Mathf.Min(255, g + 90), 255);
                else if (diff[i] > PixelThreshold) px[i] = new Color32((byte)Mathf.Clamp(96 + diff[i], 0, 255), 0, 0, 255);
                else px[i] = new Color32(g, g, g, 255);
            }
            if (rect != null)
            {
                var yellow = new Color32(255, 220, 0, 255);
                int x0 = Math.Max(0, rect[0] - 2), x1 = Math.Min(w - 1, rect[0] + rect[2] + 1);
                int y0 = Math.Max(0, rect[1] - 2), y1 = Math.Min(h - 1, rect[1] + rect[3] + 1);   // from the top
                for (var x = x0; x <= x1; x++) { px[(h - 1 - y0) * w + x] = yellow; px[(h - 1 - y1) * w + x] = yellow; }
                for (var y = y0; y <= y1; y++) { px[(h - 1 - y) * w + x0] = yellow; px[(h - 1 - y) * w + x1] = yellow; }
            }
            HarnessCapture.WritePng(px, w, h, path);
        }
    }
}
