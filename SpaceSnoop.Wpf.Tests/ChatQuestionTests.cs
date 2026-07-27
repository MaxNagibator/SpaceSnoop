using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.ViewModels.Chat;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ChatQuestionTests
{
    [Test]
    public void Вопрос_о_каталоге_несёт_путь_и_размер()
    {
        var question = ChatQuestion.ForScanNode(@"C:\Projects\node_modules", "12,3 ГБ", isDirectory: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(question, Does.Contain(@"`C:\Projects\node_modules`"));
            Assert.That(question, Does.Contain("12,3 ГБ"));
            Assert.That(question, Does.Contain("занимает место"));
        }
    }

    [Test]
    public void Вопрос_о_файле_отличается_от_вопроса_о_каталоге()
    {
        var file = ChatQuestion.ForScanNode(@"C:\docker_data.vhdx", "54 ГБ", isDirectory: false);

        Assert.That(file, Does.Contain("Что это за файл"));
    }

    [Test]
    public void Узел_без_размера_не_получает_пустых_скобок()
    {
        var question = ChatQuestion.ForScanNode(@"C:\temp", string.Empty, isDirectory: true);

        Assert.That(question, Does.Not.Contain("()"));
    }

    [TestCase(ComparisonStatus.LeftOnly, ExpectedResult = "только слева")]
    [TestCase(ComparisonStatus.RightOnly, ExpectedResult = "только справа")]
    [TestCase(ComparisonStatus.Modified, ExpectedResult = "изменён")]
    [TestCase(ComparisonStatus.Conflict, ExpectedResult = "спорный")]
    [TestCase(ComparisonStatus.Identical, ExpectedResult = "идентичен")]
    public string Статус_сравнения_назван_по_русски(ComparisonStatus status)
    {
        return ChatQuestion.Describe(status);
    }

    [Test]
    public void Вопрос_о_различии_несёт_обе_стороны_и_причину()
    {
        var question = ChatQuestion.ForSyncNode(
            @"src\app.config",
            ComparisonStatus.Modified,
            isDirectory: false,
            "1,2 МБ",
            "1,1 МБ",
            "размер");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(question, Does.StartWith("Файл `src\\app.config`"));
            Assert.That(question, Does.Contain("«изменён»"));
            Assert.That(question, Does.Contain("слева 1,2 МБ, справа 1,1 МБ"));
            Assert.That(question, Does.Contain("различие: размер"));
        }
    }

    [Test]
    public void Односторонний_каталог_не_упоминает_отсутствующую_сторону()
    {
        var question = ChatQuestion.ForSyncNode(
            @"logs",
            ComparisonStatus.LeftOnly,
            isDirectory: true,
            "8 МБ",
            string.Empty,
            string.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(question, Does.StartWith("Каталог `logs`"));
            Assert.That(question, Does.Contain("(слева 8 МБ)"));
            Assert.That(question, Does.Not.Contain("справа"));
        }
    }

    [Test]
    public void Контекст_окна_уезжает_приставкой_к_ходу_а_не_вместо_него()
    {
        var request = new AgentRequest { Prompt = "Куда делось место?", Context = "[Состояние окна]" };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.TurnText, Is.EqualTo("[Состояние окна]\n\nКуда делось место?"));
            Assert.That(request with { Context = null }, Has.Property(nameof(AgentRequest.TurnText)).EqualTo("Куда делось место?"));
        }
    }
}
