namespace SpaceSnoop.Core.Interfaces;

public interface IDiskSpaceCalculator
{
    /// <summary>
    ///     Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    DirectorySpace Calculate(DirectoryInfo directory, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Вычисляет занимаемое дисковое пространство указанной директории и ее подкаталогов в многопоточном режиме.
    /// </summary>
    /// <param name="directory">Директория, для которой нужно вычислить занимаемое дисковое пространство.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Объект <see cref="DirectorySpace" /> с вычисленной информацией о занимаемом дисковом пространстве.</returns>
    /// <remarks>Повышенное выделение памяти</remarks>
    DirectorySpace CalculateMultithreaded(DirectoryInfo directory, CancellationToken cancellationToken = default);
}
