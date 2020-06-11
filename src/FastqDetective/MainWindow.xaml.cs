
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
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void AnalysisButton_Click(object sender, RoutedEventArgs e)
        {
            var analysisWindow = new AnalysisWindow();
            analysisWindow.Show();
        }

        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            var conversionWindow = new ConvertWindow();
            conversionWindow.Show();
        }
    }
}
