// See the LICENSE file for license details.

using System;
using System.Runtime.CompilerServices;

namespace Ionic.Zlib
{
    using Consts = ZlibConstants;

    public sealed class Deflater
    {
        public struct BitBuf
        {
            /// <summary>
            /// Output buffer. bits are inserted starting at the bottom (least significant bits).
            /// </summary>
            public short buf;

            /// <summary>
            /// Number of valid bits in bi_buf.  
            /// All bits above the last valid bit are always zero.
            /// </summary>
            public int valid;
        }

        public delegate DeflateBlockState CompressFunc(
            ZlibFlushType flush, ref ReadOnlySpan<byte> input, ref Span<byte> output,
            ref int consumed, ref int written);

        private CompressFunc DeflateFunction;

        private static readonly string[] _ErrorMessage = new string[]
        {
            "need dictionary",
            "stream end",
            "",
            "file error",
            "stream error",
            "data error",
            "insufficient memory",
            "buffer error",
            "incompatible version",
            ""
        };

        // preset dictionary flag in zlib header
        private const int PRESET_DICT = 0x20;

        private const int INIT_STATE = 42;
        private const int BUSY_STATE = 113;
        private const int FINISH_STATE = 666;

        // The deflate compression method
        private const int Z_DEFLATED = 8;

        private const int STORED_BLOCK = 0;
        private const int STATIC_TREES = 1;
        private const int DYN_TREES = 2;

        // The three kinds of block type
        private const int Z_BINARY = 0;
        private const int Z_ASCII = 1;
        private const int Z_UNKNOWN = 2;

        private const int Buf_size = 8 * 2;

        private const int MIN_MATCH = 3;
        private const int MAX_MATCH = 258;

        private const int MIN_LOOKAHEAD = MAX_MATCH + MIN_MATCH + 1;

        private const int HEAP_SIZE = 2 * DeflateConstants.L_CODES + 1;

        private const int END_BLOCK = 256;

        internal int status;       // as the name implies
        internal byte[] _pending;   // output still pending - waiting to be compressed
        internal int nextPending;  // index of next pending byte to output to the stream
        internal int pendingCount; // number of bytes in the pending buffer
        internal uint _adler32;

        internal sbyte data_type; // UNKNOWN, BINARY or ASCII
        internal ZlibFlushType last_flush; // value of flush param for previous deflate call

        internal int w_size; // LZ77 window size (32K by default)
        internal int w_bits; // log2(w_size)  (8..16)
        internal int _w_mask; // w_size - 1

        // Sliding window. Input bytes are read into the second half of the window,
        // and move to the first half later to keep a dictionary of at least wSize
        // bytes. With this organization, matches are limited to a distance of
        // wSize-MAX_MATCH bytes, but this ensures that IO is always
        // performed with a length multiple of the block size.
        //
        // To do: use the user input buffer as sliding window.
        internal byte[] _window;

        // Actual size of window: 2*wSize, except when the user input buffer
        // is directly used as sliding window.
        internal int window_size;

        // Link to older string with same hash index. To limit the size of this
        // array to 64K, this link is maintained only for the last 32K strings.
        // An index in this array is thus a window index modulo 32K. 
        internal ushort[] _prev;

        internal ushort[] _head;  // Heads of the hash chains or NIL.

        internal int ins_h;     // hash index of string to be inserted
        internal int hash_size; // number of elements in hash table
        internal int hash_bits; // log2(hash_size)
        internal int hash_mask; // hash_size-1

        // Number of bits by which ins_h must be shifted at each input
        // step. It must be such that after MIN_MATCH steps, the oldest
        // byte no longer takes part in the hash key, that is:
        // hash_shift * MIN_MATCH >= hash_bits
        internal int hash_shift;

        // Window position at the beginning of the current output block. Gets
        // negative when the window is moved backwards.
        internal int block_start;

        private DeflaterConfig _config;
        internal int match_length;    // length of best match
        internal int prev_match;      // previous match
        internal int match_available; // set if previous match exists
        internal int strstart;        // start of string to insert into.....????
        internal int match_start;     // start of matching string
        internal int lookahead;       // number of valid bytes ahead in window

        // Length of the best match at previous step. Matches not greater than this
        // are discarded. This is used in the lazy match evaluation.
        internal int prev_length;

        // Insert new strings in the hash table only if the match length is not
        // greater than this length. This saves time but degrades compression.
        // max_insert_length is used only for compression levels <= 3.

        internal CompressionLevel _compressionLevel; // compression level (1..9)
        internal CompressionStrategy _compressionStrategy; // favor or force Huffman coding


        internal short[] dyn_ltree; // literal and length tree
        internal short[] dyn_dtree; // distance tree
        internal short[] bl_tree;   // Huffman tree for bit lengths

        internal DeflateTree treeLiterals = new DeflateTree();   // desc for literal tree
        internal DeflateTree treeDistances = new DeflateTree();  // desc for distance tree
        internal DeflateTree treeBitLengths = new DeflateTree(); // desc for bit length tree

        // number of codes at each bit length for an optimal tree
        internal short[] bl_count = new short[DeflateConstants.MAX_BITS + 1];

        // heap used to build the Huffman trees
        internal int[] heap = new int[2 * DeflateConstants.L_CODES + 1];

        internal int heap_len; // number of elements in the heap
        internal int heap_max; // element of largest frequency

        // The sons of heap[n] are heap[2*n] and heap[2*n+1]. heap[0] is not used.
        // The same heap array is used to build all trees.

        // Depth of each subtree used as tie breaker for trees of equal frequency
        internal sbyte[] depth = new sbyte[2 * DeflateConstants.L_CODES + 1];

        internal int _lengthOffset; // index for literals or lengths

        // Size of match buffer for literals/lengths.  There are 4 reasons for
        // limiting lit_bufsize to 64K:
        //   - frequencies can be kept in 16 bit counters
        //   - if compression is not successful for the first block, all input
        //     data is still in the window so we can still emit a stored block even
        //     when input comes from standard input.  (This can also be done for
        //     all blocks if lit_bufsize is not greater than 32K.)
        //   - if compression is not successful for a file smaller than 64K, we can
        //     even emit a stored file instead of a stored block (saving 5 bytes).
        //     This is applicable only for zip (not gzip or zlib).
        //   - creating new Huffman trees less frequently may not provide fast
        //     adaptation to changes in the input data statistics. (Take for
        //     example a binary file with poorly compressible code followed by
        //     a highly compressible string table.) Smaller buffer sizes give
        //     fast adaptation but have of course the overhead of transmitting
        //     trees more frequently.

        internal int lit_bufsize;

        internal int last_lit; // running index in l_buf

        // Buffer for distances. To simplify the code, d_buf and l_buf have
        // the same number of elements. To use different lengths, an extra flag
        // array would be necessary.

        internal int _distanceOffset; // index into pending; points to distance data??

        internal int opt_len;      // bit length of current block with optimal trees
        internal int static_len;   // bit length of current block with static trees
        internal int matches;      // number of string matches in current block
        internal int last_eob_len; // bit length of EOB code for last block

        internal BitBuf _bi;

        private bool Rfc1950BytesEmitted;

        public bool Rfc1950Compliant { get; private set; }
        public int AdlerChecksum => (int)_adler32;

        public Deflater()
        {
            dyn_ltree = new short[HEAP_SIZE * 2];
            dyn_dtree = new short[(2 * DeflateConstants.D_CODES + 1) * 2]; // distance tree
            bl_tree = new short[(2 * DeflateConstants.BL_CODES + 1) * 2]; // Huffman tree for bit lengths
        }

