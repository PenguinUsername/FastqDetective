using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FastqDetective.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FastqDetective.Tests
{
    [TestClass]
    public class AnalysisParserTests
    {
        [TestMethod]
        public void ParseFile_NullContext_ThrowsArgumentNullException()
        {
            using (var fastq = new TemporaryFastq("@r1", "acgt", "+", "!!!!"))
            using (var cancellation = new CancellationTokenSource())
            {
                Assert.ThrowsException<ArgumentNullException>(() =>
                    AnalysisParser.ParseFile(fastq.Path, 0, 0, 4, 0, 1, null, cancellation));
            }
        }

        [TestMethod]
        public void ParseFile_NullCancellationTokenSource_ThrowsArgumentNullException()
        {
            using (var fastq = new TemporaryFastq("@r1", "acgt", "+", "!!!!"))
            {
                Assert.ThrowsException<ArgumentNullException>(() =>
                    AnalysisParser.ParseFile(fastq.Path, 0, 0, 4, 0, 1, new ParsingContext(), null));
            }
        }

        [TestMethod]
        public void ParseFile_NonPositiveChunkSize_ThrowsArgumentOutOfRangeException()
        {
            using (var fastq = new TemporaryFastq("@r1", "acgt", "+", "!!!!"))
            using (var cancellation = new CancellationTokenSource())
            {
                Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                    AnalysisParser.ParseFile(fastq.Path, 0, 0, 0, 0, 1, new ParsingContext(), cancellation));
            }
        }

        [TestMethod]
        public void ParseFile_InvalidSlidingStep_ThrowsArgumentOutOfRangeException()
        {
            using (var fastq = new TemporaryFastq("@r1", "acgt", "+", "!!!!"))
            using (var cancellation = new CancellationTokenSource())
            {
                Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                    AnalysisParser.ParseFile(fastq.Path, 0, 0, 4, 4, 1, new ParsingContext(), cancellation));
            }
        }

        [TestMethod]
        public void ParseFile_ExactMarkerMatch_ReturnsFullScoreAndCorrectLocus()
        {
            var result = new ParsingResult("acgt");
            var context = new ParsingContext();
            context.parsingResults.Add(result);
            context.Threshold = 0.9;

            using (var fastq = new TemporaryFastq("@r1", "ACGT", "+", "!!!!"))
            using (var cancellation = new CancellationTokenSource())
            {
                AnalysisParser.ParseFile(fastq.Path, 0, 0, 4, 0, 1, context, cancellation);
            }

            var match = result.Matches.Single();
            Assert.AreEqual("0", match.Locus);
            Assert.AreEqual(1.0, match.Score, 0.000001);
            Assert.AreEqual("acgt", match.SequenceChunk);
            Assert.AreEqual(context.TasksStarted, context.TasksResultsReceived);
        }

        [TestMethod]
        public void ParseFile_EqualBestAlignments_KeepsFirstMaximum()
        {
            var result = new ParsingResult("acgt");
            var context = new ParsingContext();
            context.parsingResults.Add(result);
            context.Threshold = 1.0;

            using (var fastq = new TemporaryFastq("@r1", "ACGTACGT", "+", "!!!!!!!!"))
            using (var cancellation = new CancellationTokenSource())
            {
                AnalysisParser.ParseFile(fastq.Path, 0, 0, 8, 0, 1, context, cancellation);
            }

            var match = result.Matches.Single();
            Assert.AreEqual("0", match.Locus);
            Assert.AreEqual(1.0, match.Score, 0.000001);
        }

        [TestMethod]
        public void ParseFile_BelowThreshold_DoesNotReturnMatch()
        {
            var result = new ParsingResult("aaaa");
            var context = new ParsingContext();
            context.parsingResults.Add(result);
            context.Threshold = 0.9;

            using (var fastq = new TemporaryFastq("@r1", "tttt", "+", "!!!!"))
            using (var cancellation = new CancellationTokenSource())
            {
                AnalysisParser.ParseFile(fastq.Path, 0, 0, 4, 0, 1, context, cancellation);
            }

            Assert.AreEqual(0, result.Matches.Count);
            Assert.AreEqual(context.TasksStarted, context.TasksResultsReceived);
        }

        [TestMethod]
        public void ParseFile_PartialFinalChunk_IsEvaluated()
        {
            var result = new ParsingResult("acg");
            var context = new ParsingContext();
            context.parsingResults.Add(result);
            context.Threshold = 1.0;

            using (var fastq = new TemporaryFastq("@r1", "ACG", "+", "!!!"))
            using (var cancellation = new CancellationTokenSource())
            {
                AnalysisParser.ParseFile(fastq.Path, 0, 0, 8, 0, 1, context, cancellation);
            }

            var match = result.Matches.Single();
            Assert.AreEqual("0", match.Locus);
            Assert.AreEqual(1.0, match.Score, 0.000001);
            Assert.AreEqual("acg", match.SequenceChunk);
        }

        [TestMethod]
        public void ParseFile_OverlappingChunks_FindsMarkerInLaterWindow()
        {
            var result = new ParsingResult("cgt");
            var context = new ParsingContext();
            context.parsingResults.Add(result);
            context.Threshold = 1.0;

            using (var fastq = new TemporaryFastq("@r1", "aacgtt", "+", "!!!!!!"))
            using (var cancellation = new CancellationTokenSource())
            {
                // chunk size 4, remainder 2 => step 2; windows are "aacg", "cgtt".
                AnalysisParser.ParseFile(fastq.Path, 0, 0, 4, 2, 2, context, cancellation);
            }

            Assert.IsTrue(result.Matches.Any(m => m.Score >= 1.0));
            Assert.IsTrue(result.Matches.Any(m => m.Locus == "2" && m.SequenceChunk == "cgtt"));
        }

        [TestMethod]
        public void ParseFile_QualitySequenceShorterThanSequence_ProcessesOnlySharedPrefix()
        {
            var result = new ParsingResult("acg");
            var context = new ParsingContext();
            context.parsingResults.Add(result);
            context.Threshold = 1.0;

            using (var fastq = new TemporaryFastq("@r1", "ACGT", "+", "!!!"))
            using (var cancellation = new CancellationTokenSource())
            {
                AnalysisParser.ParseFile(fastq.Path, 0, 0, 8, 0, 1, context, cancellation);
            }

            var match = result.Matches.Single();
            Assert.AreEqual("acg", match.SequenceChunk);
            Assert.AreEqual(1.0, match.Score, 0.000001);
        }

        private sealed class TemporaryFastq : IDisposable
        {
            public string Path { get; private set; }

            public TemporaryFastq(string header, string sequence, string separator, string quality)
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "FastqDetectiveTests_" + Guid.NewGuid().ToString("N") + ".fastq");

                File.WriteAllText(
                    Path,
                    header + Environment.NewLine +
                    sequence + Environment.NewLine +
                    separator + Environment.NewLine +
                    quality + Environment.NewLine,
                    Encoding.ASCII);
            }

            public void Dispose()
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
        }
    }
}
