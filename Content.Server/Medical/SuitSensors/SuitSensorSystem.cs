// SPDX-FileCopyrightText: 2021 Alex Evgrashin
// SPDX-FileCopyrightText: 2021 Paul Ritter
// SPDX-FileCopyrightText: 2022 Jezithyr
// SPDX-FileCopyrightText: 2022 KIBORG04
// SPDX-FileCopyrightText: 2022 mirrorcult
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 Ahion
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2023 Leon Friedrich
// SPDX-FileCopyrightText: 2023 chromiumboy
// SPDX-FileCopyrightText: 2023 keronshb
// SPDX-FileCopyrightText: 2024 BombasterDS
// SPDX-FileCopyrightText: 2024 GreaseMonk
// SPDX-FileCopyrightText: 2024 Jake Huxell
// SPDX-FileCopyrightText: 2024 Julian Giebel
// SPDX-FileCopyrightText: 2024 Kara
// SPDX-FileCopyrightText: 2024 Pieter-Jan Briers
// SPDX-FileCopyrightText: 2024 Pspritechologist
// SPDX-FileCopyrightText: 2024 Qulibly
// SPDX-FileCopyrightText: 2024 Tayrtahn
// SPDX-FileCopyrightText: 2024 Whatstone
// SPDX-FileCopyrightText: 2024 chavonadelal
// SPDX-FileCopyrightText: 2024 eoineoineoin
// SPDX-FileCopyrightText: 2024 metalgearsloth
// SPDX-FileCopyrightText: 2024 nikthechampiongr
// SPDX-FileCopyrightText: 2024 slarticodefast
// SPDX-FileCopyrightText: 2024 themias
// SPDX-FileCopyrightText: 2025 Ignaz "Ian" Kraft
// SPDX-FileCopyrightText: 2025 Redrover1760
// SPDX-FileCopyrightText: 2025 ScyronX
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Server.Access.Systems;
using Content.Server.DeviceNetwork;
using Content.Server.DeviceNetwork.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Emp;
using Content.Server.Medical.CrewMonitoring;
using Content.Server.Popups;
//using Content.Server.Station.Systems; //Frontier Modification
using Content.Shared.ActionBlocker;
using Content.Shared.Clothing;
using Content.Shared.Damage;
using Content.Shared.DeviceNetwork;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics; //Frontier modification
using Content.Server.Salvage.Expeditions;
using Content.Server._Mono.Radar; // Monolith
using Content.Server.Explosion.EntitySystems;
using Content.Server._NF.Medical.SuitSensors; // Frontier modification

namespace Content.Server.Medical.SuitSensors;

