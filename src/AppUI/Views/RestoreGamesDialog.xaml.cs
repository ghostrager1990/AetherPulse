using System.Windows;
using AppUI.ViewModels;

namespace AppUI.Views
{
    public partial class RestoreGamesDialog : Window
    {
        public RestoreGamesViewModel ViewModel { get; }

        public RestoreGamesDialog(RestoreGamesViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
        }

        private void OnRestoreClick(object sender, RoutedEventArgs e)
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
