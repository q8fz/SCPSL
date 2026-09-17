// DMA Base - per-game Unity/IL2CPP offsets (update when the target game patches).

namespace DmaBase.Unity
{
    /// <summary>
    /// Centralized IL2CPP Unity offsets (Dec 2025).
    /// UPDATE THESE when game updates break functionality.
    /// Verified against common Unity IL2CPP layouts; retune per game build.
    /// </summary>
    public readonly struct UnityOffsets
    {
        #region ObjectClass (Base for all Unity objects)
        /// <summary>
        /// Base ObjectClass offsets - starting point for most pointer chains.
        /// </summary>
        public readonly struct ObjectClass
        {
            public const uint MonoBehaviourOffset = 0x10;  // ObjectClass ?? MonoBehaviour

            /// <summary>Chain to read class name from ObjectClass: [0x0, 0x10] ?? string pointer</summary>
            public static readonly uint[] ToNamePtr = [0x0, 0x10];
        }
        #endregion

        #region Component (MonoBehaviour base)
        public readonly struct Component
        {
            public const uint Size = 0x58; // IL2CPP: 0x58, Mono was: 0x38
            public static readonly uint[] To_NativeClassName = new uint[] { 0x0, 0x10 }; // String

            // IL2CPP Component offsets (from XM Dec 2025)
            public const uint ObjectClassOffset = 0x20; // IL2CPP: 0x30 - Component ?? ObjectClass (InteractiveClass)
            public const uint GameObject = 0x58;        // IL2CPP: 0x58 - Component ?? GameObject (Mono was: 0x30)
        }
        #endregion

        #region GameObject
        public readonly struct GameObject
        {
            // IL2CPP GameObject structure offsets (Dec 2025)
            public const uint ObjectClassOffset = 0x80;
            public const uint ComponentsOffset = 0x58;   // GameObject ?? ComponentArray
            public const uint NameOffset = 0x88;         // GameObject ?? Name string pointer
        }
        #endregion

        #region ComponentArray
        public readonly struct ComponentArray
        {
            public const uint Items = 0x8;  // First component entry in array
        }
        #endregion

        #region Transform
        public readonly struct Transform
        {
            public const uint ObjectClassOffset = 0x20;  // Transform component ?? ObjectClass
            public const uint InternalOffset = 0x10;     // Final dereference ?? TransformInternal
        }
        #endregion

        #region ModuleBase (UnityPlayer.dll offsets)
        public readonly struct ModuleBase
        {
            // IL2CPP OFFSETS (Dec 2025 - from XM's accurate offsets)
            // NOTE: GameObjectManager uses signature scanning with this as fallback
            // SIGNATURE: "48 89 05 ?? ?? ?? ?? 48 83 C4 ?? C3 33 C9"
            public const uint GameObjectManager = 0x1A233A0; // IL2CPP GOM (XM Dec 2025)

            // Camera offsets (less frequently changed)
            public const uint AllCameras = 0x19F3080; // IL2CPP AllCameras (XM Dec 2025)

            // Unity 6 IL2CPP InputManager (verified in UnityPlayer.dll 6000.0.43.9905963)
            public const uint InputManager = 0x1F0C2D8;
            public const uint GfxDevice = 0x1CF9F48; // g_MainGfxDevice , Type GfxDeviceClient
        }
        #endregion
        public readonly struct UnityInputManager
        {
            /// <summary>
            /// Unique signature in UnityPlayer.dll resolving to InputManager pointer.
            /// 48 8B 05 [disp32] 4C 8B C1 49 C1 E8 05 48 8B 50 70
            /// </summary>
            public const string Signature = "48 8B 05 ? ? ? ? 4C 8B C1 49 C1 E8 05 48 8B 50 70";

            /// <summary>
            /// Pointer to uint32 bitmask array of currently held keys (persists across hold frames).
            /// Unity dynamic_array<UInt32, 0> m_Keys at +0x50.
            /// Bit test: (m_Keys[key >> 5] & (1u << (key & 31))) != 0
            /// </summary>
            public const uint Keys = 0x50;
            public const uint KeysCapacity = 0x58;
            public const uint KeysCount = 0x60;

            /// <summary>
            /// Pointer to uint32 bitmask array of keys pressed down in the current/initial frame.
            /// Unity dynamic_array<UInt32, 0> m_KeysDown at +0x70.
            /// Bit test: (m_KeysDown[key >> 5] & (1u << (key & 31))) != 0
            /// </summary>
            public const uint KeysDown = 0x70;
            public const uint KeysDownCapacity = 0x78;
            public const uint KeysDownCount = 0x80;

            /// <summary>
            /// Pointer to uint32 bitmask array of keys released in the current frame.
            /// Unity dynamic_array<UInt32, 0> m_KeysUp at +0x90.
            /// </summary>
            public const uint KeysUp = 0x90;
            public const uint KeysUpCapacity = 0x98;
            public const uint KeysUpCount = 0xA0;
        }
        public readonly struct TransformInternal
        {
            // IL2CPP offset (from XM Dec 2025)
            public const uint TransformAccess = 0x90; // IL2CPP: 0x90, Mono was: 0x38
        }
        public readonly struct TransformAccess
        {
            // Unity 2021/2022 IL2CPP offsets
            public const uint IndexOffset = 0x30;      // nativeTr + 0x30 -> int Index
            public const uint HierarchyOffset = 0x28;  // nativeTr + 0x28 -> TransformHierarchy*

            public const uint Vertices = 0x18;         // TransformHierarchy + 0x18 -> Vertices* (TrsX[])
            public const uint Indices = 0x20;          // TransformHierarchy + 0x20 -> Indices* (int[])
            public const uint Capacity = 0x10;         // TransformHierarchy + 0x10 -> int Capacity
        }
        public readonly struct SkinnedMeshRenderer // SkinnedMeshRenderer : Renderer
        {
            public const uint Renderer = 0x10; // Renderer : Unity::Component
        }
        public readonly struct Renderer // Renderer : Unity::Component
        {
            public const uint Materials = 0x170; // m_Materials : dynamic_array<PPtr<Material>,0>
            public const uint Count = 0x180; // Extends from m_Materials type (0x20 length?)
            const int IL2CPP_ARRAY_LENGTH = 0x18;
            const int IL2CPP_ARRAY_DATA = 0x20;
        }
        public static class Il2CppArray
        {
            public const uint Length = 0x18;
            public const uint Data = 0x20;
        }
        public readonly struct Behaviour
        {
            public const uint IsEnabled = 0x38; // bool, Behaviour : m_Enabled
            public const uint IsAdded = 0x39; // bool, Behaviour : m_IsAdded
        }
        public readonly struct Camera
        {
            // IL2CPP offsets (updated Dec 2025) ? resolved dynamically via sig scan, hardcoded as fallback
            public static uint ViewMatrix = 0x128;      // IL2CPP: 0x128 (was 0x100 in Mono)
            public static uint FOV = 0x170;             // Unity 6 IL2CPP: 0x170 (verified in UnityPlayer.dll native Camera::get/set_fieldOfView)
            public const uint DirtyFlags = 0x500;       // Unity 6: 0x500 (ushort: 0x101 = projection matrix recalculation required)
            public const uint LastPosition = 0x454;    // TODO: Verify for IL2CPP
            public static uint AspectRatio = 0x518;     // IL2CPP: 0x518 (was 0x4F0 in Mono)
            public const uint ZoomLevel = 0xE8;        // IL2CPP: Zoom level (used for scope detection)
            public const uint DerefIsAddedOffset = 0x35; // IL2CPP: IsAdded offset after +0x10 dereference (DEC 2025 from Camera-PWA)
            public const uint OcclusionCulling = 0x524; // bool, Camera::CopiableState -> m_OcclusionCulling
            public const uint NearClip = 0x464; // float (m_NearClip)
        }
        public readonly struct LevelSettings
        {
            public static readonly uint[] LevelSettingsChain =
            [
                GameObject.ComponentsOffset,
                0x18,
                Component.ObjectClassOffset
            ];
        }

        public readonly struct GfxDeviceClient
        {
            public const uint Viewport = 0x25A0; // m_Viewport      RectT<int> ?
        }

        public readonly struct UnityAnimator // Animator        struc ; (sizeof=0x6A0, align=0x8, copyof_18870)
        {
            public const uint Speed = 0x4B0; // 0000047C m_Speed
            public const uint GetSpeed_RVA = 0x3736A50; // UnityEngine.Animator::get_speed()
            public const uint SetSpeed_RVA = 0x2B44670; // UnityEngine.Animator::set_speed(float)            
        }

        public readonly struct SSAA // Unity.Postprocessing.Runtime Assembly in UNISPECT
        {
            public const uint OpticMaskMaterial = 0x98; // -.SSAA->_opticMaskMaterial // Offset: 0x0060 (Type: UnityEngine.Material)
        }
        public readonly struct UnityString
        {
            public const uint Length = 0x10; // int32
            public const uint Value = 0x14; // string,unicode
        }
        public readonly struct UnityList
        {
            public const uint Base = 0x10; // to UnityListBase
            public const uint Count = 0x18; // int32
        }

        public readonly struct UnityListBase
        {
            public const uint Start = 0x20; // start of list +(i * 0x8)
        }

        #region IL2CPP Pointer Chains
        // ============================================================
        // Pre-built chains for common Unity object traversals.
        // These use IL2CPP offsets and should be updated when game updates.
        // VERIFIED WORKING: Dec 2025 (XM + Camera-PWA sources)
        // ============================================================

        /// <summary>
        /// Pointer chain from a MonoBehaviour-derived object to TransformInternal.
        /// Values come from the nested consts above — retune those per Unity version.
        /// </summary>
        public static readonly uint[] TransformChain =
        [
            ObjectClass.MonoBehaviourOffset,
            Component.GameObject,
            GameObject.ComponentsOffset,
            ComponentArray.Items,
            Transform.ObjectClassOffset,
            Transform.InternalOffset
        ];

        /// <summary>
        /// Example chain toward a world/behaviour object class from a GameObject.
        /// Game-specific; verify the middle entry against your component array layout.
        /// </summary>
        public static readonly uint[] GameWorldChain =
        [
            GameObject.ComponentsOffset,
            0x18,
            Component.ObjectClassOffset
        ];

        #endregion

        #region .NET Runtime Structures (Extremely Stable)
        // These are C# runtime internal structures - virtually never change

        /// <summary>
        /// List&lt;T&gt; structure offsets (IL2CPP)
        /// </summary>
        public readonly struct ManagedList
        {
            public const uint ItemsPtr = 0x10;      // Pointer to items array
            public const uint Count = 0x18;         // Count of items (_size)
        }

        /// <summary>
        /// Array header offset to first element
        /// </summary>
        public readonly struct ManagedArray
        {
            public const uint FirstElement = 0x20;  // First element (after header)
            public const uint ElementSize = 0x8;    // Size of pointer element
        }

        /// <summary>
        /// MongoID struct offsets (UPDATED Dec 2025 from Camera-PWA)
        /// </summary>
        public readonly struct MongoID
        {
            public const uint TimeStamp = 0x00;    // uint
            public const uint Counter = 0x08;      // ulong
            public const uint StringID = 0x10;     // string pointer - CONFIRMED Dec 2025
        }

        /// <summary>
        /// HashSet&lt;T&gt; structure offsets (IL2CPP with inline MongoID storage)
        /// Used for: CompletedConditions, quest tracking, etc.
        /// </summary>
        public readonly struct IL2CPPHashSet
        {
            public const uint BucketCount = 0x18;       // int - bucket/entry count (small prime: 3, 7, 17...)
            public const uint EntriesStart = 0x20;      // Entry[0] starts here (inline storage)
            public const int EntrySize = 0x20;          // 32 bytes per entry

            /// <summary>
            /// HashSet Entry structure (inline MongoID):
            ///   +0x00: hashCode (int) + next (int)
            ///   +0x08: MongoID._bytes (12 bytes inline)
            ///   +0x14: padding (4 bytes)
            ///   +0x18: MongoID._stringID pointer
            /// </summary>
            public readonly struct Entry
            {
                public const uint HashCode = 0x00;      // int - hashCode (-1 = empty)
                public const uint Next = 0x04;          // int - next entry index
                public const uint MongoIDBytes = 0x08;  // 12 bytes - raw MongoID
                public const uint StringPtr = 0x18;     // ulong - cached string pointer
            }
        }
        public readonly struct IL2CPPHashSet2
        {
            public const uint Entries = 0x18;           // Pointer to entries array
            public const uint Count = 0x1C;             // int - count (try 0x20 or 0x3C if this fails)
            public const int EntrySize = 0x20;          // 32 bytes per entry
            public const uint EntryValueOffset = 0x08;  // MongoID value starts here in entry

            // Legacy aliases for compatibility
            public const uint BucketCount = Count;
            public const uint EntriesStart = 0x20;      // First element in entries array (after array header)

            /// <summary>
            /// HashSet Entry structure (inline MongoID):
            ///   +0x00: hashCode (int) + next (int)
            ///   +0x08: MongoID value (struct inline)
            ///     +0x00: _timeStamp (uint)
            ///     +0x08: _counter (ulong)  
            ///     +0x10: _stringID (string pointer) - UPDATED Dec 2025
            /// </summary>
            public readonly struct Entry
            {
                public const uint HashCode = 0x00;      // int - hashCode (-1 = empty)
                public const uint Next = 0x04;          // int - next entry index
                public const uint MongoIDValue = 0x08;  // MongoID struct inline
            }
        }
        /// <summary>
        /// Dictionary&lt;K,V&gt; structure offsets (IL2CPP)
        /// </summary>
        public readonly struct IL2CPPDictionary
        {
            public const uint Entries = 0x18;           // Pointer to entries array
            public const uint Count = 0x20;             // int - number of entries
            public const uint EntriesStart = 0x20;      // Offset to first entry in entries array
            public const int EntrySize = 24;            // Size of each entry (key + value + hash + next)
            public const int EntryValueOffset = 16;     // Offset to value within entry
        }
        #endregion
        public readonly struct BaseRenderer
        {
            public const uint RendererData = 0x8; // SharedRendererData (m_RendererData)
        }

        public readonly struct SharedRendererData
        {
            public const uint TransformInfo = 0x0; // TransformInfo (m_TransformInfo)
        }

        public readonly struct TransformInfo
        {
            public const uint LocalAABB = 0x98; // AABB (localAABB)
        }
    }
}
