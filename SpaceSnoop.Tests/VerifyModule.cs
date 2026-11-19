using DiffEngine;
using System.Runtime.CompilerServices;

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
