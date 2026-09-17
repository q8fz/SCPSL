// DMA Base - shared config/state bridge between library and host app.

using DmaBase.Misc.Config;

namespace DmaBase
{
    /// <summary>
    /// Shared state between the DMA/Unity satellite modules and the host application.
    /// </summary>
    public static class SharedProgram
    {
        internal static DirectoryInfo ConfigPath { get; private set; }
        internal static IConfig Config { get; private set; }

        /// <summary>
        /// Initialize shared state from the host application.
        /// </summary>
        public static void Initialize(DirectoryInfo configPath, IConfig config)
        {
            ArgumentNullException.ThrowIfNull(configPath);
            ArgumentNullException.ThrowIfNull(config);
            ConfigPath = configPath;
            Config = config;
        }

        /// <summary>
        /// Replace the active config instance (e.g. after loading a profile).
        /// </summary>
        public static void UpdateConfig(IConfig newConfig)
        {
            ArgumentNullException.ThrowIfNull(newConfig);
            Config = newConfig;
        }
    }
}
