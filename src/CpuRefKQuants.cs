// K-quant (Q4_K, Q5_K, Q6_K) dequantization and dot products against Q8_K activations. C# 5 only.
// Ported from dequantize_row_q*_K / quantize_row_q8_K_ref (ggml-quants.c) and
// ggml_vec_dot_q*_K_q8_K_generic (ggml-cpu/quants.c), MIT, llama.cpp.
using System;
using System.Threading.Tasks;

namespace Phoenix
{
    public static unsafe partial class CpuRef
    {
        public const int QK_K = 256;
        public const int BlockQ4_KBytes = 144;  // half d, dmin; u8 scales[12]; u8 qs[128]
        public const int BlockQ5_KBytes = 176;  // half d, dmin; u8 scales[12]; u8 qh[32]; u8 qs[128]
        public const int BlockQ6_KBytes = 210;  // u8 ql[128]; u8 qh[64]; i8 scales[16]; half d

        public static int KBlockBytes(GgmlType t)
        {
            switch (t)
            {
                case GgmlType.Q4_K: return BlockQ4_KBytes;
                case GgmlType.Q5_K: return BlockQ5_KBytes;
                case GgmlType.Q6_K: return BlockQ6_KBytes;
                case GgmlType.Q3_K: return BlockQ3_KBytes;
                case GgmlType.IQ2_XXS: return BlockIQ2_XXSBytes;
                case GgmlType.IQ2_S: return BlockIQ2_SBytes;
                case GgmlType.IQ3_S: return BlockIQ3_SBytes;
            }
            throw new NotSupportedException(GgmlTypes.Name(t) + " is not a k-quant.");
        }

        // 6-bit scale and min for sub-block j of a Q4_K/Q5_K block (get_scale_min_k4).
        public static void ScaleMinK4(int j, byte* q, out int sc, out int m)
        {
            if (j < 4)
            {
                sc = q[j] & 63; m = q[j + 4] & 63;
            }
            else
            {
                sc = (q[j + 4] & 0xF) | ((q[j - 4] >> 6) << 4);
                m = (q[j + 4] >> 4) | ((q[j] >> 6) << 4);
            }
        }

        // C's nearest_int(): round half to even via the 1.5*2^23 trick.
        static int NearestInt(float f)
        {
            float val = f + 12582912f;
            int i = *(int*)&val;
            return (i & 0x007fffff) - 0x00400000;
        }

        public static void QuantizeQ8_K(float[] x, Q8Activations q)
        {
            int nb = x.Length / QK_K;
            for (int b = 0; b < nb; b++)
            {
                int o = b * QK_K;
                float max = 0, amax = 0;
                for (int j = 0; j < QK_K; j++)
                {
                    float ax = Math.Abs(x[o + j]);
                    if (ax > amax) { amax = ax; max = x[o + j]; }
                }
                if (amax == 0)
                {
                    q.KD[b] = 0;
                    for (int j = 0; j < QK_K; j++) q.KQs[o + j] = 0;
                    for (int j = 0; j < QK_K / 16; j++) q.KBsums[b * 16 + j] = 0;
                    continue;
                }
                float iscale = -127f / max;
                for (int j = 0; j < QK_K; j++)
                {
                    int v = NearestInt(iscale * x[o + j]);
                    q.KQs[o + j] = (sbyte)Math.Min(127, v);
                }
                for (int j = 0; j < QK_K / 16; j++)
                {
                    int sum = 0;
                    for (int ii = 0; ii < 16; ii++) sum += q.KQs[o + j * 16 + ii];
                    q.KBsums[b * 16 + j] = (short)sum;
                }
                q.KD[b] = 1 / iscale;
            }
        }

        // Unpacks one k-quant block into 256 small integers (the "aux8" of the ggml dot products).
        // For Q4_K/Q5_K also returns 8 scales and 8 mins; for Q6_K 16 signed scales.
        static void UnpackK(GgmlType t, byte* blk, sbyte* a, int* scales, int* mins, out float d, out float dmin)
        {
            if (t == GgmlType.Q3_K)
            {
                UnpackQ3K(blk, a, scales, out d);
                dmin = 0;
                return;
            }
            if (t == GgmlType.Q6_K)
            {
                byte* ql = blk; byte* qh = blk + 128; sbyte* sc = (sbyte*)(blk + 192);
                for (int n = 0; n < QK_K / 128; n++)
                {
                    for (int l = 0; l < 32; l++)
                    {
                        a[l + 0] = (sbyte)(((ql[l + 0] & 0xF) | (((qh[l] >> 0) & 3) << 4)) - 32);
                        a[l + 32] = (sbyte)(((ql[l + 32] & 0xF) | (((qh[l] >> 2) & 3) << 4)) - 32);
                        a[l + 64] = (sbyte)(((ql[l + 0] >> 4) | (((qh[l] >> 4) & 3) << 4)) - 32);
                        a[l + 96] = (sbyte)(((ql[l + 32] >> 4) | (((qh[l] >> 6) & 3) << 4)) - 32);
                    }
                    a += 128; ql += 64; qh += 32;
                }
                for (int j = 0; j < 16; j++) scales[j] = sc[j];
                d = Half.ToFloat(*(ushort*)(blk + 208));
                dmin = 0;
                return;
            }

            d = Half.ToFloat(*(ushort*)blk);
            dmin = Half.ToFloat(*(ushort*)(blk + 2));
            byte* sc8 = blk + 4;
            for (int j = 0; j < 8; j++) { int s, m; ScaleMinK4(j, sc8, out s, out m); scales[j] = s; mins[j] = m; }

            if (t == GgmlType.Q4_K)
            {
                byte* q4 = blk + 16;
                for (int j = 0; j < QK_K / 64; j++)
                {
                    for (int l = 0; l < 32; l++) a[l] = (sbyte)(q4[l] & 0xF);
                    a += 32;
                    for (int l = 0; l < 32; l++) a[l] = (sbyte)(q4[l] >> 4);
                    a += 32; q4 += 32;
                }
            }
            else
            {
                byte* hm = blk + 16; byte* q4 = blk + 48;
                int m = 1;
                for (int j = 0; j < QK_K / 64; j++)
                {
                    for (int l = 0; l < 32; l++) a[l] = (sbyte)((q4[l] & 0xF) + ((hm[l] & m) != 0 ? 16 : 0));
                    a += 32; m <<= 1;
                    for (int l = 0; l < 32; l++) a[l] = (sbyte)((q4[l] >> 4) + ((hm[l] & m) != 0 ? 16 : 0));
                    a += 32; m <<= 1;
                    q4 += 32;
                }
            }
        }

