// See the LICENSE file for license details.

using System;

namespace Ionic
{
    /// <summary>
    /// Computes an Adler-32 checksum.
    /// </summary>
    /// <remarks>
    /// The Adler checksum is similar to a CRC checksum,
    /// but faster to compute, though less reliable.  
    /// It is used in producing RFC1950 compressed streams. 
    /// </remarks>
    public sealed class Adler32
    {
        // TODO: vectorize with version from chromium source

        // largest prime smaller than 65536
        private const uint BASE = 65521;

        // NMAX is the largest n such that 255n(n+1)/2 + (n+1)(BASE-1) <= 2^32-1
        private const int NMAX = 5552;

        /// <summary>
        ///   Calculates a Adler32 checksum.
        /// </summary>
        public static uint Compute(uint baseSum, ReadOnlySpan<byte> buf)
        {
            if (buf == null)
                return 1;

            uint s1 = baseSum & 0xffff;
            uint s2 = (baseSum >> 16) & 0xffff;

            while (buf.Length > 0)
            {
                var k = buf.Slice(0, Math.Min(NMAX, buf.Length));
                buf = buf.Slice(k.Length);

                while (k.Length >= 16)
                {
                    s2 += s1 += k[0];
                    s2 += s1 += k[1];
                    s2 += s1 += k[2];
                    s2 += s1 += k[3];
                    s2 += s1 += k[4];
                    s2 += s1 += k[5];
                    s2 += s1 += k[6];
                    s2 += s1 += k[7];
                    s2 += s1 += k[8];
                    s2 += s1 += k[9];
                    s2 += s1 += k[10];
                    s2 += s1 += k[11];
                    s2 += s1 += k[12];
                    s2 += s1 += k[13];
                    s2 += s1 += k[14];
                    s2 += s1 += k[15];

                    k = k.Slice(16);
                }

                for (int i = 0; i < k.Length; i++)
                {
                    s1 += k[i];
                    s2 += s1;
                }

                s1 %= BASE;
                s2 %= BASE;
            }
            return (s2 << 16) | s1;
        }
    }

}