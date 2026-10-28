using System.Collections.Generic;
using System.Linq;

internal static partial class Harness
{
    private static void VerifyConfigLists()
    {
        string[] list = { "wood", "stone", "wood", null, "stone", "plant" };
        List<int> repeats = ListDuplicates.Find(list, entry => entry);
        Check(repeats.SequenceEqual(new[] { 2, 4 }), "duplicates are found after their first entry, empty rows are skipped");

        int cleared = ListDuplicates.Clear(list, entry => entry, index => list[index] = null, null, "Items");
        Check(cleared == 2 && list[2] == null && list[4] == null && list[0] == "wood" && list[1] == "stone",
            "a repeated entry is cleared and the first one is kept");
        Check(ListDuplicates.Find(list, entry => entry).Count == 0, "a cleared list holds no duplicates");

        var never = new ContainerLoot(FakeItem("loot"), 2, 5, 0f);
        var always = new ContainerLoot(FakeItem("loot"), 2, 5, 1f);
        var fixedAmount = new ContainerLoot(FakeItem("loot"), 3, 0, 1f);
        var first = new System.Random(7);
        var second = new System.Random(7);
        bool deterministic = true;
        bool inRange = true;

        for (int i = 0; i < 64; i++)
        {
            int a = always.Roll(first);
            int b = always.Roll(second);
            deterministic &= a == b;
            inRange &= a >= 2 && a <= 5;
        }

        Check(never.Roll(new System.Random(1)) == 0, "loot with zero chance never drops");
        Check(inRange && deterministic, "loot rolls between Amount and MaxAmount and repeats for the same seed");
        Check(fixedAmount.Roll(new System.Random(1)) == 3, "loot without MaxAmount drops exactly Amount");
    }
}