        /// <summary>
        /// Read data from the given input and write it to output
        /// while updating the Adler32 checksum.
        /// </summary>
        internal int ReadFrom(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (input.Length > output.Length)
                input = input.Slice(0, output.Length);

            if (input.Length == 0)
                return 0;

            if (Rfc1950Compliant)
                _adler32 = Adler32.Compute(_adler32, input);

            input.CopyTo(output);

            return input.Length;
        }


        private void InitializeLazyMatch()
        {
            window_size = 2 * w_size;

            // clear the hash - workitem 9063
            Array.Clear(_head, 0, hash_size);
            //for (int i = 0; i < hash_size; i++) head[i] = 0;

            _config = DeflaterConfig.Lookup(_compressionLevel);
            SetDeflateFunction();

            strstart = 0;
            block_start = 0;
            lookahead = 0;
            match_length = prev_length = MIN_MATCH - 1;
            match_available = 0;
            ins_h = 0;
        }

        /// <summary>
        /// Initialize the tree data structures for a new zlib stream.
        /// </summary>
        private void InitializeTreeData()
        {
            treeLiterals.dyn_tree = dyn_ltree;
            treeLiterals.staticTree = StaticTree.Literals;

            treeDistances.dyn_tree = dyn_dtree;
            treeDistances.staticTree = StaticTree.Distances;

            treeBitLengths.dyn_tree = bl_tree;
            treeBitLengths.staticTree = StaticTree.BitLengths;

            _bi = default;
            last_eob_len = 8; // enough lookahead for inflate

            // Initialize the first block of the first file:
            InitializeBlocks();
        }

        internal void InitializeBlocks()
        {
            // Initialize the trees.
            for (int i = 0; i < DeflateConstants.L_CODES; i++)
                dyn_ltree[i * 2] = 0;
            for (int i = 0; i < DeflateConstants.D_CODES; i++)
                dyn_dtree[i * 2] = 0;
            for (int i = 0; i < DeflateConstants.BL_CODES; i++)
                bl_tree[i * 2] = 0;

            dyn_ltree[END_BLOCK * 2] = 1;
            opt_len = static_len = 0;
            last_lit = matches = 0;
        }

        /// <summary>
        /// Restore the heap property by moving down the tree starting at node k,
        /// exchanging a node with the smallest of its two sons if necessary, stopping
        /// when the heap property is re-established (each father smaller than its
        /// two sons).
        /// </summary>
        internal void Pqdownheap(short[] tree, int k)
        {
            int v = heap[k];
            int j = k << 1; // left son of k
            while (j <= heap_len)
            {
                // Set j to the smallest of the two sons:
                if (j < heap_len && IsSmaller(tree, heap[j + 1], heap[j], depth))
                {
                    j++;
                }
                // Exit if v is smaller than both sons
                if (IsSmaller(tree, v, heap[j], depth))
                    break;

                // Exchange v with the smallest son
                heap[k] = heap[j];
                k = j;
                // And continue down the tree, setting j to the left son of k
                j <<= 1;
            }
            heap[k] = v;
        }

        internal static bool IsSmaller(short[] tree, int n, int m, sbyte[] depth)
        {
            short tn2 = tree[n * 2];
            short tm2 = tree[m * 2];
            return tn2 < tm2 || (tn2 == tm2 && depth[n] <= depth[m]);
        }


        /// <summary>
        /// Scan a literal or distance tree to determine the frequencies of the codes
        /// in the bit length tree.
        /// </summary>
        internal void ScanTree(short[] tree, int max_code)
        {
            int n; // iterates over all tree elements
            int prevlen = -1; // last emitted length
            int curlen; // length of current code
            int nextlen = tree[0 * 2 + 1]; // length of next code
            int count = 0; // repeat count of the current code
            int max_count = 7; // max repeat count
            int min_count = 4; // min repeat count

            if (nextlen == 0)
            {
                max_count = 138;
                min_count = 3;
            }
            tree[(max_code + 1) * 2 + 1] = 0x7fff; // guard //??

            for (n = 0; n <= max_code; n++)
            {
                curlen = nextlen;
                nextlen = tree[(n + 1) * 2 + 1];
                if (++count < max_count && curlen == nextlen)
                {
                    continue;
                }
                else if (count < min_count)
                {
                    bl_tree[curlen * 2] = (short)(bl_tree[curlen * 2] + count);
                }
                else if (curlen != 0)
                {
                    if (curlen != prevlen)
                        bl_tree[curlen * 2]++;
                    bl_tree[DeflateConstants.REP_3_6 * 2]++;
                }
                else if (count <= 10)
                {
                    bl_tree[DeflateConstants.REPZ_3_10 * 2]++;
                }
                else
                {
                    bl_tree[DeflateConstants.REPZ_11_138 * 2]++;
                }
                count = 0;
                prevlen = curlen;
                if (nextlen == 0)
                {
                    max_count = 138;
                    min_count = 3;
                }
                else if (curlen == nextlen)
                {
                    max_count = 6;
                    min_count = 3;
                }
                else
                {
                    max_count = 7;
                    min_count = 4;
                }
            }
        }

        // Construct the Huffman tree for the bit lengths and return the index in
        // bl_order of the last bit length code to send.
        internal int BuildBlTree()
        {
            int max_blindex; // index of last bit length code of non zero freq

            // Determine the bit length frequencies for literal and distance trees
            ScanTree(dyn_ltree, treeLiterals.max_code);
            ScanTree(dyn_dtree, treeDistances.max_code);

            // Build the bit length tree:
            treeBitLengths.BuildTree(this);
            // opt_len now includes the length of the tree representations, except
            // the lengths of the bit lengths codes and the 5+5+4 bits for the counts.

            // Determine the number of bit length codes to send. The pkzip format
            // requires that at least 4 bit length codes be sent. (appnote.txt says
            // 3 but the actual value used is 4.)
            var blOrder = DeflateTree.BlOrder;
            for (max_blindex = DeflateConstants.BL_CODES - 1; max_blindex >= 3; max_blindex--)
            {
                if (bl_tree[blOrder[max_blindex] * 2 + 1] != 0)
                    break;
            }
            // Update opt_len to include the bit length tree and counts
            opt_len += 3 * (max_blindex + 1) + 5 + 5 + 4;

            return max_blindex;
        }


        /// <summary>
        /// Send the header for a block using dynamic Huffman trees: the counts, the
        /// lengths of the bit length codes, the literal tree and the distance tree.
        /// <para>
        /// IN assertion: lcodes >= 257, dcodes >= 1, blcodes >= 4.
        /// </para>
        /// </summary>
        internal void SendAllTrees(int lcodes, int dcodes, int blcodes, ref BitBuf bi)
        {
            SendBits(lcodes - 257, 5, ref bi); // not +255 as stated in appnote.txt
            SendBits(dcodes - 1, 5, ref bi);
            SendBits(blcodes - 4, 4, ref bi); // not -3 as stated in appnote.txt

            var blOrder = DeflateTree.BlOrder.Slice(0, blcodes);
            for (int rank = 0; rank < blOrder.Length; rank++)
                SendBits(bl_tree[blOrder[rank] * 2 + 1], 3, ref bi);

            SendTree(dyn_ltree, lcodes - 1, ref bi); // literal tree
            SendTree(dyn_dtree, dcodes - 1, ref bi); // distance tree
        }

