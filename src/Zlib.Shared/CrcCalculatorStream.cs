﻿// See the LICENSE file for license details.

using System;

namespace Ionic
{
    /// <summary>
    /// Calculates a CRC32 checksum of all read or written bytes.
    /// </summary>
    /// <remarks>
    /// This class can be used to verify the CRC of data when
    /// reading from a stream, or to calculate a CRC when writing to a stream.
    /// </remarks>
    public class CrcCalculatorStream : System.IO.Stream
    {
        private const long UnsetLengthLimit = -99;
        private readonly System.IO.Stream _innerStream;
        private readonly Crc32 _crc32;
        private readonly long _lengthLimit = UnsetLengthLimit;

        /// <summary>
        /// The default constructor.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     Instances returned from this constructor will leave the underlying
        ///     stream open upon Close().  The stream uses the default CRC32
        ///     algorithm, which implies a polynomial of 0xEDB88320.
        ///   </para>
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        public CrcCalculatorStream(System.IO.Stream stream)
            : this(true, UnsetLengthLimit, stream, null)
        {
        }

        /// <summary>
        ///   The constructor allows the caller to specify how to handle the
        ///   underlying stream at close.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     The stream uses the default CRC32 algorithm, which implies a
        ///     polynomial of 0xEDB88320.
        ///   </para>
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        /// <param name="leaveOpen">true to leave the underlying stream
        /// open upon close of the <c>CrcCalculatorStream</c>; false otherwise.</param>
        public CrcCalculatorStream(System.IO.Stream stream, bool leaveOpen)
            : this(leaveOpen, UnsetLengthLimit, stream, null)
        {
        }

        /// <summary>
        ///   A constructor allowing the specification of the length of the stream
        ///   to read.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     The stream uses the default CRC32 algorithm, which implies a
        ///     polynomial of 0xEDB88320.
        ///   </para>
        ///   <para>
        ///     Instances returned from this constructor will leave the underlying
        ///     stream open upon Close().
        ///   </para>
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        /// <param name="length">The length of the stream to slurp</param>
        public CrcCalculatorStream(System.IO.Stream stream, long length)
            : this(true, length, stream, null)
        {
            if (length < 0)
                throw new ArgumentException(null, nameof(length));
        }

        /// <summary>
        ///   A constructor allowing the specification of the length of the stream
        ///   to read, as well as whether to keep the underlying stream open upon
        ///   Close().
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     The stream uses the default CRC32 algorithm, which implies a
        ///     polynomial of 0xEDB88320.
        ///   </para>
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        /// <param name="length">The length of the stream to slurp</param>
        /// <param name="leaveOpen">true to leave the underlying stream
        /// open upon close of the <c>CrcCalculatorStream</c>; false otherwise.</param>
        public CrcCalculatorStream(System.IO.Stream stream, long length, bool leaveOpen)
            : this(leaveOpen, length, stream, null)
        {
            if (length < 0)
                throw new ArgumentException(null, nameof(length));
        }

        /// <summary>
        ///   A constructor allowing the specification of the length of the stream
        ///   to read, as well as whether to keep the underlying stream open upon
        ///   Close(), and the CRC32 instance to use.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     The stream uses the specified CRC32 instance, which allows the
        ///     application to specify how the CRC gets calculated.
        ///   </para>
        /// </remarks>
        /// <param name="stream">The underlying stream</param>
        /// <param name="length">The length of the stream to slurp</param>
        /// <param name="leaveOpen">true to leave the underlying stream
        /// open upon close of the <c>CrcCalculatorStream</c>; false otherwise.</param>
        /// <param name="crc32">the CRC32 instance to use to calculate the CRC32</param>
        public CrcCalculatorStream(System.IO.Stream stream, long length, bool leaveOpen, Crc32 crc32)
            : this(leaveOpen, length, stream, crc32)
        {
            if (length < 0)
                throw new ArgumentException(null, nameof(length));
        }


        // This ctor is private - no validation except null is done here.
        // This is to allow the use
        // of a (specific) negative value for the _lengthLimit, to indicate that there
        // is no length set.  So we validate the length limit in those ctors that use an
        // explicit param, otherwise we don't validate, because it could be our special
        // value.
        private CrcCalculatorStream(bool leaveOpen, long length, System.IO.Stream stream, Crc32 crc32)
        {
            _innerStream = stream ?? throw new ArgumentNullException(nameof(stream));
            _crc32 = crc32 ?? new Crc32();
            _lengthLimit = length;
            LeaveOpen = leaveOpen;
        }


        /// <summary>
        ///   Gets the total number of bytes run through the CRC32 calculator.
        /// </summary>
        ///
        /// <remarks>
        ///   This is either the total number of bytes read, or the total number of
        ///   bytes written, depending on the direction of this stream.
        /// </remarks>
        public long TotalBytesSlurped => _crc32.BytesProcessed;

        /// <summary>
        ///   Provides the current CRC for all blocks slurped in.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     The running total of the CRC is kept as data is written or read
        ///     through the stream.  read this property after all reads or writes to
        ///     get an accurate CRC for the entire stream.
        ///   </para>
        /// </remarks>
        public int Crc => _crc32.Result;

        /// <summary>
        ///   Indicates whether the underlying stream will be left open when the
        ///   <c>CrcCalculatorStream</c> is Closed.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     Set this at any point before calling <see cref="Close()"/>.
        ///   </para>
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

            if (_lengthLimit != UnsetLengthLimit)
            {
                if (_crc32.BytesProcessed >= _lengthLimit)
                    return 0; // EOF

                long bytesRemaining = _lengthLimit - _crc32.BytesProcessed;
                if (bytesRemaining < buffer.Length)
                    buffer = buffer.Slice(0, (int)bytesRemaining);
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
        ///   <para>
        ///     Always returns false.
        ///   </para>
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
        ///   Returns the length of the underlying stream.
        /// </summary>
        public override long Length
        {
            get
            {
                if (_lengthLimit == UnsetLengthLimit)
                    return _innerStream.Length;
                else
                    return _lengthLimit;
            }
        }

        /// <summary>
        ///   The getter for this property returns the total bytes read.
        ///   If you use the setter, it will throw
        /// <see cref="NotSupportedException"/>.
        /// </summary>
        public override long Position
        {
            get => _crc32.BytesProcessed;
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Seeking is not supported on this stream. This method always throws
        /// <see cref="NotSupportedException"/>
        /// </summary>
        /// <param name="offset">N/A</param>
        /// <param name="origin">N/A</param>
        /// <returns>N/A</returns>
        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// This method always throws
        /// <see cref="NotSupportedException"/>
        /// </summary>
        /// <param name="value">N/A</param>
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
