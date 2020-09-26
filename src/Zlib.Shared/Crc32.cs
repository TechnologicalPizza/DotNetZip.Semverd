// CRC32.cs
// ------------------------------------------------------------------
//
// Copyright (c) 2011 Dino Chiesa.
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
// Last Saved: <2011-August-02 18:25:54>
//
// ------------------------------------------------------------------
//
// This module defines the CRC32 class, which can do the CRC32 algorithm, using
// arbitrary starting polynomials, and bit reversal. The bit reversal is what
// distinguishes this CRC-32 used in BZip2 from the CRC-32 that is used in PKZIP
// files, or GZIP files. This class does both.
//
// ------------------------------------------------------------------


using System;

namespace Ionic.Crc
{
    /// <summary>
    ///   Computes a CRC-32. The CRC-32 algorithm is parameterized - 
    ///   you can set the polynomial and enable or disable bit reversal. 
    ///   This can be used for GZIP, BZip2, or ZIP.
    /// </summary>
    public class Crc32
    {
        // TODO: vectorize with version from chromium source

        private const int BUFFER_SIZE = 4096;

        // private member vars
        private uint dwPolynomial;
        private bool reverseBits;
        private uint[] crc32Table;
        private uint _register = 0xFFFFFFFFU;

        /// <summary>
        ///   Indicates the total number of bytes applied to the CRC.
        /// </summary>
        public long TotalBytesRead { get; private set; }

        /// <summary>
        /// Indicates the current CRC for all blocks slurped in.
        /// </summary>
        public int Crc32Result => unchecked((int)~_register);

        /// <summary>
        /// Returns the CRC32 for the specified stream.
        /// </summary>
        /// <param name="input">The stream over which to calculate the CRC32</param>
        /// <returns>the CRC32 calculation</returns>
        public int GetCrc32(System.IO.Stream input)
        {
            return GetCrc32AndCopy(input, null);
        }

        /// <summary>
        /// Returns the CRC32 for the specified stream, and writes the input into the
        /// output stream.
        /// </summary>
        /// <param name="input">The stream over which to calculate the CRC32</param>
        /// <param name="output">The stream into which to deflate the input</param>
        /// <returns>the CRC32 calculation</returns>
        public int GetCrc32AndCopy(System.IO.Stream input, System.IO.Stream? output)
        {
            if (input == null)
                throw new Exception("The input stream must not be null.");

            unchecked
            {
                Span<byte> buffer = stackalloc byte[BUFFER_SIZE];
                TotalBytesRead = 0;

                int count;
                while ((count = input.Read(buffer)) > 0)
                {
                    var buf = buffer.Slice(0, count);
                    SlurpBlock(buf);
                    output?.Write(buf);
                    TotalBytesRead += count;
                }

                return (int)~_register;
            }
        }


        /// <summary>
        ///   Get the CRC32 for the given (word,byte) combo.  This is a
        ///   computation defined by PKzip for PKZIP 2.0 (weak) encryption.
        /// </summary>
        /// <param name="W">The word to start with.</param>
        /// <param name="B">The byte to combine it with.</param>
        /// <returns>The CRC-ized result.</returns>
        public int ComputeCrc32(int W, byte B)
        {
            return ComputeCrc32((uint)W, B);
        }

        internal int ComputeCrc32(uint W, byte B)
        {
            return (int)(crc32Table[(W ^ B) & 0xFF] ^ (W >> 8));
        }

        /// <summary>
        /// Update the value for the running CRC32 using the given block of bytes.
        /// This is useful when using the CRC32() class in a Stream.
        /// </summary>
        /// <param name="block">block of bytes to slurp</param>
        public void SlurpBlock(ReadOnlySpan<byte> block)
        {
            // bzip algorithm
            for (int i = 0; i < block.Length; i++)
            {
                byte b = block[i];
                if (reverseBits)
                {
                    uint temp = (_register >> 24) ^ b;
                    _register = (_register << 8) ^ crc32Table[temp];
                }
                else
                {
                    uint temp = (_register & 0x000000FF) ^ b;
                    _register = (_register >> 8) ^ crc32Table[temp];
                }
            }
            TotalBytesRead += block.Length;
        }


