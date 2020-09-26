// See the LICENSE file for license details.

using System;

namespace Ionic.Zlib
{
    internal sealed class InflateBlocks
    {
        private const int MANY = 1440;

        // Table for deflate from PKZIP's appnote.txt.
        internal static readonly int[] border = new int[]
        {
            16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
        };

        private InflateBlockMode _mode;      // current inflate_block mode

        internal int left;                  // if STORED, bytes left to copy

        internal int table;                 // table lengths (14 bits)
        internal int index;                 // index into blens (or border)
        internal int[]? _bitLengths;              // bit lengths of codes
        internal int bb;                    // bit length tree depth
        internal int tb;                    // bit length decoding tree

        internal InflateCodes codes = new InflateCodes(); // if CODES, current state
        internal InflateTree inftree = new InflateTree();

        internal int last;                  // true if this block is the last block

        internal Inflater _inflater;

        // mode independent information
        internal int bitk;                  // bits in bit buffer
        internal int bitb;                  // bit buffer
        internal int[] _treeSpace;          // single malloc for tree space
        internal byte[] _window;            // sliding window
        internal int readAt;                // window read pointer
        internal int writeAt;               // window write pointer
        internal uint check;                // check on output
        internal bool _checkRfc1950;

        internal int WindowLength => _window.Length;  // one byte after sliding window

        internal InflateBlocks(Inflater inflater, bool checkRfc1950, int windowSize)
        {
            _inflater = inflater ?? throw new ArgumentNullException(nameof(inflater));
            _treeSpace = new int[MANY * 3];
            _window = new byte[windowSize];
            _checkRfc1950 = checkRfc1950;
            _mode = InflateBlockMode.TYPE;

            Reset();
        }

        internal uint Reset()
        {
            uint oldCheck = check;
            _mode = InflateBlockMode.TYPE;
            bitk = 0;
            bitb = 0;
            readAt = 0;
            writeAt = 0;

            if (_checkRfc1950)
            {
                check = ZlibConstants.InitialAdler32;
                _inflater._adler32 = ZlibConstants.InitialAdler32;
            }

            return oldCheck;
        }

        public int GetBytesToEnd()
        {
            return writeAt < readAt
                ? readAt - writeAt - 1
                : WindowLength - writeAt;
        }

