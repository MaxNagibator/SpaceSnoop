namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed class SyncQuickProfileItem : ObservableObject
{
    private readonly Action<SyncQuickProfileItem> _requestDelete;
    private readonly Action<SyncQuickProfileItem> _confirmDelete;
    private readonly Action<SyncQuickProfileItem> _cancelDelete;
    private readonly Action<SyncQuickProfileItem> _requestUpdate;
    private readonly Action<SyncQuickProfileItem> _confirmUpdate;
    private readonly Action<SyncQuickProfileItem> _cancelUpdate;
    private readonly Action<SyncQuickProfileItem> _requestRename;
    private readonly Action<SyncQuickProfileItem> _confirmRename;
    private readonly Action<SyncQuickProfileItem> _cancelRename;
    private bool _isDeleteConfirming;
    private bool _isUpdateConfirming;
    private bool _isRenameConfirming;
    private string _editName = string.Empty;

    public SyncQuickProfileItem(
        SyncProfile model,
        bool isDefault,
        Action<SyncQuickProfileItem> requestDelete,
        Action<SyncQuickProfileItem> confirmDelete,
        Action<SyncQuickProfileItem> cancelDelete,
        Action<SyncQuickProfileItem> requestUpdate,
        Action<SyncQuickProfileItem> confirmUpdate,
        Action<SyncQuickProfileItem> cancelUpdate,
        Action<SyncQuickProfileItem> requestRename,
        Action<SyncQuickProfileItem> confirmRename,
        Action<SyncQuickProfileItem> cancelRename)
    {
        Model = model;
        IsDefault = isDefault;
        _requestDelete = requestDelete;
        _confirmDelete = confirmDelete;
        _cancelDelete = cancelDelete;
        _requestUpdate = requestUpdate;
        _confirmUpdate = confirmUpdate;
        _cancelUpdate = cancelUpdate;
        _requestRename = requestRename;
        _confirmRename = confirmRename;
        _cancelRename = cancelRename;
        RequestDeleteCommand = new RelayCommand(() => _requestDelete(this));
        ConfirmDeleteCommand = new RelayCommand(() => _confirmDelete(this));
        CancelDeleteCommand = new RelayCommand(() => _cancelDelete(this));
        RequestUpdateCommand = new RelayCommand(() => _requestUpdate(this));
        ConfirmUpdateCommand = new RelayCommand(() => _confirmUpdate(this));
        CancelUpdateCommand = new RelayCommand(() => _cancelUpdate(this));
        RequestRenameCommand = new RelayCommand(() => _requestRename(this));
        ConfirmRenameCommand = new RelayCommand(() => _confirmRename(this));
        CancelRenameCommand = new RelayCommand(() => _cancelRename(this));
    }

    public IRelayCommand RequestDeleteCommand { get; }

    public IRelayCommand ConfirmDeleteCommand { get; }

    public IRelayCommand CancelDeleteCommand { get; }

    public IRelayCommand RequestUpdateCommand { get; }

    public IRelayCommand ConfirmUpdateCommand { get; }

    public IRelayCommand CancelUpdateCommand { get; }

    public IRelayCommand RequestRenameCommand { get; }

    public IRelayCommand ConfirmRenameCommand { get; }

    public IRelayCommand CancelRenameCommand { get; }

    public SyncProfile Model { get; }

    public bool IsDefault { get; }

    public string Id => Model.Id;

    public string Name => string.IsNullOrWhiteSpace(Model.Name) ? "Без названия" : Model.Name;

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public bool CanShowActions => !IsDefault && !IsDeleteConfirming && !IsUpdateConfirming && !IsRenameConfirming;

    public bool CanConfirmDelete => !IsDefault && IsDeleteConfirming;

    public bool CanConfirmUpdate => !IsDefault && IsUpdateConfirming;

    public bool CanConfirmRename => !IsDefault && IsRenameConfirming;

    public bool IsDeleteConfirming
    {
        get => _isDeleteConfirming;
        set
        {
            if (SetProperty(ref _isDeleteConfirming, value))
            {
                OnPropertyChanged(nameof(CanShowActions));
                OnPropertyChanged(nameof(CanConfirmDelete));
            }
        }
    }

    public bool IsUpdateConfirming
    {
        get => _isUpdateConfirming;
        set
        {
            if (SetProperty(ref _isUpdateConfirming, value))
            {
                OnPropertyChanged(nameof(CanShowActions));
                OnPropertyChanged(nameof(CanConfirmUpdate));
            }
        }
    }

    public bool IsRenameConfirming
    {
        get => _isRenameConfirming;
        set
        {
            if (SetProperty(ref _isRenameConfirming, value))
            {
                OnPropertyChanged(nameof(CanShowActions));
                OnPropertyChanged(nameof(CanConfirmRename));
            }
        }
    }
}
