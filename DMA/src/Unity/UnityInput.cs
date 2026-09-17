// DMA Base - Unity InputManager reader over DMA.
// Strictly READ-ONLY. Reads Unity 6 native InputManager bitmask arrays for held and frame-pressed keys.

using DmaBase.DMA;
using DmaBase.Misc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DmaBase.Unity
{
    /// <summary>
    /// Read-only Unity InputManager memory reader.
    /// Eliminates window focus checks and OS-level GetAsyncKeyState conflicts
    /// by reading the Unity engine's internal key bitmasks directly over DMA.
    /// </summary>
    public static class UnityInput
    {
        private static readonly object _sync = new();

        // 16 uints = 512 bits, covering KeyCode 0 to 511 (all standard keys and mouse buttons)
        private const int KeyBufferDwordCount = 16;
        private static readonly uint[] _keysBuffer = new uint[KeyBufferDwordCount];
        private static readonly uint[] _keysDownBuffer = new uint[KeyBufferDwordCount];

        private static ulong _unityBase;
        private static ulong _inputManagerPtr;
        private static ulong _keysPtr;
        private static ulong _keysDownPtr;
        private static long _lastRefreshTimestamp;
        private static readonly long _refreshThresholdTicks = Stopwatch.Frequency / 1000; // 1 ms throttle
        private static long _lastConnectAttemptTimestamp;
        private static readonly long _connectRetryThresholdTicks = Stopwatch.Frequency; // 1 s reconnect throttle

        // Edge detection fallback when InputManager is offline
        private static readonly bool[] _fallbackPrevState = new bool[512];

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        /// <summary>True if Unity's native InputManager and key bitmask array were successfully resolved.</summary>
        public static bool IsConnected { get; private set; }

        /// <summary>Diagnostic status message describing the current state of UnityInput.</summary>
        public static string Status { get; private set; } = "Not initialized";

        /// <summary>Address of the native Unity InputManager instance.</summary>
        public static ulong InputManagerAddress => _inputManagerPtr;

        /// <summary>Address of the m_Keys held-key bitmask array.</summary>
        public static ulong KeysAddress => _keysPtr;

        /// <summary>
        /// Initialize or re-initialize UnityInput with the target UnityPlayer.dll base address.
        /// </summary>
        public static void Initialize(ulong unityBase)
        {
            lock (_sync)
            {
                _unityBase = unityBase;
                Reset();

                if (!unityBase.IsValidVirtualAddress())
                {
                    Status = "Invalid Unity base address";
                    return;
                }

                try
                {
                    // 1. Fast-path: verified hardcoded offset in UnityPlayer.dll (takes < 0.1ms, avoids scanning 200MB)
                    ulong inputManagerGlobal = unityBase + UnityOffsets.ModuleBase.InputManager;
                    _inputManagerPtr = DmaMemory.ReadPtr(inputManagerGlobal, false);

                    if (_inputManagerPtr.IsValidVirtualAddress())
                    {
                        Log.WriteLine($"[UnityInput] InputManager located via verified offset: 0x{_inputManagerPtr:X}");
                    }
                    else
                    {
                        // 2. Fall back to signature scan only if hardcoded offset failed (engine update)
                        try
                        {
                            ulong sigAddr = DmaMemory.FindSignature(UnityOffsets.UnityInputManager.Signature, "UnityPlayer.dll");
                            if (sigAddr.IsValidVirtualAddress())
                            {
                                int rva = DmaMemory.ReadValue<int>(sigAddr + 3);
                                ulong sigGlobal = sigAddr + 7 + (ulong)rva;
                                _inputManagerPtr = DmaMemory.ReadPtr(sigGlobal, false);

                                if (_inputManagerPtr.IsValidVirtualAddress())
                                {
                                    Log.WriteLine($"[UnityInput] InputManager located via signature scan: 0x{_inputManagerPtr:X}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLine($"[UnityInput] Signature scan failed: {ex.Message}");
                        }
                    }

                    if (!_inputManagerPtr.IsValidVirtualAddress())
                    {
                        Status = "InputManager pointer unresolved (fallback active)";
                        return;
                    }

                    // 3. Resolve key bitmask arrays (m_Keys at +0x50, m_KeysDown at +0x70)
                    _keysPtr = DmaMemory.ReadPtr(_inputManagerPtr + UnityOffsets.UnityInputManager.Keys, false);
                    _keysDownPtr = DmaMemory.ReadPtr(_inputManagerPtr + UnityOffsets.UnityInputManager.KeysDown, false);

                    if (_keysPtr.IsValidVirtualAddress() || _keysDownPtr.IsValidVirtualAddress())
                    {
                        IsConnected = true;
                        Status = $"Connected (0x{_inputManagerPtr:X})";
                        Log.WriteLine($"[UnityInput] Connected: Keys=0x{_keysPtr:X}, KeysDown=0x{_keysDownPtr:X}");
                    }
                    else
                    {
                        Status = "Keys array pointer unresolved (fallback active)";
                    }
                }
                catch (Exception ex)
                {
                    Status = $"Init error: {ex.Message} (fallback active)";
                    Log.WriteLine($"[UnityInput] Init exception: {ex}");
                }
            }
        }

        /// <summary>
        /// Refreshes the local key bitmask buffers from DMA.
        /// Throttled to 1ms to prevent redundant DMA roundtrips when checking multiple keys in the same frame.
        /// </summary>
        public static void Refresh(bool force = false)
        {
            if (!IsConnected)
            {
                // Dynamic auto-reconnect if base address is known or available from DmaMemory
                ulong baseAddr = _unityBase.IsValidVirtualAddress() ? _unityBase : (DmaMemory.Ready ? DmaMemory.UnityBase : 0);
                if (baseAddr.IsValidVirtualAddress())
                {
                    long nowTick = Stopwatch.GetTimestamp();
                    if ((nowTick - _lastConnectAttemptTimestamp) >= _connectRetryThresholdTicks)
                    {
                        _lastConnectAttemptTimestamp = nowTick;
                        Initialize(baseAddr);
                    }
                }

                if (!IsConnected) return;
            }

            long now = Stopwatch.GetTimestamp();
            if (!force && (now - _lastRefreshTimestamp) < _refreshThresholdTicks)
            {
                return;
            }

            lock (_sync)
            {
                try
                {
                    if (_keysPtr.IsValidVirtualAddress())
                    {
                        DmaMemory.ReadBuffer<uint>(_keysPtr, _keysBuffer.AsSpan(), false);
                    }

                    if (_keysDownPtr.IsValidVirtualAddress())
                    {
                        DmaMemory.ReadBuffer<uint>(_keysDownPtr, _keysDownBuffer.AsSpan(), false);
                    }

                    _lastRefreshTimestamp = now;
                }
                catch
                {
                    // If a read fails (e.g. map reload), mark disconnected so fallback takes over
                    IsConnected = false;
                    Status = "Read failed (fallback active)";
                }
            }
        }

        /// <summary>
        /// Checks if a key is currently held down.
        /// Uses read-only Unity InputManager memory when connected; falls back to GetAsyncKeyState otherwise.
        /// Matching native Unity engine GetKeyInt: returns true if key is in m_Keys (held) OR m_KeysDown (initial press).
        /// </summary>
        public static bool IsKeyDown(UnityKeyCode key)
        {
            Refresh();

            int k = (int)key;
            if (IsConnected && k >= 0 && k < 512)
            {
                int dwordIdx = k >> 5;
                int bitIdx = k & 31;
                uint mask = 1u << bitIdx;
                return ((_keysBuffer[dwordIdx] & mask) != 0) || ((_keysDownBuffer[dwordIdx] & mask) != 0);
            }

            // Fallback for when Unity memory is not connected
            int vk = ToVirtualKey(key);
            if (vk != 0)
            {
                return (GetAsyncKeyState(vk) & 0x8000) != 0;
            }

            return false;
        }

        /// <summary>
        /// Checks if a key was pressed down during the current frame (edge trigger).
        /// Uses read-only Unity InputManager m_KeysDown when connected; falls back to local edge detection.
        /// </summary>
        public static bool GetKeyDown(UnityKeyCode key)
        {
            Refresh();

            int k = (int)key;
            if (IsConnected && k >= 0 && k < 512)
            {
                int dwordIdx = k >> 5;
                int bitIdx = k & 31;
                return (_keysDownBuffer[dwordIdx] & (1u << bitIdx)) != 0;
            }

            // Fallback edge detection
            int vk = ToVirtualKey(key);
            if (vk != 0 && k >= 0 && k < 512)
            {
                bool isDown = (GetAsyncKeyState(vk) & 0x8000) != 0;
                bool pressed = isDown && !_fallbackPrevState[k];
                _fallbackPrevState[k] = isDown;
                return pressed;
            }

            return false;
        }

        /// <summary>
        /// Scans the internal bitmasks and returns all keys currently held down in Unity.
        /// Combines m_Keys (held) and m_KeysDown (initial frame) to guarantee no tick drops.
        /// </summary>
        public static List<UnityKeyCode> GetHeldKeys()
        {
            var list = new List<UnityKeyCode>();
            Refresh();

            if (!IsConnected)
            {
                for (int k = 0; k < 512; k++)
                {
                    var code = (UnityKeyCode)k;
                    int vk = ToVirtualKey(code);
                    if (vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0)
                    {
                        list.Add(code);
                    }
                }
                return list;
            }

            lock (_sync)
            {
                for (int i = 0; i < KeyBufferDwordCount; i++)
                {
                    uint val = _keysBuffer[i] | _keysDownBuffer[i];
                    if (val == 0) continue;
                    for (int bit = 0; bit < 32; bit++)
                    {
                        if ((val & (1u << bit)) != 0)
                        {
                            list.Add((UnityKeyCode)((i << 5) | bit));
                        }
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Scans the internal bitmask and returns all keys pressed down during this frame.
        /// </summary>
        public static List<UnityKeyCode> GetFramePressedKeys()
        {
            var list = new List<UnityKeyCode>();
            Refresh();
            if (!IsConnected) return list;

            lock (_sync)
            {
                for (int i = 0; i < KeyBufferDwordCount; i++)
                {
                    uint val = _keysDownBuffer[i];
                    if (val == 0) continue;
                    for (int bit = 0; bit < 32; bit++)
                    {
                        if ((val & (1u << bit)) != 0)
                        {
                            list.Add((UnityKeyCode)((i << 5) | bit));
                        }
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Resets the internal pointers and buffers.
        /// </summary>
        public static void Reset()
        {
            lock (_sync)
            {
                IsConnected = false;
                Status = "Reset";
                _inputManagerPtr = 0;
                _keysPtr = 0;
                _keysDownPtr = 0;
                _lastRefreshTimestamp = 0;
                Array.Clear(_keysBuffer, 0, _keysBuffer.Length);
                Array.Clear(_keysDownBuffer, 0, _keysDownBuffer.Length);
                Array.Clear(_fallbackPrevState, 0, _fallbackPrevState.Length);
            }
        }

        /// <summary>
        /// Maps Unity KeyCode to Windows Virtual-Key code for fallback compatibility.
        /// </summary>
        public static int ToVirtualKey(UnityKeyCode key)
        {
            int k = (int)key;

            // Letters A-Z: Unity 97-122 -> VK 0x41-0x5A (65-90)
            if (k >= (int)UnityKeyCode.A && k <= (int)UnityKeyCode.Z)
            {
                return 0x41 + (k - (int)UnityKeyCode.A);
            }

            // Digits 0-9: Unity 48-57 -> VK 0x30-0x39 (48-57)
            if (k >= (int)UnityKeyCode.Alpha0 && k <= (int)UnityKeyCode.Alpha9)
            {
                return k;
            }

            // Function keys F1-F12: Unity 282-293 -> VK 0x70-0x7B (112-123)
            if (k >= (int)UnityKeyCode.F1 && k <= (int)UnityKeyCode.F12)
            {
                return 0x70 + (k - (int)UnityKeyCode.F1);
            }

            // Common special keys
            return key switch
            {
                UnityKeyCode.Space => 0x20,        // VK_SPACE
                UnityKeyCode.Return => 0x0D,       // VK_RETURN
                UnityKeyCode.Escape => 0x1B,       // VK_ESCAPE
                UnityKeyCode.Tab => 0x09,          // VK_TAB
                UnityKeyCode.Backspace => 0x08,    // VK_BACK
                UnityKeyCode.Insert => 0x2D,       // VK_INSERT
                UnityKeyCode.Delete => 0x2E,       // VK_DELETE
                UnityKeyCode.Home => 0x24,         // VK_HOME
                UnityKeyCode.End => 0x23,          // VK_END
                UnityKeyCode.PageUp => 0x21,       // VK_PRIOR
                UnityKeyCode.PageDown => 0x22,     // VK_NEXT
                UnityKeyCode.LeftArrow => 0x25,    // VK_LEFT
                UnityKeyCode.UpArrow => 0x26,      // VK_UP
                UnityKeyCode.RightArrow => 0x27,   // VK_RIGHT
                UnityKeyCode.DownArrow => 0x28,    // VK_DOWN
                UnityKeyCode.LeftShift => 0xA0,    // VK_LSHIFT
                UnityKeyCode.RightShift => 0xA1,   // VK_RSHIFT
                UnityKeyCode.LeftControl => 0xA2,  // VK_LCONTROL
                UnityKeyCode.RightControl => 0xA3, // VK_RCONTROL
                UnityKeyCode.LeftAlt => 0x12,      // VK_MENU
                UnityKeyCode.RightAlt => 0x12,     // VK_MENU
                UnityKeyCode.Mouse0 => 0x01,       // VK_LBUTTON
                UnityKeyCode.Mouse1 => 0x02,       // VK_RBUTTON
                UnityKeyCode.Mouse2 => 0x04,       // VK_MBUTTON
                UnityKeyCode.Mouse3 => 0x05,       // VK_XBUTTON1
                UnityKeyCode.Mouse4 => 0x06,       // VK_XBUTTON2
                _ => 0
            };
        }
    }
}
