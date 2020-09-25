// GZipStream.cs
// ------------------------------------------------------------------
//
// Copyright (c) 2009 Dino Chiesa and Microsoft Corporation.
// All rights reserved.
//
// This code module is part of DotNetZip, a zipfile class library.
//
// ------------------------------------------------------------------
//
// This code is licensed under the Microsoft Public License.
// See the file License.txt for the license details.
// More info on: http://dotnetzip.codeplex.com
//
// ------------------------------------------------------------------
//
// last saved (in emacs):
// Time-stamp: <2011-August-08 18:14:39>
//
// ------------------------------------------------------------------
//
// This module defines the GZipStream class, which can be used as a replacement for
// the System.IO.Compression.GZipStream class in the .NET BCL.  NB: The design is not
// completely OO clean: there is some intelligence in the ZlibBaseStream that reads the
// GZip header.
//
// ------------------------------------------------------------------


using System;
using System.Buffers.Binary;
using System.IO;

namespace Ionic.Zlib
{
    /// <summary>
    ///   A class for compressing and decompressing GZIP streams.
    /// </summary>
    /// <remarks>
    ///
    /// <para>
    ///   The <c>GZipStream</c> is a <see
    ///   href="http://en.wikipedia.org/wiki/Decorator_pattern">Decorator</see> on a
    ///   <see cref="Stream"/>. It adds GZIP compression or decompression to any
    ///   stream.
    /// </para>
    ///
    /// <para>
    ///   Like the <c>System.IO.Compression.GZipStream</c> in the .NET Base Class Library, the
    ///   <c>Ionic.Zlib.GZipStream</c> can compress while writing, or decompress while
    ///   reading, but not vice versa.  The compression method used is GZIP, which is
    ///   documented in <see href="http://www.ietf.org/rfc/rfc1952.txt">IETF RFC
    ///   1952</see>, "GZIP file format specification version 4.3".</para>
    ///
    /// <para>
    ///   A <c>GZipStream</c> can be used to decompress data (through <c>Read()</c>) or
    ///   to compress data (through <c>Write()</c>), but not both.
    /// </para>
    ///
    /// <para>
    ///   If you wish to use the <c>GZipStream</c> to compress data, you must wrap it
    ///   around a write-able stream. As you call <c>Write()</c> on the <c>GZipStream</c>, the
    ///   data will be compressed into the GZIP format.  If you want to decompress data,
    ///   you must wrap the <c>GZipStream</c> around a readable stream that contains an
    ///   IETF RFC 1952-compliant stream.  The data will be decompressed as you call
    ///   <c>Read()</c> on the <c>GZipStream</c>.
    /// </para>
    ///
    /// <para>
    ///   Though the GZIP format allows data from multiple files to be concatenated
    ///   together, this stream handles only a single segment of GZIP format, typically
    ///   representing a single file.
    /// </para>
    ///
    /// <para>
    ///   This class is similar to <see cref="ZlibStream"/> and <see cref="DeflateStream"/>.
    ///   <c>ZlibStream</c> handles RFC1950-compliant streams.  <see cref="DeflateStream"/>
    ///   handles RFC1951-compliant streams. This class handles RFC1952-compliant streams.
    /// </para>
    ///
    /// </remarks>
    ///
    /// <seealso cref="DeflateStream" />
    /// <seealso cref="ZlibStream" />
    public class GZipStream : ZlibBaseStream
    {
        // GZip format
        // source: http://tools.ietf.org/html/rfc1952
        //
        //  header id:           2 bytes    1F 8B
        //  compress method      1 byte     8= DEFLATE (none other supported)
        //  flag                 1 byte     bitfield (See below)
        //  mtime                4 bytes    time_t (seconds since jan 1, 1970 UTC of the file.
        //  xflg                 1 byte     2 = max compress used , 4 = max speed (can be ignored)
        //  OS                   1 byte     OS for originating archive. set to 0xFF in compression.
        //  extra field length   2 bytes    optional - only if FEXTRA is set.
        //  extra field          varies
        //  filename             varies     optional - if FNAME is set.  zero terminated. ISO-8859-1.
        //  file comment         varies     optional - if FCOMMENT is set. zero terminated. ISO-8859-1.
        //  crc16                1 byte     optional - present only if FHCRC bit is set
        //  compressed data      varies
        //  CRC32                4 bytes
        //  isize                4 bytes    data size modulo 2^32
        //
        //     FLG (FLaGs)
        //                bit 0   FTEXT - indicates file is ASCII text (can be safely ignored)
        //                bit 1   FHCRC - there is a CRC16 for the header immediately following the header
        //                bit 2   FEXTRA - extra fields are present
        //                bit 3   FNAME - the zero-terminated filename is present. encoding; ISO-8859-1.
        //                bit 4   FCOMMENT  - a zero-terminated file comment is present. encoding: ISO-8859-1
        //                bit 5   reserved
        //                bit 6   reserved
        //                bit 7   reserved
        //
        // On consumption:
        // Extra field is a bunch of nonsense and can be safely ignored.
        // Header CRC and OS, likewise.
        //
        // on generation:
        // all optional fields get 0, except for the OS, which gets 255.
        //



