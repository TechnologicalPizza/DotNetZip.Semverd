// See the LICENSE file for license details.

using System;

namespace Ionic.Zlib
{
    internal sealed class InflateCodes
    {
        // waiting for "i:"=input,
        //             "o:"=output,
        //             "x:"=nothing
        private const int START = 0; // x: set up for LEN
        private const int LEN = 1; // i: get length/literal/eob next
        private const int LENEXT = 2; // i: getting length extra (have base)
        private const int DIST = 3; // i: get distance next
        private const int DISTEXT = 4; // i: getting distance extra
        private const int COPY = 5; // o: copying bytes in window, waiting for space
        private const int LIT = 6; // o: got literal, waiting for output space
        private const int WASH = 7; // o: got eob, possibly still output waiting
        private const int END = 8; // x: got eob and all data flushed
        private const int BADCODE = 9; // x: got error

        internal int mode;        // current inflate_codes mode

        // mode dependent information
        internal int len;

        internal int[]? tree;      // pointer into tree
        internal int tree_index;
        internal int need;        // bits needed

        internal int lit;

        // if EXT or COPY, where and how much
        internal int bitsToGet;   // bits to get for extra
        internal int dist;        // distance back to copy from

        internal byte lbits;      // ltree bits decoded per branch
        internal byte dbits;      // dtree bits decoder per branch
        internal int[]? ltree;     // literal/length/eob tree
        internal int ltree_index; // literal/length/eob tree
        internal int[]? dtree;     // distance tree
        internal int dtree_index; // distance tree

        internal InflateCodes()
        {
        }

        internal void Init(int bl, int bd, int[]? tl, int tl_index, int[]? td, int td_index)
        {
            mode = START;
            lbits = (byte)bl;
            dbits = (byte)bd;
            ltree = tl;
            ltree_index = tl_index;
            dtree = td;
            dtree_index = td_index;
            tree = null;
        }

