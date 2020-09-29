// See the LICENSE file for license details.

using System;
using System.IO;

namespace Ionic
{
    /// <summary>
    /// Calculates a CRC32 checksum of all read or written bytes.
    /// </summary>
    /// <remarks>
    /// This class can be used to verify the CRC of data when
    /// reading from a stream, or to calculate a CRC when writing to a stream.
    /// </remarks>
    public class CrcStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly Crc32 _crc32;
        private readonly long? _lengthLimit;

        /// <summary>
        /// The default constructor.
        /// </summary>
        /// <remarks>
        ///   Instances returned from this constructor will leave the underlying
        ///   stream open upon Close().  The stream uses the default CRC32
        ///   algorithm, which implies a polynomial of 0xEDB88320.
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        public CrcStream(Stream stream) : this(stream, false)
        {
        }

        /// <summary>
        ///   The constructor allows the caller to specify how to handle the
        ///   underlying stream at close.
        /// </summary>
        /// <remarks>
        ///   The stream uses the default CRC32 algorithm, which implies a
        ///   polynomial of 0xEDB88320.
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        /// <param name="leaveOpen">true to leave the underlying stream
        /// open upon close of the <c>CrcCalculatorStream</c>; false otherwise.</param>
        public CrcStream(Stream stream, bool leaveOpen) : this(stream, leaveOpen, null)
        {
        }

        /// <summary>
        ///   A constructor allowing the specification of the length of the stream
        ///   to read. The stream is closed upon disposal.
        /// </summary>
        /// <remarks>
        ///   The stream uses the default CRC32 algorithm, which implies a
        ///   polynomial of 0xEDB88320.
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        /// <param name="length">The length of the stream to slurp</param>
        public CrcStream(Stream stream, long? length) : this(stream, false, length)
        {
        }

        /// <summary>
        ///   A constructor allowing the specification of the length of the stream
        ///   to read, as well as whether to keep the underlying stream open upon
        ///   Close().
        /// </summary>
        /// <remarks>
        ///   The stream uses the default CRC32 algorithm, which implies a
        ///   polynomial of 0xEDB88320.
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        /// <param name="leaveOpen">true to leave the underlying stream
        /// open upon close of the <c>CrcCalculatorStream</c>; false otherwise.</param>
        /// <param name="length">The length of the stream to slurp</param>
        public CrcStream(Stream stream, bool leaveOpen, long? length)
            : this(stream, leaveOpen, length, null)
        {
        }