        internal ZlibCode Process(
            ZlibCode r,
            ReadOnlySpan<byte> input, ref Span<byte> output,
            ref int consumed, ref int length, ref int written,
            out string? message)
        {
            ref int b = ref bitb;    // bit buffer
            ref int k = ref bitk;    // bits in bit buffer
            ref int q = ref writeAt; // output window write pointer
            int m = GetBytesToEnd(); // bytes to end of window or read pointer

            // proxying class values with locals should be faster
            // TODO: move these locals to a struct
            //       then copy the struct from class onto stack
            //       then copy back to class with try/finally to 
            //       avoid assignments on every return

            message = null;

            // process input based on current state
            while (true)
            {
                switch (_mode)
                {
                    #region TYPE
                    case InflateBlockMode.TYPE:
                    {
                        while (k < 3)
                        {
                            if (length != 0)
                                r = ZlibCode.Ok;
                            else
                                return Flush(r, ref output, ref written);

                            length--;
                            b |= (input[consumed++] & 0xff) << k;
                            k += 8;
                        }
                        int t = b & 7;
                        last = t & 1;

                        switch ((uint)t >> 1)
                        {
                            case 0:  // stored
                                b >>= 3;
                                k -= 3;
                                t = k & 7; // go to byte boundary
                                b >>= t;
                                k -= t;
                                _mode = InflateBlockMode.LENS; // get length of stored block
                                break;

                            case 1:  // fixed
                                int bl = 0;
                                int bd = 0;
                                InflateTree.InflateTreesFixed(ref bl, ref bd, out int[] tl, out int[] td);
                                codes.Init(bl, bd, tl, 0, td, 0);
                                b >>= 3;
                                k -= 3;
                                _mode = InflateBlockMode.CODES;
                                break;

                            case 2:  // dynamic
                                b >>= 3;
                                k -= 3;
                                _mode = InflateBlockMode.TABLE;
                                break;

                            case 3:  // illegal
                                b >>= 3;
                                k -= 3;
                                _mode = InflateBlockMode.BAD;
                                message = "invalid block type";
                                r = ZlibCode.DataError;

                                return Flush(r, ref output, ref written);
                        }
                        break;
                    }
                    #endregion

                    #region LENS
                    case InflateBlockMode.LENS:
                    {
                        while (k < 32)
                        {
                            if (length != 0)
                                r = ZlibCode.Ok;
                            else
                                return Flush(r, ref output, ref written);

                            length--;
                            b |= (input[consumed++] & 0xff) << k;
                            k += 8;
                        }

                        if ((((~b) >> 16) & 0xffff) != (b & 0xffff))
                        {
                            _mode = InflateBlockMode.BAD;
                            message = "invalid stored block lengths";
                            r = ZlibCode.DataError;

                            return Flush(r, ref output, ref written);
                        }
                        left = b & 0xffff;
                        b = k = 0; // dump bits
                        _mode = left != 0
                            ? InflateBlockMode.STORED
                            : (last != 0 ? InflateBlockMode.DRY : InflateBlockMode.TYPE);
                        break;
                    }
                    #endregion

                    #region STORED
                    case InflateBlockMode.STORED:
                    {
                        if (length == 0)
                            return Flush(r, ref output, ref written);

                        if (m == 0)
                        {
                            if (q == WindowLength && readAt != 0)
                            {
                                q = 0;
                                m = GetBytesToEnd();
                            }

                            if (m == 0)
                            {
                                r = Flush(r, ref output, ref written);
                                m = GetBytesToEnd();
                                if (q == WindowLength && readAt != 0)
                                {
                                    q = 0;
                                    m = GetBytesToEnd();
                                }

                                if (m == 0)
                                    return Flush(r, ref output, ref written);
                            }
                        }
                        r = ZlibCode.Ok;

                        int t = left;
                        if (t > length)
                            t = length;
                        if (t > m)
                            t = m;

                        input.Slice(consumed, t).CopyTo(_window.AsSpan(q));
                        consumed += t;
                        length -= t;
                        q += t;
                        m -= t;
                        if ((left -= t) != 0)
                            break;
                        _mode = last != 0 ? InflateBlockMode.DRY : InflateBlockMode.TYPE;
                        break;
                    }
                    #endregion

                    #region TABLE
                    case InflateBlockMode.TABLE:
                    {
                        while (k < 14)
                        {
                            if (length != 0)
                                r = ZlibCode.Ok;
                            else
                                return Flush(r, ref output, ref written);

                            length--;
                            b |= (input[consumed++] & 0xff) << k;
                            k += 8;
                        }

                        int t;
                        table = t = b & 0x3fff;
                        if ((t & 0x1f) > 29 || ((t >> 5) & 0x1f) > 29)
                        {
                            _mode = InflateBlockMode.BAD;
                            message = "too many length or distance symbols";
                            r = ZlibCode.DataError;

                            return Flush(r, ref output, ref written);
                        }
                        t = 258 + (t & 0x1f) + ((t >> 5) & 0x1f);

                        if (_bitLengths == null || _bitLengths.Length < t)
                            _bitLengths = new int[t];
                        else
                            Array.Clear(_bitLengths, 0, t);

                        b >>= 14;
                        k -= 14;

                        index = 0;
                        _mode = InflateBlockMode.BTREE;
                        goto case InflateBlockMode.BTREE;
                    }
                    #endregion

                    #region BTREE
                    case InflateBlockMode.BTREE:
                    {
                        int[]? bitLengths = _bitLengths;
                        if (bitLengths == null)
                            throw new ZlibException("Invalid state.");

                        while (index < 4 + (table >> 10))
                        {
                            while (k < 3)
                            {
                                if (length != 0)
                                    r = ZlibCode.Ok;
                                else
                                    return Flush(r, ref output, ref written);

                                length--;
                                b |= (input[consumed++] & 0xff) << k;
                                k += 8;
                            }

                            bitLengths[border[index++]] = b & 7;

                            b >>= 3;
                            k -= 3;
                        }

                        while (index < 19)
                        {
                            bitLengths[border[index++]] = 0;
                        }

                        bb = 7;
                        var tr = inftree.InflateTreesBits(
                            bitLengths, ref bb, ref tb, _treeSpace, out message);

                        if (tr != ZlibCode.Ok)
                        {
                            r = tr;
                            if (r == ZlibCode.DataError)
                            {
                                _bitLengths = null;
                                _mode = InflateBlockMode.BAD;
                            }

                            return Flush(r, ref output, ref written);
                        }

                        index = 0;
                        _mode = InflateBlockMode.DTREE;
                        goto case InflateBlockMode.DTREE;
                    }
                    #endregion

                    #region DTREE
                    case InflateBlockMode.DTREE:
                    {
                        int[]? bitLengths = _bitLengths;
                        if (bitLengths == null)
                            throw new ZlibException("Unknown state.");

                        int[] inflateMask = InflateConstants.InflateMask;
                        while (true)
                        {
                            int t = table;
                            if (!(index < 258 + (t & 0x1f) + ((t >> 5) & 0x1f)))
                                break;

                            int i, j, c;

                            t = bb;
                            while (k < t)
                            {
                                if (length != 0)
                                    r = ZlibCode.Ok;
                                else
                                    return Flush(r, ref output, ref written);

                                length--;
                                b |= (input[consumed++] & 0xff) << k;
                                k += 8;
                            }

                            t = _treeSpace[(tb + (b & inflateMask[t])) * 3 + 1];
                            c = _treeSpace[(tb + (b & inflateMask[t])) * 3 + 2];

                            if (c < 16)
                            {
                                b >>= t;
                                k -= t;
                                bitLengths[index++] = c;
                            }
                            else
                            {
                                // c == 16..18
                                i = c == 18 ? 7 : c - 14;
                                j = c == 18 ? 11 : 3;

                                while (k < (t + i))
                                {
                                    if (length != 0)
                                        r = ZlibCode.Ok;
                                    else
                                        return Flush(r, ref output, ref written);

                                    length--;
                                    b |= (input[consumed++] & 0xff) << k;
                                    k += 8;
                                }

                                b >>= t;
                                k -= t;

                                j += b & inflateMask[i];

                                b >>= i;
                                k -= i;

                                i = index;
                                t = table;

                                if (i + j > 258 + (t & 0x1f) + ((t >> 5) & 0x1f) || (c == 16 && i < 1))
                                {
                                    _bitLengths = null;
                                    _mode = InflateBlockMode.BAD;
                                    message = "invalid bit length repeat";
                                    r = ZlibCode.DataError;

                                    return Flush(r, ref output, ref written);
                                }

                                c = (c == 16) ? bitLengths[i - 1] : 0;
                                do
                                {
                                    bitLengths[i++] = c;
                                }
                                while (--j != 0);
                                index = i;
                            }
                        }

                        tb = -1;
                        {
                            int bl = 9;  // must be <= 9 for lookahead assumptions
                            int bd = 6;  // must be <= 9 for lookahead assumptions
                            int tl = 0;
                            int td = 0;

                            var rt = inftree.InflateTreesDynamic(
                                257 + (table & 0x1f), 1 + ((table >> 5) & 0x1f), _bitLengths,
                                ref bl, ref bd, ref tl, ref td, _treeSpace, out message);

                            if (rt != ZlibCode.Ok)
                            {
                                if (rt == ZlibCode.DataError)
                                {
                                    _bitLengths = null;
                                    _mode = InflateBlockMode.BAD;
                                }
                                r = rt;

                                return Flush(r, ref output, ref written);
                            }
                            codes.Init(bl, bd, _treeSpace, tl, _treeSpace, td);
                        }
                        _mode = InflateBlockMode.CODES;
                        goto case InflateBlockMode.CODES;
                    }
                    #endregion

                    case InflateBlockMode.CODES:
                    {
                        r = codes.Process(
                            this, r, input,
                            ref output, ref consumed, ref length, ref written, out message);

                        if (r != ZlibCode.StreamEnd)
                            return Flush(r, ref output, ref written);

                        r = ZlibCode.Ok;
                        m = GetBytesToEnd();

                        if (last == 0)
                        {
                            _mode = InflateBlockMode.TYPE;
                            break;
                        }
                        _mode = InflateBlockMode.DRY;
                        goto case InflateBlockMode.DRY;
                    }

                    case InflateBlockMode.DRY:
                    {
                        r = Flush(r, ref output, ref written);
                        //m = blocks.GetBytesToEnd(); // END case returns

                        if (readAt != q)
                            return Flush(r, ref output, ref written);

                        _mode = InflateBlockMode.DONE;
                        goto case InflateBlockMode.DONE;
                    }

                    case InflateBlockMode.DONE:
                        r = ZlibCode.StreamEnd;
                        return Flush(r, ref output, ref written);

                    case InflateBlockMode.BAD:
                        r = ZlibCode.DataError;
                        return Flush(r, ref output, ref written);

                    default:
                        r = ZlibCode.StreamError;
                        return Flush(r, ref output, ref written);
                }
            }
        }

