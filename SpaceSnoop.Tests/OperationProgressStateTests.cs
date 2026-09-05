using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
public class OperationProgressStateTests
{
    [Test]
    public void Снимок_отдаёт_последний_отчёт()
    {
        var state = new OperationProgressState();

        state.Report(new(1, @"C:\первый", 100));
        state.Report(new(2, @"C:\второй", 300));

        Assert.That(state.CreateSnapshot(), Is.EqualTo(new OperationProgress(2, @"C:\второй", 300)));
    }

    [TestCase(5, 500)]
    [TestCase(9, 100)]
    public void Отставший_отчёт_счётчики_назад_не_откатывает(int completed, long bytes)
    {
        var state = new OperationProgressState();

        state.Report(new(10, @"C:\десятый", 1000));
        state.Report(new(completed, @"C:\отставший", bytes));

        var snapshot = state.CreateSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.Completed, Is.EqualTo(10));
            Assert.That(snapshot.Bytes, Is.EqualTo(1000));
            Assert.That(snapshot.Current, Is.EqualTo(@"C:\отставший"));
        }
    }

    [Test]
    public void Отчёты_из_нескольких_потоков_дают_наибольший_счётчик()
    {
        const int Threads = 8;
        const int PerThread = 10_000;

        var state = new OperationProgressState();
        var counter = 0;

        Parallel.For(0, Threads, _ =>
        {
            for (var index = 0; index < PerThread; index++)
            {
                var completed = Interlocked.Increment(ref counter);
                state.Report(new(completed, @"C:\файл", (long)completed * 4096));
            }
        });

        var snapshot = state.CreateSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.Completed, Is.EqualTo(Threads * PerThread));
            Assert.That(snapshot.Bytes, Is.EqualTo((long)Threads * PerThread * 4096));
        }
    }

    [Test]
    public void Сброс_не_даёт_прошлой_операции_сложиться_с_новой()
    {
        var state = new OperationProgressState();

        state.Report(new(7, @"C:\брошенный", 700));
        state.Reset();
        state.Report(new(1, @"C:\новый", 100));

        Assert.That(state.CreateSnapshot(), Is.EqualTo(new OperationProgress(1, @"C:\новый", 100)));
    }
}
