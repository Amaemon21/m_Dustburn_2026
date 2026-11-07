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

    private static readonly (string Word, Gender Gender)[] Places =
    {
        ("долина", Gender.Feminine),
        ("низина", Gender.Feminine),
        ("гряда", Gender.Feminine),
        ("пустошь", Gender.Feminine),
        ("территория", Gender.Feminine),
        ("округ", Gender.Masculine),
        ("край", Gender.Masculine),
        ("кряж", Gender.Masculine),
        ("перевал", Gender.Masculine),
        ("урочище", Gender.Neuter),
        ("взгорье", Gender.Neuter),
        ("плато", Gender.Neuter),
        ("земли", Gender.Plural),
        ("пустоши", Gender.Plural),
        ("предгорья", Gender.Plural),
        ("холмы", Gender.Plural),
        ("топи", Gender.Plural)
    };

    private static readonly string[] Adjectives =
    {
        "Северн", "Южн", "Западн", "Восточн", "Стар", "Нов", "Мёртв", "Пыльн", "Ржав", "Пепельн",
        "Выжженн", "Забыт", "Проклят", "Брошенн", "Глух", "Сер", "Чёрн", "Красн", "Кровав", "Безымянн"
    };

    private static readonly string[] Starts =
    {
        "Ка", "Ве", "Мо", "Ри", "Та", "Ло", "Бра", "Гре", "Дру", "Зо", "Ке", "Ни", "Ор", "Са", "Ту", "Хе", "Яр", "Ми", "Ро", "Эль", "Ас", "Тор", "Вал", "Кор"
    };

    private static readonly string[] Middles =
    {
        "ла", "ри", "то", "ве", "на", "со", "ми", "де", "ру", "ка", "ле", "ро", "за", "ти"
    };

    private static readonly string[] Ends =
    {
        "ро", "ни", "ле", "то", "ри", "но", "ви", "ко", "де", "си", "мо", "ли", "те", "ну"
    };

    public static string Of(int seed)
    {
        var random = new Random(seed);
        (string place, Gender gender) = Places[random.Next(Places.Length)];
        string adjective = Adjective(Adjectives[random.Next(Adjectives.Length)], gender);
        string name = ProperName(random);
        int pattern = random.Next(100);

        if (pattern < WITH_ADJECTIVE_AND_NAME)
            return $"{adjective} {place} {name}";

        if (pattern < WITH_NAME)
            return $"{Capitalize(place)} {name}";

        return $"{adjective} {place}";
    }

    private static string ProperName(Random random)
    {
        string name = Starts[random.Next(Starts.Length)];
        int syllables = random.Next(100) < LONG_NAME_CHANCE ? 2 : 1;
        string previous = null;

        for (int i = 0; i < syllables; i++)
        {
            previous = Next(random, Middles, name, previous);
            name += previous;
        }

        return name + Next(random, Ends, name, previous);
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
        bool soft = stem.EndsWith("х", StringComparison.Ordinal);

        return gender switch
        {
            Gender.Feminine => stem + "ая",
            Gender.Neuter => stem + "ое",
            Gender.Plural => stem + (soft ? "ие" : "ые"),
            _ => stem + (soft ? "ой" : "ый")
        };
    }

    private static string Capitalize(string word) => char.ToUpperInvariant(word[0]) + word.Substring(1);
}
