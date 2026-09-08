using System.Globalization;
using SharpCommander.Core.Utilities;
using Xunit;

namespace SharpCommander.Tests;

public class PathUtilsTests
{
    [Fact]
    public void NormalizeFullPath_TrimsTrailingSeparators_ButKeepsRoot()
    {
        using var dir = new TempDir();
        var withSeparator = dir.Path + Path.DirectorySeparatorChar;

        Assert.Equal(dir.Path, PathUtils.NormalizeFullPath(withSeparator));

        var root = Path.GetPathRoot(dir.Path)!;
        Assert.Equal(root, PathUtils.NormalizeFullPath(root));
    }

    [Fact]
    public void AreSamePath_IgnoresTrailingSeparatorAndRelativeSegments()
    {
        using var dir = new TempDir();
        var docs = dir.Dir("docs");

        Assert.True(PathUtils.AreSamePath(docs, docs + Path.DirectorySeparatorChar));
        Assert.True(PathUtils.AreSamePath(docs, Path.Combine(dir.Path, "other", "..", "docs")));
        Assert.False(PathUtils.AreSamePath(docs, Path.Combine(dir.Path, "docs2")));
    }

    [Fact]
    public void IsSameOrDescendant_DoesNotMatchSiblingWithPrefixName()
    {
        using var dir = new TempDir();
        var docs = dir.Dir("docs");
        var docs2 = dir.Dir("docs2");
        var inside = dir.Dir(Path.Combine("docs", "sub"));

        Assert.True(PathUtils.IsSameOrDescendant(docs, docs));
        Assert.True(PathUtils.IsSameOrDescendant(docs, inside));
        Assert.False(PathUtils.IsSameOrDescendant(docs, docs2));
        Assert.False(PathUtils.IsSameOrDescendant(inside, docs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    public void ValidateFileName_RejectsEmptyAndDotNames(string name)
    {
        Assert.NotNull(PathUtils.ValidateFileName(name));
    }

    [Fact]
    public void ValidateFileName_RejectsSeparatorsAndInvalidCharacters()
    {
        Assert.NotNull(PathUtils.ValidateFileName("a/b"));
        Assert.NotNull(PathUtils.ValidateFileName("../x.txt"));
        Assert.NotNull(PathUtils.ValidateFileName("bad\0name"));

        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(PathUtils.ValidateFileName("a\\b"));
            Assert.NotNull(PathUtils.ValidateFileName("name."));
            Assert.NotNull(PathUtils.ValidateFileName("name "));
            Assert.NotNull(PathUtils.ValidateFileName("CON"));
            Assert.NotNull(PathUtils.ValidateFileName("com1.txt"));
        }
    }

    [Theory]
    [InlineData("report.txt")]
    [InlineData(".gitignore")]
    [InlineData("my folder")]
    [InlineData("café (2).md")]
    public void ValidateFileName_AcceptsOrdinaryNames(string name)
    {
        Assert.Null(PathUtils.ValidateFileName(name));
    }

    [Fact]
    public void GetUniqueName_AppendsCounterUntilFree()
    {
        using var dir = new TempDir();

        Assert.Equal("new.txt", PathUtils.GetUniqueName(dir.Path, "new.txt"));

        dir.File("report.txt");
        Assert.Equal("report (2).txt", PathUtils.GetUniqueName(dir.Path, "report.txt"));

        dir.File("report (2).txt");
        Assert.Equal("report (3).txt", PathUtils.GetUniqueName(dir.Path, "report.txt"));
        Assert.Equal("report (3).txt", PathUtils.GetUniqueName(dir.Path, "report (2).txt"));

        dir.Dir("docs");
        Assert.Equal("docs (2)", PathUtils.GetUniqueName(dir.Path, "docs"));

        dir.File(".env");
        Assert.Equal(".env (2)", PathUtils.GetUniqueName(dir.Path, ".env"));
    }

    [Fact]
    public void PathComparison_IsCaseSensitiveOnlyOnLinux()
    {
        var expected = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        Assert.Equal(expected, PathUtils.PathComparison);
        Assert.Equal(!OperatingSystem.IsLinux(), PathUtils.PathComparer.Equals("A", "a"));
    }
}

public class NaturalStringComparerTests
{
    private static readonly NaturalStringComparer Comparer = new(CultureInfo.InvariantCulture);

    [Fact]
    public void SortsDigitRunsNumerically()
    {
        var names = new[] { "file10.txt", "file2.txt", "file1.txt", "File3.txt" };

        var sorted = names.OrderBy(n => n, Comparer).ToArray();

        Assert.Equal(new[] { "file1.txt", "file2.txt", "File3.txt", "file10.txt" }, sorted);
    }

    [Fact]
    public void IsCaseInsensitiveButDeterministic()
    {
        Assert.True(Comparer.Compare("alpha", "Bravo") < 0);
        Assert.True(Comparer.Compare("Bravo", "alpha") > 0);
        Assert.NotEqual(0, Comparer.Compare("File.txt", "file.txt"));
        Assert.Equal(0, Comparer.Compare("same", "same"));
    }

    [Fact]
    public void HandlesLeadingZerosMixedRunsAndNulls()
    {
        Assert.True(Comparer.Compare("7", "007") < 0);
        Assert.True(Comparer.Compare("007", "8") < 0);
        Assert.True(Comparer.Compare("a1b2", "a1b10") < 0);
        Assert.True(Comparer.Compare("a1", "ab") < 0);
        Assert.True(Comparer.Compare(null, "a") < 0);
        Assert.True(Comparer.Compare("a", null) > 0);
    }

    // ---- wildcard masks -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("*.cs", "Program.cs", true)]
    [InlineData("*.cs", "Program.csproj", false)]
    [InlineData("?.txt", "a.txt", true)]
    [InlineData("?.txt", "ab.txt", false)]
    [InlineData("*", "anything", true)]
    [InlineData("report_*.txt", "report_2026.txt", true)]
    [InlineData("report_*.txt", "summary_2026.txt", false)]
    [InlineData("*.cs;*.md", "notes.md", true)]
    [InlineData("*.cs *.md", "Program.cs", true)]
    [InlineData("*.cs;*.md", "image.png", false)]
    public void WildcardToRegex_MatchesWholeNames(string mask, string name, bool expected)
    {
        Assert.Equal(expected, PathUtils.WildcardToRegex(mask).IsMatch(name));
    }

    [Fact]
    public void WildcardToRegex_TreatsDotsAndBracketsAsLiterals()
    {
        var regex = PathUtils.WildcardToRegex("a.b[1].txt");

        Assert.Matches(regex, "a.b[1].txt");
        Assert.DoesNotMatch(regex, "axbx1x.txt");
    }

    [Fact]
    public void WildcardToRegex_RejectsAnEmptyMask()
    {
        Assert.Throws<ArgumentException>(() => PathUtils.WildcardToRegex("   "));
    }
}
