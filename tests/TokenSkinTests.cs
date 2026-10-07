namespace Yorehold.Rules.Tests;

public class TokenSkinTests
{
    [Fact]
    public void EveryTokenHasASide()
    {
        using WorldFixture fixture = WorldFixture.Load("chapters/goblin-keep", 2);
        World w = fixture.World;
        Assert.Equal(TokenSide.Mine, w.SideOf(0));
        w.Tokens.Tokens[1].Owner = w.Tokens.LocalPlayer + 1;
        Assert.True(w.SideOf(1) == TokenSide.Party, "A hero another player owns is the party's, not mine");
        int enemy = Enumerable.Range(w.HeroCount, w.Creatures.Count - w.HeroCount).First(i => w.Creatures[i].Team == 1);
        Assert.Equal(TokenSide.Enemy, w.SideOf(enemy));
        w.Creatures[enemy].Team = 2;
        Assert.Equal(TokenSide.Neutral, w.SideOf(enemy));
        w.Creatures[enemy].Team = 0;
        Assert.True(w.SideOf(enemy) == TokenSide.Ally, "Someone on the party's side who isn't a hero is an ally");
        Assert.Equal(TokenSide.Neutral, w.SideOf(-1));
    }

    [Fact]
    public void ASkinSaysHowItsFramesSit()
    {
        TokenSkin plain = TokenSkin.Read(ContentNode.Parse("token.json", "{}"));
        Assert.True(!plain.Square && plain.Inset == 0.1, "With nothing said, faces are round and a tenth in");
        TokenSkin square = TokenSkin.Read(ContentNode.Parse("token.json", "{\"shape\": \"square\", \"inset\": 0.15}"));
        Assert.True(square.Square && square.Inset == 0.15);
        ContentException error = TestContent.Refused(() => TokenSkin.Read(ContentNode.Parse("token.json", "{\"shape\": \"star\"}")));
        Assert.Contains("shape: is round or square", error.Message);
        TestContent.Refused(() => TokenSkin.Read(ContentNode.Parse("token.json", "{\"inset\": 0.6}")));
        Assert.Equal("ui/tokens/enemy.png", TokenSkin.FramePath(TokenSide.Enemy));
    }
}
