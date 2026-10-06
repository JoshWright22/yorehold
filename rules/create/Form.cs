using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// One field of a form for editing a JSON definition file without a screen made for each kind.
/// It turns typed text into the value it stands for and says what is wrong with the value a file
/// has. Ported from the framework's FormField.
/// </summary>
public sealed class FormField
{
    public enum Kind
    {
        /// <summary>A string.</summary>
        Text,
        /// <summary>A whole number.</summary>
        Integer,
        /// <summary>Any number.</summary>
        Number,
        /// <summary>True or false.</summary>
        Flag,
        /// <summary>One string out of Options and the list OptionsFrom names.</summary>
        Choice,
        /// <summary>Strings, typed "a, b, c"; checked against the same lists when they are given.</summary>
        List,
        /// <summary>Anything, typed as JSON (nested parts the form has no fields for).</summary>
        Json,
    }

    private static readonly (Kind Type, string Name)[] TypeNames =
    {
        (Kind.Text, "text"), (Kind.Integer, "integer"), (Kind.Number, "number"), (Kind.Flag, "flag"),
        (Kind.Choice, "choice"), (Kind.List, "list"), (Kind.Json, "json"),
    };

    public string Key { get; init; } = "";
    /// <summary>Shown; empty = the key.</summary>
    public string Label { get; init; } = "";
    public string Help { get; init; } = "";
    public Kind Type { get; init; } = Kind.Text;
    public bool Required { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public List<string> Options { get; init; } = new();
    public string OptionsFrom { get; init; } = "";
    /// <summary>What a new entry starts with; null = left out (required fields get an empty value).</summary>
    public JsonNode? Fallback { get; init; }

    public string Title => Label.Length == 0 ? Key : Label;

    public static Kind? TypeFromName(string name)
    {
        foreach ((Kind type, string text) in TypeNames)
        {
            if (text == name)
            {
                return type;
            }
        }
        return null;
    }

    /// <summary>Options then the named list, without repeats.</summary>
    public List<string> Choices(IReadOnlyDictionary<string, List<string>> lists)
    {
        var out_ = new List<string>();
        foreach (string value in Options.Concat(OptionsFrom.Length > 0 && lists.TryGetValue(OptionsFrom, out List<string>? named) ? named : new List<string>()))
        {
            if (!out_.Contains(value))
            {
                out_.Add(value);
            }
        }
        return out_;
    }

    /// <summary>As a text box shows it: "" when the key is missing, "a, b" for a list, compact JSON for the rest.</summary>
    public string Text(JsonObject entry)
    {
        if (!entry.TryGetPropertyValue(Key, out JsonNode? value))
        {
            return "";
        }
        if (FormJson.IsString(value, out string? text))
        {
            return text;
        }
        if (Type == Kind.List && value is JsonArray list && list.All(v => FormJson.IsString(v, out _)))
        {
            return string.Join(", ", list.Select(v => v!.GetValue<string>()));
        }
        return CreateJson.Compact(value);
    }

    /// <summary>
    /// What typed text stands for. Null value with true means leave the key out (blank text on a
    /// field that isn't required). False, with error filled, when it can't be this field's value.
    /// </summary>
    public bool Parse(string typed, out JsonNode? value, out string error)
    {
        value = null;
        error = "";
        bool Fail(string why, out string message)
        {
            message = Title + " " + why;
            return false;
        }
        string text = Type == Kind.Text ? typed : typed.Trim(' ', '\t', '\r', '\n');
        if (text.Length == 0 && Type != Kind.Text)
        {
            value = Required ? EmptyOf(Type) : null;
            return true;
        }
        switch (Type)
        {
            case Kind.Text:
                value = text.Length == 0 ? (Required ? JsonValue.Create("") : null) : JsonValue.Create(text);
                return true;
            case Kind.Integer:
            {
                if (text.StartsWith('+') || !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long whole))
                {
                    return Fail("is a whole number", out error);
                }
                string range = OutOfRange(whole);
                if (range.Length > 0)
                {
                    return Fail(range, out error);
                }
                value = JsonValue.Create(whole);
                return true;
            }
            case Kind.Number:
            {
                if (text.StartsWith('+') || !double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                {
                    return Fail("is a number", out error);
                }
                string range = OutOfRange(number);
                if (range.Length > 0)
                {
                    return Fail(range, out error);
                }
                // written as typed: "3" stays a whole number, "3.5" doesn't
                value = text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && Math.Abs(number) < 9e15 ? JsonValue.Create((long)number) : JsonValue.Create(number);
                return true;
            }
            case Kind.Flag:
                if (text is "true" or "yes" or "1")
                {
                    value = JsonValue.Create(true);
                    return true;
                }
                if (text is "false" or "no" or "0")
                {
                    value = JsonValue.Create(false);
                    return true;
                }
                return Fail("is true or false", out error);
            case Kind.Choice:
                value = JsonValue.Create(text);
                return true;
            case Kind.List:
            {
                List<string> items = text.Split(',').Select(i => i.Trim(' ', '\t', '\r', '\n')).Where(i => i.Length > 0).ToList();
                value = items.Count == 0 && !Required ? null : CreateJson.Texts(items);
                return true;
            }
            case Kind.Json:
                try
                {
                    value = JsonNode.Parse(text);
                    return true;
                }
                catch (JsonException)
                {
                    return Fail("isn't valid JSON", out error);
                }
        }
        return Fail("has an unknown type", out error);
    }

    /// <summary>
    /// "" when the entry's value fits, else why. An item that isn't among the offered choices sets
    /// unknown instead of being refused, since the lists may not know everything the game will.
    /// </summary>
    public string Problem(JsonObject entry, IReadOnlyDictionary<string, List<string>> lists, out bool unknown)
    {
        unknown = false;
        if (!entry.TryGetPropertyValue(Key, out JsonNode? value))
        {
            return Required ? "is missing" : "";
        }
        List<string> allowed = Choices(lists);
        bool Known(string item) => allowed.Count == 0 || allowed.Contains(item);
        switch (Type)
        {
            case Kind.Text:
                if (!FormJson.IsString(value, out string? text))
                {
                    return "is text";
                }
                return Required && text.Length == 0 ? "is empty" : "";
            case Kind.Integer:
                if (!FormJson.IsInteger(value))
                {
                    return "is a whole number";
                }
                return OutOfRange(FormJson.Number(value));
            case Kind.Number:
                if (!FormJson.IsNumber(value))
                {
                    return "is a number";
                }
                return OutOfRange(FormJson.Number(value));
            case Kind.Flag:
                return value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? "" : "is true or false";
            case Kind.Choice:
            {
                // something other than a name (a file's long form, like an object of changes) is
                // left to the reader of the file; the form can only replace it with a name
                if (!FormJson.IsString(value, out string? name))
                {
                    unknown = true;
                    return "";
                }
                if (Required && name.Length == 0)
                {
                    return "is empty";
                }
                unknown = name.Length > 0 && !Known(name);
                return "";
            }
            case Kind.List:
                if (value is not JsonArray list || !list.All(v => FormJson.IsString(v, out _)))
                {
                    return "is a list of names";
                }
                unknown = !list.All(v => Known(v!.GetValue<string>()));
                return "";
        }
        return "";
    }

    // The empty value a required field starts with.
    internal static JsonNode EmptyOf(Kind type) => type switch
    {
        Kind.Integer or Kind.Number => JsonValue.Create(0),
        Kind.Flag => JsonValue.Create(false),
        Kind.List => new JsonArray(),
        Kind.Json => new JsonObject(),
        _ => JsonValue.Create(""),
    };

    private string OutOfRange(double value)
    {
        if (Min is double low && value < low)
        {
            return "is at least " + NumberText(low);
        }
        if (Max is double high && value > high)
        {
            return "is at most " + NumberText(high);
        }
        return "";
    }

    private static string NumberText(double value)
    {
        return value == Math.Floor(value) && Math.Abs(value) < 9e15 ? ((long)value).ToString(CultureInfo.InvariantCulture) : value.ToString("R", CultureInfo.InvariantCulture);
    }
}

/// <summary>A kind's form: its fields, where its files go and the key the file name follows. Ported from the framework's FormSchema.</summary>
public sealed class FormSchema
{
    private static readonly string[] FieldKeys = { "key", "type", "label", "help", "required", "min", "max", "options", "optionsFrom", "default" };

