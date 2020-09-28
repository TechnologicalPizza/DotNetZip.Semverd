// See the LICENSE file for license details.

using System;
using System.IO;

namespace Ionic.Zlib
{

    /// <summary>
    /// Represents a Zlib stream for compression or decompression.
    /// </summary>
    /// <remarks>
    ///
    /// <para>
    /// The ZlibStream is a <see
    /// href="http://en.wikipedia.org/wiki/Decorator_pattern">Decorator</see> on a <see
    /// cref="Stream"/>.  It adds ZLIB compression or decompression to any
    /// stream.
    /// </para>
    ///
    /// <para> Using this stream, applications can compress or decompress data via
    /// stream <c>Read()</c> and <c>Write()</c> operations.  Either compresssion or
    /// decompression can occur through either reading or writing. The compression
    /// format used is ZLIB, which is documented in <see
    /// href="http://www.ietf.org/rfc/rfc1950.txt">IETF RFC 1950</see>, "ZLIB Compressed
    /// Data Format Specification version 3.3". This implementation of ZLIB always uses
    /// DEFLATE as the compression method.  (see <see
    /// href="http://www.ietf.org/rfc/rfc1951.txt">IETF RFC 1951</see>, "DEFLATE
    /// Compressed Data Format Specification version 1.3.") </para>
    ///
    /// <para>
    /// The ZLIB format allows for varying compression methods, window sizes, and dictionaries.
    /// This implementation always uses the DEFLATE compression method, a preset dictionary,
    /// and 15 window bits by default.
    /// </para>
    ///
    /// <para>
    /// This class is similar to <see cref="DeflateStream"/>, except that it adds the
    /// RFC1950 header and trailer bytes to a compressed stream when compressing, or expects
    /// the RFC1950 header and trailer bytes when decompressing.  It is also similar to the
    /// <see cref="GZipStream"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="DeflateStream" />
    /// <seealso cref="GZipStream" />
    public class ZlibStream : ZlibBaseStream
    {
        /// <summary>
        ///   The position of the stream pointer.
        /// </summary>
        ///
        /// <remarks>
        ///   Setting this property always throws a <see
        ///   cref="NotSupportedException"/>. Reading will return the total bytes
        ///   written out, if used in writing, or the total bytes read in, if used in
        ///   reading.  The count may refer to compressed bytes or uncompressed bytes,
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

        /// <summary>
        /// Create a <c>ZlibStream</c> using the specified <c>CompressionMode</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        ///   When mode is <c>CompressionMode.Compress</c>, the <c>ZlibStream</c>
        ///   will use the default compression level. The "captive" stream will be
        ///   closed when the <c>ZlibStream</c> is closed.
        /// </para>
        /// </remarks>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the ZlibStream will compress or decompress.</param>
        public ZlibStream(Stream stream, CompressionMode mode)
            : this(stream, mode, CompressionLevel.Default, false)
        {
        }

        /// <summary>
        ///   Create a <c>ZlibStream</c> using the specified <c>CompressionMode</c> and
        ///   the specified <c>CompressionLevel</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        ///   When mode is <c>CompressionMode.Decompress</c>, the level parameter is ignored.
        ///   The "captive" stream will be closed when the <c>ZlibStream</c> is closed.
        /// </para>
        /// </remarks>
        /// <param name="stream">The stream to be read or written while deflating or inflating.</param>
        /// <param name="mode">Indicates whether the ZlibStream will compress or decompress.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        public ZlibStream(Stream stream, CompressionMode mode, CompressionLevel level)
            : this(stream, mode, level, false)
        {
        }

        /// <summary>
        ///   Create a <c>ZlibStream</c> using the specified <c>CompressionMode</c>, and
        ///   explicitly specify whether the captive stream should be left open after
        ///   Deflation or Inflation.
        /// </summary>
        /// <remarks>
        /// <para>
        ///   When mode is <c>CompressionMode.Compress</c>, the <c>ZlibStream</c> will use
        ///   the default compression level.
        /// </para>
        /// <para>
        ///   This constructor allows the application to request that the captive stream
        ///   remain open after the deflation or inflation occurs.  By default, after
        ///   <c>Close()</c> is called on the stream, the captive stream is also
        ///   closed. In some cases this is not desired, for example if the stream is a
        ///   <see cref="MemoryStream"/> that will be re-read after
        ///   compression.  Specify true for the <paramref name="leaveOpen"/> parameter to leave the stream
        ///   open.
        /// </para>
        /// <para>
        /// See the other overloads of this constructor for example code.
        /// </para>
        /// </remarks>
        /// <param name="stream">The stream which will be read or written. This is called the
        /// "captive" stream in other places in this documentation.</param>
        /// <param name="mode">Indicates whether the ZlibStream will compress or decompress.</param>
        /// <param name="leaveOpen">true if the application would like the stream to remain
        /// open after inflation/deflation.</param>
        public ZlibStream(Stream stream, CompressionMode mode, bool leaveOpen)
            : this(stream, mode, CompressionLevel.Default, leaveOpen)
        {
        }

        /// <summary>
        ///   Create a <c>ZlibStream</c> using the specified <c>CompressionMode</c>
        ///   and the specified <c>CompressionLevel</c>, and explicitly specify
        ///   whether the stream should be left open after Deflation or Inflation.
        /// </summary>
        ///
        /// <remarks>
        ///
        /// <para>
        ///   This constructor allows the application to request that the captive
        ///   stream remain open after the deflation or inflation occurs.  By
        ///   default, after <c>Close()</c> is called on the stream, the captive
        ///   stream is also closed. In some cases this is not desired, for example
        ///   if the stream is a <see cref="MemoryStream"/> that will be
        ///   re-read after compression.  Specify true for the <paramref
        ///   name="leaveOpen"/> parameter to leave the stream open.
        /// </para>
        ///
        /// <para>
        ///   When mode is <c>CompressionMode.Decompress</c>, the level parameter is
        ///   ignored.
        /// </para>
        ///
        /// </remarks>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the ZlibStream will compress or decompress.</param>
        /// <param name="leaveOpen">
        /// true if the application would like the stream to remain open after inflation/deflation.
        /// </param>
        /// <param name="level">
        /// A tuning knob to trade speed for effectiveness. This parameter is
        /// effective only when mode is <c>CompressionMode.Compress</c>.
        /// </param>
        public ZlibStream(Stream stream, CompressionMode mode, CompressionLevel level, bool leaveOpen) :
            base(stream, mode, level, leaveOpen)
        {
        }

        protected override void GetZlibParams(out bool rfc1950Compliant)
        {
            rfc1950Compliant = true;
        }

        protected override void FirstWrite()
        {
        }

        protected override bool FirstRead()
        {
            return true;
        }
    }
}