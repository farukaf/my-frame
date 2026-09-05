using MyFrame.Core;

namespace MyFrame.Core.Tests;

public sealed class AlecaFramePathTests
{
    [Fact]
    public void NormalizesAndRaisesChangeOnlyForANewPath()
    {
        var initial = Path.Combine(Path.GetTempPath(), "AlecaA");
        var path = new AlecaFramePath(initial + Path.DirectorySeparatorChar);
        string? changed = null;
        path.Changed += (_, value) => changed = value;

        path.SetDirectory(initial);
        Assert.Null(changed);

        var replacement = Path.Combine(Path.GetTempPath(), "AlecaB");
        path.SetDirectory(replacement);
        Assert.Equal(Path.GetFullPath(replacement), changed);
    }
}
