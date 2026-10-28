// Batched prefill matmuls on the GPU's matrix units (VK_KHR_cooperative_matrix, GpuDevice.CoopMat). C# 5 only.
// Y[t][r] = sum_k W[r][k] X[t][k] with Q4_0 weights W and f32 activations X: each 64-wide K step dequantizes a 64-row
// weight tile to f16 and converts a 64-token activation tile to f16 in shared memory; four subgroups (64 lanes each)
// multiply-accumulate 16x16x16 f16 tiles into f32 (each subgroup 32 rows x 32 tokens). The activations stay f16 instead
// of the Q8_0 quantization the integer kernels (KernelsPrefill) use, so the prefill is at least as precise as decode.
// Requirements (checked by the caller): rows % 64 == 0, columns % 64 == 0, subgroup size 64.
//   MatMul         dense: grid (rows / 64, ceil(T / 64)); Y rows of tokens >= T (up to the next 64) are scratch.
//   MatMul grouped  mixture of experts (KernelsMoe.Bucket lists): grid (rows / 64, ceil(maxTokens / 64), nExp); activation
//                  row a / actDiv of assignment a, output row a (with the fused gate|up split when half > 0).
//   GeluMul        g = gelu(g) * u in place (the f32 input of a down projection).
namespace Phoenix
{
    public static class KernelsCoop
    {
        public const uint Bm = 64, Bn = 64, Bk = 64, Sa = Bk + 8, Threads = 256;   // dense tile; Sa: shared row stride in halves (padded)

        // dense bindings: 0 wq u32, 1 wd u32 (fp16 scale pairs), 2 x f32 [T][K], 3 y f32 [T][rows]. push: rows, nb, T, stride
        // grouped bindings: + 4 y2 f32, 5 list u32, 6 cnt u32. push: rows, nb, stride, listStride, actDiv, half
        public static uint[] MatMul(bool grouped) { return MatMul(grouped, Bm, Bn, 2); }

