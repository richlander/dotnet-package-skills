using System.Text;

namespace CommandLineDemo.Tests;

public class CommandLineDemoTests
{
    [Fact]
    public void Greet_WithOptions_PrintsExpectedOutput()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = CommandLineDemoApp.Build(output, error);

        var exitCode = app.Execute("greet", "Mona", "--times", "2", "--excited");

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal("Hello, Mona!" + Environment.NewLine + "Hello, Mona!" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Greet_DefaultsToSingleGreeting()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = CommandLineDemoApp.Build(output, error);

        var exitCode = app.Execute("greet", "Sam");

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal("Hello, Sam." + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Sum_WithAbsoluteAndJsonFormat_PrintsExpectedOutput()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = CommandLineDemoApp.Build(output, error);

        var exitCode = app.Execute("sum", "1", "2", "3", "--absolute", "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal("{\"sum\":6}" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Sum_InvalidNumber_ReturnsError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = CommandLineDemoApp.Build(output, error);

        var exitCode = app.Execute("sum", "1", "oops");

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid number: oops", error.ToString());
        Assert.Equal(string.Empty, output.ToString());
    }
}
