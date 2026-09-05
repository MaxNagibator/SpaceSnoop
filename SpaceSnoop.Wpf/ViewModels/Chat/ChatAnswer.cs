using System.Buffers;

namespace SpaceSnoop.Wpf.ViewModels.Chat;

public static class ChatAnswer
{
    private static readonly SearchValues<char> Terminators = SearchValues.Create(".!?…:;»)]\"'`*");

    private static readonly SearchValues<char> SentenceEnd = SearchValues.Create(".!?…");

    // TODO: обрыв распознаётся по концу текста – ни claude, ни codex, ни opencode не сообщают в потоке
    // причину остановки хода. Брать причину из потока, как только её начнёт присылать хоть один CLI.
    public static bool LooksTruncated(string text)
    {
        var trimmed = text.AsSpan().TrimEnd();

        if (trimmed.IsEmpty || Terminators.Contains(trimmed[^1]))
        {
            return false;
        }

        return trimmed[..^1].IndexOfAny(SentenceEnd) >= 0;
    }
}
