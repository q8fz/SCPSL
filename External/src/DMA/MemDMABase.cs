// DMA Base - Usermode External memory implementation using Win32 API.
// Provides 100% source-compatibility with DMA callers without requiring VMM or PCIe hardware.

using DmaBase.DMA.ScatterAPI;
using DmaBase.Misc;
using DmaBase.Unity;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Refresh;
using VmmSharpEx.Scatter;
using DmaBase;

namespace DmaBase.DMA
{
    internal static class BaseMemoryHolder
    {
        private static MemDMABase? _memory;
        public static MemDMABase MemoryBase
        {
            get => _memory!;
            internal set => _memory ??= value;
        }
    }

    public abstract class MemDMABase
    {
        #region Native Win32

        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;

        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_EXECUTE_READ = 0x20;
        private const uint PAGE_READONLY = 0x02;

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint TH32CS_SNAPMODULE = 0x00000008;
        private const uint TH32CS_SNAPMODULE32 = 0x00000010;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern unsafe bool ReadProcessMemory(
            IntPtr hProcess,
            ulong lpBaseAddress,
            void* lpBuffer,
            nuint nSize,
            out nuint lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern unsafe bool WriteProcessMemory(
            IntPtr hProcess,
            ulong lpBaseAddress,
            void* lpBuffer,
            nuint nSize,
            out nuint lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(
            IntPtr hProcess,
            ulong lpAddress,
            nuint dwSize,
            uint flNewProtect,
            out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern nuint VirtualQueryEx(
            IntPtr hProcess,
            ulong lpAddress,
            out MEMORY_BASIC_INFORMATION lpBuffer,
            nuint dwLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public ulong BaseAddress;
            public ulong AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public nuint RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MODULEENTRY32
        {
            public uint dwSize;
            public uint th32ModuleID;
            public uint th32ProcessID;
            public uint GlblcntUsage;
            public uint ProccntUsage;
            public IntPtr modBaseAddr;
            public uint modBaseSize;
            public IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExePath;
        }

        #endregion

        #region Init & State

        public const uint MAX_READ_SIZE = (uint)0x1000 * 1500;
        protected static readonly ManualResetEvent _syncProcessRunning = new(false);
        protected static readonly ManualResetEvent _syncInRound = new(false);
        protected readonly Vmm _hVMM;
        protected volatile bool _isDisposed;
        protected bool _restartLoop;
        protected IntPtr _hProcess = IntPtr.Zero;

        public ulong MonoBase { get; protected set; }
        public ulong UnityBase { get; protected set; }
        public uint ProcessPID { get; protected set; }
        public virtual bool Starting => false;
        public virtual bool Ready => ProcessPID != 0 && UnityBase != 0 && GameAssemblyBase != 0;
        public virtual bool InRound { get; }
        [Obsolete("Use InRound instead for SCP:SL.")]
        public virtual bool InRaid => InRound;
        public virtual bool IsOffline => false;
        public virtual ulong LevelSettings => 0;
        public virtual bool RoundHasStarted => InRound;
        [Obsolete("Use RoundHasStarted instead for SCP:SL.")]
        public virtual bool RaidHasStarted => RoundHasStarted;

        public virtual ulong GameAssemblyBase { get; protected set; }

        public bool RestartLoop
        {
            set
            {
                if (InRound)
                    _restartLoop = value;
            }
        }

        public Vmm VmmHandle => _hVMM;
        public bool IsDisposed => _isDisposed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ThrowIfVmmDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(MemDMABase), "Memory instance disposed.");
        }

        protected MemDMABase()
        {
            _hVMM = new Vmm(this);
            BaseMemoryHolder.MemoryBase = this;
        }

        protected MemDMABase(FpgaAlgo fpgaAlgo, bool useMemMap)
        {
            Log.WriteLine("[External] Initializing Usermode Memory Engine...");
            _hVMM = new Vmm(this);
            BaseMemoryHolder.MemoryBase = this;
            Log.WriteLine("[External] Usermode Memory Engine Initialized!");
        }

        #endregion

        #region Module & Section Cache

        private readonly object _moduleCacheLock = new();
        private volatile VmmModule[]? _cachedModules;
        private uint _cachedModulesPid;
        private readonly Dictionary<string, VmmSection[]> _cachedSections = new(StringComparer.OrdinalIgnoreCase);

        public void ClearModuleCache()
        {
            lock (_moduleCacheLock)
            {
                _cachedModules = null;
                _cachedModulesPid = 0;
                _cachedSections.Clear();
            }
        }

        #endregion

        #region Process & Handle Management

        protected internal IntPtr EnsureProcessHandle(uint pid = 0)
        {
            uint targetPid = pid != 0 ? pid : ProcessPID;
            if (targetPid == 0) return IntPtr.Zero;
            if (ProcessPID == 0) ProcessPID = targetPid;

            if (_hProcess != IntPtr.Zero) return _hProcess;

            _hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, targetPid);
            if (_hProcess == IntPtr.Zero)
            {
                uint desired = PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION;
                _hProcess = OpenProcess(desired, false, targetPid);
            }
            if (_hProcess == IntPtr.Zero)
            {
                uint readOnly = PROCESS_VM_READ | PROCESS_QUERY_INFORMATION;
                _hProcess = OpenProcess(readOnly, false, targetPid);
            }
            return _hProcess;
        }

        private VmmModule[] FetchModules(uint targetPid)
        {
            // Fast native path: CreateToolhelp32Snapshot (~1ms)
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, targetPid);
            if (snapshot == IntPtr.Zero || snapshot == (IntPtr)(-1))
            {
                snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, targetPid);
            }

