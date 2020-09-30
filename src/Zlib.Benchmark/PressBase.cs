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

        [Params(/*1024 * 16,*/ 1024 * 64)]
        public int ByteCount { get; set; }

        [Params(CompressionLevel.Fastest/*, CompressionLevel.Optimal*/)]
        public CompressionLevel Level { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _data = new byte[ByteCount];
            _buffer = new byte[ByteCount];

            var rng = new Random(1234);
            string bigg = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            string smol = "abcdefghijklmnopqrstuvwxyz";
            for (int i = 0; i < _data.Length; i++)
            {
                string src;
                if (rng.Next(0, 10) == 0)
                    src = bigg;
                else
                    src = smol;

                _data[i] = (byte)src[rng.Next(src.Length)];
            }

            using var compressed = new MemoryStream();
            using (var compressor = CreateCompressor(compressed))
                compressor.Write(_data);
            _compressed = compressed.ToArray();
        }

        public abstract Stream CreateCompressor(Stream output);

        public abstract Stream CreateDecompressor(Stream input);

        [Benchmark]
        public long Compress()
        {
            var counter = new CounterStream(Stream.Null);
            using (var compressor = CreateCompressor(counter))
                compressor.Write(_data);
            return counter.TotalWrite;
        }

        //[Benchmark]
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
