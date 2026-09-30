#ifndef HARNESS_NOISE_INCLUDED
#define HARNESS_NOISE_INCLUDED

// The noise of Harness.Procedural.Noise (C#) on the GPU: the same integer hash, gradients, octave seeds and ranges, so a
// GPU bake lines up with geometry a builder made on the CPU from the same calls (values agree up to float rounding).
// #include "Packages/com.geuneda.agentharness/Shaders/HarnessNoise.hlsl"

uint Noise_Hash(uint x)
{
    x ^= x >> 16; x *= 0x7feb352du;
    x ^= x >> 15; x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

uint Noise_Hash(int x, int y, int seed)
{
    return Noise_Hash((uint)x * 0x8da6b343u ^ Noise_Hash((uint)y * 0xd8163841u ^ Noise_Hash((uint)seed * 0xcb1ab31fu)));
}

/// Uniform in [0, 1) from integer coordinates (Noise.Value01).
float Noise_Value01(int x, int y, int seed)
{
    return (Noise_Hash(x, y, seed) & 0xFFFFFFu) / 16777216.0;
}

float Noise_Fade(float t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }

float Noise_Grad2(int ix, int iy, int seed, float dx, float dy)
{
    float a = ((Noise_Hash(ix, iy, seed) & 31u) + 0.5) / 32.0 * 6.28318530718;   // 32 unit gradients around the circle
    return cos(a) * dx + sin(a) * dy;
}

/// 2D gradient noise, range about [-1, 1] (Noise.Perlin).
float Noise_Perlin(float2 p, int seed)
{
    int ix = (int)floor(p.x); int iy = (int)floor(p.y);
    float fx = p.x - ix; float fy = p.y - iy;
    float u = Noise_Fade(fx); float v = Noise_Fade(fy);
    float n00 = Noise_Grad2(ix, iy, seed, fx, fy);
    float n10 = Noise_Grad2(ix + 1, iy, seed, fx - 1.0, fy);
    float n01 = Noise_Grad2(ix, iy + 1, seed, fx, fy - 1.0);
    float n11 = Noise_Grad2(ix + 1, iy + 1, seed, fx - 1.0, fy - 1.0);
    return lerp(lerp(n00, n10, u), lerp(n01, n11, u), v) * 1.4142135;
}

/// Fractal Brownian motion, about [-1, 1] (Noise.Fbm: octave o uses seed + o * 1013).
float Noise_Fbm(float2 p, int octaves, float lacunarity, float gain, int seed)
{
    float sum = 0.0, amp = 1.0, norm = 0.0, freq = 1.0;
    for (int i = 0; i < octaves; i++)
    {
        sum += Noise_Perlin(p * freq, seed + i * 1013) * amp;
        norm += amp; amp *= gain; freq *= lacunarity;
    }
    return sum / norm;
}

/// Ridged multifractal in [0, 1] (Noise.Ridged: octave o uses seed + o * 7919).
float Noise_Ridged(float2 p, int octaves, float lacunarity, float gain, int seed)
{
    float sum = 0.0, amp = 0.5, norm = 0.0, freq = 1.0, prev = 1.0;
    for (int i = 0; i < octaves; i++)
    {
        float n = 1.0 - abs(Noise_Perlin(p * freq, seed + i * 7919));
        n *= n;
        sum += n * amp * prev;
        norm += amp;
        prev = saturate(n * 2.0);
        amp *= gain; freq *= lacunarity;
    }
    return saturate(sum / norm);
}

/// Gradient noise that repeats every <period> lattice cells (integer): a tileable texture samples p = uv * period.
float Noise_PerlinTiled(float2 p, int period, int seed)
{
    int ix = (int)floor(p.x); int iy = (int)floor(p.y);
    float fx = p.x - ix; float fy = p.y - iy;
    float u = Noise_Fade(fx); float v = Noise_Fade(fy);
    int x0 = ((ix % period) + period) % period; int y0 = ((iy % period) + period) % period;
    int x1 = (x0 + 1) % period; int y1 = (y0 + 1) % period;
    float n00 = Noise_Grad2(x0, y0, seed, fx, fy);
    float n10 = Noise_Grad2(x1, y0, seed, fx - 1.0, fy);
    float n01 = Noise_Grad2(x0, y1, seed, fx, fy - 1.0);
    float n11 = Noise_Grad2(x1, y1, seed, fx - 1.0, fy - 1.0);
    return lerp(lerp(n00, n10, u), lerp(n01, n11, u), v) * 1.4142135;
}

/// fBm of tiled gradient noise: uv in [0, 1] repeats seamlessly; <period> lattice cells across the first octave, doubling per octave.
float Noise_FbmTiled(float2 uv, int period, int octaves, float gain, int seed)
{
    float sum = 0.0, amp = 1.0, norm = 0.0;
    int p = period;
    for (int i = 0; i < octaves; i++)
    {
        sum += Noise_PerlinTiled(uv * p, p, seed + i * 1013) * amp;
        norm += amp; amp *= gain; p *= 2;
    }
    return sum / norm;
}

/// Cellular F1 distance in [0, about 1] (Noise.Worley).
float Noise_Worley(float2 p, int seed)
{
    int ix = (int)floor(p.x); int iy = (int)floor(p.y);
    float best = 8.0;
    for (int oy = -1; oy <= 1; oy++)
    for (int ox = -1; ox <= 1; ox++)
    {
        int cx = ix + ox; int cy = iy + oy;
        float2 d = float2(cx + Noise_Value01(cx, cy, seed), cy + Noise_Value01(cx, cy, seed + 1)) - p;
        best = min(best, dot(d, d));
    }
    return sqrt(best);
}

/// Cellular F1 distance that repeats every <period> cells: a tileable texture samples p = uv * period.
float Noise_WorleyTiled(float2 p, int period, int seed)
{
    int ix = (int)floor(p.x); int iy = (int)floor(p.y);
    float best = 8.0;
    for (int oy = -1; oy <= 1; oy++)
    for (int ox = -1; ox <= 1; ox++)
    {
        int cx = ix + ox; int cy = iy + oy;
        int wx = ((cx % period) + period) % period; int wy = ((cy % period) + period) % period;
        float2 d = float2(cx + Noise_Value01(wx, wy, seed), cy + Noise_Value01(wx, wy, seed + 1)) - p;
        best = min(best, dot(d, d));
    }
    return sqrt(best);
}

#endif
