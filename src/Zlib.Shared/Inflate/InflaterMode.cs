// See the LICENSE file for license details.

namespace Ionic.Zlib
{
    internal enum InflaterMode
    {
        METHOD = 0,  // waiting for method byte
        FLAG = 1,  // waiting for flag byte
        DICT4 = 2,  // four dictionary check bytes to go
        DICT3 = 3,  // three dictionary check bytes to go
        DICT2 = 4,  // two dictionary check bytes to go
        DICT1 = 5,  // one dictionary check byte to go
        DICT0 = 6,  // waiting for inflateSetDictionary
        BLOCKS = 7,  // decompressing blocks
        CHECK4 = 8,  // four check bytes to go
        CHECK3 = 9,  // three check bytes to go
        CHECK2 = 10, // two check bytes to go
        CHECK1 = 11, // one check byte to go
        DONE = 12, // finished check, done
        BAD = 13, // got an error--stay here
    }
}