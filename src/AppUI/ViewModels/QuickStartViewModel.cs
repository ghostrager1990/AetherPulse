using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppUI.ViewModels
{
    public partial class QuickStartViewModel : ObservableObject
    {
        private readonly MainViewModel? _mainViewModel;

        public QuickStartViewModel()
        {
        }

        public QuickStartViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
        }

        [RelayCommand]
        public void NavigateToLibrary()
        {
            _mainViewModel?.Navigate(NavigationPage.Library);
        }

        [RelayCommand]
        public void NavigateToPacing()
        {
            _mainViewModel?.Navigate(NavigationPage.PacingTuning);
        }

        [RelayCommand]
        public void NavigateToFsr()
        {
            _mainViewModel?.Navigate(NavigationPage.FsrTuning);
        }

        [RelayCommand]
        public void NavigateToRayRegen()
        {
            _mainViewModel?.Navigate(NavigationPage.RayRegenTuning);
        }
    }
}