        internal void SetDictionary(ReadOnlySpan<byte> dictionary)
        {
            dictionary.CopyTo(_window);
            readAt = dictionary.Length;
            writeAt = dictionary.Length;
        }

        // Returns true if inflate is currently at the end of a block generated
        // by Z_SYNC_FLUSH or Z_FULL_FLUSH.
        internal int SyncPoint()
        {
            return _mode == InflateBlockMode.LENS ? 1 : 0;
        }

        // copy as much as possible from the sliding window to the output area
        internal ZlibCode Flush(ZlibCode r, ref Span<byte> output, ref int written)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int count;
                if (pass == 0)
                {
                    // compute number of bytes to copy as far as end of window
                    count = (readAt <= writeAt ? writeAt : WindowLength) - readAt;
                }
                else
                {
                    // compute bytes to copy
                    count = writeAt - readAt;
                }

                // workitem 8870
                if (count == 0)
                {
                    if (r == ZlibCode.BufError)
                        r = ZlibCode.Ok;
                    return r;
                }

                if (count > output.Length)
                    count = output.Length;

                if (count != 0 && r == ZlibCode.BufError)
                    r = ZlibCode.Ok;

                // update check information
                if (_checkRfc1950)
                {
                    check = Adler32.Compute(check, _window.AsSpan(readAt, count));
                    _inflater._adler32 = check;
                }

                // copy as far as end of window
                _window.AsSpan(readAt, count).CopyTo(output);
                output = output.Slice(count);
                written += count;
                readAt += count;

                // see if more to copy at beginning of window
                if (readAt == WindowLength && pass == 0)
                {
                    // wrap pointers
                    readAt = 0;
                    if (writeAt == WindowLength)
                        writeAt = 0;
                }
                else
                {
                    pass++;
                }
            }

            // done
            return r;
        }
    }
}