        /// <summary>
        ///   The comment on the GZIP stream.
        /// </summary>
        ///
        /// <remarks>
        /// <para>
        ///   The GZIP format allows for each file to optionally have an associated
        ///   comment stored with the file.  The comment is encoded with the ISO-8859-1
        ///   code page.  To include a comment in a GZIP stream you create, set this
        ///   property before calling <c>Write()</c> for the first time on the
        ///   <c>GZipStream</c>.
        /// </para>
        ///
        /// <para>
        ///   When using <c>GZipStream</c> to decompress, you can retrieve this property
        ///   after the first call to <c>Read()</c>.  If no comment has been set in the
        ///   GZIP bytestream, the Comment property will return <c>null</c>
        ///   (<c>Nothing</c> in VB).
        /// </para>
        /// </remarks>
        public string? Comment
        {
            get => _Comment;
            set
            {
                AssertNotDisposed();
                _Comment = value;
            }
        }

        /// <summary>
        ///   The FileName for the GZIP stream.
        /// </summary>
        ///
        /// <remarks>
        ///
        /// <para>
        ///   The GZIP format optionally allows each file to have an associated
        ///   filename.  When compressing data (through <c>Write()</c>), set this
        ///   FileName before calling <c>Write()</c> the first time on the <c>GZipStream</c>.
        ///   The actual filename is encoded into the GZIP bytestream with the
        ///   ISO-8859-1 code page, according to RFC 1952. It is the application's
        ///   responsibility to insure that the FileName can be encoded and decoded
        ///   correctly with this code page.
        /// </para>
        ///
        /// <para>
        ///   When decompressing (through <c>Read()</c>), you can retrieve this value
        ///   any time after the first <c>Read()</c>.  In the case where there was no filename
        ///   encoded into the GZIP bytestream, the property will return <c>null</c> (<c>Nothing</c>
        ///   in VB).
        /// </para>
        /// </remarks>
        public string? FileName
        {
            get => _FileName;
            set
            {
                AssertNotDisposed();

                _FileName = value;
                if (_FileName == null)
                    return;

                if (_FileName.IndexOf("/") != -1)
                    _FileName = _FileName.Replace("/", "\\");

                if (_FileName.EndsWith("\\"))
                    throw new Exception("Illegal filename");

                if (_FileName.IndexOf("\\") != -1)
                {
                    // trim any leading path
                    _FileName = Path.GetFileName(_FileName);
                }
            }
        }

        /// <summary>
        ///   The last modified time for the GZIP stream.
        /// </summary>
        ///
        /// <remarks>
        ///   GZIP allows the storage of a last modified time with each GZIP entry.
        ///   When compressing data, you can set this before the first call to
        ///   <c>Write()</c>.  When decompressing, you can retrieve this value any time
        ///   after the first call to <c>Read()</c>.
        /// </remarks>
        public DateTime? LastModified;

        private int _headerByteCount;
        private bool _firstReadDone;
        private string? _FileName;
        private string? _Comment;


