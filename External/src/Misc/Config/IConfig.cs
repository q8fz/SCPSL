// DMA Base - host config contract (mem-write gate, monitor size, cache).

using DmaBase.Unity.LowLevel;

namespace DmaBase.Misc.Config
{
    public interface IConfig
    {
        LowLevelCache LowLevelCache { get; }
        bool MemWritesEnabled { get; }
        int MonitorWidth { get; }
        int MonitorHeight { get; }

        void Save();
        Task SaveAsync();
    }
}
