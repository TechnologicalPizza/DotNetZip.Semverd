// See the LICENSE file for license details.

namespace Ionic.Zlib
{
    internal enum InflateBlockMode
    {
        TYPE = 0,        // get type bits (3, including end bit)
        LENS = 1,        // get lengths for stored
        STORED = 2,      // processing stored block
        TABLE = 3,       // get table lengths
        BTREE = 4,       // get bit lengths tree for a dynamic block
        DTREE = 5,       // get length, distance trees for a dynamic block
        CODES = 6,       // processing fixed or dynamic block
        DRY = 7,         // output remaining window bytes
        DONE = 8,        // finished last block, done
        BAD = 9,         // ot a data error--stuck here
    }
}