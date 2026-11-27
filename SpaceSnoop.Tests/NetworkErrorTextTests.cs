using SpaceSnoop.Core;
using System.ComponentModel;

namespace SpaceSnoop.Tests;

[TestFixture]
public class NetworkErrorTextTests
{
    [TestCase(5)]
    [TestCase(86)]
    [TestCase(1326)]
    public void Повышенный_сеанс_объясняет_UAC_и_диспетчер_учётных_данных(int errorCode)
    {
        var exception = new Win32Exception(errorCode);

        var message = NetworkErrorText.Describe(exception, @"\\nas\media", isElevated: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("сеанс").IgnoreCase);
            Assert.That(message, Does.Contain("администратора").IgnoreCase);
            Assert.That(message, Does.Contain("Диспетчере учётных данных Windows"));
            Assert.That(message, Does.Contain("без прав администратора"));
            Assert.That(message, Does.Contain(@"\\nas\media"));
        }
    }

    [Test]
    public void Ошибка_доступа_по_HResult_без_повышения_не_предлагает_снять_админа()
    {
        var exception = new UnauthorizedAccessException();

        var message = NetworkErrorText.Describe(exception, @"\\nas\media", isElevated: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("Нет доступа"));
            Assert.That(message, Does.Contain("Диспетчере учётных данных Windows"));
            Assert.That(message, Does.Not.Contain("без прав администратора"));
        }
    }

    [Test]
    public void Конфликт_сеансов_1219_объясняет_запрет_двух_логинов_и_IP()
    {
        var exception = new IOException("conflict", unchecked((int)0x8007_04C3));

        var message = NetworkErrorText.Describe(exception, @"\\nas\share", isElevated: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("два одновременных подключения"));
            Assert.That(message, Does.Contain("nas"));
            Assert.That(message, Does.Contain("IP"));
        }
    }

    [TestCase(53, "недоступен")]
    [TestCase(67, "не найдена")]
    public void Ошибки_сети_53_и_67_дают_адресное_сообщение(int errorCode, string expectedFragment)
    {
        var hresult = unchecked((int)(0x8007_0000u | (uint)errorCode));
        var exception = new IOException("wrapped", new IOException("inner", hresult));

        var message = NetworkErrorText.Describe(exception, @"\\nas\missing", isElevated: false);

        Assert.That(message, Does.Contain(expectedFragment));
    }

    [Test]
    public void Неизвестная_ошибка_сохраняет_исходное_сообщение()
    {
        var exception = new InvalidOperationException("нестандартная ошибка");

        var message = NetworkErrorText.Describe(exception, @"\\nas\share", isElevated: true);

        Assert.That(message, Is.EqualTo("нестандартная ошибка"));
    }
}