        /// <summary>
        /// Send a literal or distance tree in compressed form, using the codes in bl_tree.
        /// </summary>
        internal void SendTree(short[] tree, int max_code, ref BitBuf bi)
        {
            int n;                           // iterates over all tree elements
            int prevlen = -1;              // last emitted length
            int curlen;                      // length of current code
            int nextlen = tree[0 * 2 + 1]; // length of next code
            int count = 0;               // repeat count of the current code
            int max_count = 7;               // max repeat count
            int min_count = 4;               // min repeat count

            if (nextlen == 0)
            {
                max_count = 138;
                min_count = 3;
            }

            for (n = 0; n <= max_code; n++)
            {
                curlen = nextlen;
                nextlen = tree[(n + 1) * 2 + 1];
                if (++count < max_count && curlen == nextlen)
                {
                    continue;
                }
                else if (count < min_count)
                {
                    do
                    {
                        SendCode(curlen, bl_tree, ref bi);
                    }
                    while (--count != 0);
                }
                else if (curlen != 0)
                {
                    if (curlen != prevlen)
                    {
                        SendCode(curlen, bl_tree, ref bi);
                        count--;
                    }
                    SendCode(DeflateConstants.REP_3_6, bl_tree, ref bi);
                    SendBits(count - 3, 2, ref bi);
                }
                else if (count <= 10)
                {
                    SendCode(DeflateConstants.REPZ_3_10, bl_tree, ref bi);
                    SendBits(count - 3, 3, ref bi);
                }
                else
                {
                    SendCode(DeflateConstants.REPZ_11_138, bl_tree, ref bi);
                    SendBits(count - 11, 7, ref bi);
                }
                count = 0;
                prevlen = curlen;
                if (nextlen == 0)
                {
                    max_count = 138;
                    min_count = 3;
                }
                else if (curlen == nextlen)
                {
                    max_count = 6;
                    min_count = 3;
                }
                else
                {
                    max_count = 7;
                    min_count = 4;
                }
            }
        }

        /// <summary>
        /// Output a block of bytes on the stream.
        /// IN assertion: there is enough room in pending_buf.
        /// </summary>
        private void PutBytes(byte[] p, int start, int len)
        {
            Array.Copy(p, start, _pending, pendingCount, len);
            pendingCount += len;
        }

        // TODO: make these static

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SendCode(int c, short[] tree, ref BitBuf bi)
        {
            int c2 = c * 2;
            SendBits(tree[c2], tree[c2 + 1], ref bi);
        }

        internal void SendBits(int value, int length, ref BitBuf bi)
        {
            if (bi.valid > Buf_size - length)
            {
                bi.buf |= (short)(value << bi.valid);
                //put_short(bi_buf);
                _pending[pendingCount++] = (byte)bi.buf;
                _pending[pendingCount++] = (byte)(bi.buf >> 8);

                bi.buf = (short)((uint)value >> (Buf_size - bi.valid));
                bi.valid += length - Buf_size;
            }
            else
            {
                bi.buf |= (short)(value << bi.valid);
                bi.valid += length;
            }
        }

        /// <summary>
        /// Send one empty static block to give enough lookahead for inflate.
        /// This takes 10 bits, of which 7 may remain in the bit buffer.
        /// The current inflate code requires 9 bits of lookahead. If the
        /// last two codes for the previous block (real code plus EOB) were coded
        /// on 5 bits or less, inflate may have only 5+3 bits of lookahead to decode
        /// the last real code. In this case we send two empty static blocks instead
        /// of one. (There are no problems if the previous block is stored or fixed.)
        /// To simplify the code, we assume the worst case of last real code encoded
        /// on one bit only.
        /// </summary>
        internal void TrAlign(ref BitBuf bi)
        {
            SendBits(STATIC_TREES << 1, 3, ref bi);
            SendCode(END_BLOCK, StaticTree.LengthAndLiteralsTreeCodes, ref bi);

            BitFlush(ref bi);

            // Of the 10 bits for the empty block, we have already sent
            // (10 - bi_valid) bits. The lookahead for the last real code (before
            // the EOB of the previous block) was thus at least one plus the length
            // of the EOB plus what we have just sent of the empty static block.
            if (1 + last_eob_len + 10 - bi.valid < 9)
            {
                SendBits(STATIC_TREES << 1, 3, ref bi);
                SendCode(END_BLOCK, StaticTree.LengthAndLiteralsTreeCodes, ref bi);
                BitFlush(ref bi);
            }
            last_eob_len = 7;
        }

        /// <summary>
        /// Save the match info and tally the frequency counts. Return true if
        /// the current block must be flushed.
        /// </summary>
        internal bool TrTally(int dist, int lc)
        {
            _pending[_distanceOffset + last_lit * 2] = (byte)(((uint)dist >> 8) & 0xff);
            _pending[_distanceOffset + last_lit * 2 + 1] = (byte)(dist & 0xff);
            _pending[_lengthOffset + last_lit] = (byte)(lc & 0xff);

            last_lit++;

            if (dist == 0)
            {
                // lc is the unmatched char
                dyn_ltree[lc * 2]++;
            }
            else
            {
                matches++;
                // Here, lc is the match length - MIN_MATCH
                dist--; // dist = match distance - 1
                dyn_ltree[(DeflateTree.LengthCode[lc] + DeflateConstants.LITERALS + 1) * 2]++;
                dyn_dtree[DeflateTree.DistanceCode(dist) * 2]++;
            }

            if ((last_lit & 0x1fff) == 0 && (int)_compressionLevel > 2)
            {
                // Compute an upper bound for the compressed length
                int out_length = last_lit << 3;
                int in_length = strstart - block_start;

                var extraDistanceBits = DeflateTree.ExtraDistanceBits.AsSpan(0, DeflateConstants.D_CODES);
                var dyndtree = dyn_dtree.AsSpan(0, DeflateConstants.D_CODES * 2);
                for (int dcode = 0; dcode < DeflateConstants.D_CODES; dcode++)
                    out_length = (int)(out_length + dyndtree[dcode * 2] * (5L + extraDistanceBits[dcode]));

                out_length >>= 3;
                if ((matches < (last_lit / 2)) && out_length < in_length / 2)
                    return true;
            }

            return (last_lit == lit_bufsize - 1) || (last_lit == lit_bufsize);
            // dinoch - wraparound?
            // We avoid equality with lit_bufsize because of wraparound at 64K
            // on 16 bit machines and because stored blocks are restricted to
            // 64K-1 bytes.
        }


        /// <summary>
        /// Send the block data compressed using the given Huffman trees
        /// </summary>
        internal void SendCompressedBlock(short[] ltree, short[] dtree, ref BitBuf bi)
        {
            if (last_lit != 0)
            {
                int distance; // distance of matched string
                int lc;       // match length or unmatched char (if dist == 0)
                int lx = 0;   // running index in l_buf
                int code;     // the code to send
                int extra;    // number of extra bits to send

                var lengthBase = DeflateTree.LengthBase;
                var lengthCode = DeflateTree.LengthCode;
                var distanceBase = DeflateTree.DistanceBase;
                var distCode = DeflateTree.DistCode;
                var extraLengthBits = DeflateTree.ExtraLengthBits;
                var extraDistanceBits = DeflateTree.ExtraDistanceBits;

                do
                {
                    int ix = _distanceOffset + lx * 2;
                    distance = (_pending[ix] << 8) | _pending[ix + 1];
                    lc = _pending[_lengthOffset + lx];
                    lx++;

                    if (distance == 0)
                    {
                        SendCode(lc, ltree, ref bi); // send a literal byte
                    }
                    else
                    {
                        // literal or match pair
                        // Here, lc is the match length - MIN_MATCH
                        code = lengthCode[lc];

                        // send the length code
                        SendCode(code + DeflateConstants.LITERALS + 1, ltree, ref bi);
                        extra = extraLengthBits[code];
                        if (extra != 0)
                        {
                            // send the extra length bits
                            lc -= lengthBase[code];
                            SendBits(lc, extra, ref bi);
                        }
                        distance--; // dist is now the match distance - 1
                        code = DeflateTree.DistanceCode(distCode, distance);

                        // send the distance code
                        SendCode(code, dtree, ref bi);

                        extra = extraDistanceBits[code];
                        if (extra != 0)
                        {
                            // send the extra distance bits
                            distance -= distanceBase[code];
                            SendBits(distance, extra, ref bi);
                        }
                    }

                    // Check that the overlay between pending and d_buf+l_buf is ok:
                }
                while (lx < last_lit);
            }

            SendCode(END_BLOCK, ltree, ref bi);
            last_eob_len = ltree[END_BLOCK * 2 + 1];
        }


