using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using CS2TwitchCitizens.Commands;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Serialization;
using Game.Simulation;
using Game.UI;
using Game.UI.InGame;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2TwitchCitizens.Mod
{
    /// <summary>Consumes queued commands and stores city-specific viewer lives in the game save.</summary>
    public sealed partial class CitizenBindingSystem : GameSystemBase, IDefaultSerializable
    {
        // A !join may inspect the city's Citizen query; cap that work to one command per update.
        private const int MaxCommandsPerUpdate = 1;
        private EntityQuery _citizens;
        private ViewerBindingRegistry<Entity> _bindings = new ViewerBindingRegistry<Entity>();
        private ViewerLifeJournal<Entity> _lives = new ViewerLifeJournal<Entity>();
        private List<ViewerLifeAccount<Entity>>? _pendingRestore;
        private bool _loadReady;
        private bool _loadFailed;
        private DateTime _nextLifeCheck;
        private readonly Dictionary<string, CitizenDisplayIdentity> _identities =
            new Dictionary<string, CitizenDisplayIdentity>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _missingObservations =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private CitizenCameraService? _camera;
        private CitizenEligibilityService? _eligibility;
        private bool _diagnoseRestoredBindings;
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
            _camera = new CitizenCameraService(World);
            _eligibility = new CitizenEligibilityService(EntityManager, _citizens, entity => _lives.IsClaimed(entity));
            World.GetExistingSystemManaged<SerializerSystem>()?.SetDirty();
            Mod.Log.Info("[CS2TwitchCitizens] CitizenBindingSystem.OnCreate");
        }

        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            _loadReady = false;
            _loadFailed = false;
            _pendingRestore = null;
            _bindings = new ViewerBindingRegistry<Entity>();
            _lives = new ViewerLifeJournal<Entity>();
            _identities.Clear();
            _missingObservations.Clear();
            _diagnoseRestoredBindings = false;
            Mod.CommandQueue?.Clear();
            Mod.Log.Info($"[CS2TwitchCitizens] LIFE preload purpose={purpose} mode={mode}; city state cleared");
        }

        protected override void OnGameLoaded(Context context)
        {
            base.OnGameLoaded(context);
            CompleteRestore();
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game)
            {
                _loadReady = false;
                _pendingRestore = null;
                _bindings = new ViewerBindingRegistry<Entity>();
                _lives = new ViewerLifeJournal<Entity>();
                _identities.Clear();
                _missingObservations.Clear();
                _diagnoseRestoredBindings = false;
                Mod.CommandQueue?.Clear();
                return;
            }
            if (!_loadReady && !_loadFailed)
                CompleteRestore();
        }

        private void CompleteRestore()
        {
            if (_loadFailed) return;
            try
            {
                _lives.Restore(_pendingRestore ?? new List<ViewerLifeAccount<Entity>>(), IsValidCitizen);
                foreach (var account in _lives.Accounts)
                {
                    var life = account.Current;
                    if (life?.Status != ViewerLifeStatus.Active) continue;
                    if (_bindings.Join(account.TwitchUserId, life.CitizenKey, out _) != JoinResult.Joined)
                        throw new InvalidDataException("Duplicate Citizen in restored bindings.");
                    _identities[account.TwitchUserId] = new CitizenDisplayIdentity(
                        account.Login, account.DisplayName, account.Login) {
                        OriginalNameDescriptor = life.OriginalCitizenName
                    };
                }
                _pendingRestore = null;
                _diagnoseRestoredBindings = _bindings.GetAllBindings().Count > 0;
                // Commands received while the city was loading have no reliable city context.
                Mod.CommandQueue?.Clear();
                _loadReady = true;
                _nextLifeCheck = DateTime.UtcNow.AddSeconds(10);
                Mod.Log.Info($"[CS2TwitchCitizens] LIFE restore complete viewers={_lives.Accounts.Count}");
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                _loadReady = false;
                _bindings = new ViewerBindingRegistry<Entity>();
                Mod.Log.Info($"[CS2TwitchCitizens] LIFE restore failed; commands and save blocked: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public void SetDefaults(Context context)
        {
            _pendingRestore = new List<ViewerLifeAccount<Entity>>();
            Mod.Log.Info("[CS2TwitchCitizens] LIFE save data absent; starting empty city history");
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            if (!_loadReady || _loadFailed)
                throw new InvalidOperationException("Viewer lives have not been restored; save blocked.");
            writer.Write(ViewerLifeSaveFormat.Version);
            writer.Write(_lives.Accounts.Count);
            foreach (var account in _lives.Accounts)
            {
                writer.Write(account.TwitchUserId);
                writer.Write(account.Login);
                writer.Write(account.DisplayName);
                writer.Write(account.Lives.Count);
                foreach (var life in account.Lives)
                {
                    writer.Write(life.LifeId);
                    writer.Write(life.Status == ViewerLifeStatus.Active ? life.CitizenKey : Entity.Null);
                    writer.Write(life.OriginalCitizenName);
                    writer.Write(life.TwitchDisplayName);
                    writer.Write(life.StartGameDate);
                    writer.Write(life.EndGameDate);
                    writer.Write((int)life.Status);
                    writer.Write(life.LastKnownAge);
                    writer.Write(life.LastKnownWorkplace);
                    writer.Write(life.LastKnownHome);
                    writer.Write(life.CauseOfDeath);
                }
            }
            Mod.Log.Info($"[CS2TwitchCitizens] LIFE serialized format={ViewerLifeSaveFormat.Version} viewers={_lives.Accounts.Count}");
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            try
            {
                var version = 0;
                reader.Read(out version);
                ViewerLifeSaveFormat.RequireSupported(version);
                var count = 0;
                reader.Read(out count);
                if (count < 0 || count > 100000)
                    throw new InvalidDataException("Invalid viewer count in save.");
                var accounts = new List<ViewerLifeAccount<Entity>>(count);
                var totalLives = 0;
                for (var i = 0; i < count; i++)
                {
                    var account = new ViewerLifeAccount<Entity> {
                        TwitchUserId = ReadString(ref reader),
                        Login = ReadString(ref reader),
                        DisplayName = ReadString(ref reader)
                    };
                    var lifeCount = 0;
                    reader.Read(out lifeCount);
                    if (lifeCount < 1 || lifeCount > 100000 || (totalLives += lifeCount) > 1000000)
                        throw new InvalidDataException("Invalid life count in save.");
                    for (var j = 0; j < lifeCount; j++)
                    {
                        var life = new ViewerLife<Entity> { LifeId = ReadString(ref reader) };
                        var citizen = Entity.Null;
                        reader.Read(out citizen);
                        life.CitizenKey = citizen;
                        life.OriginalCitizenName = ReadString(ref reader);
                        life.TwitchDisplayName = ReadString(ref reader);
                        life.StartGameDate = ReadString(ref reader);
                        life.EndGameDate = ReadString(ref reader);
                        var status = 0;
                        reader.Read(out status);
                        life.Status = (ViewerLifeStatus)status;
                        life.LastKnownAge = ReadString(ref reader);
                        life.LastKnownWorkplace = ReadString(ref reader);
                        life.LastKnownHome = ReadString(ref reader);
                        life.CauseOfDeath = ReadString(ref reader);
                        account.Lives.Add(life);
                    }
                    accounts.Add(account);
                }
                _pendingRestore = accounts;
                Mod.Log.Info($"[CS2TwitchCitizens] LIFE deserialized format={version} viewers={count} lives={totalLives}; awaiting game load");
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                _loadReady = false;
                _pendingRestore = null;
                Mod.Log.Info($"[CS2TwitchCitizens] LIFE deserialize failed; save blocked: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private static string ReadString<TReader>(ref TReader reader) where TReader : IReader
        {
            string value = string.Empty;
            reader.Read(out value);
            return value ?? string.Empty;
        }

        protected override void OnUpdate()
        {
            if (!_loadReady || _loadFailed) return;
            if (_diagnoseRestoredBindings)
            {
                _diagnoseRestoredBindings = false;
                foreach (var binding in _bindings.GetAllBindings())
                {
                    var reason = _eligibility!.Check(binding.CitizenKey, false, out _);
                    Mod.Log.Info($"[CS2TwitchCitizens] JOIN eligibility restored viewer={binding.TwitchUserId} citizen={binding.CitizenKey} result={reason}; binding retained");
                }
            }
            if (DateTime.UtcNow >= _nextLifeCheck)
            {
                _nextLifeCheck = DateTime.UtcNow.AddSeconds(10);
                CheckBoundLives();
            }
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
            _lives.UpdateIdentity(command.TwitchUserId, command.Login, command.DisplayName);
            if (_identities.TryGetValue(command.TwitchUserId, out var identity))
            {
                identity.Login = command.Login;
                identity.DisplayName = command.DisplayName;
            }
            switch (command.Command)
            {
                case "!join":
                    Join(command);
                    break;
                case "!me":
                    Me(command);
                    break;
                case "!find":
                    Find(command);
                    break;
                case "!history":
                    History(command);
                    break;
            }
        }

        private void Join(TwitchCommand command)
        {
            if (_lives.TryGet(command.TwitchUserId, out var prior) &&
                prior!.Current?.Status == ViewerLifeStatus.Missing)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} blocked: previous life is Missing");
                return;
            }
            if (_bindings.TryGet(command.TwitchUserId, out var existing))
            {
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} already joined citizen={existing!.CitizenKey}");
                return;
            }

            var citizen = FindAvailableCitizen(command.TwitchUserId);
            if (citizen == Entity.Null)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} no eligible citizen");
                return;
            }

            var gameDate = GetGameDate();
            if (string.IsNullOrEmpty(gameDate))
            {
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} game date unavailable");
                return;
            }
            var result = _bindings.Join(command.TwitchUserId, citizen, out var binding);
            if (result == JoinResult.Joined)
            {
                var before = BuildViewerInfo(command.TwitchUserId, citizen);
                if (!_lives.TryStart(command.TwitchUserId, command.Login, command.DisplayName,
                    citizen, gameDate, string.Empty, before.Age,
                    string.IsNullOrEmpty(before.Home) ? string.Empty : "home available",
                    string.IsNullOrEmpty(before.Workplace) ? string.Empty : "workplace available", out var life))
                {
                    _bindings.Remove(command.TwitchUserId);
                    Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} life claim rejected");
                    return;
                }
                AssignDisplayIdentity(command, citizen);
                life!.OriginalCitizenName = _identities[command.TwitchUserId].OriginalNameDescriptor;
                CaptureLifeSnapshot(command.TwitchUserId, citizen);
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} login={command.Login} -> citizen={binding.CitizenKey} life={life.LifeId}");
            }
            else if (result == JoinResult.AlreadyJoined)
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} already joined citizen={binding.CitizenKey}");
            else
                Mod.Log.Info($"[CS2TwitchCitizens] JOIN viewer={command.TwitchUserId} candidate already bound citizen={citizen}");
        }

        private Entity FindAvailableCitizen(string viewerId)
        {
            var chosen = _eligibility!.FindAvailable(out var diagnostic);
            Mod.Log.Info($"[CS2TwitchCitizens] JOIN eligibility viewer={viewerId} {diagnostic}");
            return chosen;
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
            entity != Entity.Null && EntityManager.Exists(entity) &&
            EntityManager.HasComponent<Citizen>(entity) && !EntityManager.HasComponent<Deleted>(entity);

        private string GetGameDate()
        {
            var time = World.GetExistingSystemManaged<TimeSystem>();
            return time == null ? string.Empty :
                time.GetCurrentDateTime().ToString("O", CultureInfo.InvariantCulture);
        }

        private void CheckBoundLives()
        {
            foreach (var binding in _bindings.GetAllBindings())
            {
                var entity = binding.CitizenKey;
                var viewerId = binding.TwitchUserId;
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<Citizen>(entity) &&
                    CitizenUtils.IsDead(EntityManager, entity))
                {
                    _missingObservations.Remove(viewerId);
                    CaptureLifeSnapshot(viewerId, entity);
                    if (_lives.MarkDeceased(viewerId, GetGameDate(), string.Empty))
                    {
                        _bindings.Remove(viewerId);
                        Mod.Log.Info($"[CS2TwitchCitizens] LIFE deceased viewer={viewerId} citizen={entity}");
                    }
                }
                else if (!IsValidCitizen(entity))
                {
                    _missingObservations.TryGetValue(viewerId, out var misses);
                    _missingObservations[viewerId] = ++misses;
                    if (misses >= 2 && _lives.MarkMissing(viewerId))
                    {
                        _missingObservations.Remove(viewerId);
                        _bindings.Remove(viewerId);
                        Mod.Log.Info($"[CS2TwitchCitizens] LIFE missing viewer={viewerId} citizen={entity}; no replacement assigned");
                    }
                }
                else
                {
                    _missingObservations.Remove(viewerId);
                    CaptureLifeSnapshot(viewerId, entity);
                }
            }
        }

        private void CaptureLifeSnapshot(string viewerId, Entity entity)
        {
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<Citizen>(entity)) return;
            var age = EntityManager.GetComponentData<Citizen>(entity).GetAge().ToString();
            var home = string.Empty;
            var workplace = string.Empty;
            if (EntityManager.HasComponent<HouseholdMember>(entity))
            {
                var household = EntityManager.GetComponentData<HouseholdMember>(entity).m_Household;
                if (EntityManager.Exists(household) && EntityManager.HasComponent<PropertyRenter>(household))
                {
                    var property = EntityManager.GetComponentData<PropertyRenter>(household).m_Property;
                    if (EntityManager.Exists(property)) home = GetPlaceLabel(property, "home available");
                }
            }
            if (EntityManager.HasComponent<Worker>(entity))
            {
                var work = EntityManager.GetComponentData<Worker>(entity).m_Workplace;
                if (EntityManager.Exists(work)) workplace = GetPlaceLabel(work, "workplace available");
            }
            _lives.UpdateSnapshot(viewerId, age, home, workplace);
        }

        private string GetPlaceLabel(Entity place, string fallback)
        {
            try
            {
                var label = World.GetExistingSystemManaged<NameSystem>()?.GetRenderedLabelName(place);
                return string.IsNullOrWhiteSpace(label) ? fallback : label!;
            }
            catch { return fallback; }
        }

        private void History(TwitchCommand command)
        {
            if (!_lives.TryGet(command.TwitchUserId, out var account))
            {
                Mod.Log.Info($"[CS2TwitchCitizens] HISTORY viewer={command.TwitchUserId} lives=0");
                return;
            }
            var completed = 0;
            foreach (var life in account!.Lives)
                if (life.Status == ViewerLifeStatus.Deceased) completed++;
            Mod.Log.Info($"[CS2TwitchCitizens] HISTORY viewer={command.TwitchUserId} lives={account.Lives.Count} current={account.Lives.Count} completed={completed} status={account.Current?.Status}");
        }

        private void AssignDisplayIdentity(TwitchCommand command, Entity citizen)
        {
            var requestedName = !string.IsNullOrWhiteSpace(command.Login) ? command.Login : command.DisplayName;
            var identity = new CitizenDisplayIdentity(command.Login, command.DisplayName, requestedName);
            _identities[command.TwitchUserId] = identity;

            if (!IsValidCitizen(citizen) || string.IsNullOrWhiteSpace(requestedName))
                return;
            var nameSystem = World.GetExistingSystemManaged<NameSystem>();
            if (nameSystem == null)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] NAME viewer={command.TwitchUserId} game NameSystem unavailable");
                return;
            }

            if (nameSystem.TryGetCustomName(citizen, out var customName))
                identity.OriginalNameDescriptor = "custom:" + customName;
            else
                identity.OriginalNameDescriptor = "label:" + nameSystem.GetRenderedLabelName(citizen);

            nameSystem.SetCustomName(citizen, requestedName);
            Mod.Log.Info($"[CS2TwitchCitizens] NAME viewer={command.TwitchUserId} citizen={citizen} name={requestedName} original={identity.OriginalNameDescriptor}");
        }

        /// <summary>Game-thread snapshot for a future UI; TwitchUserId is the safe action key.</summary>
        public IReadOnlyList<ViewerCitizenInfo> GetAllBindings()
        {
            var result = new List<ViewerCitizenInfo>(_lives.Accounts.Count);
            foreach (var account in _lives.Accounts)
                result.Add(BuildViewerInfo(account.TwitchUserId,
                    account.Current?.Status == ViewerLifeStatus.Active ? account.Current.CitizenKey : Entity.Null));
            return result;
        }

        public ViewerCitizenInfo? GetViewerInfo(string viewerId)
        {
            return _lives.TryGet(viewerId, out var account)
                ? BuildViewerInfo(viewerId,
                    account!.Current?.Status == ViewerLifeStatus.Active ? account.Current.CitizenKey : Entity.Null)
                : null;
        }

        public CitizenFindResult<Entity> FindCitizen(string viewerId) =>
            CitizenLookup.Find(_bindings, viewerId, IsValidCitizen, LocateCitizen);

        public CitizenFocusStatus FocusCitizen(string viewerId)
        {
            var found = FindCitizen(viewerId);
            switch (found.Status)
            {
                case CitizenFindStatus.NotJoined: return CitizenFocusStatus.CitizenNotFound;
                case CitizenFindStatus.Stale: return CitizenFocusStatus.CitizenNotFound;
                case CitizenFindStatus.PositionUnavailable: return CitizenFocusStatus.PositionUnavailable;
                default: return _camera?.Focus(found.CitizenKey, found.Position!.Value) ?? CitizenFocusStatus.CameraUnavailable;
            }
        }

        public CitizenFocusStatus FollowCitizen(string viewerId)
        {
            var found = FindCitizen(viewerId);
            switch (found.Status)
            {
                case CitizenFindStatus.NotJoined: return CitizenFocusStatus.CitizenNotFound;
                case CitizenFindStatus.Stale: return CitizenFocusStatus.CitizenNotFound;
                case CitizenFindStatus.PositionUnavailable: return CitizenFocusStatus.PositionUnavailable;
                default: return _camera?.Follow(found.CitizenKey, found.Position!.Value) ?? CitizenFocusStatus.CameraUnavailable;
            }
        }

        public CitizenFocusStatus StopFollowing() =>
            _camera?.StopFollowing() ?? CitizenFocusStatus.CameraUnavailable;

        public bool TryPollFocus(out CitizenFocusStatus status)
        {
            status = CitizenFocusStatus.CameraUnavailable;
            return _camera != null && _camera.TryPollFocus(out status);
        }

        private void Find(TwitchCommand command)
        {
            var found = FindCitizen(command.TwitchUserId);
            switch (found.Status)
            {
                case CitizenFindStatus.NotJoined:
                    Mod.Log.Info($"[CS2TwitchCitizens] FIND viewer={command.TwitchUserId} not joined");
                    break;
                case CitizenFindStatus.Stale:
                    Mod.Log.Info($"[CS2TwitchCitizens] FIND viewer={command.TwitchUserId} stale binding");
                    break;
                case CitizenFindStatus.PositionUnavailable:
                    Mod.Log.Info($"[CS2TwitchCitizens] FIND viewer={command.TwitchUserId} position unavailable");
                    break;
                default:
                    Mod.Log.Info($"[CS2TwitchCitizens] FIND viewer={command.TwitchUserId} citizen={found.CitizenKey} position={found.Position}");
                    break;
            }
        }

        private CitizenPosition? LocateCitizen(Entity entity)
        {
            var index = -1;
            if (!SelectedInfoUISystem.TryGetPosition(
                    entity, EntityManager, ref index,
                    out _, out float3 position, out Bounds3 _, out quaternion _, true))
                return null;
            return new CitizenPosition(position.x, position.y, position.z);
        }

        private ViewerCitizenInfo BuildViewerInfo(string viewerId, Entity entity)
        {
            _identities.TryGetValue(viewerId, out var identity);
            _lives.TryGet(viewerId, out var account);
            var current = account?.Current;
            var previous = new List<ViewerLifeInfo>();
            if (account != null)
            {
                for (var i = 0; i < account.Lives.Count - 1; i++)
                {
                    var life = account.Lives[i];
                    previous.Add(new ViewerLifeInfo {
                        LifeId = life.LifeId, OriginalCitizenName = life.OriginalCitizenName,
                        TwitchDisplayName = life.TwitchDisplayName,
                        StartGameDate = life.StartGameDate, EndGameDate = life.EndGameDate,
                        Status = life.Status.ToString(), LastKnownAge = life.LastKnownAge,
                        LastKnownHome = life.LastKnownHome,
                        LastKnownWorkplace = life.LastKnownWorkplace,
                        CauseOfDeath = life.CauseOfDeath
                    });
                }
            }
            var info = new ViewerCitizenInfo
            {
                TwitchUserId = viewerId,
                Login = account?.Login ?? identity?.Login ?? string.Empty,
                DisplayName = account?.DisplayName ?? identity?.DisplayName ?? string.Empty,
                OriginalNameDescriptor = current?.OriginalCitizenName ?? identity?.OriginalNameDescriptor ?? string.Empty,
                CurrentLifeId = current?.LifeId ?? string.Empty,
                TotalLives = account?.Lives.Count ?? 0,
                CurrentLifeStatus = current?.Status.ToString() ?? string.Empty,
                CurrentLife = current == null ? null : ToLifeInfo(current),
                PreviousLives = previous.ToArray(),
                IsValid = (current == null || current.Status == ViewerLifeStatus.Active) && IsValidCitizen(entity)
            };
            if (!info.IsValid)
            {
                info.Age = current?.LastKnownAge ?? string.Empty;
                info.Home = current?.LastKnownHome ?? string.Empty;
                info.Workplace = current?.LastKnownWorkplace ?? string.Empty;
                return info;
            }

            var nameSystem = World.GetExistingSystemManaged<NameSystem>();
            if (nameSystem != null && nameSystem.TryGetCustomName(entity, out var customName))
                info.CitizenName = customName;
            else
                info.CitizenName = identity?.AssignedName ?? string.Empty;

            info.Age = EntityManager.GetComponentData<Citizen>(entity).GetAge().ToString();
            if (info.CurrentLife != null)
                info.CurrentLife.LastKnownAge = info.Age;
            if (EntityManager.HasComponent<HouseholdMember>(entity))
            {
                var household = EntityManager.GetComponentData<HouseholdMember>(entity).m_Household;
                if (EntityManager.Exists(household) && EntityManager.HasComponent<PropertyRenter>(household))
                    info.Home = EntityManager.GetComponentData<PropertyRenter>(household).m_Property.ToString();
            }
            if (EntityManager.HasComponent<Worker>(entity))
                info.Workplace = EntityManager.GetComponentData<Worker>(entity).m_Workplace.ToString();
            if (EntityManager.HasComponent<CurrentBuilding>(entity))
                info.CurrentBuilding = EntityManager.GetComponentData<CurrentBuilding>(entity).m_CurrentBuilding.ToString();

            info.Position = LocateCitizen(entity);
            info.PositionAvailable = info.Position.HasValue;
            return info;
        }

        private static ViewerLifeInfo ToLifeInfo(ViewerLife<Entity> life) => new ViewerLifeInfo {
            LifeId = life.LifeId, OriginalCitizenName = life.OriginalCitizenName,
            TwitchDisplayName = life.TwitchDisplayName, StartGameDate = life.StartGameDate,
            EndGameDate = life.EndGameDate, Status = life.Status.ToString(),
            LastKnownAge = life.LastKnownAge, LastKnownHome = life.LastKnownHome,
            LastKnownWorkplace = life.LastKnownWorkplace, CauseOfDeath = life.CauseOfDeath
        };

        private sealed class CitizenDisplayIdentity
        {
            public CitizenDisplayIdentity(string login, string displayName, string assignedName)
            {
                Login = login;
                DisplayName = displayName;
                AssignedName = assignedName;
            }
            public string Login { get; set; }
            public string DisplayName { get; set; }
            public string AssignedName { get; }
            public string OriginalNameDescriptor { get; set; } = string.Empty;
        }
    }
}
