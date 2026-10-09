using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FastqDetective.Parsing;

namespace FastqDetective.Tests
{
    [TestClass]
    public class ConversionParserTests
    {
        [TestMethod]
        public void ConvertFile_FastqRecords_WritesOnlySequenceLines()
        {
            WithTemporaryPaths((input, output) =>
            {
                File.WriteAllText(input,
                    "@read1\nACGT\n+\n!!!!\n@read2\nTGCA\n+\n####\n", Encoding.ASCII);

                ConversionParser.ConvertFile(input, output);

                Assert.AreEqual("ACGTTGCA", File.ReadAllText(output, Encoding.ASCII));
            });
        }

        [TestMethod]
        public void ConvertFile_AppendsToExistingOutput()
        {
            WithTemporaryPaths((input, output) =>
            {
                File.WriteAllText(input, "@read1\nACGT\n+\n!!!!\n", Encoding.ASCII);
                File.WriteAllText(output, "existing\n", Encoding.ASCII);

                ConversionParser.ConvertFile(input, output);

                Assert.AreEqual("existing\nACGT", File.ReadAllText(output, Encoding.ASCII));
            });
        }

        [TestMethod]
        public void ConvertFile_EmptyInput_LeavesOutputEmpty()
        {
            WithTemporaryPaths((input, output) =>
            {
                File.WriteAllText(input, string.Empty, Encoding.ASCII);

                ConversionParser.ConvertFile(input, output);

                Assert.AreEqual(string.Empty, File.ReadAllText(output, Encoding.ASCII));
            });
        }

        [TestMethod]
        public void ConvertFile_IncompleteRecord_WritesSequenceLineWhenPresent()
        {
            WithTemporaryPaths((input, output) =>
            {
                File.WriteAllText(input, "@read1\nACGT\n", Encoding.ASCII);

                ConversionParser.ConvertFile(input, output);

                Assert.AreEqual("ACGT", File.ReadAllText(output, Encoding.ASCII));
            });
        }

        private static void WithTemporaryPaths(Action<string, string> test)
        {
            var stem = Path.Combine(Path.GetTempPath(), "FastqDetectiveTests_" + Guid.NewGuid().ToString("N"));
            var input = stem + ".fastq";
            var output = stem + ".txt";
            try
            {
                test(input, output);
            }
            finally
            {
                if (File.Exists(input)) File.Delete(input);
                if (File.Exists(output)) File.Delete(output);
            }
        }
    }
}
