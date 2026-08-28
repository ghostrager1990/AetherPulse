using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AppUI.ViewModels;

namespace AppUI.Views
{
    public partial class GameLibraryView : UserControl
    {
        public GameLibraryView()
        {
            InitializeComponent();
        }

        private async void OnAddGameClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Game Executable",
                Filter = "Game Executable (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                if (DataContext is GameLibraryViewModel vm)
                {
                    await vm.AddGameFromPathAsync(dialog.FileName);
                }
            }
        }
    }
}