        /// <summary>
        ///   Create a <c>GZipStream</c> using the specified <c>CompressionMode</c>.
        /// </summary>
        /// <remarks>
        ///
        /// <para>
        ///   When mode is <c>CompressionMode.Compress</c>, the <c>GZipStream</c> will use the
        ///   default compression level.
        /// </para>
        ///
        /// <para>
        ///   As noted in the class documentation, the <c>CompressionMode</c> (Compress
        ///   or Decompress) also establishes the "direction" of the stream.  A
        ///   <c>GZipStream</c> with <c>CompressionMode.Compress</c> works only through
        ///   <c>Write()</c>.  A <c>GZipStream</c> with
        ///   <c>CompressionMode.Decompress</c> works only through <c>Read()</c>.
        /// </para>
        ///
        /// </remarks>
        ///
        /// <example>
        ///   This example shows how to use a GZipStream to compress data.
        /// <code>
        /// using (System.IO.Stream input = System.IO.File.OpenRead(fileToCompress))
        /// {
        ///     using (var raw = System.IO.File.Create(outputFile))
        ///     {
        ///         using (Stream compressor = new GZipStream(raw, CompressionMode.Compress))
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
        /// <code lang="VB">
        /// Dim outputFile As String = (fileToCompress &amp; ".compressed")
        /// Using input As Stream = File.OpenRead(fileToCompress)
        ///     Using raw As FileStream = File.Create(outputFile)
        ///     Using compressor As Stream = New GZipStream(raw, CompressionMode.Compress)
        ///         Dim buffer As Byte() = New Byte(4096) {}
        ///         Dim n As Integer = -1
        ///         Do While (n &lt;&gt; 0)
        ///             If (n &gt; 0) Then
        ///                 compressor.Write(buffer, 0, n)
        ///             End If
        ///             n = input.Read(buffer, 0, buffer.Length)
        ///         Loop
        ///     End Using
        ///     End Using
        /// End Using
        /// </code>
        /// </example>
        ///
        /// <example>
        /// This example shows how to use a GZipStream to uncompress a file.
        /// <code>
        /// private void GunZipFile(string filename)
        /// {
        ///     if (!filename.EndsWith(".gz))
        ///         throw new ArgumentException("filename");
        ///     var DecompressedFile = filename.Substring(0,filename.Length-3);
        ///     byte[] working = new byte[WORKING_BUFFER_SIZE];
        ///     int n= 1;
        ///     using (System.IO.Stream input = System.IO.File.OpenRead(filename))
        ///     {
        ///         using (Stream decompressor= new Ionic.Zlib.GZipStream(input, CompressionMode.Decompress, true))
        ///         {
        ///             using (var output = System.IO.File.Create(DecompressedFile))
        ///             {
        ///                 while (n !=0)
        ///                 {
        ///                     n= decompressor.Read(working, 0, working.Length);
        ///                     if (n > 0)
        ///                     {
        ///                         output.Write(working, 0, n);
        ///                     }
        ///                 }
        ///             }
        ///         }
        ///     }
        /// }
        /// </code>
        ///
        /// <code lang="VB">
        /// Private Sub GunZipFile(ByVal filename as String)
        ///     If Not (filename.EndsWith(".gz)) Then
        ///         Throw New ArgumentException("filename")
        ///     End If
        ///     Dim DecompressedFile as String = filename.Substring(0,filename.Length-3)
        ///     Dim working(WORKING_BUFFER_SIZE) as Byte
        ///     Dim n As Integer = 1
        ///     Using input As Stream = File.OpenRead(filename)
        ///         Using decompressor As Stream = new Ionic.Zlib.GZipStream(input, CompressionMode.Decompress, True)
        ///             Using output As Stream = File.Create(UncompressedFile)
        ///                 Do
        ///                     n= decompressor.Read(working, 0, working.Length)
        ///                     If n > 0 Then
        ///                         output.Write(working, 0, n)
        ///                     End IF
        ///                 Loop While (n  > 0)
        ///             End Using
        ///         End Using
        ///     End Using
        /// End Sub
        /// </code>
        /// </example>
        ///
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the GZipStream will compress or decompress.</param>
        public GZipStream(Stream stream, CompressionMode mode)
            : this(stream, mode, CompressionLevel.Default, false)
        {
        }