        /// <summary>
        /// Set the data type to ASCII or BINARY, using a crude approximation:
        /// binary if more than 20% of the bytes are <= 6 or >= 128, ascii otherwise.
        /// IN assertion: the fields freq of dyn_ltree are set and the total of all
        /// frequencies does not exceed 64K (to fit in an int on 16 bit machines).
        /// </summary>
        internal void SetDataType()
        {
            int n = 0;
            int ascii_freq = 0;
            int bin_freq = 0;
            while (n < 7)
            {
                bin_freq += dyn_ltree[n * 2];
                n++;
            }
            while (n < 128)
            {
                ascii_freq += dyn_ltree[n * 2];
                n++;
            }
            while (n < DeflateConstants.LITERALS)
            {
                bin_freq += dyn_ltree[n * 2];
                n++;
            }
            data_type = (sbyte)(bin_freq > (ascii_freq >> 2) ? Z_BINARY : Z_ASCII);
        }


        /// <summary>
        /// Flush the bit buffer, keeping at most 7 bits in it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void BitFlush(ref BitBuf bi)
        {
            if (bi.valid == 16)
            {
                _pending[pendingCount++] = (byte)bi.buf;
                _pending[pendingCount++] = (byte)(bi.buf >> 8);
                bi = default;
            }
            else if (bi.valid >= 8)
            {
                _pending[pendingCount++] = (byte)bi.buf;
                bi.buf >>= 8;
                bi.valid -= 8;
            }
        }

        /// <summary>
        /// Flush the bit buffer and align the output on a byte boundary
        /// </summary>
        internal void BitWindup(ref BitBuf bi)
        {
            if (bi.valid > 8)
            {
                _pending[pendingCount++] = (byte)bi.buf;
                _pending[pendingCount++] = (byte)(bi.buf >> 8);
            }
            else if (bi.valid > 0)
            {
                _pending[pendingCount++] = (byte)bi.buf;
            }
            bi = default;
        }

        /// <summary>
        /// Copy a stored block, storing first the length and its
        /// one's complement if requested.
        /// </summary>
        internal void CopyBlock(int buf, int len, bool header, ref BitBuf bi)
        {
            BitWindup(ref bi); // align on byte boundary
            last_eob_len = 8; // enough lookahead for inflate

            if (header)
            {
                _pending[pendingCount++] = (byte)len;
                _pending[pendingCount++] = (byte)(len >> 8);

                int flen = ~len;
                _pending[pendingCount++] = (byte)flen;
                _pending[pendingCount++] = (byte)(flen >> 8);
            }
            PutBytes(_window, buf, len);
        }

        internal void FlushBlockOnly(
            bool eof, ref Span<byte> output, ref int written, ref BitBuf bi)
        {
            TrFlushBlock(block_start >= 0 ? block_start : -1, strstart - block_start, eof, ref bi);

            block_start = strstart;
            FlushPending(ref output, ref written);
        }

        /// <summary>
        /// Copy without compression as much as possible from the input stream, return
        /// the current block state.
        /// This function does not insert new strings in the dictionary since
        /// uncompressible data is probably not useful. This function is used
        /// only for the level=0 compression option.
        /// </summary>
        internal DeflateBlockState DeflateNone(
            ZlibFlushType flush, ref ReadOnlySpan<byte> input, ref Span<byte> output,
            ref int consumed, ref int written)
        {
            // NOTE: this function should be optimized to avoid extra copying from
            // window to pending_buf.

            // Stored blocks are limited to 0xffff bytes, pending is limited
            // to pending_buf_size, and each stored block has a 5 byte header:

            BitBuf bi = _bi;

            int max_block_size = 0xffff;
            int max_start;

            if (max_block_size > _pending.Length - 5)
                max_block_size = _pending.Length - 5;

            // Copy as much as possible from input to output:
            while (true)
            {
                // Fill the window as much as possible:
                if (lookahead <= 1)
                {
                    FillWindow(ref input, ref consumed);

                    if (lookahead == 0 && flush == ZlibFlushType.None)
                    {
                        _bi = bi;
                        return DeflateBlockState.NeedMore;
                    }

                    if (lookahead == 0)
                        break; // flush the current block
                }

                strstart += lookahead;
                lookahead = 0;

                // Emit a stored block if pending will be full:
                max_start = block_start + max_block_size;
                if (strstart == 0 || strstart >= max_start)
                {
                    // strstart == 0 is possible when wraparound on 16-bit machine
                    lookahead = strstart - max_start;
                    strstart = max_start;

                    FlushBlockOnly(false, ref output, ref written, ref bi);

                    if (output.Length == 0)
                    {
                        _bi = bi;
                        return DeflateBlockState.NeedMore;
                    }
                }

                // Flush if we may have to slide, otherwise block_start may become
                // negative and the data will be gone:
                if (strstart - block_start >= w_size - MIN_LOOKAHEAD)
                {
                    FlushBlockOnly(false, ref output, ref written, ref bi);

                    if (output.Length == 0)
                    {
                        _bi = bi;
                        return DeflateBlockState.NeedMore;
                    }
                }
            }

            FlushBlockOnly(flush == ZlibFlushType.Finish, ref output, ref written, ref bi);
            _bi = bi;

            if (output.Length == 0)
            {
                return (flush == ZlibFlushType.Finish)
                    ? DeflateBlockState.FinishStarted
                    : DeflateBlockState.NeedMore;
            }
            return flush == ZlibFlushType.Finish ? DeflateBlockState.FinishDone : DeflateBlockState.BlockDone;
        }

        /// <summary>
        /// Send a stored block
        /// </summary>
        internal void TrStoredBlock(int buf, int stored_len, bool eof, ref BitBuf bi)
        {
            SendBits((STORED_BLOCK << 1) + (eof ? 1 : 0), 3, ref bi); // send block type
            CopyBlock(buf, stored_len, true, ref bi); // with header
        }

