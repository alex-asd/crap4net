namespace Crap4Net.Tests;

public class CliArgumentsParserTests
{
    private static CliOptions Parse(params string[] args) => CliArgumentsParser.Parse(args);

    [Fact]
    public void No_arguments_analyzes_everything_with_the_default_threshold()
    {
        var options = Parse();

        Assert.Equal(CliMode.All, options.Mode);
        Assert.Equal(8.0, options.Threshold);
        Assert.Empty(options.Paths);
        Assert.False(options.Json);
    }

    [Fact]
    public void Paths_select_paths_mode()
    {
        var options = Parse("src/A.cs", "lib");

        Assert.Equal(CliMode.Paths, options.Mode);
        Assert.Equal(["src/A.cs", "lib"], options.Paths);
    }

    [Fact]
    public void Everything_after_double_dash_is_a_path() =>
        Assert.Equal(["--json", "x"], Parse("--", "--json", "x").Paths);

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_stops_parsing_and_wins(string flag) =>
        Assert.Equal(CliMode.Help, Parse("--changed", flag, "--bogus").Mode);

    [Fact]
    public void Collects_options_in_both_spellings()
    {
        var options = Parse(
            "--changed", "--json", "--threshold=12.5",
            "--exclude", "Migrations/**", "--exclude=**/*Dto.cs",
            "--coverage-file", "a.xml", "--coverage-file=b/**/*.xml",
            "--coverage-command", "make coverage");

        Assert.Equal(CliMode.Changed, options.Mode);
        Assert.True(options.Json);
        Assert.Equal(12.5, options.Threshold);
        Assert.Equal(["Migrations/**", "**/*Dto.cs"], options.Excludes);
        Assert.Equal(["a.xml", "b/**/*.xml"], options.CoverageFiles);
        Assert.Equal("make coverage", options.CoverageCommand);
    }

    [Theory]
    [InlineData("--frobnicate")]
    [InlineData("--threshold")]
    [InlineData("--threshold", "abc")]
    [InlineData("--threshold", "-1")]
    [InlineData("--threshold", "8,5")]
    [InlineData("--changed", "src")]
    [InlineData("--coverage-command", "make")]
    [InlineData("--exclude")]
    public void Rejects_bad_usage(params string[] args) =>
        Assert.Throws<CliUsageException>(() => Parse(args));
}