        /// <summary>
        ///   Create a <c>GZipStream</c> using the specified <c>CompressionMode</c> and
        ///   the specified <c>CompressionLevel</c>.
        /// </summary>
        /// <remarks>
        ///
        /// <para>
        ///   The <c>CompressionMode</c> (Compress or Decompress) also establishes the
        ///   "direction" of the stream.  A <c>GZipStream</c> with
        ///   <c>CompressionMode.Compress</c> works only through <c>Write()</c>.  A
        ///   <c>GZipStream</c> with <c>CompressionMode.Decompress</c> works only
        ///   through <c>Read()</c>.
        /// </para>
        ///
        /// </remarks>
        ///
        /// <example>
        ///
        /// This example shows how to use a <c>GZipStream</c> to compress a file into a .gz file.
        ///
        /// <code>
        /// using (System.IO.Stream input = System.IO.File.OpenRead(fileToCompress))
        /// {
        ///     using (var raw = System.IO.File.Create(fileToCompress + ".gz"))
        ///     {
        ///         using (Stream compressor = new GZipStream(raw,
        ///                                                   CompressionMode.Compress,
        ///                                                   CompressionLevel.BestCompression))
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
        ///     Using raw As FileStream = File.Create(fileToCompress &amp; ".gz")
        ///         Using compressor As Stream = New GZipStream(raw, CompressionMode.Compress, CompressionLevel.BestCompression)
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
        /// <param name="mode">Indicates whether the <c>GZipStream</c> will compress or decompress.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        public GZipStream(Stream stream, CompressionMode mode, CompressionLevel level)
            : this(stream, mode, level, false)
        {
        }

        /// <summary>
        ///   Create a <c>GZipStream</c> using the specified <c>CompressionMode</c>, and
        ///   explicitly specify whether the stream should be left open after Deflation
        ///   or Inflation.
        /// </summary>
        ///
        /// <remarks>
        /// <para>
        ///   This constructor allows the application to request that the captive stream
        ///   remain open after the deflation or inflation occurs.  By default, after
        ///   <c>Close()</c> is called on the stream, the captive stream is also
        ///   closed. In some cases this is not desired, for example if the stream is a
        ///   memory stream that will be re-read after compressed data has been written
        ///   to it.  Specify true for the <paramref name="leaveOpen"/> parameter to leave
        ///   the stream open.
        /// </para>
        ///
        /// <para>
        ///   The <see cref="CompressionMode"/> (Compress or Decompress) also
        ///   establishes the "direction" of the stream.  A <c>GZipStream</c> with
        ///   <c>CompressionMode.Compress</c> works only through <c>Write()</c>.  A <c>GZipStream</c>
        ///   with <c>CompressionMode.Decompress</c> works only through <c>Read()</c>.
        /// </para>
        ///
        /// <para>
        ///   The <c>GZipStream</c> will use the default compression level. If you want
        ///   to specify the compression level, see <see cref="GZipStream(Stream,
        ///   CompressionMode, CompressionLevel, bool)"/>.
        /// </para>
        ///
        /// <para>
        ///   See the other overloads of this constructor for example code.
        /// </para>
        ///
        /// </remarks>
        ///
        /// <param name="stream">
        ///   The stream which will be read or written. This is called the "captive"
        ///   stream in other places in this documentation.
        /// </param>
        ///
        /// <param name="mode">Indicates whether the GZipStream will compress or decompress.
        /// </param>
        ///
        /// <param name="leaveOpen">
        ///   true if the application would like the base stream to remain open after
        ///   inflation/deflation.
        /// </param>
        public GZipStream(Stream stream, CompressionMode mode, bool leaveOpen)
            : this(stream, mode, CompressionLevel.Default, leaveOpen)
        {
        }

