// Q3_K unpacking and IQ2_XXS / IQ2_S / IQ3_S dot products against Q8_K activations. C# 5 only.
// Ported from ggml_vec_dot_*_q8_K_generic (ggml-cpu/quants.c) and dequantize_row_q3_K (ggml-quants.c), MIT, llama.cpp.
using System;

namespace Phoenix
{
    public static unsafe partial class CpuRef
    {
        public const int BlockQ3_KBytes = 110;    // u8 hmask[32]; u8 qs[64]; u8 scales[12]; half d
        public const int BlockIQ2_XXSBytes = 66;  // half d; u16 qs[32]
        public const int BlockIQ2_SBytes = 82;    // half d; u8 qs[64] (32 grid + 32 signs); u8 qh[8]; u8 scales[8]
        public const int BlockIQ3_SBytes = 110;   // half d; u8 qs[64]; u8 qh[8]; u8 signs[32]; u8 scales[4]

        public static bool IsIQ(GgmlType t)
        {
            return t == GgmlType.IQ2_XXS || t == GgmlType.IQ2_S || t == GgmlType.IQ3_S;
        }

        // 6-bit Q3_K scale j (0..15), before the -32 offset.
        public static int ScaleQ3K(byte* s, int j)
        {
            int low = j < 8 ? s[j] & 0xF : s[j - 8] >> 4;
            int high = (s[8 + (j & 3)] >> (2 * (j >> 2))) & 3;
            return low | (high << 4);
        }

        // Q3_K into 256 signed values (aux8 order) and 16 scales (already minus 32).
        static void UnpackQ3K(byte* blk, sbyte* a, int* scales, out float d)
        {
            byte* hm = blk; byte* q3 = blk + 32;
            int m = 1;
            for (int j = 0; j < QK_K; j += 128)
            {
                for (int shift = 0; shift < 8; shift += 2)
                {
                    for (int l = 0; l < 32; l++) a[l] = (sbyte)(((q3[l] >> shift) & 3) - ((hm[l] & m) != 0 ? 0 : 4));
                    a += 32; m <<= 1;
                }
                q3 += 32;
            }
            for (int j = 0; j < 16; j++) scales[j] = ScaleQ3K(blk + 96, j) - 32;
            d = Half.ToFloat(*(ushort*)(blk + 108));
        }

        static float DotIQ2XXS(byte* row, int nb, Q8Activations y)
        {
            float sumf = 0f;
            byte[] kmask = IqTables.KmaskIq2xs, ksigns = IqTables.KsignsIq2xs;
            ulong[] grid = IqTables.Iq2xxsGrid;
            for (int i = 0; i < nb; i++)
            {
                byte* blk = row + (long)i * BlockIQ2_XXSBytes;
                float d = Half.ToFloat(*(ushort*)blk) * y.KD[i];
                uint* q2 = (uint*)(blk + 2);
                int q8 = i * QK_K;
                int bsum = 0;
                for (int ib32 = 0; ib32 < QK_K / 32; ib32++)
                {
                    uint a0 = q2[2 * ib32], a1 = q2[2 * ib32 + 1];
                    int ls = (int)(2 * (a1 >> 28) + 1);
                    int sumi = 0;
                    for (int l = 0; l < 4; l++)
                    {
                        ulong g = grid[(a0 >> (8 * l)) & 0xFF];
                        byte signs = ksigns[(a1 >> (7 * l)) & 127];
                        for (int j = 0; j < 8; j++)
                        {
                            int gv = (int)((g >> (8 * j)) & 0xFF);
                            sumi += gv * y.KQs[q8 + j] * ((signs & kmask[j]) != 0 ? -1 : 1);
                        }
                        q8 += 8;
                    }
                    bsum += sumi * ls;
                }
                sumf += d * bsum;
            }
            return 0.125f * sumf;
        }

