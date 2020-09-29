// See the LICENSE file for license details.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Ionic.Zlib
{
    /// <summary>
    ///   Stream for compression or decompression using the GZip format.
    /// </summary>
    /// <remarks>
    /// 
    /// <para>
    ///   Like <see cref="System.IO.Compression.GZipStream"/>,
    ///   <see cref="GZipStream"/> can compress while writing, or decompress while reading.  
    ///   The compression method used is GZIP, which is documented in 
    ///   <see href="http://www.ietf.org/rfc/rfc1952.txt">IETF RFC 1952</see>,
    ///   "GZIP file format specification version 4.3".
    /// </para>
    ///
    /// <para>
    ///   Though the GZIP format allows data from multiple files to be concatenated
    ///   together, this stream handles only a single segment of GZIP format, typically
    ///   representing a single file.
    /// </para>
    ///
    /// </remarks>
    ///
    /// <seealso cref="DeflateStream" />
    /// <seealso cref="ZlibStream" />
    public class GZipStream : ZlibBaseStream
    {
        #region GZip format spec

        // Source: http://tools.ietf.org/html/rfc1952

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

        // On generation:
        // all optional fields get 0, except for the OS, which gets 255.

        #endregion

        public static DateTime UnixEpoch { get; } = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static Encoding Iso8859Dash1 { get; } = Encoding.GetEncoding("iso-8859-1");

        private int _headerByteCount;
        private Crc32 _crc;

        private string? _fileName;
        private string? _comment;

        /// <summary>
        ///   Gets the position of the stream pointer.
        /// </summary>
        ///
        /// <remarks>
        ///   Gets the total bytes written out, if used in writing,
        ///   or the total bytes read in, if used in reading. 
        ///   The count may refer to compressed bytes or uncompressed bytes,
        ///   depending on how you've used the stream.
        /// </remarks>
        /// <exception cref="NotSupportedException">Setting the value.</exception>
        public override long Position
        {
            get
            {
                throw new NotImplementedException();

                //if (IsCompressor)
                //    return TotalBytesOut + _headerByteCount;
                //if (IsDecompressor)
                //    return TotalBytesIn + _headerByteCount;
                return 0;
            }
            set => throw new NotSupportedException();
        }

        #region GZip properties

        /// <summary>
        ///   The comment on the GZIP stream.
        /// </summary>
        /// <remarks>
        /// <para>
        ///   The GZIP format allows for each file to optionally have an associated
        ///   comment stored with the file.
        ///   The comment is encoded with the ISO-8859-1 code page.  
        ///   To include a comment in a GZIP stream you create, 
        ///   set this property before writing.
        /// </para>
        /// <para>
        ///   When compressing, you can set this before the first write.  
        ///   When decompressing, you can retrieve this after the first read.
        /// </para>
        /// </remarks>
        public string? Comment
        {
            get => _comment;
            set
            {
                AssertNotDisposed();
                _comment = value;
            }
        }

        /// <summary>
        ///   The FileName for the GZIP stream.
        /// </summary>
        /// <remarks>
        /// <para>
        ///   The GZIP format optionally allows each file to have an associated
        ///   filename. When compressing data, set this property before writing.
        ///   The actual filename is encoded into the GZIP bytestream with the
        ///   ISO-8859-1 code page, according to RFC 1952. It is the application's
        ///   responsibility to insure that the FileName can be encoded and decoded
        ///   correctly with this code page.
        /// </para>
        /// <para>
        ///   When compressing, you can set this before the first write.  
        ///   When decompressing, you can retrieve this after the first read.
        /// </para>
        /// </remarks>
        public string? FileName
        {
            get => _fileName;
            set
            {
                AssertNotDisposed();

                _fileName = value;
                if (_fileName == null)
                    return;

                var comp = StringComparison.OrdinalIgnoreCase;

                if (_fileName.IndexOf("/", comp) != -1)
                    _fileName = _fileName.Replace("/", "\\", comp);

                if (_fileName.EndsWith("\\", comp))
                    throw new Exception("Illegal filename");

                if (_fileName.IndexOf("\\", comp) != -1)
                {
                    // trim any leading path
                    _fileName = Path.GetFileName(_fileName);
                }
            }
        }

        /// <summary>
        ///   The last modified time for the GZIP stream.
        /// </summary>
        /// <remarks>
        ///   GZIP allows the storage of a last modified time with each GZIP entry.
        ///   When compressing, you can set this before the first write.  
        ///   When decompressing, you can retrieve this after the first read.
        /// </remarks>
        public DateTime? LastModified { get; set; }

        /// <summary>
        /// The CRC on the GZIP stream.
        /// </summary>
        /// <remarks>
        /// This is used for internal error checking.
        /// </remarks>
        public int Crc32 => _crc.Result;

        #endregion

        #region Constructors

        /// <summary>
        ///   Create a <see cref="GZipStream"/> using the specified <see cref="CompressionMode"/>
        ///   and default compression level. 
        ///   The stream will be closed upon disposal.
        /// </summary>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the <see cref="GZipStream"/> will compress or decompress.</param>
        public GZipStream(Stream stream, CompressionMode mode)
            : this(stream, mode, false)
        {
        }

        /// <summary>
        ///   Create a <see cref="GZipStream"/> using the specified <see cref="CompressionMode"/>,
        ///   default compression level,
        ///   and whether the stream should be left open after disposal.
        /// </summary>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="mode">Indicates whether the <see cref="GZipStream"/> will compress or decompress.</param>
        /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open after disposal.</param>
        public GZipStream(Stream stream, CompressionMode mode, bool leaveOpen)
            : this(stream, mode, CompressionLevel.Default, leaveOpen)
        {
        }

        /// <summary>
        ///   Create a compressor <see cref="GZipStream"/> using the specified <see cref="CompressionLevel"/>.
        ///   The stream will be closed upon disposal.
        /// </summary>
        /// <param name="stream">The stream to write to while deflating.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        public GZipStream(Stream stream, CompressionLevel level)
            : this(stream, level, false)
        {
        }

        /// <summary>
        ///   Create a compressor <see cref="GZipStream"/> using the specified <see cref="CompressionLevel"/>,
        ///   and whether the stream should be left open after disposal.
        /// </summary>
        /// <param name="stream">The stream which will be read or written.</param>
        /// <param name="level">A tuning knob to trade speed for effectiveness.</param>
        /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open after disposal.</param>
        public GZipStream(Stream stream, CompressionLevel level, bool leaveOpen)
            : this(stream, CompressionMode.Compress, level, leaveOpen)
        {
        }

        private GZipStream(Stream stream, CompressionMode mode, CompressionLevel level, bool leaveOpen) :
            base(stream, mode, level, leaveOpen)
        {
            _crc = new Crc32();
        }

        #endregion

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            // calculate the CRC on the uncompressed data (before writing)
            _crc.Slurp(buffer);

            base.Write(buffer);
        }

        public override int Read(Span<byte> buffer)
        {
            int read = base.Read(buffer);

            // calculate CRC after reading
            _crc.Slurp(buffer.Slice(0, read));

            return read;
        }

        protected override bool WriteDone(int bytesIn, int bytesOut)
        {
            // If GZIP and de-compress, we're done when 8 bytes remain.
            if (!IsCompressor)
                return bytesIn == 8 && bytesOut != 0;

            return base.WriteDone(bytesIn, bytesOut);
        }

        protected override void FirstWrite()
        {
            if (!IsCompressor)
                throw new InvalidOperationException();

            // first write in compression, therefore, emit the GZIP header
            _headerByteCount = EmitHeader();
        }

        protected override bool FirstRead()
        {
            if (!IsDecompressor)
                throw new InvalidOperationException();

            _headerByteCount = ReadAndValidateGzipHeader();
            if (_headerByteCount == 0)
                return false;

            return true;
        }

        protected override void PostWriteFinish()
        {
            if (!IsCompressor)
                throw new ZlibException("Writing with decompression is not supported.");

            // Emit the GZIP trailer: CRC32 and  size mod 2^32

            Span<byte> tmp = stackalloc byte[8];
            int c1 = _crc.Result;
            int c2 = (int)(_crc.BytesProcessed & 0x00000000FFFFFFFF);

            BinaryPrimitives.WriteInt32LittleEndian(tmp, c1);
            BinaryPrimitives.WriteInt32LittleEndian(tmp.Slice(4), c2);
            BaseStream.Write(tmp);
        }

        protected override void PostReadFinish(ReadOnlySpan<byte> input)
        {
            if (!IsDecompressor)
                throw new ZlibException("Reading with compression is not supported.");

            // handle edge case (decompress empty stream)
            if (TotalBytesOut == 0)
                return;

            // Read and potentially verify the GZIP trailer:
            // CRC32 and size mod 2^32
            Span<byte> trailer = stackalloc byte[8];

            if (input.Length < 8)
            {
                // Make sure we have read to the end of the stream
                input.CopyTo(trailer);
                int bytesNeeded = 8 - input.Length;

                Span<byte> toFill = trailer.Slice(input.Length, bytesNeeded);
                do
                {
                    int bytesRead = BaseStream.Read(toFill);
                    if (bytesRead == 0)
                        break;
                    toFill = toFill.Slice(bytesRead);
                }
                while (toFill.Length > 0);

                if (toFill.Length > 0)
                {
                    throw new ZlibException(string.Format(
                        "Missing or incomplete GZIP trailer. Expected 8 bytes, missing {0}.",
                        toFill.Length));
                }
            }
            else
            {
                input.Slice(0, trailer.Length).CopyTo(trailer);
            }

            int crc32_expected = BinaryPrimitives.ReadInt32LittleEndian(trailer);
            int crc32_actual = _crc.Result;
            int isize_expected = BinaryPrimitives.ReadInt32LittleEndian(trailer.Slice(4));
            int isize_actual = (int)(TotalBytesOut & 0x00000000FFFFFFFF);

            if (crc32_actual != crc32_expected)
                throw new ZlibException(
                    string.Format(
                        "Bad CRC32 in GZIP trailer. (actual({0:X8})!=expected({1:X8}))",
                        crc32_actual, crc32_expected));

            if (isize_actual != isize_expected)
                throw new ZlibException(
                    string.Format(
                        "Bad size in GZIP trailer. (actual({0})!=expected({1}))",
                        isize_actual, isize_expected));
        }

        private int ReadAndValidateGzipHeader()
        {
            int totalBytesRead = 0;
            Span<byte> header = stackalloc byte[10];

            Span<byte> headerSlice = header;
            while (headerSlice.Length > 0)
            {
                int read = BaseStream.Read(headerSlice);
                if (read == 0)
                    break;

                headerSlice = headerSlice.Slice(read);
                totalBytesRead += read;
            }

            if (headerSlice.Length == header.Length)
                return 0; // nothing was read

            if (headerSlice.Length != 0)
                throw new EndOfStreamException("Not enough bytes to form a valid GZIP header.");

            if (header[0] != 0x1F || header[1] != 0x8B || header[2] != 8)
                throw new ZlibException("Bad GZIP header.");

            int timet = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4));
            LastModified = UnixEpoch.AddSeconds(timet);

            if ((header[3] & 0x04) == 0x04)
            {
                // read and discard extra field

                Span<byte> extraLengthSlice = header.Slice(0, sizeof(short));
                while (extraLengthSlice.Length > 0)
                {
                    int read = BaseStream.Read(extraLengthSlice);
                    if (read == 0)
                        break;

                    extraLengthSlice = extraLengthSlice.Slice(read);
                    totalBytesRead += read;
                }

                int extraLength = -1;
                if (extraLengthSlice.Length == 0) // skip if length wasn't fully read
                {
                    extraLength = BinaryPrimitives.ReadInt16LittleEndian(header);
                    Span<byte> skipBuffer = stackalloc byte[Math.Min(1024, extraLength)];
                    while (extraLength > 0)
                    {
                        int toSkip = Math.Min(skipBuffer.Length, extraLength);
                        int read = BaseStream.Read(skipBuffer.Slice(0, toSkip));
                        if (read == 0)
                            break;

                        extraLength -= read;
                        totalBytesRead += read;
                    }
                }

                if (extraLength != 0)
                    throw new EndOfStreamException("Unexpected end-of-file reading GZIP header.");
            }

            if ((header[3] & 0x08) == 0x08)
                _fileName = ReadZeroTerminatedString();

            if ((header[3] & 0x10) == 0x010)
                _comment = ReadZeroTerminatedString();

            if ((header[3] & 0x02) == 0x02)
                Read(stackalloc byte[1]); // CRC16, ignore

            return totalBytesRead;
        }

        private string ReadZeroTerminatedString()
        {
            var list = new System.Collections.Generic.List<byte>();
            while (true)
            {
                int n = BaseStream.ReadByte();
                if (n == -1)
                    throw new EndOfStreamException("Unexpected EOF reading GZIP header.");

                if (n == 0)
                    break;

                list.Add((byte)n);
            }

            byte[] a = list.ToArray();
            return Iso8859Dash1.GetString(a, 0, a.Length);
        }

        private int EmitHeader()
        {
            // TODO: write strings to stackalloced buffer directly

            byte[]? commentBytes = (Comment == null) ? null : Iso8859Dash1.GetBytes(Comment);
            byte[]? filenameBytes = (FileName == null) ? null : Iso8859Dash1.GetBytes(FileName);

            int cbLength = (commentBytes == null) ? 0 : commentBytes.Length + 1;
            int fnLength = (filenameBytes == null) ? 0 : filenameBytes.Length + 1;

            int bufferLength = 10 + cbLength + fnLength;
            var header = bufferLength <= 4096 
                ? stackalloc byte[bufferLength] : new byte[bufferLength];
            int i = 0;

            // ID
            header[i++] = 0x1F;
            header[i++] = 0x8B;

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
            TimeSpan delta = LastModified.Value - UnixEpoch;
            int timet = (int)delta.TotalSeconds;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(i), timet);
            i += 4;

            // xflg
            header[i++] = 0; // extra flags are unused
            // OS
            header[i++] = 0xff; // 0xff == unspecified

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
