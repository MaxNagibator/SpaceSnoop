using SpaceSnoop.Wpf.Agent;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentModelsTests
{
    private const string Cache = """
        {
          "fetched_at": "2026-07-01T00:00:00Z",
          "models": [
            {
              "slug": "gpt-slow",
              "display_name": "GPT Slow",
              "description": "Медленная",
              "visibility": "list",
              "priority": 7,
              "supported_reasoning_levels": [{ "effort": "low" }, { "effort": "high" }]
            },
            {
              "slug": "gpt-fast",
              "display_name": "GPT Fast",
              "description": "Быстрая",
              "visibility": "list",
              "priority": 1,
              "supported_reasoning_levels": [{ "effort": "low" }, { "effort": "medium" }, { "effort": "ultra" }]
            },
            {
              "slug": "codex-auto-review",
              "display_name": "Служебная",
              "visibility": "hide",
              "priority": 2,
              "supported_reasoning_levels": [{ "effort": "low" }]
            }
          ]
        }
        """;

    [Test]
    public void Кэш_даёт_модели_по_приоритету_без_скрытых()
    {
        var models = AgentModels.ParseCodexCache(Cache);

        Assert.That(models?.Select(model => model.Id), Is.EqualTo(new[] { "gpt-fast", "gpt-slow" }));
    }

    [Test]
    public void Модель_из_кэша_несёт_имя_описание_и_свои_уровни()
    {
        var model = AgentModels.ParseCodexCache(Cache)?[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model?.Title, Is.EqualTo("GPT Fast"));
            Assert.That(model?.Hint, Is.EqualTo("Быстрая"));
            Assert.That(model?.Efforts, Is.EqualTo(new[] { "low", "medium", "ultra" }));
        }
    }

    [TestCase("не json")]
    [TestCase("{}")]
    [TestCase("""{ "models": [] }""")]
    [TestCase("""{ "models": [{ "display_name": "Без слага", "visibility": "list" }] }""")]
    public void Непригодный_кэш_откатывает_к_статике(string json)
    {
        Assert.That(AgentModels.ParseCodexCache(json), Is.Null);
    }

    [Test]
    public void Уровни_известной_модели_ограничены_её_списком()
    {
        var efforts = AgentModels.Efforts(AgentBackendKind.Claude, "opus").Select(option => option.Id);

        Assert.That(efforts, Is.EqualTo(new[] { "", "low", "medium", "high", "xhigh", "max" }));
    }

    [Test]
    public void Незнакомой_модели_достаются_все_уровни_бэкенда()
    {
        var efforts = AgentModels.Efforts(AgentBackendKind.Codex, "своя-модель").Select(option => option.Id);

        Assert.That(efforts, Does.Contain("ultra"));
    }

    [Test]
    public void Первым_уровнем_идёт_умолчание_CLI()
    {
        Assert.That(AgentModels.Efforts(AgentBackendKind.Claude, string.Empty)[0], Is.EqualTo(AgentModels.EffortDefault));
    }
}
