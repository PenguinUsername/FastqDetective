
using FastqDetective.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FastqDetective
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class AnalysisWindow : Window
    {
        static Task<List<ParsingResult>> task;
        static DispatcherTimer dispatcherTimer;
        static ParsingContext context;

        static CancellationTokenSource cancellationTokenSource;

        static long ticks = 0;
        public AnalysisWindow()
        {
            InitializeComponent();
            SequencePath.Text = @"C:\Examplepath\XX-YYYYY_XYYXYY_libYYYYYY_YYYY_Y_Y.fastq";
            MarkerPath.Text = @"C:\Examplepath\marker.txt";
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            SequencePath.IsEnabled = false;
            MarkerPath.IsEnabled = false;
            FromIndex.IsEnabled = false;
            ToIndex.IsEnabled = false;
            ChunkSize.IsEnabled = false;
            MaxChunks.IsEnabled = false;
            ThresholdSlider.IsEnabled = false;

            StartButton.Content = "Stop";
            StartButton.Click -= StartButton_Click;
            StartButton.Click += StopButton_Click;

            var sequencePath = SequencePath.Text;
            var markerPath = MarkerPath.Text;
            var threshold = ThresholdSlider.Value / 100;
            var chunkSize = 0;
            var maxChunks = 0;
            var fromIndex = 0;
            var toIndex = -1;

            int.TryParse(FromIndex.Text, out fromIndex);
            int.TryParse(ToIndex.Text, out toIndex);
            int.TryParse(ChunkSize.Text, out chunkSize);
            int.TryParse(MaxChunks.Text, out maxChunks);

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = TimeSpan.FromMilliseconds(100);
            dispatcherTimer.Start();
            ticks = 0;

            var markers = MarkerParser.ParseFile(markerPath);
            var chunk = Math.Max(markers.Max(m => m.Length) * 3, chunkSize);
            var concurrentMax = Math.Max(64, maxChunks);
            var slidingStepRemainder = (int)((3 - threshold * 2) * markers.Max(m => m.Length) - 1);

            ChunkSize.Text = chunk.ToString();
            MaxChunks.Text = concurrentMax.ToString();

            cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = cancellationTokenSource.Token;

            task = Task<List<ParsingResult>>.Run(() => {
                var results = new List<ParsingResult>();

                foreach (var marker in markers)
                {
                    results.Add(new ParsingResult(marker));
                }

                context = new ParsingContext(results);
                context.Threshold = threshold;

                AnalysisParser.ParseFile(sequencePath, fromIndex, toIndex, chunk, slidingStepRemainder, concurrentMax, context, cancellationTokenSource);

                return context.parsingResults;
            }, token);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource.Cancel();
            StartButton.IsEnabled = false;
            StartButton.Content = "Stopping";
            ToIndex.Text = context.CurrentSequenceIndex.ToString();
        }

        private void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => {
                ProcessingStatus.Content = "Analyzing: " + Interlocked.Read(ref context.CurrentSequenceIndex);
                //ProcessingStatus.Content += Environment.NewLine;
                //ProcessingStatus.Content += $"{context.CurrentSequence.Length}";
                ProcessingStatus.Content += Environment.NewLine;
                ProcessingStatus.Content += $"{Interlocked.Read(ref context.TasksResultsReceived)}/{Interlocked.Read(ref context.TasksStarted)} Task Results Retrieved";
            }, DispatcherPriority.Render);

            ++ticks;

            if (task.IsCompleted)
            {
                dispatcherTimer.Stop();

                var results = task.Result;

                ProcessingStatus.Content = string.Empty;

                foreach (var result in results)
                {
                    ProcessingStatus.Content += "Matches: " + result.Matches.Count;
                }

                ResultsWriter.WriteResults(context, SequencePath.Text, MarkerPath.Text, FromIndex.Text, ToIndex.Text, ticks * dispatcherTimer.Interval, ChunkSize.Text);

                context = null;
                cancellationTokenSource.Dispose();

                StartButton.Content = "Start";
                StartButton.Click += StartButton_Click;
                StartButton.Click -= StopButton_Click;

                SequencePath.IsEnabled = true;
                MarkerPath.IsEnabled = true;
                FromIndex.IsEnabled = true;
                ToIndex.IsEnabled = true;
                ChunkSize.IsEnabled = true;
                MaxChunks.IsEnabled = true;
                ThresholdSlider.IsEnabled = true;
                StartButton.IsEnabled = true;
            }
        }
    }
}
