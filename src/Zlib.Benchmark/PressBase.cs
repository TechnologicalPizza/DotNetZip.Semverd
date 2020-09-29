using System;
using System.IO;
using System.IO.Compression;
using BenchmarkDotNet.Attributes;

namespace Zlib.Benchmark
{
    public abstract class PressBase
    {
        private byte[] _data;
        private byte[] _buffer;
        private byte[] _compressed;

        [Params(1024 * 16, 1024 * 64)]
        public int ByteCount { get; set; }

        [Params(CompressionLevel.Fastest/*, CompressionLevel.Optimal*/)]
        public CompressionLevel Level { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _data = new byte[ByteCount];
            _buffer = new byte[ByteCount];
            new Random(1234).NextBytes(_data);

            using var compressed = new MemoryStream();
            using (var compressor = CreateCompressor(compressed))
                compressor.Write(_data);
            _compressed = compressed.ToArray();
        }

        public abstract Stream CreateCompressor(Stream output);

        public abstract Stream CreateDecompressor(Stream input);

        [Benchmark]
        public void Compress()
        {
            using var compressor = CreateCompressor(Stream.Null);
            compressor.Write(_data);
        }

        [Benchmark]
        public void Decompress()
        {
            using var decompressor = CreateDecompressor(new MemoryStream(_compressed, 0, _compressed.Length));
            var buffer = _buffer.AsSpan();
            while (decompressor.Read(buffer) > 0)
            {
            }
        }

        public static Ionic.Zlib.CompressionLevel IonicLevel(CompressionLevel level)
        {
            return level switch
            {
                CompressionLevel.NoCompression => Ionic.Zlib.CompressionLevel.None,
                CompressionLevel.Fastest => Ionic.Zlib.CompressionLevel.BestSpeed,
                CompressionLevel.Optimal => Ionic.Zlib.CompressionLevel.BestCompression,
                _ => throw new InvalidOperationException(),
            };
        }

        public static Ionic.Zlib.CompressionMode IonicMode(CompressionMode mode)
        {
            return mode switch
            {
                CompressionMode.Compress => Ionic.Zlib.CompressionMode.Compress,
                CompressionMode.Decompress => Ionic.Zlib.CompressionMode.Decompress,
                _ => throw new InvalidOperationException()
            };
        }
    }
}