            if (snapshot != IntPtr.Zero && snapshot != (IntPtr)(-1))
            {
                try
                {
                    var resultList = new List<VmmModule>();
                    MODULEENTRY32 me = new MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>() };
                    if (Module32First(snapshot, ref me))
                    {
                        do
                        {
                            resultList.Add(new VmmModule
                            {
                                vaBase = (ulong)me.modBaseAddr,
                                cbImageSize = me.modBaseSize,
                                sText = me.szModule,
                                fValid = true
                            });
                        } while (Module32Next(snapshot, ref me));
                    }
                    if (resultList.Count > 0)
                        return resultList.ToArray();
                }
                finally
                {
                    CloseHandle(snapshot);
                }
            }

            // Fallback path: Process.GetProcessById
            try
            {
                var proc = Process.GetProcessById((int)targetPid);
                var list = new List<VmmModule>();
                foreach (ProcessModule mod in proc.Modules)
                {
                    list.Add(new VmmModule
                    {
                        vaBase = (ulong)mod.BaseAddress,
                        cbImageSize = (uint)mod.ModuleMemorySize,
                        sText = mod.ModuleName ?? string.Empty,
                        fValid = true
                    });
                }
                if (list.Count > 0) return list.ToArray();
            }
            catch { }

            return Array.Empty<VmmModule>();
        }

        internal ulong GetModuleBase(uint pid, string moduleName)
        {
            uint targetPid = pid != 0 ? pid : ProcessPID;
            if (targetPid == 0) return 0;
            if (ProcessPID == 0) ProcessPID = targetPid;

            var modules = GetLoadedModules(targetPid);
            for (int i = 0; i < modules.Length; i++)
            {
                if (string.Equals(modules[i].sText, moduleName, StringComparison.OrdinalIgnoreCase))
                    return modules[i].vaBase;
            }

            // If not found in current cache (e.g. module was just loaded during game startup),
            // force a refresh once to check if newly loaded
            lock (_moduleCacheLock)
            {
                var refreshed = FetchModules(targetPid);
                if (refreshed.Length > 0)
                {
                    _cachedModules = refreshed;
                    _cachedModulesPid = targetPid;
                    for (int i = 0; i < refreshed.Length; i++)
                    {
                        if (string.Equals(refreshed[i].sText, moduleName, StringComparison.OrdinalIgnoreCase))
                            return refreshed[i].vaBase;
                    }
                }
            }

            return 0;
        }

        internal ulong GetModuleBase(string moduleName) => GetModuleBase(ProcessPID, moduleName);

        internal VmmModule[] GetLoadedModules(uint pid = 0)
        {
            uint targetPid = pid != 0 ? pid : ProcessPID;
            if (targetPid == 0) return Array.Empty<VmmModule>();
            if (ProcessPID == 0) ProcessPID = targetPid;

            var cached = _cachedModules;
            if (cached != null && _cachedModulesPid == targetPid && cached.Length > 0)
                return cached;

            lock (_moduleCacheLock)
            {
                if (_cachedModules != null && _cachedModulesPid == targetPid && _cachedModules.Length > 0)
                    return _cachedModules;

                var modules = FetchModules(targetPid);
                if (modules.Length > 0)
                {
                    _cachedModules = modules;
                    _cachedModulesPid = targetPid;
                }
                return modules;
            }
        }

        internal VmmModule[] GetLoadedModules() => GetLoadedModules(ProcessPID);

        internal VmmSection[] GetModuleSections(uint pid, string moduleName)
        {
            uint targetPid = pid != 0 ? pid : ProcessPID;
            if (targetPid == 0) return Array.Empty<VmmSection>();
            if (ProcessPID == 0) ProcessPID = targetPid;

            lock (_moduleCacheLock)
            {
                if (_cachedSections.TryGetValue(moduleName, out var cached))
                    return cached;
            }

            ulong modBase = GetModuleBase(targetPid, moduleName);
            if (modBase == 0) return Array.Empty<VmmSection>();

            try
            {
                int e_lfanew = ReadValue<int>(modBase + 0x3C, false);
                if (e_lfanew <= 0 || e_lfanew > 0x1000) return Array.Empty<VmmSection>();

                ulong ntHeaders = modBase + (ulong)e_lfanew;
                uint sig = ReadValue<uint>(ntHeaders, false);
                if (sig != 0x00004550) return Array.Empty<VmmSection>();

                ushort numSections = ReadValue<ushort>(ntHeaders + 0x6, false);
                ushort sizeOfOptHeader = ReadValue<ushort>(ntHeaders + 0x14, false);

                ulong sectionHeaderStart = ntHeaders + 0x18 + sizeOfOptHeader;
                var sections = new List<VmmSection>(numSections);

                for (int i = 0; i < numSections; i++)
                {
                    ulong secAddr = sectionHeaderStart + (ulong)(i * 40);
                    byte[] nameBytes = new byte[8];
                    ReadBuffer(secAddr, nameBytes.AsSpan(), false);
                    string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0', ' ');

                    uint virtualSize = ReadValue<uint>(secAddr + 8, false);
                    uint virtualAddress = ReadValue<uint>(secAddr + 12, false);
                    uint sizeOfRawData = ReadValue<uint>(secAddr + 16, false);
                    uint characteristics = ReadValue<uint>(secAddr + 36, false);

                    sections.Add(new VmmSection
                    {
                        Name = name,
                        Characteristics = characteristics,
                        VA = virtualAddress,
                        MiscPhysicalAddressOrVirtualSize = virtualSize,
                        SizeOfRawData = sizeOfRawData
                    });
                }

                var result = sections.ToArray();
                lock (_moduleCacheLock)
                {
                    _cachedSections[moduleName] = result;
                }
                return result;
            }
            catch
            {
                return Array.Empty<VmmSection>();
            }
        }

        internal VmmSection[] GetModuleSections(string moduleName) => GetModuleSections(ProcessPID, moduleName);

        #endregion

        #region Refresh

        public void FullRefresh()
        {
            ClearModuleCache();
        }

        #endregion

        #region Events

        public static event EventHandler<EventArgs>? GameStarted;
        public static event EventHandler<EventArgs>? GameStopped;
        public static event EventHandler<EventArgs>? RoundStarted;
        public static event EventHandler<EventArgs>? RoundEnded;

        [Obsolete("Use RoundStarted instead for SCP:SL.")]
        public static event EventHandler<EventArgs>? RaidStarted
        {
            add => RoundStarted += value;
            remove => RoundStarted -= value;
        }

        [Obsolete("Use RoundEnded instead for SCP:SL.")]
        public static event EventHandler<EventArgs>? RaidStopped
        {
            add => RoundEnded += value;
            remove => RoundEnded -= value;
        }

        protected internal static void OnGameStarted()
        {
            _syncProcessRunning.Set();
            GameStarted?.Invoke(null, EventArgs.Empty);
        }

        protected internal static void OnGameStopped()
        {
            _syncProcessRunning.Reset();
            var mem = BaseMemoryHolder.MemoryBase;
            if (mem != null)
            {
                mem.ClearModuleCache();
                if (mem._hProcess != IntPtr.Zero)
                {
                    CloseHandle(mem._hProcess);
                    mem._hProcess = IntPtr.Zero;
                }
            }
            GameStopped?.Invoke(null, EventArgs.Empty);
        }

        protected internal static void OnRoundStarted()
        {
            _syncInRound.Set();
            RoundStarted?.Invoke(null, EventArgs.Empty);
        }

        protected internal static void OnRoundEnded()
        {
            _syncInRound.Reset();
            RoundEnded?.Invoke(null, EventArgs.Empty);
        }

        [Obsolete("Use OnRoundStarted instead for SCP:SL.")]
        protected static void OnRaidStarted() => OnRoundStarted();

        [Obsolete("Use OnRoundEnded instead for SCP:SL.")]
        protected static void OnRaidStopped() => OnRoundEnded();

        public static bool WaitForProcess(int timeoutMs = Timeout.Infinite) => _syncProcessRunning.WaitOne(timeoutMs);
        public static bool WaitForRound(int timeoutMs = Timeout.Infinite) => _syncInRound.WaitOne(timeoutMs);
        [Obsolete("Use WaitForRound instead for SCP:SL.")]
        public static bool WaitForRaid() => WaitForRound();

        #endregion

        #region ScatterRead

        public void ReadScatter(IScatterEntry[] entries, bool useCache = true)
            => ReadScatter(entries, entries.Length, useCache);

        public void ReadScatter(IScatterEntry[] entries, int count, bool useCache = true)
        {
            if (count == 0) return;
            ThrowIfVmmDisposed();

            var vmmFlags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            using var scatter = new VmmScatter(_hVMM, ProcessPID, vmmFlags);

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (entry.Address == 0x0 || entry.CB == 0 || (uint)entry.CB > MAX_READ_SIZE)
                {
                    entry.IsFailed = true;
                    continue;
                }
                if (!scatter.PrepareRead(entry.Address, (uint)entry.CB))
                    entry.IsFailed = true;
            }

            scatter.Execute();

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (!entry.IsFailed)
                    entry.ReadResult(scatter);
            }
        }

        public VmmScatter GetScatter(VmmFlags flags) => new VmmScatter(_hVMM, ProcessPID, flags);

        #endregion

        #region ReadMethods

        public void ReadCache(params ulong[] va) { }

        public unsafe void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true, bool allowPartialRead = false)
            where T : unmanaged
        {
            ThrowIfVmmDisposed();
            if (addr == 0 || buffer.Length == 0)
            {
                if (!allowPartialRead) throw new VmmException("Memory Read Failed: null address or zero buffer.");
                return;
            }

            IntPtr handle = EnsureProcessHandle();
            if (handle == IntPtr.Zero) throw new VmmException("Memory Read Failed: No process handle.");

            nuint bytesToRead = (nuint)(buffer.Length * sizeof(T));
            fixed (void* p = buffer)
            {
                if (!ReadProcessMemory(handle, addr, p, bytesToRead, out nuint bytesRead))
                {
                    if (!allowPartialRead || bytesRead == 0)
                        throw new VmmException($"Memory Read Failed at 0x{addr:X} (error {Marshal.GetLastWin32Error()})");
                }
            }
        }

        public T[] ReadArray<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged
        {
            if (count <= 0) return Array.Empty<T>();
            T[] result = new T[count];
            ReadBuffer(addr, result.AsSpan(), useCache, false);
            return result;
        }

        public unsafe byte[] ReadBuffer(ulong addr, int size, bool useCache = true, bool allowIncompleteRead = false)
        {
            ThrowIfVmmDisposed();
            if (size <= 0) return Array.Empty<byte>();

            byte[] buf = new byte[size];
            IntPtr handle = EnsureProcessHandle();
            if (handle == IntPtr.Zero) return Array.Empty<byte>();

            fixed (void* p = buf)
            {
                if (!ReadProcessMemory(handle, addr, p, (nuint)size, out nuint read))
                {
                    if (!allowIncompleteRead)
                        throw new Exception($"[External] ERROR reading buffer at 0x{addr:X}");
                }
                if (!allowIncompleteRead && read != (nuint)size)
                    throw new Exception("Incomplete memory read!");
            }
            return buf;
        }

        public unsafe void ReadBufferEnsure<T>(ulong addr, Span<T> buffer1)
            where T : unmanaged
        {
            ReadBuffer(addr, buffer1, false, false);
        }

        public unsafe T ReadValue<T>(ulong addr, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            ThrowIfVmmDisposed();
            if (addr == 0) return default;

            IntPtr handle = EnsureProcessHandle();
            if (handle == IntPtr.Zero) return default;

            T result = default;
            nuint size = (nuint)sizeof(T);
            if (!ReadProcessMemory(handle, addr, &result, size, out _))
                return default;

            return result;
        }

        public unsafe T ReadValueEnsure<T>(ulong addr)
            where T : unmanaged, allows ref struct
        {
            return ReadValue<T>(addr, false);
        }

        public ulong ReadPtr(ulong addr, bool useCache = true) =>
            ReadValue<ulong>(addr, useCache);

        public ulong ReadPtrChain(ulong addr, uint[] offsets, bool useCache = true)
        {
            ulong ptr = addr;
            for (int i = 0; i < offsets.Length; i++)
            {
                ptr = ReadPtr(ptr + offsets[i], useCache);
                if (ptr == 0) return 0;
            }
            return ptr;
        }

        public string ReadString(ulong addr, int length, bool useCache = true)
        {
            if (addr == 0 || length <= 0) return string.Empty;
            try
            {
                byte[] buf = ReadBuffer(addr, length, useCache, true);
                int nullIdx = Array.IndexOf(buf, (byte)0);
                if (nullIdx >= 0)
                    return Encoding.UTF8.GetString(buf, 0, nullIdx).Trim();
                return Encoding.UTF8.GetString(buf).Trim();
            }
            catch { return string.Empty; }
        }

        #endregion

        #region WriteMethods

        private unsafe bool WriteInternal(ulong addr, void* pBuffer, nuint size)
        {
            IntPtr handle = EnsureProcessHandle();
            if (handle == IntPtr.Zero || addr == 0 || size == 0) return false;

            // Fast path: direct WriteProcessMemory (succeeds immediately for writable heap/data pages)
            if (WriteProcessMemory(handle, addr, pBuffer, size, out _))
                return true;

            // Slow path: if write failed (e.g. read-only code/data page), temporarily change protection
            uint oldProtect = 0;
            if (VirtualProtectEx(handle, addr, size, PAGE_EXECUTE_READWRITE, out oldProtect))
            {
                bool result = WriteProcessMemory(handle, addr, pBuffer, size, out _);
                VirtualProtectEx(handle, addr, size, oldProtect, out _);
                return result;
            }

            return false;
        }

        public unsafe void WriteValue<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            ThrowIfVmmDisposed();
            WriteInternal(addr, &value, (nuint)sizeof(T));
        }

        public unsafe void WriteValue<T>(ulong addr, ref T value)
            where T : unmanaged, allows ref struct
        {
            ThrowIfVmmDisposed();
            fixed (void* p = &value)
            {
                WriteInternal(addr, p, (nuint)sizeof(T));
            }
        }

        public unsafe void WriteValueEnsure<T>(ulong addr, T value)
            where T : unmanaged
        {
            WriteValue(addr, value);
        }

        public unsafe void WriteBuffer<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            ThrowIfVmmDisposed();
            if (buffer.Length == 0) return;
            fixed (void* p = buffer)
            {
                WriteInternal(addr, p, (nuint)(buffer.Length * sizeof(T)));
            }
        }

        public void WriteBufferEnsure<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            WriteBuffer(addr, buffer);
        }

        #endregion

        #region Signature Scan

        public ulong FindSignature(string signature, string moduleName)
        {
            ulong modBase = GetModuleBase(moduleName);
            if (modBase == 0) return 0;

            var modules = GetLoadedModules();
            uint modSize = 0;
            foreach (var m in modules)
            {
                if (string.Equals(m.sText, moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    modSize = m.cbImageSize;
                    break;
                }
            }
            if (modSize == 0) modSize = 0x2000000;

            string[] tokens = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            byte?[] pattern = new byte?[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                pattern[i] = (tokens[i] == "?" || tokens[i] == "??")
                    ? null
                    : Convert.ToByte(tokens[i], 16);
            }

            const int chunkSize = 0x10000;
            byte[] chunk = new byte[chunkSize + pattern.Length];
            IntPtr handle = EnsureProcessHandle();
            if (handle == IntPtr.Zero) return 0;

            for (ulong offset = 0; offset < modSize; offset += chunkSize)
            {
                nuint toRead = (nuint)Math.Min((ulong)chunk.Length, modSize - offset);
                unsafe
                {
                    fixed (void* p = chunk)
                    {
                        if (!ReadProcessMemory(handle, modBase + offset, p, toRead, out nuint read) || read < (nuint)pattern.Length)
                            continue;

                        int scanLen = (int)read - pattern.Length;
                        for (int i = 0; i <= scanLen; i++)
                        {
                            bool match = true;
                            for (int j = 0; j < pattern.Length; j++)
                            {
                                if (pattern[j].HasValue && chunk[i + j] != pattern[j]!.Value)
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match)
                                return modBase + offset + (ulong)i;
                        }
                    }
                }
            }
            return 0;
        }

        #endregion
    }
}
