using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppUI.ViewModels
{
    public partial class QuickStartViewModel : ObservableObject
    {
        private readonly MainViewModel? _mainViewModel;

        public QuickStartViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
        }

        public QuickStartViewModel() : this(null!) { }

        [RelayCommand]
        private void NavigateToLibrary()
        {
            _mainViewModel?.NavigateCommand.Execute(NavigationPage.Library);
        }

        [RelayCommand]
        private void NavigateToPacing()
        {
            _mainViewModel?.NavigateCommand.Execute(NavigationPage.PacingTuning);
        }

        [RelayCommand]
        private void NavigateToRayRegen()
        {
            _mainViewModel?.NavigateCommand.Execute(NavigationPage.RayRegenTuning);
        }
    }
}
