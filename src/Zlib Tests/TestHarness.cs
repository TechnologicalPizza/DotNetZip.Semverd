using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ionic.Zlib.Tests
{
    public class TestHarness
    {
        public const int WORKING_BUFFER_SIZE = 1024 * 16;

        #region Weird texts

        internal static string LetMeDoItNow = "I expect to pass through the world but once. Any good therefore that I can do, or any kindness I can show to any creature, let me do it now. Let me not defer it, for I shall not pass this way again. -- Anonymous, although some have attributed it to Stephen Grellet";

        internal static string UntilHeExtends = "Until he extends the circle of his compassion to all living things, man will not himself find peace. - Albert Schweitzer, early 20th-century German Nobel Peace Prize-winning mission doctor and theologian.";

        internal static string WhatWouldThingsHaveBeenLike = "'What would things have been like [in Russia] if during periods of mass arrests people had not simply sat there, paling with terror at every bang on the downstairs door and at every step on the staircase, but understood they had nothing to lose and had boldly set up in the downstairs hall an ambush of half a dozen people?' -- Alexander Solzhenitsyn";

        internal static string GoPlacidly =
            @"Go placidly amid the noise and haste, and remember what peace there may be in silence.

As far as possible, without surrender, be on good terms with all persons. Speak your truth quietly and clearly; and listen to others, even to the dull and the ignorant, they too have their story. Avoid loud and aggressive persons, they are vexations to the spirit.

If you compare yourself with others, you may become vain and bitter; for always there will be greater and lesser persons than yourself. Enjoy your achievements as well as your plans. Keep interested in your own career, however humble; it is a real possession in the changing fortunes of time.

Exercise caution in your business affairs, for the world is full of trickery. But let this not blind you to what virtue there is; many persons strive for high ideals, and everywhere life is full of heroism. Be yourself. Especially, do not feign affection. Neither be cynical about love, for in the face of all aridity and disenchantment it is perennial as the grass.

Take kindly to the counsel of the years, gracefully surrendering the things of youth. Nurture strength of spirit to shield you in sudden misfortune. But do not distress yourself with imaginings. Many fears are born of fatigue and loneliness.

Beyond a wholesome discipline, be gentle with yourself. You are a child of the universe, no less than the trees and the stars; you have a right to be here. And whether or not it is clear to you, no doubt the universe is unfolding as it should.

Therefore be at peace with God, whatever you conceive Him to be, and whatever your labors and aspirations, in the noisy confusion of life, keep peace in your soul.

With all its sham, drudgery and broken dreams, it is still a beautiful world.

Be cheerful. Strive to be happy.

Max Ehrmann c.1920
";


        internal static string IhaveaDream = @"Let us not wallow in the valley of despair, I say to you today, my friends.

And so even though we face the difficulties of today and tomorrow, I still have a dream. It is a dream deeply rooted in the American dream.

I have a dream that one day this nation will rise up and live out the true meaning of its creed: 'We hold these truths to be self-evident, that all men are created equal.'

I have a dream that one day on the red hills of Georgia, the sons of former slaves and the sons of former slave owners will be able to sit down together at the table of brotherhood.

I have a dream that one day even the state of Mississippi, a state sweltering with the heat of injustice, sweltering with the heat of oppression, will be transformed into an oasis of freedom and justice.

I have a dream that my four little children will one day live in a nation where they will not be judged by the color of their skin but by the content of their character.

I have a dream today!

I have a dream that one day, down in Alabama, with its vicious racists, with its governor having his lips dripping with the words of 'interposition' and 'nullification' -- one day right there in Alabama little black boys and black girls will be able to join hands with little white boys and white girls as sisters and brothers.

I have a dream today!

I have a dream that one day every valley shall be exalted, and every hill and mountain shall be made low, the rough places will be made plain, and the crooked places will be made straight; 'and the glory of the Lord shall be revealed and all flesh shall see it together.'2
";

        internal static string LoremIpsum =
"Lorem ipsum dolor sit amet, consectetuer adipiscing elit. Integer " +
"vulputate, nibh non rhoncus euismod, erat odio pellentesque lacus, sit " +
"amet convallis mi augue et odio. Phasellus cursus urna facilisis " +
"quam. Suspendisse nec metus et sapien scelerisque euismod. Nullam " +
"molestie sem quis nisl. Fusce pellentesque, ante sed semper egestas, sem " +
"nulla vestibulum nulla, quis sollicitudin leo lorem elementum " +
"wisi. Aliquam vestibulum nonummy orci. Sed in dolor sed enim ullamcorper " +
"accumsan. Duis vel nibh. Class aptent taciti sociosqu ad litora torquent " +
"per conubia nostra, per inceptos hymenaeos. Sed faucibus, enim sit amet " +
"venenatis laoreet, nisl elit posuere est, ut sollicitudin tortor velit " +
"ut ipsum. Aliquam erat volutpat. Phasellus tincidunt vehicula " +
"eros. Curabitur vitae erat. " +
"\n " +
"Quisque pharetra lacus quis sapien. Duis id est non wisi sagittis " +
"adipiscing. Nulla facilisi. Etiam quam erat, lobortis eu, facilisis nec, " +
"blandit hendrerit, metus. Fusce hendrerit. Nunc magna libero, " +
"sollicitudin non, vulputate non, ornare id, nulla.  Suspendisse " +
"potenti. Nullam in mauris. Curabitur et nisl vel purus vehicula " +
"sodales. Class aptent taciti sociosqu ad litora torquent per conubia " +
"nostra, per inceptos hymenaeos. Cum sociis natoque penatibus et magnis " +
"dis parturient montes, nascetur ridiculus mus. Donec semper, arcu nec " +
"dignissim porta, eros odio tempus pede, et laoreet nibh arcu et " +
"nisl. Morbi pellentesque eleifend ante. Morbi dictum lorem non " +
"ante. Nullam et augue sit amet sapien varius mollis. " +
"\n " +
"Nulla erat lorem, fringilla eget, ultrices nec, dictum sed, " +
"sapien. Aliquam libero ligula, porttitor scelerisque, lobortis nec, " +
"dignissim eu, elit. Etiam feugiat, dui vitae laoreet faucibus, tellus " +
"urna molestie purus, sit amet pretium lorem pede in erat.  Ut non libero " +
"et sapien porttitor eleifend. Vestibulum ante ipsum primis in faucibus " +
"orci luctus et ultrices posuere cubilia Curae; In at lorem et lacus " +
"feugiat iaculis. Nunc tempus eros nec arcu tristique egestas. Quisque " +
"metus arcu, pretium in, suscipit dictum, bibendum sit amet, " +
"mauris. Aliquam non urna. Suspendisse eget diam. Aliquam erat " +
"volutpat. In euismod aliquam lorem. Mauris dolor nisl, consectetuer sit " +
"amet, suscipit sodales, rutrum in, lorem. Nunc nec nisl. Nulla ante " +
"libero, aliquam porttitor, aliquet at, imperdiet sed, diam. Pellentesque " +
"tincidunt nisl et ipsum. Suspendisse purus urna, semper quis, laoreet " +
"in, vestibulum vel, arcu. Nunc elementum eros nec mauris. " +
"\n " +
"Vivamus congue pede at quam. Aliquam aliquam leo vel turpis. Ut " +
"commodo. Integer tincidunt sem a risus. Cras aliquam libero quis " +
"arcu. Integer posuere. Nulla malesuada, wisi ac elementum sollicitudin, " +
"libero libero molestie velit, eu faucibus est ante eu libero. Sed " +
"vestibulum, dolor ac ultricies consectetuer, tellus risus interdum diam, " +
"a imperdiet nibh eros eget mauris. Donec faucibus volutpat " +
"augue. Phasellus vitae arcu quis ipsum ultrices fermentum. Vivamus " +
"ultricies porta ligula. Nullam malesuada. Ut feugiat urna non " +
"turpis. Vivamus ipsum. Vivamus eleifend condimentum risus. Curabitur " +
"pede. Maecenas suscipit pretium tortor. Integer pellentesque. " +
"\n " +
"Mauris est. Aenean accumsan purus vitae ligula. Lorem ipsum dolor sit " +
"amet, consectetuer adipiscing elit. Nullam at mauris id turpis placerat " +
"accumsan. Sed pharetra metus ut ante. Aenean vel urna sit amet ante " +
"pretium dapibus. Sed nulla. Sed nonummy, lacus a suscipit semper, erat " +
"wisi convallis mi, et accumsan magna elit laoreet sem. Nam leo est, " +
"cursus ut, molestie ac, laoreet id, mauris. Suspendisse auctor nibh. " +
"\n";
        #endregion

        protected static string[] LoremIpsumWords { get; private set; }

        protected Dictionary<string, string> TestStrings { get; private set; }
        protected Random Rnd { get; private set; }
        protected string CurrentDir { get; private set; }
        protected string TestTmpDir { get; private set; }

        /// <summary>
        /// Gets or sets the test context which provides
        /// information about and functionality for the current test run.
        /// </summary>
        public TestContext TestContext { get; set; }

        public TestHarness()
        {
            Rnd = new Random(1234);
            TestStrings = new Dictionary<string, string>()
            {
                { "LetMeDoItNow", LetMeDoItNow },
                { "GoPlacidly", GoPlacidly },
                { "IhaveaDream", IhaveaDream },
                { "LoremIpsum", LoremIpsum },
            };
        }

        static TestHarness()
        {
            LoremIpsumWords = LoremIpsum.Split(
                " ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
        }


        /// <summary>
        /// Use TestInitialize to run code before running each test
        /// </summary>
        [TestInitialize()]
        public virtual void TestInitialize()
        {
            CurrentDir = Directory.GetCurrentDirectory();
            Assert.AreNotEqual(Path.GetFileName(CurrentDir), "Temp", "at start");

            string tmpDir = Environment.GetEnvironmentVariable("TEMP");

            TestTmpDir = Path.Combine(
                tmpDir,
                string.Format("Ionic.ZlibTest-{0}.tmp", DateTime.Now.ToString("yyyyMMMdd-HHmmss")));

            Directory.CreateDirectory(TestTmpDir);
            Directory.SetCurrentDirectory(TestTmpDir);
        }


        /// <summary>
        /// Use TestCleanup to run code after each test has run
        /// </summary>
        [TestCleanup()]
        public void TestCleanup()
        {
            Directory.SetCurrentDirectory(Environment.GetEnvironmentVariable("TEMP"));
            Directory.Delete(TestTmpDir, true);
            Assert.AreNotEqual(Path.GetFileName(CurrentDir), "Temp", "at finish");
            Directory.SetCurrentDirectory(CurrentDir);
        }


        #region Helpers

        /// <summary>
        /// Converts a string to a MemoryStream.
        /// </summary>
        public static MemoryStream StringToMemoryStream(string s)
        {
            var enc = Encoding.ASCII;
            int byteCount = enc.GetByteCount(s.ToCharArray(), 0, s.Length);
            byte[] ByteArray = new byte[byteCount];
            int bytesEncodedCount = enc.GetBytes(s, 0, s.Length, ByteArray, 0);
            var ms = new MemoryStream(ByteArray, 0, bytesEncodedCount);
            return ms;
        }

        /// <summary>
        /// Converts a MemoryStream to a string. Makes some assumptions about the content of the stream.
        /// </summary>
        public static string MemoryStreamToString(MemoryStream ms)
        {
            byte[] ByteArray = ms.ToArray();
            var s = Encoding.ASCII.GetString(ByteArray);
            return s;
        }

        public static void CopyStream(Stream src, Stream dest)
        {
            byte[] buffer = new byte[4096];
            int len;
            while ((len = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                dest.Write(buffer, 0, len);
            }
            dest.Flush();
        }

        public static string GetTestDependentDir(string startingPoint, string subdir)
        {
            var location = startingPoint;
            for (int i = 0; i < 3; i++)
                location = Path.GetDirectoryName(location);

            location = Path.Combine(location, subdir);
            return location;
        }

        public string GetContentFilePath(string fileName)
        {
            string path = Path.Combine(CurrentDir, string.Format("Resources\\{0}", fileName));
            Assert.IsTrue(File.Exists(path), "file ({0}) does not exist", path);
            return path;
        }

        internal string Exec(string program, string args)
        {
            return Exec(program, args, true);
        }

        internal string Exec(string program, string args, bool waitForExit)
        {
            return Exec(program, args, waitForExit, true);
        }

        internal string Exec(string program, string args, bool waitForExit, bool emitOutput)
        {
            if (program == null)
                throw new ArgumentException("program");
            if (args == null)
                throw new ArgumentException("args");

            // Microsoft.VisualStudio.TestTools.UnitTesting
            TestContext.WriteLine("running command: {0} {1}", program, args);

            int rc = Exec_NoContext(program, args, waitForExit, out string output);

            if (rc != 0)
                throw new Exception(string.Format("Non-zero RC {0}: {1}", program, output));

            if (emitOutput)
                TestContext.WriteLine("output: {0}", output);
            else
                TestContext.WriteLine("A-OK. (output suppressed)");

            return output;
        }

        internal static int Exec_NoContext(string program, string args, bool waitForExit, out string output)
        {
            var p = new System.Diagnostics.Process
            {
                StartInfo =
                {
                    FileName = program,
                    CreateNoWindow = true,
                    Arguments = args,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                }

            };

            if (waitForExit)
            {
                var sb = new StringBuilder();
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                // must read at least one of the stderr or stdout asynchronously,
                // to avoid deadlock
                Action<object, System.Diagnostics.DataReceivedEventArgs> stdErrorRead = (o, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        sb.Append(e.Data);
                };

                p.ErrorDataReceived += new System.Diagnostics.DataReceivedEventHandler(stdErrorRead);
                p.Start();
                p.BeginErrorReadLine();
                output = p.StandardOutput.ReadToEnd();

                p.WaitForExit();
                if (sb.Length > 0)
                    output += sb.ToString();
                //output = CleanWzzipOut(output); // just in case
                return p.ExitCode;
            }
            else
            {
                p.Start();
            }
            output = "";
            return 0;
        }

        #endregion


        public static int DoCrc(string filename)
        {
            using Stream a = File.OpenRead(filename);
            using var crc = new CrcStream(a);
            byte[] working = new byte[WORKING_BUFFER_SIZE];
            int n = -1;
            while (n != 0)
                n = crc.Read(working, 0, working.Length);
            return crc.CrcChecksum;
        }



        public static void CreateAndFillBinary(string Filename, long size, bool zeroes)
        {
            long bytesRemaining = size;
            var rnd = new Random();
            // fill with binary data
            byte[] Buffer = new byte[20000];
            using var fileStream = new FileStream(Filename, FileMode.Create, FileAccess.Write);
            while (bytesRemaining > 0)
            {
                int sizeOfChunkToWrite = (bytesRemaining > Buffer.Length) ? Buffer.Length : (int)bytesRemaining;
                if (!zeroes)
                    rnd.NextBytes(Buffer);
                fileStream.Write(Buffer, 0, sizeOfChunkToWrite);
                bytesRemaining -= sizeOfChunkToWrite;
            }
            fileStream.Close();
        }


        public static void CreateAndFillFileText(string Filename, long size)
        {
            long bytesRemaining = size;
            var rnd = new Random();
            // fill the file with text data
            using var sw = File.CreateText(Filename);
            do
            {
                // pick a word at random
                string selectedWord = LoremIpsumWords[rnd.Next(LoremIpsumWords.Length)];
                if (bytesRemaining < selectedWord.Length + 1)
                {
                    sw.Write(selectedWord.Substring(0, (int)bytesRemaining));
                    bytesRemaining = 0;
                }
                else
                {
                    sw.Write(selectedWord);
                    sw.Write(" ");
                    bytesRemaining -= selectedWord.Length + 1;
                }
            }
            while (bytesRemaining > 0);
            sw.Close();
        }
    }
}
