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
        public const int WindowBitsMax = 15; // 32K LZ77 window

        /// <summary>
        /// The default number of window bits for the Deflate algorithm.
        /// </summary>
        public const int WindowBitsDefault = WindowBitsMax;

        /// <summary>
        /// The size of the working buffer used in the ZlibCodec class.
        /// </summary>
        public const int WorkingBufferSizeDefault = 16384;

        /// <summary>
        /// The minimum size of the working buffer used in the ZlibCodec class.
        /// </summary>
        public const int WorkingBufferSizeMin = 1024;
    }

}

