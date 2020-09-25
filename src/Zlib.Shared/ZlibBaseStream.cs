// ZlibBaseStream.cs
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
// Time-stamp: <2011-August-06 21:22:38>
//
// ------------------------------------------------------------------
//
// This module defines the ZlibBaseStream class, which is an intnernal
// base class for DeflateStream, ZlibStream and GZipStream.
//
// ------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.IO;

namespace Ionic.Zlib
{
    internal enum ZlibStreamFlavor
    {
        ZLIB = 1950,
        DEFLATE = 1951,
        GZIP = 1952
    }

    internal class ZlibBaseStream : Stream
    {
        internal enum StreamMode
        {
            Writer,
            Reader,
            Undefined,
        }

        protected internal ZlibCodec _z;

        protected internal StreamMode _streamMode = StreamMode.Undefined;
        protected internal FlushType _flushMode;
        protected internal ZlibStreamFlavor _flavor;
        protected internal CompressionMode _compressionMode;
        protected internal CompressionLevel _level;
        protected internal bool _leaveOpen;
        protected internal byte[] _workingBuffer;
        private Memory<byte> _leftover;
        protected internal int _bufferSize = ZlibConstants.WorkingBufferSizeDefault;

        protected internal Stream _stream;
        protected internal CompressionStrategy Strategy = CompressionStrategy.Default;

        private bool nomoreinput;

        // workitem 7159
        private Crc.Crc32 _crc;
        protected internal string _GzipFileName;
        protected internal string _GzipComment;
        protected internal DateTime _GzipMtime;
        protected internal int _gzipHeaderByteCount;
        protected internal long _totalBytesOut;

        internal int Crc32 { get { if (_crc == null) return 0; return _crc.Crc32Result; } }

        public ZlibBaseStream(
            Stream stream,
            CompressionMode compressionMode,
            CompressionLevel level,
            ZlibStreamFlavor flavor,
            bool leaveOpen)
            : base()
        {
            _flushMode = FlushType.None;
            //this._workingBuffer = new byte[WORKING_BUFFER_SIZE_DEFAULT];
            _stream = stream;
            _leaveOpen = leaveOpen;
            _compressionMode = compressionMode;
            _flavor = flavor;
            _level = level;

            // workitem 7159
            if (flavor == ZlibStreamFlavor.GZIP)
            {
                _crc = new Crc.Crc32();
            }
        }


        protected internal bool WantCompress
        {
            get
            {
                return _compressionMode == CompressionMode.Compress;
            }
        }

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
                        _z.Strategy = Strategy;
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
            if (_streamMode == StreamMode.Undefined)
                _streamMode = StreamMode.Writer;
            else if (_streamMode != StreamMode.Writer)
                throw new ZlibException("Cannot Write after Reading.");

            if (buffer.Length == 0)
                return;

            // calculate the CRC on the uncompressed data
            _crc?.SlurpBlock(buffer);

            var z = Z;
            var input = buffer;
            var output = WorkingBuffer.AsSpan();

