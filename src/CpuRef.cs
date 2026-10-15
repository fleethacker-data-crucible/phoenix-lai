// CPU reference ops: ground truth for the GPU path and the fallback. C# 5 only.
// Numerics follow ggml's CPU backend (ggml-cpu ops.cpp, quants.c; MIT, llama.cpp).
using System;
using System.Threading.Tasks;

namespace Phoenix
{
    // A 2-D weight matrix: Rows output features, each a contiguous row of Cols inputs.
    public sealed unsafe class Weight
    {
        public string Name;
        public GgmlType Type;
        public int Rows, Cols;
        public byte* Data;
        public long RowBytes;

        public Weight(GgufTensor t)
        {
            Name = t.Name;
            Type = t.Type;
            Cols = (int)t.Dims[0];
            Rows = t.Dims.Length > 1 ? (int)t.Dims[1] : 1;
            Data = t.Data;
            RowBytes = t.SizeBytes / Rows;
        }
    }

    public static unsafe class CpuRef
    {
        public const int QK8_0 = 32;
        public const int BlockQ8_0Bytes = 34; // ggml_half d; int8 qs[32]

        // Activations quantized to Q8_0, as ggml does before a q8_0 dot product (quantize_row_q8_0_ref).
        public sealed class Q8Activations
        {
            public ushort[] D;
            public sbyte[] Qs;
            public Q8Activations(int n) { D = new ushort[n / QK8_0]; Qs = new sbyte[n]; }
        }

        public static void QuantizeQ8_0(float[] x, Q8Activations q)
        {
            int nb = x.Length / QK8_0;
            for (int b = 0; b < nb; b++)
            {
                int o = b * QK8_0;
                float amax = 0;
                for (int j = 0; j < QK8_0; j++) { float a = Math.Abs(x[o + j]); if (a > amax) amax = a; }
                float d = amax / 127f;
                float id = d != 0 ? 1f / d : 0f;
                q.D[b] = Half.FromFloat(d);
                for (int j = 0; j < QK8_0; j++) q.Qs[o + j] = (sbyte)RoundAway(x[o + j] * id);
            }
        }

        static float RoundAway(float v)
        {
            return (float)Math.Round(v, MidpointRounding.AwayFromZero); // C roundf
        }

        // y[r] = dot(W[r], x) for every row r.
        public static void MatVec(Weight w, float[] x, Q8Activations xq, float[] y)
        {
            if (x.Length != w.Cols || y.Length < w.Rows) throw new ArgumentException("MatVec shape mismatch for " + w.Name);
            switch (w.Type)
            {
                case GgmlType.Q8_0: MatVecQ8_0(w, x, xq, y); return;
                case GgmlType.F32: MatVecF32(w, x, y); return;
                case GgmlType.F16: MatVecF16(w, x, y); return;
            }
            throw new NotSupportedException("MatVec for " + GgmlTypes.Name(w.Type) + " (" + w.Name + ") is not implemented yet.");
        }

