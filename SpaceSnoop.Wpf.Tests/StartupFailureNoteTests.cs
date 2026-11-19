using SpaceSnoop.Wpf.Bootstrap;
using System.Reflection;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class StartupFailureNoteTests
{
    private const string LogsDirectory = @"C:\Users\Тест\AppData\Local\SpaceSnoop\logs";

    [TestCaseSource(nameof(Failures))]
    public void Ошибка_старта_объясняется_без_стектрейса_и_технических_подробностей(Exception failure, bool aboutFiles)
    {
        var text = StartupFailureNote.Describe(failure, LogsDirectory);
        var root = failure.GetBaseException();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.StackTrace, Is.Not.Null, "Исключение не брошено – стектрейсу неоткуда взяться, кейс ничего не проверяет.");
            Assert.That(text, Does.StartWith(AppInfo.Name));
            Assert.That(text, Does.Not.Contain(" at "));
            Assert.That(text, Does.Not.Contain(failure.GetType().Name));
            Assert.That(text, Does.Not.Contain(root.GetType().Name));
            Assert.That(text, Does.Not.Contain(root.Message));
            Assert.That(text, Does.Contain(LogsDirectory));
            Assert.That(text.Contains("не удалось открыть свои файлы", StringComparison.Ordinal), Is.EqualTo(aboutFiles));
        }
    }

    [Test]
    public void Сбой_до_старта_журнала_не_отправляет_за_журналом_в_папку()
    {
        var failure = Thrown(() => throw new TypeInitializationException("SpaceSnoop.Wpf.Bootstrap.Storage.AppStorage", new UnauthorizedAccessException("Access to the path is denied")));

        var text = StartupFailureNote.Describe(failure, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain("журнал из папки"));
            Assert.That(text, Does.Contain("Журнал ошибок записать не удалось"));
            Assert.That(text, Does.Contain("не удалось открыть свои файлы"));
            Assert.That(text, Does.Not.Contain("Access to the path"));
        }
    }

    private static IEnumerable<TestCaseData> Failures()
    {
        yield return new TestCaseData(Thrown(() => throw new InvalidOperationException("Unable to resolve service for type 'ScanViewModel'")), false)
            .SetArgDisplayNames("сбой сборки сервисов");

        yield return new TestCaseData(Thrown(() => throw new TargetInvocationException(new IOException("The process cannot access the file 'settings.toml'"))), true)
            .SetArgDisplayNames("файл настроек занят");

        yield return new TestCaseData(Thrown(() => throw new AggregateException(new UnauthorizedAccessException("Access to the path is denied"))), true)
            .SetArgDisplayNames("нет прав на папку");

        yield return new TestCaseData(Thrown(() => throw new AggregateException(new InvalidOperationException("Sequence contains no elements"), new IOException("The process cannot access the file 'settings.toml'"))), true)
            .SetArgDisplayNames("файловая ошибка второй в наборе");
    }

    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Действие не бросило исключение.");
    }
}