        internal ZlibCode Process(
            InflateBlocks blocks, ZlibCode r,
            ReadOnlySpan<byte> input, ref Span<byte> output,
            ref int consumed, ref int length, ref int written)
        {
            ZlibCodec z = blocks._codec;
            byte[] window = blocks._window;
            int windowLength = window.Length;

            // proxying class values with locals should be faster
            // TODO: move these locals to a struct
            //       then copy the struct from class onto stack
            //       then copy back to class with try/finally to 
            //       avoid assignments on every return

            ref int b = ref blocks.bitb;    // bit buffer
            ref int k = ref blocks.bitk;    // bits in bit buffer
            ref int q = ref blocks.writeAt; // output window write pointer
            int m = blocks.GetBytesToEnd(); // bytes to end of window or read pointer
            int[] inflateMask = InflateConstants.InflateMask;

            int j;      // temporary storage
            int tindex; // temporary pointer
            int e;      // extra bits or operation
            int f;      // pointer to copy strings from

            // process input and output based on current state
            while (true)
            {
                switch (mode)
                {
                    // waiting for "i:"=input, "o:"=output, "x:"=nothing
                    case START:  // x: set up for LEN
                        if (m >= 258 && length >= 10)
                        {
                            r = InflateFast(
                                lbits, dbits, ltree, ltree_index, dtree, dtree_index, blocks,
                                input, ref consumed, ref length, out z.Message);

                            m = blocks.GetBytesToEnd();

                            if (r != ZlibCode.Ok)
                            {
                                mode = (r == ZlibCode.StreamEnd) ? WASH : BADCODE;
                                break;
                            }
                        }
                        need = lbits;
                        tree = ltree;
                        tree_index = ltree_index;

                        mode = LEN;
                        goto case LEN;

                    case LEN:  // i: get length/literal/eob next
                        j = need;

                        while (k < j)
                        {
                            if (length != 0)
                                r = ZlibCode.Ok;
                            else
                                return blocks.Flush(r, ref output, ref written);

                            length--;
                            b |= (input[consumed++] & 0xff) << k;
                            k += 8;
                        }

                        tindex = (tree_index + (b & inflateMask[j])) * 3;

                        b >>= tree[tindex + 1];
                        k -= tree[tindex + 1];

                        e = tree[tindex];

                        if (e == 0)
                        {
                            // literal
                            lit = tree[tindex + 2];
                            mode = LIT;
                            break;
                        }
                        if ((e & 16) != 0)
                        {
                            // length
                            bitsToGet = e & 15;
                            len = tree[tindex + 2];
                            mode = LENEXT;
                            break;
                        }
                        if ((e & 64) == 0)
                        {
                            // next table
                            need = e;
                            tree_index = tindex / 3 + tree[tindex + 2];
                            break;
                        }
                        if ((e & 32) != 0)
                        {
                            // end of block
                            mode = WASH;
                            break;
                        }
                        mode = BADCODE; // invalid code
                        z.Message = "invalid literal/length code";
                        r = ZlibCode.DataError;

                        return blocks.Flush(r, ref output, ref written);


                    case LENEXT:  // i: getting length extra (have base)
                        j = bitsToGet;

                        while (k < j)
                        {
                            if (length != 0)
                                r = ZlibCode.Ok;
                            else
                                return blocks.Flush(r, ref output, ref written);

                            length--;
                            b |= (input[consumed++] & 0xff) << k;
                            k += 8;
                        }

                        len += b & inflateMask[j];

                        b >>= j;
                        k -= j;

                        need = dbits;
                        tree = dtree;
                        tree_index = dtree_index;
                        mode = DIST;
                        goto case DIST;

                    case DIST:  // i: get distance next
                        j = need;

                        while (k < j)
                        {
                            if (length != 0)
                                r = ZlibCode.Ok;
                            else
                                return blocks.Flush(r, ref output, ref written);

                            length--;
                            b |= (input[consumed++] & 0xff) << k;
                            k += 8;
                        }

                        tindex = (tree_index + (b & inflateMask[j])) * 3;

                        b >>= tree[tindex + 1];
                        k -= tree[tindex + 1];

                        e = tree[tindex];
                        if ((e & 0x10) != 0)
                        {
                            // distance
                            bitsToGet = e & 15;
                            dist = tree[tindex + 2];
                            mode = DISTEXT;
                            break;
                        }
                        if ((e & 64) == 0)
                        {
                            // next table
                            need = e;
                            tree_index = tindex / 3 + tree[tindex + 2];
                            break;
                        }
                        mode = BADCODE; // invalid code
                        z.Message = "invalid distance code";
                        r = ZlibCode.DataError;

                        return blocks.Flush(r, ref output, ref written);


                    case DISTEXT:  // i: getting distance extra
                        j = bitsToGet;

                        while (k < j)
                        {
                            if (length != 0)
                                r = ZlibCode.Ok;
                            else
                                return blocks.Flush(r, ref output, ref written);

                            length--;
                            b |= (input[consumed++] & 0xff) << k;
                            k += 8;
                        }

                        dist += b & inflateMask[j];

                        b >>= j;
                        k -= j;

                        mode = COPY;
                        goto case COPY;

                    case COPY:  // o: copying bytes in window, waiting for space
                        f = q - dist;
                        while (f < 0)
                        {
                            // modulo window size-"while" instead
                            f += windowLength; // of "if" handles invalid distances
                        }

                        while (len != 0)
                        {
                            if (m == 0)
                            {
                                if (q == windowLength && blocks.readAt != 0)
                                {
                                    q = 0;
                                    m = blocks.GetBytesToEnd();
                                }

                                if (m == 0)
                                {
                                    r = blocks.Flush(r, ref output, ref written);
                                    m = blocks.GetBytesToEnd();

                                    if (q == windowLength && blocks.readAt != 0)
                                    {
                                        q = 0;
                                        m = blocks.GetBytesToEnd();
                                    }

                                    if (m == 0)
                                        return blocks.Flush(r, ref output, ref written);
                                }
                            }

                            window[q++] = window[f++];
                            m--;

                            if (f == windowLength)
                                f = 0;
                            len--;
                        }
                        mode = START;
                        break;

                    case LIT:  // o: got literal, waiting for output space
                        if (m == 0)
                        {
                            if (q == windowLength && blocks.readAt != 0)
                            {
                                q = 0;
                                m = blocks.GetBytesToEnd();
                            }
                            if (m == 0)
                            {
                                r = blocks.Flush(r, ref output, ref written);
                                m = blocks.GetBytesToEnd();

                                if (q == windowLength && blocks.readAt != 0)
                                {
                                    q = 0;
                                    m = blocks.GetBytesToEnd();
                                }

                                if (m == 0)
                                    return blocks.Flush(r, ref output, ref written);
                            }
                        }
                        r = ZlibCode.Ok;

                        window[q++] = (byte)lit;
                        m--;

                        mode = START;
                        break;

                    case WASH:  // o: got eob, possibly more output
                        if (k > 7)
                        {
                            // return unused byte, if any
                            k -= 8;
                            length++;
                            consumed--; // can always return one
                        }

                        r = blocks.Flush(r, ref output, ref written);
                        //m = blocks.GetBytesToEnd(); // END case returns

                        if (blocks.readAt != q)
                            return blocks.Flush(r, ref output, ref written);

                        mode = END;
                        goto case END;

                    case END:
                        r = ZlibCode.StreamEnd;
                        return blocks.Flush(r, ref output, ref written);

                    case BADCODE: // x: got error
                        r = ZlibCode.DataError;
                        return blocks.Flush(r, ref output, ref written);

                    default:
                        r = ZlibCode.StreamError;
                        return blocks.Flush(r, ref output, ref written);
                }
            }
        }


