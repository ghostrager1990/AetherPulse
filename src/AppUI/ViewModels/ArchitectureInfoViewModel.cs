using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AppUI.Models;

namespace AppUI.ViewModels
{
    public partial class ArchitectureInfoViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<SDKComponentVersion> _sdkComponents = new();

        public ArchitectureInfoViewModel()
        {
            LoadSDKComponents();
        }

        private void LoadSDKComponents()
        {
            SdkComponents.Clear();
            SdkComponents.Add(SDKVersionDiscovery.GetFidelityFXVersion());
            SdkComponents.Add(SDKVersionDiscovery.GetAntiLag2Version());
            SdkComponents.Add(SDKVersionDiscovery.GetHLSLBytecodeTarget());
            SdkComponents.Add(SDKVersionDiscovery.GetStreamlineInteropVersion());
        }
    }
}
