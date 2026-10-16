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

        // File format: "PHXKV001", i32 pos0, count, layers, then per layer i32 src0, m, words, u32[words].
        public void Save(string path)
        {
            string tmp = path + ".tmp";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            using (System.IO.BinaryWriter w = new System.IO.BinaryWriter(System.IO.File.Create(tmp)))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("PHXKV001"));
                w.Write(Pos0); w.Write(Count); w.Write(Layers.Length);
                byte[] buf = new byte[1 << 20];
                foreach (Layer l in Layers)
                {
                    w.Write(l.Src0); w.Write(l.M); w.Write(l.Words.Length);
                    for (int i = 0; i < l.Words.Length; i += buf.Length / 4)
                    {
                        int n = System.Math.Min(buf.Length / 4, l.Words.Length - i);
                        System.Buffer.BlockCopy(l.Words, i * 4, buf, 0, n * 4);
                        w.Write(buf, 0, n * 4);
                    }
                }
            }
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            System.IO.File.Move(tmp, path);
        }

        public static KvSnapshot Load(string path)
        {
            using (System.IO.BinaryReader r = new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
            {
                if (System.Text.Encoding.ASCII.GetString(r.ReadBytes(8)) != "PHXKV001") return null;
                KvSnapshot s = new KvSnapshot();
                s.Pos0 = r.ReadInt32(); s.Count = r.ReadInt32();
                s.Layers = new Layer[r.ReadInt32()];
                for (int i = 0; i < s.Layers.Length; i++)
                {
                    Layer l = new Layer();
                    l.Src0 = r.ReadInt32(); l.M = r.ReadInt32();
                    int words = r.ReadInt32();
                    byte[] b = r.ReadBytes(words * 4);
                    l.Words = new uint[words];
                    System.Buffer.BlockCopy(b, 0, l.Words, 0, b.Length);
                    s.Layers[i] = l;
                }
                return s;
            }
        }
    }
}
