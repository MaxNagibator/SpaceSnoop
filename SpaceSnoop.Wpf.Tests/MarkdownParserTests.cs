using SpaceSnoop.Wpf.Markdown;

namespace SpaceSnoop.Wpf.Tests;

public class MarkdownParserTests
{
    [Test]
    public void Соседние_строки_склеиваются_в_один_абзац()
    {
        var blocks = MarkdownParser.Parse("Нашёл главного:\nэто раздутый диск Docker.");

        Assert.That(blocks, Has.Count.EqualTo(1));
        Assert.That(blocks[0].Kind, Is.EqualTo(MarkdownBlockKind.Paragraph));
        Assert.That(blocks[0].Text, Is.EqualTo("Нашёл главного: это раздутый диск Docker."));
    }

    [Test]
    public void Пустая_строка_делит_абзацы()
    {
        var blocks = MarkdownParser.Parse("Первый.\n\nВторой.");

        Assert.That(blocks.Select(block => block.Text), Is.EqualTo(new[] { "Первый.", "Второй." }));
    }

    [TestCase("- Корзина 41 ГБ")]
    [TestCase("* Корзина 41 ГБ")]
    [TestCase("+ Корзина 41 ГБ")]
    public void Строка_с_маркером_становится_пунктом_списка(string line)
    {
        var blocks = MarkdownParser.Parse(line);

        Assert.That(blocks[0].Kind, Is.EqualTo(MarkdownBlockKind.Bullet));
        Assert.That(blocks[0].Text, Is.EqualTo("Корзина 41 ГБ"));
    }

    [Test]
    public void Нумерованный_пункт_несёт_свой_номер()
    {
        var blocks = MarkdownParser.Parse("2) Почистить корзину");

        Assert.That(blocks[0].Kind, Is.EqualTo(MarkdownBlockKind.Ordered));
        Assert.That(blocks[0].Marker, Is.EqualTo("2"));
        Assert.That(blocks[0].Text, Is.EqualTo("Почистить корзину"));
    }

    [Test]
    public void Отступ_даёт_вложенный_пункт()
    {
        var blocks = MarkdownParser.Parse("- Верхний\n  - Вложенный");

        Assert.That(blocks[0].Level, Is.EqualTo(0));
        Assert.That(blocks[1].Level, Is.EqualTo(1));
    }

    [Test]
    public void Список_прерывает_абзац_без_пустой_строки()
    {
        var blocks = MarkdownParser.Parse("Дальше по убыванию:\n- Корзина 41 ГБ");

        Assert.That(blocks.Select(block => block.Kind), Is.EqualTo(new[] { MarkdownBlockKind.Paragraph, MarkdownBlockKind.Bullet }));
    }

    [Test]
    public void Заголовок_несёт_уровень()
    {
        var blocks = MarkdownParser.Parse("## Итог");

        Assert.That(blocks[0].Kind, Is.EqualTo(MarkdownBlockKind.Heading));
        Assert.That(blocks[0].Level, Is.EqualTo(2));
        Assert.That(blocks[0].Text, Is.EqualTo("Итог"));
    }

    [Test]
    public void Решётка_без_пробела_остаётся_текстом()
    {
        var blocks = MarkdownParser.Parse("#4 в списке");

        Assert.That(blocks[0].Kind, Is.EqualTo(MarkdownBlockKind.Paragraph));
        Assert.That(blocks[0].Text, Is.EqualTo("#4 в списке"));
    }

    [Test]
    public void Блок_кода_сохраняет_переносы_строк()
    {
        var blocks = MarkdownParser.Parse("Команда:\n```\ndocker system prune\nwsl --shutdown\n```");

        Assert.That(blocks[1].Kind, Is.EqualTo(MarkdownBlockKind.Code));
        Assert.That(blocks[1].Text, Is.EqualTo("docker system prune\nwsl --shutdown"));
    }

    [TestCase("**91 ГБ**", MarkdownSpanStyle.Bold, "91 ГБ")]
    [TestCase("*почти*", MarkdownSpanStyle.Italic, "почти")]
    [TestCase("`docker system df`", MarkdownSpanStyle.Code, "docker system df")]
    public void Парные_маркеры_дают_оформленный_фрагмент(string source, MarkdownSpanStyle style, string expected)
    {
        var spans = MarkdownParser.Parse(source)[0].Spans;

        Assert.That(spans, Has.Count.EqualTo(1));
        Assert.That(spans[0].Style, Is.EqualTo(style));
        Assert.That(spans[0].Text, Is.EqualTo(expected));
    }

    [Test]
    public void Оформленный_фрагмент_разрезает_обычный_текст()
    {
        var spans = MarkdownParser.Parse("Виновник – **Docker**, 91 ГБ")[0].Spans;

        Assert.That(spans.Select(span => span.Text), Is.EqualTo(new[] { "Виновник – ", "Docker", ", 91 ГБ" }));
        Assert.That(spans[1].Style, Is.EqualTo(MarkdownSpanStyle.Bold));
    }

    [Test]
    public void Код_внутри_жирного_несёт_оба_стиля()
    {
        var spans = MarkdownParser.Parse("**у всех `mirror: true` в двустороннем**")[0].Spans;

        Assert.That(spans.Select(span => span.Text), Is.EqualTo(new[] { "у всех ", "mirror: true", " в двустороннем" }));
        Assert.That(spans[0].Style, Is.EqualTo(MarkdownSpanStyle.Bold));
        Assert.That(spans[1].Style, Is.EqualTo(MarkdownSpanStyle.Bold | MarkdownSpanStyle.Code));
    }

    [TestCase("2 * 3 = 6")]
    [TestCase("Файл docker_data.vhdx")]
    [TestCase("Осталось **мало")]
    public void Непарный_маркер_остаётся_обычным_текстом(string source)
    {
        var spans = MarkdownParser.Parse(source)[0].Spans;

        Assert.That(spans, Has.Count.EqualTo(1));
        Assert.That(spans[0].Style, Is.EqualTo(MarkdownSpanStyle.None));
        Assert.That(spans[0].Text, Is.EqualTo(source));
    }

    [Test]
    public void Пустой_текст_не_даёт_блоков()
    {
        Assert.That(MarkdownParser.Parse("   \n\n"), Is.Empty);
    }
}