        /// <summary>
        ///   Process one byte in the CRC.
        /// </summary>
        /// <param name = "b">the byte to include into the CRC .  </param>
        public void UpdateCRC(byte b)
        {
            if (reverseBits)
            {
                uint tmp = (_register >> 24) ^ b;
                _register = (_register << 8) ^ crc32Table[tmp];
            }
            else
            {
                uint tmp = (_register & 0x000000FF) ^ b;
                _register = (_register >> 8) ^ crc32Table[tmp];
            }
        }

        /// <summary>
        ///   Process a run of N identical bytes into the CRC.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     This method serves as an optimization for updating the CRC when a
        ///     run of identical bytes is found. Rather than passing in a buffer of
        ///     length n, containing all identical bytes b, this method accepts the
        ///     byte value and the length of the (virtual) buffer - the length of
        ///     the run.
        ///   </para>
        /// </remarks>
        /// <param name = "b">the byte to include into the CRC.  </param>
        /// <param name = "n">the number of times that byte should be repeated. </param>
        public void UpdateCRC(byte b, int n)
        {
            while (n-- > 0)
            {
                if (reverseBits)
                {
                    uint tmp = (_register >> 24) ^ b;
                    _register = (_register << 8) ^ crc32Table[(tmp >= 0) ? tmp : (tmp + 256)];
                }
                else
                {
                    uint tmp = (_register & 0x000000FF) ^ b;
                    _register = (_register >> 8) ^ crc32Table[(tmp >= 0) ? tmp : (tmp + 256)];

                }
            }
        }



        private static uint ReverseBits(uint data)
        {
            unchecked
            {
                uint ret = data;
                ret = (ret & 0x55555555) << 1 | (ret >> 1) & 0x55555555;
                ret = (ret & 0x33333333) << 2 | (ret >> 2) & 0x33333333;
                ret = (ret & 0x0F0F0F0F) << 4 | (ret >> 4) & 0x0F0F0F0F;
                ret = (ret << 24) | ((ret & 0xFF00) << 8) | ((ret >> 8) & 0xFF00) | (ret >> 24);
                return ret;
            }
        }

        private static byte ReverseBits(byte data)
        {
            unchecked
            {
                uint u = (uint)data * 0x00020202;
                uint m = 0x01044010;
                uint s = u & m;
                uint t = (u << 2) & (m << 1);
                return (byte)((0x01001001 * (s + t)) >> 24);
            }
        }



        private void GenerateLookupTable()
        {
            crc32Table = new uint[256];
            unchecked
            {
                uint dwCrc;
                byte i = 0;
                do
                {
                    dwCrc = i;
                    for (byte j = 8; j > 0; j--)
                    {
                        if ((dwCrc & 1) == 1)
                        {
                            dwCrc = (dwCrc >> 1) ^ dwPolynomial;
                        }
                        else
                        {
                            dwCrc >>= 1;
                        }
                    }
                    if (reverseBits)
                    {
                        crc32Table[ReverseBits(i)] = ReverseBits(dwCrc);
                    }
                    else
                    {
                        crc32Table[i] = dwCrc;
                    }
                    i++;
                } while (i != 0);
            }
        }


        private static uint Gf2_matrix_times(Span<uint> matrix, uint vec)
        {
            uint sum = 0;
            int i = 0;
            while (vec != 0)
            {
                if ((vec & 0x01) == 0x01)
                    sum ^= matrix[i];
                vec >>= 1;
                i++;
            }
            return sum;
        }

        private static void Gf2_matrix_square(Span<uint> square, Span<uint> mat)
        {
            for (int i = 0; i < square.Length; i++)
                square[i] = Gf2_matrix_times(mat, mat[i]);
        }



