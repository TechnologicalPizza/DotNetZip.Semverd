using System.IO;
using Ionic.Zlib;

namespace Zlib.Benchmark
{
    public class IonicDeflate : PressBase
    {
        public override Stream CreateDecompressor(Stream input)
        {
            return new DeflateStream(input, CompressionMode.Decompress);
        }

        public override Stream CreateCompressor(Stream output)
        {
            return new DeflateStream(output, IonicLevel(Level));
        }
    }
}
