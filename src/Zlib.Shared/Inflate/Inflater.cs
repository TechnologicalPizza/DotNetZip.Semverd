// See the LICENSE file for license details.

using System;

namespace Ionic.Zlib
{
    public sealed class Inflater
    {
        // preset dictionary flag in zlib header
        private const int PRESET_DICT = 0x20;

        private const int Z_DEFLATED = 8;

        private static ReadOnlySpan<byte> Mark => new byte[] { 0, 0, 0xff, 0xff };

        private InflaterMode mode; // current inflate mode

        // mode dependent information
        internal int method; // if FLAGS, method byte

        // if CHECK, check values to compare
        internal uint _computedAdler32;
        internal uint _expectedAdler32;
        internal uint _adler32;

        // if BAD, inflateSync's marker bytes count
        internal int marker;

        internal int _windowBits; // log2(window size)  (8..15, defaults to 15)

        internal InflateBlocks? _blocks; // current inflate_blocks state

        public bool HandleRfc1950HeaderBytes { get; set; } = true;
        public int AdlerChecksum => (int)_adler32;

        public Inflater()
        {
        }

        public void Reset()
        {
            mode = HandleRfc1950HeaderBytes ? InflaterMode.METHOD : InflaterMode.BLOCKS;
            _blocks.Reset();
        }

        public void End()
        {
            _blocks = null;
        }

        public void Initialize(int windowBits = ZlibConstants.DefaultWindowBits)
        {
            _blocks = null;

            // handle undocumented nowrap option (no zlib header or check)
            //nowrap = 0;
            //if (w < 0)
            //{
            //    w = - w;
            //    nowrap = 1;
            //}

            // set window size
            if (windowBits < 8 || windowBits > 15)
                throw new ArgumentOutOfRangeException(nameof(windowBits), "Bad window size.");
            _windowBits = windowBits;

            _blocks = new InflateBlocks(this, 1 << windowBits);

            // reset state
            Reset();
        }


        public (ZlibCode Code, string? Message) Inflate(
            ZlibFlushType flush, ReadOnlySpan<byte> input, Span<byte> output,
            out int consumed, out int written)
        {
            int b;

            //int f = (flush == FlushType.Finish)
            //    ? ZlibCode.Z_BUF_ERROR
            //    : ZlibCode.Z_OK;

            // workitem 8870
            var f = ZlibCode.Ok;
            var r = ZlibCode.BufError;
            int length = input.Length;

            consumed = 0;
            written = 0;
            string? message = null;

            while (true)
            {
                switch (mode)
                {
                    case InflaterMode.METHOD:
                        if (length == 0)
                            return (r, message);

                        r = f;
                        length--;
                        if (((method = input[consumed++]) & 0xf) != Z_DEFLATED)
                        {
                            mode = InflaterMode.BAD;
                            message = string.Format("unknown compression method (0x{0:X2})", method);
                            marker = 5; // can't try inflateSync
                            break;
                        }
                        if ((method >> 4) + 8 > _windowBits)
                        {
                            mode = InflaterMode.BAD;
                            message = string.Format("invalid window size ({0})", (method >> 4) + 8);
                            marker = 5; // can't try inflateSync
                            break;
                        }
                        mode = InflaterMode.FLAG;
                        break;


                    case InflaterMode.FLAG:
                        if (length == 0)
                            return (r, message);
                        r = f;
                        length--;
                        b = (input[consumed++]) & 0xff;

                        if ((((method << 8) + b) % 31) != 0)
                        {
                            mode = InflaterMode.BAD;
                            message = "incorrect header check";
                            marker = 5; // can't try inflateSync
                            break;
                        }

                        mode = ((b & PRESET_DICT) == 0)
                            ? InflaterMode.BLOCKS
                            : InflaterMode.DICT4;
                        break;

                    case InflaterMode.DICT4:
                        if (length == 0)
                            return (r, message);
                        r = f;
                        length--;
                        _expectedAdler32 = (uint)((input[consumed++] << 24) & 0xff000000);
                        mode = InflaterMode.DICT3;
                        break;

                    case InflaterMode.DICT3:
                        if (length == 0)
                            return (r, message);
                        r = f;
                        length--;
                        _expectedAdler32 += (uint)((input[consumed++] << 16) & 0x00ff0000);
                        mode = InflaterMode.DICT2;
                        break;

                    case InflaterMode.DICT2:
                        if (length == 0)
                            return (r, message);
                        r = f;
                        length--;
                        _expectedAdler32 += (uint)((input[consumed++] << 8) & 0x0000ff00);
                        mode = InflaterMode.DICT1;
                        break;


                    case InflaterMode.DICT1:
                        if (length == 0)
                            return (r, message);

                        //length--; // unnecessary due to return
                        _expectedAdler32 += (uint)(input[consumed++] & 0x000000ff);
                        _adler32 = _expectedAdler32;
                        mode = InflaterMode.DICT0;
                        return (ZlibCode.NeedDict, message);


                    case InflaterMode.DICT0:
                        mode = InflaterMode.BAD;
                        message = "need dictionary";
                        marker = 0; // can try inflateSync
                        return (ZlibCode.StreamError, message);


                    case InflaterMode.BLOCKS:
                        r = _blocks.Process(
                            r, input,
                            ref output, ref consumed, ref length, ref written, out message);

                        if (r == ZlibCode.DataError)
                        {
                            mode = InflaterMode.BAD;
                            marker = 0; // can try inflateSync
                            break;
                        }

                        if (r == ZlibCode.Ok)
                            r = f;

                        if (r != ZlibCode.StreamEnd)
                            return (r, message);

                        r = f;
                        _computedAdler32 = _blocks.Reset();
                        if (!HandleRfc1950HeaderBytes)
                        {
                            mode = InflaterMode.DONE;
                            return (ZlibCode.StreamEnd, message);
                        }
                        mode = InflaterMode.CHECK4;
                        break;

                    case InflaterMode.CHECK4:
                        if (length == 0)
                            return (r, message);
                        r = f;
                        length--;
                        _expectedAdler32 = (uint)((input[consumed++] << 24) & 0xff000000);
                        mode = InflaterMode.CHECK3;
                        break;

                    case InflaterMode.CHECK3:
                        if (length == 0)
                            return (r, message);
                        r = f;
                        length--;
                        _expectedAdler32 += (uint)((input[consumed++] << 16) & 0x00ff0000);
                        mode = InflaterMode.CHECK2;
                        break;

                    case InflaterMode.CHECK2:
                        if (length == 0)
                            return (r, message);
                        r = f;
                        length--;
                        _expectedAdler32 += (uint)((input[consumed++] << 8) & 0x0000ff00);
                        mode = InflaterMode.CHECK1;
                        break;

                    case InflaterMode.CHECK1:
                        if (length == 0)
                            return (r, message);
                        r = f;
                        length--;
                        _expectedAdler32 += (uint)(input[consumed++] & 0x000000ff);
                        if (_computedAdler32 != _expectedAdler32)
                        {
                            mode = InflaterMode.BAD;
                            message = "incorrect data check";
                            marker = 5; // can't try inflateSync
                            break;
                        }
                        mode = InflaterMode.DONE;
                        return (ZlibCode.StreamEnd, message);

                    case InflaterMode.DONE:
                        return (ZlibCode.StreamEnd, message);

                    case InflaterMode.BAD:
                        throw new ZlibException(string.Format("Bad state ({0})", message));

                    default:
                        throw new ZlibException("Stream error.");

                }
            }
        }



