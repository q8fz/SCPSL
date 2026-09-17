// DMA Base - static facade over the active MemDMABase instance (use after host init).

using DmaBase.DMA.ScatterAPI;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

namespace DmaBase.DMA
{
    /// <summary>
    /// Global accessor for the active <see cref="MemDMABase"/> instance.
    /// The host application must construct a <see cref="MemDMABase"/> derivative before calling Unity helpers.
    /// </summary>
    public static class DmaMemory
    {
        private static MemDMABase Instance =>
            BaseMemoryHolder.MemoryBase
            ?? throw new InvalidOperationException(
                "DMA is not initialized. Construct your MemDMABase subclass before reading memory.");

        public static bool Ready => Instance.Ready;
        public static ulong GameAssemblyBase => Instance.GameAssemblyBase;
        public static uint ProcessPID => Instance.ProcessPID;
        public static ulong UnityBase => Instance.UnityBase;
        public static ulong MonoBase => Instance.MonoBase;

        public static void ReadScatter(IScatterEntry[] entries, bool useCache = true) =>
            Instance.ReadScatter(entries, useCache);

        public static void ReadScatter(IScatterEntry[] entries, int count, bool useCache = true) =>
            Instance.ReadScatter(entries, count, useCache);

        public static unsafe void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true, bool allowPartialRead = false)
            where T : unmanaged =>
            Instance.ReadBuffer(addr, buffer, useCache, allowPartialRead);

        public static T[] ReadArray<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged =>
            Instance.ReadArray<T>(addr, count, useCache);

        public static ulong ReadPtrChain(ulong addr, uint[] offsets, bool useCache = true) =>
            Instance.ReadPtrChain(addr, offsets, useCache);

        public static ulong ReadPtr(ulong addr, bool useCache = true) =>
            Instance.ReadPtr(addr, useCache);

        public static unsafe T ReadValue<T>(ulong addr, bool useCache = true)
            where T : unmanaged, allows ref struct =>
            Instance.ReadValue<T>(addr, useCache);

        public static unsafe T ReadValueEnsure<T>(ulong addr)
            where T : unmanaged, allows ref struct =>
            Instance.ReadValueEnsure<T>(addr);

        public static string ReadString(ulong addr, int length, bool useCache = true) =>
            Instance.ReadString(addr, length, useCache);

        public static ulong FindSignature(string signature, string moduleName) =>
            Instance.FindSignature(signature, moduleName);

        public static VmmScatter GetScatter(VmmFlags flags) =>
            Instance.GetScatter(flags);

        public static unsafe void WriteValue<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct =>
            Instance.WriteValue(addr, value);

        public static unsafe void WriteValue<T>(ulong addr, ref T value)
            where T : unmanaged, allows ref struct =>
            Instance.WriteValue(addr, ref value);

        public static unsafe void WriteBuffer<T>(ulong addr, Span<T> buffer)
            where T : unmanaged =>
            Instance.WriteBuffer(addr, buffer);

        public static void WriteBufferEnsure<T>(ulong addr, Span<T> buffer)
            where T : unmanaged =>
            Instance.WriteBufferEnsure(addr, buffer);

        public static unsafe void WriteValueEnsure<T>(ulong addr, T value)
            where T : unmanaged =>
            Instance.WriteValueEnsure(addr, value);
    }
}