        public static void DequantizeRowK(GgmlType t, byte* row, int n, float[] dst)
        {
            if (IsIQ(t)) throw new NotSupportedException("Dequantizing " + GgmlTypes.Name(t) + " rows is not implemented.");
            int nb = n / QK_K, bytes = KBlockBytes(t);
            sbyte* a = stackalloc sbyte[QK_K];
            int* scales = stackalloc int[16];
            int* mins = stackalloc int[8];
            for (int b = 0; b < nb; b++)
            {
                float d, dmin;
                UnpackK(t, row + (long)b * bytes, a, scales, mins, out d, out dmin);
                int o = b * QK_K;
                if (t == GgmlType.Q6_K || t == GgmlType.Q3_K)
                {
                    for (int e = 0; e < QK_K; e++) dst[o + e] = d * scales[e / 16] * a[e];
                }
                else
                {
                    for (int e = 0; e < QK_K; e++)
                    {
                        int sb = e / 32;
                        dst[o + e] = d * scales[sb] * a[e] - dmin * mins[sb];
                    }
                }
            }
        }

        // Row dot product, same integer math and float lane order as ggml_vec_dot_q*_K_q8_K_generic.
        static float DotK(GgmlType t, byte* row, int nb, int bytes, Q8Activations y, sbyte* a, int* scales, int* mins)
        {
            float* sums = stackalloc float[8];
            int* aux32 = stackalloc int[8];
            for (int l = 0; l < 8; l++) sums[l] = 0;
            float sumf = 0;
            bool sixteen = t == GgmlType.Q6_K || t == GgmlType.Q3_K;
            int sub = sixteen ? 16 : 32;   // elements per scale

            for (int i = 0; i < nb; i++)
            {
                float d, dmin;
                UnpackK(t, row + (long)i * bytes, a, scales, mins, out d, out dmin);
                for (int l = 0; l < 8; l++) aux32[l] = 0;

                int o = i * QK_K;
                for (int e = 0; e < QK_K; e += 8)
                {
                    int scale = scales[e / sub];
                    for (int l = 0; l < 8; l++) aux32[l] += scale * (short)(y.KQs[o + e + l] * a[e + l]);
                }
                float yd = y.KD[i];
                float dd = d * yd;
                for (int l = 0; l < 8; l++) sums[l] += dd * aux32[l];

                if (!sixteen)
                {
                    int sumi = 0;
                    for (int j = 0; j < QK_K / 16; j++) sumi += y.KBsums[i * 16 + j] * mins[j / 2];
                    float dm = dmin * yd;
                    sumf -= dm * sumi;
                }
            }
            for (int l = 0; l < 8; l++) sumf += sums[l];
            return sumf;
        }

        static void MatVecK(Weight w, float[] x, Q8Activations xq, float[] y)
        {
            if (xq.KD == null || w.Cols % QK_K != 0) throw new ArgumentException(w.Name + ": k-quant rows must be a multiple of 256.");
            QuantizeQ8_K(x, xq);
            GgmlType t = w.Type;
            int nb = w.Cols / QK_K, bytes = KBlockBytes(t);
            IntPtr basePtr = (IntPtr)w.Data;
            long rowBytes = w.RowBytes;
            Parallel.For(0, w.Rows,
                delegate { return new IntPtr[] { System.Runtime.InteropServices.Marshal.AllocHGlobal(QK_K + 16 * 4 + 8 * 4) }; },
                delegate(int r, ParallelLoopState st, IntPtr[] scratch)
                {
                    byte* s0 = (byte*)scratch[0];
                    byte* row = (byte*)basePtr + r * rowBytes;
                    y[r] = IsIQ(t) ? DotIQ(t, row, nb, xq) : DotK(t, row, nb, bytes, xq, (sbyte*)s0, (int*)(s0 + QK_K), (int*)(s0 + QK_K + 64));
                    return scratch;
                },
                delegate(IntPtr[] scratch) { System.Runtime.InteropServices.Marshal.FreeHGlobal(scratch[0]); });
        }
    }
}
