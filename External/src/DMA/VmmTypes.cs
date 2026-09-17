// Compatibility shims for VMM types in Usermode External mode
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DmaBase.Misc;

namespace VmmSharpEx.Options
{
    [Flags]
    public enum VmmFlags : uint
    {
        NONE = 0,
        NOCACHE = 1,
        ZEROPAD_ON_FAIL = 2,
        FORCECACHE_READ = 4,
        NOPAGING = 8,
        NOPAGING_IO = 16,
        CACHE_STICKY = 32
    }
}

namespace VmmSharpEx.Refresh
{
    public enum RefreshOption
    {
        MemoryPartial,
        MemoryFull,
        TlbPartial,
        TlbFull
    }
}

namespace VmmSharpEx.Scatter
{
    using VmmSharpEx.Options;

    public sealed class VmmScatter : IDisposable
    {
        private readonly DmaBase.DMA.MemDMABase? _memory;
        private readonly uint _pid;
        private readonly VmmFlags _flags;
        private readonly List<(ulong Address, byte[] Buffer)> _queuedWrites = new();

        public VmmScatter(VmmSharpEx.Vmm vmm, uint pid, VmmFlags flags)
        {
            _memory = DmaBase.DMA.BaseMemoryHolder.MemoryBase;
            _pid = pid;
            _flags = flags;
        }

        public bool PrepareRead(ulong va, uint size)
        {
            return va != 0 && size > 0;
        }

        public bool PrepareWriteSpan<T>(ulong va, Span<T> buffer) where T : unmanaged
        {
            if (va == 0 || buffer.Length == 0) return false;
            byte[] bytes = MemoryMarshal.AsBytes(buffer).ToArray();
            _queuedWrites.Add((va, bytes));
            return true;
        }

        public bool PrepareWriteValue<T>(ulong va, in T value) where T : unmanaged
        {
            if (va == 0) return false;
            byte[] bytes = new byte[Unsafe.SizeOf<T>()];
            MemoryMarshal.Write(bytes, in value);
            _queuedWrites.Add((va, bytes));
            return true;
        }

        public void Clear(VmmFlags flags = VmmFlags.NONE)
        {
            _queuedWrites.Clear();
        }

        public void Execute()
        {
            if (_queuedWrites.Count > 0 && _memory != null)
            {
                foreach (var (addr, bytes) in _queuedWrites)
                {
                    _memory.WriteBuffer(addr, bytes.AsSpan());
                }
                _queuedWrites.Clear();
            }
        }

        public bool ReadSpan<T>(ulong va, Span<T> buffer) where T : unmanaged
        {
            if (_memory == null || va == 0) return false;
            try
            {
                _memory.ReadBuffer(va, buffer, _flags != VmmFlags.NOCACHE, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            _queuedWrites.Clear();
        }
    }
}

namespace VmmSharpEx
{
    using VmmSharpEx.Options;
    using VmmSharpEx.Refresh;
    using VmmSharpEx.Scatter;

    public sealed class VmmException : Exception
    {
        public VmmException(string message) : base(message) { }
        public VmmException(string message, Exception inner) : base(message, inner) { }
    }

    public struct VmmModule
    {
        public ulong vaBase;
        public uint cbImageSize;
        public string sText;
        public bool fValid;
    }

    public struct VmmSection
    {
        public string Name;
        public uint Characteristics;
        public ulong VA;
        public uint MiscPhysicalAddressOrVirtualSize;
        public uint SizeOfRawData;
        public ulong va => VA;
        public uint cb => MiscPhysicalAddressOrVirtualSize;
    }

    public sealed class Vmm : IDisposable
    {
        private readonly DmaBase.DMA.MemDMABase? _owner;

        public Vmm(DmaBase.DMA.MemDMABase owner)
        {
            _owner = owner;
        }

        public Vmm(string[] initArgs)
        {
            _owner = DmaBase.DMA.BaseMemoryHolder.MemoryBase;
        }

        public ulong GetMemoryMap(bool applyMap, string outputFile) => 0;

        public void RegisterAutoRefresh(RefreshOption option, TimeSpan interval) { }

        public void MemPrefetchPages(uint pid, ReadOnlySpan<ulong> va) { }

        public bool PidGetFromName(string processName, out uint pid)
        {
            pid = 0;
            string cleanName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4)
                : processName;

            var procs = Process.GetProcessesByName(cleanName);
            if (procs.Length == 0)
                procs = Process.GetProcessesByName(processName);

            if (procs.Length > 0)
            {
                var best = procs
                    .OrderByDescending(p => p.MainWindowHandle != IntPtr.Zero)
                    .ThenByDescending(p =>
                    {
                        try { return !p.MainModule?.FileName?.Contains("Dedicated Server", StringComparison.OrdinalIgnoreCase) ?? true; }
                        catch { return true; }
                    })
                    .ThenByDescending(p => p.WorkingSet64)
                    .FirstOrDefault();

                if (best != null)
                {
                    pid = (uint)best.Id;
                    return true;
                }
            }
            return false;
        }

        public ulong ProcessGetModuleBase(uint pid, string moduleName)
        {
            return _owner != null ? _owner.GetModuleBase(pid, moduleName) : 0;
        }

        public VmmModule[] Map_GetModule(uint pid, bool is64)
        {
            return _owner != null ? _owner.GetLoadedModules(pid) : Array.Empty<VmmModule>();
        }

        public VmmSection[] ProcessGetSections(uint pid, string moduleName)
        {
            return _owner != null ? _owner.GetModuleSections(pid, moduleName) : Array.Empty<VmmSection>();
        }

        public void ForceFullRefresh() { }

        public void Dispose() { }
    }
}