        /// <summary>
        ///   Combines the given CRC32 value with the current running total.
        /// </summary>
        /// <remarks>
        ///   This is useful when using a divide-and-conquer approach to
        ///   calculating a CRC.  Multiple threads can each calculate a
        ///   CRC32 on a segment of the data, and then combine the
        ///   individual CRC32 values at the end.
        /// </remarks>
        /// <param name="crc">the crc value to be combined with this one</param>
        /// <param name="length">the length of data the CRC value was calculated on</param>
        public void Combine(int crc, int length)
        {
            Span<uint> odd = stackalloc uint[32]; // odd-power-of-two zeros operator
            Span<uint> even = stackalloc uint[32]; // even-power-of-two zeros operator

            if (length == 0)
                return;

            uint crc1 = ~_register;
            uint crc2 = (uint)crc;

            // put operator for one zero bit in odd
            odd[0] = dwPolynomial;  // the CRC-32 polynomial
            uint row = 1;
            for (int i = 1; i < 32; i++)
            {
                odd[i] = row;
                row <<= 1;
            }

            // put operator for two zero bits in even
            Gf2_matrix_square(even, odd);

            // put operator for four zero bits in odd
            Gf2_matrix_square(odd, even);

            uint len2 = (uint)length;

            // apply len2 zeros to crc1 (first square will put the operator for one
            // zero byte, eight zero bits, in even)
            do
            {
                // apply zeros operator for this bit of len2
                Gf2_matrix_square(even, odd);

                if ((len2 & 1) == 1)
                    crc1 = Gf2_matrix_times(even, crc1);
                len2 >>= 1;

                if (len2 == 0)
                    break;

                // another iteration of the loop with odd and even swapped
                Gf2_matrix_square(odd, even);
                if ((len2 & 1) == 1)
                    crc1 = Gf2_matrix_times(odd, crc1);
                len2 >>= 1;


            } while (len2 != 0);

            crc1 ^= crc2;

            _register = ~crc1;

            //return (int) crc1;
            return;
        }


        /// <summary>
        ///   Create an instance of the CRC32 class using the default settings: no
        ///   bit reversal, and a polynomial of 0xEDB88320.
        /// </summary>
        public Crc32() : this(false)
        {
        }

        /// <summary>
        ///   Create an instance of the CRC32 class, specifying whether to reverse
        ///   data bits or not and a polynomial of 0xEDB88320.
        /// </summary>
        /// <param name='reverseBits'>
        ///   specify true if the instance should reverse data bits.
        /// </param>
        /// <remarks>
        ///   <para>
        ///     In the CRC-32 used by BZip2, the bits are reversed. Therefore if you
        ///     want a CRC32 with compatibility with BZip2, you should pass true
        ///     here. In the CRC-32 used by GZIP and PKZIP, the bits are not
        ///     reversed; Therefore if you want a CRC32 with compatibility with
        ///     those, you should pass false.
        ///   </para>
        /// </remarks>
        public Crc32(bool reverseBits) :
            this(unchecked((int)0xEDB88320), reverseBits)
        {
        }


        /// <summary>
        ///   Create an instance of the CRC32 class, specifying the polynomial and
        ///   whether to reverse data bits or not.
        /// </summary>
        /// <param name='polynomial'>
        ///   The polynomial to use for the CRC, expressed in the reversed (LSB)
        ///   format: the highest ordered bit in the polynomial value is the
        ///   coefficient of the 0th power; the second-highest order bit is the
        ///   coefficient of the 1 power, and so on. Expressed this way, the
        ///   polynomial for the CRC-32C used in IEEE 802.3, is 0xEDB88320.
        /// </param>
        /// <param name='reverseBits'>
        ///   specify true if the instance should reverse data bits.
        /// </param>
        ///
        /// <remarks>
        ///   <para>
        ///     In the CRC-32 used by BZip2, the bits are reversed. Therefore if you
        ///     want a CRC32 with compatibility with BZip2, you should pass true
        ///     here for the <c>reverseBits</c> parameter. In the CRC-32 used by
        ///     GZIP and PKZIP, the bits are not reversed; Therefore if you want a
        ///     CRC32 with compatibility with those, you should pass false for the
        ///     <c>reverseBits</c> parameter.
        ///   </para>
        /// </remarks>
        public Crc32(int polynomial, bool reverseBits)
        {
            this.reverseBits = reverseBits;
            dwPolynomial = (uint)polynomial;
            GenerateLookupTable();
        }

        /// <summary>
        ///   Reset the CRC-32 class - clear the CRC "remainder register."
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     Use this when employing a single instance of this class to compute
        ///     multiple, distinct CRCs on multiple, distinct data blocks.
        ///   </para>
        /// </remarks>
        public void Reset()
        {
            _register = 0xFFFFFFFFU;
        }
    }
}
