using System;

namespace CreditBook.UI.Common;

public static class NotificationSettings
{
    public static readonly TimeSpan OverdueThreshold = TimeSpan.FromDays(30);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
}