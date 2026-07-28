using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace TORCareerUniques
{
    // Pure helper: no event registration, no static campaign state, no recurring work.
    public static class HostNavigationSafety
    {
        private const float TownClearance = 30f;
        private const float VillageClearance = 18f;
        private const float OtherClearance = 14f;

        public static void ResetSession()
        {
            // ABI-compatible no-op.
        }

        public static void MaintainHost(MobileParty party, string careerId, Settlement anchor)
        {
            // ABI-compatible no-op. Navigation safety is load/spawn-only.
        }

        public static CampaignVec2 ComputeSafeHomePosition(string careerId, Settlement anchor)
        {
            if (anchor == null)
                throw new ArgumentNullException("anchor");
            if (Campaign.Current == null)
                throw new InvalidOperationException("Campaign is not active.");

            return FindSafePositionAround(anchor);
        }

        public static bool RepairLegacyParty(MobileParty party, string careerId, Settlement anchor)
        {
            try
            {
                if (party == null || !party.IsActive || party.MapEvent != null ||
                    party.CurrentSettlement != null || party.AttachedTo != null || party.Ai == null)
                    return false;

                Settlement obstruction = FindContainingSettlement(party.Position);
                if (obstruction == null)
                    return false;

                CampaignVec2 safe = FindSafePositionAround(obstruction);
                party.Position = safe;

                // Do not mark the party as released here and do not assign a patrol
                // order. The normal encounter initialization path clears its old
                // order once and hands it back to native bandit AI.
                party.Ai.SetDoNotMakeNewDecisions(false);
                party.Ai.EnableAi();
                party.Ai.RethinkAtNextHourlyTick = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static CampaignVec2 FindSafePositionAround(Settlement centerSettlement)
        {
            // Follow Bannerlord's own BanditSpawnCampaignBehavior contract:
            // search from the settlement gate over half a campaign day's average
            // bandit travel.  In particular, do not request uniform distribution.
            // TaleWorlds' implementation samples 0..(max-min) in that mode, which
            // violates its advertised non-zero minimum-radius contract.
            CampaignVec2[] centers =
            {
                centerSettlement.GatePosition,
                centerSettlement.Position
            };
            float radius = Math.Max(8f,
                Campaign.Current.EstimatedAverageBanditPartySpeed * 12f);

            for (int i = 0; i < centers.Length; i++)
            {
                CampaignVec2 center = centers[i];
                CampaignVec2 candidate;
                try
                {
                    candidate = Helpers.NavigationHelper.FindPointAroundPosition(
                        center, MobileParty.NavigationType.Default, radius, 0f,
                        true, false);
                }
                catch
                {
                    continue;
                }

                if (IsValidNativeSpawn(candidate))
                    return candidate;

                // FindPointAroundPosition deliberately returns its center when
                // its bounded random search cannot improve it.  A valid gate is
                // still Bannerlord's canonical party creation position.
                if (IsValidNativeSpawn(center))
                    return center;
            }

            throw new InvalidOperationException(
                "Neither the native settlement gate nor its bounded native " +
                "bandit-spawn neighborhood is navigable near " +
                centerSettlement.StringId + ".");
        }

        private static Settlement FindContainingSettlement(CampaignVec2 position)
        {
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement != null && IsInsideSettlementFootprint(position, settlement))
                    return settlement;
            }
            return null;
        }

        private static bool IsValidNativeSpawn(CampaignVec2 position)
        {
            if (Single.IsNaN(position.X) || Single.IsNaN(position.Y) ||
                Single.IsInfinity(position.X) || Single.IsInfinity(position.Y))
                return false;
            return Helpers.NavigationHelper.IsPositionValidForNavigationType(
                position, MobileParty.NavigationType.Default);
        }

        private static bool IsInsideSettlementFootprint(CampaignVec2 position, Settlement settlement)
        {
            float radius = GetClearance(settlement);
            float radiusSquared = radius * radius;
            return position.DistanceSquared(settlement.Position) < radiusSquared ||
                   position.DistanceSquared(settlement.GatePosition) < radiusSquared;
        }

        private static float GetClearance(Settlement settlement)
        {
            if (settlement.IsTown || settlement.IsCastle)
                return TownClearance;
            if (settlement.IsVillage)
                return VillageClearance;
            return OtherClearance;
        }
    }
}
