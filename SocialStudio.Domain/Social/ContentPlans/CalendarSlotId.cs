namespace SocialStudio.Domain.Social.ContentPlans;

public readonly record struct CalendarSlotId(Guid Value)
{
    public static CalendarSlotId New() => new(Guid.NewGuid());
}
