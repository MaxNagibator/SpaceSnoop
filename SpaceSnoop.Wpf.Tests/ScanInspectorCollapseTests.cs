using KeepShell.Bootstrap;
using KeepShell.Testing;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanInspectorCollapseTests
{
    private ISettingsStore _settings = null!;
    private ScanNodeFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = new MemorySettings();
        _factory = new(new(_settings), new(_settings), new FakeShellLauncher());
    }

    [Test]
    public void В_сжатом_виде_выбор_не_раскрывает_детали_а_после_сжатия_раскрывает()
    {
        var inspector = Create();
        inspector.SetCompact(true);

        inspector.Show(Node());
        var compact = inspector.IsInspectorCollapsed;

        inspector.SetCompact(false);
        inspector.Show(Node());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compact, Is.True, "Выбор в сжатом виде раскрыл детали.");
            Assert.That(inspector.IsInspectorCollapsed, Is.False, "Первый выбор вне сжатого вида не раскрыл детали.");
        }
    }

    [Test]
    public void В_сжатом_виде_явное_раскрытие_работает_и_сжатие_его_не_отменяет()
    {
        var inspector = Create();
        inspector.SetCompact(true);

        inspector.ToggleInspectorCollapsedCommand.Execute(null);
        inspector.Show(Node());
        inspector.SetCompact(true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inspector.IsInspectorCollapsed, Is.False);
            Assert.That(_settings.GetBool(SettingsKeys.ScanInspectorCollapsed), Is.False);
        }
    }

    [Test]
    public void До_выбора_узла_детали_свёрнуты()
    {
        Assert.That(Create().IsInspectorCollapsed, Is.True);
    }

    [Test]
    public void Если_раскрытие_само_сжимает_вид_детали_остаются_свёрнутыми_а_не_качаются()
    {
        var inspector = Create();
        inspector.Show(Node());
        inspector.SetCompact(true);

        inspector.SetCompact(false);
        var reopened = inspector.IsInspectorCollapsed;
        inspector.SetCompact(true);
        inspector.SetCompact(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reopened, Is.False, "Выход из сжатия не вернул детали.");
            Assert.That(inspector.IsInspectorCollapsed, Is.True, "Детали снова раскрылись сразу после того, как их раскрытие сжало вид.");
        }
    }

    [TestCase("выбор, сжатие, расширение")]
    [TestCase("сжатие, выбор, расширение")]
    [TestCase("сжатие, расширение, выбор")]
    [TestCase("расширение, выбор, сжатие, выбор, расширение")]
    public void Итог_после_выхода_из_сжатия_не_зависит_от_порядка_событий(string order)
    {
        var inspector = Create();

        foreach (var step in order.Split(", "))
        {
            switch (step)
            {
                case "выбор":
                    inspector.Show(Node());
                    break;

                case "сжатие":
                    inspector.SetCompact(true);
                    break;

                case "расширение":
                    inspector.SetCompact(false);
                    break;
            }
        }

        Assert.That(inspector.IsInspectorCollapsed, Is.False);
    }

    [Test]
    public void Первый_выбор_раскрывает_детали()
    {
        var inspector = Create();

        inspector.Show(Node());

        Assert.That(inspector.IsInspectorCollapsed, Is.False);
    }

    [Test]
    public void После_перезапуска_сохранённое_сворачивание_не_даёт_раскрыть_при_выборе()
    {
        _settings.SetBool(SettingsKeys.ScanInspectorCollapsed, true);
        var inspector = Create();

        inspector.Show(Node());

        Assert.That(inspector.IsInspectorCollapsed, Is.True);
    }

    [Test]
    public void Сжатие_сворачивает_раскрытые_автоматически_детали_а_выход_из_него_раскрывает_обратно()
    {
        var inspector = Create();
        inspector.Show(Node());

        inspector.SetCompact(true);
        var compact = inspector.IsInspectorCollapsed;

        inspector.SetCompact(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compact, Is.True, "Сжатие оставило раскрытыми детали, которые пользователь не раскрывал.");
            Assert.That(inspector.IsInspectorCollapsed, Is.False, "Выход из сжатия не вернул автоматически свёрнутые детали.");
            Assert.That(_settings.GetBool(SettingsKeys.ScanInspectorCollapsed), Is.False, "Автоматическое сворачивание записало явное.");
        }
    }

    [Test]
    public void Снятие_выбора_не_сворачивает_раскрытые_детали()
    {
        var inspector = Create();
        inspector.Show(Node());

        inspector.Clear();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inspector.IsInspectorCollapsed, Is.False);
            Assert.That(_settings.GetBool(SettingsKeys.ScanInspectorCollapsed), Is.False);
        }
    }

    [Test]
    public void Устоявшееся_раскрытие_после_сжатия_возвращается_и_в_следующий_раз()
    {
        var inspector = Create();
        inspector.Show(Node());

        inspector.SetCompact(true);
        inspector.SetCompact(false);
        inspector.SetCompact(false);
        inspector.SetCompact(true);
        inspector.SetCompact(false);

        Assert.That(inspector.IsInspectorCollapsed, Is.False);
    }

    [Test]
    public void Явно_свёрнутые_детали_выход_из_сжатия_не_раскрывает()
    {
        var inspector = Create();
        inspector.Show(Node());
        inspector.ToggleInspectorCollapsedCommand.Execute(null);

        inspector.SetCompact(true);
        inspector.SetCompact(false);

        Assert.That(inspector.IsInspectorCollapsed, Is.True);
    }

    [Test]
    public void Явное_раскрытие_снимает_запрет_на_раскрытие_при_выборе()
    {
        _settings.SetBool(SettingsKeys.ScanInspectorCollapsed, true);
        var inspector = Create();

        inspector.ToggleInspectorCollapsedCommand.Execute(null);
        inspector.Show(Node());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inspector.IsInspectorCollapsed, Is.False);
            Assert.That(_settings.GetBool(SettingsKeys.ScanInspectorCollapsed), Is.False);
        }
    }

    [Test]
    public void Явное_сворачивание_переживает_следующий_выбор_и_сохраняется()
    {
        var inspector = Create();
        inspector.Show(Node());

        inspector.ToggleInspectorCollapsedCommand.Execute(null);
        inspector.Show(Node());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inspector.IsInspectorCollapsed, Is.True);
            Assert.That(_settings.GetBool(SettingsKeys.ScanInspectorCollapsed), Is.True);
        }
    }

    private ScanInspectorViewModel Create()
    {
        return new(_settings, new FakeClipboard());
    }

    private ScanNodeViewModel Node()
    {
        var space = new DirectorySpace(@"C:\inspector", null, DateTime.Now, DateTime.Now);
        return _factory.CreateRoot(space, new());
    }
}
