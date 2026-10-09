using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FastqDetective.Parsing
{
    public static class AnalysisParser
    {
        private const int HeaderLineStep = 0;
        private const int SequenceLineStep = 1;
        private const int SeparatorLineStep = 2;
        private const int QualityLineStep = 3;
        private const int TaskPollIntervalMilliseconds = 10;

        public static void ParseFile(
            string path,
            int fromIndex,
            int toIndex,
            int chunkSize,
            int slidingStepRemainder,
            int concurrentMax,
            ParsingContext context,
            CancellationTokenSource cancellationTokenSource)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (cancellationTokenSource == null) throw new ArgumentNullException(nameof(cancellationTokenSource));
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));

            concurrentMax = Math.Max(1, concurrentMax);
            var slidingStep = chunkSize - slidingStepRemainder;
            if (slidingStep <= 0 || slidingStep > chunkSize)
                throw new ArgumentOutOfRangeException(nameof(slidingStepRemainder),
                    "The resulting sliding step must be between 1 and chunkSize.");

            using (var concurrencyGate = new SemaphoreSlim(concurrentMax, concurrentMax))
            {
                try
                {
                    using (var sr = new StreamReader(path))
                    {
                        string currentLine;
                        bool skip = context.CurrentSequenceIndex < fromIndex;

                        while ((currentLine = sr.ReadLine()) != null &&
                               !cancellationTokenSource.IsCancellationRequested)
                        {
                            if (skip)
                            {
                                switch (context.ParseStep)
                                {
                                    case HeaderLineStep:
                                        ++context.ParseStep;
                                        break;
                                    case SequenceLineStep:
                                        ++context.ParseStep;
                                        context.CurrentSequence = currentLine.ToLowerInvariant();
                                        context.CurrentSequenceIndex += currentLine.Length;
                                        break;
                                    case SeparatorLineStep:
                                        ++context.ParseStep;
                                        break;
                                    case QualityLineStep:
                                        context.CurrentCertaintySequence = currentLine.ToLowerInvariant();
                                        context.ParseStep = 0;
                                        break;
                                }

                                skip = context.CurrentSequenceIndex < fromIndex || context.ParseStep != 0;
                                continue;
                            }

                            switch (context.ParseStep)
                            {
                                case HeaderLineStep:
                                    ++context.ParseStep;
                                    break;
                                case SequenceLineStep:
                                    ++context.ParseStep;
                                    context.CurrentSequence += currentLine.ToLowerInvariant();
                                    break;
                                case SeparatorLineStep:
                                    ++context.ParseStep;
                                    break;
                                case QualityLineStep:
                                    context.CurrentCertaintySequence += currentLine.ToLowerInvariant();

                                    // Keep sequence and quality buffers aligned. If malformed FASTQ
                                    // records provide different line lengths, process only their shared prefix.
                                    while (context.CurrentSequence.Length >= chunkSize &&
                                           context.CurrentCertaintySequence.Length >= chunkSize)
                                    {
                                        ScheduleChunk(
                                            context.CurrentSequence,
                                            context.CurrentCertaintySequence,
                                            chunkSize,
                                            context.CurrentSequenceIndex,
                                            context,
                                            concurrencyGate);

                                        context.CurrentSequence = context.CurrentSequence.Substring(slidingStep);
                                        context.CurrentCertaintySequence =
                                            context.CurrentCertaintySequence.Substring(slidingStep);
                                        context.CurrentSequenceIndex += slidingStep;
                                    }

                                    context.ParseStep = 0;
                                    break;
                            }

                            UpdateTaskState(context);

                            if (toIndex > 0 && context.CurrentSequenceIndex >= toIndex)
                                break;
                        }

                        // On normal EOF, evaluate the remaining partial chunk. If cancellation was
                        // requested, do not enqueue new work; already-started tasks are still drained below.
                        if (!cancellationTokenSource.IsCancellationRequested &&
                            context.CurrentSequence.Length > 0 &&
                            context.CurrentCertaintySequence.Length > 0)
                        {
                            var sharedLength = Math.Min(
                                context.CurrentSequence.Length,
                                context.CurrentCertaintySequence.Length);
                            ScheduleChunk(
                                context.CurrentSequence,
                                context.CurrentCertaintySequence,
                                sharedLength,
                                context.CurrentSequenceIndex,
                                context,
                                concurrencyGate);
                        }
                    }

                    while (context.parsingResults.SelectMany(r => r.ActiveTasks).Any(t => !t.IsCompleted))
                    {
                        UpdateTaskState(context);
                        Thread.Sleep(TaskPollIntervalMilliseconds);
                    }

                    UpdateTaskState(context);
                }
                catch (Exception e)
                {
                    Console.WriteLine("The file could not be read or analyzed:");
                    Console.WriteLine(e);
                    throw;
                }
            }
        }

        private static void ScheduleChunk(
            string sequence,
            string certaintySequence,
            int length,
            long chunkStartIndex,
            ParsingContext context,
            SemaphoreSlim concurrencyGate)
        {
            // Convert only the selected prefix; avoid allocating temporary Substring values.
            var sequenceBytes = GetAsciiBytes(sequence, length);
            var certaintyBytes = GetAsciiBytes(certaintySequence, length);
            var threshold = context.Threshold;

            foreach (var parsingResult in context.parsingResults)
            {
                ScheduleLocus(
                    parsingResult,
                    sequenceBytes,
                    certaintyBytes,
                    threshold,
                    chunkStartIndex,
                    concurrencyGate,
                    context);
            }
        }

        private static byte[] GetAsciiBytes(string value, int length)
        {
            var bytes = new byte[length];
            Encoding.ASCII.GetBytes(value, 0, length, bytes, 0);
            return bytes;
        }

        private static void ScheduleLocus(
            ParsingResult parsingResult,
            byte[] sequenceBytes,
            byte[] certaintyBytes,
            double threshold,
            long sequenceIndex,
            SemaphoreSlim concurrencyGate,
            ParsingContext context)
        {
            // Wait before scheduling so the number of running calculations is bounded.
            concurrencyGate.Wait();

            // All per-task inputs are captured as arguments, not read later from mutable parser state.
            // TODO: Marker is a byte[] property; verify it is immutable after scheduling. If markers
            // can be edited/replaced in place, clone it here before the Task.Run delegate captures it.
            var task = Task.Run(() =>
            {
                try
                {
                    return EvaluateLocus(
                        parsingResult.Marker,
                        sequenceBytes,
                        certaintyBytes,
                        threshold,
                        sequenceIndex);
                }
                finally
                {
                    concurrencyGate.Release();
                }
            });

            parsingResult.ActiveTasks.Add(task);
            Interlocked.Increment(ref context.TasksStarted);
        }

        private static void UpdateTaskState(ParsingContext context)
        {
            foreach (var parsingResult in context.parsingResults)
            {
                var completedTasks = parsingResult.ActiveTasks.Where(t => t.IsCompleted).ToList();

                foreach (var task in completedTasks)
                {
                    // Observe faults rather than silently treating failed work as a missing match.
                    var result = task.GetAwaiter().GetResult();
                    if (result != null)
                        parsingResult.Matches.Add(result);

                    Interlocked.Increment(ref context.TasksResultsReceived);
                    parsingResult.ActiveTasks.Remove(task);
                }
            }
        }

        private static MarkerMatch EvaluateLocus(
            byte[] marker,
            byte[] currentSequenceBytes,
            byte[] currentCertaintyBytes,
            double threshold,
            long sequenceIndex)
        {
            // Quality/certainty bytes are retained in the signature for compatibility with callers,
            // but the original scoring model does not use them.
            var score = RegionalSmithWaterman(marker, currentSequenceBytes, threshold);

            if (!score.HasValue)
                return null;

            var value = score.Value;
            var normalizedScore = (double)value.Score / (MatchScore * marker.Length);
            var sequence = Encoding.ASCII.GetString(currentSequenceBytes);

            return new MarkerMatch(
                (sequenceIndex + value.StartPosition).ToString(),
                normalizedScore,
                sequence);
        }

        private const int MatchScore = 2;
        private const int MismatchScore = -1;
        private const int GapOpenPenalty = 2;
        private const int GapExtensionPenalty = 1;
        private const int NegativeInfinity = int.MinValue / 4;

        private struct ScoreContext
        {
            public int Score;
            public int StartPosition;
            public int EndPosition;

            public ScoreContext(int score, int startPosition, int endPosition)
            {
                Score = score;
                StartPosition = startPosition;
                EndPosition = endPosition;
            }
        }

        /// <summary>
        /// Smith-Waterman local alignment with affine gaps and rolling arrays.
        /// Match = +2, mismatch = -1, gap-open = -2, gap-extension = -1.
        /// The zero floor permits a local alignment to restart at any cell.
        ///
        /// Conservative upper-bound pruning stops when no continuation or new local alignment
        /// can strictly improve the first maximum already found. The legacy heuristic and
        /// diagSkip cell skipping remain disabled until their correctness is proven.
        /// </summary>
        private static ScoreContext? RegionalSmithWaterman(
            byte[] marker,
            byte[] sequence,
            double threshold)
        {
            if (marker == null) throw new ArgumentNullException(nameof(marker));
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            if (marker.Length == 0 || sequence.Length == 0)
                return null;

            int markerLength = marker.Length;
            int sequenceLength = sequence.Length;
            double absoluteThreshold = threshold * MatchScore * markerLength;

            // H is the best local score. E/F track gaps in the two directions.
            var previousH = new int[markerLength + 1];
            var currentH = new int[markerLength + 1];
            var previousOrigin = new int[markerLength + 1];
            var currentOrigin = new int[markerLength + 1];

            var previousGapInMarker = Enumerable.Repeat(NegativeInfinity, markerLength + 1).ToArray();
            var currentGapInMarker = new int[markerLength + 1];
            var gapInSequence = new int[markerLength + 1];

            int globalMaxScore = 0;
            int globalMaxOrigin = 0;
            int globalMaxEnd = 0;

            for (int i = 1; i <= sequenceLength; i++)
            {
                currentH[0] = 0;
                currentOrigin[0] = i;
                gapInSequence[0] = NegativeInfinity;
                currentGapInMarker[0] = NegativeInfinity;

                int rowMaxScore = 0;

                for (int j = 1; j <= markerLength; j++)
                {
                    currentGapInMarker[j] = Math.Max(
                        previousH[j] - GapOpenPenalty,
                        previousGapInMarker[j] - GapExtensionPenalty);

                    gapInSequence[j] = Math.Max(
                        currentH[j - 1] - GapOpenPenalty,
                        gapInSequence[j - 1] - GapExtensionPenalty);

                    int substitution = marker[j - 1] == sequence[i - 1]
                        ? MatchScore
                        : MismatchScore;
                    int diagonal = previousH[j - 1] + substitution;

                    // Canonical Smith-Waterman local-alignment reset.
                    int best = Math.Max(0, Math.Max(diagonal,
                        Math.Max(currentGapInMarker[j], gapInSequence[j])));

                    if (best == 0)
                    {
                        currentOrigin[j] = i;
                    }
                    else if (best == diagonal)
                    {
                        currentOrigin[j] = previousH[j - 1] == 0
                            ? i - 1
                            : previousOrigin[j - 1];
                    }
                    else if (best == currentGapInMarker[j])
                    {
                        currentOrigin[j] = previousOrigin[j];
                    }
                    else
                    {
                        currentOrigin[j] = currentOrigin[j - 1];
                    }

                    currentH[j] = best;

                    if (best > globalMaxScore)
                    {
                        globalMaxScore = best;
                        globalMaxOrigin = currentOrigin[j];
                        globalMaxEnd = i;
                    }

                    if (best > rowMaxScore)
                        rowMaxScore = best;
                }

                // Bound both alignments already in progress (including the vertical gap state)
                // and any new local alignment that could start in the unprocessed suffix.
                // Every future sequence character can add at most MatchScore to an active
                // alignment. A fresh local alignment is bounded by a perfect match over the
                // shorter of the remaining sequence and marker. Use long arithmetic to avoid
                // overflow in the bounds for unusually large inputs.
                int remaining = sequenceLength - i;
                long activePotential = 0;
                for (int j = 1; j <= markerLength; j++)
                {
                    // At column j, at most markerLength-j additional marker characters
                    // can contribute positive substitutions to this alignment.
                    int additionalMatches = Math.Min(remaining, markerLength - j);
                    long statePotential = (long)currentH[j] +
                        (long)MatchScore * additionalMatches;
                    if (statePotential > activePotential)
                        activePotential = statePotential;

                    statePotential = (long)currentGapInMarker[j] +
                        (long)MatchScore * additionalMatches;
                    if (statePotential > activePotential)
                        activePotential = statePotential;
                }

                long newAlignmentPotential = (long)MatchScore * Math.Min(markerLength, remaining);
                long potential = Math.Max(activePotential, newAlignmentPotential);

                // Equal scores need not replace the first maximum already recorded.
                if (globalMaxScore > 0 && potential <= globalMaxScore)
                    break;

                Swap(ref previousH, ref currentH);
                Swap(ref previousOrigin, ref currentOrigin);
                Swap(ref previousGapInMarker, ref currentGapInMarker);
            }

            if (globalMaxScore >= absoluteThreshold && globalMaxScore > 0)
            {
                return new ScoreContext(globalMaxScore, globalMaxOrigin, globalMaxEnd);
            }

            return null;
        }

        private static void Swap<T>(ref T left, ref T right)
        {
            T temporary = left;
            left = right;
            right = temporary;
        }
    }
}
