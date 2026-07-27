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

    [Test]
    public void Без_запроса_к_CLI_каталога_у_OpenCode_нет()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AgentModels.For(AgentBackendKind.OpenCode), Is.Empty);
            Assert.That(AgentModels.Efforts(AgentBackendKind.OpenCode, "opencode/big-pickle").Select(option => option.Id), Does.Contain("minimal"));
        }
    }

    [Test]
    public void Список_моделей_OpenCode_читается_из_вывода_CLI()
    {
        var models = AgentModels.ParseOpenCodeModels(OpenCodeListing);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Select(model => model.Id), Is.EqualTo(new[] { "opencode/big-pickle", "opencode/laguna-s-2.1-free", "opencode/gpt-5.6-sol" }));
            Assert.That(models[0].Title, Is.EqualTo("Big Pickle"));
            Assert.That(models[0].Hint, Does.StartWith("Бесплатная"));
            Assert.That(models[0].Hint, Does.Contain("контекст"));
            Assert.That(models[2].Hint, Does.StartWith("$5"));
        }
    }

    [Test]
    public void Варианты_модели_становятся_её_уровнями_рассуждений()
    {
        var models = AgentModels.ParseOpenCodeModels(OpenCodeListing);
        var laguna = models.First(model => model.Id == "opencode/laguna-s-2.1-free");

        Assert.That(AgentModels.EffortsFor(laguna, AgentBackendKind.OpenCode).Select(option => option.Id), Is.EqualTo(new[] { "", "low", "medium", "high" }));
    }

    [Test]
    public void Модель_без_вариантов_уровней_не_предлагает()
    {
        var models = AgentModels.ParseOpenCodeModels(OpenCodeListing);
        var pickle = models.First(model => model.Id == "opencode/big-pickle");

        Assert.That(AgentModels.EffortsFor(pickle, AgentBackendKind.OpenCode), Is.EqualTo(new[] { AgentModels.EffortDefault }));
    }

    [Test]
    public void Неизвестной_модели_достаются_уровни_бэкенда()
    {
        Assert.That(AgentModels.EffortsFor(null, AgentBackendKind.OpenCode).Select(option => option.Id), Does.Contain("high"));
    }

    [TestCase("")]
    [TestCase("opencode/big-pickle")]
    [TestCase("не json вообще\n{ поломанный }\n")]
    public void Непригодный_вывод_списка_даёт_пустой_каталог(string output)
    {
        Assert.That(AgentModels.ParseOpenCodeModels(output), Is.Empty);
    }

    private const string OpenCodeListing = """
                                           opencode/big-pickle
                                           {
                                             "id": "big-pickle",
                                             "providerID": "opencode",
                                             "name": "Big Pickle",
                                             "cost": {
                                               "input": 0,
                                               "output": 0
                                             },
                                             "limit": {
                                               "context": 200000,
                                               "output": 32000
                                             },
                                             "variants": {}
                                           }
                                           opencode/laguna-s-2.1-free
                                           {
                                             "id": "laguna-s-2.1-free",
                                             "providerID": "opencode",
                                             "name": "Laguna S 2.1 Free",
                                             "cost": {
                                               "input": 0,
                                               "output": 0
                                             },
                                             "limit": {
                                               "context": 256000
                                             },
                                             "variants": {
                                               "low": {
                                                 "reasoningEffort": "low"
                                               },
                                               "medium": {
                                                 "reasoningEffort": "medium"
                                               },
                                               "high": {
                                                 "reasoningEffort": "high"
                                               }
                                             }
                                           }
                                           opencode/gpt-5.6-sol
                                           {
                                             "id": "gpt-5.6-sol",
                                             "providerID": "opencode",
                                             "name": "GPT-5.6 Sol",
                                             "cost": {
                                               "input": 5,
                                               "output": 30
                                             },
                                             "limit": {
                                               "context": 400000
                                             }
                                           }
                                           """;
}
