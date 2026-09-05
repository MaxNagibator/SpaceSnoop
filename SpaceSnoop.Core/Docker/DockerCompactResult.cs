namespace SpaceSnoop.Core.Docker;

public enum DockerCompactStatus
{
    None = 0,
    Succeeded = 1,
    Failed = 2,
    RequiresWslShutdownConfirmation = 3,
    Canceled = 4,
}

public enum DockerCompactStage
{
    None = 0,
    Preparing = 1,
    Stopping = 2,
    Compacting = 3,
}

public readonly record struct DockerCompactMeasurement(long LogicalBytes, long AllocatedBytes)
{
    public string LogicalText => SizeFormatter.Format(LogicalBytes);

    public string AllocatedText => SizeFormatter.Format(AllocatedBytes);
}

public sealed record DockerCompactStep(string Name, bool Succeeded, string Detail);

public sealed record DockerCompactResult(
    DockerCompactStatus Status,
    string? VhdxPath,
    DockerCompactMeasurement? Before,
    DockerCompactMeasurement? After,
    IReadOnlyList<DockerCompactStep> Steps,
    IReadOnlyList<string> WslDistributions)
{
    public bool Succeeded => Status == DockerCompactStatus.Succeeded;

    public bool RequiresWslShutdownConfirmation => Status == DockerCompactStatus.RequiresWslShutdownConfirmation;

    public bool DockerStopped => Steps.Any(step => step.Succeeded
        && (step.Name is "Остановка Docker Desktop" or "Остановка WSL"));

    public long? FreedAllocatedBytes => Before is { } before && After is { } after
        ? before.AllocatedBytes - after.AllocatedBytes
        : null;

    public string Summary
    {
        get
        {
            if (Status == DockerCompactStatus.RequiresWslShutdownConfirmation)
            {
                return "Docker Desktop не удалось остановить штатно. Требуется отдельное подтверждение остановки WSL.";
            }

            if (Status == DockerCompactStatus.Canceled)
            {
                return DockerStopped
                    ? "Сжатие диска Docker отменено. Docker Desktop остановлен, запустите его заново."
                    : "Сжатие диска Docker отменено.";
            }

            if (!Succeeded)
            {
                var failure = Steps.FirstOrDefault(step => !step.Succeeded)?.Detail
                    ?? "Сжатие диска Docker не выполнено.";
                return DockerStopped ? $"{failure} Docker Desktop остановлен, запустите его заново." : failure;
            }

            if (After is null)
            {
                return "Сжатие выполнено. Итоговый размер образа не удалось измерить.";
            }

            return FreedAllocatedBytes is { } freed && freed > 0
                ? $"Сжатие выполнено. Освобождено на диске: {SizeFormatter.Format(freed)}."
                : "Сжатие выполнено, но освобождённого места не обнаружено.";
        }
    }

    public string ToDisplayText()
    {
        var lines = new List<string> { Summary };

        if (VhdxPath is not null)
        {
            lines.Add($"Образ: {VhdxPath}");
        }

        if (Before is { } before)
        {
            lines.Add($"До: логический размер {before.LogicalText}, занято на диске {before.AllocatedText}.");
        }

        if (After is { } after)
        {
            lines.Add($"После: логический размер {after.LogicalText}, занято на диске {after.AllocatedText}.");
        }

        lines.Add(string.Empty);
        lines.AddRange(Steps.Select(step => $"{(step.Succeeded ? "OK" : "Ошибка")}: {step.Name} – {step.Detail}"));

        return string.Join(Environment.NewLine, lines);
    }
}
