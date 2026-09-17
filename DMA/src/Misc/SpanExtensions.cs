// DMA Base - span/vector helpers for Unity string and transform reads.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace DmaBase.Misc
{
    /// <summary>String/span helpers for Unity UTF-16 reads.</summary>
    public static class SpanExtensions
    {
        /// <summary>Returns byte index of UTF-16 null terminator, or -1 if not found.</summary>
        public static int FindUtf16NullTerminatorIndex(this Span<byte> buffer)
        {
            for (int i = 0; i + 1 < buffer.Length; i += 2)
            {
                if (buffer[i] == 0 && buffer[i + 1] == 0)
                    return i;
            }
            return -1;
        }
    }

    /// <summary>Sanity checks for world-space vectors read over DMA.</summary>
    public static class VectorExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this Vector3 v)
        {
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
                throw new InvalidOperationException($"Abnormal Vector3: {v}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfAbnormal(this Quaternion q)
        {
            if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W))
                throw new InvalidOperationException($"Abnormal Quaternion: {q}");
        }
    }
}
