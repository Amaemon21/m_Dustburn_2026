using System;

public static class WorldNames
{
    private enum Gender
    {
        Masculine,
        Feminine,
        Neuter,
        Plural
    }

    private const int WITH_ADJECTIVE_AND_NAME = 40;
    private const int WITH_NAME = 75;
    private const int LONG_NAME_CHANCE = 45;

    private static readonly (string Key, Gender Gender)[] PLACES =
    {
        ("worldNames.place.valley", Gender.Feminine),
        ("worldNames.place.lowland", Gender.Feminine),
        ("worldNames.place.ridge", Gender.Feminine),
        ("worldNames.place.wasteland", Gender.Feminine),
        ("worldNames.place.territory", Gender.Feminine),
        ("worldNames.place.district", Gender.Masculine),
        ("worldNames.place.region", Gender.Masculine),
        ("worldNames.place.highRidge", Gender.Masculine),
        ("worldNames.place.pass", Gender.Masculine),
        ("worldNames.place.tract", Gender.Neuter),
        ("worldNames.place.rise", Gender.Neuter),
        ("worldNames.place.plateau", Gender.Neuter),
        ("worldNames.place.lands", Gender.Plural),
        ("worldNames.place.wastes", Gender.Plural),
        ("worldNames.place.foothills", Gender.Plural),
        ("worldNames.place.hills", Gender.Plural),
        ("worldNames.place.bogs", Gender.Plural)
    };

    public static string Of(int seed)
    {
        string[] adjectives = Words("worldNames.adjectives");
        string[] starts = Words("worldNames.starts");
        string[] middles = Words("worldNames.middles");
        string[] ends = Words("worldNames.ends");

        if (adjectives.Length == 0 || starts.Length == 0 || middles.Length == 0 || ends.Length == 0)
            return $"World {seed}";

        var random = new Random(seed);
        (string placeKey, Gender gender) = PLACES[random.Next(PLACES.Length)];
        string place = LocalizationService.Text(placeKey);
        string adjective = Adjective(adjectives[random.Next(adjectives.Length)], gender);
        string name = ProperName(random, starts, middles, ends);
        int pattern = random.Next(100);

        if (pattern < WITH_ADJECTIVE_AND_NAME)
            return $"{adjective} {place} {name}";

        if (pattern < WITH_NAME)
            return $"{Capitalize(place)} {name}";

        return $"{adjective} {place}";
    }

    private static string[] Words(string key)
    {
        string text = LocalizationService.Text(key);
        return text == key ? Array.Empty<string>() : text.Split('|', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string ProperName(Random random, string[] starts, string[] middles, string[] ends)
    {
        string name = starts[random.Next(starts.Length)];
        int syllables = random.Next(100) < LONG_NAME_CHANCE ? 2 : 1;
        string previous = null;

        for (int i = 0; i < syllables; i++)
        {
            previous = Next(random, middles, name, previous);
            name += previous;
        }

        return name + Next(random, ends, name, previous);
    }

    private static string Next(Random random, string[] syllables, string name, string previous)
    {
        string syllable = syllables[random.Next(syllables.Length)];

        for (int attempt = 0; attempt < syllables.Length && Clashes(syllable, name, previous); attempt++)
            syllable = syllables[random.Next(syllables.Length)];

        return syllable;
    }

    private static bool Clashes(string syllable, string name, string previous)
    {
        return syllable == previous || char.ToLowerInvariant(name[name.Length - 1]) == syllable[0] || name.EndsWith(syllable.Substring(1), StringComparison.Ordinal);
    }

    private static string Adjective(string stem, Gender gender)
    {
        bool soft = stem.EndsWith(LocalizationService.Text("worldNames.softStemEnding"), StringComparison.Ordinal);

        string endingKey = gender switch
        {
            Gender.Feminine => "worldNames.ending.feminine",
            Gender.Neuter => "worldNames.ending.neuter",
            Gender.Plural => soft ? "worldNames.ending.pluralSoft" : "worldNames.ending.plural",
            _ => soft ? "worldNames.ending.masculineSoft" : "worldNames.ending.masculine"
        };

        return stem + LocalizationService.Text(endingKey);
    }

    private static string Capitalize(string word) => char.ToUpperInvariant(word[0]) + word.Substring(1);
}
