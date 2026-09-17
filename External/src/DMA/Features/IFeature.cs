// DMA Base - lifecycle hooks for optional host-side feature plugins.

using System.Collections.Concurrent;

namespace DmaBase.DMA.Features
{
    public interface IFeature
    {
        bool CanRun { get; }
        void OnApply();
        void OnGameStart();
        void OnRoundStart();
        void OnRoundEnd();
        void OnGameStop();

        [Obsolete("Use OnRoundStart instead for SCP:SL.")]
        void OnRaidStart() => OnRoundStart();
        [Obsolete("Use OnRoundEnd instead for SCP:SL.")]
        void OnRaidEnd() => OnRoundEnd();

        #region Static Interface
        private static readonly ConcurrentBag<IFeature> _features = new();
        /// <summary>
        /// All Memory Write Features.
        /// </summary>
        public static IEnumerable<IFeature> AllFeatures => _features;

        /// <summary>
        /// Add a feature to the collection.
        /// </summary>
        /// <param name="feature">Feature to add.</param>
        protected static void Register(IFeature feature) => _features.Add(feature);

        /// <summary>
        /// Dispatches OnGameStart to all registered features.
        /// </summary>
        public static void DispatchGameStart()
        {
            foreach (var f in _features) { try { f.OnGameStart(); } catch { } }
        }

        /// <summary>
        /// Dispatches OnGameStop to all registered features.
        /// </summary>
        public static void DispatchGameStop()
        {
            foreach (var f in _features) { try { f.OnGameStop(); } catch { } }
        }

        /// <summary>
        /// Dispatches OnRoundStart to all registered features.
        /// </summary>
        public static void DispatchRoundStart()
        {
            foreach (var f in _features) { try { f.OnRoundStart(); } catch { } }
        }

        /// <summary>
        /// Dispatches OnRoundEnd to all registered features.
        /// </summary>
        public static void DispatchRoundEnd()
        {
            foreach (var f in _features) { try { f.OnRoundEnd(); } catch { } }
        }
        #endregion
    }
}
