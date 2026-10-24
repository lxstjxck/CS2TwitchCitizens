using System;
using System.Collections.Generic;
using CS2TwitchCitizens.Commands;
using CS2TwitchCitizens.Twitch;

namespace CS2TwitchCitizens.Mod
{
    /// <summary>Stable Twitch definitions. UI language never changes a server reward.</summary>
    public sealed class FixedRewardTemplate
    {
        public string Id { get; }
        public string Title { get; }
        public string Prompt { get; }
        public long DefaultCost { get; }
        public int DisplaySeconds { get; }
        public FixedRewardTemplate(string id, string title, string prompt, long cost, int seconds)
        { Id = id; Title = title; Prompt = prompt; DefaultCost = cost; DisplaySeconds = seconds; }
        public TwitchRewardEdit CreateEdit(long cost) => new TwitchRewardEdit {
            Title = Title, Prompt = Prompt, Cost = cost, BackgroundColor = "#286987",
            IsEnabled = true, IsUserInputRequired = true, SkipQueue = false
        };
    }

    public static class FixedRewardCatalog
    {
        public static readonly IReadOnlyList<FixedRewardTemplate> All = new[] {
            new FixedRewardTemplate(InteractiveAction.ShowMessage, "Сообщение в город",
                "Отправьте сообщение, которое появится в городе.", 500, 8),
            new FixedRewardTemplate(InteractiveAction.CityAnnouncement, "Городское объявление",
                "Опубликуйте большое объявление на экране стримера.", 1000, 30)
        };
        public static FixedRewardTemplate? Find(string id)
        {
            foreach (var item in All) if (item.Id == id) return item;
            return null;
        }
    }
}
