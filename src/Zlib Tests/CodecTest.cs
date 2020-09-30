using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ionic.Zlib.Tests
{
    [TestClass]
    public class CodecTest : TestHarness
    {
        [TestMethod]
        public void Compat_decompress_wi13446()
        {
            string zlibbedFile = GetContentFilePath("zlibbed.file");

            var streamCopy = new Action<Stream, Stream, int>((source, dest, bufferSize) =>
            {
                var temp = new byte[bufferSize];
                while (true)
                {
                    var read = source.Read(temp, 0, temp.Length);
                    if (read <= 0)
                        break;
                    dest.Write(temp, 0, read);
                }
            });

            var unpack = new Action<int>((bufferSize) =>
            {
                using var output = new MemoryStream();
                using var input = File.OpenRead(zlibbedFile);
                using var zinput = new ZlibStream(input, CompressionMode.Decompress);
                streamCopy(zinput, output, bufferSize);
            });

            unpack(1024);
            unpack(16384);
        }


        [TestMethod]
        public void DeflateAndInflateOneByOne()
        {
            string TextToCompress = LoremIpsum;

            int bufferSize = 40000;
            var compressedBytes = new byte[bufferSize];
            var decompressedBytes = new byte[bufferSize];

            ZlibCode code;
            string? message;
            int consumed;
            int written;

            int compressorTotalBytesIn = 0;
            int compressorTotalBytesOut = 0;
            {
                var deflater = new Deflater();
                deflater.Setup();

                var inputBuffer = Encoding.ASCII.GetBytes(TextToCompress);
                var outputBuffer = compressedBytes;

                var input = inputBuffer.AsSpan();
                var output = outputBuffer.AsSpan();

                while (input.Length > 0 && output.Length > 0)
                {
                    // force small buffers

                    (code, message) = deflater.Deflate(
                        ZlibFlushType.None,
                        input.Slice(0, Math.Min(input.Length, 1)),
                        output.Slice(0, Math.Min(output.Length, 1)),
                        out consumed, out written);

                    input = input.Slice(consumed);
                    output = output.Slice(written);
                    compressorTotalBytesIn += consumed;
                    compressorTotalBytesOut += written;

                    Assert.AreEqual(ZlibCode.Ok, code, string.Format("at Deflate(1) [{0}]", message));
                }

                while (true)
                {
                    // force small buffers

                    (code, message) = deflater.Deflate(
                        ZlibFlushType.Finish,
                        input.Slice(0, Math.Min(input.Length, 1)),
                        output.Slice(0, Math.Min(output.Length, 1)),
                        out consumed, out written);

                    input = input.Slice(consumed);
                    output = output.Slice(written);
                    compressorTotalBytesIn += consumed;
                    compressorTotalBytesOut += written;

                    if (code == ZlibCode.StreamEnd)
                        break;
                    Assert.AreEqual(ZlibCode.Ok, code, string.Format("at Deflate(2) [{0}]", message));
                }
                deflater.End();
            }

            int decompressorTotalBytesIn = 0;
            int decompressorTotalBytesOut = 0;
            {
                var inflater = new Inflater();
                inflater.Setup();

                var input = compressedBytes.AsSpan(0, compressorTotalBytesOut);
                var output = decompressedBytes.AsSpan(0, compressorTotalBytesIn);
                while (input.Length > 0 && output.Length > 0)
                {
                    (code, message) = inflater.Inflate(
                        ZlibFlushType.None,
                        input.Slice(0, 1), output.Slice(0, 1),
                        out consumed, out written);

                    input = input.Slice(consumed);
                    output = output.Slice(written);
                    decompressorTotalBytesIn += consumed;
                    decompressorTotalBytesOut += written;

                    if (code == ZlibCode.StreamEnd)
                        break;
                    Assert.AreEqual(ZlibCode.Ok, code, string.Format("at Inflate() [{0}]", message));
                }
            }

            int j = 0;
            for (; j < decompressedBytes.Length; j++)
            {
                if (decompressedBytes[j] == 0)
                    break;
            }
            Assert.AreEqual(TextToCompress.Length, j, string.Format("Unequal lengths"));

            int i = 0;
            for (; i < j; i++)
            {
                if (TextToCompress[i] != decompressedBytes[i])
                    break;
            }
            Assert.AreEqual(j, i, string.Format("Non-identical content"));

            TestContext.WriteLine("orig length: {0}", TextToCompress.Length);
            TestContext.WriteLine("compressed length: {0}", compressorTotalBytesOut);
            TestContext.WriteLine("decompressed length: {0}", decompressorTotalBytesOut);

            var result = Encoding.ASCII.GetString(decompressedBytes, 0, j);
            TestContext.WriteLine("result length: {0}", result.Length);
            TestContext.WriteLine("result of inflate:\n{0}", result);
        }


        [TestMethod]
        public void DeflateInflateWithDictionary()
        {
            int comprLen = 40000;
            int uncomprLen = comprLen;
            var uncompr = new byte[uncomprLen];
            var compr = new byte[comprLen];

            string dictionaryWord = "hello ";
            string TextToCompress = "hello, hello!  How are you, Joe? I said hello. ";
            byte[] dictionary = Encoding.ASCII.GetBytes(dictionaryWord);
            byte[] BytesToCompress = Encoding.ASCII.GetBytes(TextToCompress);

            int compressorAdler32;
            int compressorTotalBytesOut;
            {
                var deflater = new Deflater();
                deflater.Setup(CompressionLevel.BestCompression);

                deflater.SetDictionary(dictionary);
                compressorAdler32 = deflater.AdlerChecksum;

                var input = BytesToCompress.AsSpan();
                var output = compr.AsSpan(0, comprLen);

                var (code, message) = deflater.Deflate(
                    ZlibFlushType.Finish, input, output,
                    out _, out compressorTotalBytesOut);

                Assert.AreEqual(ZlibCode.StreamEnd, code, string.Format("at Deflate() [{0}]", message));
                deflater.End();
            }

            int decompressorTotalBytesOut = 0;
            {
                var inflater = new Inflater();
                inflater.Setup();

                var input = compr.AsSpan(0, comprLen);
                var output = uncompr.AsSpan(0, uncomprLen);
                while (true)
                {
                    var (code, message) = inflater.Inflate(
                        ZlibFlushType.None, input, output,
                        out int consumed, out int written);

                    input = input.Slice(consumed);
                    output = output.Slice(written);
                    decompressorTotalBytesOut += written;

                    if (code == ZlibCode.StreamEnd)
                        break;

                    if (code == ZlibCode.NeedDict)
                    {
                        Assert.AreEqual<long>(
                            compressorAdler32, inflater.AdlerChecksum,
                            "Unexpected Dictionary");

                        inflater.SetDictionary(dictionary);

                        code = ZlibCode.Ok;
                        message = null;
                    }

                    Assert.AreEqual(
                        ZlibCode.Ok, code,
                        string.Format("at Inflate/SetInflateDictionary() [{0}]", message));
                }
            }

            int j = 0;
            for (; j < uncompr.Length; j++)
            {
                if (uncompr[j] == 0)
                    break;
            }
            Assert.AreEqual(TextToCompress.Length, j, string.Format("Unequal lengths"));

            int i = 0;
            for (; i < j; i++)
            {
                if (TextToCompress[i] != uncompr[i])
                    break;
            }
            Assert.AreEqual(j, i, string.Format("Non-identical content"));

            TestContext.WriteLine("orig length: {0}", TextToCompress.Length);
            TestContext.WriteLine("compressed length: {0}", compressorTotalBytesOut);
            TestContext.WriteLine("uncompressed length: {0}", decompressorTotalBytesOut);

            var result = Encoding.ASCII.GetString(uncompr, 0, j);
            TestContext.WriteLine("result length: {0}", result.Length);
            TestContext.WriteLine("result of inflate:\n{0}", result);
        }

        [TestMethod]
        public void TestFlushSync()
        {
            int bufferSize = 40000;
            var CompressedBytes = new byte[bufferSize];
            var DecompressedBytes = new byte[bufferSize];

            int lengthSkip = 3;
            string TextToCompress = "This is the text that will be compressed.";
            var BytesToCompress = Encoding.ASCII.GetBytes(TextToCompress);

            int compressorTotalBytesOut = 0;
            {
                var deflater = new Deflater();
                deflater.Setup(CompressionLevel.BestCompression);

                var output = CompressedBytes.AsSpan();

                deflater.Deflate(
                    ZlibFlushType.Full,
                    BytesToCompress.AsSpan(0, lengthSkip),
                    output,
                    out _, out int written);

                output = output.Slice(written);
                compressorTotalBytesOut += written;

                CompressedBytes[3]++; // force an error in first compressed block // dinoch - ??

                var (code, message) = deflater.Deflate(
                    ZlibFlushType.Finish,
                    BytesToCompress.AsSpan(lengthSkip, BytesToCompress.Length - lengthSkip),
                    output,
                    out _, out written);

                compressorTotalBytesOut += written;

                Assert.AreEqual(ZlibCode.StreamEnd, code, string.Format("at Deflate() [{0}]", message));
                deflater.End();
                bufferSize = compressorTotalBytesOut;
            }

            int decompressorTotalBytesOut = 0;
            {
                var inflater = new Inflater();
                inflater.Setup();

                var output = DecompressedBytes.AsSpan();

                inflater.Inflate(
                    ZlibFlushType.None,
                    CompressedBytes.AsSpan(0, 2), output,
                    out _, out int written);

                output = output.Slice(written);
                decompressorTotalBytesOut += written;

                var input = CompressedBytes.AsSpan(2, bufferSize - 2);

                inflater.Sync(input, out int syncConsumed);
                input = input.Slice(syncConsumed);

                bool gotException = false;
                try
                {
                    inflater.Inflate(ZlibFlushType.Finish, input, output, out _, out written);

                    decompressorTotalBytesOut += written;
                }
                catch (ZlibException ex1)
                {
                    TestContext.WriteLine("Got Expected Exception: " + ex1);
                    gotException = true;
                }

                Assert.IsTrue(gotException, "inflate should report DATA_ERROR");
            }

            int j = 0;
            for (; j < DecompressedBytes.Length; j++)
                if (DecompressedBytes[j] == 0)
                    break;

            string result = Encoding.ASCII.GetString(DecompressedBytes, 0, j);

            Assert.AreEqual(TextToCompress.Length, result.Length + lengthSkip, "Strings are unequal lengths");
            Assert.AreEqual(TextToCompress.Substring(lengthSkip), result, "Strings are unequal");

            Console.WriteLine("orig length: {0}", TextToCompress.Length);
            Console.WriteLine("compressed length: {0}", compressorTotalBytesOut);
            Console.WriteLine("uncompressed length: {0}", decompressorTotalBytesOut);
            Console.WriteLine("result length: {0}", result.Length);
            Console.WriteLine("result of inflate:\n(Thi){0}", result);
        }

        [TestMethod]
        public void DirectLargeDeflateInflate()
        {
            int j;
            int bufferSize = 80000;
            var compressedBytes = new byte[bufferSize];
            var workBuffer = new byte[bufferSize / 4];

            var compressor = new ZlibCodec();

            compressor.InitializeDeflate(CompressionLevel.Level1);

            compressor.OutputBuffer = compressedBytes;
            compressor.AvailableBytesOut = compressedBytes.Length;
            compressor.NextOut = 0;
            var rnd = new Random();

            int consumed;
            int written;
            ZlibCode code;
            string? message;

            int compressorTotalBytesIn = 0;
            int compressorTotalBytesOut = 0;

            for (int k = 0; k < 4; k++)
            {
                switch (k)
                {
                    case 0:
                        // At this point, workBuffer is all zeroes, so it should compress very well.
                        break;

                    case 1:
                        // switch to no compression, keep same workBuffer (all zeroes):
                        compressor.SetDeflateParams(
                            CompressionLevel.None, CompressionStrategy.Default,
                            compressor.InputBuffer.AsSpan(compressor.NextIn, compressor.AvailableBytesIn),
                            compressor.OutputBuffer.AsSpan(compressor.NextOut, compressor.AvailableBytesOut),
                            out consumed, out written);

                        compressor.NextIn += consumed;
                        compressorTotalBytesIn += consumed;
                        compressor.NextOut += written;
                        compressorTotalBytesOut += written;
                        compressor.AvailableBytesIn -= consumed;
                        compressor.AvailableBytesOut -= written;
                        break;

                    case 2:
                        // Insert data into workBuffer, and switch back to compressing mode.
                        // we'll use lengths of the same random byte:
                        for (int i = 0; i < workBuffer.Length / 1000; i++)
                        {
                            byte b = (byte)rnd.Next();
                            int n = 500 + rnd.Next(500);
                            for (j = 0; j < n; j++)
                                workBuffer[j + i] = b;
                            i += j - 1;
                        }

                        compressor.SetDeflateParams(
                            CompressionLevel.BestCompression, CompressionStrategy.Filtered,
                            compressor.InputBuffer.AsSpan(compressor.NextIn, compressor.AvailableBytesIn),
                            compressor.OutputBuffer.AsSpan(compressor.NextOut, compressor.AvailableBytesOut),
                            out consumed, out written);

                        compressor.NextIn += consumed;
                        compressorTotalBytesIn += consumed;
                        compressor.NextOut += written;
                        compressorTotalBytesOut += written;
                        compressor.AvailableBytesIn -= consumed;
                        compressor.AvailableBytesOut -= written;
                        break;

                    case 3:
                        // insert totally random data into the workBuffer
                        rnd.NextBytes(workBuffer);
                        break;
                }

                compressor.InputBuffer = workBuffer;
                compressor.NextIn = 0;
                compressor.AvailableBytesIn = workBuffer.Length;

                (code, message) = compressor.Deflate(
                    ZlibFlushType.None,
                    compressor.InputBuffer.AsSpan(compressor.NextIn, compressor.AvailableBytesIn),
                    compressor.OutputBuffer.AsSpan(compressor.NextOut, compressor.AvailableBytesOut),
                    out consumed, out written);

                compressor.NextIn += consumed;
                compressorTotalBytesIn += consumed;
                compressor.NextOut += written;
                compressorTotalBytesOut += written;
                compressor.AvailableBytesIn -= consumed;
                compressor.AvailableBytesOut -= written;

                Assert.AreEqual(ZlibCode.Ok, code, string.Format("at Deflate({0}) [{1}]", k, message));

                if (k == 0)
                    Assert.AreEqual(0, compressor.AvailableBytesIn, "Deflate should be greedy.");

                TestContext.WriteLine("Stage {0}: uncompressed/compresssed bytes so far:  ({1,6}/{2,6})",
                    k, compressorTotalBytesIn, compressorTotalBytesOut);
            }

            (code, message) = compressor.Deflate(
                ZlibFlushType.Finish,
                compressor.InputBuffer.AsSpan(compressor.NextIn, compressor.AvailableBytesIn),
                compressor.OutputBuffer.AsSpan(compressor.NextOut, compressor.AvailableBytesOut),
                out consumed, out written);

            Assert.AreEqual(ZlibCode.StreamEnd, code, string.Format("at Deflate() [{0}]", message));

            compressor.NextIn += consumed;
            compressorTotalBytesIn += consumed;
            compressor.NextOut += written;
            compressorTotalBytesOut += written;
            compressor.AvailableBytesIn -= consumed;
            compressor.AvailableBytesOut -= written;

            compressor.EndDeflate();

            TestContext.WriteLine("Final: uncompressed/compressed bytes: ({0,6},{1,6})",
                compressorTotalBytesIn, compressorTotalBytesOut);

            var decompressor = new ZlibCodec(CompressionMode.Decompress);

            decompressor.InputBuffer = compressedBytes;
            decompressor.NextIn = 0;
            decompressor.AvailableBytesIn = bufferSize;

            int decompressorTotalBytesOut = 0;

            // upon inflating, we overwrite the decompressedBytes buffer repeatedly
            int nCycles = 0;
            while (true)
            {
                decompressor.OutputBuffer = workBuffer;
                decompressor.NextOut = 0;
                decompressor.AvailableBytesOut = workBuffer.Length;

                (code, message) = decompressor.Inflate(ZlibFlushType.None,
                    decompressor.InputBuffer.AsSpan(decompressor.NextIn, decompressor.AvailableBytesIn),
                    decompressor.OutputBuffer.AsSpan(decompressor.NextOut, decompressor.AvailableBytesOut),
                    out consumed, out written);

                decompressor.NextIn += consumed;
                decompressor.NextOut += written;
                decompressorTotalBytesOut += written;
                decompressor.AvailableBytesIn -= consumed;
                decompressor.AvailableBytesOut -= written;

                nCycles++;

                if (code == ZlibCode.StreamEnd)
                    break;

                Assert.AreEqual(ZlibCode.Ok, code, string.Format("at Inflate() [{0}] TotalBytesOut={1}",
                    message, decompressorTotalBytesOut));
            }

            decompressor.EndInflate();

            Assert.AreEqual(4 * workBuffer.Length, decompressorTotalBytesOut);

            TestContext.WriteLine("compressed length: {0}", compressorTotalBytesOut);
            TestContext.WriteLine("decompressed length (expected): {0}", 4 * workBuffer.Length);
            TestContext.WriteLine("decompressed length (actual)  : {0}", decompressorTotalBytesOut);
            TestContext.WriteLine("decompression cycles: {0}", nCycles);
        }

        [TestMethod]
        public void ZlibStream_CompressWhileWriting()
        {
            MemoryStream msSinkCompressed;
            MemoryStream msSinkDecompressed;
            ZlibStream zOut;

            // first, compress:
            msSinkCompressed = new MemoryStream();
            zOut = new ZlibStream(msSinkCompressed, CompressionLevel.BestCompression, true);
            CopyStream(StringToMemoryStream(IhaveaDream), zOut);
            zOut.Close();

            // at this point, msSinkCompressed contains the compressed bytes

            // now, decompress:
            msSinkDecompressed = new MemoryStream();
            zOut = new ZlibStream(msSinkDecompressed, CompressionMode.Decompress);
            msSinkCompressed.Position = 0;
            CopyStream(msSinkCompressed, zOut);

            string result = MemoryStreamToString(msSinkDecompressed);
            TestContext.WriteLine("decompressed: {0}", result);
            Assert.AreEqual(IhaveaDream, result);
        }



        [TestMethod]
        public void ZlibStream_CompressWhileReading_wi8557()
        {
            // first, compress:
            var msSinkCompressed = new MemoryStream();
            var zIn = new ZlibStream(
                StringToMemoryStream(WhatWouldThingsHaveBeenLike),
                CompressionLevel.BestCompression,
                true);
            CopyStream(zIn, msSinkCompressed);

            // At this point, msSinkCompressed contains the compressed bytes.
            // Now, decompress:
            var msSinkDecompressed = new MemoryStream();
            var zOut = new ZlibStream(msSinkDecompressed, CompressionMode.Decompress);
            msSinkCompressed.Position = 0;
            CopyStream(msSinkCompressed, zOut);

            string result = MemoryStreamToString(msSinkDecompressed);
            TestContext.WriteLine("decompressed: {0}", result);
            Assert.AreEqual(WhatWouldThingsHaveBeenLike, result);
        }



        [TestMethod]
        public void DirectCodec()
        {
            int sz = Rnd.Next(50000) + 50000;
            string fileName = Path.Combine(TestTmpDir, "Zlib_CodecTest.txt");
            CreateAndFillFileText(fileName, sz);

            byte[] UncompressedBytes = File.ReadAllBytes(fileName);

            foreach (CompressionLevel level in Enum.GetValues(typeof(CompressionLevel)))
            {
                TestContext.WriteLine("\n\n+++++++++++++++++++++++++++++++++++++++++++++++++++++++");
                TestContext.WriteLine("trying compression level '{0}'", level.ToString());

                byte[] CompressedBytes = DeflateBuffer(UncompressedBytes, level);
                byte[] DecompressedBytes = InflateBuffer(CompressedBytes, UncompressedBytes.Length);
                CompareBuffers(UncompressedBytes, DecompressedBytes);
            }
        }

        private byte[] InflateBuffer(byte[] b, int length)
        {
            int bufferSize = 1024;
            var buffer = new byte[bufferSize];
            var DecompressedBytes = new byte[length];
            var ms = new MemoryStream(DecompressedBytes);

            TestContext.WriteLine("\n============================================");
            TestContext.WriteLine("Size of Buffer to Inflate: {0} bytes.", b.Length);

            var decompressor = new ZlibCodec();
            decompressor.InitializeInflate();

            decompressor.InputBuffer = b;
            decompressor.NextIn = 0;
            decompressor.AvailableBytesIn = b.Length;

            decompressor.OutputBuffer = buffer;

            int decompressorTotalBytesOut = 0;

            for (int pass = 0; pass < 2; pass++)
            {
                ZlibFlushType flush = (pass == 0)
                    ? ZlibFlushType.None
                    : ZlibFlushType.Finish;
                do
                {
                    decompressor.NextOut = 0;
                    decompressor.AvailableBytesOut = buffer.Length;

                    var (code, message) = decompressor.Inflate(
                        flush,
                        decompressor.InputBuffer.AsSpan(decompressor.NextIn, decompressor.AvailableBytesIn),
                        decompressor.OutputBuffer.AsSpan(decompressor.NextOut, decompressor.AvailableBytesOut),
                        out int consumed, out int written);

                    decompressor.NextIn += consumed;
                    decompressor.NextOut += written;
                    decompressorTotalBytesOut += written;
                    decompressor.AvailableBytesIn -= consumed;
                    decompressor.AvailableBytesOut -= written;

                    if (code != ZlibCode.Ok && code != ZlibCode.StreamEnd)
                        throw new Exception("inflating: " + message);

                    if (buffer.Length - decompressor.AvailableBytesOut > 0)
                        ms.Write(decompressor.OutputBuffer, 0, buffer.Length - decompressor.AvailableBytesOut);
                }
                while (decompressor.AvailableBytesIn > 0 || decompressor.AvailableBytesOut == 0);
            }

            decompressor.EndInflate();
            TestContext.WriteLine("TBO({0}).", decompressorTotalBytesOut);
            return DecompressedBytes;
        }



        private void CompareBuffers(byte[] a, byte[] b)
        {
            TestContext.WriteLine("\n============================================");
            TestContext.WriteLine("Comparing...");

            if (a.Length != b.Length)
                throw new Exception(string.Format("not equal size ({0}!={1})", a.Length, b.Length));

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    throw new Exception("not equal at index " + i);
            }
        }



        private byte[] DeflateBuffer(byte[] b, CompressionLevel level)
        {
            int bufferSize = 1024;
            var buffer = new byte[bufferSize];
            var compressor = new ZlibCodec();

            TestContext.WriteLine("\n============================================");
            TestContext.WriteLine("Size of Buffer to Deflate: {0} bytes.", b.Length);
            var ms = new MemoryStream();

            compressor.InitializeDeflate(level);

            compressor.InputBuffer = b;
            compressor.NextIn = 0;
            compressor.AvailableBytesIn = b.Length;

            compressor.OutputBuffer = buffer;

            int compressorTotalBytesOut = 0;

            for (int pass = 0; pass < 2; pass++)
            {
                ZlibFlushType flush = (pass == 0)
                    ? ZlibFlushType.None
                    : ZlibFlushType.Finish;
                do
                {
                    compressor.NextOut = 0;
                    compressor.AvailableBytesOut = buffer.Length;

                    var (code, message) = compressor.Deflate(
                        flush,
                        compressor.InputBuffer.AsSpan(compressor.NextIn, compressor.AvailableBytesIn),
                        compressor.OutputBuffer.AsSpan(compressor.NextOut, compressor.AvailableBytesOut),
                        out int consumed, out int written);

                    compressor.NextIn += consumed;
                    compressor.NextOut += written;
                    compressorTotalBytesOut += written;
                    compressor.AvailableBytesIn -= consumed;
                    compressor.AvailableBytesOut -= written;

                    if (code != ZlibCode.Ok && code != ZlibCode.StreamEnd)
                        throw new Exception("deflating: " + message);

                    if (buffer.Length - compressor.AvailableBytesOut > 0)
                        ms.Write(compressor.OutputBuffer, 0, buffer.Length - compressor.AvailableBytesOut);
                }
                while (compressor.AvailableBytesIn > 0 || compressor.AvailableBytesOut == 0);
            }

            compressor.EndDeflate();
            Console.WriteLine("TBO({0}).", compressorTotalBytesOut);

            ms.Seek(0, SeekOrigin.Begin);
            byte[] c = new byte[compressorTotalBytesOut];
            ms.Read(c, 0, c.Length);
            return c;
        }


        [TestMethod]
        public void GZipStream_FileName_And_Comments()
        {
            // select the name of the zip file
            string FileToCompress = Path.Combine(TestTmpDir, "Zlib_GZipStream.dat");
            Assert.IsFalse(File.Exists(FileToCompress), "The temporary zip file '{0}' already exists.", FileToCompress);
            byte[] working = new byte[WORKING_BUFFER_SIZE];
            int n = -1;

            int sz = Rnd.Next(21000) + 15000;
            TestContext.WriteLine("  Creating file: {0} sz({1})", FileToCompress, sz);
            CreateAndFillFileText(FileToCompress, sz);

            var fi1 = new FileInfo(FileToCompress);
            int crc1 = DoCrc(FileToCompress);
            Span<byte> buffer = new byte[1024];

            // four trials, all combos of FileName and Comment null or not null.
            for (int k = 0; k < 4; k++)
            {
                string CompressedFile = string.Format("{0}-{1}.compressed", FileToCompress, k);

                using (FileStream input = File.OpenRead(FileToCompress))
                {
                    using var raw = new FileStream(CompressedFile, FileMode.Create);
                    using var compressor = new GZipStream(
                        raw, CompressionLevel.BestCompression, true);

                    // FileName is optional metadata in the GZip bytestream
                    if (k % 2 == 1)
                        compressor.FileName = FileToCompress;

                    // Comment is optional metadata in the GZip bytestream
                    if (k > 2)
                        compressor.Comment = "Compressing: " + FileToCompress;

                    n = -1;
                    while (n != 0)
                    {
                        if (n > 0)
                            compressor.Write(buffer.Slice(0, n));

                        n = input.Read(buffer);
                    }
                }

                var fi2 = new FileInfo(CompressedFile);

                Assert.IsTrue(
                    fi1.Length > fi2.Length,
                    string.Format("Compressed File is not smaller, trial {0} ({1}!>{2})", k, fi1.Length, fi2.Length));


                // decompress twice:
                // once with System.IO.Compression.GZipStream and once with Ionic.Zlib.GZipStream
                for (int j = 0; j < 2; j++)
                {
                    using var input = File.OpenRead(CompressedFile);

                    Stream? decompressor = null;
                    try
                    {
                        decompressor = j switch
                        {
                            0 => new GZipStream(input, CompressionMode.Decompress, true),
                            1 => new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress, true),
                            _ => throw new InvalidOperationException(),
                        };

                        string DecompressedFile = string.Format("{0}.{1}.decompressed", CompressedFile, (j == 0) ? "Ionic" : "BCL");
                        TestContext.WriteLine("........{0} ...", Path.GetFileName(DecompressedFile));

                        using (var s2 = File.Create(DecompressedFile))
                        {
                            n = -1;
                            while (n != 0)
                            {
                                n = decompressor.Read(working, 0, working.Length);
                                if (n > 0)
                                    s2.Write(working, 0, n);
                            }
                        }

                        int crc2 = DoCrc(DecompressedFile);
                        Assert.AreEqual(crc1, crc2);

                    }
                    finally
                    {
                        decompressor?.Dispose();
                    }
                }
            }
        }


        [TestMethod]
        public void GZipStream_ByteByByte_CheckCrc()
        {
            // select the name of the zip file
            string FileToCompress = Path.Combine(TestTmpDir, "Zlib_GZipStream_ByteByByte.dat");
            Assert.IsFalse(File.Exists(FileToCompress), "The temporary zip file '{0}' already exists.", FileToCompress);
            byte[] working = new byte[WORKING_BUFFER_SIZE];
            int n = -1;

            int sz = Rnd.Next(21000) + 15000;
            TestContext.WriteLine("  Creating file: {0} sz({1})", FileToCompress, sz);
            CreateAndFillFileText(FileToCompress, sz);

            var fi1 = new FileInfo(FileToCompress);
            int crc1 = DoCrc(FileToCompress);

            // four trials, all combos of FileName and Comment null or not null.
            for (int k = 0; k < 4; k++)
            {
                string CompressedFile = string.Format("{0}-{1}.compressed", FileToCompress, k);

                using (Stream input = File.OpenRead(FileToCompress))
                {
                    using var raw = new FileStream(CompressedFile, FileMode.Create);
                    using var compressor = new GZipStream(
                        raw, CompressionLevel.BestCompression, true);

                    // FileName is optional metadata in the GZip bytestream
                    if (k % 2 == 1)
                        compressor.FileName = FileToCompress;

                    // Comment is optional metadata in the GZip bytestream
                    if (k > 2)
                        compressor.Comment = "Compressing: " + FileToCompress;

                    byte[] buffer = new byte[1024];
                    n = -1;
                    while (n != 0)
                    {
                        if (n > 0)
                        {
                            for (int i = 0; i < n; i++)
                                compressor.WriteByte(buffer[i]);
                        }

                        n = input.Read(buffer, 0, buffer.Length);
                    }
                }

                var fi2 = new FileInfo(CompressedFile);

                Assert.IsTrue(
                    fi1.Length > fi2.Length,
                    string.Format("Compressed File is not smaller, trial {0} ({1}!>{2})", k, fi1.Length, fi2.Length));


                // decompress twice:
                // once with System.IO.Compression.GZipStream and once with Ionic.Zlib.GZipStream
                for (int j = 0; j < 2; j++)
                {
                    using var input = File.OpenRead(CompressedFile);

                    Stream decompressor = null;
                    try
                    {
                        switch (j)
                        {
                            case 0:
                                decompressor = new GZipStream(input, CompressionMode.Decompress, true);
                                break;
                            case 1:
                                decompressor = new System.IO.Compression.GZipStream(
                                    input, System.IO.Compression.CompressionMode.Decompress, true);
                                break;
                        }

                        string DecompressedFile =
                            string.Format("{0}.{1}.decompressed", CompressedFile, (j == 0) ? "Ionic" : "BCL");

                        TestContext.WriteLine("........{0} ...", Path.GetFileName(DecompressedFile));

                        using (var s2 = File.Create(DecompressedFile))
                        {
                            n = -1;
                            while (n != 0)
                            {
                                n = decompressor.Read(working, 0, working.Length);
                                if (n > 0)
                                    s2.Write(working, 0, n);
                            }
                        }

                        int crc2 = DoCrc(DecompressedFile);
                        Assert.AreEqual(crc1, crc2);

                    }
                    finally
                    {
                        if (decompressor as GZipStream != null)
                        {
                            var gz = (GZipStream)decompressor;
                            gz.Close(); // sets the final CRC
                            Assert.AreEqual(gz.Crc32, crc1);
                        }

                        if (decompressor != null)
                            decompressor.Dispose();
                    }
                }
            }
        }


        [TestMethod]
        public void GZipStream_DecompressEmptyStream()
        {
            DecompressEmptyStream(typeof(GZipStream));
        }


        [TestMethod]
        public void ZlibStream_DecompressEmptyStream()
        {
            DecompressEmptyStream(typeof(ZlibStream));
        }

        private void DecompressEmptyStream(Type t)
        {
            byte[] working = new byte[WORKING_BUFFER_SIZE];

            // once politely, and the 2nd time through, try to read after EOF
            for (int m = 0; m < 2; m++)
            {
                using var ms1 = new MemoryStream();
                object[] args = { ms1, CompressionMode.Decompress, false };
                using var decompressor = (Stream)Activator.CreateInstance(t, args);
                using var ms2 = new MemoryStream();

                int n = -1;
                while (n != 0)
                {
                    n = decompressor.Read(working, 0, working.Length);
                    if (n > 0)
                        ms2.Write(working, 0, n);
                }

                // we know there is no more data.  Want to insure it does
                // not throw.
                if (m == 1)
                    n = decompressor.Read(working, 0, working.Length);


                Assert.AreEqual(ms2.Length, 0L);
            }
        }


        [TestMethod]
        public void DeflateStream_InMemory()
        {
            string TextToCompress = UntilHeExtends;

            CompressionLevel[] levels = {
                CompressionLevel.Level0,
                CompressionLevel.Level1,
                CompressionLevel.Default,
                CompressionLevel.Level7,
                CompressionLevel.BestCompression
            };

            var ms = new MemoryStream();

            // compress with various Ionic levels, and System.IO.Compression (default level)
            for (int k = 0; k < levels.Length + 1; k++)
            {
                ms.Position = 0;
                ms.SetLength(0);

                Stream compressor;
                if (k == levels.Length)
                {
                    compressor = new System.IO.Compression.DeflateStream(
                        ms, System.IO.Compression.CompressionMode.Compress, true);
                }
                else
                {
                    compressor = new DeflateStream(ms, levels[k], true);
                    TestContext.WriteLine("using level: {0}", levels[k].ToString());
                }

                TestContext.WriteLine("Text to compress is {0} bytes: '{1}'",
                                      TextToCompress.Length, TextToCompress);
                TestContext.WriteLine("using compressor: {0}", compressor.GetType().FullName);

                var sw = new StreamWriter(compressor, Encoding.ASCII);
                sw.Write(TextToCompress);
                sw.Close();

                var a = ms.ToArray();
                TestContext.WriteLine("Compressed stream is {0} bytes long", a.Length);

                // de-compress with both Ionic and System.IO.Compression
                for (int j = 0; j < 2; j++)
                {
                    var slow = new MySlowMemoryStream(a); // want to force EOF
                    Stream decompressor = null;

                    switch (j)
                    {
                        case 0:
                            decompressor = new DeflateStream(slow, CompressionMode.Decompress, false);
                            break;
                        case 1:
                            decompressor = new System.IO.Compression.DeflateStream(
                                slow, System.IO.Compression.CompressionMode.Decompress, false);
                            break;
                    }

                    TestContext.WriteLine("using decompressor: {0}", decompressor.GetType().FullName);

                    var sr = new StreamReader(decompressor, Encoding.ASCII);
                    string DecompressedText = sr.ReadToEnd();

                    TestContext.WriteLine("Read {0} characters: '{1}'", DecompressedText.Length, DecompressedText);
                    TestContext.WriteLine("\n");
                    Assert.AreEqual(TextToCompress, DecompressedText);
                }
            }
        }



        [TestMethod]
        public void CloseTwice()
        {
            string TextToCompress = LetMeDoItNow;

            for (int i = 0; i < 3; i++)
            {
                var ms1 = new MemoryStream();

                Stream compressor = null;
                switch (i)
                {
                    case 0:
                        compressor = new DeflateStream(
                            ms1, CompressionLevel.BestCompression, false);
                        break;
                    case 1:
                        compressor = new GZipStream(ms1, CompressionMode.Compress, false);
                        break;
                    case 2:
                        compressor = new ZlibStream(ms1, CompressionMode.Compress, false);
                        break;
                }

                TestContext.WriteLine("Text to compress is {0} bytes: '{1}'",
                                      TextToCompress.Length, TextToCompress);
                TestContext.WriteLine("using compressor: {0}", compressor.GetType().FullName);

                var sw = new StreamWriter(compressor, Encoding.ASCII);
                sw.Write(TextToCompress);
                sw.Close(); // implicitly closes compressor
                sw.Close();// implicitly closes compressor, again

                compressor.Close(); // explicitly closes compressor
                var a = ms1.ToArray();
                TestContext.WriteLine("Compressed stream is {0} bytes long", a.Length);

                var ms2 = new MemoryStream(a);
                Stream decompressor = null;

                switch (i)
                {
                    case 0:
                        decompressor = new DeflateStream(ms2, CompressionMode.Decompress, false);
                        break;
                    case 1:
                        decompressor = new GZipStream(ms2, CompressionMode.Decompress, false);
                        break;
                    case 2:
                        decompressor = new ZlibStream(ms2, CompressionMode.Decompress, false);
                        break;
                }

                TestContext.WriteLine("using decompressor: {0}", decompressor.GetType().FullName);

                var sr = new StreamReader(decompressor, Encoding.ASCII);
                string DecompressedText = sr.ReadToEnd();

                // verify that multiple calls to Close() do not throw
                sr.Close();
                sr.Close();
                decompressor.Close();

                TestContext.WriteLine("Read {0} characters: '{1}'", DecompressedText.Length, DecompressedText);
                TestContext.WriteLine("\n");
                Assert.AreEqual(TextToCompress, DecompressedText);
            }
        }


        [TestMethod]
        public void Streams_VariousSizes()
        {
            byte[] working = new byte[WORKING_BUFFER_SIZE];
            int n = -1;
            int[] Sizes = { 8000, 88000, 188000, 388000, /*580000, 1580000*/ };

            for (int p = 0; p < Sizes.Length; p++)
            {
                // both binary and text files
                for (int m = 0; m < 2; m++)
                {
                    int sz = Rnd.Next(Sizes[p]) + Sizes[p];
                    string FileToCompress = Path.Combine(
                        TestTmpDir,
                        string.Format("Zlib_Streams.{0}.{1}", sz, (m == 0) ? "txt" : "bin"));

                    Assert.IsFalse(File.Exists(FileToCompress), "The temporary file '{0}' already exists.", FileToCompress);
                    TestContext.WriteLine("Creating file {0}   {1} bytes", FileToCompress, sz);

                    if (m == 0)
                        CreateAndFillFileText(FileToCompress, sz);
                    else
                        CreateAndFillBinary(FileToCompress, sz, false);

                    int crc1 = DoCrc(FileToCompress);
                    TestContext.WriteLine("Initial CRC: 0x{0:X8}", crc1);

                    // try both GZipStream and DeflateStream
                    for (int k = 0; k < 2; k++)
                    {
                        // compress with Ionic and System.IO.Compression
                        for (int i = 0; i < 2; i++)
                        {
                            string CompressedFileRoot =
                                string.Format("{0}.{1}.{2}.compressed", FileToCompress,
                                    (k == 0) ? "GZIP" : "DEFLATE",
                                    (i == 0) ? "Ionic" : "BCL");

                            int x = k + i * 2;
                            int z = (x == 0) ? 4 : 1;
                            // why 4 trials??   (only for GZIP and Ionic)
                            for (int h = 0; h < z; h++)
                            {
                                string CompressedFile = (x == 0)
                                    ? CompressedFileRoot + ".trial" + h
                                    : CompressedFileRoot;

                                using (var raw = File.Create(CompressedFile))
                                {
                                    Stream compressor = null;
                                    try
                                    {
                                        switch (x)
                                        {
                                            case 0: // k == 0, i == 0
                                                compressor = new GZipStream(raw, CompressionMode.Compress, true);
                                                break;
                                            case 1: // k == 1, i == 0
                                                compressor = new DeflateStream(raw, CompressionMode.Compress, true);
                                                break;

                                            case 2: // k == 0, i == 1
                                                compressor = new System.IO.Compression.GZipStream(
                                                    raw, System.IO.Compression.CompressionMode.Compress, true);
                                                break;

                                            case 3: // k == 1, i == 1
                                                compressor = new System.IO.Compression.DeflateStream(
                                                    raw, System.IO.Compression.CompressionMode.Compress, true);
                                                break;
                                        }
                                        //TestContext.WriteLine("Compress with: {0} ..", compressor.GetType().FullName);

                                        TestContext.WriteLine("........{0} ...", Path.GetFileName(CompressedFile));

                                        if (x == 0 && h != 0)
                                        {
                                            var gzip = compressor as GZipStream;

                                            if (h % 2 == 1)
                                                gzip.FileName = FileToCompress;

                                            if (h > 2)
                                                gzip.Comment = "Compressing: " + FileToCompress;
                                        }

                                        n = -1;
                                        using var input = File.OpenRead(FileToCompress);
                                        while ((n = input.Read(working, 0, working.Length)) != 0)
                                        {
                                            compressor.Write(working, 0, n);
                                        }
                                    }
                                    finally
                                    {
                                        if (compressor != null)
                                            compressor.Dispose();
                                    }
                                }

                                // now, decompress with Ionic and System.IO.Compression
                                // for (int j = 0; j < 2; j++)
                                for (int j = 1; j >= 0; j--)
                                {
                                    using var input = File.OpenRead(CompressedFile);
                                    Stream decompressor = null;
                                    try
                                    {
                                        int w = k + j * 2;
                                        switch (w)
                                        {
                                            case 0: // k == 0, j == 0
                                                decompressor = new GZipStream(input, CompressionMode.Decompress, true);
                                                break;
                                            case 1: // k == 1, j == 0
                                                decompressor = new DeflateStream(input, CompressionMode.Decompress, true);
                                                break;

                                            case 2: // k == 0, j == 1
                                                decompressor = new System.IO.Compression.GZipStream(
                                                    input, System.IO.Compression.CompressionMode.Decompress, true);
                                                break;

                                            case 3: // k == 1, j == 1
                                                decompressor = new System.IO.Compression.DeflateStream(
                                                    input, System.IO.Compression.CompressionMode.Decompress, true);
                                                break;
                                        }

                                        //TestContext.WriteLine("Decompress: {0} ...", decompressor.GetType().FullName);
                                        string DecompressedFile =
                                            string.Format("{0}.{1}.decompressed", CompressedFile, (j == 0) ? "Ionic" : "BCL");

                                        TestContext.WriteLine("........{0} ...", Path.GetFileName(DecompressedFile));

                                        using (var s2 = File.Create(DecompressedFile))
                                        {
                                            n = -1;
                                            while (n != 0)
                                            {
                                                n = decompressor.Read(working, 0, working.Length);
                                                if (n > 0)
                                                    s2.Write(working, 0, n);
                                            }
                                        }

                                        int crc2 = DoCrc(DecompressedFile);
                                        Assert.AreEqual((uint)crc1, (uint)crc2);

                                    }
                                    finally
                                    {
                                        if (decompressor != null)
                                            decompressor.Dispose();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            TestContext.WriteLine("Done.");
        }



        private void PerformTrialWi8870(byte[] buffer)
        {
            TestContext.WriteLine("Original");

            byte[] compressedBytes = null;
            using (var ms1 = new MemoryStream())
            {
                using (var compressor = new DeflateStream(ms1, CompressionMode.Compress, false))
                {
                    compressor.Write(buffer, 0, buffer.Length);
                }
                compressedBytes = ms1.ToArray();
            }

            TestContext.WriteLine("Compressed {0} bytes into {1} bytes",
                                  buffer.Length, compressedBytes.Length);

            byte[] decompressed = null;
            using (var ms2 = new MemoryStream())
            {
                using (var deflateStream = new DeflateStream(ms2, CompressionMode.Decompress, false))
                {
                    deflateStream.Write(compressedBytes, 0, compressedBytes.Length);
                }
                decompressed = ms2.ToArray();
            }

            TestContext.WriteLine("Decompressed");


            bool check = true;
            if (buffer.Length != decompressed.Length)
            {
                TestContext.WriteLine("Different lengths.");
                check = false;
            }
            else
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i] != decompressed[i])
                    {
                        TestContext.WriteLine("byte {0} differs", i);
                        check = false;
                        break;
                    }
                }
            }

            Assert.IsTrue(check, "Data check failed.");
        }




        private byte[] RandomizeBuffer(int length)
        {
            byte[] buffer = new byte[length];
            int mod1 = 86 + Rnd.Next(46) / 2 + 1;
            int mod2 = 50 + Rnd.Next(72) / 2 + 1;
            for (int i = 0; i < length; i++)
            {
                if (i > 200)
                    buffer[i] = (byte)(i % mod1);
                else if (i > 100)
                    buffer[i] = (byte)(i % mod2);
                else if (i > 42)
                    buffer[i] = (byte)(i % 33);
                else
                    buffer[i] = (byte)i;
            }
            return buffer;
        }



        [TestMethod]
        public void DeflateStream_wi8870()
        {
            for (int j = 0; j < 1000; j++)
            {
                byte[] buffer = RandomizeBuffer(117 + (Rnd.Next(3) * 100));
                PerformTrialWi8870(buffer);
            }
        }

        [TestMethod]
        public void ParallelDeflateStream_ReadLength()
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            int sz = 128 * 1024 * Rnd.Next(14, 28); // 128k = default buffer size;

            using var s = new MemoryStream();
            TestContext.WriteLine("{0}: Compressing data...", sw.Elapsed);
            using (var compressor = new ParallelDeflateOutputStream(s, true))
                compressor.Write(new byte[sz], 0, sz);

            s.Position = 0;
            TestContext.WriteLine("{0}: Trying to decompress...", sw.Elapsed);

            using var decompressor = new DeflateStream(s, CompressionMode.Decompress, true);

            int bread = decompressor.Read(new byte[sz], 0, sz);
            Assert.AreEqual(sz, bread, "Size of decompressed bytes does not match size of input bytes");

            TestContext.WriteLine("{0}: Done...", sw.Elapsed);
        }


        [TestMethod]
        public void ParallelDeflateStream_CrcChecked()
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            int sz = 256 * 1024 + Rnd.Next(120000);
            string FileToCompress = Path.Combine(TestTmpDir, string.Format("Zlib_ParallelDeflateStream.{0}.txt", sz));

            CreateAndFillFileText(FileToCompress, sz);
            TestContext.WriteLine("{0}: Created file: {1}", sw.Elapsed, FileToCompress);

            byte[] original = File.ReadAllBytes(FileToCompress);

            int crc1 = DoCrc(FileToCompress);
            TestContext.WriteLine("{0}: Original CRC: {1:X8}", sw.Elapsed, crc1);

            int crc2 = 0;

            long originalLength;
            var ms1 = new MemoryStream();
            {
                using (FileStream fs1 = File.OpenRead(FileToCompress))
                {
                    originalLength = fs1.Length;

                    var compressor = new ParallelDeflateOutputStream(ms1, true);
                    using (compressor)
                        fs1.CopyTo(compressor);

                    TestContext.WriteLine("{0}: CRC by Input: {1:X8}", sw.Elapsed, compressor.Crc32);
                    crc2 = compressor.Crc32;
                }
                ms1.Seek(0, SeekOrigin.Begin);
            }
            Assert.AreEqual(crc1, crc2, "Compressor reported invalid Crc32.");

            TestContext.WriteLine(
                "{0}: Compressed {1} bytes into {2} bytes",
                sw.Elapsed, originalLength, ms1.Length);

            byte[] decompressedBytes = null;
            var crc3 = new Crc32();

            using (var ms2 = new MemoryStream())
            {
                using (var decompressor = new DeflateStream(ms1, CompressionMode.Decompress, false))
                    crc3.Slurp(decompressor, ms2);

                TestContext.WriteLine("{0}: Decompressed", sw.Elapsed);
                TestContext.WriteLine("{0}: Decompressed length: {1}", sw.Elapsed, ms2.Length);

                decompressedBytes = ms2.ToArray();
                TestContext.WriteLine("{0}: Decompressed CRC: {1:X8}", sw.Elapsed, crc3);
            }
            Assert.AreEqual(crc1, crc3.Result, "Decompressed yields invalid Crc32.");

            TestContext.WriteLine("{0}: Checking...", sw.Elapsed);

            bool check = true;
            if (originalLength != decompressedBytes.Length)
            {
                TestContext.WriteLine("Different lengths.");
                check = false;
            }
            else
            {
                for (int i = 0; i < decompressedBytes.Length; i++)
                {
                    if (original[i] != decompressedBytes[i])
                    {
                        TestContext.WriteLine("byte {0} differs", i);
                        check = false;
                        break;
                    }
                }
            }

            Assert.IsTrue(check, "Data check failed");
            TestContext.WriteLine("{0}: Done...", sw.Elapsed);
        }


        [TestMethod]
        public void TestAdler32()
        {
            // create a buffer full of 0xff's
            var buffer = new byte[2048 * 4];
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 255;
            };

            uint goal = 4104380882;
            var testAdler = new Action<int>(chunk =>
            {
                var index = 0;
                uint adler = ZlibConstants.InitialAdler32;
                while (index < buffer.Length)
                {
                    var length = Math.Min(buffer.Length - index, chunk);
                    adler = Adler32.Compute(adler, buffer.AsSpan(index, length));
                    index += chunk;
                }
                Assert.AreEqual(adler, goal);
            });

            testAdler(3979);
            testAdler(3980);
            testAdler(3999);
        }

    }


    public class MySlowMemoryStream : MemoryStream
    {
        // ctor
        public MySlowMemoryStream(byte[] bytes) : base(bytes, false) { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException();

            if (count == 0)
                return 0;

            // force stream to read just one byte at a time
            int NextByte = base.ReadByte();
            if (NextByte == -1)
                return 0;

            buffer[offset] = (byte)NextByte;
            return 1;
        }
    }



}
