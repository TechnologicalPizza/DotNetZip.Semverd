// See the LICENSE file for license details.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Ionic.Zlib
{
    public enum ZlibStreamFlavor
    {
        ZLIB = 1950,
        DEFLATE = 1951,
        GZIP = 1952
    }

    public enum ZlibStreamMode
    {
        Undefined,
        Writer,
        Reader
    }

    public abstract class ZlibBaseStream : Stream
    {
        /// <summary>
        /// The default size of the working buffer.
        /// </summary>
        public const int DefaultWorkingBufferSize = 1024 * 32;

        private ZlibCodec _z;
        private ZlibFlushType _flushMode;
        private ZlibStreamFlavor _flavor;
        private CompressionMode _compressionMode;
        private CompressionLevel _level;
        private bool _leaveOpen;
        private int _bufferSize = DefaultWorkingBufferSize;

        private CompressionStrategy _strategy = CompressionStrategy.Default;
        private byte[] _workingBuffer;
        private Memory<byte> _leftover;
        private bool nomoreinput;

        // workitem 7159
        private Crc32 _crc;
        protected string _GzipFileName;
        protected string _GzipComment;
        protected DateTime _GzipMtime;
        protected int _gzipHeaderByteCount;
        protected long _totalBytesOut;

        public Stream BaseStream { get; }
        public bool IsDisposed { get; private set; }
        public ZlibStreamMode StreamMode { get; private set; }

        protected bool IsCompressor => _compressionMode == CompressionMode.Compress;
        protected bool IsDecompressor => _compressionMode == CompressionMode.Decompress;

        public int Crc32 => _crc?.Result ?? 0;

        public ZlibBaseStream(
            Stream stream,
            CompressionMode compressionMode,
            CompressionLevel level,
            ZlibStreamFlavor flavor,
            bool leaveOpen)
            : base()
        {
            BaseStream = stream ?? throw new ArgumentNullException(nameof(stream));
            _compressionMode = compressionMode;
            _level = level;
            _flavor = flavor;
            _leaveOpen = leaveOpen;
            _flushMode = ZlibFlushType.None;

            // workitem 7159
            if (flavor == ZlibStreamFlavor.GZIP)
                _crc = new Crc32();
        }

        protected void AssertNotDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }


        #region Zlib properties

        /// <summary>
        /// This property sets the flush behavior on the stream.
        /// </summary>
        public virtual ZlibFlushType FlushMode
        {
            get => _flushMode;
            set
            {
                AssertNotDisposed();
                _flushMode = value;
            }
        }

        /// <summary>
        ///   The size of the working buffer for the compression codec.
        /// </summary>
        /// <remarks>
        /// <para>
        ///   The working buffer is used for all stream operations.
        ///   A larger buffer may yield better performance.
        /// </para>
        /// <para>
        ///   Set this before the first call to <c>Read()</c> or <c>Write()</c> on the
        ///   stream. If you try to set it afterwards, it will throw.
        /// </para>
        /// </remarks>
        public int BufferSize
        {
            get => _bufferSize;
            set
            {
                AssertNotDisposed();
                if (_workingBuffer != null)
                    throw new ZlibException("The working buffer is already set.");
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _bufferSize = value;
            }
        }

        #endregion

        #region Stream methods

        /// <summary>
        /// Indicates whether the stream can be read.
        /// </summary>
        /// <remarks>
        /// The return value depends on whether the captive stream supports reading.
        /// </remarks>
        public override bool CanRead => IsDecompressor && BaseStream.CanRead;

        /// <summary>
        /// Indicates whether the stream supports Seek operations.
        /// </summary>
        /// <remarks>
        /// Always returns false.
        /// </remarks>
        public override bool CanSeek => false;

        /// <summary>
        /// Indicates whether the stream can be written.
        /// </summary>
        /// <remarks>
        /// The return value depends on whether the captive stream supports writing.
        /// </remarks>
        public override bool CanWrite => IsCompressor && BaseStream.CanWrite;

        /// <summary>
        /// Reading this property always throws a <see cref="NotSupportedException"/>.
        /// </summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>
        ///   The position of the stream pointer.
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

                //if (_baseStream._streamMode == ZlibBaseStream.StreamMode.Writer)
                //    return _baseStream._z.TotalBytesOut + _headerByteCount;
                //if (_baseStream._streamMode == ZlibBaseStream.StreamMode.Reader)
                //    return _baseStream._z.TotalBytesIn + _baseStream._gzipHeaderByteCount;
                return 0;
            }
            set => throw new NotSupportedException();
        }

        #endregion

        private ZlibCodec Z
        {
            get
            {
                if (_z == null)
                {
                    bool wantRfc1950Header = _flavor == ZlibStreamFlavor.ZLIB;

                    _z = new ZlibCodec();
                    if (_compressionMode == CompressionMode.Decompress)
                    {
                        _z.InitializeInflate(wantRfc1950Header);
                    }
                    else
                    {
                        _z.Strategy = _strategy;
                        _z.InitializeDeflate(_level, wantRfc1950Header);
                    }
                }
                return _z;
            }
        }



        private byte[] WorkingBuffer
        {
            get
            {
                if (_workingBuffer == null)
                    _workingBuffer = new byte[_bufferSize];
                return _workingBuffer;
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            AssertNotDisposed();

            if (StreamMode == ZlibStreamMode.Undefined)
                StreamMode = ZlibStreamMode.Writer;
            else if (StreamMode != ZlibStreamMode.Writer)
                throw new ZlibException("Cannot Write after Reading.");

            if (buffer.Length == 0)
                return;

            // calculate the CRC on the uncompressed data
            _crc?.Slurp(buffer);

            var z = Z;
            var input = buffer;
            var output = WorkingBuffer.AsSpan();

            bool done;
            do
            {
                var (code, message) = IsCompressor
                    ? z.Deflate(_flushMode, input, output, out int consumed, out int written)
                    : z.Inflate(_flushMode, input, output, out consumed, out written);

                input = input.Slice(consumed);
                _totalBytesOut += written;

                if (code != ZlibCode.Ok && code != ZlibCode.StreamEnd)
                    throw new ZlibException((IsCompressor ? "de" : "in") + "flating: " + message);

                if (written > 0)
                    BaseStream.Write(output.Slice(0, written));

                // If GZIP and de-compress, we're done when 8 bytes remain.
                if (_flavor == ZlibStreamFlavor.GZIP && !IsCompressor)
                    done = input.Length == 8 && output.Length - written != 0;
                else
                    done = input.Length == 0 && output.Length - written != 0;

            }
            while (!done);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        /// <summary>
        /// Flush the stream.
        /// </summary>
        public override void Flush()
        {
            AssertNotDisposed();

            BaseStream.Flush();
        }

        private void Finish()
        {
            if (_z == null)
                return;

            Memory<byte> input = _leftover;

            if (StreamMode == ZlibStreamMode.Writer)
            {
                Span<byte> output = WorkingBuffer.AsSpan();

                bool done;
                do
                {
                    var (code, message) = IsCompressor
                        ? _z.Deflate(ZlibFlushType.Finish, input.Span, output, out int consumed, out int written)
                        : _z.Inflate(ZlibFlushType.Finish, input.Span, output, out consumed, out written);

                    input = input.Slice(consumed);
                    _totalBytesOut += written;

                    if (code != ZlibCode.StreamEnd && code != ZlibCode.Ok)
                    {
                        string verb = (IsCompressor ? "de" : "in") + "flating";
                        if (message == null)
                            throw new ZlibException(string.Format("{0}: (rc = {1})", verb, code));
                        else
                            throw new ZlibException(verb + ": " + message);
                    }

                    if (written > 0)
                        BaseStream.Write(output.Slice(0, written));

                    // If GZIP and de-compress, we're done when 8 bytes remain.
                    if (_flavor == ZlibStreamFlavor.GZIP && !IsCompressor)
                        done = input.Length == 8 && output.Length - written != 0;
                    else
                        done = input.Length == 0 && output.Length - written != 0;

                }
                while (!done);

                // workitem 7159
                if (_flavor == ZlibStreamFlavor.GZIP)
                {
                    if (IsCompressor)
                    {
                        // Emit the GZIP trailer: CRC32 and  size mod 2^32

                        Span<byte> tmp = stackalloc byte[8];
                        int c1 = _crc.Result;
                        int c2 = (int)(_crc.BytesProcessed & 0x00000000FFFFFFFF);

                        BinaryPrimitives.WriteInt32LittleEndian(tmp, c1);
                        BinaryPrimitives.WriteInt32LittleEndian(tmp.Slice(4), c2);
                        BaseStream.Write(tmp);
                    }
                    else
                    {
                        throw new ZlibException("Writing with decompression is not supported.");
                    }
                }
            }
            // workitem 7159
            else if (StreamMode == ZlibStreamMode.Reader)
            {
                if (_flavor == ZlibStreamFlavor.GZIP)
                {
                    if (!IsCompressor)
                    {
                        // workitem 8501: handle edge case (decompress empty stream)
                        if (_totalBytesOut == 0)
                            return;


                        // Read and potentially verify the GZIP trailer:
                        // CRC32 and size mod 2^32
                        Span<byte> trailer = stackalloc byte[8];

                        // workitems 8679 & 12554
                        if (input.Length < 8)
                        {
                            // Make sure we have read to the end of the stream
                            input.Span.CopyTo(trailer);

                            int bytesNeeded = 8 - input.Length;
                            int bytesRead = BaseStream.Read(trailer.Slice(input.Length, bytesNeeded));
                            if (bytesNeeded != bytesRead)
                            {
                                throw new ZlibException(
                                    string.Format(
                                        "Missing or incomplete GZIP trailer. Expected 8 bytes, got {0}.",
                                        input.Length + bytesRead));
                            }
                        }
                        else
                        {
                            input.Span.Slice(0, trailer.Length).CopyTo(trailer);
                        }

                        int crc32_expected = BinaryPrimitives.ReadInt32LittleEndian(trailer);
                        int crc32_actual = _crc.Result;
                        int isize_expected = BinaryPrimitives.ReadInt32LittleEndian(trailer.Slice(4));
                        int isize_actual = (int)(_totalBytesOut & 0x00000000FFFFFFFF);

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
                    else
                    {
                        throw new ZlibException("Reading with compression is not supported.");
                    }
                }
            }
        }


        private void End()
        {
            if (Z == null)
                return;

            if (IsCompressor)
                _z.EndDeflate();
            else
                _z.EndInflate();

            _z = null!;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
                return;

            if (BaseStream == null)
                return;
            try
            {
                Finish();
            }
            finally
            {
                End();

                if (!_leaveOpen)
                    BaseStream.Dispose();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int ReadByte()
        {
            Span<byte> buf = stackalloc byte[1];
            if (Read(buf) == 0)
                return -1;
            return buf[0];
        }

        private string ReadZeroTerminatedString()
        {
            var list = new System.Collections.Generic.List<byte>();
            bool done = false;
            do
            {
                // workitem 7740
                int n = BaseStream.ReadByte();
                if (n == -1)
                {
                    throw new EndOfStreamException("Unexpected EOF reading GZIP header.");
                }
                else
                {
                    if (n == 0)
                        done = true;
                    else
                        list.Add((byte)n);
                }
            }
            while (!done);

            byte[] a = list.ToArray(); // TODO: NET5 use CollectionsMarshal.AsSpan(list)
            return GZipStream.iso8859dash1.GetString(a, 0, a.Length);
        }


        private int ReadAndValidateGzipHeader()
        {
            int totalBytesRead = 0;
            // read the header on the first read
            Span<byte> header = stackalloc byte[10];
            int n = BaseStream.Read(header);

            // workitem 8501: handle edge case (decompress empty stream)
            if (n == 0)
                return 0;

            if (n != 10)
                throw new ZlibException("Not a valid GZIP stream.");

            if (header[0] != 0x1F || header[1] != 0x8B || header[2] != 8)
                throw new ZlibException("Bad GZIP header.");

            int timet = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4));
            _GzipMtime = GZipStream._unixEpoch.AddSeconds(timet);
            totalBytesRead += n;
            if ((header[3] & 0x04) == 0x04)
            {
                // read and discard extra field
                n = BaseStream.Read(header.Slice(0, 2)); // 2-byte length field
                totalBytesRead += n;

                short extraLength = (short)(header[0] + header[1] * 256);
                byte[] extra = new byte[extraLength];
                n = BaseStream.Read(extra, 0, extra.Length);
                if (n != extraLength)
                    throw new ZlibException("Unexpected end-of-file reading GZIP header.");
                totalBytesRead += n;
            }
            if ((header[3] & 0x08) == 0x08)
                _GzipFileName = ReadZeroTerminatedString();
            if ((header[3] & 0x10) == 0x010)
                _GzipComment = ReadZeroTerminatedString();
            if ((header[3] & 0x02) == 0x02)
                Read(stackalloc byte[1]); // CRC16, ignore

            return totalBytesRead;
        }


        public override int Read(Span<byte> buffer)
        {
            AssertNotDisposed();

            if (StreamMode == ZlibStreamMode.Undefined)
            {
                if (!BaseStream.CanRead)
                    throw new ZlibException("The stream is not readable.");

                // for the first read, set up some controls.
                StreamMode = ZlibStreamMode.Reader;

                if (_flavor == ZlibStreamFlavor.GZIP)
                {
                    _gzipHeaderByteCount = ReadAndValidateGzipHeader();
                    // workitem 8501: handle edge case (decompress empty stream)
                    if (_gzipHeaderByteCount == 0)
                        return 0;
                }
            }

            if (StreamMode != ZlibStreamMode.Reader)
                throw new ZlibException("Cannot Read after Writing.");
            if (nomoreinput && IsCompressor)
                return 0;  // workitem 8557

            var z = Z;
            Span<byte> output = buffer;
            Memory<byte> input = _leftover;

            ZlibCode code;
            string? message;
            do
            {
                // need data in _workingBuffer in order to deflate/inflate. 
                // Here, we check if we have any.
                if (input.Length == 0 && !nomoreinput)
                {
                    // No data available, so try to Read data from the captive stream.

                    var buf = WorkingBuffer;
                    int n = BaseStream.Read(buf);
                    input = buf.AsMemory(0, n);

                    if (input.Length == 0)
                        nomoreinput = true;

                }
                // we have data in InputBuffer; now compress or decompress as appropriate


                (code, message) = IsCompressor
                    ? z.Deflate(_flushMode, input.Span, output, out int consumed, out int written)
                    : z.Inflate(_flushMode, input.Span, output, out consumed, out written);

                input = input.Slice(consumed);
                output = output.Slice(written);
                _totalBytesOut += written;

                if (nomoreinput && (code == ZlibCode.BufError))
                    return 0;

                if (code != ZlibCode.Ok && code != ZlibCode.StreamEnd)
                    throw new ZlibException(string.Format(
                        "{0}flating:  rc={1}  msg={2}", IsCompressor ? "de" : "in", code, message));

                if ((nomoreinput || code == ZlibCode.StreamEnd) && (output.Length == buffer.Length))
                    break; // nothing more to read
            }
            //while (_z.AvailableBytesOut == count && rc == ZlibCode.Z_OK);
            while (output.Length > 0 && !nomoreinput && code == ZlibCode.Ok);


            // workitem 8557
            // is there more room in output?
            if (output.Length > 0)
            {
                //if (code == ZlibCode.Ok && input.Length == 0)
                //{
                //    // deferred
                //}

                // are we completely done reading?
                if (nomoreinput)
                {
                    // and in compression?
                    if (IsCompressor)
                    {
                        // no more input data available; therefore we flush to
                        // try to complete the read
                        (code, message) = z.Deflate(
                            ZlibFlushType.Finish, input.Span, output, out int consumed, out int written);

                        input = input.Slice(consumed);
                        output = output.Slice(written);

                        if (code != ZlibCode.Ok && code != ZlibCode.StreamEnd)
                            throw new ZlibException(
                                string.Format("Deflating:  rc={0}  msg={1}", code, message));
                    }
                }
            }

            _leftover = input;

            int read = buffer.Length - output.Length;

            // calculate CRC after reading
            _crc?.Slurp(buffer.Slice(0, read));

            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }
    }
}