        /// <summary>
        /// Determine the best encoding for the current block: dynamic trees, static
        /// trees or store, and output the encoded block to the zip file.
        /// </summary>
        internal void TrFlushBlock(int buf, int stored_len, bool eof, ref BitBuf bi)
        {
            int opt_lenb, static_lenb; // opt_len and static_len in bytes
            int max_blindex = 0; // index of last bit length code of non zero freq

            // Build the Huffman trees unless a stored block is forced
            if (_compressionLevel > 0)
            {
                // Check if the file is ascii or binary
                if (data_type == Z_UNKNOWN)
                    SetDataType();

                // Construct the literal and distance trees
                treeLiterals.BuildTree(this);

                treeDistances.BuildTree(this);

                // At this point, opt_len and static_len are the total bit lengths of
                // the compressed block data, excluding the tree representations.

                // Build the bit length tree for the above two trees, and get the index
                // in bl_order of the last bit length code to send.
                max_blindex = BuildBlTree();

                // Determine the best encoding. Compute first the block length in bytes
                opt_lenb = (opt_len + 3 + 7) >> 3;
                static_lenb = (static_len + 3 + 7) >> 3;

                if (static_lenb <= opt_lenb)
                    opt_lenb = static_lenb;
            }
            else
            {
                opt_lenb = static_lenb = stored_len + 5; // force a stored block
            }

            if (stored_len + 4 <= opt_lenb && buf != -1)
            {
                // 4: two words for the lengths
                // The test buf != NULL is only necessary if LIT_BUFSIZE > WSIZE.
                // Otherwise we can't have processed more than WSIZE input bytes since
                // the last block flush, because compression would have been
                // successful. If LIT_BUFSIZE <= WSIZE, it is never too late to
                // transform a block into a stored block.
                TrStoredBlock(buf, stored_len, eof, ref bi);
            }
            else if (static_lenb == opt_lenb)
            {
                SendBits((STATIC_TREES << 1) + (eof ? 1 : 0), 3, ref bi);
                SendCompressedBlock(
                    StaticTree.LengthAndLiteralsTreeCodes, StaticTree.DistTreeCodes, ref bi);
            }
            else
            {
                SendBits((DYN_TREES << 1) + (eof ? 1 : 0), 3, ref bi);
                SendAllTrees(
                    treeLiterals.max_code + 1, treeDistances.max_code + 1, max_blindex + 1, ref bi);
                SendCompressedBlock(dyn_ltree, dyn_dtree, ref bi);
            }

            // The above check is made mod 2^32, for files larger than 512 MB
            // and uLong implemented on 32 bits.

            InitializeBlocks();

            if (eof)
                BitWindup(ref bi);
        }

        // Fill the window when the lookahead becomes insufficient.
        // Updates strstart and lookahead.
        //
        // IN assertion: lookahead < MIN_LOOKAHEAD
        // OUT assertions: strstart <= window_size-MIN_LOOKAHEAD
        //    At least one byte has been read, or avail_in == 0; reads are
        //    performed for at least two bytes (required for the zip translate_eol
        //    option -- not supported here).
        private void FillWindow(ref ReadOnlySpan<byte> input, ref int consumed)
        {
            int n;
            int p;
            int m;
            int more; // Amount of free space at the end of the window.

            byte[] window = _window;
            ushort[] prev = _prev;
            ushort[] head = _head;

            do
            {
                more = window_size - lookahead - strstart;

                // Deal with !@#$% 64K limit:
                if (more == 0 && strstart == 0 && lookahead == 0)
                {
                    more = w_size;
                }
                else if (more == -1)
                {
                    // Very unlikely, but possible on 16 bit machine if strstart == 0
                    // and lookahead == 1 (input done one byte at time)
                    more--;

                    // If the window is almost full and there is insufficient lookahead,
                    // move the upper half to the lower one to make room in the upper half.
                }
                else if (strstart >= w_size + w_size - MIN_LOOKAHEAD)
                {
                    Array.Copy(window, w_size, window, 0, w_size);
                    match_start -= w_size;
                    strstart -= w_size; // we now have strstart >= MAX_DIST
                    block_start -= w_size;

                    // Slide the hash table (could be avoided with 32 bit values
                    // at the expense of memory usage). We slide even when level == 0
                    // to keep the hash table consistent if we switch back to level > 0
                    // later. (Using level 0 permanently is not an optimal usage of
                    // zlib, so we don't care about this pathological case.)

                    n = hash_size;
                    p = n;
                    do
                    {
                        m = head[--p];
                        head[p] = (ushort)((m >= w_size) ? (m - w_size) : 0);
                    }
                    while (--n != 0);

                    n = w_size;
                    p = n;
                    do
                    {
                        m = prev[--p];
                        prev[p] = (ushort)((m >= w_size) ? (m - w_size) : 0);
                        // If n is not on any hash chain, prev[n] is garbage but
                        // its value will never be used.
                    }
                    while (--n != 0);
                    more += w_size;
                }

                if (input.Length == 0)
                    return;

                // If there was no sliding:
                //    strstart <= WSIZE+MAX_DIST-1 && lookahead <= MIN_LOOKAHEAD - 1 &&
                //    more == window_size - lookahead - strstart
                // => more >= window_size - (MIN_LOOKAHEAD-1 + WSIZE + MAX_DIST-1)
                // => more >= window_size - 2*WSIZE + 2
                // In the BIG_MEM or MMAP case (not yet supported),
                //   window_size == input_size + MIN_LOOKAHEAD  &&
                //   strstart + s->lookahead <= input_size => more >= MIN_LOOKAHEAD.
                // Otherwise, window_size == 2*WSIZE so more >= 2.
                // If there was sliding, more >= WSIZE. So in all cases, more >= 2.

                n = ReadFrom(input, window.AsSpan(strstart + lookahead, more));
                input = input.Slice(n);
                consumed += n;

                lookahead += n;

                // Initialize the hash value now that we have some input:
                if (lookahead >= MIN_MATCH)
                {
                    ins_h = window[strstart];
                    ins_h = ((ins_h << hash_shift) ^ window[strstart + 1]) & hash_mask;
                }
                // If the whole input has less than MIN_MATCH bytes, ins_h is garbage,
                // but this is not important since only literal bytes will be emitted.
            }
            while (lookahead < MIN_LOOKAHEAD && input.Length != 0);
        }