        public ZlibCode SetDictionary(ReadOnlySpan<byte> dictionary, bool unconditional = false)
        {
            if (_blocks == null)
                throw new InvalidOperationException();

            // MSZip requires the dictionary to be set unconditionally
            if (!unconditional)
            {
                if (mode != InflaterMode.DICT0)
                    throw new ZlibException("Stream error.");

                if (Adler32.Compute(1, dictionary) != _adler32)
                    return ZlibCode.DataError;
            }

            _adler32 = ZlibConstants.InitialAdler32;

            if (dictionary.Length >= (1 << _windowBits))
            {
                int length = (1 << _windowBits) - 1;
                int index = dictionary.Length - length;
                dictionary = dictionary.Slice(index, length);
            }

            _blocks.SetDictionary(dictionary);
            mode = InflaterMode.BLOCKS;
            return ZlibCode.Ok;
        }

        public ZlibCode Sync(ReadOnlySpan<byte> input, out int consumed)
        {
            // set up
            if (mode != InflaterMode.BAD)
            {
                mode = InflaterMode.BAD;
                marker = 0;
            }

            consumed = 0;
            if (input.IsEmpty)
                return ZlibCode.BufError;

            // search
            while (input.Length - consumed > 0 && marker < 4)
            {
                if (input[consumed] == Mark[marker])
                    marker++;
                else if (input[consumed] != 0)
                    marker = 0;
                else
                    marker = 4 - marker;

                consumed++;
            }

            // return no joy or set up to restart on a new block
            if (marker != 4)
                return ZlibCode.DataError;

            Reset();

            mode = InflaterMode.BLOCKS;
            return ZlibCode.Ok;
        }

        /// <summary>
        /// Returns true if inflate is currently at the end of a block generated
        /// by Z_SYNC_FLUSH or Z_FULL_FLUSH. This function is used by one PPP
        /// implementation to provide an additional safety check. PPP uses Z_SYNC_FLUSH
        /// but removes the length bytes of the resulting empty stored block. When
        /// decompressing, PPP checks that at the end of input packet, inflate is
        /// waiting for these length bytes.
        /// </summary>
        internal int SyncPoint()
        {
            if (_blocks == null)
                throw new InvalidOperationException();

            return _blocks.SyncPoint();
        }
    }
}