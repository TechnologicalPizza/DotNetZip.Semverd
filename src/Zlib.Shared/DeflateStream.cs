// See the LICENSE file for license details.

namespace Ionic.Zlib
{
    /// <summary>
    /// A class for compressing and decompressing streams using the Deflate algorithm.
    /// </summary>
    ///
    /// <remarks>
    ///
    /// <para>
    ///   The DeflateStream is a <see
    ///   href="http://en.wikipedia.org/wiki/Decorator_pattern">Decorator</see> on a <see
    ///   cref="System.IO.Stream"/>.  It adds DEFLATE compression or decompression to any
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
        ///   Create a DeflateStream using the specified CompressionMode.
        /// </summary>
        ///
        /// <remarks>
        ///   When mode is <c>CompressionMode.Compress</c>, the DeflateStream will use
        ///   the default compression level. The underlying stream will be closed when
        ///   the DeflateStream is closed.
        /// </remarks>
        ///
        /// <example>
        /// This example uses a DeflateStream to compress data from a file, and writes
        /// the compressed data to another file.
        /// <code>
        /// using (System.IO.Stream input = System.IO.File.OpenRead(fileToCompress))
        /// {
        ///     using (var raw = System.IO.File.Create(fileToCompress + ".deflated"))
        ///     {
        ///         using (Stream compressor = new DeflateStream(raw, CompressionMode.Compress))
        ///         {
        ///             byte[] buffer = new byte[WORKING_BUFFER_SIZE];
        ///             int n;
        ///             while ((n= input.Read(buffer, 0, buffer.Length)) != 0)
        ///             {
        ///                 compressor.Write(buffer, 0, n);
        ///             }
        ///         }
        ///     }
        /// }
        /// </code>
        ///
        /// <code lang="VB">
        /// Using input As Stream = File.OpenRead(fileToCompress)
        ///     Using raw As FileStream = File.Create(fileToCompress &amp; ".deflated")
        ///         Using compressor As Stream = New DeflateStream(raw, CompressionMode.Compress)
        ///             Dim buffer As Byte() = New Byte(4096) {}
        ///             Dim n As Integer = -1
        ///             Do While (n &lt;&gt; 0)
        ///                 If (n &gt; 0) Then
        ///                     compressor.Write(buffer, 0, n)
        ///                 End If
        ///                 n = input.Read(buffer, 0, buffer.Length)
        ///             Loop
        ///         End Using
        ///     End Using
        /// End Using
        /// </code>
        /// </example>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the DeflateStream will compress or decompress.</param>
        public DeflateStream(System.IO.Stream stream, CompressionMode mode)
            : this(stream, mode, CompressionLevel.Default, false)
        {
        }

        /// <summary>
        /// Create a DeflateStream using the specified CompressionMode and the specified CompressionLevel.
        /// </summary>
        ///
        /// <remarks>
        ///
        /// <para>
        ///   When mode is <c>CompressionMode.Decompress</c>, the level parameter is
        ///   ignored.  The underlying stream will be closed when the DeflateStream is closed.
        /// </para>
        ///
        /// </remarks>
        ///
        /// <example>
        ///
        ///   This example uses a DeflateStream to compress data from a file, and writes
        ///   the compressed data to another file.
        ///
        /// <code>
        /// using (System.IO.Stream input = System.IO.File.OpenRead(fileToCompress))
        /// {
        ///     using (var raw = System.IO.File.Create(fileToCompress + ".deflated"))
        ///     {
        ///         using (Stream compressor = new DeflateStream(raw,
        ///                                                      CompressionMode.Compress,
        ///                                                      CompressionLevel.BestCompression))
        ///         {
        ///             byte[] buffer = new byte[WORKING_BUFFER_SIZE];
        ///             int n= -1;
        ///             while (n != 0)
        ///             {
        ///                 if (n &gt; 0)
        ///                     compressor.Write(buffer, 0, n);
        ///                 n= input.Read(buffer, 0, buffer.Length);
        ///             }
        ///         }
        ///     }
        /// }
        /// </code>
        ///
        /// <code lang="VB">
        /// Using input As Stream = File.OpenRead(fileToCompress)
        ///     Using raw As FileStream = File.Create(fileToCompress &amp; ".deflated")
        ///         Using compressor As Stream = New DeflateStream(raw, CompressionMode.Compress, CompressionLevel.BestCompression)
        ///             Dim buffer As Byte() = New Byte(4096) {}
        ///             Dim n As Integer = -1
        ///             Do While (n &lt;&gt; 0)
        ///                 If (n &gt; 0) Then
        ///                     compressor.Write(buffer, 0, n)
        ///                 End If
        ///                 n = input.Read(buffer, 0, buffer.Length)
        ///             Loop
        ///         End Using
        ///     End Using
        /// End Using
        /// </code>
        /// </example>
        /// <param name="stream">The stream to be read or written while deflating or inflating.</param>
        /// <param name="mode">Indicates whether the <c>DeflateStream</c> will compress or decompress.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        public DeflateStream(System.IO.Stream stream, CompressionMode mode, CompressionLevel level)
            : this(stream, mode, level, false)
        {
        }

