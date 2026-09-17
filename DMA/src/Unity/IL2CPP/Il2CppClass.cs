// DMA Base - IL2CPP class lookup via TypeInfoTable + scatter name reads.

using System.Diagnostics;
using DmaBase.DMA;
using DmaBase.Unity;
using DmaBase.DMA.ScatterAPI;
using System.Collections.Concurrent;
using DmaBase.Misc;

namespace DmaBase.Unity.IL2CPP
{
    public static class Il2CppClass
    {
        private static readonly ConcurrentDictionary<string, ulong> _cache = new();

        private static volatile bool _loaded = false;

        private static ulong[] _typeTable = Array.Empty<ulong>();
        private static ulong[] _namePtr = Array.Empty<ulong>();
        private static ulong[] _nsPtr = Array.Empty<ulong>();

        private static ulong _lastGA = 0;
        private static ulong _lastTablePtr = 0;

        private static readonly object _sync = new();

        private const int MaxClasses = 80000;
        private const int MaxNameLen = 128;

        // -------------------------------------------------------
        // RESET (never spam, never called on scatter miss)
        // -------------------------------------------------------
        private static void Reset(string reason)
        {
            Debug.WriteLine($"[Il2CppClass] Reset ???? {reason}");

            _loaded = false;
            _typeTable = Array.Empty<ulong>();
            _namePtr = Array.Empty<ulong>();
            _nsPtr = Array.Empty<ulong>();
            _cache.Clear();

            _lastGA = 0;
            _lastTablePtr = 0;
        }

        // -------------------------------------------------------
        // SAFE LOADER ?? identical to your version + relocation guards
        // -------------------------------------------------------
        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            lock (_sync)
            {
                if (_loaded)
                    return;

                try
                {
                    ulong ga = DmaMemory.GameAssemblyBase;
                    if (ga == 0)
                        return;

                    // GameAssembly moved? (map reload / Unity reload)
                    if (_lastGA != 0 && _lastGA != ga)
                    {
                        Reset($"GameAssemblyBase moved (old=0x{_lastGA:X}, new=0x{ga:X})");
                    }
                    _lastGA = ga;

                    // Load TypeInfoTable pointer
                    ulong tablePtr = DmaMemory.ReadPtr(ga + Il2CppOffsets.Special.TypeInfoTableRva);
                    if (!tablePtr.IsValidVirtualAddress())
                    {
                        Reset("TypeInfoTablePtr invalid");
                        return;
                    }

                    // Moved?
                    if (_lastTablePtr != 0 && _lastTablePtr != tablePtr)
                    {
                        Reset($"TypeInfoTablePtr moved (old=0x{_lastTablePtr:X}, new=0x{tablePtr:X})");
                    }
                    _lastTablePtr = tablePtr;

                    // YOUR working logic:
                    var tmp = DmaMemory.ReadArray<ulong>(tablePtr, MaxClasses, false);
                    if (tmp == null)
                        return;

                    int actualCount = tmp.Count(x => x.IsValidVirtualAddress());
                    if (actualCount == 0)
                        return;

                    Array.Resize(ref tmp, actualCount);
                    _typeTable = tmp;

                    _namePtr = new ulong[actualCount];
                    _nsPtr = new ulong[actualCount];

                    // Scatter read name + namespace pointers
                    var ptrEntries = new ScatterEntry.PtrEntry[actualCount * 2];
                    int ei = 0;

                    for (int i = 0; i < actualCount; i++)
                    {
                        ulong typePtr = _typeTable[i];
                        ptrEntries[ei++] = ScatterEntry.CreatePtr(typePtr + Il2CppOffsets.Il2CppClass.Name);
                        ptrEntries[ei++] = ScatterEntry.CreatePtr(typePtr + Il2CppOffsets.Il2CppClass.Namespace);
                    }

                    var scatterList = new IScatterEntry[ptrEntries.Length];
                    for (int j = 0; j < ptrEntries.Length; j++)
                        scatterList[j] = ptrEntries[j].Entry;

                    DmaMemory.ReadScatter(scatterList, useCache: false);

                    // DO NOT treat scatter read failures as fatal.
                    // Only check after commit.
                    int k = 0;
                    for (int i = 0; i < actualCount; i++)
                    {
                        _namePtr[i] = ptrEntries[k++].Transfer();
                        _nsPtr[i] = ptrEntries[k++].Transfer();
                    }

                    _loaded = true;
                }
                catch (Exception ex)
                {
                    Reset($"Exception: {ex.Message}");
                }
            }
        }

        // -------------------------------------------------------
        // FIND CLASS: Recovery if pointers become stale
        // -------------------------------------------------------
        public static ulong Find(string asm, string className, out ulong klassPtr)
        {
            klassPtr = 0;

            // Cache hit
            if (_cache.TryGetValue(className, out klassPtr))
                return klassPtr;

            EnsureLoaded();
            if (!_loaded)
                return 0;

            int count = _typeTable.Length;

            for (int i = 0; i < count; i++)
            {
                ulong nameP = _namePtr[i];
                ulong nsP = _nsPtr[i];

                // Pointer went stale? Reset + reload
                if (!nameP.IsValidVirtualAddress())
                {
                    Reset("namePtr stale during Find()");
                    return 0;
                }

                string name = DmaMemory.ReadString(nameP, MaxNameLen, false);

                if (string.IsNullOrEmpty(name))
                    continue;

                string ns = DmaMemory.ReadString(nsP, MaxNameLen, false);
                string fq = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                if (fq.Equals(className, StringComparison.OrdinalIgnoreCase))
                {
                    klassPtr = _typeTable[i];
                    _cache[className] = klassPtr;
                    return klassPtr;
                }
            }

            return 0;
        }

        // -------------------------------------------------------
        public static ulong GetStaticFieldData(ulong klassPtr)
        {
            if (!klassPtr.IsValidVirtualAddress())
            {
                Reset("klassPtr stale in GetStaticFieldData");
                return 0;
            }

            ulong fields = DmaMemory.ReadPtr(klassPtr + Il2CppOffsets.Il2CppClass.StaticFields);
            return fields.IsValidVirtualAddress() ? fields : 0;
        }

        public static ulong GetStaticInstance(ulong klassPtr, int offset)
        {
            ulong data = GetStaticFieldData(klassPtr);
            if (!data.IsValidVirtualAddress())
                return 0;

            ulong inst = DmaMemory.ReadPtr(data + (ulong)offset);
            return inst.IsValidVirtualAddress() ? inst : 0;
        }

        public static ulong FindStaticData(string asm, string name)
        {
            var ptr = Find(asm, name, out var klass);
            return klass == 0 ? 0 : GetStaticFieldData(klass);
        }
        public static void ForceReset()
        {
            lock (_sync)
            {
                _loaded = false;
                _cache.Clear();
                _typeTable = Array.Empty<ulong>();
                _namePtr = Array.Empty<ulong>();
                _nsPtr = Array.Empty<ulong>();
            }
        }
    }
}