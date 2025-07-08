namespace SpaceSnoop.Core.Domain;

public abstract class SpaceBase(string name, SpaceBase? parent, DateTime creationDate, DateTime lastAccessTime)
{
    /// <summary>
    /// Родительская директория.
    /// </summary>
    public SpaceBase? Parent { get; protected set; } = parent;

    /// <summary>
    /// Название директории.
    /// </summary>
    public string Name { get; } = string.Intern(name);

    /// <summary>
    /// Полный путь до директории.
    /// </summary>
    public string AbsolutePath => GetAbsolutePath();

    /// <summary>
    /// Дата создания директории.
    /// </summary>
    public DateTime CreationDate { get; } = creationDate;

    /// <summary>
    /// Время последнего доступа к директории.
    /// </summary>
    public DateTime LastAccessTime { get; } = lastAccessTime;

    /// <summary>
    /// Размер.
    /// </summary>
    public long Size { get; protected set; }

    /// <summary>
    /// Занимаемый размер.
    /// </summary>
    public virtual long TotalSize => Size;

    /// <summary>
    /// Размер файлов в директории, исключая подкаталоги, в виде строки с суффиксом размера.
    /// </summary>
    public string SizeText => SizeFormatter.Format(Size);

    public SpaceState State { get; private set; } = SpaceState.Added;

    public bool IsDeleted => State == SpaceState.Deleted;

    /// <summary>
    /// Метаданные синхронизации для этого элемента.
    /// </summary>
    public SyncMetadata? SyncMetadata { get; private set; }

    /// <summary>
    /// Указывает, находится ли элемент в состоянии синхронизации.
    /// </summary>
    public bool IsSyncState => State is SpaceState.SyncPending
        or SpaceState.SyncInProgress
        or SpaceState.SyncCompleted
        or SpaceState.SyncFailed
        or SpaceState.SyncConflict;

    /// <summary>
    /// Указывает, требует ли элемент синхронизации.
    /// </summary>
    public bool NeedsSync => State is SpaceState.SyncPending
        or SpaceState.SyncConflict;

    /// <summary>
    /// Указывает, находится ли элемент в процессе синхронизации.
    /// </summary>
    public bool IsSyncing => State == SpaceState.SyncInProgress;

    /// <summary>
    /// Возвращает строку, представляющую информацию о директории для tooltip.
    /// </summary>
    /// <returns>Информация о директории в формате tooltip.</returns>
    public virtual string GetTooltipText()
    {
        var baseInfo = $"""
                        Название: {Name}
                        Путь: {AbsolutePath}
                        Дата создания: {CreationDate}
                        Последний доступ: {LastAccessTime}
                        """;

        if (SyncMetadata == null)
        {
            return baseInfo;
        }

        var syncInfo = $"""

                        Синхронизация:
                        Последняя синхронизация: {SyncMetadata.LastSyncTime?.ToString("dd.MM.yyyy HH:mm:ss") ?? "Никогда"}
                        Статус: {GetSyncStatusText()}
                        """;

        if (SyncMetadata.SyncConflictReason != null)
        {
            syncInfo += $"\nКонфликт: {SyncMetadata.SyncConflictReason}";
        }

        if (SyncMetadata.SyncErrorMessage != null)
        {
            syncInfo += $"\nОшибка: {SyncMetadata.SyncErrorMessage}";
        }

        return baseInfo + syncInfo;
    }

    public void Delete()
    {
        if (State == SpaceState.Deleted || State == SpaceState.Error)
        {
            return;
        }

        State = SpaceState.Deleted;

        DeleteInner();
    }

    public void Restore()
    {
        if (State != SpaceState.Deleted || State == SpaceState.Error)
        {
            return;
        }

        State = SpaceState.Added;

        if (Parent is { IsDeleted: true })
        {
            Parent.Restore();
        }

        RestoreInner();
    }

    public void SwapDelete()
    {
        switch (State)
        {
            case SpaceState.Added:
                Delete();
                break;

            case SpaceState.Deleted:
                Restore();
                break;

            case SpaceState.None:
            case SpaceState.Error:
            default:
                break;
        }
    }

    public void Error()
    {
        State = SpaceState.Error;
    }

    /// <summary>
    /// Инициализирует метаданные синхронизации для этого элемента.
    /// </summary>
    public void InitializeSyncMetadata()
    {
        SyncMetadata ??= new();
    }

    /// <summary>
    /// Устанавливает состояние синхронизации.
    /// </summary>
    /// <param name="syncState">Новое состояние синхронизации.</param>
    public void SetSyncState(SpaceState syncState)
    {
        State = syncState;
    }

    /// <summary>
    /// Отмечает начало синхронизации.
    /// </summary>
    public void MarkSyncStarted()
    {
        InitializeSyncMetadata();
        SetSyncState(SpaceState.SyncInProgress);
    }

    /// <summary>
    /// Отмечает успешное завершение синхронизации.
    /// </summary>
    /// <param name="operationType">Тип выполненной операции.</param>
    /// <param name="targetPath">Путь к целевому элементу.</param>
    public void MarkSyncCompleted(SyncOperationType operationType, string targetPath)
    {
        InitializeSyncMetadata();
        SyncMetadata!.MarkSyncSuccess(operationType, targetPath);
        SetSyncState(SpaceState.SyncCompleted);
    }

    /// <summary>
    /// Отмечает неудачную синхронизацию.
    /// </summary>
    /// <param name="errorMessage">Сообщение об ошибке.</param>
    public void MarkSyncFailed(string errorMessage)
    {
        InitializeSyncMetadata();
        SyncMetadata!.MarkSyncFailure(errorMessage);
        SetSyncState(SpaceState.SyncFailed);
    }

    /// <summary>
    /// Отмечает конфликт синхронизации.
    /// </summary>
    /// <param name="conflictReason">Причина конфликта.</param>
    public void MarkSyncConflict(string conflictReason)
    {
        InitializeSyncMetadata();
        SyncMetadata!.MarkSyncConflict(conflictReason);
        SetSyncState(SpaceState.SyncConflict);
    }

    /// <summary>
    /// Отмечает элемент как ожидающий синхронизации.
    /// </summary>
    public void MarkSyncPending()
    {
        InitializeSyncMetadata();
        SetSyncState(SpaceState.SyncPending);
    }

    /// <summary>
    /// Сбрасывает состояние синхронизации к исходному.
    /// </summary>
    public void ResetSyncState()
    {
        if (IsSyncState)
        {
            State = SpaceState.Added;
        }

        SyncMetadata?.Reset();
    }

    protected virtual void RestoreInner()
    {
    }

    protected virtual void DeleteInner()
    {
    }

    /// <summary>
    /// Возвращает текстовое описание текущего состояния синхронизации.
    /// </summary>
    /// <returns>Описание состояния синхронизации.</returns>
    private string GetSyncStatusText()
    {
        return State switch
        {
            SpaceState.SyncInProgress => "В процессе",
            SpaceState.SyncCompleted => "Завершена",
            SpaceState.SyncFailed => "Ошибка",
            SpaceState.SyncConflict => "Конфликт",
            SpaceState.SyncPending => "Ожидает",
            _ => "Не синхронизирован",
        };
    }

    private string GetAbsolutePath()
    {
        var segments = new List<string>();
        var current = this;

        while (current != null)
        {
            segments.Add(current.Name);
            current = current.Parent;
        }

        var result = segments.ToArray();
        result.AsSpan().Reverse();
        return Path.Combine(result);
    }
}
