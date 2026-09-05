using SpaceSnoop.Wpf.Agent;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentCliQuoteTests
{
    [TestCase("plain", ExpectedResult = "plain")]
    [TestCase("two words", ExpectedResult = "\"two words\"")]
    [TestCase("C:\\Program Files\\app", ExpectedResult = "\"C:\\Program Files\\app\"")]
    [TestCase("quote\"mark", ExpectedResult = "\"quote\\\"mark\"")]
    [TestCase("path \\\\", ExpectedResult = "\"" + "path " + "\\" + "\\" + "\\" + "\\" + "\"")]
    [TestCase("path\\\\\"mark", ExpectedResult = "\"" + "path" + "\\" + "\\" + "\\" + "\\" + "\\" + "\"mark\"")]
    [TestCase("", ExpectedResult = "\"\"")]
    public string Кавычки_экранируют_аргументы_по_правилам_cmd(string value)
    {
        return AgentCli.Quote(value);
    }
}
