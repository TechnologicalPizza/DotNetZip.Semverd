// Inflate.cs
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
// Time-stamp: <2010-January-08 18:32:12>
//
// ------------------------------------------------------------------
//
// This module defines classes for decompression. This code is derived
// from the jzlib implementation of zlib, but significantly modified.
// The object model is not the same, and many of the behaviors are
// different.  Nonetheless, in keeping with the license for jzlib, I am
// reproducing the copyright to that code here.
//
// ------------------------------------------------------------------
//
// Copyright (c) 2000,2001,2002,2003 ymnk, JCraft,Inc. All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
// 1. Redistributions of source code must retain the above copyright notice,
// this list of conditions and the following disclaimer.
//
// 2. Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in
// the documentation and/or other materials provided with the distribution.
//
// 3. The names of the authors may not be used to endorse or promote products
// derived from this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED ``AS IS'' AND ANY EXPRESSED OR IMPLIED WARRANTIES,
// INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL JCRAFT,
// INC. OR ANY CONTRIBUTORS TO THIS SOFTWARE BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
// LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
// NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
// EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// -----------------------------------------------------------------------
//
// This program is based on zlib-1.1.3; credit to authors
// Jean-loup Gailly(jloup@gzip.org) and Mark Adler(madler@alumni.caltech.edu)
// and contributors of zlib.
//
// -----------------------------------------------------------------------


using System;

namespace Ionic.Zlib
{
    internal sealed class Inflater
    {
        // preset dictionary flag in zlib header
        private const int PRESET_DICT = 0x20;

        private const int Z_DEFLATED = 8;

        private static readonly byte[] mark = new byte[] { 0, 0, 0xff, 0xff };

        private InflaterMode mode; // current inflate mode
        internal ZlibCodec _codec; // pointer back to this zlib stream

        // mode dependent information
        internal int method; // if FLAGS, method byte

        // if CHECK, check values to compare
        internal uint computedCheck; // computed check value
        internal uint expectedCheck; // stream check value

        // if BAD, inflateSync's marker bytes count
        internal int marker;

        internal bool HandleRfc1950HeaderBytes { get; set; } = true;
        internal int _windowBits; // log2(window size)  (8..15, defaults to 15)

        internal InflateBlocks? blocks; // current inflate_blocks state

        public Inflater()
        {
        }

        public Inflater(bool expectRfc1950HeaderBytes)
        {
            HandleRfc1950HeaderBytes = expectRfc1950HeaderBytes;
        }

        internal ZlibCode Reset()
        {
            _codec.Message = null;
            mode = HandleRfc1950HeaderBytes ? InflaterMode.METHOD : InflaterMode.BLOCKS;
            blocks.Reset();
            return ZlibCode.Ok;
        }

        internal ZlibCode End()
        {
            blocks = null;
            return ZlibCode.Ok;
        }

        internal ZlibCode Initialize(ZlibCodec codec, int windowBits)
        {
            _codec = codec;
            _codec.Message = null;
            blocks = null;

            // handle undocumented nowrap option (no zlib header or check)
            //nowrap = 0;
            //if (w < 0)
            //{
            //    w = - w;
            //    nowrap = 1;
            //}

            // set window size
            if (windowBits < 8 || windowBits > 15)
            {
                End();
                throw new ZlibException("Bad window size.");

                //return ZlibCode.Z_STREAM_ERROR;
            }
            _windowBits = windowBits;

            blocks = new InflateBlocks(
                codec,
                HandleRfc1950HeaderBytes,
                1 << windowBits);

            // reset state
            Reset();
            return ZlibCode.Ok;
        }


