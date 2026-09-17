// DMA Base - GameAssembly metadata offsets for Il2CppClass.

namespace DmaBase.Unity.IL2CPP
{
    /// <summary>
    /// IL2CPP metadata offsets inside GameAssembly.dll.
    /// Update <see cref="Special.TypeInfoTableRva"/> and class field offsets when the game updates.
    /// </summary>
    public static class Il2CppOffsets
    {
        /// <summary>GameAssembly-wide constants (RVAs, globals).</summary>
        public static class Special
        {
            /// <summary>RVA of the TypeInfoTable pointer in GameAssembly.dll.</summary>
            public const uint TypeInfoTableRva = 0;
        }

        /// <summary>Offsets on Il2CppClass structures.</summary>
        public static class Il2CppClass
        {
            public const uint Name = 0x10;
            public const uint Namespace = 0x18;
            public const uint StaticFields = 0xB8;
        }
    }
}
