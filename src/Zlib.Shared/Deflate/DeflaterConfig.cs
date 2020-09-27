// See the LICENSE file for license details.

namespace Ionic.Zlib
{
    public class DeflaterConfig
    {
        private static DeflaterConfig[] Table { get; } =
        {
            new DeflaterConfig(0, 0, 0, 0, DeflateFlavor.Store),
            new DeflaterConfig(4, 4, 8, 4, DeflateFlavor.Fast),
            new DeflaterConfig(4, 5, 16, 8, DeflateFlavor.Fast),
            new DeflaterConfig(4, 6, 32, 32, DeflateFlavor.Fast),

            new DeflaterConfig(4, 4, 16, 16, DeflateFlavor.Slow),
            new DeflaterConfig(8, 16, 32, 32, DeflateFlavor.Slow),
            new DeflaterConfig(8, 16, 128, 128, DeflateFlavor.Slow),
            new DeflaterConfig(8, 32, 128, 256, DeflateFlavor.Slow),
            new DeflaterConfig(32, 128, 258, 1024, DeflateFlavor.Slow),
            new DeflaterConfig(32, 258, 258, 4096, DeflateFlavor.Slow),
        };

        /// <summary>
        /// Use a faster search when the previous match is longer than this.
        /// </summary>
        public int GoodLength { get; }

        /// <summary>
        /// Do not perform lazy search above this match length.
        /// </summary>
        /// <remarks>
        /// Attempt to find a better match only when the current match is
        /// strictly smaller than this value. This mechanism is used only for
        /// compression levels >= 4.  For levels 1,2,3: MaxLazy is actually
        /// MaxInsertLength. (See DeflateFast)
        /// </remarks>
        public int MaxLazy { get; } 

        /// <summary>
        /// Quit search above this match length.
        /// </summary>
        public int NiceLength { get; }

        /// <summary>
        /// To speed up deflation, hash chains are never searched beyond this length. 
        /// A higher limit improves compression ratio but degrades the speed.
        /// </summary>
        public int MaxChainLength { get; }

        public DeflateFlavor Flavor { get; }

        private DeflaterConfig(
            int goodLength, int maxLazy, int niceLength, int maxChainLength, DeflateFlavor flavor)
        {
            GoodLength = goodLength;
            MaxLazy = maxLazy;
            NiceLength = niceLength;
            MaxChainLength = maxChainLength;
            Flavor = flavor;
        }

        public static DeflaterConfig Lookup(CompressionLevel level)
        {
            return Table[(int)level];
        }
    }
}