        internal ZlibCode Inflate(
            FlushType flush, ReadOnlySpan<byte> input, Span<byte> output,
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

            while (true)
            {
                switch (mode)
                {
                    case InflaterMode.METHOD:
                        if (length == 0)
                            return r;

                        r = f;
                        length--;
                        if (((method = input[consumed++]) & 0xf) != Z_DEFLATED)
                        {
                            mode = InflaterMode.BAD;
                            _codec.Message = string.Format("unknown compression method (0x{0:X2})", method);
                            marker = 5; // can't try inflateSync
                            break;
                        }
                        if ((method >> 4) + 8 > _windowBits)
                        {
                            mode = InflaterMode.BAD;
                            _codec.Message = string.Format("invalid window size ({0})", (method >> 4) + 8);
                            marker = 5; // can't try inflateSync
                            break;
                        }
                        mode = InflaterMode.FLAG;
                        break;


                    case InflaterMode.FLAG:
                        if (length == 0)
                            return r;
                        r = f;
                        length--;
                        b = (input[consumed++]) & 0xff;

                        if ((((method << 8) + b) % 31) != 0)
                        {
                            mode = InflaterMode.BAD;
                            _codec.Message = "incorrect header check";
                            marker = 5; // can't try inflateSync
                            break;
                        }

                        mode = ((b & PRESET_DICT) == 0)
                            ? InflaterMode.BLOCKS
                            : InflaterMode.DICT4;
                        break;

                    case InflaterMode.DICT4:
                        if (length == 0)
                            return r;
                        r = f;
                        length--;
                        expectedCheck = (uint)((input[consumed++] << 24) & 0xff000000);
                        mode = InflaterMode.DICT3;
                        break;

                    case InflaterMode.DICT3:
                        if (length == 0)
                            return r;
                        r = f;
                        length--;
                        expectedCheck += (uint)((input[consumed++] << 16) & 0x00ff0000);
                        mode = InflaterMode.DICT2;
                        break;

                    case InflaterMode.DICT2:

                        if (length == 0)
                            return r;
                        r = f;
                        length--;
                        expectedCheck += (uint)((input[consumed++] << 8) & 0x0000ff00);
                        mode = InflaterMode.DICT1;
                        break;


                    case InflaterMode.DICT1:
                        if (length == 0)
                            return r;

                        //length--; // unnecessary due to return
                        expectedCheck += (uint)(input[consumed++] & 0x000000ff);
                        _codec._adler32 = expectedCheck;
                        mode = InflaterMode.DICT0;
                        return ZlibCode.NeedDict;


                    case InflaterMode.DICT0:
                        mode = InflaterMode.BAD;
                        _codec.Message = "need dictionary";
                        marker = 0; // can try inflateSync
                        return ZlibCode.StreamError;


                    case InflaterMode.BLOCKS:
                        r = blocks.Process(
                            r, input, ref output, ref consumed, ref length, ref written);

                        if (r == ZlibCode.DataError)
                        {
                            mode = InflaterMode.BAD;
                            marker = 0; // can try inflateSync
                            break;
                        }

                        if (r == ZlibCode.Ok)
                            r = f;

                        if (r != ZlibCode.StreamEnd)
                            return r;

                        r = f;
                        computedCheck = blocks.Reset();
                        if (!HandleRfc1950HeaderBytes)
                        {
                            mode = InflaterMode.DONE;
                            return ZlibCode.StreamEnd;
                        }
                        mode = InflaterMode.CHECK4;
                        break;

                    case InflaterMode.CHECK4:
                        if (length == 0)
                            return r;
                        r = f;
                        length--;
                        expectedCheck = (uint)((input[consumed++] << 24) & 0xff000000);
                        mode = InflaterMode.CHECK3;
                        break;

                    case InflaterMode.CHECK3:
                        if (length == 0)
                            return r;
                        r = f;
                        length--;
                        expectedCheck += (uint)((input[consumed++] << 16) & 0x00ff0000);
                        mode = InflaterMode.CHECK2;
                        break;

                    case InflaterMode.CHECK2:
                        if (length == 0)
                            return r;
                        r = f;
                        length--;
                        expectedCheck += (uint)((input[consumed++] << 8) & 0x0000ff00);
                        mode = InflaterMode.CHECK1;
                        break;

                    case InflaterMode.CHECK1:
                        if (length == 0)
                            return r;
                        r = f;
                        length--;
                        expectedCheck += (uint)(input[consumed++] & 0x000000ff);
                        if (computedCheck != expectedCheck)
                        {
                            mode = InflaterMode.BAD;
                            _codec.Message = "incorrect data check";
                            marker = 5; // can't try inflateSync
                            break;
                        }
                        mode = InflaterMode.DONE;
                        return ZlibCode.StreamEnd;

                    case InflaterMode.DONE:
                        return ZlibCode.StreamEnd;

                    case InflaterMode.BAD:
                        throw new ZlibException(string.Format("Bad state ({0})", _codec.Message));

                    default:
                        throw new ZlibException("Stream error.");

                }
            }
        }



        internal ZlibCode SetDictionary(ReadOnlySpan<byte> dictionary, bool unconditional = false)
        {
            if (blocks == null)
                throw new InvalidOperationException();

            //MSZip requires the dictionary to be set unconditionally
            if (!unconditional)
            {
                if (mode != InflaterMode.DICT0)
                    throw new ZlibException("Stream error.");

                if (Adler.Adler32(1, dictionary) != _codec._adler32)
                    return ZlibCode.DataError;
            }

            _codec._adler32 = Adler.Adler32(0, default);

            if (dictionary.Length >= (1 << _windowBits))
            {
                int length = (1 << _windowBits) - 1;
                int index = dictionary.Length - length;
                dictionary = dictionary.Slice(index, length);
            }

            blocks.SetDictionary(dictionary);
            mode = InflaterMode.BLOCKS;
            return ZlibCode.Ok;
        }

        internal ZlibCode Sync(ReadOnlySpan<byte> input, out int consumed)
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
            while (!input.IsEmpty && marker < 4)
            {
                if (input[0] == mark[marker])
                    marker++;
                else if (input[0] != 0)
                    marker = 0;
                else
                    marker = 4 - marker;

                input = input.Slice(1);
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
            if (blocks == null)
                throw new InvalidOperationException();

            return blocks.SyncPoint();
        }
    }
}