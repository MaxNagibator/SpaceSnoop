using System.Text;

namespace SpaceSnoop.Core.Cleanup;

public sealed record DotNetCleanOptions(string Configuration = "Debug", string? Framework = null, string? Runtime = null);

public sealed class DotNetCleanPlan
{
    internal int Used;
    public DateTime ExpiresUtc => CreatedUtc.Add(CleanupDefaults.PlanLifetime);
    public string Executable { get; }
    public string ProjectPath { get; }
    public string WorkingDirectory => Path.GetDirectoryName(ProjectPath)!;
    public string SdkVersion { get; }
    public DotNetCleanOptions Options { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string InspectionLogPath { get; }

    public string Command => string.Join(
        " ",
        new[]
            {
                Executable,
            }.Concat(Arguments)
            .Select(Quote));

    internal Guid Owner { get; }
    internal DotNetCleanInput Input { get; }
    internal DotNetCleanExecutable ExecutableInput { get; }
    internal DateTime CreatedUtc { get; }

    internal DotNetCleanPlan(
        Guid owner,
        string executable,
        string projectPath,
        string sdkVersion,
        DotNetCleanOptions options,
        IReadOnlyList<string> arguments,
        DotNetCleanInput input,
        DotNetCleanExecutable executableInput,
        DateTime createdUtc,
        string logPath)
    {
        Owner = owner;
        Executable = executable;
        ProjectPath = projectPath;
        SdkVersion = sdkVersion;
        Options = options;
        Arguments = arguments;
        Input = input;
        ExecutableInput = executableInput;
        CreatedUtc = createdUtc;
        InspectionLogPath = logPath;
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', (slashes * 2) + 1);
            }
            else
            {
                result.Append('\\', slashes);
            }

            result.Append(character);
            slashes = 0;
        }

        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}

internal sealed record DotNetCleanInput(
    CacheFileStamp Directory,
    CacheFileStamp Project,
    string ProjectHash,
    string? GlobalPath,
    CacheFileStamp? Global,
    string? GlobalHash);

internal sealed record DotNetCleanExecutable(CacheFileStamp File, string Hash);

public sealed record DotNetCleanReport(
    DotNetCliOutcome Outcome,
    int? ExitCode,
    string OutputTail,
    string LogPath,
    bool OutputTruncated,
    string? Error)
{
    public bool Succeeded => Outcome == DotNetCliOutcome.Succeeded;
    public bool Cancelled => Outcome == DotNetCliOutcome.Cancelled;
    public bool TimedOut => Outcome == DotNetCliOutcome.TimedOut;
}

public sealed class DotNetCleanInspectionException : InvalidOperationException
{
    public DotNetCliOutcome Outcome { get; }
    public int? ExitCode { get; }
    public string OutputTail { get; }
    public string LogPath { get; }
    public bool OutputTruncated { get; }

    internal DotNetCleanInspectionException(string message, DotNetCliResult result) : base(message)
    {
        Outcome = result.Outcome;
        ExitCode = result.ExitCode;
        OutputTail = result.OutputTail;
        LogPath = result.LogPath;
        OutputTruncated = result.OutputTruncated;
    }
}
