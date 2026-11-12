namespace CreditBook.UI.Common;

public static class ScoringSettings
{
    public const int StartingScore = 100;
    public const int MaxScore = 100;
    public const int MinScore = 0;

    public const int OnTimePaymentBonus = 5;
    public const int LatePaymentPenalty = 15;
    public const int BecameOverduePenalty = 10;

    public const int WarningThreshold = 50;
}