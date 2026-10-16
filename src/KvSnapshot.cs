// Instant Recall: the KV cache of a token range, per layer, as saved by a model's CaptureKv and put back by SpliceKv.
// C# 5 only.
namespace Phoenix
{
    public sealed class KvSnapshot
    {
        public int Pos0, Count;          // the range it was computed at
        public Layer[] Layers;

        public sealed class Layer
        {
            public int Src0, M;          // positions Src0 .. Src0+M-1 (sliding-window layers keep only the last Ring)
            public uint[] Words;         // KernelsRecall snapshot layout (fp16 pairs)
        }

        public long Bytes
        {
            get { long b = 0; foreach (Layer l in Layers) b += (long)l.Words.Length * 4; return b; }
        }
    }
}
