using System.IO;
using System.IO.Compression;

namespace Zlib.Benchmark
{
    public class SystemDeflate : PressBase
    {
        public override Stream CreateDecompressor(Stream input)
        {
            return new DeflateStream(input, CompressionMode.Decompress);
        }

        public override Stream CreateCompressor(Stream output)
        {
            return new DeflateStream(output, Level);
        }
    }
}