            bool done;
            do
            {
                ZlibCode rc = WantCompress
                    ? z.Deflate(_flushMode, input, output, out int consumed, out int written)
                    : z.Inflate(_flushMode, input, output, out consumed, out written);

                input = input.Slice(consumed);
                _totalBytesOut += written;

                if (rc != ZlibCode.Ok && rc != ZlibCode.StreamEnd)
                    throw new ZlibException((WantCompress ? "de" : "in") + "flating: " + z.Message);

                if (written > 0)
                    _stream.Write(output.Slice(0, written));

                // If GZIP and de-compress, we're done when 8 bytes remain.
                if (_flavor == ZlibStreamFlavor.GZIP && !WantCompress)
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


        private void Finish()
        {
            if (_z == null)
                return;

            Memory<byte> input = _leftover;

            if (_streamMode == StreamMode.Writer)
            {
                Span<byte> output = WorkingBuffer.AsSpan();

                bool done;
                do
                {
                    ZlibCode rc = WantCompress
                        ? _z.Deflate(FlushType.Finish, input.Span, output, out int consumed, out int written)
                        : _z.Inflate(FlushType.Finish, input.Span, output, out consumed, out written);

                    input = input.Slice(consumed);
                    _totalBytesOut += written;

                    if (rc != ZlibCode.StreamEnd && rc != ZlibCode.Ok)
                    {
                        string verb = (WantCompress ? "de" : "in") + "flating";
                        if (_z.Message == null)
                            throw new ZlibException(string.Format("{0}: (rc = {1})", verb, rc));
                        else
                            throw new ZlibException(verb + ": " + _z.Message);
                    }

                    if (written > 0)
                        _stream.Write(output.Slice(0, written));

                    // If GZIP and de-compress, we're done when 8 bytes remain.
                    if (_flavor == ZlibStreamFlavor.GZIP && !WantCompress)
                        done = input.Length == 8 && output.Length - written != 0;
                    else
                        done = input.Length == 0 && output.Length - written != 0;

                }
                while (!done);

                Flush();

                // workitem 7159
                if (_flavor == ZlibStreamFlavor.GZIP)
                {
                    if (WantCompress)
                    {
                        // Emit the GZIP trailer: CRC32 and  size mod 2^32

                        Span<byte> tmp = stackalloc byte[8];
                        int c1 = _crc.Crc32Result;
                        int c2 = (int)(_crc.TotalBytesRead & 0x00000000FFFFFFFF);

                        BinaryPrimitives.WriteInt32LittleEndian(tmp, c1);
                        BinaryPrimitives.WriteInt32LittleEndian(tmp.Slice(4), c2);
                        _stream.Write(tmp);
                    }
                    else
                    {
                        throw new ZlibException("Writing with decompression is not supported.");
                    }
                }
            }
            // workitem 7159
            else if (_streamMode == StreamMode.Reader)
            {
                if (_flavor == ZlibStreamFlavor.GZIP)
                {
                    if (!WantCompress)
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
                            int bytesRead = _stream.Read(trailer.Slice(input.Length, bytesNeeded));
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
                        int crc32_actual = _crc.Crc32Result;
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

            if (WantCompress)
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

            if (_stream == null)
                return;
            try
            {
                Finish();
            }
            finally
            {
                End();
                if (!_leaveOpen)
                    _stream.Dispose();
                _stream = null!;
            }
        }

        public override void Flush()
        {
            _stream.Flush();
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

            // calculate CRC after reading
            _crc?.SlurpBlock(buf);
            return buf[0] & 0xFF;
        }

        private string ReadZeroTerminatedString()
        {
            var list = new System.Collections.Generic.List<byte>();
            bool done = false;
            do
            {
                // workitem 7740
                int n = _stream.ReadByte();
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
            int n = _stream.Read(header);

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
                n = _stream.Read(header.Slice(0, 2)); // 2-byte length field
                totalBytesRead += n;

                short extraLength = (short)(header[0] + header[1] * 256);
                byte[] extra = new byte[extraLength];
                n = _stream.Read(extra, 0, extra.Length);
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
            if (_streamMode == StreamMode.Undefined)
            {
                if (!_stream.CanRead)
                    throw new ZlibException("The stream is not readable.");

                // for the first read, set up some controls.
                _streamMode = StreamMode.Reader;

                if (_flavor == ZlibStreamFlavor.GZIP)
                {
                    _gzipHeaderByteCount = ReadAndValidateGzipHeader();
                    // workitem 8501: handle edge case (decompress empty stream)
                    if (_gzipHeaderByteCount == 0)
                        return 0;
                }
            }

            if (_streamMode != StreamMode.Reader)
                throw new ZlibException("Cannot Read after Writing.");
            if (nomoreinput && WantCompress)
                return 0;  // workitem 8557
            
            var z = Z;
            Span<byte> output = buffer;
            Memory<byte> input = _leftover;

            ZlibCode rc;
            do
            {
                // need data in _workingBuffer in order to deflate/inflate. 
                // Here, we check if we have any.
                if (input.Length == 0 && !nomoreinput)
                {
                    // No data available, so try to Read data from the captive stream.

                    var buf = WorkingBuffer;
                    int n = _stream.Read(buf);
                    input = buf.AsMemory(0, n);

                    if (input.Length == 0)
                        nomoreinput = true;

                }
                // we have data in InputBuffer; now compress or decompress as appropriate


                rc = WantCompress
                    ? z.Deflate(_flushMode, input.Span, output, out int consumed, out int written)
                    : z.Inflate(_flushMode, input.Span, output, out consumed, out written);

                input = input.Slice(consumed);
                output = output.Slice(written);
                _totalBytesOut += written;

                if (nomoreinput && (rc == ZlibCode.BufError))
                    return 0;

                if (rc != ZlibCode.Ok && rc != ZlibCode.StreamEnd)
                    throw new ZlibException(string.Format(
                        "{0}flating:  rc={1}  msg={2}", WantCompress ? "de" : "in", rc, z.Message));

                if ((nomoreinput || rc == ZlibCode.StreamEnd) && (output.Length == buffer.Length))
                    break; // nothing more to read
            }
            //while (_z.AvailableBytesOut == count && rc == ZlibCode.Z_OK);
            while (output.Length > 0 && !nomoreinput && rc == ZlibCode.Ok);


            // workitem 8557
            // is there more room in output?
            if (output.Length > 0)
            {
                if (rc == ZlibCode.Ok && input.Length == 0)
                {
                    // deferred
                }

                // are we completely done reading?
                if (nomoreinput)
                {
                    // and in compression?
                    if (WantCompress)
                    {
                        // no more input data available; therefore we flush to
                        // try to complete the read
                        rc = z.Deflate(
                            FlushType.Finish, input.Span, output, out int consumed, out int written);

                        input = input.Slice(consumed);
                        output = output.Slice(written);

                        if (rc != ZlibCode.Ok && rc != ZlibCode.StreamEnd)
                            throw new ZlibException(
                                string.Format("Deflating:  rc={0}  msg={1}", rc, z.Message));
                    }
                }
            }

            _leftover = input;

            int read = buffer.Length - output.Length;

            // calculate CRC after reading
            _crc?.SlurpBlock(buffer.Slice(0, read));

            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }



        public override bool CanRead
        {
            get { return _stream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return _stream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _stream.CanWrite; }
        }

        public override long Length
        {
            get { return _stream.Length; }
        }

        public override long Position
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }
    }
}
