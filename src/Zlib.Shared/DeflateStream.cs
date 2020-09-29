// See the LICENSE file for license details.

using System;
using System.IO;

namespace Ionic.Zlib
{
    /// <summary>
    /// Stream for compression or decompression using the Deflate format.
    /// </summary>
    ///
    /// <remarks>
    ///
    /// <para>
    ///   The DeflateStream is a <see
    ///   href="http://en.wikipedia.org/wiki/Decorator_pattern">Decorator</see> on a <see
    ///   cref="Stream"/>.  It adds DEFLATE compression or decompression to any
    ///   stream.
    /// </para>
    ///
    /// <para>
    ///   Using this stream, applications can compress or decompress data via stream
    ///   <c>Read</c> and <c>Write</c> operations.  Either compresssion or decompression
    ///   can occur through either reading or writing. The compression format used is
    ///   DEFLATE, which is documented in <see
    ///   href="http://www.ietf.org/rfc/rfc1951.txt">IETF RFC 1951</see>, "DEFLATE
    ///   Compressed Data Format Specification version 1.3.".
    /// </para>
    ///
    /// <para>
    ///   This class is similar to <see cref="ZlibStream"/>, except that
    ///   <c>ZlibStream</c> adds the <see href="http://www.ietf.org/rfc/rfc1950.txt">RFC
    ///   1950 - ZLIB</see> framing bytes to a compressed stream when compressing, or
    ///   expects the RFC1950 framing bytes when decompressing. The <c>DeflateStream</c>
    ///   does not.
    /// </para>
    ///
    /// </remarks>
    ///
    /// <seealso cref="ZlibStream" />
    /// <seealso cref="GZipStream" />
    public class DeflateStream : ZlibBaseStream
    {
        /// <summary>
        /// The position of the stream pointer.
        /// </summary>
        ///
        /// <remarks>
        ///   Setting this property always throws a <see cref="NotSupportedException"/>. 
        ///   Reading will return the total bytes written out, if used in writing,
        ///   or the total bytes read in, if used in reading. 
        ///   The count may refer to compressed bytes or uncompressed bytes,
        ///   depending on how you've used the stream.
        /// </remarks>
        public override long Position
        {
            get
            {
                throw new NotImplementedException();
                //if (IsCompressor)
                //    return Codec.TotalBytesOut;
                //if (IsDecompressor)
                //    return Codec.TotalBytesIn;
                //return 0;
            }
            set => throw new NotSupportedException();
        }

        #region Constructors

        /// <summary>
        ///   Create a <see cref="DeflateStream"/> using the specified <see cref="CompressionMode"/>
        ///   and default compression level. 
        ///   The stream will be closed upon disposal.
        /// </summary>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the <see cref="DeflateStream"/> will compress or decompress.</param>
        public DeflateStream(Stream stream, CompressionMode mode)
            : this(stream, mode, false)
        {
        }

        /// <summary>
        ///   Create a <see cref="DeflateStream"/> using the specified <see cref="CompressionMode"/>,
        ///   default compression level,
        ///   and whether the stream should be left open after disposal.
        /// </summary>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the <see cref="DeflateStream"/> will compress or decompress.</param>
        /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open after disposal.</param>
        public DeflateStream(Stream stream, CompressionMode mode, bool leaveOpen)
            : base(stream, mode, CompressionLevel.Default, leaveOpen)
        {
        }

        /// <summary>
        ///   Create a compressor <see cref="DeflateStream"/> using the specified <see cref="CompressionLevel"/>.
        ///   The stream will be closed upon disposal.
        /// </summary>
        /// <param name="stream">The stream to write to while deflating.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        public DeflateStream(Stream stream, CompressionLevel level)
            : this(stream, level, false)
        {
        }

        /// <summary>
        ///   Create a compressor <see cref="DeflateStream"/> using the specified <see cref="CompressionLevel"/>,
        ///   and whether the stream should be left open after disposal.
        /// </summary>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open after disposal.</param>
        public DeflateStream(Stream stream, CompressionLevel level, bool leaveOpen)
            : base(stream, CompressionMode.Compress, level, leaveOpen)
        {
        }

        #endregion
    }
}

