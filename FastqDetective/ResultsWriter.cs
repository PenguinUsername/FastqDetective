
using FastqDetective.Parsing;
using System;

namespace FastqDetective
{
    public static class ResultsWriter
    {
        public static void WriteResults(ParsingContext context, string sequencePath, string markerPath, string from, string to, TimeSpan duration, string chunkSize)
        {
            using (System.IO.StreamWriter file = new System.IO.StreamWriter($"./sequence_{DateTime.Now.ToString("yyyyMMdd_hhmmss_ffff")}.txt"))
            {
                file.WriteLine($"Time Finished: {DateTime.Now.ToString()}");
                file.WriteLine($"Analysis Duration: {duration.ToString()}");
                file.WriteLine($"Sequence file: {sequencePath}");
                file.WriteLine($"Marker file: {markerPath}");
                file.WriteLine($"Markers: {context.parsingResults.Count}");
                file.WriteLine($"Threshold: {context.Threshold}");

                if (chunkSize.Length > 0)
                {
                    file.WriteLine($"Chunk Size: {chunkSize}");
                }

                if (from.Length > 0)
                {
                    file.WriteLine($"From: {from}");
                }

                if (to.Length > 0)
                {
                    file.WriteLine($"To: {to}");
                } else
                {
                    file.WriteLine($"Sequence Size: {context.CurrentSequenceIndex}");
                }

                file.WriteLine($"Tasks executed: {context.TasksStarted}");
                file.WriteLine();
                file.WriteLine("RESULTS");
                file.WriteLine();

                foreach (var result in context.parsingResults)
                {
                    file.WriteLine("-----------------------------------------------------------------------");
                    file.WriteLine(result.MarkerString);
                    file.WriteLine();
                    file.WriteLine($"Size: {result.MarkerString.Length}");
                    file.WriteLine($"Matches: {result.Matches.Count}");
                    file.WriteLine();

                    foreach (var match in result.Matches)
                    {
                        file.WriteLine($"Locus {match.Locus} - Score {match.Score}");
                        file.WriteLine($"Matching chunk:");
                        file.WriteLine($"{match.SequenceChunk}");
                        file.WriteLine();
                    }

                    file.WriteLine("-----------------------------------------------------------------------");
                }
            }
        }
    }
}
