﻿// Inflate.cs
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

namespace Ionic.Zlib
{
    internal enum InflateBlockMode
    {
        TYPE = 0,        // get type bits (3, including end bit)
        LENS = 1,        // get lengths for stored
        STORED = 2,      // processing stored block
        TABLE = 3,       // get table lengths
        BTREE = 4,       // get bit lengths tree for a dynamic block
        DTREE = 5,       // get length, distance trees for a dynamic block
        CODES = 6,       // processing fixed or dynamic block
        DRY = 7,         // output remaining window bytes
        DONE = 8,        // finished last block, done
        BAD = 9,         // ot a data error--stuck here
    }
}