public sealed class SuitSensorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly DeviceNetworkSystem _deviceNetworkSystem = default!;
    [Dependency] private readonly IdCardSystem _idCardSystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    // [Dependency] private readonly StationSystem _stationSystem = default!; // Frontier
    [Dependency] private readonly MetaDataSystem _metaData = default!; // Frontier
    [Dependency] private readonly SingletonDeviceNetServerSystem _singletonServerSystem = default!;
    [Dependency] private readonly MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        //SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn); // Frontier modification
        SubscribeLocalEvent<SuitSensorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SuitSensorComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<SuitSensorComponent, ClothingGotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<SuitSensorComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<SuitSensorComponent, GetVerbsEvent<Verb>>(OnVerb);
        SubscribeLocalEvent<SuitSensorComponent, EntGotInsertedIntoContainerMessage>(OnInsert);
        SubscribeLocalEvent<SuitSensorComponent, EntGotRemovedFromContainerMessage>(OnRemove);
        SubscribeLocalEvent<SuitSensorComponent, EmpPulseEvent>(OnEmpPulse);
        SubscribeLocalEvent<SuitSensorComponent, EmpDisabledRemoved>(OnEmpFinished);
        SubscribeLocalEvent<SuitSensorComponent, SuitSensorChangeDoAfterEvent>(OnSuitSensorDoAfter);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        //var sensors = EntityManager.EntityQueryEnumerator<SuitSensorComponent, DeviceNetworkComponent>(); // Frontier modification
        var sensors = EntityQueryEnumerator<SuitSensorComponent, DeviceNetworkComponent, TransformComponent>(); // Frontier modification

        while (sensors.MoveNext(out var uid, out var sensor, out var device, out var xform)) // Frontier modification
        {
            if (device.TransmitFrequency is null)
                continue;

            // check if sensor is ready to update
            if (curTime < sensor.NextUpdate)
                continue;

            /* -- Frontier modification
            if (!CheckSensorAssignedStation(uid, sensor))
                continue;
            */

            // TODO: This would cause imprecision at different tick rates.
            sensor.NextUpdate = curTime + sensor.UpdateRate;

            // get sensor status
            var status = GetSensorState(uid, sensor);
            if (status == null)
                continue;

            //Retrieve active server address if the sensor isn't connected to a server
            if (sensor.ConnectedServer == null)
            {
                // Frontier - PR 1053 QoL changes to coordinates display
                // if (!_singletonServerSystem.TryGetActiveServerAddress<CrewMonitoringServerComponent>(sensor.StationId!.Value, out var address))
                if (!_singletonServerSystem.TryGetActiveServerAddress<CrewMonitoringServerComponent>(xform.MapID, out var address))
                    continue;


                sensor.ConnectedServer = address;
            }

            // Send it to the connected server
            var payload = SuitSensorToPacket(status);

            // Clear the connected server if its address isn't on the network
            if (!_deviceNetworkSystem.IsAddressPresent(device.DeviceNetId, sensor.ConnectedServer))
            {
                sensor.ConnectedServer = null;
                continue;
            }

            _deviceNetworkSystem.QueuePacket(uid, sensor.ConnectedServer, payload, device: device);
        }
    }

    /* -- Frontier modification
    /// <summary>
    /// Checks whether the sensor is assigned to a station or not
    /// and tries to assign an unassigned sensor to a station if it's currently on a grid
    /// </summary>
    /// <returns>True if the sensor is assigned to a station or assigning it was successful. False otherwise.</returns>
    private bool CheckSensorAssignedStation(EntityUid uid, SuitSensorComponent sensor)
    {
        if (!sensor.StationId.HasValue && Transform(uid).GridUid == null)
            return false;

        sensor.StationId = _stationSystem.GetOwningStation(uid);
        return sensor.StationId.HasValue;
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent ev)
    {
        // If the player spawns in arrivals then the grid underneath them may not be appropriate.
        // in which case we'll just use the station spawn code told us they are attached to and set all of their
        // sensors.
        var sensorQuery = GetEntityQuery<SuitSensorComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();
        RecursiveSensor(ev.Mob, ev.Station, sensorQuery, xformQuery);
    }

    private void RecursiveSensor(EntityUid uid, EntityUid stationUid, EntityQuery<SuitSensorComponent> sensorQuery, EntityQuery<TransformComponent> xformQuery)
    {
        var xform = xformQuery.GetComponent(uid);
        var enumerator = xform.ChildEnumerator;

        while (enumerator.MoveNext(out var child))
        {
            if (sensorQuery.TryGetComponent(child, out var sensor))
            {
                sensor.StationId = stationUid;
            }

            RecursiveSensor(child, stationUid, sensorQuery, xformQuery);
        }
    }
    */

    private void OnMapInit(EntityUid uid, SuitSensorComponent component, MapInitEvent args)
    {
        /* -- Frontier modification
        // Fallback
        component.StationId ??= _stationSystem.GetOwningStation(uid);
        */

        // generate random mode
        if (component.RandomMode)
        {
            //make the sensor mode favor higher levels, except coords.
            var modesDist = new[]
            {
                SuitSensorMode.SensorOff,
                SuitSensorMode.SensorBinary, SuitSensorMode.SensorBinary,
                SuitSensorMode.SensorVitals, SuitSensorMode.SensorVitals, SuitSensorMode.SensorVitals,
                SuitSensorMode.SensorCords, SuitSensorMode.SensorCords
            };
            component.Mode = _random.Pick(modesDist);
        }
    }

    private void OnEquipped(EntityUid uid, SuitSensorComponent component, ref ClothingGotEquippedEvent args)
    {
        if (HasComp<DisableSuitSensorsComponent>(args.Wearer)) // Frontier: entities with disabled suit sensors must never be set as a valid user.
            return; // Frontier

        component.User = args.Wearer;
    }

    private void OnUnequipped(EntityUid uid, SuitSensorComponent component, ref ClothingGotUnequippedEvent args)
    {
        component.User = null;
    }

    private void OnExamine(EntityUid uid, SuitSensorComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        string msg;
        switch (component.Mode)
        {
            case SuitSensorMode.SensorOff:
                msg = "suit-sensor-examine-off";
                break;
            case SuitSensorMode.SensorBinary:
                msg = "suit-sensor-examine-binary";
                break;
            case SuitSensorMode.SensorVitals:
                msg = "suit-sensor-examine-vitals";
                break;
            case SuitSensorMode.SensorCords:
                msg = "suit-sensor-examine-cords";
                break;
            default:
                return;
        }
        // Mono Begin
        string radarMsg;
        switch (component.IFFSignatureEnabled)
        {
            case true:
                radarMsg = "suit-sensor-signature-examine-on";
                break;
            case false:
                radarMsg = "suit-sensor-signature-examine-off";
                break;
        }
        // Mono End
        args.PushMarkup(Loc.GetString(msg));
        args.PushMarkup(Loc.GetString(radarMsg)); // Mono
    }

    private void OnVerb(EntityUid uid, SuitSensorComponent component, GetVerbsEvent<Verb> args)
    {
        // check if user can change sensor
        if (component.ControlsLocked)
            return;

        // standard interaction checks
        if (!args.CanInteract || args.Hands == null)
            return;

        if (!_interactionSystem.InRangeUnobstructed(args.User, args.Target))
            return;

        // check if target is incapacitated (cuffed, dead, etc)
        if (component.User != null && args.User != component.User && _actionBlocker.CanInteract(component.User.Value, null))
            return;

        args.Verbs.UnionWith(new[]
        {
            CreateVerb(uid, component, args.User, SuitSensorMode.SensorOff),
            CreateVerb(uid, component, args.User, SuitSensorMode.SensorBinary),
            CreateVerb(uid, component, args.User, SuitSensorMode.SensorVitals),
            CreateVerb(uid, component, args.User, SuitSensorMode.SensorCords)
        });

        // Monolith IFF signature edit Start
        var verb = new Verb
        {
            Text = Loc.GetString("suit-sensor-signature-toggle", ("status", GetStatusSignatureName(component))),
            Act = () =>
            {
                TryToggleSignature(uid, component);
            }
        };
        args.Verbs.Add(verb);
        // End
    }

    private void OnInsert(EntityUid uid, SuitSensorComponent component, EntGotInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != component.ActivationContainer)
            return;

        if (HasComp<DisableSuitSensorsComponent>(args.Container.Owner)) // Frontier: entities with disabled suit sensors must never be set as a valid user.
            return; // Frontier

        component.User = args.Container.Owner;
    }

    private void OnRemove(EntityUid uid, SuitSensorComponent component, EntGotRemovedFromContainerMessage args)
    {
        if (args.Container.ID != component.ActivationContainer)
            return;

        component.User = null;
    }

    private void OnEmpPulse(EntityUid uid, SuitSensorComponent component, ref EmpPulseEvent args)
    {
        args.Affected = true;
        args.Disabled = true;

        component.PreviousMode = component.Mode;
        SetSensor((uid, component), SuitSensorMode.SensorOff, null);

        component.PreviousControlsLocked = component.ControlsLocked;
        component.ControlsLocked = true;
    }

    private void OnEmpFinished(EntityUid uid, SuitSensorComponent component, ref EmpDisabledRemoved args)
    {
        SetSensor((uid, component), component.PreviousMode, null);
        component.ControlsLocked = component.PreviousControlsLocked;
    }

    private Verb CreateVerb(EntityUid uid, SuitSensorComponent component, EntityUid userUid, SuitSensorMode mode)
    {
        return new Verb()
        {
            Text = GetModeName(mode),
            Disabled = component.Mode == mode,
            Priority = -(int) mode, // sort them in descending order
            Category = VerbCategory.SetSensor,
            Act = () => TrySetSensor((uid, component), mode, userUid)
        };
    }

    private string GetModeName(SuitSensorMode mode)
    {
        string name;
        switch (mode)
        {
            case SuitSensorMode.SensorOff:
                name = "suit-sensor-mode-off";
                break;
            case SuitSensorMode.SensorBinary:
                name = "suit-sensor-mode-binary";
                break;
            case SuitSensorMode.SensorVitals:
                name = "suit-sensor-mode-vitals";
                break;
            case SuitSensorMode.SensorCords:
                name = "suit-sensor-mode-cords";
                break;
            default:
                return "";
        }

        return Loc.GetString(name);
    }

    // Mono Begin
    private string GetStatusSignatureName(SuitSensorComponent component)
    {
        string signatureName;
        switch (component.IFFSignatureEnabled)
        {
            case true:
                signatureName = "suit-sensor-signature-verb-disable";
                break;
            case false:
                signatureName = "suit-sensor-signature-verb-enable";
                break;
        }

        return Loc.GetString(signatureName);
    }
    // Mono End

    public void TrySetSensor(Entity<SuitSensorComponent> sensors, SuitSensorMode mode, EntityUid userUid)
    {
        var comp = sensors.Comp;

        if (!Resolve(sensors, ref comp))
            return;

        if (comp.User == null || userUid == comp.User)
            SetSensor(sensors, mode, userUid);
        else
        {
            var doAfterEvent = new SuitSensorChangeDoAfterEvent(mode);
            var doAfterArgs = new DoAfterArgs(EntityManager, userUid, comp.SensorsTime, doAfterEvent, sensors)
            {
                BreakOnMove = true,
                BreakOnDamage = true
            };

            _doAfterSystem.TryStartDoAfter(doAfterArgs);
        }
    }

    // Monolith - Radar signature toggle verb
    public void TryToggleSignature(EntityUid uid, SuitSensorComponent comp)
    {
        if (comp.IFFSignatureEnabled || HasComp<RadarBlipComponent>(uid))
        {
            comp.IFFSignatureEnabled = false;
            RemComp<RadarBlipComponent>(uid);
            _popupSystem.PopupEntity(Loc.GetString("suit-sensor-signature-toggled-off"), uid);
        }
        else
        {
            comp.IFFSignatureEnabled = true;
            var blip = EnsureComp<RadarBlipComponent>(uid);
            blip.RadarColor = Color.Cyan;
            blip.Scale = 0.5f;
            blip.VisibleFromOtherGrids = true;
            _popupSystem.PopupEntity(Loc.GetString("suit-sensor-signature-toggled-on"), uid);
        }
    }

    private void OnSuitSensorDoAfter(Entity<SuitSensorComponent> sensors, ref SuitSensorChangeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        SetSensor(sensors, args.Mode, args.User);
    }

    public void SetSensor(Entity<SuitSensorComponent> sensors, SuitSensorMode mode, EntityUid? userUid = null)
    {
        var comp = sensors.Comp;

        comp.Mode = mode;

        if (userUid != null)
        {
            var msg = Loc.GetString("suit-sensor-mode-state", ("mode", GetModeName(mode)));
            _popupSystem.PopupEntity(msg, sensors, userUid.Value);
        }
    }

    public SuitSensorStatus? GetSensorState(EntityUid uid, SuitSensorComponent? sensor = null, TransformComponent? transform = null)
    {
        if (!Resolve(uid, ref sensor, ref transform))
            return null;

        // check if sensor is enabled and worn by user
        // Frontier modification, made sensor work with grid being null
        if (sensor.Mode == SuitSensorMode.SensorOff || sensor.User == null || !HasComp<MobStateComponent>(sensor.User) ) // || transform.GridUid == null
            return null;

        // try to get mobs id from ID slot
        var userName = Loc.GetString("suit-sensor-component-unknown-name");
        var userJob = Loc.GetString("suit-sensor-component-unknown-job");
        var userJobIcon = "JobIconNoId";
        var userJobDepartments = new List<string>();
        var userLocationName = Loc.GetString("suit-sensor-location-unknown"); // Frontier modification

        if (_idCardSystem.TryFindIdCard(sensor.User.Value, out var card))
        {
            if (card.Comp.FullName != null)
                userName = card.Comp.FullName;
            if (card.Comp.LocalizedJobTitle != null)
                userJob = card.Comp.LocalizedJobTitle;
            userJobIcon = card.Comp.JobIcon;

            foreach (var department in card.Comp.JobDepartments)
                userJobDepartments.Add(Loc.GetString(_proto.Index(department).Name));
        }

        // get health mob state
        var isAlive = false;
        if (EntityManager.TryGetComponent(sensor.User.Value, out MobStateComponent? mobState))
            isAlive = !_mobStateSystem.IsDead(sensor.User.Value, mobState);

        // get mob total damage
        var totalDamage = 0;
        if (TryComp<DamageableComponent>(sensor.User.Value, out var damageable))
            totalDamage = damageable.TotalDamage.Int();

        // Get mob total damage crit threshold
        int? totalDamageThreshold = null;
        if (_mobThresholdSystem.TryGetThresholdForState(sensor.User.Value, MobState.Critical, out var critThreshold))
            totalDamageThreshold = critThreshold.Value.Int();

        // finally, form suit sensor status
        // will additonally check the grid and name if it exists, as well if its expedition
        var status = new SuitSensorStatus(GetNetEntity(uid), userName, userJob, userJobIcon, userJobDepartments, userLocationName);
        switch (sensor.Mode)
        {
            case SuitSensorMode.SensorBinary:
                status.IsAlive = isAlive;
                break;
            case SuitSensorMode.SensorVitals:
                status.IsAlive = isAlive;
                status.TotalDamage = totalDamage;
                status.TotalDamageThreshold = totalDamageThreshold;
                break;
            case SuitSensorMode.SensorCords:
                status.IsAlive = isAlive;
                status.TotalDamage = totalDamage;
                status.TotalDamageThreshold = totalDamageThreshold;
                EntityCoordinates coordinates;
                var xformQuery = GetEntityQuery<TransformComponent>();
                var locationName = "";

                if (transform.GridUid != null)
                {

                    coordinates = new EntityCoordinates(transform.GridUid.Value,
                        Vector2.Transform(_transform.GetWorldPosition(transform, xformQuery),
                            _transform.GetInvWorldMatrix(xformQuery.GetComponent(transform.GridUid.Value), xformQuery)));

                    // Frontier modification
                    /// Checks if sensor is present on expedition grid
                    if(TryComp<SalvageExpeditionComponent>(transform.GridUid.Value, out var salvageComp))
                    {
                        locationName = Loc.GetString("suit-sensor-location-expedition");
                    }
                    else
                    {
                        var meta = MetaData(transform.GridUid.Value);

                        locationName = meta.EntityName;
                    }
                }
                else if (transform.MapUid != null)
                {

                    coordinates = new EntityCoordinates(transform.MapUid.Value,
                        _transform.GetWorldPosition(transform, xformQuery)); //Frontier modification

                    locationName = Loc.GetString("suit-sensor-location-space"); // Frontier modification
                }
                else
                {
                    coordinates = EntityCoordinates.Invalid;

                    locationName = Loc.GetString("suit-sensor-location-unknown"); // Frontier modification
                }

                status.Coordinates = GetNetCoordinates(coordinates);
                status.LocationName = locationName; //Frontier modification
                break;
        }

        return status;
    }

    /// <summary>
    ///     Serialize create a device network package from the suit sensors status.
    /// </summary>
    public NetworkPayload SuitSensorToPacket(SuitSensorStatus status)
    {
        var payload = new NetworkPayload()
        {
            [DeviceNetworkConstants.Command] = DeviceNetworkConstants.CmdUpdatedState,
            [SuitSensorConstants.NET_NAME] = status.Name,
            [SuitSensorConstants.NET_JOB] = status.Job,
            [SuitSensorConstants.NET_JOB_ICON] = status.JobIcon,
            [SuitSensorConstants.NET_JOB_DEPARTMENTS] = status.JobDepartments,
            [SuitSensorConstants.NET_IS_ALIVE] = status.IsAlive,
            [SuitSensorConstants.NET_SUIT_SENSOR_UID] = status.SuitSensorUid,
        };

        if (status.TotalDamage != null)
            payload.Add(SuitSensorConstants.NET_TOTAL_DAMAGE, status.TotalDamage);
        if (status.TotalDamageThreshold != null)
            payload.Add(SuitSensorConstants.NET_TOTAL_DAMAGE_THRESHOLD, status.TotalDamageThreshold);
        if (status.Coordinates != null)
            payload.Add(SuitSensorConstants.NET_COORDINATES, status.Coordinates);
        if (status.LocationName != null) //Frontier modification
            payload.Add(SuitSensorConstants.NET_LOCATION_NAME, status.LocationName); //Frontier modification

        return payload;
    }

    /// <summary>
    ///     Try to create the suit sensors status from the device network message
    /// </summary>
    public SuitSensorStatus? PacketToSuitSensor(NetworkPayload payload)
    {
        // check command
        if (!payload.TryGetValue(DeviceNetworkConstants.Command, out string? command))
            return null;
        if (command != DeviceNetworkConstants.CmdUpdatedState)
            return null;

        // check name, job and alive
        if (!payload.TryGetValue(SuitSensorConstants.NET_NAME, out string? name)) return null;
        if (!payload.TryGetValue(SuitSensorConstants.NET_JOB, out string? job)) return null;
        if (!payload.TryGetValue(SuitSensorConstants.NET_JOB_ICON, out string? jobIcon)) return null;
        if (!payload.TryGetValue(SuitSensorConstants.NET_JOB_DEPARTMENTS, out List<string>? jobDepartments)) return null;
        if (!payload.TryGetValue(SuitSensorConstants.NET_IS_ALIVE, out bool? isAlive)) return null;
        if (!payload.TryGetValue(SuitSensorConstants.NET_SUIT_SENSOR_UID, out NetEntity suitSensorUid)) return null;
        if (!payload.TryGetValue(SuitSensorConstants.NET_LOCATION_NAME, out string? location)) return null; // Frontier modification

        // try get total damage and cords and location name (optionals)
        payload.TryGetValue(SuitSensorConstants.NET_TOTAL_DAMAGE, out int? totalDamage);
        payload.TryGetValue(SuitSensorConstants.NET_TOTAL_DAMAGE_THRESHOLD, out int? totalDamageThreshold);
        payload.TryGetValue(SuitSensorConstants.NET_COORDINATES, out NetCoordinates? coords);

        var status = new SuitSensorStatus(suitSensorUid, name, job, jobIcon, jobDepartments, location)
        {
            IsAlive = isAlive.Value,
            TotalDamage = totalDamage,
            TotalDamageThreshold = totalDamageThreshold,
            Coordinates = coords,
        };
        return status;
    }
}
