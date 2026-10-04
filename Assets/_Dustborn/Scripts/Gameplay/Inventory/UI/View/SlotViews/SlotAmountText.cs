public static class SlotAmountText
{
    public static string Format(int amount) => amount <= 1 ? string.Empty : amount.ToString();
}
