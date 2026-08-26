using System.Windows;

namespace AppUI.Views
{
    public partial class AntiCheatWarningDialog : Window
    {
        public AntiCheatWarningDialog()
        {
            InitializeComponent();
        }

        private void OnProceedClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