        // Tile bm rows x bn tokens; the 4 subgroups form sr rows x (4 / sr) columns, each (bm / sr) x (bn / (4 / sr)).
        // Mixture-of-experts tiles are narrow in tokens (an expert sees ~T * nUsed / nExp of a chunk).
        public static uint[] MatMul(bool grouped, uint bm, uint bn, uint sr)
        {
            uint sc = 4 / sr, mi = bm / sr / 16, nj = bn / sc / 16;
            if (mi == 0 || nj == 0 || sr * sc != 4 || (bm * 4) % Threads != 0 || (bn * Bk) % Threads != 0) throw new System.ArgumentException("coop tile " + bm + "x" + bn + "/" + sr);
            KB k = new KB(Threads);
            SpirvBuilder s = k.S;
            s.Capability(Spv.CapabilityFloat16);
            s.Capability(Spv.CapabilityCooperativeMatrixKHR);
            s.Extension("SPV_KHR_cooperative_matrix");
            uint f16 = s.TypeFloat(16);
            uint cmA = s.TypeCoopMat(f16, 16, 16, Spv.MatrixAKHR), cmB = s.TypeCoopMat(f16, 16, 16, Spv.MatrixBKHR);
            uint cmC = s.TypeCoopMat(k.F32, 16, 16, Spv.MatrixAccumulatorKHR);
            KB.Buf wq = k.Buffer(0, k.U32, true), wd = k.Buffer(1, k.U32, true), x = k.Buffer(2, k.F32, true), y = k.Buffer(3, k.F32, false);
            KB.Buf y2 = null, list = null, cnt = null;
            if (grouped) { y2 = k.Buffer(4, k.F32, false); list = k.Buffer(5, k.U32, true); cnt = k.Buffer(6, k.U32, true); }
            k.Push(grouped ? 6 : 4);
            KB.Shared sA = k.SharedArray(f16, bm * Sa), sB = k.SharedArray(f16, bn * Sa);
            KB.Shared sC = grouped ? k.SharedArray(k.F32, 4 * 256) : null;
            KB.Shared arow = grouped ? k.SharedArray(k.U32, bn) : null, orow = grouped ? k.SharedArray(k.U32, bn) : null;
            k.Begin();
            uint b0v = k.Local(k.U32);
            uint[,] acc = new uint[mi, nj];
            for (int a = 0; a < mi; a++) for (int b = 0; b < nj; b++) acc[a, b] = k.Local(cmC);
            uint rows = k.PushU(0), nb = k.PushU(1);
            uint T, stride, ls = 0, actDiv = 0, half = 0, e = 0;
            if (grouped) { stride = k.PushU(2); ls = k.PushU(3); actDiv = k.PushU(4); half = k.PushU(5); e = k.GroupZ(); T = k.Ld(cnt, e); }
            else { T = k.PushU(2); stride = k.PushU(3); }
            uint lid = k.LocalId();
            uint sg = k.ScalarBuiltIn(Spv.BuiltInSubgroupId), lane = k.ScalarBuiltIn(Spv.BuiltInSubgroupLocalInvocationId);
            uint sgRow = k.IMul(k.UDiv(sg, k.C(sc)), k.C(bm / sr)), sgCol = k.IMul(k.UMod(sg, k.C(sc)), k.C(bn / sc));
            uint row0 = k.IMul(k.GroupX(), k.C(bm)), tok0 = k.IMul(k.GroupY(), k.C(bn));
            uint K = k.IMul(nb, k.C(32));
            uint zero = s.ConstComposite(cmC, k.CF(0));
            uint layoutRow = k.C(Spv.RowMajorKHR), layoutCol = k.C(Spv.ColumnMajorKHR), strideSa = k.C(Sa);
            uint ptrF16 = s.TypePointer(Spv.StorageWorkgroup, f16);

            k.If(k.ULt(tok0, T), delegate
            {
                if (grouped)
                {
                    k.If(k.ULt(lid, k.C(bn)), delegate
                    {
                        uint tk = k.IAdd(tok0, lid);
                        uint ok = k.ULt(tk, T);
                        uint a = k.Select(k.U32, ok, k.Ld(list, k.IAdd(k.IMul(e, ls), k.Select(k.U32, ok, tk, k.C(0)))), k.C(0));
                        k.St(arow, lid, k.UDiv(a, actDiv));
                        k.St(orow, lid, a);
                    });
                    k.Barrier();
                }
                for (int a = 0; a < mi; a++) for (int b = 0; b < nj; b++) k.Set(acc[a, b], zero);
                // weight half-blocks of the tile (4 per row: 2 blocks x 2 halves), bm * 4 / 256 per thread:
                // unit u -> row u / 4, block (u / 2) % 2, bytes 8 h .. 8 h + 7
                uint units = bm * 4 / Threads;
                uint[] wr = new uint[units], wb = new uint[units], wh = new uint[units], wRowBase = new uint[units];
                for (uint u = 0; u < units; u++)
                {
                    uint unit = k.IAdd(k.C(u * Threads), lid);
                    wr[u] = k.UDiv(unit, k.C(4)); wb[u] = k.UMod(k.UDiv(unit, k.C(2)), k.C(2)); wh[u] = k.UMod(unit, k.C(2));
                    uint wRow = k.IAdd(row0, wr[u]);
                    if (grouped) wRow = k.IAdd(k.IMul(e, rows), wRow);
                    wRowBase[u] = k.IMul(wRow, stride);
                }
                k.For(b0v, k.C(0), nb, k.C(Bk / 32), delegate
                {
                    uint b0 = k.Get(b0v, k.U32);
                    // weights -> f16 (q - 8) * d
                    for (uint u = 0; u < units; u++)
                    {
                        uint blk = k.IAdd(wRowBase[u], k.IAdd(b0, wb[u]));
                        uint dw = KernelsAttention.HalfAt(k, wd, blk);
                        uint aBase = k.IAdd(k.IMul(wr[u], strideSa), k.IAdd(k.IMul(wb[u], k.C(32)), k.IMul(wh[u], k.C(8))));
                        for (uint w = 0; w < 2; w++)
                        {
                            uint ww = k.Ld(wq, k.IAdd(k.IMul(blk, k.C(4)), k.IAdd(k.IMul(wh[u], k.C(2)), k.C(w))));
                            for (uint j = 0; j < 4; j++)
                            {
                                uint by = k.And(k.Shr(ww, k.C(8 * j)), k.C(0xFF));
                                uint lo = k.FMul(k.SToF(k.ISubS(k.AsI(k.And(by, k.C(0xF))), k.CI(8))), dw);
                                uint hi = k.FMul(k.SToF(k.ISubS(k.AsI(k.Shr(by, k.C(4))), k.CI(8))), dw);
                                uint col = k.C(w * 4 + j);
                                k.St(sA, k.IAdd(aBase, col), s.Value(Op.FConvert, f16, lo));
                                k.St(sA, k.IAdd(k.IAdd(aBase, col), k.C(16)), s.Value(Op.FConvert, f16, hi));
                            }
                        }
                    }
                    // activations -> f16, [token][k]
                    uint kOff = k.IMul(b0, k.C(32));
                    for (uint q = 0; q < bn * Bk / Threads; q++)
                    {
                        uint idx = k.IAdd(k.C(q * Threads), lid);
                        uint n = k.Shr(idx, k.C(6)), kk = k.And(idx, k.C(63));
                        uint tk = k.IAdd(tok0, n);
                        uint ok = k.ULt(tk, T);
                        uint src = grouped ? k.Ld(arow, n) : k.Select(k.U32, ok, tk, k.C(0));
                        uint v = k.Ld(x, k.IAdd(k.IMul(src, K), k.IAdd(kOff, kk)));
                        k.St(sB, k.IAdd(k.IMul(n, strideSa), kk), s.Value(Op.FConvert, f16, k.Select(k.F32, ok, v, k.CF(0))));
                    }
                    k.Barrier();
                    for (uint ks = 0; ks < Bk / 16; ks++)
                    {
                        uint[] am = new uint[mi], bmat = new uint[nj];
                        for (uint i = 0; i < mi; i++)
                        {
                            uint pa = s.AccessChain(ptrF16, sA.Var, k.IAdd(k.IMul(k.IAdd(sgRow, k.C(i * 16)), strideSa), k.C(ks * 16)));
                            am[i] = s.Value(Op.CooperativeMatrixLoadKHR, cmA, pa, layoutRow, strideSa);
                        }
                        for (uint j = 0; j < nj; j++)
                        {
                            uint pb = s.AccessChain(ptrF16, sB.Var, k.IAdd(k.IMul(k.IAdd(sgCol, k.C(j * 16)), strideSa), k.C(ks * 16)));
                            bmat[j] = s.Value(Op.CooperativeMatrixLoadKHR, cmB, pb, layoutCol, strideSa);
                        }
                        for (int i = 0; i < mi; i++)
                            for (int j = 0; j < nj; j++)
                                k.Set(acc[i, j], s.Value(Op.CooperativeMatrixMulAddKHR, cmC, am[i], bmat[j], k.Get(acc[i, j], cmC)));
                    }
                    k.Barrier();
                });
                for (uint i = 0; i < mi; i++)
                    for (uint j = 0; j < nj; j++)
                    {
                        uint r0 = k.IAdd(row0, k.IAdd(sgRow, k.C(i * 16)));
                        uint t0 = k.IAdd(sgCol, k.C(j * 16));   // within the tile
                        uint cv = k.Get(acc[i, j], cmC);
                        if (!grouped)
                        {
                            // element (row m, token n) -> y[(tok0 + t0 + n) * rows + r0 + m]: column-major with stride rows
                            uint py = s.AccessChain(y.Ptr, y.Var, k.C(0), k.IAdd(k.IMul(k.IAdd(tok0, t0), rows), r0));
                            s.Instr(Op.CooperativeMatrixStoreKHR, py, cv, layoutCol, rows);
                            continue;
                        }
                        uint pc = s.AccessChain(s.TypePointer(Spv.StorageWorkgroup, k.F32), sC.Var, k.IMul(sg, k.C(256)));
                        s.Instr(Op.CooperativeMatrixStoreKHR, pc, cv, layoutCol, k.C(16));
                        k.Barrier();
                        for (uint q = 0; q < 4; q++)
                        {
                            uint idx = k.IAdd(lane, k.C(q * 64));
                            uint n = k.Shr(idx, k.C(4)), m = k.And(idx, k.C(15));
                            uint tt = k.IAdd(t0, n);
                            uint r = k.IAdd(r0, m);
                            uint v = k.Ld(sC, k.IAdd(k.IMul(sg, k.C(256)), idx));
                            k.If(k.ULt(k.IAdd(tok0, tt), T), delegate
                            {
                                uint o = k.Ld(orow, tt);
                                uint split = s.Value(Op.INotEqual, k.Bool, half, k.C(0));
                                k.If(s.Value(Op.LogicalNot, k.Bool, split), delegate { k.St(y, k.IAdd(k.IMul(o, rows), r), v); });
                                k.If(split, delegate
                                {
                                    uint isGate = k.ULt(r, half);
                                    k.If(isGate, delegate { k.St(y, k.IAdd(k.IMul(o, half), r), v); });
                                    k.If(s.Value(Op.LogicalNot, k.Bool, isGate), delegate { k.St(y2, k.IAdd(k.IMul(o, half), s.Value(Op.ISub, k.U32, r, half)), v); });
                                });
                            });
                        }
                        k.Barrier();
                    }
            });
            return k.Finish();
        }

        // bindings: 0 g f32, 1 u f32. push: n. g[i] = gelu(g[i]) * u[i] (the decode path's GELU, KernelsQuant.Gelu)
        public static uint[] GeluMul()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf g = k.Buffer(0, k.F32, false), u = k.Buffer(1, k.F32, true);
            k.Push(1);
            k.Begin();
            uint i = k.GlobalId();
            k.If(k.ULt(i, k.PushU(0)), delegate { k.St(g, i, k.FMul(KernelsQuant.Gelu(k, k.Ld(g, i)), k.Ld(u, i))); });
            return k.Finish();
        }
    }
}
