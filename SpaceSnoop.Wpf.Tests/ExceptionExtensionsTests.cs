using SpaceSnoop.Wpf.Extensions;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ExceptionExtensionsTests
{
    [Test]
    public void Вложенные_AggregateException_разворачиваются_до_корневой_причины()
    {
        var root = new DirectoryNotFoundException("нет пути");
        Exception nested = root;

        for (var depth = 0; depth < 8; depth++)
        {
            nested = new AggregateException(nested);
        }

        Assert.That(nested.Unwrap(), Is.SameAs(root));
    }

    [Test]
    public void Обычное_исключение_возвращается_как_есть()
    {
        var exception = new InvalidOperationException("сбой");

        Assert.That(exception.Unwrap(), Is.SameAs(exception));
    }
}
