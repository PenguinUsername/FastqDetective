using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FastqDetective.Parsing;

namespace FastqDetective.Tests
{
    [TestClass]
    public class MarkerParserTests
    {
        [TestMethod]
        public void ParseFile_RemovesWhitespaceAndNormalizesCase()
        {
            WithTemporaryFile("  Ac GT\tN  " + Environment.NewLine + "Tg ca", path =>
            {
                var markers = MarkerParser.ParseFile(path);

                CollectionAssert.AreEqual(new[] { "acgtn", "tgca" }, markers);
            });
        }

        [TestMethod]
        public void ParseFile_PreservesBlankLinesAsEmptyMarkers()
        {
            WithTemporaryFile("ACGT" + Environment.NewLine + Environment.NewLine + "  " + Environment.NewLine + "TGCA", path =>
            {
                var markers = MarkerParser.ParseFile(path);

                CollectionAssert.AreEqual(new[] { "acgt", "", "", "tgca" }, markers);
            });
        }

        [TestMethod]
        public void ParseFile_EmptyFile_ReturnsEmptyList()
        {
            WithTemporaryFile(string.Empty, path =>
            {
                Assert.AreEqual(0, MarkerParser.ParseFile(path).Count);
            });
        }

        [TestMethod]
        public void ParseFile_MissingFile_ReturnsEmptyList()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".markers");
            Assert.AreEqual(0, MarkerParser.ParseFile(path).Count);
        }

        private static void WithTemporaryFile(string contents, Action<string> test)
        {
            var path = Path.Combine(Path.GetTempPath(), "FastqDetectiveTests_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(path, contents);
                test(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