        /// <summary>
        ///   Create a <c>GZipStream</c> using the specified <c>CompressionMode</c> and the
        ///   specified <c>CompressionLevel</c>, and explicitly specify whether the
        ///   stream should be left open after Deflation or Inflation.
        /// </summary>
        ///
        /// <remarks>
        ///
        /// <para>
        ///   This constructor allows the application to request that the captive stream
        ///   remain open after the deflation or inflation occurs.  By default, after
        ///   <c>Close()</c> is called on the stream, the captive stream is also
        ///   closed. In some cases this is not desired, for example if the stream is a
        ///   memory stream that will be re-read after compressed data has been written
        ///   to it.  Specify true for the <paramref name="leaveOpen"/> parameter to
        ///   leave the stream open.
        /// </para>
        ///
        /// <para>
        ///   As noted in the class documentation, the <c>CompressionMode</c> (Compress
        ///   or Decompress) also establishes the "direction" of the stream.  A
        ///   <c>GZipStream</c> with <c>CompressionMode.Compress</c> works only through
        ///   <c>Write()</c>.  A <c>GZipStream</c> with <c>CompressionMode.Decompress</c> works only
        ///   through <c>Read()</c>.
        /// </para>
        ///
        /// </remarks>
        ///
        /// <example>
        ///   This example shows how to use a <c>GZipStream</c> to compress data.
        /// <code>
        /// using (System.IO.Stream input = System.IO.File.OpenRead(fileToCompress))
        /// {
        ///     using (var raw = System.IO.File.Create(outputFile))
        ///     {
        ///         using (Stream compressor = new GZipStream(raw, CompressionMode.Compress, CompressionLevel.BestCompression, true))
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
        /// <code lang="VB">
        /// Dim outputFile As String = (fileToCompress &amp; ".compressed")
        /// Using input As Stream = File.OpenRead(fileToCompress)
        ///     Using raw As FileStream = File.Create(outputFile)
        ///     Using compressor As Stream = New GZipStream(raw, CompressionMode.Compress, CompressionLevel.BestCompression, True)
        ///         Dim buffer As Byte() = New Byte(4096) {}
        ///         Dim n As Integer = -1
        ///         Do While (n &lt;&gt; 0)
        ///             If (n &gt; 0) Then
        ///                 compressor.Write(buffer, 0, n)
        ///             End If
        ///             n = input.Read(buffer, 0, buffer.Length)
        ///         Loop
        ///     End Using
        ///     End Using
        /// End Using
        /// </code>
        /// </example>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the GZipStream will compress or decompress.</param>
        /// <param name="leaveOpen">true if the application would like the stream to remain open after inflation/deflation.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        public GZipStream(Stream stream, CompressionMode mode, CompressionLevel level, bool leaveOpen) :
            base(stream, mode, level, ZlibStreamFlavor.GZIP, leaveOpen)
        {
        }

        #region Stream methods

        public override int Read(Span<byte> buffer)
        {
            int n = base.Read(buffer);

            if (!_firstReadDone)
            {
                _firstReadDone = true;
                FileName = _GzipFileName;
                Comment = _GzipComment;
            }
            return n;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (StreamMode == ZlibStreamMode.Undefined)
            {
                //Console.WriteLine("GZipStream: First write");
                if (IsCompressor)
                {
                    // first write in compression, therefore, emit the GZIP header
                    _headerByteCount = EmitHeader();
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }

            base.Write(buffer);
        }
        #endregion


        internal static readonly DateTime _unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        internal static readonly System.Text.Encoding iso8859dash1 = System.Text.Encoding.GetEncoding("iso-8859-1");

        private int EmitHeader()
        {
            byte[]? commentBytes = (Comment == null) ? null : iso8859dash1.GetBytes(Comment);
            byte[]? filenameBytes = (FileName == null) ? null : iso8859dash1.GetBytes(FileName);

            int cbLength = (commentBytes == null) ? 0 : commentBytes.Length + 1;
            int fnLength = (filenameBytes == null) ? 0 : filenameBytes.Length + 1;

            int bufferLength = 10 + cbLength + fnLength;
            Span<byte> header = bufferLength <= 4096
                ? stackalloc byte[bufferLength] : new byte[bufferLength];

            int i = 0;
            // ID
            header[i++] = 0x1F;
            header[i++] = 0x8B;

            // TODO: flags enum
            // compression method
            header[i++] = 8;
            byte flag = 0;
            if (Comment != null)
                flag ^= 0x10;
            if (FileName != null)
                flag ^= 0x8;

            // flag
            header[i++] = flag;

            // mtime
            if (!LastModified.HasValue)
                LastModified = DateTime.Now;
            TimeSpan delta = LastModified.Value - _unixEpoch;
            int timet = (int)delta.TotalSeconds;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(i), timet);
            i += 4;

            // xflg
            header[i++] = 0;    // flags are unused

            // OS
            header[i++] = 0xff; // 0xFF == unspecified

            // extra field length - only if FEXTRA is set, which it is not.
            //header[i++]= 0;
            //header[i++]= 0;

            // filename
            if (filenameBytes != null && fnLength != 0)
            {
                filenameBytes.AsSpan(0, fnLength - 1).CopyTo(header.Slice(i));
                i += fnLength - 1;
                header[i++] = 0; // terminate
            }

            // comment
            if (commentBytes != null && cbLength != 0)
            {
                commentBytes.AsSpan(0, cbLength - 1).CopyTo(header.Slice(i));
                i += cbLength - 1;
                header[i++] = 0; // terminate
            }

            BaseStream.Write(header);

            return header.Length; // bytes written
        }
    }
}
