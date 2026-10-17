// Gemma 4 vision (no audio): image -> soft tokens in the language model's embedding space. C# 5 only.
// Two projector kinds, from the model's mmproj GGUF (clip.vision.projector_type):
//  - gemma4v  (E2B, E4B): 16-layer ViT on 16 px patches (RMSNorm, clipped linears, 2-D axial RoPE theta 100, scale 1,
//    plain RMSNorm on V, GELU-tanh MLP), 3x3 average pooling, * sqrt(768), RMSNorm, linear projection.
//  - gemma4uv (12B "unified", encoder-free): 48 px patches -> LayerNorm -> Linear(+bias) -> LayerNorm -> + x/y position
//    rows -> LayerNorm -> RMSNorm -> linear projection.
// Follows Hugging Face transformers (models/gemma4, models/gemma4_unified) and llama.cpp tools/mtmd (gemma4v.cpp,
// gemma4uv.cpp). Where they differ, transformers wins: the ViT MLP uses gelu_pytorch_tanh (llama.cpp falls back to
// gelu_quick because the mmproj has no use_gelu key; VisionEncoder.GeluQuick reproduces that for comparisons).
// Preprocessing is the Hugging Face Gemma 4 image processor: aspect-preserving Pillow bicubic resize to the largest
// size that fits max_soft_tokens * 9 patches with sides a multiple of 48, pixels / 255 in [0, 1].
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Phoenix
{
    // Image soft tokens inside a prompt's token list: negative ids, -(image * Span + i) for soft token i of image number
    // `image` (numbered by ChatEngine per distinct image), so the same picture always gets the same ids (prompt-cache
    // reuse works) and different pictures never match. The model looks the embedding rows up by id.
    public static class ImageTokens
    {
        public const int Span = 4096;
        public static int Make(int image, int i) { return -(image * Span + i); }
        public static int Image(int tok) { return tok < 0 ? (-tok) / Span : 0; }
        public static int Index(int tok) { return (-tok) % Span; }
        static bool Same(IList<int> t, int a, int b) { return t[a] < 0 && t[b] < 0 && Image(t[a]) == Image(t[b]); }

        // Length of a chunk starting at i (at most n, before `end`) that never cuts an image in two: an image that starts
        // at i is taken whole (it may exceed n), one that would not fit ends the chunk before it.
        public static int Fit(IList<int> t, int i, int n, int end)
        {
            if (i < end && t[i] < 0)
            {
                int j = i + 1;
                while (j < end && Same(t, i, j)) j++;
                return j - i;
            }
            int lim = Math.Min(i + n, end);
            for (int k = i; k < lim; k++)
            {
                if (t[k] >= 0 || (k > i && Same(t, k - 1, k))) continue;
                int j = k + 1;
                while (j < end && Same(t, k, j)) j++;
                if (j > lim) return k - i;
                k = j - 1;
            }
            return lim - i;
        }

        // The first image run within [i, i + n): positions [b0, b1), or 0, 0.
        public static void FirstRun(IList<int> t, int i, int n, out int b0, out int b1)
        {
            b0 = b1 = 0;
            for (int k = i; k < i + n; k++)
                if (t[k] < 0)
                {
                    int j = k + 1;
                    while (j < i + n && Same(t, k, j)) j++;
                    b0 = k; b1 = j;
                    return;
                }
        }

        // Moves a cache-reuse point back to the start of an image it would land inside.
        public static int ReuseStart(IList<int> t, int reuse)
        {
            while (reuse > 0 && reuse < t.Count && Same(t, reuse - 1, reuse)) reuse--;
            return reuse;
        }
    }

    public sealed class RgbImage
    {
        public int W, H;
        public byte[] Px;   // RGB, row-major, 3 bytes per pixel

        public RgbImage(int w, int h) { W = w; H = h; Px = new byte[w * h * 3]; }

        // PNG / JPEG / GIF / BMP through GDI+ (part of Windows). Alpha is dropped, not blended (PIL convert("RGB")).
        public static RgbImage Decode(byte[] file)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream(file))
            using (Bitmap src = new Bitmap(ms))
            {
                if (src.Width < 1 || src.Height < 1 || (long)src.Width * src.Height > 64L << 20) throw new NotSupportedException("Image size not supported.");
                RgbImage img = new RgbImage(src.Width, src.Height);
                BitmapData bd = src.LockBits(new Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[src.Width * 4];
                    for (int y = 0; y < src.Height; y++)
                    {
                        Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, row.Length);
                        int o = y * src.Width * 3;
                        for (int x = 0; x < src.Width; x++) { img.Px[o + x * 3] = row[x * 4 + 2]; img.Px[o + x * 3 + 1] = row[x * 4 + 1]; img.Px[o + x * 3 + 2] = row[x * 4]; }
                    }
                }
                finally { src.UnlockBits(bd); }
                return img;
            }
        }

        // transformers get_aspect_ratio_preserving_size: the largest size within maxPatches patches of `patch` px whose
        // sides are multiples of k * patch.
        public static void TargetSize(int height, int width, int patch, int maxPatches, int k, out int th, out int tw)
        {
            double totalPx = (double)height * width, targetPx = (double)maxPatches * patch * patch;
            double factor = Math.Sqrt(targetPx / totalPx);
            int side = k * patch;
            th = (int)Math.Floor(factor * height / side) * side;
            tw = (int)Math.Floor(factor * width / side) * side;
            if (th == 0 && tw == 0) throw new NotSupportedException("Image is too small or too thin.");
            int maxSide = (maxPatches / (k * k)) * side;
            if (th == 0) { th = side; tw = Math.Min((int)Math.Floor((double)width / height) * side, maxSide); }
            else if (tw == 0) { tw = side; th = Math.Min((int)Math.Floor((double)height / width) * side, maxSide); }
        }

        // Pillow's bicubic resample (a = -0.5, antialiased when shrinking, 22-bit fixed point, 8-bit between passes),
        // ported from llama.cpp mtmd-image.cpp resize_pillow, which follows Pillow's Resample.c.
        public RgbImage Resize(int tw, int th)
        {
            if (tw == W && th == H) { RgbImage c = new RgbImage(W, H); Buffer.BlockCopy(Px, 0, c.Px, 0, Px.Length); return c; }
            byte[] cur = Px;
            int cw = W, ch = H;
            if (tw != W)
            {
                int[] b; int[] kk; int ks = Weights(W, tw, out b, out kk);
                byte[] o = new byte[tw * ch * 3];
                Parallel.For(0, ch, delegate(int yy)
                {
                    for (int xx = 0; xx < tw; xx++)
                    {
                        int xmin = b[xx * 2], xcnt = b[xx * 2 + 1];
                        int s0 = 1 << (Prec - 1), s1 = s0, s2 = s0;
                        int p = (yy * cw + xmin) * 3;
                        for (int x = 0; x < xcnt; x++, p += 3) { int wv = kk[xx * ks + x]; s0 += cur[p] * wv; s1 += cur[p + 1] * wv; s2 += cur[p + 2] * wv; }
                        int q = (yy * tw + xx) * 3;
                        o[q] = Clip8(s0 >> Prec); o[q + 1] = Clip8(s1 >> Prec); o[q + 2] = Clip8(s2 >> Prec);
                    }
                });
                cur = o; cw = tw;
            }
            if (th != H)
            {
                int[] b; int[] kk; int ks = Weights(H, th, out b, out kk);
                byte[] o = new byte[cw * th * 3];
                int rowElems = cw * 3;
                byte[] src = cur;
                Parallel.For(0, th, delegate(int yy)
                {
                    int ymin = b[yy * 2], ycnt = b[yy * 2 + 1];
                    for (int i = 0; i < rowElems; i++)
                    {
                        int acc = 1 << (Prec - 1);
                        for (int y = 0; y < ycnt; y++) acc += src[(ymin + y) * rowElems + i] * kk[yy * ks + y];
                        o[yy * rowElems + i] = Clip8(acc >> Prec);
                    }
                });
                cur = o; ch = th;
            }
            RgbImage r = new RgbImage(tw, th);
            r.Px = cur;
            return r;
        }

        const int Prec = 22;
        static byte Clip8(int v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }

        static double Cubic(double x)
        {
            const double a = -0.5;
            if (x < 0) x = -x;
            if (x < 1.0) return ((a + 2.0) * x - (a + 3.0)) * x * x + 1;
            if (x < 2.0) return (((x - 5) * x + 8) * x - 4) * a;
            return 0.0;
        }

        static int Weights(int inSize, int outSize, out int[] bounds, out int[] weights)
        {
            double scale = (double)inSize / outSize, filterscale = scale < 1.0 ? 1.0 : scale;
            double support = 2.0 * filterscale;
            int ksize = (int)Math.Ceiling(support) * 2 + 1;
            double[] pre = new double[outSize * ksize];
            bounds = new int[outSize * 2];
            for (int xx = 0; xx < outSize; xx++)
            {
                double center = (xx + 0.5) * scale, ww = 0, ss = 1.0 / filterscale;
                int xmin = (int)(center - support + 0.5); if (xmin < 0) xmin = 0;
                int xmax = (int)(center + support + 0.5); if (xmax > inSize) xmax = inSize;
                xmax -= xmin;
                int x;
                for (x = 0; x < xmax; x++) { double w = Cubic((x + xmin - center + 0.5) * ss); pre[xx * ksize + x] = w; ww += w; }
                for (x = 0; x < xmax; x++) if (ww != 0.0) pre[xx * ksize + x] /= ww;
                for (; x < ksize; x++) pre[xx * ksize + x] = 0;
                bounds[xx * 2] = xmin; bounds[xx * 2 + 1] = xmax;
            }
            weights = new int[outSize * ksize];
            double fxp = Math.Pow(2, Prec);
            for (int i = 0; i < weights.Length; i++) weights[i] = (int)(pre[i] * fxp + (pre[i] < 0 ? -0.5 : 0.5));
            return ksize;
        }
    }

    // The vision tower of one mmproj file. Encode() returns the image's soft tokens, [n][NEmbdText], ready to be fed to
    // the language model in place of token embeddings (no sqrt(d) scale).
    public sealed unsafe class VisionEncoder : IDisposable
    {
        public readonly string Kind;            // "gemma4v" or "gemma4uv"
        public readonly int NEmbdText, Patch, Merge;
        public static int MaxSoftTokens = 280;  // transformers' default; 70 / 140 / 280 (an image must fit one prefill chunk)
        public static bool GeluQuick;           // llama.cpp's activation for gemma4v (tests only)
        readonly GgufFile _g;
        readonly GpuDevice _gpu;                // null: CPU
        readonly float _eps;
        // gemma4v
        readonly int _d, _nLayer, _nHead, _hd, _ff;
        readonly VLayer[] _layers;
        // shared
        readonly Weight _proj, _patch;
        readonly float* _pos;                   // [2][posSize][d]
        readonly int _posSize;
        // gemma4uv
        readonly float* _patchBias, _ln1w, _ln1b, _ln2w, _ln2b, _ln3w, _ln3b;
        VisionGpu _vg;

        sealed class VLayer
        {
            public float* Ln1, Ln2, QNorm, KNorm, PostAttn, PostFfn;
            public Weight Q, K, V, O, Gate, Up, Down;
            public float[] Clamp = new float[7 * 4];   // per weight (Q K V O Gate Up Down): inMin, inMax, outMin, outMax
        }

        public VisionEncoder(string mmprojPath, GpuDevice gpu)
        {
            _g = GgufFile.Open(mmprojPath);
            try
            {
                if (_g.GetString("general.architecture") != "clip" || !_g.Has("clip.has_vision_encoder") || !Convert.ToBoolean(_g.Metadata["clip.has_vision_encoder"].Value))
                    throw new NotSupportedException("Not a vision projector file.");
                Kind = _g.GetString("clip.vision.projector_type");
                if (Kind != "gemma4v" && Kind != "gemma4uv") throw new NotSupportedException("Vision projector '" + Kind + "' is not supported (Gemma 4 only).");
                NEmbdText = (int)_g.GetInt("clip.vision.projection_dim");
                _eps = _g.GetFloat("clip.vision.attention.layer_norm_epsilon");
                _d = (int)_g.GetInt("clip.vision.embedding_length");
                Patch = (int)_g.GetInt("clip.vision.patch_size");
                Merge = 3;   // pooling_kernel_size (llama.cpp and transformers default; the GGUF does not store it)
                _proj = W("mm.input_projection.weight");
                _patch = W("v.patch_embd.weight");
                GgufTensor pe = T("v.patch_embd.weight");
                if (pe.Dims.Length == 4)   // conv kernel [kw, kh, 3, d] -> a [d][3 * kh * kw] matrix, columns (channel, row, col)
                {
                    _patch.Cols = (int)(pe.Dims[0] * pe.Dims[1] * pe.Dims[2]); _patch.Rows = (int)pe.Dims[3];
                    _patch.RowBytes = pe.SizeBytes / _patch.Rows;
                }
                GgufTensor pt = T("v.position_embd.weight");
                if (pt.Type != GgmlType.F32) throw new NotSupportedException("position table must be f32");
                _pos = (float*)pt.Data; _posSize = (int)pt.Dims[1];
                if (Kind == "gemma4v")
                {
                    _nLayer = (int)_g.GetInt("clip.vision.block_count");
                    _nHead = (int)_g.GetInt("clip.vision.attention.head_count");
                    _ff = (int)_g.GetInt("clip.vision.feed_forward_length");
                    _hd = _d / _nHead;
                    _layers = new VLayer[_nLayer];
                    string[] names = { "attn_q", "attn_k", "attn_v", "attn_out", "ffn_gate", "ffn_up", "ffn_down" };
                    for (int i = 0; i < _nLayer; i++)
                    {
                        string p = "v.blk." + i + ".";
                        VLayer l = new VLayer();
                        l.Ln1 = F(p + "ln1.weight"); l.Ln2 = F(p + "ln2.weight");
                        l.QNorm = F(p + "attn_q_norm.weight"); l.KNorm = F(p + "attn_k_norm.weight");
                        l.PostAttn = F(p + "attn_post_norm.weight"); l.PostFfn = F(p + "ffn_post_norm.weight");
                        l.Q = W(p + "attn_q.weight"); l.K = W(p + "attn_k.weight"); l.V = W(p + "attn_v.weight"); l.O = W(p + "attn_out.weight");
                        l.Gate = W(p + "ffn_gate.weight"); l.Up = W(p + "ffn_up.weight"); l.Down = W(p + "ffn_down.weight");
                        for (int j = 0; j < names.Length; j++)
                        {
                            l.Clamp[j * 4] = Scalar(p + names[j] + ".input_min", float.NegativeInfinity);
                            l.Clamp[j * 4 + 1] = Scalar(p + names[j] + ".input_max", float.PositiveInfinity);
                            l.Clamp[j * 4 + 2] = Scalar(p + names[j] + ".output_min", float.NegativeInfinity);
                            l.Clamp[j * 4 + 3] = Scalar(p + names[j] + ".output_max", float.PositiveInfinity);
                        }
                        _layers[i] = l;
                    }
                }
                else
                {
                    Patch *= Merge;   // the unified embedder reads 48 px patches directly
                    _patchBias = F("v.patch_embd.bias");
                    _ln1w = F("v.patch_norm.1.weight"); _ln1b = F("v.patch_norm.1.bias");
                    _ln2w = F("v.patch_norm.2.weight"); _ln2b = F("v.patch_norm.2.bias");
                    _ln3w = F("v.patch_norm.3.weight"); _ln3b = F("v.patch_norm.3.bias");
                }
                _gpu = gpu;
                if (gpu != null) _vg = new VisionGpu(this, gpu);
            }
            catch { Dispose(); throw; }
        }

        GgufTensor T(string name)
        {
            GgufTensor t;
            if (!_g.TensorsByName.TryGetValue(name, out t)) throw new KeyNotFoundException("Missing tensor " + name);
            return t;
        }
        Weight W(string name) { return new Weight(T(name)); }
        float* F(string name) { GgufTensor t = T(name); if (t.Type != GgmlType.F32) throw new NotSupportedException(name + " must be f32"); return (float*)t.Data; }
        float Scalar(string name, float dflt) { GgufTensor t; return _g.TensorsByName.TryGetValue(name, out t) ? *(float*)t.Data : dflt; }

        public string Backend { get { return _vg != null ? "GPU" : "CPU"; } }

        // GPU memory the vision tower takes: its weights in fp16 (the position table stays on the CPU; audio is never loaded),
        // plus ~64 MB of activations for a 280-token image.
        public static long VramBytes(string mmprojPath)
        {
            long b = 64L << 20;
            using (GgufFile g = GgufFile.Open(mmprojPath))
                foreach (GgufTensor t in g.Tensors)
                    if ((t.Name.StartsWith("v.") || t.Name == "mm.input_projection.weight") && t.Name != "v.position_embd.weight" && t.Dims.Length >= 2)
                    {
                        b += t.ElementCount * 2;
                    }
            return b;
        }

        // The vision projector for a Gemma 4 model in the same folder: a *mmproj*.gguf with a Gemma 4 vision projector
        // whose output width is the model's embedding width (E2B 1536, E4B 2560, 12B 3840, ...); among several, the one
        // whose name shares the longest start with the model's ("_" prefixes ignored). null: none.
        public static string FindFor(string modelPath, int nEmbd)
        {
            string dir = System.IO.Path.GetDirectoryName(modelPath), best = null;
            string name = System.IO.Path.GetFileName(modelPath).TrimStart('_').ToLowerInvariant();
            int bestScore = -1;
            foreach (string f in System.IO.Directory.GetFiles(dir, "*.gguf"))
            {
                string fn = System.IO.Path.GetFileName(f).ToLowerInvariant();
                if (fn.IndexOf("mmproj", StringComparison.Ordinal) < 0) continue;
                try
                {
                    using (GgufFile g = GgufFile.Open(f))
                    {
                        if (g.GetString("general.architecture") != "clip" || !g.Has("clip.vision.projector_type") || !g.Has("clip.vision.projection_dim")) continue;
                        string pt = g.GetString("clip.vision.projector_type");
                        if ((pt != "gemma4v" && pt != "gemma4uv") || (int)g.GetInt("clip.vision.projection_dim") != nEmbd) continue;
                    }
                }
                catch { continue; }
                string t = fn.TrimStart('_');
                int s = 0;
                while (s < t.Length && s < name.Length && t[s] == name[s]) s++;
                if (s > bestScore) { bestScore = s; best = f; }
            }
            return best;
        }

        // Soft-token count for an image of this size (after the resize).
        public int Tokens(int w, int h, out int tw, out int th)
        {
            RgbImage.TargetSize(h, w, Patch / (Kind == "gemma4uv" ? Merge : 1), MaxSoftTokens * Merge * Merge, Merge, out th, out tw);
            return (tw / (Kind == "gemma4uv" ? Patch : Patch * Merge)) * (th / (Kind == "gemma4uv" ? Patch : Patch * Merge));
        }

        public float[][] Encode(RgbImage img)
        {
            int tw, th;
            Tokens(img.W, img.H, out tw, out th);
            RgbImage r = img.Resize(tw, th);
            return Kind == "gemma4v" ? EncodeV(r) : EncodeUV(r);
        }

        // ---------------- gemma4v (E2B / E4B) ----------------

        // Patch rows in the conv weight's column order (channel, row, col), pixels scaled to [-1, 1].
        float[][] PatchRows(RgbImage r, int p, out int nx, out int ny, bool signed)
        {
            nx = r.W / p; ny = r.H / p;
            int n = nx * ny, cols = 3 * p * p, nxx = nx;
            float[][] rows = new float[n][];
            for (int i = 0; i < n; i++) rows[i] = new float[cols];
            Parallel.For(0, n, delegate(int i)
            {
                int px0 = (i % nxx) * p, py0 = (i / nxx) * p;
                float[] row = rows[i];
                for (int c = 0; c < 3; c++)
                    for (int y = 0; y < p; y++)
                        for (int x = 0; x < p; x++)
                        {
                            float v = r.Px[((py0 + y) * r.W + px0 + x) * 3 + c] / 255f;
                            row[c * p * p + y * p + x] = signed ? 2f * v - 1f : v;
                        }
            });
            return rows;
        }

        // x/y position rows: pos[0][col] + pos[1][row]
        void AddPos(float[][] x, int nx)
        {
            int d = x[0].Length;
            for (int i = 0; i < x.Length; i++)
            {
                float* px = _pos + (long)(i % nx) * d, py = _pos + ((long)_posSize + i / nx) * d;
                for (int j = 0; j < d; j++) x[i][j] += px[j] + py[j];
            }
        }

        float[][] EncodeV(RgbImage r)
        {
            int nx, ny;
            float[][] patches = PatchRows(r, Patch, out nx, out ny, true);
            if (nx > _posSize || ny > _posSize) throw new NotSupportedException("Image too large for the position table.");
            float[][] x = _vg != null ? _vg.Run(patches, nx, ny) : TowerCpu(patches, nx, ny);
            // 3x3 average pooling by position, * sqrt(d), RMSNorm (no weight), projection
            int k = Merge, ox = nx / k, oy = ny / k, d = _d;
            float[][] pooled = Rows(ox * oy, d);
            float sc = (float)Math.Sqrt(d) / (k * k);
            for (int i = 0; i < x.Length; i++)
            {
                int o = (i / nx) / k * ox + (i % nx) / k;
                for (int j = 0; j < d; j++) pooled[o][j] += x[i][j];
            }
            foreach (float[] p in pooled) { for (int j = 0; j < d; j++) p[j] *= sc; RmsPlain(p, _eps); }
            return Project(pooled);
        }

        float[][] Project(float[][] rows)
        {
            if (_vg != null) return _vg.Project(rows);
            float[][] o = Rows(rows.Length, NEmbdText);
            CpuRef.MatMul(_proj, rows, o);
            return o;
        }

        float[][] TowerCpu(float[][] patches, int nx, int ny)
        {
            int n = patches.Length, d = _d;
            float[][] x = Rows(n, d);
            CpuRef.MatMul(_patch, patches, x);
            AddPos(x, nx);
            float[][] h = Rows(n, d), q = Rows(n, d), kk = Rows(n, d), v = Rows(n, d), a = Rows(n, d), t = Rows(n, d), g = Rows(n, _ff), u = Rows(n, _ff);
            float[] cs = RopeTable(nx, ny);
            foreach (VLayer l in _layers)
            {
                for (int i = 0; i < n; i++) RmsW(x[i], l.Ln1, _eps, h[i]);
                ClampMm(l.Q, h, q, l.Clamp, 0); ClampMm(l.K, h, kk, l.Clamp, 1); ClampMm(l.V, h, v, l.Clamp, 2);
                int hd = _hd, nh = _nHead;
                Parallel.For(0, n, delegate(int i)
                {
                    for (int hh = 0; hh < nh; hh++)
                    {
                        CpuRef.RmsNormInPlace(q[i], hh * hd, hd, l.QNorm, _eps);
                        CpuRef.RmsNormInPlace(kk[i], hh * hd, hd, l.KNorm, _eps);
                        CpuRef.RmsNormPlain(v[i], hh * hd, hd, _eps);
                        Rope2(q[i], hh * hd, cs, i); Rope2(kk[i], hh * hd, cs, i);
                    }
                });
                Parallel.For(0, n, delegate(int i)
                {
                    float[] sc = new float[n];
                    for (int hh = 0; hh < nh; hh++)
                    {
                        int o = hh * hd;
                        float max = float.NegativeInfinity;
                        for (int j = 0; j < n; j++) { float s = 0; for (int e = 0; e < hd; e++) s += q[i][o + e] * kk[j][o + e]; sc[j] = s; if (s > max) max = s; }
                        double sum = 0;
                        for (int j = 0; j < n; j++) { sc[j] = (float)Math.Exp(sc[j] - max); sum += sc[j]; }
                        float inv = (float)(1.0 / sum);
                        for (int e = 0; e < hd; e++) a[i][o + e] = 0;
                        for (int j = 0; j < n; j++) { float pj = sc[j] * inv; for (int e = 0; e < hd; e++) a[i][o + e] += pj * v[j][o + e]; }
                    }
                });
                ClampMm(l.O, a, t, l.Clamp, 3);
                for (int i = 0; i < n; i++) { RmsW(t[i], l.PostAttn, _eps, h[i]); for (int j = 0; j < d; j++) x[i][j] += h[i][j]; }
                for (int i = 0; i < n; i++) RmsW(x[i], l.Ln2, _eps, h[i]);
                ClampMm(l.Gate, h, g, l.Clamp, 4); ClampMm(l.Up, h, u, l.Clamp, 5);
                int ff = _ff;
                Parallel.For(0, n, delegate(int i) { for (int j = 0; j < ff; j++) g[i][j] = Act(g[i][j]) * u[i][j]; });
                ClampMm(l.Down, g, t, l.Clamp, 6);
                for (int i = 0; i < n; i++) { RmsW(t[i], l.PostFfn, _eps, h[i]); for (int j = 0; j < d; j++) x[i][j] += h[i][j]; }
            }
            return x;
        }

        static float Act(float x) { return GeluQuick ? x / (1f + (float)Math.Exp(-1.702f * x)) : CpuRef.Gelu(x); }

        static void ClampMm(Weight w, float[][] x, float[][] y, float[] cl, int j)
        {
            float lo = cl[j * 4], hi = cl[j * 4 + 1], olo = cl[j * 4 + 2], ohi = cl[j * 4 + 3];
            float[][] xc = new float[x.Length][];
            for (int i = 0; i < x.Length; i++) { xc[i] = new float[x[i].Length]; for (int e = 0; e < xc[i].Length; e++) xc[i][e] = Math.Min(Math.Max(x[i][e], lo), hi); }
            CpuRef.MatMul(w, xc, y);
            foreach (float[] r in y) for (int e = 0; e < r.Length; e++) r[e] = Math.Min(Math.Max(r[e], olo), ohi);
        }

        // Axial 2-D rope: per patch, 2 * (hd / 4) (cos, sin) pairs: the first hd/4 for x (rotating dims [0, hd/2)),
        // the next hd/4 for y (dims [hd/2, hd)); NeoX pairing within each half; inv_freq = theta^(-2i / (hd/2)), theta 100.
        internal float[] RopeTable(int nx, int ny)
        {
            int n = nx * ny, q = _hd / 4;
            float[] cs = new float[n * q * 4];
            for (int i = 0; i < n; i++)
                for (int part = 0; part < 2; part++)
                {
                    int pos = part == 0 ? i % nx : i / nx;
                    for (int j = 0; j < q; j++)
                    {
                        float inv = (float)(1.0 / Math.Pow(100.0, 2.0 * j / (_hd / 2)));
                        float ang = pos * inv;
                        int o = ((i * 2 + part) * q + j) * 2;
                        cs[o] = (float)Math.Cos(ang); cs[o + 1] = (float)Math.Sin(ang);
                    }
                }
            return cs;
        }

        void Rope2(float[] x, int off, float[] cs, int i)
        {
            int q = _hd / 4;
            for (int part = 0; part < 2; part++)
            {
                int b = off + part * 2 * q;
                for (int j = 0; j < q; j++)
                {
                    int o = ((i * 2 + part) * q + j) * 2;
                    float c = cs[o], s = cs[o + 1], a0 = x[b + j], a1 = x[b + j + q];
                    x[b + j] = a0 * c - a1 * s;
                    x[b + j + q] = a0 * s + a1 * c;
                }
            }
        }

        // ---------------- gemma4uv (12B) ----------------

        float[][] EncodeUV(RgbImage r)
        {
            int nx, ny;
            float[][] rows = PatchRows(r, Patch, out nx, out ny, false);
            if (nx > _posSize || ny > _posSize) throw new NotSupportedException("Image too large for the position table.");
            int d = _d;
            foreach (float[] row in rows) LayerNorm(row, _ln1w, _ln1b, 1e-5f);
            float[][] x = _vg != null ? _vg.PatchDense(rows) : null;
            if (x == null) { x = Rows(rows.Length, d); CpuRef.MatMul(_patch, rows, x); }
            for (int i = 0; i < x.Length; i++) { for (int j = 0; j < d; j++) x[i][j] += _patchBias[j]; LayerNorm(x[i], _ln2w, _ln2b, 1e-5f); }
            AddPos(x, nx);
            foreach (float[] row in x) { LayerNorm(row, _ln3w, _ln3b, 1e-5f); RmsPlain(row, _eps); }
            return Project(x);
        }

        // ---------------- helpers ----------------

        internal static float[][] Rows(int n, int m) { float[][] r = new float[n][]; for (int i = 0; i < n; i++) r[i] = new float[m]; return r; }

        static void RmsW(float[] x, float* w, float eps, float[] y)
        {
            double ss = 0; for (int i = 0; i < x.Length; i++) ss += (double)x[i] * x[i];
            float s = (float)(1.0 / Math.Sqrt(ss / x.Length + eps));
            for (int i = 0; i < x.Length; i++) y[i] = x[i] * s * w[i];
        }

        static void RmsPlain(float[] x, float eps)
        {
            double ss = 0; for (int i = 0; i < x.Length; i++) ss += (double)x[i] * x[i];
            float s = (float)(1.0 / Math.Sqrt(ss / x.Length + eps));
            for (int i = 0; i < x.Length; i++) x[i] *= s;
        }

        static void LayerNorm(float[] x, float* w, float* b, float eps)
        {
            double m = 0; for (int i = 0; i < x.Length; i++) m += x[i]; m /= x.Length;
            double v = 0; for (int i = 0; i < x.Length; i++) { double t = x[i] - m; v += t * t; } v /= x.Length;
            float s = (float)(1.0 / Math.Sqrt(v + eps)), mf = (float)m;
            for (int i = 0; i < x.Length; i++) x[i] = (x[i] - mf) * s * w[i] + b[i];
        }

        public void Dispose()
        {
            if (_vg != null) { _vg.Dispose(); _vg = null; }
            if (_g != null) _g.Dispose();
        }

        // ---------------- GPU path (KernelsBatch: f16 weights, f32 activations) ----------------
        sealed class VisionGpu : IDisposable
        {
            readonly VisionEncoder _e;
            readonly GpuDevice _gpu;
            readonly List<IDisposable> _owned = new List<IDisposable>();
            readonly GpuKernel _kMm, _kNorm, _kHeadNorm, _kRope, _kAttn, _kGelu, _kGeluQ, _kClamp;
            readonly uint _attnTq;
            readonly GpuBuffer _wPatch, _wProj;
            readonly LayerBufs[] _l;

            sealed class LayerBufs { public GpuBuffer Ln1, Ln2, QNorm, KNorm, PostAttn, PostFfn, Q, K, V, O, Gate, Up, Down; }

            public VisionGpu(VisionEncoder e, GpuDevice gpu)
            {
                _e = e; _gpu = gpu;
                try
                {
                    _kMm = Kernel(KernelsBatch.MatMulF16(), 3, 3, "v mm f16");
                    _kClamp = Kernel(KernelsGemma.Clamp(), 2, 3, "v clamp");
                    _wPatch = W16(e._patch);
                    _wProj = W16(e._proj);
                    if (e.Kind == "gemma4v")
                    {
                        _kNorm = Kernel(KernelsBatch.RowNorm(), 3, 4, "v row norm");
                        _kHeadNorm = Kernel(KernelsGemma.HeadNorm(), 3, 3, "v head norm");
                        _kRope = Kernel(KernelsBatch.RopeNeoxRows(), 2, 4, "v rope");
                        _kGelu = Kernel(KernelsBatch.GeluMulRows(), 2, 4, "v gelu*u");
                        _kGeluQ = Kernel(KernelsGemma.GeluQuickMul(), 2, 1, "v gelu_quick*u");
                        uint tq, kc;
                        KernelsBatch.AttnTile((uint)e._hd, gpu.MaxSharedMemory, out tq, out kc);
                        if ((uint)e._hd % (2 * kc) != 0) throw new NotSupportedException("vision head size");
                        _kAttn = Kernel(KernelsBatch.AttentionBi((uint)e._hd, tq, kc), 4, 5, "v attention");
                        _attnTq = tq;
                        _l = new LayerBufs[e._nLayer];
                        for (int i = 0; i < e._nLayer; i++)
                        {
                            VLayer s = e._layers[i];
                            LayerBufs b = new LayerBufs();
                            b.Ln1 = F32(s.Ln1, e._d); b.Ln2 = F32(s.Ln2, e._d); b.QNorm = F32(s.QNorm, e._hd); b.KNorm = F32(s.KNorm, e._hd);
                            b.PostAttn = F32(s.PostAttn, e._d); b.PostFfn = F32(s.PostFfn, e._d);
                            b.Q = W16(s.Q); b.K = W16(s.K); b.V = W16(s.V); b.O = W16(s.O); b.Gate = W16(s.Gate); b.Up = W16(s.Up); b.Down = W16(s.Down);
                            _l[i] = b;
                        }
                    }
                }
                catch { Dispose(); throw; }
            }

            GpuKernel Kernel(uint[] spirv, int bindings, int pushWords, string name)
            {
                GpuKernel k = _gpu.CreateKernel(spirv, bindings, (uint)pushWords * 4, 4096);
                k.Name = name;
                _owned.Add(k);
                return k;
            }

            GpuBuffer Buf(ulong bytes, BufferKind kind) { GpuBuffer b = _gpu.CreateBuffer(Math.Max(bytes, 4), kind); _owned.Add(b); return b; }

            GpuBuffer F32(float* p, int n) { GpuBuffer b = Buf((ulong)n * 4, BufferKind.Device); _gpu.Upload(b, 0, (IntPtr)p, (ulong)n * 4); return b; }

            GpuBuffer W16(Weight w)
            {
                if (w.Cols % (int)KernelsBatch.MmK != 0) throw new NotSupportedException(w.Name + ": columns must be a multiple of " + KernelsBatch.MmK);
                GpuBuffer b = Buf((ulong)w.Rows * (ulong)w.Cols * 2, BufferKind.Device);
                float[] row = new float[w.Cols];
                int rowsPer = Math.Max(1, (8 << 20) / (w.Cols * 2));
                ushort[] chunk = new ushort[(long)rowsPer * w.Cols];
                for (int r0 = 0; r0 < w.Rows; r0 += rowsPer)
                {
                    int nr = Math.Min(rowsPer, w.Rows - r0);
                    for (int r = 0; r < nr; r++)
                    {
                        CpuRef.GetRow(w, r0 + r, row);
                        for (int j = 0; j < w.Cols; j++) chunk[(long)r * w.Cols + j] = Half.FromFloat(row[j]);
                    }
                    fixed (ushort* p = chunk) _gpu.Upload(b, (ulong)r0 * (ulong)w.Cols * 2, (IntPtr)p, (ulong)nr * (ulong)w.Cols * 2);
                }
                return b;
            }

            static uint Bits(float f) { return *(uint*)&f; }
            static uint Groups(long n, uint size) { return (uint)((n + size - 1) / size); }

            void Mm(GpuBuffer w, GpuBuffer x, GpuBuffer y, int rows, int cols, int n)
            {
                _gpu.Record(_kMm, new GpuBuffer[] { w, x, y }, new uint[] { (uint)rows, (uint)cols, (uint)n }, Groups(rows, KernelsBatch.MmRows), Groups(n, KernelsBatch.MmTokens), 1);
            }

            void Clamp(GpuBuffer x, GpuBuffer y, long n, float lo, float hi)
            {
                _gpu.Record(_kClamp, new GpuBuffer[] { x, y }, new uint[] { (uint)n, Bits(lo), Bits(hi) }, Groups(n, Kernels.Wg), 1, 1);
            }

            // y = clamp(W clamp(x)) for a clipped linear (tmp holds the clamped input)
            void ClampMm(GpuBuffer w, GpuBuffer x, GpuBuffer tmp, GpuBuffer y, int rows, int cols, int n, float[] cl, int j)
            {
                Clamp(x, tmp, (long)n * cols, cl[j * 4], cl[j * 4 + 1]);
                Mm(w, tmp, y, rows, cols, n);
                Clamp(y, y, (long)n * rows, cl[j * 4 + 2], cl[j * 4 + 3]);
            }

            GpuBuffer Upload(float[][] rows)
            {
                int n = rows.Length, m = rows[0].Length;
                float[] flat = new float[(long)n * m];
                for (int i = 0; i < n; i++) Array.Copy(rows[i], 0, flat, (long)i * m, m);
                GpuBuffer b = Buf((ulong)flat.Length * 4, BufferKind.Upload);
                b.Upload(flat, 0);
                return b;
            }

            float[][] Download(GpuBuffer b, int n, int m)
            {
                float[] flat = new float[(long)n * m];
                b.Download(flat, 0);
                float[][] r = Rows(n, m);
                for (int i = 0; i < n; i++) Array.Copy(flat, (long)i * m, r[i], 0, m);
                return r;
            }

            void Free(params GpuBuffer[] bs) { foreach (GpuBuffer b in bs) if (b != null) { _owned.Remove(b); b.Dispose(); } }

            // x[n][rows] = W x[n][cols] for host rows, returned on the host
            float[][] MmHost(GpuBuffer w, float[][] x, int rows)
            {
                int n = x.Length, cols = x[0].Length;
                GpuBuffer xu = Upload(x), y = Buf((ulong)n * (ulong)rows * 4, BufferKind.Readback);
                try
                {
                    lock (GpuGate.Lock) { _gpu.Begin(); Mm(w, xu, y, rows, cols, n); _gpu.SubmitAndWait(); }
                    return Download(y, n, rows);
                }
                finally { Free(xu, y); }
            }

            public float[][] Project(float[][] rows) { return MmHost(_wProj, rows, _e.NEmbdText); }
            public float[][] PatchDense(float[][] rows) { return MmHost(_wPatch, rows, _e._d); }

            // The ViT: patches [n][3*p*p] -> hidden [n][d]. One submission per layer (each ~tens of ms).
            public float[][] Run(float[][] patches, int nx, int ny)
            {
                VisionEncoder e = _e;
                int n = patches.Length, d = e._d, ff = e._ff, hd = e._hd, nh = e._nHead, pc = patches[0].Length;
                float[][] pos = Rows(n, d);
                e.AddPos(pos, nx);
                // rope tables in RopeNeoxRows layout, one "token" per (patch, head, half): [n * nh * 2][hd / 4 pairs]
                float[] cs0 = e.RopeTable(nx, ny), cs = new float[(long)n * nh * 2 * (hd / 2)];
                int qq = hd / 4;
                for (int i = 0; i < n; i++)
                    for (int h = 0; h < nh; h++)
                        for (int part = 0; part < 2; part++)
                            Array.Copy(cs0, ((i * 2 + part) * qq) * 2, cs, (((long)i * nh + h) * 2 + part) * qq * 2, qq * 2);
                GpuBuffer pu = Upload(patches), posu = Upload(pos), csu = Buf((ulong)cs.Length * 4, BufferKind.Upload);
                csu.Upload(cs, 0);
                ulong N = (ulong)n;
                GpuBuffer xr = Buf(N * (ulong)d * 4, BufferKind.Readback);
                GpuBuffer x = Buf(N * (ulong)d * 4, BufferKind.Device), h1 = Buf(N * (ulong)d * 4, BufferKind.Device), tmp = Buf(N * (ulong)Math.Max(d, ff) * 4, BufferKind.Device);
                GpuBuffer q = Buf(N * (ulong)d * 4, BufferKind.Device), k = Buf(N * (ulong)d * 4, BufferKind.Device), v = Buf(N * (ulong)d * 4, BufferKind.Device), a = Buf(N * (ulong)d * 4, BufferKind.Device);
                GpuBuffer g = Buf(N * (ulong)ff * 4, BufferKind.Device), u = Buf(N * (ulong)ff * 4, BufferKind.Device);
                uint eps = Bits(e._eps);
                try
                {
                    lock (GpuGate.Lock)
                    {
                        _gpu.Begin();
                        Mm(_wPatch, pu, x, d, pc, n);
                        _gpu.Record(KAdd(), new GpuBuffer[] { x, posu }, new uint[] { (uint)(n * d) }, Groups((long)n * d, Kernels.Wg), 1, 1);
                        _gpu.SubmitAndWait();
                        for (int il = 0; il < e._nLayer; il++)
                        {
                            LayerBufs b = _l[il];
                            float[] cl = e._layers[il].Clamp;
                            _gpu.Begin();
                            _gpu.Record(_kNorm, new GpuBuffer[] { x, h1, b.Ln1 }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)n, 1, 1);
                            ClampMm(b.Q, h1, tmp, q, d, d, n, cl, 0);
                            ClampMm(b.K, h1, tmp, k, d, d, n, cl, 1);
                            ClampMm(b.V, h1, tmp, v, d, d, n, cl, 2);
                            _gpu.Record(_kHeadNorm, new GpuBuffer[] { q, b.QNorm, q }, new uint[] { (uint)hd, eps, 1 }, (uint)(n * nh), 1, 1, false);
                            _gpu.Record(_kHeadNorm, new GpuBuffer[] { k, b.KNorm, k }, new uint[] { (uint)hd, eps, 1 }, (uint)(n * nh), 1, 1, false);
                            _gpu.Record(_kHeadNorm, new GpuBuffer[] { v, b.KNorm, v }, new uint[] { (uint)hd, eps, 0 }, (uint)(n * nh), 1, 1);
                            // each (patch, head, half) is a 32-wide "head" with its own table row
                            uint rows2 = (uint)(n * nh * 2), half = (uint)(hd / 4);
                            _gpu.Record(_kRope, new GpuBuffer[] { q, csu }, new uint[] { 1, (uint)(hd / 2), half, rows2 }, Groups((long)rows2 * half, Kernels.Wg), 1, 1, false);
                            _gpu.Record(_kRope, new GpuBuffer[] { k, csu }, new uint[] { 1, (uint)(hd / 2), half, rows2 }, Groups((long)rows2 * half, Kernels.Wg), 1, 1);
                            _gpu.Record(_kAttn, new GpuBuffer[] { q, k, v, a }, new uint[] { (uint)n, (uint)hd, (uint)nh, (uint)nh, 0 }, Groups(n, _attnTq), (uint)nh, 1);
                            ClampMm(b.O, a, tmp, h1, d, d, n, cl, 3);
                            _gpu.Record(_kNorm, new GpuBuffer[] { x, h1, b.PostAttn }, new uint[] { (uint)d, eps, 1, Bits(1f) }, (uint)n, 1, 1);
                            _gpu.Record(_kNorm, new GpuBuffer[] { x, h1, b.Ln2 }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)n, 1, 1);
                            ClampMm(b.Gate, h1, tmp, g, ff, d, n, cl, 4);
                            ClampMm(b.Up, h1, tmp, u, ff, d, n, cl, 5);
                            if (GeluQuick) _gpu.Record(_kGeluQ, new GpuBuffer[] { g, u }, new uint[] { (uint)(n * ff) }, Groups((long)n * ff, Kernels.Wg), 1, 1);
                            else _gpu.Record(_kGelu, new GpuBuffer[] { g, u }, new uint[] { (uint)ff, (uint)n, (uint)ff, 0 }, Groups((long)n * ff, Kernels.Wg), 1, 1);
                            ClampMm(b.Down, g, tmp, h1, d, ff, n, cl, 6);
                            _gpu.Record(_kNorm, new GpuBuffer[] { x, h1, b.PostFfn }, new uint[] { (uint)d, eps, 1, Bits(1f) }, (uint)n, 1, 1);
                            _gpu.SubmitAndWait();
                        }
                        _gpu.Begin();
                        _gpu.Record(KCopy(), new GpuBuffer[] { x, xr }, new uint[] { (uint)(n * d) }, Groups((long)n * d, Kernels.Wg), 1, 1);
                        _gpu.SubmitAndWait();
                    }
                    return Download(xr, n, d);
                }
                finally { Free(pu, posu, csu, xr, x, h1, tmp, q, k, v, a, g, u); }
            }

            GpuKernel _kAdd, _kCopy;
            GpuKernel KCopy() { if (_kCopy == null) _kCopy = Kernel(KernelsBatch.Copy(), 2, 1, "v copy"); return _kCopy; }
            GpuKernel KAdd() { if (_kAdd == null) _kAdd = Kernel(Kernels.Add(), 2, 1, "v add"); return _kAdd; }

            public void Dispose()
            {
                for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
                _owned.Clear();
            }
        }
    }
}