    /// <summary>"item".</summary>
    public string Id { get; init; } = "";
    /// <summary>"Items"; empty = the id.</summary>
    public string Label { get; init; } = "";
    /// <summary>Where its files go: "items".</summary>
    public string Folder { get; init; } = "";
    /// <summary>The key the file name follows; empty = the file name is the only id.</summary>
    public string IdKey { get; init; } = "id";
    public List<FormField> Fields { get; init; } = new();

    public FormField? Field(string key) => Fields.Find(f => f.Key == key);

    /// <summary>A new entry: the id (under IdKey) and every field's fallback, in field order.</summary>
    public JsonObject Blank(string id)
    {
        var made = new JsonObject();
        if (IdKey.Length > 0)
        {
            made[IdKey] = id;
        }
        foreach (FormField f in Fields.Where(f => f.Key != IdKey))
        {
            if (f.Fallback != null)
            {
                made[f.Key] = f.Fallback.DeepClone();
            }
            else if (f.Required)
            {
                made[f.Key] = FormField.EmptyOf(f.Type);
            }
        }
        return made;
    }

    /// <summary>"name: is missing" for each field that doesn't fit; warnings gets the unknown choices.</summary>
    public List<string> Problems(JsonObject entry, IReadOnlyDictionary<string, List<string>> lists, List<string>? warnings = null)
    {
        var found = new List<string>();
        foreach (FormField f in Fields)
        {
            string why = f.Problem(entry, lists, out bool unknown);
            if (why.Length > 0)
            {
                found.Add($"{f.Title}: {why}");
            }
            else if (unknown)
            {
                warnings?.Add($"{f.Title}: names something that isn't offered here");
            }
        }
        return found;
    }