        // Compress as much as possible from the input stream, return the current
        // block state.
        // This function does not perform lazy evaluation of matches and inserts
        // new strings in the dictionary only for unmatched strings or for short
        // matches. It is used only for the fast compression options.
        internal DeflateBlockState DeflateFast(
            ZlibFlushType flush, ref ReadOnlySpan<byte> input, ref Span<byte> output,
            ref int consumed, ref int written)
        {
            ushort hash_head = 0; // head of the hash chain
            bool bflush; // set if current block must be flushed

            byte[] window = _window;
            ushort[] head = _head;
            ushort[] prev = _prev;
            int w_mask = _w_mask;

            BitBuf bi = _bi;

            while (true)
            {
                // Make sure that we always have enough lookahead, except
                // at the end of the input file. We need MAX_MATCH bytes
                // for the next match, plus MIN_MATCH bytes to insert the
                // string following the next match.
                if (lookahead < MIN_LOOKAHEAD)
                {
                    FillWindow(ref input, ref consumed);

                    if (lookahead < MIN_LOOKAHEAD && flush == ZlibFlushType.None)
                    {
                        _bi = bi;
                        return DeflateBlockState.NeedMore;
                    }

                    if (lookahead == 0)
                        break; // flush the current block
                }

                // Insert the string window[strstart .. strstart+2] in the
                // dictionary, and set hash_head to the head of the hash chain:
                if (lookahead >= MIN_MATCH)
                {
                    ins_h = ((ins_h << hash_shift) ^ window[strstart + (MIN_MATCH - 1)]) & hash_mask;

                    prev[strstart & w_mask] = hash_head = head[ins_h];
                    head[ins_h] = (ushort)strstart;
                }

                // Find the longest match, discarding those <= prev_length.
                // At this point we have always match_length < MIN_MATCH

                if (hash_head != 0L && (strstart - hash_head) <= w_size - MIN_LOOKAHEAD)
                {
                    // To simplify the code, we prevent matches with the string
                    // of window index 0 (in particular we have to avoid a match
                    // of the string with itself at the start of the input file).
                    if (_compressionStrategy != CompressionStrategy.HuffmanOnly)
                    {
                        match_length = LongestMatch(hash_head);
                    }
                    // longest_match() sets match_start
                }
                if (match_length >= MIN_MATCH)
                {
                    // check_match(strstart, match_start, match_length);

                    bflush = TrTally(strstart - match_start, match_length - MIN_MATCH);

                    lookahead -= match_length;

                    // Insert new strings in the hash table only if the match length
                    // is not too large. This saves time but degrades compression.
                    if (match_length <= _config.MaxLazy && lookahead >= MIN_MATCH)
                    {
                        match_length--; // string at strstart already in hash table
                        do
                        {
                            strstart++;

                            ins_h = ((ins_h << hash_shift) ^ window[strstart + (MIN_MATCH - 1)]) & hash_mask;

                            prev[strstart & w_mask] = hash_head = head[ins_h];
                            head[ins_h] = (ushort)strstart;

                            // strstart never exceeds WSIZE-MAX_MATCH, so there are
                            // always MIN_MATCH bytes ahead.
                        }
                        while (--match_length != 0);
                        strstart++;
                    }
                    else
                    {
                        strstart += match_length;
                        match_length = 0;

                        ins_h = window[strstart];
                        ins_h = ((ins_h << hash_shift) ^ window[strstart + 1]) & hash_mask;

                        // If lookahead < MIN_MATCH, ins_h is garbage, but it does not
                        // matter since it will be recomputed at next deflate call.
                    }
                }
                else
                {
                    // No match, output a literal byte

                    bflush = TrTally(0, window[strstart]);
                    lookahead--;
                    strstart++;
                }
                if (bflush)
                {
                    FlushBlockOnly(false, ref output, ref written, ref bi);

                    if (output.Length == 0)
                    {
                        _bi = bi;
                        return DeflateBlockState.NeedMore;
                    }
                }
            }

            FlushBlockOnly(flush == ZlibFlushType.Finish, ref output, ref written, ref bi);
            _bi = bi;

            if (output.Length == 0)
            {
                return (flush == ZlibFlushType.Finish)
                    ? DeflateBlockState.FinishStarted
                    : DeflateBlockState.NeedMore;
            }
            return flush == ZlibFlushType.Finish ? DeflateBlockState.FinishDone : DeflateBlockState.BlockDone;
        }

        // Same as above, but achieves better compression. We use a lazy
        // evaluation for matches: a match is finally adopted only if there is
        // no better match at the next window position.
        internal DeflateBlockState DeflateSlow(
            ZlibFlushType flush, ref ReadOnlySpan<byte> input, ref Span<byte> output,
            ref int consumed, ref int written)
        {
            ushort hash_head = 0; // head of hash chain
            bool bflush; // set if current block must be flushed

            byte[] window = _window;
            ushort[] head = _head;
            ushort[] prev = _prev;
            int w_mask = _w_mask;

            BitBuf bi = _bi;

            // Process the input block.
            while (true)
            {
                // Make sure that we always have enough lookahead, except
                // at the end of the input file. We need MAX_MATCH bytes
                // for the next match, plus MIN_MATCH bytes to insert the
                // string following the next match.

                if (lookahead < MIN_LOOKAHEAD)
                {
                    FillWindow(ref input, ref consumed);

                    if (lookahead < MIN_LOOKAHEAD && flush == ZlibFlushType.None)
                    {
                        _bi = bi;
                        return DeflateBlockState.NeedMore;
                    }

                    if (lookahead == 0)
                        break; // flush the current block
                }

                // Insert the string window[strstart .. strstart+2] in the
                // dictionary, and set hash_head to the head of the hash chain:

                if (lookahead >= MIN_MATCH)
                {
                    ins_h = ((ins_h << hash_shift) ^ window[strstart + (MIN_MATCH - 1)]) & hash_mask;

                    prev[strstart & w_mask] = hash_head = head[ins_h];
                    head[ins_h] = (ushort)strstart;
                }

                // Find the longest match, discarding those <= prev_length.
                prev_length = match_length;
                prev_match = match_start;
                match_length = MIN_MATCH - 1;

                if (hash_head != 0 && prev_length < _config.MaxLazy &&
                    ((strstart - hash_head)) <= w_size - MIN_LOOKAHEAD)
                {
                    // To simplify the code, we prevent matches with the string
                    // of window index 0 (in particular we have to avoid a match
                    // of the string with itself at the start of the input file).

                    if (_compressionStrategy != CompressionStrategy.HuffmanOnly)
                    {
                        match_length = LongestMatch(hash_head);
                    }
                    // longest_match() sets match_start

                    if (match_length <= 5 &&
                        (_compressionStrategy == CompressionStrategy.Filtered ||
                        (match_length == MIN_MATCH && strstart - match_start > 4096)))
                    {

                        // If prev_match is also MIN_MATCH, match_start is garbage
                        // but we will ignore the current match anyway.
                        match_length = MIN_MATCH - 1;
                    }
                }

                // If there was a match at the previous step and the current
                // match is not better, output the previous match:
                if (prev_length >= MIN_MATCH && match_length <= prev_length)
                {
                    int max_insert = strstart + lookahead - MIN_MATCH;
                    // Do not insert strings in hash table beyond this.

                    // check_match(strstart-1, prev_match, prev_length);

                    bflush = TrTally(strstart - 1 - prev_match, prev_length - MIN_MATCH);

                    // Insert in hash table all strings up to the end of the match.
                    // strstart-1 and strstart are already inserted. If there is not
                    // enough lookahead, the last two strings are not inserted in
                    // the hash table.
                    lookahead -= prev_length - 1;
                    prev_length -= 2;
                    do
                    {
                        if (++strstart <= max_insert)
                        {
                            ins_h = ((ins_h << hash_shift) ^ (window[strstart + (MIN_MATCH - 1)])) & hash_mask;

                            prev[strstart & w_mask] = hash_head = head[ins_h];
                            head[ins_h] = (ushort)strstart;
                        }
                    }
                    while (--prev_length != 0);
                    match_available = 0;
                    match_length = MIN_MATCH - 1;
                    strstart++;

                    if (bflush)
                    {
                        FlushBlockOnly(false, ref output, ref written, ref bi);

                        if (output.Length == 0)
                        {
                            _bi = bi;
                            return DeflateBlockState.NeedMore;
                        }
                    }
                }
                else if (match_available != 0)
                {
                    // If there was no match at the previous position, output a
                    // single literal. If there was a match but the current match
                    // is longer, truncate the previous match to a single literal.

                    bflush = TrTally(0, window[strstart - 1]);

                    if (bflush)
                        FlushBlockOnly(false, ref output, ref written, ref bi);

                    strstart++;
                    lookahead--;

                    if (output.Length == 0)
                    {
                        _bi = bi;
                        return DeflateBlockState.NeedMore;
                    }
                }
                else
                {
                    // There is no previous match to compare with, wait for
                    // the next step to decide.

                    match_available = 1;
                    strstart++;
                    lookahead--;
                }
            }

            if (match_available != 0)
            {
                TrTally(0, window[strstart - 1]);
                match_available = 0;
            }

            FlushBlockOnly(flush == ZlibFlushType.Finish, ref output, ref written, ref bi);
            _bi = bi;

            if (output.Length == 0)
            {
                return (flush == ZlibFlushType.Finish)
                    ? DeflateBlockState.FinishStarted
                    : DeflateBlockState.NeedMore;
            }
            return flush == ZlibFlushType.Finish ? DeflateBlockState.FinishDone : DeflateBlockState.BlockDone;
        }