        static void MatVecQ8_0(Weight w, float[] x, Q8Activations xq, float[] y)
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
                        byte* blk = row + b * BlockQ8_0Bytes;
                        sbyte* qw = (sbyte*)(blk + 2);
                        sbyte* qx = qx0 + b * QK8_0;
                        int isum = 0;
                        for (int j = 0; j < QK8_0; j += 4)
                            isum += qw[j] * qx[j] + qw[j + 1] * qx[j + 1] + qw[j + 2] * qx[j + 2] + qw[j + 3] * qx[j + 3];
                        sum += isum * (Half.ToFloat(*(ushort*)blk) * Half.ToFloat(xd[b]));
                    }
                }
                y[r] = sum;
            });
        }

        static void MatVecF32(Weight w, float[] x, float[] y)
        {
            IntPtr basePtr = (IntPtr)w.Data;
            int cols = w.Cols;
            Parallel.For(0, w.Rows, delegate(int r)
            {
                float* row = (float*)basePtr + (long)r * cols;
                double sum = 0;
                for (int j = 0; j < cols; j++) sum += row[j] * x[j];
                y[r] = (float)sum;
            });
        }

        static void MatVecF16(Weight w, float[] x, float[] y)
        {
            IntPtr basePtr = (IntPtr)w.Data;
            int cols = w.Cols;
            Parallel.For(0, w.Rows, delegate(int r)
            {
                ushort* row = (ushort*)basePtr + (long)r * cols;
                double sum = 0;
                for (int j = 0; j < cols; j++) sum += Half.ToFloat(row[j]) * x[j];
                y[r] = (float)sum;
            });
        }

        // Dequantize one row (e.g. an embedding lookup).
        public static void GetRow(Weight w, int r, float[] dst)
        {
            byte* row = w.Data + r * w.RowBytes;
            switch (w.Type)
            {
                case GgmlType.Q8_0:
                    for (int b = 0; b < w.Cols / QK8_0; b++)
                    {
                        byte* blk = row + b * BlockQ8_0Bytes;
                        float d = Half.ToFloat(*(ushort*)blk);
                        sbyte* q = (sbyte*)(blk + 2);
                        for (int j = 0; j < QK8_0; j++) dst[b * QK8_0 + j] = q[j] * d;
                    }
                    return;
                case GgmlType.F32:
                    for (int j = 0; j < w.Cols; j++) dst[j] = ((float*)row)[j];
                    return;
                case GgmlType.F16:
                    for (int j = 0; j < w.Cols; j++) dst[j] = Half.ToFloat(((ushort*)row)[j]);
                    return;
            }
            throw new NotSupportedException("GetRow for " + GgmlTypes.Name(w.Type) + " is not implemented yet.");
        }

        // y = x / sqrt(mean(x^2) + eps) * weight   (ggml_rms_norm then ggml_mul)
        public static void RmsNorm(float[] x, float* weight, float eps, float[] y)
        {
            double sum = 0;
            for (int i = 0; i < x.Length; i++) sum += (double)x[i] * x[i];
            float mean = (float)(sum / x.Length);
            float scale = 1.0f / (float)Math.Sqrt(mean + eps);
            for (int i = 0; i < x.Length; i++) y[i] = x[i] * scale * weight[i];
        }

        // cos/sin for each rotated pair at position pos: cs[2i] = cos, cs[2i+1] = sin.
        // freqFactors (rope_freqs.weight) divide theta, as in ggml's rope with freq_factors.
        public static void RopeTable(int pos, int nRot, float freqBase, float* freqFactors, float[] cs)
        {
            float thetaScale = (float)Math.Pow(freqBase, -2.0 / nRot);
            float theta = pos;
            for (int i0 = 0; i0 < nRot; i0 += 2)
            {
                float ff = freqFactors != null ? freqFactors[i0 / 2] : 1.0f;
                float t = theta / ff;
                cs[i0] = (float)Math.Cos(t);
                cs[i0 + 1] = (float)Math.Sin(t);
                theta *= thetaScale;
            }
        }

        // Llama "normal" RoPE: rotates adjacent pairs (x[2i], x[2i+1]) in each head.
        public static void Rope(float[] x, int nHeads, int headDim, int nRot, float[] cs)
        {
            for (int h = 0; h < nHeads; h++)
            {
                int o = h * headDim;
                for (int i0 = 0; i0 < nRot; i0 += 2)
                {
                    float c = cs[i0], s = cs[i0 + 1];
                    float a = x[o + i0], b = x[o + i0 + 1];
                    x[o + i0] = a * c - b * s;
                    x[o + i0 + 1] = a * s + b * c;
                }
            }
        }

        public static float Silu(float v)
        {
            return v / (1.0f + (float)Math.Exp(-v));
        }

        public static int ArgMax(float[] v)
        {
            int best = 0;
            for (int i = 1; i < v.Length; i++) if (v[i] > v[best]) best = i;
            return best;
        }
    }
}
