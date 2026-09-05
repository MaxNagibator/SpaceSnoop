using System.Runtime.CompilerServices;
using DiffEngine;

namespace SpaceSnoop.Tests;

internal static class VerifyModule
{
    [ModuleInitializer]
    public static void Initialize()
    {
        DiffRunner.Disabled = true;
        VerifierSettings.FixNewlinesOnRead();
        VerifierSettings.IgnoreTrailingNewline();
    }
}
