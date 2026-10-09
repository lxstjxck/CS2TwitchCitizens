using System;
using CS2TwitchCitizens.Commands;
using Game;
using Game.Rendering;
using Game.Tools;
using Unity.Entities;
using UnityEngine;

namespace CS2TwitchCitizens.Mod
{
    /// <summary>Game-thread camera actions through the game's controllers.</summary>
    internal sealed class CitizenCameraService
    {
        private const float RequestedZoom = 35f;
        private readonly World _world;
        private bool _pending;
        private Vector3 _target;
        private Vector3 _before;
        private DateTime _deadline;

        public CitizenCameraService(World world) { _world = world; }

        public CitizenFocusStatus Focus(Entity citizen, CitizenPosition position)
        {
            _pending = false;
            var camera = _world.GetExistingSystemManaged<CameraUpdateSystem>();
            var gameplay = camera?.gamePlayController;
            var active = camera?.activeCameraController;
            if (camera == null || gameplay == null || active == null)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] CAMERA Find citizen={citizen} result=CameraUnavailable controller={active?.GetType().FullName ?? "null"}");
                return CitizenFocusStatus.CameraUnavailable;
            }

            _target = new Vector3(position.X, position.Y, position.Z);
            _before = active.position;
            var range = gameplay.zoomRange;
            var zoom = Mathf.Clamp(RequestedZoom, range.min, range.max);
            Mod.Log.Info($"[CS2TwitchCitizens] CAMERA Find citizen={citizen} citizenPosition={position} controller={active.GetType().FullName} cameraBefore={_before} requestedTarget={_target} requestedZoom={zoom}");
            try
            {
                // In this Game.dll CameraController.position has an empty setter.
                // pivot and zoom are public inputs consumed by UpdateCamera.
                if (!ReferenceEquals(active, gameplay)) gameplay.TryMatchPosition(active);
                if (camera.orbitCameraController != null)
                    camera.orbitCameraController.followedEntity = Entity.Null;
                camera.activeCameraController = gameplay;
                gameplay.pivot = _target;
                gameplay.zoom = zoom;
                var tool = _world.GetExistingSystemManaged<ToolSystem>();
                if (tool != null) tool.selected = citizen;
                _pending = true;
                _deadline = DateTime.UtcNow.AddSeconds(3);
                Mod.Log.Info($"[CS2TwitchCitizens] CAMERA Find citizen={citizen} result=FocusRequested cameraAfterRequest={camera.activeCameraController.position}");
                return CitizenFocusStatus.FocusRequested;
            }
            catch (Exception ex)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] CAMERA Find citizen={citizen} result=FocusFailed error={ex.GetType().Name}");
                return CitizenFocusStatus.FocusFailed;
            }
        }

        public bool TryPollFocus(out CitizenFocusStatus status)
        {
            status = CitizenFocusStatus.FocusRequested;
            if (!_pending) return false;
            var camera = _world.GetExistingSystemManaged<CameraUpdateSystem>();
            var gameplay = camera?.gamePlayController;
            if (camera == null || gameplay == null || !ReferenceEquals(camera.activeCameraController, gameplay))
            {
                status = CitizenFocusStatus.FocusFailed;
                _pending = false;
            }
            else
            {
                var after = gameplay.position;
                var afterDistance = HorizontalDistance(after, _target);
                if (afterDistance <= 60f)
                {
                    status = CitizenFocusStatus.FocusConfirmed;
                    _pending = false;
                }
                else if (DateTime.UtcNow >= _deadline)
                {
                    status = CitizenFocusStatus.FocusFailed;
                    _pending = false;
                }
            }
            if (_pending) return false;
            Mod.Log.Info($"[CS2TwitchCitizens] CAMERA Find result={status} cameraBefore={_before} cameraAfter={camera?.activeCameraController?.position.ToString() ?? "unavailable"} requestedTarget={_target}");
            return true;
        }

        public CitizenFocusStatus Follow(Entity citizen, CitizenPosition position)
        {
            _pending = false;
            var camera = _world.GetExistingSystemManaged<CameraUpdateSystem>();
            var orbit = camera?.orbitCameraController;
            var active = camera?.activeCameraController;
            if (camera == null || orbit == null || active == null || !orbit.inputEnabled)
                return CitizenFocusStatus.CameraUnavailable;
            var before = active.position;
            try
            {
                orbit.mode = OrbitCameraController.Mode.Follow;
                orbit.followedEntity = citizen;
                if (!ReferenceEquals(active, orbit)) orbit.TryMatchPosition(active);
                orbit.zoom = RequestedZoom;
                camera.activeCameraController = orbit;
                var tool = _world.GetExistingSystemManaged<ToolSystem>();
                if (tool != null) tool.selected = citizen;
                Mod.Log.Info($"[CS2TwitchCitizens] CAMERA Follow citizen={citizen} citizenPosition={position} controller={active.GetType().FullName} cameraBefore={before} cameraAfterRequest={camera.activeCameraController.position} requestedTarget={position} requestedZoom={orbit.zoom} result=FollowRequested");
                return CitizenFocusStatus.FollowRequested;
            }
            catch (Exception ex)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] CAMERA Follow citizen={citizen} result=FocusFailed error={ex.GetType().Name}");
                return CitizenFocusStatus.FocusFailed;
            }
        }

        public CitizenFocusStatus StopFollowing()
        {
            _pending = false;
            var camera = _world.GetExistingSystemManaged<CameraUpdateSystem>();
            var orbit = camera?.orbitCameraController;
            var gameplay = camera?.gamePlayController;
            if (camera == null || orbit == null || gameplay == null)
                return CitizenFocusStatus.CameraUnavailable;
            try
            {
                if (ReferenceEquals(camera.activeCameraController, orbit))
                {
                    gameplay.TryMatchPosition(orbit);
                    camera.activeCameraController = gameplay;
                }
                orbit.followedEntity = Entity.Null;
                Mod.Log.Info("[CS2TwitchCitizens] CAMERA Follow result=FollowStopped");
                return CitizenFocusStatus.FollowStopped;
            }
            catch (Exception ex)
            {
                Mod.Log.Info($"[CS2TwitchCitizens] CAMERA Follow result=FocusFailed error={ex.GetType().Name}");
                return CitizenFocusStatus.FocusFailed;
            }
        }

        public void StopFollowingIf(Entity citizen)
        {
            if (citizen == Entity.Null) return;
            var orbit = _world.GetExistingSystemManaged<CameraUpdateSystem>()?.orbitCameraController;
            if (orbit != null && orbit.followedEntity == citizen) StopFollowing();
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
