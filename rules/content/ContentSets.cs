namespace Yorehold.Rules;

/// <summary>
/// The content sets a player has installed (more creatures, spells, feats... for one system): which
/// of them join a game. Only those for the system the adventure plays, and only those turned on;
/// one for another system, or one whose manifest can't be read, stays out with the reason.
/// </summary>
public static class ContentSets
{
    /// <summary>The set folders for system (an id like "pf2e"), in the order given, and why the rest stayed out.</summary>
    public static List<string> For(IEnumerable<string> folders, string system, ISet<string>? off, List<string> left)
    {
        var joined = new List<string>();
        foreach (string folder in folders)
        {
            string name = Path.GetFileName(folder.TrimEnd('/', '\\'));
            ContentPackage manifest;
            try
            {
                manifest = ContentPackage.Load(new ContentFiles(folder));
            }
            catch (ContentException error)
            {
                left.Add($"{name}: {error.Message}");
                continue;
            }
            string id = manifest.Id.Length > 0 ? manifest.Id : name;
            if (!manifest.IsSet)
            {
                left.Add($"{name}: a {(manifest.Kind.Length > 0 ? manifest.Kind : "package")}, not a content set");
            }
            else if (manifest.Ruleset.Split('@')[0] != system)
            {
                left.Add($"{name}: for {manifest.Ruleset.Split('@')[0]}, not {system}");
            }
            else if (off?.Contains(id) == true)
            {
                left.Add($"{name}: turned off");
            }
            else
            {
                joined.Add(folder);
            }
        }
        return joined;
    }
}
