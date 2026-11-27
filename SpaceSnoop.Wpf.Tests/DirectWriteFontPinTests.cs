using SpaceSnoop.Wpf.Bootstrap.Platform;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class DirectWriteFontPinTests
{
    [Test]
    public void Закрепление_системных_шрифтов_идемпотентно_и_удерживает_коллекцию()
    {
        var first = DirectWriteFontPin.PinSystemFonts();
        var attemptsAfterFirst = DirectWriteFontPin.PinAttempts;
        var facesAfterFirst = DirectWriteFontPin.ProbedFaceCount;
        var second = DirectWriteFontPin.PinSystemFonts();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.GreaterThan(0));
            Assert.That(facesAfterFirst, Is.GreaterThan(0));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(DirectWriteFontPin.PinAttempts, Is.EqualTo(attemptsAfterFirst));
        }
    }
}