        /// <summary>
        ///   A constructor allowing the specification of the length of the stream
        ///   to read, as well as whether to keep the underlying stream open upon
        ///   Close(), and the CRC32 instance to use.
        /// </summary>
        /// <remarks>
        ///   The stream uses the specified CRC32 instance, which allows the
        ///   application to specify how the CRC gets calculated.
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        /// <param name="leaveOpen">true to leave the underlying stream
        /// open upon close of the <c>CrcCalculatorStream</c>; false otherwise.</param>
        /// <param name="length">The length of the stream to slurp</param>
        /// <param name="crc32">the CRC32 instance to use to calculate the CRC32</param>
        public CrcStream(Stream stream, bool leaveOpen, long? length, Crc32? crc32)
        {
            if (length.HasValue && length.GetValueOrDefault() <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            _innerStream = stream ?? throw new ArgumentNullException(nameof(stream));
            _crc32 = crc32 ?? new Crc32();
            _lengthLimit = length;
            LeaveOpen = leaveOpen;
        }

        /// <summary>
        ///   Gets the total number of bytes run through the CRC32 calculator.
        /// </summary>
        /// <remarks>
        ///   This is either the total number of bytes read, or the total number of
        ///   bytes written, depending on the direction of this stream.
        /// </remarks>
        public long BytesProcessed => _crc32.BytesProcessed;

        /// <summary>
        ///   Provides the current CRC32 for all blocks slurped in.
        /// </summary>
        /// <remarks>
        ///   The running total of the CRC is kept as data is written or read
        ///   through the stream.  Read this property after all reads or writes to
        ///   get a checksum for the entire stream.
        /// </remarks>
        public int CrcChecksum => _crc32.Result;

        /// <summary>
        ///   Indicates whether the underlying stream will be left open when the
        ///   <c>CrcCalculatorStream</c> is Closed.
        /// </summary>
        /// <remarks>
        ///   Set this at any point before calling <see cref="Stream.Dispose())"/>.
        /// </remarks>
        public bool LeaveOpen { get; set; }

        /// <summary>
        /// Read from the stream.
        /// </summary>
        /// <param name="buffer">the buffer to read.</param>
        /// <returns>the number of bytes actually read.</returns>
        public override int Read(Span<byte> buffer)
        {
            // Need to limit the # of bytes returned, if the stream is intended to have
            // a definite length.  This is especially useful when returning a stream for
            // the uncompressed data directly to the application.  The app won't
            // necessarily read only the UncompressedSize number of bytes.  For example
            // wrapping the stream returned from OpenReader() into a StreadReader() and
            // calling ReadToEnd() on it, We can "over-read" the zip data and get a
            // corrupt string.  The length limits that, prevents that problem.

            if (_lengthLimit.HasValue)
            {
                long limit = _lengthLimit.GetValueOrDefault();
                if (_crc32.BytesProcessed >= limit)
                    return 0; // EOF

                int bytesRemaining = (int)(limit - _crc32.BytesProcessed);
                if (bytesRemaining < buffer.Length)
                    buffer = buffer.Slice(0, bytesRemaining);
            }

            int n = _innerStream.Read(buffer);
            _crc32.Slurp(buffer.Slice(0, n));
            return n;
        }

        /// <summary>
        /// Read from the stream.
        /// </summary>
        /// <param name="buffer">the buffer to read.</param>
        /// <param name="offset">the offset at which to start.</param>
        /// <param name="count">the number of bytes to read.</param>
        /// <returns>the number of bytes actually read.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        /// <summary>
        /// Write to the stream.
        /// </summary>
        /// <param name="buffer">the buffer from which to write</param>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _crc32.Slurp(buffer);
            _innerStream.Write(buffer);
        }

        /// <summary>
        /// Write to the stream.
        /// </summary>
        /// <param name="buffer">the buffer from which to write</param>
        /// <param name="offset">the offset at which to start writing</param>
        /// <param name="count">the number of bytes to write</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan());
        }

        /// <summary>
        /// Indicates whether the stream supports reading.
        /// </summary>
        public override bool CanRead => _innerStream.CanRead;

        /// <summary>
        ///   Indicates whether the stream supports seeking.
        /// </summary>
        /// <remarks>
        ///   Always returns false.
        /// </remarks>
        public override bool CanSeek => false;

        /// <summary>
        /// Indicates whether the stream supports writing.
        /// </summary>
        public override bool CanWrite => _innerStream.CanWrite;

        /// <summary>
        /// Flush the stream.
        /// </summary>
        public override void Flush()
        {
            _innerStream.Flush();
        }

        /// <summary>
        ///   Returns the length of the underlying stream or the length limit.
        /// </summary>
        public override long Length
        {
            get
            {
                if (_lengthLimit.HasValue)
                    return _lengthLimit.GetValueOrDefault();
                return _innerStream.Length;
            }
        }

        /// <summary>
        ///   The getter for this property returns the total bytes read.
        ///   If you use the setter, it will throw <see cref="NotSupportedException"/>.
        /// </summary>
        public override long Position
        {
            get => _crc32.BytesProcessed;
            set => throw new NotSupportedException();
        }
        /// <summary>
        /// </summary>
        /// <exception cref="NotSupportedException"></exception
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!LeaveOpen)
                    _innerStream.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
