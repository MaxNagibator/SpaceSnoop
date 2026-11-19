using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
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
    public async Task Поиск_дубликатов_объединяет_обновления_прогресса_и_показывает_полный_счёт()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = CreateDialog(dispatcher);
        var updates = Watch(dialog);

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.Report?.Examined, Is.EqualTo(Copies * 2), "Поиск не дошёл до всех файлов – проверять нечего.");
            Assert.That(updates.Value, Is.Positive.And.LessThan(Copies), $"Экран обновился {updates.Value} раз на {Copies * 2} файлах.");
            Assert.That(dialog.CountText, Does.StartWith($"{Copies * 2:N0} · "), "Последний срез прогресса до экрана не доехал.");
        }
    }

    private DuplicateProgressDialogViewModel CreateDialog(FakeUiDispatcher dispatcher)
    {
        var root = new DiskSpaceCalculator().Calculate(new(_root));
        root.FixAbsolutePath(new(_root));

        return new(new(root, DuplicateOptions.Default), new(), dispatcher, NullLogger.Instance);
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