        static float DotIQ2S(byte* row, int nb, Q8Activations y)
        {
            float sumf = 0;
            byte[] kmask = IqTables.KmaskIq2xs;
            ulong[] grid = IqTables.Iq2sGrid;
            for (int i = 0; i < nb; i++)
            {
                byte* blk = row + (long)i * BlockIQ2_SBytes;
                float d = Half.ToFloat(*(ushort*)blk) * y.KD[i];
                byte* qs = blk + 2; byte* signs = qs + QK_K / 8; byte* qh = blk + 66; byte* sc = blk + 74;
                int q8 = i * QK_K;
                int bsum = 0;
                for (int ib32 = 0; ib32 < QK_K / 32; ib32++)
                {
                    int ls1 = 1 + 2 * (sc[ib32] & 0xf);
                    int ls2 = 1 + 2 * (sc[ib32] >> 4);
                    int sumi1 = 0, sumi2 = 0;
                    for (int l = 0; l < 4; l++)
                    {
                        ulong g = grid[qs[l] | ((qh[ib32] << (8 - 2 * l)) & 0x300)];
                        int s = 0;
                        for (int j = 0; j < 8; j++)
                            s += y.KQs[q8 + j] * (int)((g >> (8 * j)) & 0xFF) * ((signs[l] & kmask[j]) != 0 ? -1 : 1);
                        if (l < 2) sumi1 += s; else sumi2 += s;
                        q8 += 8;
                    }
                    bsum += ls1 * sumi1 + ls2 * sumi2;
                    qs += 4;
                    signs += 4;
                }
                sumf += d * bsum;
            }
            return 0.125f * sumf;
        }

        static float DotIQ3S(byte* row, int nb, Q8Activations y)
        {
            float sumf = 0f;
            byte[] kmask = IqTables.KmaskIq2xs;
            uint[] grid = IqTables.Iq3sGrid;
            for (int i = 0; i < nb; i++)
            {
                byte* blk = row + (long)i * BlockIQ3_SBytes;
                float d = Half.ToFloat(*(ushort*)blk) * y.KD[i];
                byte* qs = blk + 2; byte* qh = blk + 66; byte* signs = blk + 74; byte* sc = blk + 106;
                int q8 = i * QK_K;
                int bsum = 0;
                for (int ib32 = 0; ib32 < QK_K / 32; ib32 += 2)
                {
                    int ls1 = 2 * (sc[ib32 / 2] & 0xf) + 1;
                    int ls2 = 2 * (sc[ib32 / 2] >> 4) + 1;
                    for (int half = 0; half < 2; half++)
                    {
                        int sumi = 0;
                        int h = qh[ib32 + half];
                        for (int l = 0; l < 4; l++)
                        {
                            uint g1 = grid[qs[2 * l + 0] | ((h << (8 - 2 * l)) & 256)];
                            uint g2 = grid[qs[2 * l + 1] | ((h << (7 - 2 * l)) & 256)];
                            for (int j = 0; j < 4; j++)
                            {
                                sumi += (int)((g1 >> (8 * j)) & 0xFF) * y.KQs[q8 + j] * ((signs[l] & kmask[j]) != 0 ? -1 : 1);
                                sumi += (int)((g2 >> (8 * j)) & 0xFF) * y.KQs[q8 + j + 4] * ((signs[l] & kmask[j + 4]) != 0 ? -1 : 1);
                            }
                            q8 += 8;
                        }
                        qs += 8;
                        signs += 4;
                        bsum += sumi * (half == 0 ? ls1 : ls2);
                    }
                }
                sumf += d * bsum;
            }
            return sumf;
        }

        static float DotIQ(GgmlType t, byte* row, int nb, Q8Activations y)
        {
            switch (t)
            {
                case GgmlType.IQ2_XXS: return DotIQ2XXS(row, nb, y);
                case GgmlType.IQ2_S: return DotIQ2S(row, nb, y);
                case GgmlType.IQ3_S: return DotIQ3S(row, nb, y);
            }
            throw new NotSupportedException(GgmlTypes.Name(t));
        }
    }
}
