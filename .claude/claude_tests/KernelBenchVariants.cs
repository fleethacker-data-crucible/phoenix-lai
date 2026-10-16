// Kernel variants under test (tests\kbench.ps1). C# 5 only.
using System.IO;

namespace Phoenix.Tests
{
    public static class KernelBenchVariants
    {
        public static void Run(string modelsDir, TextWriter o)
        {
            string m8 = Path.Combine(modelsDir, "Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf");
            string up = "blk.{0}.ffn_up.weight";
            KernelBench.MatVecK(m8, up, 8, "q4_K scalar", delegate { return KernelsK.MatVec(GgmlType.Q4_K, true, false); }, 1, 0, o);
            KernelBench.MatVecK(m8, up, 8, "q4_K dot4", delegate { return KernelsK.MatVec(GgmlType.Q4_K, true, true); }, 1, 0, o);
            KernelBench.MatVecK(m8, "blk.{0}.ffn_down.weight", 2, "q6_K scalar", delegate { return KernelsK.MatVec(GgmlType.Q6_K, true, false); }, 1, KernelsK.Q6_KPaddedBytes, o);
            KernelBench.MatVecK(m8, "blk.{0}.ffn_down.weight", 2, "q6_K dot4", delegate { return KernelsK.MatVec(GgmlType.Q6_K, true, true); }, 1, KernelsK.Q6_KPaddedBytes, o);
        }
    }
}
