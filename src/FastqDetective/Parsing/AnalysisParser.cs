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
        public static void ParseFile(string path, int fromIndex, int toIndex, int chunkSize, int slidingStepRemainder, int concurrentMax, ParsingContext context, CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                using (StreamReader sr = new StreamReader(path))
                {
                    string currentLine;

                    bool skip = context.CurrentSequenceIndex < fromIndex;

                    bool diagSkip = false;

                    while ((currentLine = sr.ReadLine()) != null && !cancellationTokenSource.IsCancellationRequested)
                    {
                        if (skip)
                        {
                            switch (context.ParseStep)
                            {
                                case 0:

                                    ++context.ParseStep;
                                    break;
                                case 1:
                                    ++context.ParseStep;
                                    context.CurrentSequence = currentLine.ToLower();
                                    context.CurrentSequenceIndex += currentLine.Length;
                                    break;
                                case 2:
                                    ++context.ParseStep;
                                    break;
                                case 3:
                                    context.CurrentCertaintySequence = currentLine.ToLower();
                                    context.ParseStep = 0;
                                    break;
                                default:
                                    break;
                            }

                            skip = context.CurrentSequenceIndex < fromIndex || context.ParseStep != 0;

                            continue;
                        }

                        switch (context.ParseStep)
                        {
                            case 0:
                                //var values = currentLine.Split(' ');
                                //var leftValues = currentLine.Split(':');
                                //var rightValues = currentLine.Split(':');

                                //context.InstrumentName = leftValues[0];
                                //context.RunId = leftValues[1];
                                //context.FlowcellId = leftValues[2];
                                //context.FlowcellLane = leftValues[3];
                                //context.ClusterCoordinateX = leftValues[4];
                                //context.ClusterCoordinateY = leftValues[5];

                                //context.IndexSequence = rightValues[3];

                                ++context.ParseStep;
                                break;
                            case 1:
                                ++context.ParseStep;
                                context.CurrentSequence += currentLine.ToLower();
                                break;
                            case 2:
                                ++context.ParseStep;
                                break;
                            case 3:
                                context.CurrentCertaintySequence += currentLine.ToLower();

                                if (context.CurrentSequence.Length >= chunkSize) // while if single byte slide
                                {
                                    var slidingStep = chunkSize - slidingStepRemainder;

                                    var currentSequenceBytes = Encoding.ASCII.GetBytes(context.CurrentSequence.Substring(0, chunkSize));
                                    var currentCertaintyBytes = Encoding.ASCII.GetBytes(context.CurrentCertaintySequence.Substring(0, chunkSize));

                                    foreach (var parsingResult in context.parsingResults)
                                    {
                                        var newTask = Task.Run(() => EvaluateLocus(parsingResult.Marker, currentSequenceBytes, currentCertaintyBytes, context.Threshold, context.CurrentSequenceIndex, diagSkip));
                                        parsingResult.ActiveTasks.Add(newTask);
                                        diagSkip = true;
                                        Interlocked.Increment(ref context.TasksStarted);
                                    }

                                    context.CurrentSequence = context.CurrentSequence.Substring(slidingStep);
                                    context.CurrentCertaintySequence = context.CurrentCertaintySequence.Substring(slidingStep);
                                    context.CurrentSequenceIndex += slidingStep;

                                    Interlocked.Exchange(ref context.CurrentSequenceIndex, context.CurrentSequenceIndex);
                                }

                                context.ParseStep = 0;
                                break;
                            default:
                                break;
                        }

                        if (context.parsingResults.Sum(res => res.ActiveTasks.Count) >= concurrentMax)
                        {
                            Thread.Sleep(1000);
                        }

                        UpdateTaskState(context);

                        if (toIndex > 0 && context.CurrentSequenceIndex >= toIndex)
                        {
                            break;
                        }
                    }

                    foreach (var parsingResult in context.parsingResults)
                    {
                        var currentSequenceBytes = Encoding.ASCII.GetBytes(context.CurrentSequence);
                        var currentCertaintyBytes = Encoding.ASCII.GetBytes(context.CurrentCertaintySequence);

                        var newTask = EvaluateLocus(parsingResult.Marker, currentSequenceBytes, currentCertaintyBytes, context.Threshold, context.CurrentSequenceIndex, false);
                        parsingResult.ActiveTasks.Add(newTask);
                        Interlocked.Increment(ref context.TasksStarted);
                    }

                    Interlocked.Exchange(ref context.CurrentSequenceIndex, context.CurrentSequenceIndex);

                    while (context.parsingResults.SelectMany(res => res.ActiveTasks).Any(t => !t.IsCompleted))
                    {
                        UpdateTaskState(context);
                        Thread.Sleep(500);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("The file could not be read:");
                Console.WriteLine(e.Message);
            }
        }

        private static void UpdateTaskState(ParsingContext context)
        {
            foreach (var parsingResult in context.parsingResults)
            {
                var completedTasks = parsingResult.ActiveTasks.Where(t => t.IsCompleted).ToList();

                foreach (var task in completedTasks)
                {
                    var result = task.Result;

                    if (result != null)
                    {
                        parsingResult.Matches.Add(result);
                    }

                    Interlocked.Increment(ref context.TasksResultsReceived);
                    parsingResult.ActiveTasks.Remove(task);
                }
            }
        }

        private static async Task<MarkerMatch> EvaluateLocus(byte[] marker, byte[] currentSequenceBytes, byte[] currentCertaintyBytes, double threshold, long sequenceIndex, bool diagSkip)
        {
            var score = RegionalSmithWaterman(marker, currentSequenceBytes, threshold, diagSkip);

            if (score != null)
            {
                var normalizedScore = (double)score.Value.Score / (2 * marker.Length);

                var match = new MarkerMatch((sequenceIndex + score.Value.StartPosition).ToString(), 
                    normalizedScore, 
                    Encoding.ASCII.GetString(currentSequenceBytes).Substring(score.Value.StartPosition, score.Value.EndPosition - score.Value.StartPosition));
                return match;
            }
            else
            {
                return null;
            }
        }

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

        private static ScoreContext? RegionalSmithWaterman(byte[] marker, byte[] sequence, double threshold, bool diagSkip)
        {
            int globalMaxScore = 0;
            int globalMaxOrigin = 0;
            int globalMaxEnd = 0;

            int markerCount = marker.Length + 1;
            int sequenceCount = sequence.Length + 1;

            var sequenceDiff = sequenceCount - markerCount;

            var absoluteThreshold = threshold * 2 * marker.Length;

            int[] previousScoreColumn = new int[markerCount];
            int[] currentScoreColumn = new int[markerCount];
            int[] previousOriginColumn = new int[markerCount];
            int[] currentOriginColumn = new int[markerCount];

            for (int j = 0; j < markerCount; j++)
            {
                currentOriginColumn[j] = 1;
            }

            var deadIndexes = new bool[markerCount];
            deadIndexes[0] = true;

            var previousFromLeft = new bool[markerCount];

            var globalPotential = markerCount * 2;

            //Matrix Fill Step
            for (int i = 1; i < sequenceCount; i++)
            {
                var rowMaxPotential = 0;
                var columnPotential = Math.Min((sequenceCount - i) * 2, globalPotential);

                var previousWasUp = false;

                currentScoreColumn[0] = 0;
                currentOriginColumn[0] = i;

                for (int j = 1; j < markerCount; j++)
                {
                    if(diagSkip)
                    {
                        if (i < markerCount && j > i )
                        {
                            deadIndexes[j] = false;
                            continue;
                        }

                        if (i > sequenceDiff && j < i - sequenceDiff)
                        {
                            deadIndexes[j] = false;
                            continue;
                        }
                    }

                    if (deadIndexes[j])
                    {
                        currentScoreColumn[j] = currentScoreColumn[j - 1] - 2;
                        currentOriginColumn[j] = currentOriginColumn[j - 1];
                        deadIndexes[j] = false;
                        continue;
                    }

                    int scoreDiag;
                    if (marker[j - 1] == sequence[i - 1])
                        scoreDiag = previousScoreColumn[j - 1] + 2;
                    else
                        scoreDiag = previousScoreColumn[j - 1] - 1;

                    int scoreUp;
                    int scoreLeft;

                    if (previousWasUp)
                    {
                        scoreUp = currentScoreColumn[j - 1] - 1;
                    } else
                    {
                        scoreUp = currentScoreColumn[j - 1] - 2;
                    }

                    if (previousFromLeft[j])
                    {
                        scoreLeft = previousScoreColumn[j] - 1;
                    }
                    else
                    {
                        scoreLeft = previousScoreColumn[j] - 2;
                    }

                    int localMax = Math.Max(Math.Max(scoreDiag, scoreLeft), scoreUp);
                    int origin = 0;

                    if (scoreUp == localMax)
                    {
                        origin = currentOriginColumn[j - 1];
                        previousWasUp = true;
                    } else
                    {
                        previousWasUp = false;
                    }

                    if (scoreLeft == localMax)
                    {
                        origin = previousOriginColumn[j];
                        previousFromLeft[j] = true;
                    }
                    else
                    {
                        previousFromLeft[j] = false;
                    }

                    if (scoreDiag == localMax)
                    {
                        origin = previousOriginColumn[j - 1];
                    }

                    if (localMax <= 0)
                    {
                        origin = i - 1;
                    }

                    currentOriginColumn[j] = origin;

                    if (localMax > globalMaxScore)
                    {
                        globalMaxScore = localMax;
                        globalMaxOrigin = origin;
                        globalMaxEnd = i;
                    }

                    var localPotential = localMax + Math.Min((markerCount - j) * 2, columnPotential);

                    if (localPotential > rowMaxPotential) {
                        rowMaxPotential = localPotential;
                    }

                    currentScoreColumn[j] = localMax;

                    deadIndexes[j] = localPotential < absoluteThreshold && deadIndexes[j - 1];
                }

                if (rowMaxPotential < Math.Max(absoluteThreshold, globalMaxScore))
                {
                    break;
                }

                var tempBubble = previousScoreColumn;
                previousScoreColumn = currentScoreColumn;
                currentScoreColumn = tempBubble;

                tempBubble = previousOriginColumn;
                previousOriginColumn = currentOriginColumn;
                currentOriginColumn = tempBubble;

                //if (i % slidingStep == 0 && columnMaxPotential )
                //{
                //    break;
                //}
            }

            if (globalMaxScore >= absoluteThreshold)
            {
                return new ScoreContext(globalMaxScore, globalMaxOrigin, globalMaxEnd);
            }
            else
            {
                return null;
            }
        }
    }
}
