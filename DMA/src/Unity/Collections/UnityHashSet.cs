// DMA Base - remote IL2CPP hash set reader (pooled).

using DmaBase.DMA;
using DmaBase.Misc.Pools;
using System.Runtime.InteropServices;

namespace DmaBase.Unity.Collections
{
    /// <summary>
    /// Reads a managed <c>HashSet&lt;T&gt;</c> layout from remote IL2CPP DmaMemory.
    /// Rent via <see cref="Get"/> and dispose when finished.
    /// </summary>
    public sealed class UnityHashSet<T> : SharedArray<UnityHashSet<T>.MemHashEntry>, IPooledObject<UnityHashSet<T>>
        where T : unmanaged
    {
        public const uint CountOffset = 0x38;
        public const uint ArrOffset = 0x18;
        public const uint ArrStartOffset = 0x20;

        public static UnityHashSet<T> Get(ulong addr, bool useCache = true)
        {
            var hs = IPooledObject<UnityHashSet<T>>.Rent();
            hs.Initialize(addr, useCache);
            return hs;
        }

        private void Initialize(ulong addr, bool useCache = true)
        {
            try
            {
                var count = DmaMemory.ReadValue<int>(addr + CountOffset, useCache);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16384, nameof(count));
                Initialize(count);
                if (count == 0)
                    return;

                var hashSetBase = DmaMemory.ReadPtr(addr + ArrOffset, useCache) + ArrStartOffset;
                DmaMemory.ReadBuffer(hashSetBase, Span, useCache);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        [Obsolete("Rent via IPooledObject")]
        public UnityHashSet() : base() { }

        protected override void Dispose(bool disposing) =>
            IPooledObject<UnityHashSet<T>>.Return(this);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public readonly struct MemHashEntry
        {
            public static implicit operator T(MemHashEntry x) => x.Value;

            private readonly int _hashCode;
            private readonly int _next;
            public readonly T Value;
        }
    }
}
