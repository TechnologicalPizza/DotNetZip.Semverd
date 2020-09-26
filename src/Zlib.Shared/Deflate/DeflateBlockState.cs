// See the LICENSE file for license details.

namespace Ionic.Zlib
{
    public enum DeflateBlockState
    {
        /// <summary>
        /// Block not completed, need more input or more output.
        /// </summary>
        NeedMore = 0,

        /// <summary>
        /// Block flush performed.
        /// </summary>
        BlockDone,

        /// <summary>
        /// Finish started, need only more output at next deflate.
        /// </summary>
        FinishStarted,

        /// <summary>
        /// Finish done, accept no more input or output.
        /// </summary>
        FinishDone
    }
}