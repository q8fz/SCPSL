// DMA Base - VmmSharpEx process attachment, reads, writes, scatter, and signature scans.

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
        private static MemDMABase _memory;
        /// <summary>
        /// Limited Singleton Instance for use in this satelite assembly.
        /// </summary>
        public static MemDMABase MemoryBase
        {
            get => _memory;
            internal set => _memory ??= value;
        }
    }
    /// <summary>
    /// DMA Memory Module.
    /// </summary>
    public abstract class MemDMABase
    {
        #region Init

        private const string _memoryMapFile = "mmap.txt";
        public const uint MAX_READ_SIZE = (uint)0x1000 * 1500;
        protected static readonly ManualResetEvent _syncProcessRunning = new(false);
        protected static readonly ManualResetEvent _syncInRound = new(false);
        protected readonly Vmm _hVMM;
        protected volatile bool _isDisposed;
        protected bool _restartLoop;
        /// <summary>
        /// Current Process ID (PID).
        /// </summary>
        public ulong MonoBase { get; protected set; }
        public ulong UnityBase { get; protected set; }
        public uint ProcessPID { get; protected set; }
        public virtual bool Starting { get; }
        public virtual bool Ready { get; }
        public virtual bool InRound { get; }
        [Obsolete("Use InRound instead for SCP:SL.")]
        public virtual bool InRaid => InRound;
        public virtual bool IsOffline { get; }
        public virtual ulong LevelSettings { get; }
        public virtual bool RoundHasStarted => InRound;
        [Obsolete("Use RoundHasStarted instead for SCP:SL.")]
        public virtual bool RaidHasStarted => RoundHasStarted;

        /// <summary>
        /// Base address of GameAssembly.dll in the target process.
        /// Override or set after module enumeration in your host app.
        /// </summary>
        public virtual ulong GameAssemblyBase { get; protected set; }

        /// <summary>
        /// Set to TRUE to restart the game loop on the next cycle.
        /// </summary>
        public bool RestartLoop
        {
            set
            {
                if (InRound)
                    _restartLoop = value;
            }
        }

        /// <summary>
        /// Vmm Handle for this DMA Connection.
        /// </summary>
        public Vmm VmmHandle => _hVMM;

        /// <summary>
        /// True if the VMM handle has been disposed.
        /// </summary>
        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// Throws <see cref="ObjectDisposedException"/> if the VMM handle has been disposed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ThrowIfVmmDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(Vmm), "VMM handle has been disposed.");
        }

        private MemDMABase() { }

        protected MemDMABase(FpgaAlgo fpgaAlgo, bool useMemMap)
        {
            Log.WriteLine("Initializing DMA...");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string vmmPath = Path.Combine(baseDir, "vmm.dll");
            string lcPath = Path.Combine(baseDir, "leechcore.dll");
            string mmapPath = Path.Combine(baseDir, _memoryMapFile);

            string vmmVersion = File.Exists(vmmPath) ? FileVersionInfo.GetVersionInfo(vmmPath).FileVersion ?? "unknown" : "not found";
            string lcVersion = File.Exists(lcPath) ? FileVersionInfo.GetVersionInfo(lcPath).FileVersion ?? "unknown" : "not found";
            string versions = $"Vmm Version: {vmmVersion}\n" +
                $"Leechcore Version: {lcVersion}";
            var initArgs = new string[] {
                "-norefresh",
                "-device",
                fpgaAlgo is FpgaAlgo.Auto ?
                    "fpga" : $"fpga://algo={(int)fpgaAlgo}",
                "-waitinitialize"};
            try
            {
                /// Begin Init...
                if (useMemMap && !File.Exists(mmapPath))
                {
                    Log.WriteLine("[DMA] No MemMap, attempting to generate...");
                    _hVMM = new Vmm(initArgs);
                    _ = _hVMM.GetMemoryMap(applyMap: true, outputFile: mmapPath);
                }
                else
                {
                    if (useMemMap && File.Exists(mmapPath))
                    {
                        var mapArgs = new string[] { "-memmap", mmapPath };
                        initArgs = initArgs.Concat(mapArgs).ToArray();
                    }
                    _hVMM = new Vmm(initArgs);
                }
                _hVMM.RegisterAutoRefresh(RefreshOption.MemoryPartial, TimeSpan.FromMilliseconds(300));
                _hVMM.RegisterAutoRefresh(RefreshOption.TlbPartial, TimeSpan.FromSeconds(2));
                BaseMemoryHolder.MemoryBase = this;
                Log.WriteLine("DMA Initialized!");
            }
            catch (Exception ex)
            {
                throw new Exception(
                "DMA Initialization Failed!\n" +
                $"Reason: {ex.Message}\n" +
                $"{versions}\n\n" +
                "===TROUBLESHOOTING===\n" +
                "1. Reboot both your Game PC / DMA PC (This USUALLY fixes it).\n" +
                "2. Reseat all cables/connections and make sure they are secure.\n" +
                "3. Changed Hardware/Operating System on Game PC? Delete your mmap.txt and symbols folder.\n" +
                "4. Make sure all Setup Steps are completed (See DMA Setup Guide/FAQ for additional troubleshooting).");
            }
        }

        #endregion

        #region VMM Refresh

        /// <summary>
        /// Manually Force a Full Vmm Refresh.
        /// </summary>
        public void FullRefresh()
        {
            if (_isDisposed)
                return;
            _hVMM.ForceFullRefresh();
        }

        #endregion

        #region Events

        /// <summary>
        /// Raised when the game process is successfully started/attached.
        /// Outside Subscribers should handle exceptions!
        /// </summary>
        public static event EventHandler<EventArgs>? GameStarted;
        /// <summary>
        /// Raised when the game process is no longer running.
        /// Outside Subscribers should handle exceptions!
        /// </summary>
        public static event EventHandler<EventArgs>? GameStopped;
        /// <summary>
        /// Raised when an SCP:SL round starts.
        /// Outside Subscribers should handle exceptions!
        /// </summary>
        public static event EventHandler<EventArgs>? RoundStarted;
        /// <summary>
        /// Raised when an SCP:SL round ends or restarts.
        /// Outside Subscribers should handle exceptions!
        /// </summary>
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

        /// <summary>
        /// Raises the GameStarted Event and signals process synchronization.
        /// </summary>
        protected internal static void OnGameStarted()
        {
            _syncProcessRunning.Set();
            GameStarted?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Raises the GameStopped Event and resets process synchronization.
        /// </summary>
        protected internal static void OnGameStopped()
        {
            _syncProcessRunning.Reset();
            GameStopped?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Raises the RoundStarted Event and signals round synchronization.
        /// </summary>
        protected internal static void OnRoundStarted()
        {
            _syncInRound.Set();
            RoundStarted?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Raises the RoundEnded Event and resets round synchronization.
        /// </summary>
        protected internal static void OnRoundEnded()
        {
            _syncInRound.Reset();
            RoundEnded?.Invoke(null, EventArgs.Empty);
        }

        [Obsolete("Use OnRoundStarted instead for SCP:SL.")]
        protected static void OnRaidStarted() => OnRoundStarted();

        [Obsolete("Use OnRoundEnded instead for SCP:SL.")]
        protected static void OnRaidStopped() => OnRoundEnded();

        /// <summary>
        /// Blocks until the Game Process is Running.
        /// </summary>
        public static bool WaitForProcess(int timeoutMs = Timeout.Infinite) => _syncProcessRunning.WaitOne(timeoutMs);

        /// <summary>
        /// Blocks until In Round/Match.
        /// </summary>
        public static bool WaitForRound(int timeoutMs = Timeout.Infinite) => _syncInRound.WaitOne(timeoutMs);

        [Obsolete("Use WaitForRound instead for SCP:SL.")]
        public static bool WaitForRaid() => WaitForRound();

        #endregion

        #region ScatterRead

        /// <summary>
        /// Performs multiple reads in one sequence using the native VmmScatter API.
        /// Page deduplication and result extraction are handled at the native layer —
        /// no managed HashSet, page array, or results dictionary is allocated.
        /// </summary>
        public void ReadScatter(IScatterEntry[] entries, bool useCache = true)
            => ReadScatter(entries, entries.Length, useCache);

        /// <summary>
        /// Overload that accepts an explicit count, enabling callers to pass ArrayPool-rented
        /// arrays larger than the actual entry count without extra allocation.
        /// </summary>
        public void ReadScatter(IScatterEntry[] entries, int count, bool useCache = true)
        {
            if (count == 0)
                return;
            ThrowIfVmmDisposed();

            var vmmFlags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            using var scatter = new VmmScatter(_hVMM, ProcessPID, vmmFlags);

            // First pass: register each address with the native scatter handle.
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

            // Execute all prepared reads in one native call.
            scatter.Execute();

            // Second pass: extract results via native ReadSpan (no page-boundary logic needed).
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (!entry.IsFailed)
                    entry.ReadResult(scatter);
            }
        }

        #endregion

        #region ReadMethods

        /// <summary>
        /// Prefetch pages into the cache.
        /// </summary>
        /// <param name="va"></param>
        public void ReadCache(params ulong[] va)
        {
            ThrowIfVmmDisposed();
            _hVMM.MemPrefetchPages(ProcessPID, va.AsSpan());
        }

        /// <summary>
        /// Read memory into a Buffer of type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T">Value Type <typeparamref name="T"/></typeparam>
        /// <param name="addr">Virtual Address to read from.</param>
        /// <param name="buffer">Buffer to receive memory read in.</param>
        /// <param name="useCache">Use caching for this read.</param>
        public unsafe void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true, bool allowPartialRead = false)
            where T : unmanaged
        {
            ThrowIfVmmDisposed();
            var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;

            if (!_hVMM.MemReadSpan(ProcessPID, addr, buffer, flags))
                throw new VmmException("Memory Read Failed!");

            if (!allowPartialRead && buffer.Length == 0)
                throw new VmmException("Memory Read Failed!");
        }
        /// <summary>
        /// Read an array of type <typeparamref name="T"/> from DmaMemory.
        /// The first element begins reading at 0x0 and the array is assumed to be contiguous.
        /// IMPORTANT: You must call <see cref="IDisposable.Dispose"/> on the returned SharedArray when done."/>
        /// </summary>
        /// <typeparam name="T">Value type to read.</typeparam>
        /// <param name="addr">Address to read from.</param>
        /// <param name="count">Number of array elements to read.</param>
        /// <param name="useCache">Use caching for this read.</param>
        /// <returns><see cref="PooledMemory{T}"/> value. Be sure to call <see cref="IDisposable.Dispose"/>!</returns>
        public T[] ReadArray<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged
        {
            if (count <= 0)
                return Array.Empty<T>();

            T[] result = new T[count];
            ReadBuffer(addr, result.AsSpan(), useCache, false);
            return result;
        }
        /// <summary>
        /// Read memory into a buffer.
        /// </summary>
        public byte[] ReadBuffer(ulong addr, int size, bool useCache = true, bool allowIncompleteRead = false)
        {
            ThrowIfVmmDisposed();
            try
            {
                var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
                var buf = _hVMM.MemRead(ProcessPID, addr, (uint)size, out uint cbRead, flags);
                if (!allowIncompleteRead && cbRead != (uint)size)
                    throw new Exception("Incomplete memory read!");
                return buf ?? Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR reading buffer at 0x{addr:X}", ex);
            }
        }
        /// <summary>
        /// Read memory into a Buffer of type <typeparamref name="T"/> and ensure the read is correct.
        /// </summary>
        /// <typeparam name="T">Value Type <typeparamref name="T"/></typeparam>
        /// <param name="addr">Virtual Address to read from.</param>
        /// <param name="buffer1">Buffer to receive memory read in.</param>
        /// <param name="useCache">Use caching for this read.</param>
        public unsafe void ReadBufferEnsure<T>(ulong addr, Span<T> buffer1)
            where T : unmanaged
        {
            uint cb = (uint)(SizeChecker<T>.Size * buffer1.Length);
            try
            {
                var buffer2 = new T[buffer1.Length].AsSpan();
                var buffer3 = new T[buffer1.Length].AsSpan();

                if (!_hVMM.MemReadSpan(ProcessPID, addr, buffer3, VmmFlags.NOCACHE))
                    throw new VmmException("Memory Read Failed!");

                Thread.SpinWait(5);

                if (!_hVMM.MemReadSpan(ProcessPID, addr, buffer2, VmmFlags.NOCACHE))
                    throw new VmmException("Memory Read Failed!");

                Thread.SpinWait(5);

                if (!_hVMM.MemReadSpan(ProcessPID, addr, buffer1, VmmFlags.NOCACHE))
                    throw new VmmException("Memory Read Failed!");
                if (!buffer1.SequenceEqual(buffer2) || !buffer1.SequenceEqual(buffer3))
                {
                    throw new VmmException("Memory Read Failed!");
                }
            }
            catch (VmmException)
            {
                throw;
            }
        }
        /// <summary>
        /// Read memory into a buffer and validate the right bytes were received.
        /// </summary>
        public static unsafe byte[] ReadBufferEnsureE(ulong addr, int size)
        {
            const int ValidationCount = 3;

            try
            {
                if (BaseMemoryHolder.MemoryBase == null)
                    throw new Exception("[DMA] BaseMemoryHolder.MemoryBase is not initialized!");

                BaseMemoryHolder.MemoryBase.ThrowIfVmmDisposed();

                byte[][] buffers = new byte[ValidationCount][];
                for (int i = 0; i < ValidationCount; i++)
                {
                    buffers[i] = BaseMemoryHolder.MemoryBase._hVMM.MemRead(
                        BaseMemoryHolder.MemoryBase.ProcessPID,
                        addr,
                        (uint)size,
                        out uint bytesRead,
                        VmmFlags.NOCACHE);

                    if (bytesRead != size)
                        throw new Exception($"Incomplete memory read ({bytesRead}/{size}) at 0x{addr:X}");
                }

                // Validation: ensure all reads match
                for (int i = 1; i < ValidationCount; i++)
                {
                    if (!buffers[i].SequenceEqual(buffers[0]))
                    {
                        Log.WriteLine($"[WARN] ReadBufferEnsure() -> 0x{addr:X} failed memory consistency check.");
                        return null;
                    }
                }

                return buffers[0];
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR reading buffer at 0x{addr:X}", ex);
            }
        }


        /// <summary>
        /// Read a chain of pointers and get the final result.
        /// </summary>
        public ulong ReadPtrChain(ulong addr, uint[] offsets, bool useCache = true)
        {
            var pointer = addr; // push ptr to first address value
            for (var i = 0; i < offsets.Length; i++)
                pointer = ReadPtr(pointer + offsets[i], useCache);

            return pointer;
        }

        /// <summary>
        /// Resolves a pointer and returns the memory address it points to.
        /// </summary>
        public ulong ReadPtr(ulong addr, bool useCache = true)
        {
            var pointer = ReadValue<ulong>(addr, useCache);
            pointer.ThrowIfInvalidVirtualAddress();
            return pointer;
        }
        public unsafe T Read<T>(ulong address) where T : unmanaged
        {
            ThrowIfVmmDisposed();
            var size = (uint)Unsafe.SizeOf<T>();
            var bytes = _hVMM.MemRead(ProcessPID, address, size, out _, VmmFlags.NOCACHE);
            if (bytes == null || bytes.Length != size)
                throw new ArgumentException($"Failed to read {typeof(T).Name} from 0x{address:X}");

            unsafe
            {
                fixed (byte* ptr = bytes)
                {
                    return *(T*)ptr;
                }
            }
        }
        /// <summary>
        /// Read null terminated UTF8 string.
        /// </summary>
        public string ReadUtf8String(ulong addr, int cb, bool useCache = true) // read n bytes (string)
        {
            ThrowIfVmmDisposed();
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cb, 0x1000, nameof(cb));
            var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            return _hVMM.MemReadString(ProcessPID, addr, cb, Encoding.UTF8, flags) ??
                throw new VmmException("Memory Read Failed!");
        }
        /// <summary>
        /// Read value type/struct from specified address.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to read from.</param>
        public unsafe T ReadValue<T>(ulong addr, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            ThrowIfVmmDisposed();
            var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            return _hVMM.MemReadValue<T>(ProcessPID, addr, flags);
        }

        public ulong FindDataXref(
            ulong targetAddress,
            string moduleName = "UnityPlayer.dll",
            int searchRange = 0x4000)
        {
            if (targetAddress == 0)
                return 0;
            ThrowIfVmmDisposed();

            ulong moduleBase = _hVMM.ProcessGetModuleBase(ProcessPID, moduleName);
            if (moduleBase == 0 || moduleBase == ulong.MaxValue)
                return 0;

            // Scan forward from the string location
            ulong scanStart = targetAddress & ~0xFFFUL; // page-align
            ulong scanEnd = scanStart + (ulong)searchRange;

            byte[] buffer;
            try
            {
                buffer = _hVMM.MemRead(
                    ProcessPID,
                    scanStart,
                    (uint)searchRange,
                    out _,
                    VmmFlags.NOCACHE);
            }
            catch
            {
                return 0;
            }

            if (buffer is null || buffer.Length < 8)
                return 0;

            for (int i = 0; i <= buffer.Length - 8; i += 8)
            {
                ulong value = BitConverter.ToUInt64(buffer, i);
                if (value == targetAddress)
                {
                    return scanStart + (ulong)i;
                }
            }

            return 0;
        }

        /// <summary>
        /// Read byref value type/struct from specified address.
        /// Result returned byref.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to read from.</param>
        public unsafe void ReadValue<T>(ulong addr, out T result, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            result = _hVMM.MemReadValue<T>(ProcessPID, addr, flags);
        }

        /// <summary>
        /// Read value type/struct from specified address multiple times to ensure the read is correct.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to read from.</param>
        public unsafe T ReadValueEnsure<T>(ulong addr)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);

            T r1 = _hVMM.MemReadValue<T>(ProcessPID, addr, VmmFlags.NOCACHE);

            Thread.SpinWait(5);

            T r2 = _hVMM.MemReadValue<T>(ProcessPID, addr, VmmFlags.NOCACHE);

            Thread.SpinWait(5);

            T r3 = _hVMM.MemReadValue<T>(ProcessPID, addr, VmmFlags.NOCACHE);

            var b1 = new ReadOnlySpan<byte>(&r1, cb);
            var b2 = new ReadOnlySpan<byte>(&r2, cb);
            var b3 = new ReadOnlySpan<byte>(&r3, cb);
            if (!b1.SequenceEqual(b2) || !b1.SequenceEqual(b3))
                throw new VmmException("Memory Read Failed!");

            return r1;
        }

        /// <summary>
        /// Read byref value type/struct from specified address multiple times to ensure the read is correct.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to read from.</param>
        public unsafe void ReadValueEnsure<T>(ulong addr, out T result)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);

            T r1 = _hVMM.MemReadValue<T>(ProcessPID, addr, VmmFlags.NOCACHE);

            Thread.SpinWait(5);

            T r2 = _hVMM.MemReadValue<T>(ProcessPID, addr, VmmFlags.NOCACHE);

            Thread.SpinWait(5);

            T r3 = _hVMM.MemReadValue<T>(ProcessPID, addr, VmmFlags.NOCACHE);

            var b1 = new ReadOnlySpan<byte>(&r1, cb);
            var b2 = new ReadOnlySpan<byte>(&r2, cb);
            var b3 = new ReadOnlySpan<byte>(&r3, cb);

            if (!b1.SequenceEqual(b2) || !b1.SequenceEqual(b3))
                throw new VmmException("Memory Read Failed!");

            result = r1;
        }
        public bool TryReadValueEnsure<T>(ulong addr, out T result) where T : unmanaged
        {
            try
            {
                ReadValueEnsure(addr, out result);
                return true;
            }
            catch
            {
                result = default;
                return false;
            }
        }
        /// <summary>
        /// Read null terminated string (utf-8/default).
        /// </summary>
        /// <param name="length">Number of bytes to read.</param>
        /// <exception cref="Exception"></exception>
        public string ReadString(ulong addr, int length, bool useCache = true) // read n bytes (string)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, (int)0x1000, nameof(length));
            Span<byte> buffer = stackalloc byte[length];
            buffer.Clear();
            ReadBuffer(addr, buffer, useCache, true);
            var nullIndex = buffer.IndexOf((byte)0);
            return nullIndex >= 0
                ? Encoding.UTF8.GetString(buffer.Slice(0, nullIndex))
                : Encoding.UTF8.GetString(buffer);
        }
        /// <summary>
        /// Read UnityEngineString structure
        /// </summary>
        public string ReadUnityString(ulong addr, int length = 64, bool useCache = true)
        {
            if (length % 2 != 0)
                length++;
            length *= 2; // Unicode 2 bytes per char
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, (int)0x1000, nameof(length));
            Span<byte> buffer = stackalloc byte[length];
            buffer.Clear();
            ReadBuffer(addr + 0x14, buffer, useCache, true);
            var nullIndex = buffer.FindUtf16NullTerminatorIndex();
            return nullIndex >= 0
                ? Encoding.Unicode.GetString(buffer.Slice(0, nullIndex))
                : Encoding.Unicode.GetString(buffer);
        }

        /// <summary>
        /// Searches for a pattern signature within a specific module.
        /// Convenience overload for module-based scanning.
        /// </summary>
        /// <param name="signature">Pattern signature in the format "AA BB ?? DD" where ?? represents a wildcard.</param>
        /// <param name="moduleName">Name of the module to search (e.g., "UnityPlayer.dll")</param>
        /// <returns>Address where the pattern was found, or 0 if not found.</returns>
        public ulong FindSignature(string signature, string moduleName)
        {
            var matches = FindSignatures(signature, moduleName, 1);
            return matches.Length != 0 ? matches[0] : 0;
        }

        /// <summary>
        /// Searches for all matches of a pattern signature within a specific module.
        /// </summary>
        /// <param name="signature">Pattern signature in the format "AA BB ?? DD" where ?? represents a wildcard.</param>
        /// <param name="moduleName">Name of the module to search (e.g., "UnityPlayer.dll")</param>
        /// <param name="maxMatches">Maximum number of matches to return.</param>
        /// <returns>All matching addresses, or an empty array when no match was found.</returns>
        public ulong[] FindSignatures(string signature, string moduleName, int maxMatches = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(signature) || maxMatches <= 0)
                return Array.Empty<ulong>();

            if (!TryParseSignature(signature, out var pattern))
                return Array.Empty<ulong>();

            try
            {
                var moduleBase = _hVMM.ProcessGetModuleBase(ProcessPID, moduleName);
                if (moduleBase == 0 || moduleBase == ulong.MaxValue)
                {
                    Log.WriteLine($"[Signature] Module {moduleName} not found");
                    return Array.Empty<ulong>();
                }

                // Resolve actual module image size to avoid scanning 200MB of extraneous memory
                const ulong DEFAULT_SEARCH_SIZE = 0xC800000; // 200MB fallback
                const ulong CHUNK_SIZE = 0x400000; // 4MB chunks for lower DMA latency & faster early break
                ulong searchSize = DEFAULT_SEARCH_SIZE;

                try
                {
                    var modules = _hVMM.Map_GetModule(ProcessPID, false);
                    if (modules != null)
                    {
                        foreach (var mod in modules)
                        {
                            if (mod.vaBase == moduleBase || string.Equals(mod.sText, moduleName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (mod.cbImageSize > 0)
                                {
                                    searchSize = mod.cbImageSize;
                                    break;
                                }
                            }
                        }
                    }

                    if (searchSize == DEFAULT_SEARCH_SIZE)
                    {
                        // Fallback: query PE header OptionalHeader.SizeOfImage (+0x50 from NT headers)
                        int e_lfanew = _hVMM.MemReadValue<int>(ProcessPID, moduleBase + 0x3C, VmmFlags.NONE);
                        if (e_lfanew > 0 && e_lfanew < 0x1000)
                        {
                            uint peSize = _hVMM.MemReadValue<uint>(ProcessPID, moduleBase + (ulong)e_lfanew + 0x50, VmmFlags.NONE);
                            if (peSize > 0 && peSize < 0x40000000) // Sanity check < 1GB
                            {
                                searchSize = peSize;
                            }
                        }
                    }
                }
                catch
                {
                    searchSize = DEFAULT_SEARCH_SIZE;
                }

                ulong rangeEnd = moduleBase + searchSize;
                int overlap = Math.Max(0x100, pattern.Length - 1);
                ulong step = CHUNK_SIZE > (ulong)overlap ? CHUNK_SIZE - (ulong)overlap : CHUNK_SIZE;

                var results = new List<ulong>(Math.Min(maxMatches, 64));

                // Search in chunks to avoid DMA read limits and exit early when maxMatches reached
                for (ulong chunkStart = moduleBase; chunkStart < rangeEnd && results.Count < maxMatches; chunkStart += step)
                {
                    ulong chunkEnd = Math.Min(chunkStart + CHUNK_SIZE, rangeEnd);
                    if (chunkEnd <= chunkStart)
                        break;

                    var chunkMatches = FindSignatures(pattern, chunkStart, chunkEnd, ProcessPID, maxMatches - results.Count);

                    foreach (var match in chunkMatches)
                    {
                        if (results.Count == 0 || results[^1] != match)
                            results.Add(match);

                        if (results.Count >= maxMatches)
                            break;
                    }
                }

                return results.ToArray();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Signature] Error searching module {moduleName}: {ex.Message}");
                return Array.Empty<ulong>();
            }
        }

        /// <summary>
        /// Searches for a pattern signature in memory within the specified address range.
        /// </summary>
        /// <param name="signature">Pattern signature in the format "AA BB ?? DD" where ?? represents a wildcard.</param>
        /// <param name="rangeStart">Start address of the search range.</param>
        /// <param name="rangeEnd">End address of the search range.</param>
        /// <param name="process">The process to read memory of.</param>
        /// <returns>Address where the pattern was found, or 0 if not found.</returns>
        public ulong FindSignature(string signature, ulong rangeStart, ulong rangeEnd, uint pid)
        {
            var matches = FindSignatures(signature, rangeStart, rangeEnd, pid, 1);
            return matches.Length != 0 ? matches[0] : 0;
        }

        /// <summary>
        /// Searches for all matches of a pattern signature in memory within the specified address range.
        /// </summary>
        /// <param name="signature">Pattern signature in the format "AA BB ?? DD" where ?? represents a wildcard.</param>
        /// <param name="rangeStart">Start address of the search range.</param>
        /// <param name="rangeEnd">End address of the search range.</param>
        /// <param name="pid">The process to read memory of.</param>
        /// <param name="maxMatches">Maximum number of matches to return.</param>
        /// <returns>All matching addresses, or an empty array when no match was found.</returns>
        public ulong[] FindSignatures(string signature, ulong rangeStart, ulong rangeEnd, uint pid, int maxMatches = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(signature) || rangeStart >= rangeEnd || maxMatches <= 0)
                return Array.Empty<ulong>();

            if (!TryParseSignature(signature, out var pattern))
                return Array.Empty<ulong>();

            return FindSignatures(pattern, rangeStart, rangeEnd, pid, maxMatches);
        }

        private ulong[] FindSignatures(byte?[] pattern, ulong rangeStart, ulong rangeEnd, uint pid, int maxMatches)
        {
            if (pattern.Length == 0 || rangeStart >= rangeEnd || maxMatches <= 0)
                return Array.Empty<ulong>();

            try
            {
                byte[] buffer = _hVMM.MemRead(pid, rangeStart, (uint)(rangeEnd - rangeStart), out _, VmmFlags.NOCACHE);
                if (buffer is null || buffer.Length < pattern.Length)
                    return Array.Empty<ulong>();

                var matches = new List<ulong>(Math.Min(maxMatches, 32));
                int lastStart = buffer.Length - pattern.Length;

                for (int i = 0; i <= lastStart; i++)
                {
                    bool isMatch = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        var expected = pattern[j];
                        if (expected.HasValue && buffer[i + j] != expected.Value)
                        {
                            isMatch = false;
                            break;
                        }
                    }

                    if (!isMatch)
                        continue;

                    matches.Add(rangeStart + (ulong)i);
                    if (matches.Count >= maxMatches)
                        break;
                }

                return matches.ToArray();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[DMA] Error in FindSignatures: {ex.Message}");
                return Array.Empty<ulong>();
            }
        }

        private static bool TryParseSignature(string signature, out byte?[] pattern)
        {
            var parts = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                pattern = Array.Empty<byte?>();
                return false;
            }

            pattern = new byte?[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part is "?" or "??")
                {
                    pattern[i] = null;
                    continue;
                }

                if (part.Length != 2 || !byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var b))
                {
                    pattern = Array.Empty<byte?>();
                    return false;
                }

                pattern[i] = b;
            }

            return true;
        }

        /// <summary>
        /// Converts a hex string to a byte value.
        /// </summary>
        private static byte GetByte(ReadOnlySpan<char> hex)
        {
            if (hex.Length < 2)
                return 0;

            byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out byte value);
            return value;
        }
        #endregion

        #region WriteMethods

        /// <summary>
        /// Write value type/struct to specified address, and ensure it is written.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to write to.</param>
        /// <param name="value">Value to write.</param>
        public unsafe void WriteValueEnsure<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);
            try
            {
                var b1 = new ReadOnlySpan<byte>(&value, cb);
                const int retryCount = 3;
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        WriteValue(addr, value);
                        Thread.SpinWait(5);
                        T temp = ReadValue<T>(addr, false);
                        var b2 = new ReadOnlySpan<byte>(&temp, cb);
                        if (b1.SequenceEqual(b2))
                        {
                            return; // SUCCESS
                        }
                    }
                    catch { }
                }
                throw new VmmException("Memory Write Failed!");
            }
            catch (VmmException)
            {
                throw;
            }
        }

        /// <summary>
        /// Write byref value type/struct to specified address, and ensure it is written.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to write to.</param>
        /// <param name="value">Value to write.</param>
        public unsafe void WriteValueEnsure<T>(ulong addr, ref T value)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);
            try
            {
                fixed (void* pb = &value)
                {
                    var b1 = new ReadOnlySpan<byte>(pb, cb);
                    const int retryCount = 3;
                    for (int i = 0; i < retryCount; i++)
                    {
                        try
                        {
                            WriteValue(addr, ref value);
                            Thread.SpinWait(5);
                            T temp = ReadValue<T>(addr, false);
                            var b2 = new ReadOnlySpan<byte>(&temp, cb);
                            if (b1.SequenceEqual(b2))
                            {
                                return; // SUCCESS
                            }
                        }
                        catch { }
                    }
                    throw new VmmException("Memory Write Failed!");
                }
            }
            catch (VmmException)
            {
                throw;
            }
        }
        public unsafe bool TryWriteValueEnsure<T>(ulong addr, ref T value)
            where T : unmanaged
        {
            int cb = sizeof(T);
            try
            {
                fixed (void* pb = &value)
                {
                    var b1 = new ReadOnlySpan<byte>(pb, cb);
                    const int retryCount = 3;
                    for (int i = 0; i < retryCount; i++)
                    {
                        try
                        {
                            WriteValue(addr, ref value);
                            Thread.SpinWait(5);
                            T temp = ReadValue<T>(addr, false);
                            var b2 = new ReadOnlySpan<byte>(&temp, cb);
                            if (b1.SequenceEqual(b2))
                                return true;
                        }
                        catch { }
                    }
                }
            }
            catch (VmmException)
            {
            }
            return false;
        }

        /// <summary>
        /// Write value type/struct to specified address.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to write to.</param>
        /// <param name="value">Value to write.</param>
        public unsafe void WriteValue<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            if (!SharedProgram.Config?.MemWritesEnabled ?? false)
                throw new Exception("Memory Writing is Disabled!");
            ThrowIfVmmDisposed();

            int size = sizeof(T);
            Span<byte> buffer = stackalloc byte[size];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer), value);
            _hVMM.MemWriteSpan(ProcessPID, addr, buffer);
        }

        /// <summary>
        /// Write byref value type/struct to specified address.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to write to.</param>
        /// <param name="value">Value to write.</param>
        public unsafe void WriteValue<T>(ulong addr, ref T value)
            where T : unmanaged, allows ref struct
        {
            if (!SharedProgram.Config?.MemWritesEnabled ?? false)
                throw new Exception("Memory Writing is Disabled!");
            ThrowIfVmmDisposed();

            int size = sizeof(T);
            Span<byte> buffer = stackalloc byte[size];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer), value);
            _hVMM.MemWriteSpan(ProcessPID, addr, buffer);
        }

        /// <summary>
        /// Write byte array buffer to Memory Address.
        /// </summary>
        /// <param name="addr">Address to write to.</param>
        /// <param name="buffer">Buffer to write.</param>
        public unsafe void WriteBuffer<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!SharedProgram.Config?.MemWritesEnabled ?? false)
                throw new Exception("Memory Writing is Disabled!");
            ThrowIfVmmDisposed();
            _hVMM.MemWriteSpan(ProcessPID, addr, buffer);
        }

        /// <summary>
        /// Write a buffer to the specified address and validate the right bytes were written.
        /// </summary>
        /// <param name="addr">Address to write to.</param>
        /// <param name="buffer">Buffer to write.</param>
        public void WriteBufferEnsure<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            int cb = SizeChecker<T>.Size * buffer.Length;
            try
            {
                Span<byte> temp = cb > 0x1000 ? new byte[cb] : stackalloc byte[cb];
                ReadOnlySpan<byte> b1 = MemoryMarshal.Cast<T, byte>(buffer);
                const int retryCount = 3;
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        WriteBuffer(addr, buffer);
                        Thread.SpinWait(5);
                        temp.Clear();
                        ReadBuffer(addr, temp, false, false);
                        if (temp.SequenceEqual(b1))
                        {
                            return; // SUCCESS
                        }
                    }
                    catch { }
                }
                throw new VmmException("Memory Write Failed!");
            }
            catch (VmmException)
            {
                throw;
            }
        }
        /// <summary>
        /// Write a buffer to the specified address and validate the right bytes were written.
        /// </summary>
        /// <param name="addr">Address to write to.</param>
        /// <param name="buffer">Buffer to write.</param>
        public bool WriteBufferEnsureB(ulong addr, byte[] buffer)
        {
            const int RetryCount = 3;

            try
            {
                bool success = false;
                for (int i = 0; i < RetryCount; i++)
                {
                    WriteBuffer<byte>(addr, buffer);

                    // Validate the bytes were written properly
                    var validateBytes = ReadBufferEnsureE(addr, buffer.Length);

                    if (validateBytes is null || !validateBytes.SequenceEqual(buffer))
                    {
                        Log.WriteLine($"[WARN] WriteBufferEnsure() -> 0x{addr:X} did not pass validation on try {i + 1}!");
                        success = false;
                        continue;
                    }

                    success = true;
                    break;
                }

                return success;
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR writing bytes at 0x{addr:X}", ex);
            }
        }
        #endregion

        #region Misc

        /// <summary>
        /// Get an Export from this process.
        /// </summary>
        /// <param name="module"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public ulong GetExport(string module, string name)
        {
            ThrowIfVmmDisposed();
            var export = _hVMM.ProcessGetProcAddress(ProcessPID, module, name);
            export.ThrowIfInvalidVirtualAddress();
            return export;
        }

        /// <summary>
        /// Close the FPGA Connection.
        /// </summary>
        public void CloseFPGA()
        {
            _isDisposed = true;
            _hVMM?.Dispose();
        }

        /// <summary>
        /// Get a Vmm Scatter Handle.
        /// </summary>
        /// <param name="flags"></param>
        /// <param name="pid"></param>
        /// <returns></returns>
        public VmmScatter GetScatter(VmmFlags flags)
        {
            ThrowIfVmmDisposed();
            return new VmmScatter(_hVMM, ProcessPID, flags);
        }

        #endregion

        #region Memory Macros

        /// <summary>
        /// The PAGE_ALIGN macro takes a virtual address and returns a page-aligned
        /// virtual address for that page.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong PAGE_ALIGN(ulong va) => va & ~(0x1000ul - 1);

        /// <summary>
        /// The ADDRESS_AND_SIZE_TO_SPAN_PAGES macro takes a virtual address and size and returns the number of pages spanned by
        /// the size.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ADDRESS_AND_SIZE_TO_SPAN_PAGES(ulong va, uint size) =>
            (uint)(BYTE_OFFSET(va) + size + (0x1000ul - 1) >> (int)12);

        /// <summary>
        /// The BYTE_OFFSET macro takes a virtual address and returns the byte offset
        /// of that address within the page.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint BYTE_OFFSET(ulong va) => (uint)(va & 0x1000ul - 1);

        /// <summary>
        /// Returns a length aligned to 8 bytes.
        /// Always rounds up.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint AlignLength(uint length) => (length + 7) & ~7u;

        /// <summary>
        /// Returns an address aligned to 8 bytes.
        /// Always the next aligned address.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong AlignAddress(ulong address) => (address + 7) & ~7ul;

        #endregion
    }
}