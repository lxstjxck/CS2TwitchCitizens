using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using CS2TwitchCitizens.Commands;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2TwitchCitizens.Mod
{
    /// <summary>Validates new Citizen assignments using this game's ECS components.</summary>
    internal sealed class CitizenEligibilityService
    {
        private readonly EntityManager _manager;
        private readonly EntityQuery _citizens;
        private readonly Func<Entity, bool> _isClaimed;

        public CitizenEligibilityService(EntityManager manager, EntityQuery citizens, Func<Entity, bool> isClaimed)
        {
            _manager = manager;
            _citizens = citizens;
            _isClaimed = isClaimed;
        }

        public Entity FindAvailable(out string diagnostic)
        {
            var counts = new Dictionary<CitizenRejection, int>();
            var viewed = 0;
            var chosen = Entity.Null;
            using (var entities = _citizens.ToEntityArray(Allocator.Temp))
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    viewed++;
                    var reason = Check(entity, true, out var adult);
                    if (reason != CitizenRejection.None)
                    {
                        counts.TryGetValue(reason, out var count);
                        counts[reason] = count + 1;
                        continue;
                    }
                    if (chosen == Entity.Null) chosen = entity;
                    if (adult) { chosen = entity; break; }
                }
            }
            diagnostic = $"viewed={viewed} rejected={FormatCounts(counts)} selected={chosen}";
            return chosen;
        }

        public CitizenRejection Check(Entity entity, bool checkClaim, out bool adult)
        {
            adult = false;
            if (!Exists<Citizen>(entity)) return CitizenRejection.InvalidCitizen;
            if (checkClaim && _isClaimed(entity)) return CitizenRejection.Claimed;
            if (CitizenUtils.IsDead(_manager, entity)) return CitizenRejection.Dead;
            var citizen = _manager.GetComponentData<Citizen>(entity);
            adult = citizen.GetAge() == CitizenAge.Adult;
            var visitor = (citizen.m_State & (CitizenFlags.Tourist | CitizenFlags.Commuter)) != 0;
            var movingAway = (citizen.m_State & CitizenFlags.MovingAwayReachOC) != 0 ||
                _manager.HasComponent<MovingAway>(entity) ||
                (_manager.HasComponent<TravelPurpose>(entity) &&
                 _manager.GetComponentData<TravelPurpose>(entity).m_Purpose == Purpose.MovingAway);

            var household = Entity.Null;
            var validHousehold = false;
            var validHome = false;
            if (_manager.HasComponent<HouseholdMember>(entity))
            {
                household = _manager.GetComponentData<HouseholdMember>(entity).m_Household;
                validHousehold = Exists<Household>(household);
                if (validHousehold)
                {
                    var householdData = _manager.GetComponentData<Household>(household);
                    visitor |= (householdData.m_Flags & (HouseholdFlags.Tourist | HouseholdFlags.Commuter)) != 0 ||
                        _manager.HasComponent<TouristHousehold>(household) ||
                        _manager.HasComponent<CommuterHousehold>(household);
                    movingAway |= _manager.HasComponent<MovingAway>(household);
                    if (!_manager.HasComponent<HomelessHousehold>(household) &&
                        _manager.HasComponent<PropertyRenter>(household))
                    {
                        var property = _manager.GetComponentData<PropertyRenter>(household).m_Property;
                        validHome = Exists<ResidentialProperty>(property) &&
                            _manager.HasComponent<Building>(property) &&
                            !IsOutsideConnection(property);
                    }
                }
            }

            var validOptionalReferences = true;
            var outside = IsOutsideConnection(entity);
            if (_manager.HasComponent<CurrentBuilding>(entity))
            {
                var building = _manager.GetComponentData<CurrentBuilding>(entity).m_CurrentBuilding;
                if (building != Entity.Null)
                {
                    validOptionalReferences &= Exists(building);
                    outside |= IsOutsideConnection(building);
                }
            }
            if (_manager.HasComponent<CurrentTransport>(entity))
            {
                var transport = _manager.GetComponentData<CurrentTransport>(entity).m_CurrentTransport;
                if (transport != Entity.Null)
                {
                    validOptionalReferences &= Exists(transport);
                    outside |= IsOutsideConnection(transport);
                }
            }
            if (_manager.HasComponent<Worker>(entity))
            {
                var workplace = _manager.GetComponentData<Worker>(entity).m_Workplace;
                if (workplace != Entity.Null) validOptionalReferences &= Exists(workplace);
            }

            var preliminary = CitizenEligibilityPolicy.Check(new CitizenEligibilityFacts(
                true, false, false, visitor, movingAway, validHousehold, validHome,
                validOptionalReferences, outside, true, adult));
            if (preliminary != CitizenRejection.None) return preliminary;

            var index = -1;
            var positionAvailable = SelectedInfoUISystem.TryGetPosition(
                entity, _manager, ref index, out Entity target, out float3 position,
                out Bounds3 _, out quaternion _, true) && math.all(math.isfinite(position));
            outside |= IsOutsideConnection(target);
            return CitizenEligibilityPolicy.Check(new CitizenEligibilityFacts(
                true, false, false, visitor, movingAway, validHousehold, validHome, validOptionalReferences,
                outside, positionAvailable, adult));
        }

        private bool Exists(Entity entity) => entity != Entity.Null && _manager.Exists(entity) &&
            !_manager.HasComponent<Deleted>(entity);

        private bool Exists<T>(Entity entity) where T : unmanaged, IComponentData =>
            Exists(entity) && _manager.HasComponent<T>(entity);

        private bool IsOutsideConnection(Entity entity) => Exists(entity) &&
            (_manager.HasComponent<Game.Objects.OutsideConnection>(entity) ||
             _manager.HasComponent<Game.Net.OutsideConnection>(entity));

        private static string FormatCounts(Dictionary<CitizenRejection, int> counts)
        {
            if (counts.Count == 0) return "none";
            var parts = new List<string>(counts.Count);
            foreach (var pair in counts) parts.Add(pair.Key + ":" + pair.Value);
            return string.Join(",", parts);
        }
    }
}
