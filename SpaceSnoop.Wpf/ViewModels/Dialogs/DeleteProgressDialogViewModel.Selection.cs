namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteProgressDialogViewModel
{
    private bool CanMoveSelection()
    {
        return IsIdle && Items.Count > 1;
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelection))]
    private void SelectPrevious()
    {
        var currentIndex = SelectedItem is null ? 0 : Items.IndexOf(SelectedItem);
        var nextIndex = Math.Max(0, currentIndex - 1);
        SelectedItem = Items[nextIndex];
        FollowIndex = nextIndex;
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelection))]
    private void SelectNext()
    {
        var currentIndex = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        var nextIndex = Math.Min(Items.Count - 1, currentIndex + 1);
        SelectedItem = Items[nextIndex];
        FollowIndex = nextIndex;
    }

    private bool CanExclude(DeleteRowViewModel? row)
    {
        return IsIdle && (row is not null || SelectedItem is not null || Items.Count > 0);
    }

    [RelayCommand(CanExecute = nameof(CanExclude))]
    private void Exclude(DeleteRowViewModel? row)
    {
        var target = row ?? SelectedItem ?? Items.FirstOrDefault();

        if (!IsIdle || target is null)
        {
            return;
        }

        var index = Items.IndexOf(target);

        if (index < 0)
        {
            return;
        }

        if (_unmark is not null)
        {
            _unmark(target.Space);
        }
        else if (target.Space.IsDeleted)
        {
            target.Space.Restore();
        }

        Items.RemoveAt(index);
        CountText = $"0 / {Items.Count}";
        SelectedItem = Items.Count > 0 ? Items[Math.Min(index, Items.Count - 1)] : null;
        RefreshModeAfterExclude(target);

        StartCommand.NotifyCanExecuteChanged();
        ExcludeCommand.NotifyCanExecuteChanged();
        SelectPreviousCommand.NotifyCanExecuteChanged();
        SelectNextCommand.NotifyCanExecuteChanged();

        if (Items.Count == 0)
        {
            CloseCommand.Execute(null);
        }
    }

    private void RefreshModeAfterExclude(DeleteRowViewModel excluded)
    {
        if (excluded.IsWithoutRecycleBin)
        {
            _networkCount = Math.Max(0, _networkCount - 1);
        }

        if (excluded.IsPermanent)
        {
            _permanentCount = Math.Max(0, _permanentCount - 1);
            _permanentBytes = Math.Max(0, _permanentBytes - excluded.SizeBytes);
        }
        else
        {
            _recycleCount = Math.Max(0, _recycleCount - 1);
            _recycleBytes = Math.Max(0, _recycleBytes - excluded.SizeBytes);
        }

        _totalBytes = _recycleBytes + _permanentBytes;

        var nextForcedByNetwork = _networkCount > 0;
        var nextIsMixed = !_basePermanent && _recycleCount > 0 && _permanentCount > 0;
        var nextAllPermanent = _basePermanent || (Items.Count > 0 && _recycleCount == 0);

        var modeChanged = _forcedByNetwork != nextForcedByNetwork
            || _isMixed != nextIsMixed
            || _allPermanent != nextAllPermanent;

        ForcedByNetwork = nextForcedByNetwork;
        IsMixed = nextIsMixed;
        _allPermanent = nextAllPermanent;

        if (modeChanged)
        {
            OnPropertyChanged(nameof(ActionVerb));
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(NetworkWarningText));
        }

        OnPropertyChanged(nameof(IntroText));
        OnPropertyChanged(nameof(CancelHint));
    }
}
