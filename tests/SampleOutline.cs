namespace Yorehold.Rules.Tests;

/// <summary>
/// The outline a reader would make of the tests' own book, The Old Mill (BookReaderTests): one
/// chapter of three places, a locked door, a fight, a chest, a ferryman to talk to and a quest.
/// Its words are the book's where the book has them; the rest is marked invented.
/// </summary>
public static class SampleOutline
{
    public const string Json = """
    {
      "format": "yorehold.outline",
      "version": 1,
      "title": "The Old Mill",
      "system": "",
      "entries": [
        {"id": "old-mill", "kind": "adventure", "data": {"title": "The Old Mill", "description": "A dry mill by the river."},
         "from": {"page": 1, "quote": "THE OLD MILL"}},
        {"id": "mill-chapter", "kind": "chapter", "data": {"title": "The Old Mill",
          "intro": ["The road to the mill runs along the river and the party follows it until dusk."], "completeWhen": ["mill_clear"]},
         "from": {"page": 1, "quote": "The road to the mill runs along the river"}},
        {"id": "marn", "kind": "hero", "data": {"name": "Marn", "class": "fighter", "color": [90, 140, 180]},
         "from": {"page": 1, "quote": "MARN"}, "picture": "pictures/p1-1.png"},
        {"id": "pell", "kind": "hero", "data": {"name": "Pell", "class": "cleric"}, "from": "invented"},
        {"id": "road", "kind": "place", "data": {"name": "The River Road", "label": "1", "size": [10, 6], "outdoors": true,
          "readAloud": ["The road to the mill runs along the river and the party follows it until dusk."]},
         "from": {"page": 1, "quote": "The road to the mill"}},
        {"id": "mill", "kind": "place", "data": {"name": "The Mill", "label": "2", "size": [8, 8],
          "readAloud": ["The wheel turns though the race is dry."]},
         "from": {"page": 1, "quote": "The wheel turns though the race is dry."}},
        {"id": "bank", "kind": "place", "data": {"name": "The Far Bank", "label": "3", "size": [6, 6], "outdoors": true,
          "readAloud": ["On the far bank a lantern swings from a pole where the ferryman waits for his fare."]},
         "from": {"page": 1, "quote": "On the far bank a lantern swings"}},
        {"id": "mill-door", "kind": "link", "data": {"from": "road", "to": "mill", "way": "locked", "key": "mill-key",
          "check": {"skill": "athletics", "difficulty": 12}}, "from": "invented"},
        {"id": "ford", "kind": "link", "data": {"from": "road", "to": "bank", "way": "open"}, "from": "invented"},
        {"id": "mill-key", "kind": "item", "data": {"name": "Mill key", "description": "Iron, and heavier than it looks.", "value": 0}, "from": "invented"},
        {"id": "mill-rat", "kind": "creature", "data": {"name": "Mill rat", "hp": 4, "armorClass": 12, "speed": 30,
          "token": {"color": [130, 110, 90], "size": 0.3}, "ai": "cunning"}, "from": "invented"},
        {"id": "rats", "kind": "encounter", "data": {"place": "mill", "text": "Rats pour out of the flour sacks!",
          "creatures": [{"creature": "mill-rat", "count": 2}, {"creature": "goblin", "name": "Snag"}], "set": ["mill_clear"]},
         "from": "invented"},
        {"id": "flour-chest", "kind": "container", "data": {"place": "mill", "name": "Flour chest", "items": ["healing-potion"], "coins": 30},
         "from": "invented"},
        {"id": "ferryman", "kind": "npc", "data": {"name": "The ferryman", "place": "bank", "dialogue": "ferryman-talk"},
         "from": {"page": 1, "quote": "the ferryman waits for his fare"}},
        {"id": "ferryman-talk", "kind": "dialogue", "data": {"start": "hello", "nodes": [
          {"id": "hello", "speaker": "The ferryman", "text": "Fare first. Then we talk about the mill.",
           "choices": [
             {"id": "pay", "text": "Here.", "next": "paid", "set": ["fare_paid"]},
             {"id": "bye", "text": "Not tonight.", "next": ""}]},
          {"id": "paid", "speaker": "The ferryman", "text": "The key's under the wheel. Mind the rats."}]},
         "from": "invented"},
        {"id": "fare", "kind": "quest", "data": {"title": "The Ferryman's Fare", "description": "The ferryman wants paying.",
          "objectives": [{"id": "pay", "text": "Pay the ferryman", "require": ["fare_paid"]}]},
         "from": {"page": 1, "quote": "waits for his fare"}},
        {"id": "lantern", "kind": "note", "data": {"text": "The lantern on the pole could signal someone across the river.", "place": "bank",
          "why": "no way to play a signal yet"}, "from": {"page": 1, "quote": "a lantern swings from a pole"}}
      ]
    }
    """;

    public static Outline Read() => Outline.Parse("outline.json", Json);
}
