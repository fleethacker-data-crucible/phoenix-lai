// CPU reference ops added for Gemma models: Q4_0 and BF16 weights, ggml's GELU. C# 5 only.
// Numerics follow ggml-cpu (quants.c vec_dot_q4_0_q8_0, vec_dot_bf16, vec.h ggml_vec_gelu_f32; MIT, llama.cpp).
using System;
using System.Threading.Tasks;

namespace Phoenix
{
    public static unsafe partial class CpuRef
    {
        public const int BlockQ4_0Bytes = 18; // ggml_half d; uint8 qs[16] (low nibbles = elements 0..15, high = 16..31)

        // Q4_0 x Q8_0: per block sum((q - 8) * x) * d_w * d_x.
        static void MatVecQ4_0(Weight w, float[] x, Q8Activations xq, float[] y)
        {
            QuantizeQ8_0(x, xq);
            int nb = w.Cols / QK8_0;
            IntPtr basePtr = (IntPtr)w.Data;
            long rowBytes = w.RowBytes;
            ushort[] xd = xq.D;
            sbyte[] xqs = xq.Qs;
            Parallel.For(0, w.Rows, delegate(int r)
            {
                byte* row = (byte*)basePtr + r * rowBytes;
                float sum = 0;
                fixed (sbyte* qx0 = xqs)
                {
                    for (int b = 0; b < nb; b++)
                    {
                        byte* blk = row + b * BlockQ4_0Bytes;
                        byte* qs = blk + 2;
                        sbyte* qx = qx0 + b * QK8_0;
                        int isum = 0;
                        for (int j = 0; j < 16; j++)
                            isum += ((qs[j] & 0x0F) - 8) * qx[j] + ((qs[j] >> 4) - 8) * qx[j + 16];
                        sum += isum * (Half.ToFloat(*(ushort*)blk) * Half.ToFloat(xd[b]));
                    }
                }
                y[r] = sum;
            });
        }

        static void DequantizeRowQ4_0(byte* row, int n, float[] dst)
        {
            for (int b = 0; b < n / QK8_0; b++)
            {
                byte* blk = row + b * BlockQ4_0Bytes;
                float d = Half.ToFloat(*(ushort*)blk);
                byte* qs = blk + 2;
                for (int j = 0; j < 16; j++)
                {
                    dst[b * QK8_0 + j] = ((qs[j] & 0x0F) - 8) * d;
                    dst[b * QK8_0 + j + 16] = ((qs[j] >> 4) - 8) * d;
                }
            }
        }

        // bf16 <-> f32: bf16 is the top 16 bits of an f32; ggml rounds to nearest even (NaN kept quiet).
        public static float Bf16ToFloat(ushort h) { uint u = (uint)h << 16; return *(float*)&u; }

        public static ushort FloatToBf16(float f)
        {
            uint u = *(uint*)&f;
            if ((u & 0x7fffffff) > 0x7f800000) return (ushort)((u >> 16) | 64);
            return (ushort)((u + (0x7fff + ((u >> 16) & 1))) >> 16);
        }

        // BF16 weights: ggml converts the activations to bf16 and accumulates in f32 (vec_dot_bf16).
        static void MatVecBF16(Weight w, float[] x, float[] y)
        {
            float[] xb = new float[x.Length];
            for (int i = 0; i < x.Length; i++) xb[i] = Bf16ToFloat(FloatToBf16(x[i]));
            IntPtr basePtr = (IntPtr)w.Data;
            int cols = w.Cols;
            Parallel.For(0, w.Rows, delegate(int r)
            {
                ushort* row = (ushort*)basePtr + (long)r * cols;
                double sum = 0;
                for (int j = 0; j < cols; j++) sum += Bf16ToFloat(row[j]) * xb[j];
                y[r] = (float)sum;
            });
        }

        // GELU (tanh form) as ggml's CPU computes it: through an fp16 lookup table, i.e. fp16(gelu(fp16(x))),
        // exact outside [-10, 10].
        public static float Gelu(float x)
        {
            if (x <= -10f) return 0f;
            if (x >= 10f) return x;
            float h = Half.Round(x);
            float g = 0.5f * h * (1.0f + (float)Math.Tanh(0.79788456080286535587989211986876f * h * (1.0f + 0.044715f * h * h)));
            return Half.Round(g);
        }

        // RMS norm without a weight (ggml_rms_norm), in place over n values starting at off.
        public static void RmsNormPlain(float[] x, int off, int n, float eps)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += (double)x[off + i] * x[off + i];
            float scale = 1.0f / (float)Math.Sqrt((float)(sum / n) + eps);
            for (int i = 0; i < n; i++) x[off + i] *= scale;
        }