        internal int LongestMatch(int cur_match)
        {
            ushort[] prev = _prev;
            byte[] window = _window;

            int chain_length = _config.MaxChainLength; // max hash chain length
            int scan = strstart;                      // current string
            int match;                                // matched string
            int len;                                  // length of current match
            int best_len = prev_length;               // best match length so far
            int limit = strstart > (w_size - MIN_LOOKAHEAD) ? strstart - (w_size - MIN_LOOKAHEAD) : 0;

            int niceLength = _config.NiceLength;

            // Stop when cur_match becomes <= limit. To simplify the code,
            // we prevent matches with the string of window index 0.

            int w_mask = _w_mask;

            int strend = strstart + MAX_MATCH;
            byte scan_end1 = window[scan + best_len - 1];
            byte scan_end = window[scan + best_len];

            // The code is optimized for HASH_BITS >= 8 and MAX_MATCH-2 multiple of 16.
            // It is easy to get rid of this optimization if necessary.

            // Do not waste too much time if we already have a good match:
            if (prev_length >= _config.GoodLength)
            {
                chain_length >>= 2;
            }

            // Do not look for matches beyond the end of the input. This is necessary
            // to make deflate deterministic.
            if (niceLength > lookahead)
                niceLength = lookahead;

            do
            {
                match = cur_match;

                // Skip to next match if the match length cannot increase
                // or if the match length is less than 2:
                if (window[match + best_len] != scan_end ||
                    window[match + best_len - 1] != scan_end1 ||
                    window[match] != window[scan] ||
                    window[++match] != window[scan + 1])
                    continue;

                // The check at best_len-1 can be removed because it will be made
                // again later. (This heuristic is not always a win.)
                // It is not necessary to compare scan[2] and match[2] since they
                // are always equal when the other bytes match, given that
                // the hash keys are equal and that HASH_BITS >= 8.
                scan += 2;
                match++;

                // We check for insufficient lookahead only every 8th comparison;
                // the 256th check will be made at strstart+258.
                while (
                    window[++scan] == window[++match] &&
                    window[++scan] == window[++match] &&
                    window[++scan] == window[++match] &&
                    window[++scan] == window[++match] &&
                    window[++scan] == window[++match] &&
                    window[++scan] == window[++match] &&
                    window[++scan] == window[++match] &&
                    window[++scan] == window[++match] &&
                    scan < strend)
                {
                }

                len = MAX_MATCH - (strend - scan);
                scan = strend - MAX_MATCH;

                if (len > best_len)
                {
                    match_start = cur_match;
                    best_len = len;
                    if (len >= niceLength)
                        break;

                    scan_end1 = window[scan + best_len - 1];
                    scan_end = window[scan + best_len];
                }
            }
            while ((cur_match = prev[cur_match & w_mask]) > limit && --chain_length != 0);

            if (best_len <= lookahead)
                return best_len;

            return lookahead;
        }

        public void Setup(
            CompressionLevel level = CompressionLevel.Default,
            int windowBits = Consts.DefaultWindowBits,
            int memoryLevel = Consts.DefaultMemoryLevel,
            CompressionStrategy strategy = CompressionStrategy.Default,
            bool rfc1950Compliant = true)
        {
            if (windowBits < Consts.MinWindowBits ||
                windowBits > Consts.MaxWindowBits)
                throw new ArgumentOutOfRangeException(
                    nameof(windowBits),
                    $"Must be in the range {Consts.MinWindowBits}..{Consts.MaxWindowBits}.");

            if (memoryLevel < Consts.MinMemoryLevel ||
                memoryLevel > Consts.MaxMemoryLevel)
                throw new ArgumentOutOfRangeException(
                    nameof(memoryLevel),
                    $"Must be in the range {Consts.MinMemoryLevel}..{Consts.MaxMemoryLevel}");

            Rfc1950Compliant = rfc1950Compliant;
            w_bits = windowBits;
            w_size = 1 << w_bits;
            _w_mask = w_size - 1;

            hash_bits = memoryLevel + 7;
            hash_size = 1 << hash_bits;
            hash_mask = hash_size - 1;
            hash_shift = (hash_bits + MIN_MATCH - 1) / MIN_MATCH;

            _window = new byte[w_size * 2];
            _prev = new ushort[w_size];
            _head = new ushort[hash_size];

            // for memLevel==8, this will be 16384, 16k
            lit_bufsize = 1 << (memoryLevel + 6);

            // Use a single array as the buffer for data pending compression,
            // the output distance codes, and the output length codes (aka tree).
            // orig comment: This works just fine since the average
            // output size for (length,distance) codes is <= 24 bits.
            _pending = new byte[lit_bufsize * 4];
            _distanceOffset = lit_bufsize;
            _lengthOffset = (1 + 2) * lit_bufsize;

            // So, for memLevel 8, the length of the pending buffer is 65536. 64k.
            // The first 16k are pending bytes.
            // The middle slice, of 32k, is used for distance codes.
            // The final 16k are length codes.

            _compressionLevel = level;
            _compressionStrategy = strategy;

            Reset();
        }


        internal void Reset()
        {
            pendingCount = 0;
            nextPending = 0;

            Rfc1950BytesEmitted = false;

            status = Rfc1950Compliant ? INIT_STATE : BUSY_STATE;
            _adler32 = Consts.InitialAdler32;

            last_flush = (int)ZlibFlushType.None;

            InitializeTreeData();
            InitializeLazyMatch();
        }


        public ZlibCode End()
        {
            if (status != INIT_STATE &&
                status != BUSY_STATE &&
                status != FINISH_STATE)
                return ZlibCode.StreamError;

            // Deallocate in reverse order of allocations:
            _pending = null;
            _head = null;
            _prev = null;
            _window = null;
            // free
            // dstate=null;
            return status == BUSY_STATE ? ZlibCode.DataError : ZlibCode.Ok;
        }


        private void SetDeflateFunction()
        {
            switch (_config.Flavor)
            {
                case DeflateFlavor.Store:
                    DeflateFunction = DeflateNone;
                    break;

                case DeflateFlavor.Fast:
                    DeflateFunction = DeflateFast;
                    break;

                case DeflateFlavor.Slow:
                    DeflateFunction = DeflateSlow;
                    break;
            }
        }


        internal (ZlibCode Code, string? Message) SetParams(
            CompressionLevel level, CompressionStrategy strategy,
            ReadOnlySpan<byte> input, Span<byte> output,
            out int consumed, out int written)
        {
            var result = default((ZlibCode Code, string? Message));

            consumed = 0;
            written = 0;

            if (_compressionLevel != level)
            {
                var newConfig = DeflaterConfig.Lookup(level);

                // change in the deflate flavor (Fast vs slow vs none)?
                if (newConfig.Flavor != _config.Flavor)
                {
                    // Flush the last buffer:
                    result = Deflate(
                        ZlibFlushType.Partial, input, output, out consumed, out written);
                }

                _compressionLevel = level;
                _config = newConfig;
                SetDeflateFunction();
            }

            // no need to flush with change in strategy?  Really?
            _compressionStrategy = strategy;

            return result;
        }


