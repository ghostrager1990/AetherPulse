using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;

namespace AppUI.ViewModels
{
    public partial class RestoreGameItemViewModel : ObservableObject
    {
        public GameProfile Profile { get; }

        [ObservableProperty]
        private bool _isSelected = true;

        public RestoreGameItemViewModel(GameProfile profile)
        {
            Profile = profile;
        }
    }

    public partial class RestoreGamesViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<RestoreGameItemViewModel> _candidates = new();

        [ObservableProperty]
        private bool? _isAllSelected = true;

        private bool _isUpdatingSelectAll;

        public RestoreGamesViewModel(IEnumerable<GameProfile> candidateProfiles)
        {
            foreach (var p in candidateProfiles)
            {
                var item = new RestoreGameItemViewModel(p);
                item.PropertyChanged += OnItemPropertyChanged;
                Candidates.Add(item);
            }
            UpdateSelectAllState();
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RestoreGameItemViewModel.IsSelected))
            {
                if (!_isUpdatingSelectAll)
                {
                    UpdateSelectAllState();
                }
            }
        }

        partial void OnIsAllSelectedChanged(bool? value)
        {
            if (_isUpdatingSelectAll || !value.HasValue) return;

            try
            {
                _isUpdatingSelectAll = true;
                foreach (var item in Candidates)
                {
                    item.IsSelected = value.Value;
                }
            }
            finally
            {
                _isUpdatingSelectAll = false;
            }
        }

        private void UpdateSelectAllState()
        {
            if (Candidates.Count == 0)
            {
                _isUpdatingSelectAll = true;
                IsAllSelected = false;
                _isUpdatingSelectAll = false;
                return;
            }

            int selectedCount = Candidates.Count(c => c.IsSelected);

            _isUpdatingSelectAll = true;
            if (selectedCount == Candidates.Count)
            {
                IsAllSelected = true;
            }
            else if (selectedCount == 0)
            {
                IsAllSelected = false;
            }
            else
            {
                IsAllSelected = false;
            }
            _isUpdatingSelectAll = false;
        }

        public List<GameProfile> GetSelectedProfiles()
        {
            return Candidates.Where(c => c.IsSelected).Select(c => c.Profile).ToList();
        }
    }
}
