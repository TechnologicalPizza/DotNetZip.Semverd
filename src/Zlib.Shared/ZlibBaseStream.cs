// See the LICENSE file for license details.

using System;
using System.IO;

namespace Ionic.Zlib
{
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
        private CompressionMode _compressionMode;
        private CompressionLevel _level;
        private bool _leaveOpen;
        private int _bufferSize = DefaultWorkingBufferSize;

        private CompressionStrategy _strategy = CompressionStrategy.Default;
        private byte[] _workingBuffer;
        private Memory<byte> _leftover;
        private bool nomoreinput;

        public Stream BaseStream { get; }
        public bool IsDisposed { get; private set; }
        public ZlibStreamMode StreamMode { get; private set; }

        public long TotalBytesOut { get; protected set; }

        protected bool IsCompressor => _compressionMode == CompressionMode.Compress;
        protected bool IsDecompressor => _compressionMode == CompressionMode.Decompress;

        public ZlibBaseStream(
            Stream stream,
            CompressionMode compressionMode,
            CompressionLevel level,
            bool leaveOpen)
            : base()
        {
            BaseStream = stream ?? throw new ArgumentNullException(nameof(stream));
            _compressionMode = compressionMode;
            _level = level;
            _leaveOpen = leaveOpen;
            _flushMode = ZlibFlushType.None;
        }

        protected void AssertNotDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        protected virtual void GetZlibParams(out bool rfc1950Compliant)
        {
            rfc1950Compliant = false;
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
        public override bool CanRead => IsDecompressor && BaseStream.CanRead;

        /// <summary>
        /// Indicates whether the stream supports Seek operations.
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// Indicates whether the stream can be written.
        /// </summary>
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

        private ZlibCodec Codec
        {
            get
            {
                if (_z == null)
                {
                    GetZlibParams(out bool rfc1950Compliant);

                    _z = new ZlibCodec();
                    if (_compressionMode == CompressionMode.Decompress)
                    {
                        _z.InitializeInflate(rfc1950Compliant);
                    }
                    else
                    {
                        _z.Strategy = _strategy;
                        _z.InitializeDeflate(_level, rfc1950Compliant);
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

        protected virtual void FirstWrite()
        {
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            AssertNotDisposed();
            
            if (StreamMode == ZlibStreamMode.Undefined)
            {
                if (!BaseStream.CanWrite)
                    throw new ZlibException("The stream is not writable.");

                FirstWrite();

                StreamMode = ZlibStreamMode.Writer;
            }
            else if (StreamMode != ZlibStreamMode.Writer)
                throw new ZlibException("Cannot Write after Reading.");

            if (buffer.IsEmpty)
                return;

            var z = Codec;
            var input = buffer;
            var output = WorkingBuffer.AsSpan();

            bool done;
            do
            {
                var (code, message) = IsCompressor
                    ? z.Deflate(_flushMode, input, output, out int consumed, out int written)
                    : z.Inflate(_flushMode, input, output, out consumed, out written);

                input = input.Slice(consumed);

                if (code != ZlibCode.Ok && code != ZlibCode.StreamEnd)
                    throw new ZlibException((IsCompressor ? "de" : "in") + "flating: " + message);

                if (written > 0)
                    BaseStream.Write(output.Slice(0, written));

                done = WriteDone(input.Length, output.Length - written);
            }
            while (!done);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        protected virtual bool WriteDone(int bytesIn, int bytesOut)
        {
            return bytesIn == 0 && bytesOut != 0;
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
                    TotalBytesOut += written;

                    if (code != ZlibCode.StreamEnd && code != ZlibCode.Ok)
                    {
                        string verb = (IsCompressor ? "de" : "in") + "flating";
                        if (message == null)
                            throw new ZlibException(string.Format("{0}: (code = {1})", verb, code));
                        else
                            throw new ZlibException(verb + ": " + message);
                    }

                    if (written > 0)
                        BaseStream.Write(output.Slice(0, written));

                    done = WriteDone(input.Length, output.Length - written);
                }
                while (!done);

                PostWriteFinish();
            }
            else if (StreamMode == ZlibStreamMode.Reader)
            {
                PostReadFinish(input.Span);
            }
        }

        protected virtual void PostWriteFinish()
        {
        }

        protected virtual void PostReadFinish(ReadOnlySpan<byte> input)
        {
        }

        private void End()
        {
            if (_z == null)
                return;

            if (IsCompressor)
                _z.EndDeflate();
            else
                _z.EndInflate();

            _z = null!;
        }

        public override int ReadByte()
        {
            Span<byte> buf = stackalloc byte[1];
            if (Read(buf) == 0)
                return -1;
            return buf[0];
        }

        protected virtual bool FirstRead()
        {
            return true;
        }

        public override int Read(Span<byte> buffer)
        {
            AssertNotDisposed();

            if (StreamMode == ZlibStreamMode.Undefined)
            {
                if (!BaseStream.CanRead)
                    throw new ZlibException("The stream is not readable.");

                if (!FirstRead())
                    return 0;

                StreamMode = ZlibStreamMode.Reader;
            }
            else if (StreamMode != ZlibStreamMode.Reader)
                throw new ZlibException("Cannot Read after Writing.");

            if (buffer.IsEmpty)
                return 0;

            if (nomoreinput && IsCompressor)
                return 0;  // workitem 8557

            var z = Codec;
            Span<byte> output = buffer;
            Memory<byte> input = _leftover;

            ZlibCode code;
            string? message;
            do
            {
                // Need data in _workingBuffer in order to deflate/inflate. 
                // Here, we check if we have any.
                if (input.Length == 0 && !nomoreinput)
                {
                    // No data available, so try to Read data from the underlying stream.

                    var workBuf = WorkingBuffer;
                    int n = BaseStream.Read(workBuf);
                    input = workBuf.AsMemory(0, n);

                    if (input.Length == 0)
                        nomoreinput = true;

                }
                // we have data in InputBuffer; now compress or decompress as appropriate

                (code, message) = IsCompressor
                    ? z.Deflate(_flushMode, input.Span, output, out int consumed, out int written)
                    : z.Inflate(_flushMode, input.Span, output, out consumed, out written);

                input = input.Slice(consumed);
                output = output.Slice(written);
                TotalBytesOut += written;

                if (nomoreinput && (code == ZlibCode.BufError))
                    return 0;

                if (code != ZlibCode.Ok && code != ZlibCode.StreamEnd)
                    throw new ZlibException(string.Format(
                        "{0}flating:  code={1}  msg={2}", IsCompressor ? "de" : "in", code, message));

                if ((nomoreinput || code == ZlibCode.StreamEnd) && (output.Length == buffer.Length))
                    break; // nothing more to read
            }
            while (output.Length > 0 && !nomoreinput && code == ZlibCode.Ok);


            // workitem 8557
            // is there more room in output?
            if (output.Length > 0)
            {
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

                        if (code != ZlibCode.Ok &&
                            code != ZlibCode.StreamEnd)
                        {
                            throw new ZlibException(
                                string.Format("Deflating:  code={0}  msg={1}", code, message));
                        }
                    }
                }
            }

            _leftover = input;

            int read = buffer.Length - output.Length;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
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
    }
}
