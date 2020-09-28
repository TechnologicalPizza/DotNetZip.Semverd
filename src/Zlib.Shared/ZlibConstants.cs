// See the LICENSE file for license details.

namespace Ionic.Zlib
{
    /// <summary>
    /// A bunch of constants used in the Zlib interface.
    /// </summary>
    public static class ZlibConstants
    {
        /// <summary>
        /// The minimum number of window bits for the Deflate algorithm.
        /// </summary>
        public const int MinWindowBits = 9; // 512 LZ77 window

        /// <summary>
        /// The maximum number of window bits for the Deflate algorithm.
        /// </summary>
        public const int MaxWindowBits = 15; // 32K LZ77 window

        /// <summary>
        /// The default number of window bits for the Deflate algorithm.
        /// </summary>
        public const int DefaultWindowBits = MaxWindowBits;

        public const int MinMemoryLevel = 1;
        public const int MaxMemoryLevel = 9;
        public const int DefaultMemoryLevel = 8;

        public const uint InitialAdler32 = 1;
    }
}

