using System;

namespace ProcessManager.Utilities
{
    public static class AffinityHelper
    {
        public static bool IsCoreEnabled(IntPtr mask, int coreIndex)
        {
            long value = mask.ToInt64();
            return (value & (1L << coreIndex)) != 0;
        }

        public static IntPtr SetCoreMask(bool[] enabledCores)
        {
            long mask = 0;
            for (int i = 0; i < enabledCores.Length; i++)
            {
                if (enabledCores[i])
                    mask |= 1L << i;
            }
            return new IntPtr(mask);
        }

        public static string ToBinaryString(IntPtr mask)
        {
            return Convert.ToString(mask.ToInt64(), 2)
                .PadLeft(Environment.ProcessorCount, '0');
        }

        public static string ToHexString(IntPtr mask)
        {
            return $"0x{mask.ToInt64():X}";
        }
    }
}