        // RMS norm with a weight, in place over n values starting at off (per-head q/k norms).
        public static void RmsNormInPlace(float[] x, int off, int n, float* weight, float eps)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += (double)x[off + i] * x[off + i];
            float scale = 1.0f / (float)Math.Sqrt((float)(sum / n) + eps);
            for (int i = 0; i < n; i++) x[off + i] = x[off + i] * scale * weight[i];
        }

        // Y[t] = W x[t] for a batch of tokens: each weight row is read once for all tokens (much faster than one MatVec
        // per token when processing a whole sequence). Q8_0 and Q4_0 weights use the same Q8_0 activation rounding as
        // MatVec, so results are identical; other types fall back to MatVec per token.
        public static void MatMul(Weight w, float[][] xs, float[][] ys)
        {
            int n = xs.Length;
            if (w.Type == GgmlType.BF16) { MatMulBF16(w, xs, ys); return; }
            if (w.Type != GgmlType.Q8_0 && w.Type != GgmlType.Q4_0)
            {
                Q8Activations q = new Q8Activations(Math.Max(w.Cols, 256));
                for (int t = 0; t < n; t++) MatVec(w, xs[t], q, ys[t]);
                return;
            }
            int nb = w.Cols / QK8_0;
            sbyte[] qs = new sbyte[(long)n * w.Cols];
            float[] ds = new float[n * nb];
            Parallel.For(0, n, delegate(int t)
            {
                float[] x = xs[t];
                for (int b = 0; b < nb; b++)
                {
                    int o = b * QK8_0;
                    float amax = 0;
                    for (int j = 0; j < QK8_0; j++) { float a = Math.Abs(x[o + j]); if (a > amax) amax = a; }
                    float d = amax / 127f, id = d != 0 ? 1f / d : 0f;
                    ds[t * nb + b] = Half.ToFloat(Half.FromFloat(d));
                    for (int j = 0; j < QK8_0; j++) qs[(long)t * w.Cols + o + j] = (sbyte)RoundAway(x[o + j] * id);
                }
            });
            IntPtr basePtr = (IntPtr)w.Data;
            long rowBytes = w.RowBytes;
            int cols = w.Cols;
            bool q4 = w.Type == GgmlType.Q4_0;
            Parallel.For(0, w.Rows, delegate(int r)
            {
                byte* row = (byte*)basePtr + r * rowBytes;
                sbyte* wq = stackalloc sbyte[cols];
                float* wd = stackalloc float[nb];
                for (int b = 0; b < nb; b++)   // unpack the row once to int8 + scale
                {
                    if (q4)
                    {
                        byte* blk = row + b * BlockQ4_0Bytes;
                        wd[b] = Half.ToFloat(*(ushort*)blk);
                        for (int j = 0; j < 16; j++) { wq[b * 32 + j] = (sbyte)((blk[2 + j] & 0x0F) - 8); wq[b * 32 + j + 16] = (sbyte)((blk[2 + j] >> 4) - 8); }
                    }
                    else
                    {
                        byte* blk = row + b * BlockQ8_0Bytes;
                        wd[b] = Half.ToFloat(*(ushort*)blk);
                        for (int j = 0; j < 32; j++) wq[b * 32 + j] = ((sbyte*)(blk + 2))[j];
                    }
                }
                fixed (sbyte* qx0 = qs)
                fixed (float* dx = ds)
                {
                    for (int t = 0; t < n; t++)
                    {
                        sbyte* qx = qx0 + (long)t * cols;
                        float sum = 0;
                        for (int b = 0; b < nb; b++)
                        {
                            sbyte* a = wq + b * 32; sbyte* c = qx + b * 32;
                            int isum = 0;
                            for (int j = 0; j < 32; j += 4) isum += a[j] * c[j] + a[j + 1] * c[j + 1] + a[j + 2] * c[j + 2] + a[j + 3] * c[j + 3];
                            sum += isum * (wd[b] * dx[t * nb + b]);
                        }
                        ys[t][r] = sum;
                    }
                }
            });
        }

        // BF16 batch: activations rounded to bf16 (as vec_dot_bf16), each weight row widened to f32 once.
        static void MatMulBF16(Weight w, float[][] xs, float[][] ys)
        {
            int n = xs.Length, cols = w.Cols;
            float[] xb = new float[(long)n * cols];
            for (int t = 0; t < n; t++) for (int j = 0; j < cols; j++) xb[(long)t * cols + j] = Bf16ToFloat(FloatToBf16(xs[t][j]));
            IntPtr basePtr = (IntPtr)w.Data;
            Parallel.For(0, w.Rows, delegate(int r)
            {
                ushort* row = (ushort*)basePtr + (long)r * cols;
                float* wf = stackalloc float[cols];
                for (int j = 0; j < cols; j++) wf[j] = Bf16ToFloat(row[j]);
                fixed (float* x0 = xb)
                {
                    for (int t = 0; t < n; t++)
                    {
                        float* x = x0 + (long)t * cols;
                        double sum = 0;
                        for (int j = 0; j < cols; j++) sum += wf[j] * x[j];
                        ys[t][r] = (float)sum;
                    }
                }
            });
        }

        // NeoX-style RoPE (Gemma, Qwen...): rotates the pairs (x[i], x[i + nRot/2]) of each head.
        public static void RopeNeox(float[] x, int nHeads, int headDim, int nRot, float[] cs)
        {
            int half = nRot / 2;
            for (int h = 0; h < nHeads; h++)
            {
                int o = h * headDim;
                for (int i = 0; i < half; i++)
                {
                    float c = cs[2 * i], s = cs[2 * i + 1];
                    float a = x[o + i], b = x[o + i + half];
                    x[o + i] = a * c - b * s;
                    x[o + i + half] = a * s + b * c;
                }
            }
        }
    }
}
