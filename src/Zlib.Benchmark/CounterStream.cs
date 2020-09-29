using System;
using System.IO;

namespace Zlib.Benchmark
{
    public class CounterStream : Stream
    {
        public Stream BaseStream { get; }

        public long TotalRead { get; private set; }
        public long TotalWrite { get; private set; }

        public override bool CanRead => BaseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => BaseStream.CanWrite;

        public override long Length => BaseStream.Length;
        public override long Position
        {
            get => BaseStream.Position;
            set => throw new NotSupportedException();
        }

        public CounterStream(Stream baseStream)
        {
            BaseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        }

        public override void Flush()
        {
            BaseStream.Flush();
        }

        public override int Read(Span<byte> buffer)
        {
            int read = BaseStream.Read(buffer);
            TotalRead += read;
            return read;
        }

        public override int ReadByte()
        {
            Span<byte> buf = stackalloc byte[1];
            if (Read(buf) == 0)
                return -1;
            return buf[0];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BaseStream.Write(buffer);
            TotalWrite += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            Write(stackalloc byte[] { value });
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
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

            if (disposing)
            {
                BaseStream.Dispose();
            }
        }
    }
}
