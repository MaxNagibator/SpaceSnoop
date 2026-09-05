using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.IO;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class DuplicateProgressPollTests
{
    private const int Copies = 60;

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"SpaceSnoopPoll_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var first = new string('п', 4096);
        var second = new string('в', 4096);

        for (var index = 0; index < Copies; index++)
        {
            File.WriteAllText(Path.Combine(_root, $"первый-{index:D3}.bin"), first);
            File.WriteAllText(Path.Combine(_root, $"второй-{index:D3}.bin"), second);
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public async Task Экран_поиска_дубликатов_обновляется_тиком_таймера_а_не_каждым_файлом()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = CreateDialog(dispatcher);
        var updates = Watch(dialog);

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.Report?.Examined, Is.EqualTo(Copies * 2), "Поиск не дошёл до всех файлов – проверять нечего.");
            Assert.That(updates.Value, Is.EqualTo(1), $"Экран обновился {updates.Value} раз на {Copies * 2} файлах – прогресс снова пушится на каждый объект.");
            Assert.That(dialog.CountText, Does.StartWith($"{Copies * 2:N0} · "), "Последний срез прогресса до экрана не доехал.");
        }
    }

    [Test]
    public async Task Таймер_опроса_заводится_один_и_останавливается_после_поиска()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = CreateDialog(dispatcher);

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Timers, Has.Count.EqualTo(1));
            Assert.That(dispatcher.Timers[0].Interval, Is.EqualTo(DuplicateProgressDialogViewModel.ProgressPollInterval));
            Assert.That(dispatcher.Timers[0].IsRunning, Is.False, "Таймер опроса пережил операцию.");
        }
    }

    [Test]
    public async Task Тик_таймера_показывает_накопленный_срез()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = CreateDialog(dispatcher);

        await dialog.StartCommand.ExecuteAsync(null);

        var updates = Watch(dialog);
        dispatcher.Timers[0].Tick();

        Assert.That(updates.Value, Is.Zero, "Тик после конца операции переписал итоговый срез.");
    }

    private DuplicateProgressDialogViewModel CreateDialog(FakeUiDispatcher dispatcher)
    {
        var root = new DiskSpaceCalculator().Calculate(new DirectoryInfo(_root));
        root.FixAbsolutePath(new(_root));

        return new(new(root, DuplicateOptions.Default), new DuplicateFinder(), dispatcher, NullLogger.Instance);
    }

    private static StrongBox<int> Watch(DuplicateProgressDialogViewModel dialog)
    {
        var updates = new StrongBox<int>(0);

        dialog.PropertyChanged += (_, arguments) =>
        {
            if (arguments.PropertyName == nameof(dialog.CountText))
            {
                updates.Value++;
            }
        };

        return updates;
    }
}
