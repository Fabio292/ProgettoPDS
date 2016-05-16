using System.Windows;
using System.Windows.Controls;

namespace Server
{
    /// <summary>
    /// Interaction logic for Test.xaml
    /// </summary>
    public partial class Test : Window
    {
        public Test()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
           
            foreach (TabItem item in TABControl.Items)
            {
                item.Visibility = Visibility.Collapsed;
            }

        }

        private void BtnStartSynch_Click(object sender, RoutedEventArgs e)
        {
            foreach (TabItem item in TABControl.Items)
            {
                item.Visibility = Visibility.Visible;
            }
        }

        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            foreach (TabItem item in TABControl.Items)
            {
                item.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnStartTimer_Click(object sender, RoutedEventArgs e)
        {
            TABControl.SelectedIndex = 0;
        }

        private void BtnStopTimer_Click(object sender, RoutedEventArgs e)
        {
            TABControl.SelectedIndex = 1;
        }
    }
}
