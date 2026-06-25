namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed class SyncQuickProfileItem : ObservableObject
{
    private readonly Action<SyncQuickProfileItem> _requestDelete;
    private readonly Action<SyncQuickProfileItem> _confirmDelete;
    private readonly Action<SyncQuickProfileItem> _cancelDelete;
    private readonly Action<SyncQuickProfileItem> _requestEdit;
    private readonly Action<SyncQuickProfileItem> _confirmEdit;
    private readonly Action<SyncQuickProfileItem> _cancelEdit;
    private bool _isDeleteConfirming;
    private bool _isEditConfirming;

    public SyncQuickProfileItem(
        SyncProfile model,
        bool isDefault,
        Action<SyncQuickProfileItem> requestDelete,
        Action<SyncQuickProfileItem> confirmDelete,
        Action<SyncQuickProfileItem> cancelDelete,
        Action<SyncQuickProfileItem> requestEdit,
        Action<SyncQuickProfileItem> confirmEdit,
        Action<SyncQuickProfileItem> cancelEdit)
    {
        Model = model;
        IsDefault = isDefault;
        _requestDelete = requestDelete;
        _confirmDelete = confirmDelete;
        _cancelDelete = cancelDelete;
        _requestEdit = requestEdit;
        _confirmEdit = confirmEdit;
        _cancelEdit = cancelEdit;
        RequestDeleteCommand = new RelayCommand(() => _requestDelete(this));
        ConfirmDeleteCommand = new RelayCommand(() => _confirmDelete(this));
        CancelDeleteCommand = new RelayCommand(() => _cancelDelete(this));
        RequestEditCommand = new RelayCommand(() => _requestEdit(this));
        ConfirmEditCommand = new RelayCommand(() => _confirmEdit(this));
        CancelEditCommand = new RelayCommand(() => _cancelEdit(this));
    }

    public IRelayCommand RequestDeleteCommand { get; }

    public IRelayCommand ConfirmDeleteCommand { get; }

    public IRelayCommand CancelDeleteCommand { get; }

    public IRelayCommand RequestEditCommand { get; }

    public IRelayCommand ConfirmEditCommand { get; }

    public IRelayCommand CancelEditCommand { get; }

    public SyncProfile Model { get; }

    public bool IsDefault { get; }

    public string Id => Model.Id;

    public string Name => string.IsNullOrWhiteSpace(Model.Name) ? "Без названия" : Model.Name;

    public bool CanShowActions => !IsDefault && !IsDeleteConfirming && !IsEditConfirming;

    public bool CanConfirmDelete => !IsDefault && IsDeleteConfirming;

    public bool CanConfirmEdit => !IsDefault && IsEditConfirming;

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

    public bool IsEditConfirming
    {
        get => _isEditConfirming;
        set
        {
            if (SetProperty(ref _isEditConfirming, value))
            {
                OnPropertyChanged(nameof(CanShowActions));
                OnPropertyChanged(nameof(CanConfirmEdit));
            }
        }
    }
}
