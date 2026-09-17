// DMA Base - remote IL2CPP list reader (pooled).

using DmaBase.DMA;
using DmaBase.Misc.Pools;

namespace DmaBase.Unity.Collections
{
    /// <summary>
    /// Reads a managed <c>List&lt;T&gt;</c> layout from remote IL2CPP DmaMemory.
    /// Rent via <see cref="Get"/> and dispose when finished.
    /// </summary>
    public sealed class UnityList<T> : SharedArray<T>, IPooledObject<UnityList<T>>
        where T : unmanaged
    {
        public const uint CountOffset = 0x18;
        public const uint ArrOffset = 0x10;
        public const uint ArrStartOffset = 0x20;

        public static UnityList<T> Get(ulong addr, bool useCache = true)
        {
            var list = IPooledObject<UnityList<T>>.Rent();
            list.Initialize(addr, useCache);
            return list;
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

                var listBase = DmaMemory.ReadPtr(addr + ArrOffset, useCache) + ArrStartOffset;
                DmaMemory.ReadBuffer(listBase, Span, useCache);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        [Obsolete("Rent via IPooledObject")]
        public UnityList() : base() { }

        protected override void Dispose(bool disposing) =>
            IPooledObject<UnityList<T>>.Return(this);
    }
}
