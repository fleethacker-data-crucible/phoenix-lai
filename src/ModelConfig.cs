// Hyperparameters for Llama-style decoders (architectures "llama" and "mistral3") and their RoPE tables. C# 5 only.
// YaRN and attention-temperature math follow llama-context.cpp, llama-graph.cpp and ggml-cpu ops.cpp (MIT, llama.cpp).
using System;

namespace Phoenix
{
    public sealed unsafe class ModelConfig
    {
        public string Arch;
        public int NEmbd, NFf, NLayer, NHead, NHeadKv, HeadDim, NRot;
        public float Eps, RopeBase;

        // RoPE scaling (YaRN when ExtFactor != 0).
        public float FreqScale = 1f, ExtFactor, AttnFactor = 1f, BetaFast = 32f, BetaSlow = 1f;
        public int NCtxOrig;
        readonly float[] _corrDims = new float[2];

        // Llama 4 / Mistral 3 attention temperature (0 = off): q *= log(floor(pos / floor) + 1) * scale + 1
        public float AttnTempScale;
        public int AttnTempFloor;

        public static bool IsSupported(string arch) { return arch == "llama" || arch == "mistral3"; }

        public static ModelConfig FromGguf(GgufFile g)
        {
            ModelConfig c = new ModelConfig();
            string a = c.Arch = g.GetString("general.architecture");
            if (!IsSupported(a)) throw new NotSupportedException("Architecture '" + a + "' is not supported yet.");

            c.NEmbd = (int)g.GetInt(a + ".embedding_length");
            c.NFf = (int)g.GetInt(a + ".feed_forward_length");
            c.NLayer = (int)g.GetInt(a + ".block_count");
            c.NHead = (int)g.GetInt(a + ".attention.head_count");
            c.NHeadKv = g.Has(a + ".attention.head_count_kv") ? (int)g.GetInt(a + ".attention.head_count_kv") : c.NHead;
            c.HeadDim = g.Has(a + ".attention.key_length") ? (int)g.GetInt(a + ".attention.key_length") : c.NEmbd / c.NHead;
            c.NRot = g.Has(a + ".rope.dimension_count") ? (int)g.GetInt(a + ".rope.dimension_count") : c.HeadDim;
            c.Eps = g.GetFloat(a + ".attention.layer_norm_rms_epsilon");
            c.RopeBase = g.Has(a + ".rope.freq_base") ? g.GetFloat(a + ".rope.freq_base") : 10000f;

            int nCtxTrain = g.Has(a + ".context_length") ? (int)g.GetInt(a + ".context_length") : 4096;
            c.NCtxOrig = g.Has(a + ".rope.scaling.original_context_length") ? (int)g.GetInt(a + ".rope.scaling.original_context_length") : nCtxTrain;

            string scaling = g.Has(a + ".rope.scaling.type") ? g.GetString(a + ".rope.scaling.type") : "none";
            if (scaling == "yarn" || scaling == "linear")
            {
                float factor = g.Has(a + ".rope.scaling.factor") ? g.GetFloat(a + ".rope.scaling.factor") : 1f;
                if (factor > 0) c.FreqScale = 1f / factor;
            }
            else if (scaling != "none")
            {
                throw new NotSupportedException("RoPE scaling '" + scaling + "' is not supported yet.");
            }

            if (scaling == "yarn")
            {
                c.ExtFactor = 1f;
                if (g.Has(a + ".rope.scaling.yarn_beta_fast")) c.BetaFast = g.GetFloat(a + ".rope.scaling.yarn_beta_fast");
                if (g.Has(a + ".rope.scaling.yarn_beta_slow")) c.BetaSlow = g.GetFloat(a + ".rope.scaling.yarn_beta_slow");
                float logMul = g.Has(a + ".rope.scaling.yarn_log_multiplier") ? g.GetFloat(a + ".rope.scaling.yarn_log_multiplier") : 0f;
                float factor = 1f / c.FreqScale;
                // llama_context: yarn_attn_factor, then cancel the 1 + 0.1 ln(factor) that rope_yarn multiplies back in.
                c.AttnFactor = logMul != 0f ? MScale(factor, 1f) / MScale(factor, logMul) : MScale(factor, 1f);
                c.AttnFactor *= 1.0f / (1.0f + 0.1f * (float)Math.Log(factor));
            }
            if (g.Has(a + ".rope.scaling.attn_factor")) c.AttnFactor *= g.GetFloat(a + ".rope.scaling.attn_factor");

            CorrDims(c.NRot, c.NCtxOrig, c.RopeBase, c.BetaFast, c.BetaSlow, c._corrDims);

            if (g.Has(a + ".attention.temperature_scale"))
            {
                c.AttnTempScale = g.GetFloat(a + ".attention.temperature_scale");
                c.AttnTempFloor = c.NCtxOrig;
                if (c.AttnTempScale != 0 && c.AttnTempFloor <= 0) throw new NotSupportedException("Invalid attention temperature floor.");
            }
            return c;
        }

        static float MScale(float scale, float mscale)
        {
            return scale <= 1.0f ? 1.0f : (0.1f * mscale * (float)Math.Log(scale) + 1.0f);
        }

        static float CorrDim(int nDims, int nCtxOrig, float nRot, float b)
        {
            return nDims * (float)Math.Log(nCtxOrig / (nRot * 2 * (float)Math.PI)) / (2 * (float)Math.Log(b));
        }

        static void CorrDims(int nDims, int nCtxOrig, float freqBase, float betaFast, float betaSlow, float[] dims)
        {
            float start = (float)Math.Floor(CorrDim(nDims, nCtxOrig, betaFast, freqBase));
            float end = (float)Math.Ceiling(CorrDim(nDims, nCtxOrig, betaSlow, freqBase));
            dims[0] = Math.Max(0, start);
            dims[1] = Math.Min(nDims - 1, end);
        }

        // cos/sin for each rotated pair at position pos: cs[2i] = cos, cs[2i+1] = sin (ggml_rope_cache_init + rope_yarn).
        // freqFactors (rope_freqs.weight) divide theta.
        public void RopeTable(int pos, float* freqFactors, float[] cs)
        {
            float thetaScale = (float)Math.Pow(RopeBase, -2.0f / NRot);
            float theta = pos;
            for (int i0 = 0; i0 < NRot; i0 += 2)
            {
                float ff = freqFactors != null ? freqFactors[i0 / 2] : 1.0f;
                float thetaExtrap = theta / ff;
                float thetaInterp = FreqScale * thetaExtrap;
                float t = thetaInterp;
                float mscale = AttnFactor;
                if (ExtFactor != 0.0f)
                {
                    float y = (i0 / 2 - _corrDims[0]) / Math.Max(0.001f, _corrDims[1] - _corrDims[0]);
                    float rampMix = (1 - Math.Min(1, Math.Max(0, y))) * ExtFactor;
                    t = thetaInterp * (1 - rampMix) + thetaExtrap * rampMix;
                    mscale *= 1.0f + 0.1f * (float)Math.Log(1.0f / FreqScale);
                }
                cs[i0] = (float)Math.Cos(t) * mscale;
                cs[i0 + 1] = (float)Math.Sin(t) * mscale;
                theta *= thetaScale;
            }
        }

        // Factor applied to Q after RoPE (1 when attention temperature is off).
        public float AttnTemperature(int pos)
        {
            if (AttnTempScale == 0) return 1f;
            return (float)(Math.Log(Math.Floor((double)pos / AttnTempFloor) + 1.0) * AttnTempScale + 1.0);
        }
    }
}