        public void SetDictionary(ReadOnlySpan<byte> dictionary)
        {
            int length = dictionary.Length;
            int index = 0;

            if (status != INIT_STATE)
                throw new ZlibException("Stream error.");

            _adler32 = Adler32.Compute(_adler32, dictionary);

            if (length < MIN_MATCH)
                return;

            if (length > w_size - MIN_LOOKAHEAD)
            {
                length = w_size - MIN_LOOKAHEAD;
                index = dictionary.Length - length; // use the tail of the dictionary
            }
            dictionary.Slice(index, length).CopyTo(_window);
            strstart = (ushort)length;
            block_start = length;

            // Insert all strings in the hash table (except for the last two bytes).
            // s->lookahead stays null, so s->ins_h will be recomputed at the next
            // call of fill_window.

            ins_h = _window[0];
            ins_h = ((ins_h << hash_shift) ^ _window[1]) & hash_mask;

            for (int n = 0; n <= length - MIN_MATCH; n++)
            {
                ins_h = ((ins_h << hash_shift) ^ (_window[n + (MIN_MATCH - 1)])) & hash_mask;

                _prev[n & _w_mask] = _head[ins_h];
                _head[ins_h] = (ushort)n;
            }
        }

        /// <summary>
        /// Flush as much pending output as possible. 
        /// </summary>
        internal void FlushPending(ref Span<byte> output, ref int written)
        {
            int length = pendingCount;
            if (length > output.Length)
                length = output.Length;
            if (length == 0)
                return;

            if (_pending.Length <= nextPending ||
                _pending.Length < (nextPending + length))
            {
                throw new ZlibException(string.Format(
                    "Invalid State. (pending.Length={0}, pendingCount={1})",
                    _pending.Length, pendingCount));
            }

            _pending.AsSpan(nextPending, length).CopyTo(output);
            output = output.Slice(length);
            written += length;

            nextPending += length;
            pendingCount -= length;

            if (pendingCount == 0)
                nextPending = 0;
        }

        public (ZlibCode Code, string? Message) Deflate(
            ZlibFlushType flush, ReadOnlySpan<byte> input, Span<byte> output,
            out int consumed, out int written)
        {
            if (status == FINISH_STATE && flush != ZlibFlushType.Finish)
                throw new ZlibException(string.Format("Invalid state. [{0}]", _ErrorMessage[4]));

            if (output.Length == 0)
                throw new ZlibException(string.Format("No room in output. [{0}]", _ErrorMessage[7]));

            var old_flush = last_flush;
            last_flush = flush;

            // Write the zlib (rfc1950) header bytes
            if (status == INIT_STATE)
            {
                int header = (Z_DEFLATED + ((w_bits - 8) << 4)) << 8;
                int level_flags = (((int)_compressionLevel - 1) & 0xff) >> 1;

                if (level_flags > 3)
                    level_flags = 3;
                header |= level_flags << 6;
                if (strstart != 0)
                    header |= PRESET_DICT;
                header += 31 - (header % 31);

                status = BUSY_STATE;
                _pending[pendingCount++] = (byte)(header >> 8);
                _pending[pendingCount++] = (byte)header;

                // Save the adler32 of the preset dictionary:
                if (strstart != 0)
                {
                    _pending[pendingCount++] = (byte)((_adler32 & 0xFF000000) >> 24);
                    _pending[pendingCount++] = (byte)((_adler32 & 0x00FF0000) >> 16);
                    _pending[pendingCount++] = (byte)((_adler32 & 0x0000FF00) >> 8);
                    _pending[pendingCount++] = (byte)(_adler32 & 0x000000FF);
                }
                _adler32 = Consts.InitialAdler32;
            }

            consumed = 0;
            written = 0;

            // Flush as much pending output as possible
            if (pendingCount != 0)
            {
                FlushPending(ref output, ref written);

                if (output.Length == 0)
                {
                    //System.out.println("  avail_out==0");
                    // Since avail_out is 0, deflate will be called again with
                    // more output space, but possibly with both pending and
                    // avail_in equal to zero. There won't be anything to do,
                    // but this is not an error situation so make sure we
                    // return OK instead of BUF_ERROR at next call of deflate:
                    last_flush = ZlibFlushType.Unknown;
                    return (ZlibCode.Ok, null);
                }

                // Make sure there is something to do and avoid duplicate consecutive
                // flushes. For repeated and useless calls with Z_FINISH, we keep
                // returning Z_STREAM_END instead of Z_BUFF_ERROR.
            }
            else if (
                input.Length == 0 &&
                flush <= old_flush &&
                flush != ZlibFlushType.Finish)
            {
                // workitem 8557
                //
                // Not sure why this needs to be an error.  pendingCount == 0, which
                // means there's nothing to deflate.  And the caller has not asked
                // for a FlushType.Finish, but...  that seems very non-fatal.  We
                // can just say "OK" and do nothing.

                // _codec.Message = z_errmsg[ZlibConstants.Z_NEED_DICT - (ZlibConstants.Z_BUF_ERROR)];
                // throw new ZlibException("input.Length == 0 && flush<=old_flush && flush != FlushType.Finish");

                return (ZlibCode.Ok, null);
            }

            // User must not provide more input after the first FINISH:
            if (status == FINISH_STATE && input.Length != 0)
            {
                throw new ZlibException(string.Format(
                    "status == FINISH_STATE && input.Length != 0 [{0}]",
                    _ErrorMessage[ZlibCode.NeedDict - ZlibCode.BufError]));
            }

            // Start a new block or continue the current one.
            if (input.Length != 0 || lookahead != 0 || (flush != ZlibFlushType.None && status != FINISH_STATE))
            {
                DeflateBlockState bstate = DeflateFunction.Invoke(
                    flush, ref input, ref output, ref consumed, ref written);

                if (bstate == DeflateBlockState.FinishStarted || bstate == DeflateBlockState.FinishDone)
                    status = FINISH_STATE;

                if (bstate == DeflateBlockState.NeedMore || bstate == DeflateBlockState.FinishStarted)
                {
                    if (output.Length == 0)
                        last_flush = ZlibFlushType.Unknown; // avoid BUF_ERROR next call, see above

                    return (ZlibCode.Ok, null);
                    // If flush != Z_NO_FLUSH && avail_out == 0, the next call
                    // of deflate should use the same flush parameter to make sure
                    // that the flush is complete. So we don't have to output an
                    // empty block here, this will be done at next call. This also
                    // ensures that for a very small output buffer, we emit at most
                    // one empty block.
                }

                if (bstate == DeflateBlockState.BlockDone)
                {
                    if (flush == ZlibFlushType.Partial)
                    {
                        TrAlign(ref _bi);
                    }
                    else
                    {
                        // FlushType.Full or FlushType.Sync
                        TrStoredBlock(0, 0, false, ref _bi);

                        // For a full flush, this empty block will be recognized
                        // as a special marker by inflate_sync().
                        if (flush == ZlibFlushType.Full)
                        {
                            // clear hash (forget the history)
                            _head.AsSpan(0, hash_size).Clear();
                        }
                    }

                    FlushPending(ref output, ref written);

                    if (output.Length == 0)
                    {
                        last_flush = ZlibFlushType.Unknown; // avoid BUF_ERROR at next call, see above
                        return (ZlibCode.Ok, null);
                    }
                }
            }

            if (flush != ZlibFlushType.Finish)
                return (ZlibCode.Ok, null);

            if (!Rfc1950Compliant || Rfc1950BytesEmitted)
                return (ZlibCode.StreamEnd, null);

            // Write the zlib trailer (adler32)
            _pending[pendingCount++] = (byte)((_adler32 & 0xFF000000) >> 24);
            _pending[pendingCount++] = (byte)((_adler32 & 0x00FF0000) >> 16);
            _pending[pendingCount++] = (byte)((_adler32 & 0x0000FF00) >> 8);
            _pending[pendingCount++] = (byte)(_adler32 & 0x000000FF);

            FlushPending(ref output, ref written);

            Rfc1950BytesEmitted = true; // write the trailer only once!

            return (pendingCount != 0 ? ZlibCode.Ok : ZlibCode.StreamEnd, null);
        }
    }
}