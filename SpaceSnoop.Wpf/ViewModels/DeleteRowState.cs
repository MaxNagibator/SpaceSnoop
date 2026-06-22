namespace SpaceSnoop.Wpf.ViewModels;

/// <summary>Состояние строки в диалоге прогресса удаления.</summary>
public enum DeleteRowState
{
    /// <summary>Ожидает удаления.</summary>
    Pending = 0,

    /// <summary>Удаляется в данный момент.</summary>
    Deleting = 1,

    /// <summary>Успешно удалено.</summary>
    Done = 2,

    /// <summary>Удаление завершилось ошибкой.</summary>
    Failed = 3,
}
