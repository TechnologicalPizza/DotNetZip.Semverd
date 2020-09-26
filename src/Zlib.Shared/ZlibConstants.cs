// See the LICENSE file for license details.

namespace Ionic.Zlib
{
    /// <summary>
    /// A bunch of constants used in the Zlib interface.
    /// </summary>
    public static class ZlibConstants
    {
        /// <summary>
        /// The maximum number of window bits for the Deflate algorithm.
        /// </summary>
        public const int MaxWindowBits = 15; // 32K LZ77 window

        /// <summary>
        /// The default number of window bits for the Deflate algorithm.
        /// </summary>
        public const int DefaultWindowBits = MaxWindowBits;
    }
}

