namespace SpaceSnoop.Core.Cleanup;

public sealed class WorkloadCleanupPlan
{
    internal int Used;
    public DateTime ExpiresUtc => CreatedUtc.Add(CleanupDefaults.PlanLifetime);
    public string Executable { get; }
    public string WorkingDirectory { get; }
    public string SdkVersion { get; }
    public string InstalledWorkloads { get; }
    public string Command => "dotnet workload clean";
    public string WorkloadListLogPath { get; }

    internal Guid Owner { get; }
    internal DateTime CreatedUtc { get; }

    internal WorkloadCleanupPlan(
        Guid owner,
        string executable,
        string workingDirectory,
        string sdkVersion,
        string installedWorkloads,
        DateTime createdUtc,
        string workloadListLogPath)
    {
        Owner = owner;
        Executable = executable;
        WorkingDirectory = workingDirectory;
        SdkVersion = sdkVersion;
        InstalledWorkloads = installedWorkloads;
        CreatedUtc = createdUtc;
        WorkloadListLogPath = workloadListLogPath;
    }
}

public sealed class WorkloadInspectionException : InvalidOperationException
{
    public DotNetCliOutcome Outcome { get; }
    public int? ExitCode { get; }
    public string OutputTail { get; }
    public string LogPath { get; }
    public bool OutputTruncated { get; }

    internal WorkloadInspectionException(string message, DotNetCliResult result) : base(message)
    {
        Outcome = result.Outcome;
        ExitCode = result.ExitCode;
        OutputTail = result.OutputTail;
        LogPath = result.LogPath;
        OutputTruncated = result.OutputTruncated;
    }
}

public sealed record WorkloadCleanupReport(
    DotNetCliOutcome Outcome,
    int? ExitCode,
    string OutputTail,
    string LogPath,
    bool OutputTruncated,
    string? Error)
{
    public bool Cancelled => Outcome == DotNetCliOutcome.Cancelled;
    public bool TimedOut => Outcome == DotNetCliOutcome.TimedOut;
    public bool Succeeded => Outcome == DotNetCliOutcome.Succeeded;
}
