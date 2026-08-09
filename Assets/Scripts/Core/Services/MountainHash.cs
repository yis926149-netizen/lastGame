/// <summary>
/// 程序化山体使用的无状态确定性 hash。所有键均一次性混合，不依赖遍历顺序或 Random 状态。
/// </summary>
public static class MountainHash
{
    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;

    public static uint Hash(int seed, params int[] keys)
    {
        unchecked
        {
            uint hash = Mix(FnvOffset, seed);
            if (keys != null)
            {
                for (int i = 0; i < keys.Length; i++)
                    hash = Mix(hash, keys[i]);
            }

            // Avalanche the FNV result so adjacent integer coordinates do not retain visible correlation.
            hash ^= hash >> 16;
            hash *= 0x7feb352du;
            hash ^= hash >> 15;
            hash *= 0x846ca68bu;
            hash ^= hash >> 16;
            return hash;
        }
    }

    public static float Hash01(int seed, params int[] keys)
    {
        // Use 24 bits so conversion to float is stable and never reaches 1.
        return (Hash(seed, keys) & 0x00ffffffu) / 16777216f;
    }

    public static float HashSigned(int seed, params int[] keys)
    {
        return Hash01(seed, keys) * 2f - 1f;
    }

    public static uint EdgeKey(int generateOrderA, int generateOrderB, int seed)
    {
        if (generateOrderA > generateOrderB)
            Swap(ref generateOrderA, ref generateOrderB);
        return Hash(seed, generateOrderA, generateOrderB);
    }

    public static uint CornerKey(int generateOrderA, int generateOrderB, int generateOrderC, int seed)
    {
        if (generateOrderA > generateOrderB) Swap(ref generateOrderA, ref generateOrderB);
        if (generateOrderB > generateOrderC) Swap(ref generateOrderB, ref generateOrderC);
        if (generateOrderA > generateOrderB) Swap(ref generateOrderA, ref generateOrderB);
        return Hash(seed, generateOrderA, generateOrderB, generateOrderC);
    }

    public static uint PeakKey(int generateOrder, int seed)
    {
        return Hash(seed, generateOrder);
    }

    private static uint Mix(uint hash, int value)
    {
        unchecked
        {
            uint data = (uint)value;
            for (int i = 0; i < sizeof(int); i++)
            {
                hash ^= (byte)data;
                hash *= FnvPrime;
                data >>= 8;
            }
            return hash;
        }
    }

    private static void Swap(ref int a, ref int b)
    {
        int value = a;
        a = b;
        b = value;
    }
}
