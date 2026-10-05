using Yorehold.Rules;

namespace Yorehold.Rules.Tests;

public class InputScriptTests
{
    [Fact]
    public void ReadsTheOldScriptFormat()
    {
        List<InputStep> steps = InputScript.Parse("2 key Return\r\n20 move 1000 360\r\n21 down left\n740 shot .dev/a.png\n");

        Assert.Equal(4, steps.Count);
        Assert.Equal(new InputStep(2, "key", "Return", ""), steps[0]);
        Assert.Equal(new InputStep(20, "move", "1000", "360"), steps[1]);
        Assert.Equal(new InputStep(21, "down", "left", ""), steps[2]);
        Assert.Equal(new InputStep(740, "shot", ".dev/a.png", ""), steps[3]);
    }

    [Fact]
    public void TextKeepsItsSpaces()
    {
        List<InputStep> steps = InputScript.Parse("5 text hello there  world");

        Assert.Equal(new InputStep(5, "text", "hello there  world", ""), Assert.Single(steps));
    }

    [Fact]
    public void SkipsCommentsAndBlankLines()
    {
        List<InputStep> steps = InputScript.Parse("# open the menu\n\n   \n3 key F6\nnot a step\n7\n");

        Assert.Equal(new InputStep(3, "key", "F6", ""), Assert.Single(steps));
    }
}
