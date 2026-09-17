// DMA Base - base class for periodic memory-write plugins.

using DmaBase.DMA.ScatterAPI;
using System.Diagnostics;

namespace DmaBase.DMA.Features
{
    /// <summary>
    /// Optional base for periodic memory-write plugins.
    /// Override <see cref="CanRun"/> for your own readiness checks (match started, etc.).
    /// </summary>
    public abstract class MemWriteFeature<T> : IFeature, IMemWriteFeature
        where T : IMemWriteFeature
    {
        public static T Instance { get; }

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        static MemWriteFeature()
        {
            Instance = Activator.CreateInstance<T>();
            IFeature.Register(Instance);
        }

        public virtual bool Enabled { get; set; }

        protected virtual TimeSpan Delay => TimeSpan.FromMilliseconds(10);

        protected bool DelayElapsed => Delay == TimeSpan.Zero || _sw.Elapsed >= Delay;

        public virtual bool CanRun => Enabled && DmaMemory.Ready && DelayElapsed;

        public virtual void TryApply(ScatterWriteHandle writes) { }

        public void OnApply()
        {
            if (Delay != TimeSpan.Zero)
                _sw.Restart();
        }

        public virtual void OnGameStart() { }
        public virtual void OnRoundStart() { }
        public virtual void OnRoundEnd() { }
        public virtual void OnGameStop() { }

        [Obsolete("Use OnRoundStart instead for SCP:SL.")]
        public virtual void OnRaidStart() => OnRoundStart();
        [Obsolete("Use OnRoundEnd instead for SCP:SL.")]
        public virtual void OnRaidEnd() => OnRoundEnd();
    }
}
