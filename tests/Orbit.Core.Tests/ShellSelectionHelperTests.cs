using System;
using Orbit.Core.Native;
using Xunit;

namespace Orbit.Core.Tests;

public class ShellSelectionHelperTests
{
    [Fact]
    public void ShellSelectionHelper_DoesNotThrow()
    {
        var files = ShellSelectionHelper.GetSelectedFiles(0, 0);
        Assert.NotNull(files);
    }
}
