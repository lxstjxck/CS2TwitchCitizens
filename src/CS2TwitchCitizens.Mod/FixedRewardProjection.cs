using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CS2TwitchCitizens.Commands;
using CS2TwitchCitizens.Twitch;

namespace CS2TwitchCitizens.Mod
{
    [DataContract]
    public sealed class FixedRewardView
    {
        [DataMember(Name = "templateId")] public string TemplateId { get; set; } = "";
        [DataMember(Name = "rewardId")] public string RewardId { get; set; } = "";
        [DataMember(Name = "cost")] public long Cost { get; set; }
        [DataMember(Name = "enabled")] public bool Enabled { get; set; }
        [DataMember(Name = "status")] public string Status { get; set; } = "Missing";
        [DataMember(Name = "iconUrl")] public string IconUrl { get; set; } = "";
    }

    public static class FixedRewardProjection
    {
        public static FixedRewardView[] Build(TwitchReward[] all, TwitchReward[] owned,
            InteractiveSettings settings)
        {
            if (all == null || owned == null || settings == null) throw new ArgumentNullException();
            settings.Validate();
            if (all.Any(r => r == null || string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Title)) ||
                owned.Any(r => r == null || string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Title)))
                throw new System.IO.InvalidDataException("Invalid Twitch reward list.");
            var ownedIds = new HashSet<string>(owned.Select(r => r.Id), StringComparer.Ordinal);
            return FixedRewardCatalog.All.Select(template =>
            {
                var links = settings.Rewards.Where(r => r.Action == template.Id).ToArray();
                var link = links.Length == 1 ? links[0] : null;
                var reward = link == null ? null : all.FirstOrDefault(r => r.Id == link.RewardId);
                var conflict = links.Length > 1 || link != null && (reward == null || !ownedIds.Contains(link.RewardId)) ||
                    link == null && all.Any(r => r.Title == template.Title);
                return new FixedRewardView {
                    TemplateId = template.Id, RewardId = reward?.Id ?? "",
                    Cost = reward?.Cost ?? template.DefaultCost,
                    Enabled = reward?.IsEnabled == true && link?.Enabled == true,
                    Status = conflict ? "Conflict" : reward == null ? "Missing" :
                        reward.IsEnabled && link?.Enabled == true ? "Synced" : "Disabled",
                    IconUrl = TwitchRewardCatalog.SafeIcon(reward?.Image?.Url1x) ??
                        TwitchRewardCatalog.SafeIcon(reward?.DefaultImage?.Url1x) ?? ""
                };
            }).ToArray();
        }
    }
}