    /// <summary>Keys the entry has that no field (or IdKey) covers.</summary>
    public List<string> Unlisted(JsonObject entry) => entry.Select(p => p.Key).Where(k => k != IdKey && Field(k) == null).ToList();

    /// <summary>
    /// {"id", "label", "folder", "idKey", "fields": [{"key", "type", "label", "help", "required",
    /// "min", "max", "options", "optionsFrom", "default"}]}. Other keys on the form itself are the
    /// caller's and are ignored; unknown keys on a field are refused.
    /// </summary>
    public static FormSchema? Read(JsonNode? j, out string error)
    {
        error = "";
        FormSchema? Fail(string why, out string message)
        {
            message = why;
            return null;
        }
        bool Text(JsonObject from, string key, ref string into)
        {
            if (!from.TryGetPropertyValue(key, out JsonNode? value))
            {
                return true;
            }
            if (!FormJson.IsString(value, out string? text))
            {
                return false;
            }
            into = text;
            return true;
        }
        if (j is not JsonObject o)
        {
            return Fail("a form is an object", out error);
        }
        string id = "", label = "", folder = "", idKey = "id";
        if (!Text(o, "id", ref id) || id.Length == 0)
        {
            return Fail("a form needs an id", out error);
        }
        string at = id + ": ";
        if (!Text(o, "label", ref label) || !Text(o, "folder", ref folder) || !Text(o, "idKey", ref idKey))
        {
            return Fail(at + "label, folder and idKey are text", out error);
        }
        if (o["fields"] is not JsonArray list)
        {
            return Fail(at + "fields is a list", out error);
        }
        var schema = new FormSchema { Id = id, Label = label, Folder = folder, IdKey = idKey };
        foreach (JsonNode? item in list)
        {
            string key = "";
            if (item is not JsonObject f || !Text(f, "key", ref key) || key.Length == 0)
            {
                return Fail(at + "each field is an object with a key", out error);
            }
            string where = at + key + ": ";
            foreach (KeyValuePair<string, JsonNode?> member in f)
            {
                if (Array.IndexOf(FieldKeys, member.Key) < 0)
                {
                    return Fail(where + $"unknown field \"{member.Key}\"", out error);
                }
            }
            if (schema.Field(key) != null || key == schema.IdKey)
            {
                return Fail(where + "is listed twice", out error);
            }
            string typeName = "text";
            if (!Text(f, "type", ref typeName))
            {
                return Fail(where + "type is text", out error);
            }
            if (FormField.TypeFromName(typeName) is not FormField.Kind type)
            {
                return Fail(where + $"unknown type \"{typeName}\"", out error);
            }
            string fieldLabel = "", help = "", optionsFrom = "";
            if (!Text(f, "label", ref fieldLabel) || !Text(f, "help", ref help) || !Text(f, "optionsFrom", ref optionsFrom))
            {
                return Fail(where + "label, help and optionsFrom are text", out error);
            }
            bool required = false;
            if (f.TryGetPropertyValue("required", out JsonNode? req))
            {
                if (req?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return Fail(where + "required is true or false", out error);
                }
                required = req.GetValue<bool>();
            }
            double? min = null, max = null;
            foreach (string bound in new[] { "min", "max" })
            {
                if (f.TryGetPropertyValue(bound, out JsonNode? b))
                {
                    if (!FormJson.IsNumber(b))
                    {
                        return Fail(where + bound + " is a number", out error);
                    }
                    if (bound == "min")
                    {
                        min = FormJson.Number(b);
                    }
                    else
                    {
                        max = FormJson.Number(b);
                    }
                }
            }
            if (min > max)
            {
                return Fail(where + "min is more than max", out error);
            }
            var options = new List<string>();
            if (f.TryGetPropertyValue("options", out JsonNode? given))
            {
                if (given is not JsonArray choices || !choices.All(c => FormJson.IsString(c, out _)))
                {
                    return Fail(where + "options is a list of names", out error);
                }
                options.AddRange(choices.Select(c => c!.GetValue<string>()));
            }
            var field = new FormField
            {
                Key = key,
                Type = type,
                Label = fieldLabel,
                Help = help,
                OptionsFrom = optionsFrom,
                Required = required,
                Min = min,
                Max = max,
                Options = options,
                Fallback = f.TryGetPropertyValue("default", out JsonNode? fallback) ? fallback?.DeepClone() : null,
            };
            if (f.ContainsKey("default"))
            {
                var probe = new JsonObject { [key] = field.Fallback?.DeepClone() };
                string why = field.Problem(probe, new Dictionary<string, List<string>>(), out _);
                if (why.Length > 0)
                {
                    return Fail(where + "default " + why, out error);
                }
            }
            schema.Fields.Add(field);
        }
        return schema;
    }

    public static FormSchema? Read(string text, out string error)
    {
        try
        {
            return Read(JsonNode.Parse(text), out error);
        }
        catch (JsonException)
        {
            error = "the form isn't valid JSON";
            return null;
        }
    }
}

/// <summary>What kind of JSON value a node is, the way the forms ask it.</summary>
public static class FormJson
{
    public static bool IsString(JsonNode? node, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        text = null;
        if (node?.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }
        text = node.GetValue<string>();
        return true;
    }

    public static bool IsNumber(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number;

    /// <summary>A number written without a point or an exponent.</summary>
    public static bool IsInteger(JsonNode? node) => IsNumber(node) && node!.ToJsonString().IndexOfAny(new[] { '.', 'e', 'E' }) < 0;

    public static double Number(JsonNode? node) => double.Parse(node!.ToJsonString(), CultureInfo.InvariantCulture);
}
