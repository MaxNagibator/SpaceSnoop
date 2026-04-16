namespace SpaceSnoop.Wpf.Bootstrap;

/// <summary>Что делать с помеченными на удаление элементами.</summary>
public enum DeleteMode
{
    /// <summary>Переместить в корзину.</summary>
    RecycleBin = 0,

    /// <summary>Удалить безвозвратно, минуя корзину.</summary>
    Permanent = 1,
}
