using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ionic.Zlib.Tests
{
    //[TestClass]
    public class ToolTest : TestHarness
    {
        [TestMethod]
        public void GZ_Utility()
        {
            var gzbin = GetTestDependentDir(CurrentDir, "..\\Tools");

            var dnzGzipexe = Path.Combine(gzbin, "gzip.exe");
            Assert.IsTrue(File.Exists(dnzGzipexe), "Gzip.exe is missing {0}", dnzGzipexe);

            var unxGzipexe = "\\bin\\gzip.exe";
            Assert.IsTrue(File.Exists(unxGzipexe), "Gzip.exe is missing {0}", unxGzipexe);

            foreach (var key in TestStrings.Keys)
            {
                int count = Rnd.Next(81) + 40;
                TestContext.WriteLine("Doing string {0}", key);
                var s = TestStrings[key];
                var fname = string.Format("Pippo-{0}.txt", key);
                using (var sw = new StreamWriter(File.Create(fname)))
                {
                    for (int k = 0; k < count; k++)
                        sw.WriteLine(s);
                }

                int crcOriginal = DoCrc(fname);

                string args = fname + " -keep -v";
                TestContext.WriteLine("Exec: gzip {0}", args);
                string gzout = Exec(dnzGzipexe, args);

                var gzfile = fname + ".gz";
                Assert.IsTrue(File.Exists(gzfile), "File is missing. {0}", gzfile);

                File.Delete(fname);
                Assert.IsTrue(!File.Exists(fname), "The delete failed. {0}", fname);

                args = "-dfv " + gzfile;
                TestContext.WriteLine("Exec: gzip {0}", args);
                gzout = Exec(unxGzipexe, args);
                Assert.IsTrue(File.Exists(fname), "File is missing. {0}", fname);

                int crcDecompressed = DoCrc(fname);
                Assert.AreEqual(
                    crcOriginal, crcDecompressed,
                    "CRC mismatch {0:X8}!={1:X8}", crcOriginal, crcDecompressed);
            }
        }
    }
}
