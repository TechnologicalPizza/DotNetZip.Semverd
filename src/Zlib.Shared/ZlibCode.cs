// See the LICENSE file for license details.

namespace Ionic.Zlib
{
    public enum ZlibCode
    {
        /// <summary>
        /// indicates everything is A-OK
        /// </summary>
        Ok = 0,

        /// <summary>
        /// Indicates that the last operation reached the end of the stream.
        /// </summary>
        StreamEnd = 1,

        /// <summary>
        /// The operation ended in need of a dictionary. 
        /// </summary>
        NeedDict = 2,

        /// <summary>
        /// There was an error with the stream - not enough data, not open and readable, etc.
        /// </summary>
        StreamError = -2,

        /// <summary>
        /// There was an error with the data - not enough data, bad data, etc.
        /// </summary>
        DataError = -3,


        MemError = -4,

        /// <summary>
        /// There was an error with the working buffer.
        /// </summary>
        BufError = -5,

        VersionError = -6
    }

}

