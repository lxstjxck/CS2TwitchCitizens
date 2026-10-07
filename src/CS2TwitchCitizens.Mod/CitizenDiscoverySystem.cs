using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Unity.Collections;
using Unity.Entities;

namespace CS2TwitchCitizens.Mod
{
    /// <summary>Read-only snapshot of at most five citizens on first update and every 1,024 updates thereafter.</summary>
    public sealed partial class CitizenDiscoverySystem : GameSystemBase
    {
        private const int ScanInterval = 1024;
        private const int SampleLimit = 5;
        private EntityQuery _citizens;
        private int _updates;
        private bool _firstUpdateLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            _citizens = GetEntityQuery(
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.Exclude<Deleted>());
            RequireForUpdate(_citizens);
            Mod.Log.Info("[CS2TwitchCitizens] CitizenDiscoverySystem.OnCreate");
        }

        protected override void OnUpdate()
        {
            if (!_firstUpdateLogged)
            {
                _firstUpdateLogged = true;
                Mod.Log.Info("[CS2TwitchCitizens] CitizenDiscoverySystem first OnUpdate");
            }
            else
            {
                if (++_updates < ScanInterval)
                    return;
                _updates = 0;
            }

            using (var entities = _citizens.ToEntityArray(Allocator.Temp))
            {
                Mod.Log.Info($"[CS2TwitchCitizens] Citizen query count={entities.Length}");
                var reported = 0;
                for (var i = 0; i < entities.Length && reported < SampleLimit; i++)
                {
                    var entity = entities[i];
                    if (CitizenUtils.IsDead(EntityManager, entity))
                        continue;

                    var citizen = EntityManager.GetComponentData<Citizen>(entity);
                    var household = Entity.Null;
                    var home = Entity.Null;
                    var workplace = Entity.Null;
                    var currentBuilding = Entity.Null;

                    if (EntityManager.HasComponent<HouseholdMember>(entity))
                    {
                        household = EntityManager.GetComponentData<HouseholdMember>(entity).m_Household;
                        if (EntityManager.Exists(household) && EntityManager.HasComponent<PropertyRenter>(household))
                            home = EntityManager.GetComponentData<PropertyRenter>(household).m_Property;
                    }

                    if (EntityManager.HasComponent<Worker>(entity))
                        workplace = EntityManager.GetComponentData<Worker>(entity).m_Workplace;
                    if (EntityManager.HasComponent<CurrentBuilding>(entity))
                        currentBuilding = EntityManager.GetComponentData<CurrentBuilding>(entity).m_CurrentBuilding;

                    Mod.Log.Info($"[CS2TwitchCitizens] Citizen entity={entity} age={citizen.GetAge()} household={household} home={home} workplace={workplace} currentBuilding={currentBuilding}");
                    reported++;
                }
            }
        }
    }
}
