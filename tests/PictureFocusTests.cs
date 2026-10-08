namespace Yorehold.Rules.Tests;

public class PictureFocusTests
{
    [Fact]
    public void AWidePaintingIsCutToASquareRoundItsMiddle()
    {
        (double x, double y, double w, double h) = PictureFocus.Middle.Cut(400, 200, 1);
        Assert.True((x, y, w, h) == (100, 0, 200, 200), "A round token shows a square, not the whole squashed picture");
    }

    [Fact]
    public void TheCutFollowsTheFocusButStaysInsideThePicture()
    {
        (double x, _, double w, _) = new PictureFocus(0.6, 0.5).Cut(400, 200, 1);
        Assert.True(x == 140 && w == 200, "Centred on the creature at three fifths across");
        (double edge, double top, _, _) = new PictureFocus(0.99, 0.0).Cut(400, 200, 1);
        Assert.True(edge == 200 && top == 0, "A face by the border is shown whole, with no blank space beside it");
    }

    [Fact]
    public void ZoomShowsLessAroundTheFocus()
    {
        (double x, double y, double w, double h) = new PictureFocus(0.25, 0.25, 2).Cut(400, 400, 1);
        Assert.True((x, y, w, h) == (0, 0, 200, 200));
        (_, _, double most, _) = new PictureFocus(0.5, 0.5, 100).Cut(400, 400, 1);
        Assert.Equal(400 / PictureFocus.MaxZoom, most);
    }

    [Fact]
    public void ATallCardTakesATallCut()
    {
        (double x, double y, double w, double h) = new PictureFocus(0.5, 0.2).Cut(300, 300, 0.5);
        Assert.True((x, y, w, h) == (75, 0, 150, 300));
    }

    [Fact]
    public void ACreatureSaysWhereItsPictureIsCut()
    {
        CreatureDefinition dragon = CreatureDefinition.Read(TestContent.Json("""
            {"id": "wyrmling", "token": {"color": [40, 40, 40], "image": "pictures/p38-1.png", "focus": [0.3, 0.25], "zoom": 1.5}}
            """));
        Assert.Equal(new PictureFocus(0.3, 0.25, 1.5), dragon.Token.Framing);
        Assert.Equal(PictureFocus.Middle, CreatureDefinition.Read(TestContent.Json("{\"id\": \"rat\"}")).Token.Framing);
        TestContent.Refused(() => CreatureDefinition.Read(TestContent.Json("{\"id\": \"rat\", \"token\": {\"color\": [1, 1, 1], \"focus\": [2, 0]}}")));
        TestContent.Refused(() => CreatureDefinition.Read(TestContent.Json("{\"id\": \"rat\", \"token\": {\"color\": [1, 1, 1], \"focus\": 0.5}}")));
    }
}
