// See the LICENSE file for license details.

namespace Ionic.Zlib
{
    internal enum DeflateBlockState
    {
        NeedMore = 0,       // block not completed, need more input or more output
        BlockDone,          // block flush performed
        FinishStarted,      // finish started, need only more output at next deflate
        FinishDone          // finish done, accept no more input or output
    }
}