        // Called with number of bytes left to write in window at least 258
        // (the maximum string length) and number of input bytes available
        // at least ten.  The ten bytes are six bytes for the longest length/
        // distance pair plus four bytes for overloading the bit buffer.

        internal static ZlibCode InflateFast(
            int bl, int bd, int[] tl, int tl_index, int[] td, int td_index, InflateBlocks blocks,
            ReadOnlySpan<byte> input, ref int consumed, ref int AvailableBytesIn, out string? message)
        {
            if (blocks._window == null)
                throw new ZlibException("window is null.");

            byte[] window = blocks._window;
            int windowLength = window.Length;

            int t;        // temporary pointer
            int[] tp;     // temporary pointer
            int tp_index; // temporary pointer
            int e;        // extra bits or operation
            int ml;       // mask for literal/length tree
            int md;       // mask for distance tree
            int c;        // bytes to copy
            int d;        // distance back to copy from
            int r;        // copy source pointer
            int tp_index_t_3; // (tp_index + t) * 3

            int n = AvailableBytesIn;       // bytes available there
            ref int b = ref blocks.bitb;    // bit buffer
            ref int k = ref blocks.bitk;    // bits in bit buffer
            ref int q = ref blocks.writeAt; // output window write pointer

            int m = blocks.GetBytesToEnd(); // bytes to end of window or read pointer

            // initialize masks
            int[] inflateMask = InflateConstants.InflateMask;
            ml = inflateMask[bl];
            md = inflateMask[bd];
            message = null;

            try
            {
                // do until not enough input or output space for fast loop
                do
                {
                    // assume called with m >= 258 && n >= 10
                    // get literal/length code
                    while (k < 20)
                    {
                        // max bits for literal/length code
                        n--;
                        b |= (input[consumed++] & 0xff) << k;
                        k += 8;
                    }

                    t = b & ml;
                    tp = tl;
                    tp_index = tl_index;
                    tp_index_t_3 = (tp_index + t) * 3;
                    if ((e = tp[tp_index_t_3]) == 0)
                    {
                        b >>= tp[tp_index_t_3 + 1];
                        k -= tp[tp_index_t_3 + 1];

                        window[q++] = (byte)tp[tp_index_t_3 + 2];
                        m--;
                        continue;
                    }
                    do
                    {

                        b >>= tp[tp_index_t_3 + 1];
                        k -= tp[tp_index_t_3 + 1];

                        if ((e & 16) != 0)
                        {
                            e &= 15;
                            c = tp[tp_index_t_3 + 2] + (b & inflateMask[e]);

                            b >>= e;
                            k -= e;

                            // decode distance base of block to copy
                            while (k < 15)
                            {
                                // max bits for distance code
                                n--;
                                b |= (input[consumed++] & 0xff) << k;
                                k += 8;
                            }

                            t = b & md;
                            tp = td;
                            tp_index = td_index;
                            tp_index_t_3 = (tp_index + t) * 3;
                            e = tp[tp_index_t_3];

                            do
                            {

                                b >>= tp[tp_index_t_3 + 1];
                                k -= tp[tp_index_t_3 + 1];

                                if ((e & 16) != 0)
                                {
                                    // get extra bits to add to distance base
                                    e &= 15;
                                    while (k < e)
                                    {
                                        // get extra bits (up to 13)
                                        n--;
                                        b |= (input[consumed++] & 0xff) << k;
                                        k += 8;
                                    }

                                    d = tp[tp_index_t_3 + 2] + (b & inflateMask[e]);

                                    b >>= e;
                                    k -= e;

                                    // do the copy
                                    m -= c;
                                    if (q >= d)
                                    {
                                        // offset before dest
                                        //  just copy
                                        r = q - d;
                                        if (q - r > 0 && 2 > (q - r))
                                        {
                                            window[q++] = window[r++]; // minimum count is three,
                                            window[q++] = window[r++]; // so unroll loop a little
                                            c -= 2;
                                        }
                                        else
                                        {
                                            Array.Copy(window, r, window, q, 2);
                                            q += 2;
                                            r += 2;
                                            c -= 2;
                                        }
                                    }
                                    else
                                    {
                                        // else offset after destination
                                        r = q - d;
                                        do
                                        {
                                            r += windowLength; // force pointer in window
                                        }
                                        while (r < 0); // covers invalid distances
                                        e = windowLength - r;
                                        if (c > e)
                                        {
                                            // if source crosses,
                                            c -= e; // wrapped copy
                                            if (q - r > 0 && e > (q - r))
                                            {
                                                do
                                                {
                                                    window[q++] = window[r++];
                                                }
                                                while (--e != 0);
                                            }
                                            else
                                            {
                                                Array.Copy(window, r, window, q, e);
                                                q += e;
                                            }
                                            r = 0; // copy rest from start of window
                                        }
                                    }

                                    // copy all or what's left
                                    if (q - r > 0 && c > (q - r))
                                    {
                                        do
                                        {
                                            window[q++] = window[r++];
                                        }
                                        while (--c != 0);
                                    }
                                    else
                                    {
                                        Array.Copy(window, r, window, q, c);
                                        q += c;
                                    }
                                    break;
                                }
                                else if ((e & 64) == 0)
                                {
                                    t += tp[tp_index_t_3 + 2];
                                    t += b & inflateMask[e];
                                    tp_index_t_3 = (tp_index + t) * 3;
                                    e = tp[tp_index_t_3];
                                }
                                else
                                {
                                    message = "invalid distance code";

                                    c = AvailableBytesIn - n;
                                    c = (k >> 3) < c ? k >> 3 : c;
                                    n += c;
                                    consumed -= c;
                                    k -= c << 3;

                                    return ZlibCode.DataError;
                                }
                            }
                            while (true);
                            break;
                        }

                        if ((e & 64) == 0)
                        {
                            t += tp[tp_index_t_3 + 2];
                            t += b & inflateMask[e];
                            tp_index_t_3 = (tp_index + t) * 3;
                            if ((e = tp[tp_index_t_3]) == 0)
                            {
                                b >>= tp[tp_index_t_3 + 1];
                                k -= tp[tp_index_t_3 + 1];
                                window[q++] = (byte)tp[tp_index_t_3 + 2];
                                m--;
                                break;
                            }
                        }
                        else if ((e & 32) != 0)
                        {
                            c = AvailableBytesIn - n;
                            c = (k >> 3) < c ? k >> 3 : c;
                            n += c;
                            consumed -= c;
                            k -= c << 3;

                            return ZlibCode.StreamEnd;
                        }
                        else
                        {
                            message = "invalid literal/length code";

                            c = AvailableBytesIn - n;
                            c = (k >> 3) < c ? k >> 3 : c;
                            n += c;
                            consumed -= c;
                            k -= c << 3;

                            return ZlibCode.DataError;
                        }
                    }
                    while (true);
                }
                while (m >= 258 && n >= 10);

                // not enough input or output; restore pointers and return
                c = AvailableBytesIn - n;
                c = (k >> 3) < c ? k >> 3 : c;
                n += c;
                consumed -= c;
                k -= c << 3;

                return ZlibCode.Ok;
            }
            finally
            {
                AvailableBytesIn = n;
            }
        }
    }
}