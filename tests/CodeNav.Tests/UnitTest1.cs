namespace CodeNav.Tests;

using EventHorizon.CodeNav;

public class CodeNavTests
{
    [Fact]
    public void RunSkeleton_ExtractsClassesAndMethods()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "SampleTestClass.cs");
        File.WriteAllText(tempFile, @"
namespace Sample;
public class Calculator
{
    public int Add(int a, int b) => a + b;
    public string Name { get; set; } = """";
}");

        try
        {
            var sw = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(sw);

            int exitCode = Program.Main(new[] { "skeleton", tempFile });
            Console.SetOut(originalOut);

            Assert.Equal(0, exitCode);
            string output = sw.ToString();
            Assert.Contains("CLASS     | Calculator", output);
            Assert.Contains("METHOD    | int Add(int a, int b)", output);
            Assert.Contains("PROP      | string Name", output);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RunSymbols_ExtractsTargetMethod()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "SampleTestSymbol.cs");
        File.WriteAllText(tempFile, @"
namespace Sample;
public class Greeter
{
    public string SayHello(string name)
    {
        return $""Hello, {name}!"";
    }
}");

        try
        {
            var sw = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(sw);

            int exitCode = Program.Main(new[] { "symbol", $"{tempFile}: SayHello", "--all" });
            Console.SetOut(originalOut);

            Assert.Equal(0, exitCode);
            string output = sw.ToString();
            Assert.Contains("SayHello", output);
            Assert.Contains("return $\"Hello, {name}!\";", output);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RunSlice_ReturnsErrorCode_AndDisabledMessage()
    {
        var swErr = new StringWriter();
        var originalErr = Console.Error;
        Console.SetError(swErr);

        int exitCode = Program.Main(new[] { "slice", "dummy.cs:1-10" });
        Console.SetError(originalErr);

        Assert.Equal(1, exitCode);
        string errOutput = swErr.ToString();
        Assert.Contains("'slice' command is disabled", errOutput);
    }
}
