// DMA Base - persisted module base addresses for the low-level API.

using System.Text.Json.Serialization;
using DmaBase;

namespace DmaBase.Unity.LowLevel
{
    /// <summary>
    /// Contains Cache Data for Unity Low Level API.
    /// </summary>
    public sealed class LowLevelCache
    {
        [JsonPropertyName("ZK8MQLY")]
        public uint PID { get; set; }

        [JsonPropertyName("tZ6Yv7m")]
        public ulong UnityPlayerDll { get; set; }

        [JsonPropertyName("K9XrF2q")]
        public ulong MonoDll { get; set; }

        /// <summary>
        /// Persist the cache to disk via the host config.
        /// </summary>
        public async Task SaveAsync()
        {
            if (SharedProgram.Config is not null)
                await SharedProgram.Config.SaveAsync();
        }

        /// <summary>
        /// Reset the cache to defaults.
        /// </summary>
        public void Reset()
        {
            UnityPlayerDll = default;
            MonoDll = default;
        }
    }
}
