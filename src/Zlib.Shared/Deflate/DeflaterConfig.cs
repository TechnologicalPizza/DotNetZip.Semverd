// Deflate.cs
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
// Time-stamp: <2011-August-03 19:52:15>
//
// ------------------------------------------------------------------
//
// This module defines logic for handling the Deflate or compression.
//
// This code is based on multiple sources:
// - the original zlib v1.2.3 source, which is Copyright (C) 1995-2005 Jean-loup Gailly.
// - the original jzlib, which is Copyright (c) 2000-2003 ymnk, JCraft,Inc.
//
// However, this code is significantly different from both.
// The object model is not the same, and many of the behaviors are different.
//
// In keeping with the license for these other works, the copyrights for
// jzlib and zlib are here.
//
// -----------------------------------------------------------------------
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

namespace Ionic.Zlib
{
    internal class DeflaterConfig
    {
        // Use a faster search when the previous match is longer than this
        internal int GoodLength; // reduce lazy search above this match length

        // Attempt to find a better match only when the current match is
        // strictly smaller than this value. This mechanism is used only for
        // compression levels >= 4.  For levels 1,2,3: MaxLazy is actually
        // MaxInsertLength. (See DeflateFast)

        internal int MaxLazy;    // do not perform lazy search above this match length

        internal int NiceLength; // quit search above this match length

        // To speed up deflation, hash chains are never searched beyond this
        // length.  A higher limit improves compression ratio but degrades the speed.

        internal int MaxChainLength;

        internal DeflateFlavor Flavor;

        private DeflaterConfig(int goodLength, int maxLazy, int niceLength, int maxChainLength, DeflateFlavor flavor)
        {
            GoodLength = goodLength;
            MaxLazy = maxLazy;
            NiceLength = niceLength;
            MaxChainLength = maxChainLength;
            Flavor = flavor;
        }

        public static DeflaterConfig Lookup(CompressionLevel level)
        {
            return Table[(int)level];
        }


        static DeflaterConfig()
        {
            Table = new DeflaterConfig[] {
                    new DeflaterConfig(0, 0, 0, 0, DeflateFlavor.Store),
                    new DeflaterConfig(4, 4, 8, 4, DeflateFlavor.Fast),
                    new DeflaterConfig(4, 5, 16, 8, DeflateFlavor.Fast),
                    new DeflaterConfig(4, 6, 32, 32, DeflateFlavor.Fast),

                    new DeflaterConfig(4, 4, 16, 16, DeflateFlavor.Slow),
                    new DeflaterConfig(8, 16, 32, 32, DeflateFlavor.Slow),
                    new DeflaterConfig(8, 16, 128, 128, DeflateFlavor.Slow),
                    new DeflaterConfig(8, 32, 128, 256, DeflateFlavor.Slow),
                    new DeflaterConfig(32, 128, 258, 1024, DeflateFlavor.Slow),
                    new DeflaterConfig(32, 258, 258, 4096, DeflateFlavor.Slow),
                };
        }

        private static readonly DeflaterConfig[] Table;
    }

}