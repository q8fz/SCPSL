// DMA Base - scatter-write feature contract.

using DmaBase.DMA.ScatterAPI;

namespace DmaBase.DMA.Features
{
    public interface IMemWriteFeature : IFeature
    {
        /// <summary>
        /// Apply the MemWrite feature via Scatter Write.
        /// Must not throw.
        /// </summary>
        /// <param name="writes"></param>
        void TryApply(ScatterWriteHandle writes);
    }
}