        /// <summary>
        ///   Create a <c>DeflateStream</c> using the specified
        ///   <c>CompressionMode</c>, and explicitly specify whether the
        ///   stream should be left open after Deflation or Inflation.
        /// </summary>
        ///
        /// <remarks>
        ///
        /// <para>
        ///   This constructor allows the application to request that the underlying stream
        ///   remain open after the deflation or inflation occurs.  By default, after
        ///   <c>Close()</c> is called on the stream, the underlying stream is also
        ///   closed. In some cases this is not desired, for example if the stream is a
        ///   memory stream that will be re-read after compression.  Specify true for
        ///   the <paramref name="leaveOpen"/> parameter to leave the stream open.
        /// </para>
        ///
        /// <para>
        ///   The <c>DeflateStream</c> will use the default compression level.
        /// </para>
        ///
        /// <para>
        ///   See the other overloads of this constructor for example code.
        /// </para>
        /// </remarks>
        ///
        /// <param name="stream">
        ///   The stream which will be read from or written to.
        /// </param>
        ///
        /// <param name="mode">
        ///   Indicates whether the <c>DeflateStream</c> will compress or decompress.
        /// </param>
        ///
        /// <param name="leaveOpen">true if the application would like the stream to
        /// remain open after inflation/deflation.</param>
        public DeflateStream(System.IO.Stream stream, CompressionMode mode, bool leaveOpen)
            : this(stream, mode, CompressionLevel.Default, leaveOpen)
        {
        }

        /// <summary>
        ///   Create a <c>DeflateStream</c> using the specified <c>CompressionMode</c>
        ///   and the specified <c>CompressionLevel</c>, and explicitly specify whether
        ///   the stream should be left open after Deflation or Inflation.
        /// </summary>
        ///
        /// <remarks>
        ///
        /// <para>
        ///   When mode is <c>CompressionMode.Decompress</c>, the level parameter is ignored.
        /// </para>
        ///
        /// <para>
        ///   This constructor allows the application to request that the underlying stream
        ///   remain open after the deflation or inflation occurs.  By default, after
        ///   <c>Close()</c> is called on the stream, the underlying stream is also
        ///   closed. In some cases this is not desired, for example if the stream is a
        ///   <see cref="System.IO.MemoryStream"/> that will be re-read after
        ///   compression.  Specify true for the <paramref name="leaveOpen"/> parameter
        ///   to leave the stream open.
        /// </para>
        ///
        /// </remarks>
        ///
        /// <example>
        ///
        ///   This example shows how to use a <c>DeflateStream</c> to compress data from
        ///   a file, and store the compressed data into another file.
        ///
        /// <code>
        /// using (var output = System.IO.File.Create(fileToCompress + ".deflated"))
        /// {
        ///     using (System.IO.Stream input = System.IO.File.OpenRead(fileToCompress))
        ///     {
        ///         using (Stream compressor = new DeflateStream(output, CompressionMode.Compress, CompressionLevel.BestCompression, true))
        ///         {
        ///             byte[] buffer = new byte[WORKING_BUFFER_SIZE];
        ///             int n= -1;
        ///             while (n != 0)
        ///             {
        ///                 if (n &gt; 0)
        ///                     compressor.Write(buffer, 0, n);
        ///                 n= input.Read(buffer, 0, buffer.Length);
        ///             }
        ///         }
        ///     }
        ///     // can write additional data to the output stream here
        /// }
        /// </code>
        ///
        /// <code lang="VB">
        /// Using output As FileStream = File.Create(fileToCompress &amp; ".deflated")
        ///     Using input As Stream = File.OpenRead(fileToCompress)
        ///         Using compressor As Stream = New DeflateStream(output, CompressionMode.Compress, CompressionLevel.BestCompression, True)
        ///             Dim buffer As Byte() = New Byte(4096) {}
        ///             Dim n As Integer = -1
        ///             Do While (n &lt;&gt; 0)
        ///                 If (n &gt; 0) Then
        ///                     compressor.Write(buffer, 0, n)
        ///                 End If
        ///                 n = input.Read(buffer, 0, buffer.Length)
        ///             Loop
        ///         End Using
        ///     End Using
        ///     ' can write additional data to the output stream here.
        /// End Using
        /// </code>
        /// </example>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the DeflateStream will compress or decompress.</param>
        /// <param name="leaveOpen">true if the application would like the stream to remain open after inflation/deflation.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        public DeflateStream(System.IO.Stream stream, CompressionMode mode, CompressionLevel level, bool leaveOpen)
            : base(stream, mode, level, ZlibStreamFlavor.DEFLATE, leaveOpen)
        {
        }
    }
}

