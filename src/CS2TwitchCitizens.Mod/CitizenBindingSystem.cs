using System;
using Colossal.Logging;
using CS2TwitchCitizens.Commands;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Unity.Collections;
using Unity.Entities;

namespace CS2TwitchCitizens.Mod
{
    /// <summary>Consumes queued commands on the simulation thread; bindings last only for this system's lifetime.</summary>
    public sealed partial class CitizenBindingSystem : GameSystemBase
    {
        private const int MaxCommandsPerUpdate = 64;
        private EntityQuery _citizens;
        private ViewerBindingRegistry<Entity> _bindings = new ViewerBindingRegistry<Entity>();
#if DEBUG
        private bool _devSequenceEnqueued;
#endif

        protected override void OnCreate()
        {
            base.OnCreate();
            _citizens = GetEntityQuery(
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.Exclude<Deleted>());
            RequireForUpdate(_citizens);
            Mod.Log.Info("[CS2TwitchCitizens] CitizenBindingSystem.OnCreate");
        }

        protected override void OnUpdate()
        {
            var queue = Mod.CommandQueue;
            if (queue == null)
                return;

#if DEBUG
            if (!Mod.TwitchEnabled && !_devSequenceEnqueued)
            {
                _devSequenceEnqueued = true;
                DevCommandInjector.EnqueueSampleSequence(queue, DateTimeOffset.UtcNow);
                Mod.Log.Info("[CS2TwitchCitizens] DEV sequence queued: !join, !me, !join viewer=dev-user-1");
            }
#endif

            if (queue.Count > 0)
                TwitchCommandReceiver.Drain(queue, MaxCommandsPerUpdate, HandleCommand);
        }

        private void HandleCommand(TwitchCommand command)
        {
            switch (command.Command)
            {
                case "!join":
                    Join(command);
                    break;
                case "!me":
                    Me(command);
                    break;
            }
        }

        private void Join(TwitchCommand command)
        {
            if (_bindings.TryGet(command.TwitchUserId, out var existing))
            {
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} already joined citizen={existing!.CitizenKey}");
                return;
            }

            var citizen = FindAvailableCitizen();
            if (citizen == Entity.Null)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} no eligible citizen");
                return;
            }

            var result = _bindings.Join(command.TwitchUserId, citizen, out var binding);
            if (result == JoinResult.Joined)
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} login={command.Login} -> citizen={binding.CitizenKey}");
            else if (result == JoinResult.AlreadyJoined)
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} already joined citizen={binding.CitizenKey}");
            else
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} candidate already bound citizen={citizen}");
        }

        private Entity FindAvailableCitizen()
        {
            var fallback = Entity.Null;
            using (var entities = _citizens.ToEntityArray(Allocator.Temp))
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    if (!IsValidCitizen(entity) || _bindings.IsCitizenBound(entity) || CitizenUtils.IsDead(EntityManager, entity))
                        continue;

                    if (EntityManager.GetComponentData<Citizen>(entity).GetAge() == CitizenAge.Adult)
                        return entity;
                    if (fallback == Entity.Null)
                        fallback = entity;
                }
            }

            return fallback;
        }

        private void Me(TwitchCommand command)
        {
            var status = _bindings.GetStatus(command.TwitchUserId, IsValidCitizen, out var binding);
            if (status == BindingStatus.NotJoined)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] !me viewer={command.TwitchUserId} not joined");
                return;
            }

            var entity = binding!.CitizenKey;
            if (status == BindingStatus.Stale)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] !me viewer={command.TwitchUserId} stale binding citizen={entity}");
                return;
            }

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

            Mod.Log.Info($"[CS2TwitchCitizens] !me viewer={command.TwitchUserId} citizen={entity} age={citizen.GetAge()} household={household} home={home} workplace={workplace} currentBuilding={currentBuilding}");
        }

        private bool IsValidCitizen(Entity entity) =>
            EntityManager.Exists(entity) && EntityManager.HasComponent<Citizen>(entity);
    }
}
