using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Duplicates;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DuplicateGroupTests
{
    [TestCase("CC", "", ExpectedResult = "без изменений")]
    [TestCase("CC", "1", ExpectedResult = "нет группы")]
    [TestCase("CCC", "1", ExpectedResult = "CC|2")]
    [TestCase("CHCC", "0", ExpectedResult = "CCC|3")]
    [TestCase("CHCC", "2", ExpectedResult = "CHC|2")]
    [TestCase("CHHC", "01", ExpectedResult = "CC|2")]
    [TestCase("CHS", "01", ExpectedResult = "нет группы")]
    [TestCase("CSC", "0", ExpectedResult = "нет группы")]
    [TestCase("CSS", "0", ExpectedResult = "нет группы")]
    [TestCase("CC/H", "0", ExpectedResult = "нет группы")]
    [TestCase("CH/C", "01", ExpectedResult = "нет группы")]
    [TestCase("CH/HC", "0", ExpectedResult = "CH/C|2")]
    [TestCase("CH/HC", "01", ExpectedResult = "CC|2")]
    [TestCase("CC/CC", "01", ExpectedResult = "CC|2")]
    [TestCase("CC/CCC", "0", ExpectedResult = "CC/CC|4")]
    public string Without_RecountsSurvivingMembers(string layout, string removed)
    {
        var shown = layout.IndexOf('/') is var split and >= 0 ? split : layout.Length;
        var kinds = layout.Replace("/", string.Empty);
        var parent = new DirectorySpace("root", null, DateTime.Now, DateTime.Now);
        var members = kinds.Select((kind, index) => new DuplicateMember(
                FileSpace.Create(new ScanEntry($"f{index}", 4096, DateTime.Now, DateTime.Now, FileAttributes.Normal, false), parent),
                $@"C:\root\f{index}",
                kind switch
                {
                    'H' => DuplicateMemberKind.HardLink,
                    'S' => DuplicateMemberKind.SymbolicLink,
                    _ => DuplicateMemberKind.Copy,
                }))
            .ToList();

        var group = shown < members.Count
            ? new DuplicateGroup(4096, members[..shown], kinds.Count(static x => x == 'C'), members[shown..])
            : new DuplicateGroup(4096, members, kinds.Count(static x => x == 'C'));

        var gone = removed.Select(x => members[x - '0'].Space).ToHashSet();

        var result = group.Without(gone.Contains);

        if (ReferenceEquals(result, group))
        {
            return "без изменений";
        }

        if (result is null)
        {
            return "нет группы";
        }

        var letters = Letters(result.Members);

        if (result.Hidden is { Count: > 0 } hidden)
        {
            letters += "/" + Letters(hidden);
        }

        return $"{letters}|{result.DistinctFiles}";
    }

    private static string Letters(IEnumerable<DuplicateMember> members)
    {
        return string.Concat(
            members.Select(static x => x.Kind switch
            {
                DuplicateMemberKind.HardLink => 'H',
                DuplicateMemberKind.SymbolicLink => 'S',
                _ => 'C',
            }));
    }
}
