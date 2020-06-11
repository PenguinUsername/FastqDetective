
using FastqDetective.Parsing;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FastqDetective
{
    /// <summary>
    /// Interaction logic for NewFileWindow.xaml
    /// </summary>
    public partial class ConvertWindow : Window
    {
        private static Task task;
        private static DispatcherTimer dispatcherTimer;

        public ConvertWindow()
        {
            InitializeComponent();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            InputPath.IsEnabled = false;
            OutputPath.IsEnabled = false;
            StartButton.IsEnabled = false;

            string inputPath = InputPath.Text;
            string outputPath = OutputPath.Text;

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = TimeSpan.FromMilliseconds(1000);
            dispatcherTimer.Start();

            task = Task.Run(() => ConversionParser.ConvertFile(inputPath, outputPath));
        }

        private void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            if (task.IsCompleted)
            {
                dispatcherTimer.Stop();
                task = null;
                InputPath.IsEnabled = true;
                OutputPath.IsEnabled = true;
                StartButton.IsEnabled = true;
            }
        }
    }
}
