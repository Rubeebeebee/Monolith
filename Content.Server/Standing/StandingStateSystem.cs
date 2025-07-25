// SPDX-FileCopyrightText: 2020 Remie Richards
// SPDX-FileCopyrightText: 2021 Acruid
// SPDX-FileCopyrightText: 2021 Clyybber
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto
// SPDX-FileCopyrightText: 2021 mirrorcult
// SPDX-FileCopyrightText: 2022 Leon Friedrich
// SPDX-FileCopyrightText: 2022 Rane
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2023 Visne
// SPDX-FileCopyrightText: 2024 metalgearsloth
// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 Redrover1760
// SPDX-FileCopyrightText: 2025 Rubeebeebee
// SPDX-FileCopyrightText: 2025 TemporalOroboros
// SPDX-FileCopyrightText: 2025 Whatstone
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared._NF.Standing;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server.Standing;

public sealed class StandingStateSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;

    private void FallOver(EntityUid uid, StandingStateComponent component, DropHandItemsEvent args)
    {
        var direction = EntityManager.TryGetComponent(uid, out PhysicsComponent? comp) ? comp.LinearVelocity / 50 : Vector2.Zero;
        var dropAngle = _random.NextFloat(0.8f, 1.2f);

        var fellEvent = new FellDownEvent(uid);
        RaiseLocalEvent(uid, fellEvent, false);

        if (!TryComp(uid, out HandsComponent? handsComp))
            return;

        if (HasComp<PreventDropOnDownedComponent>(uid)) // Frontier: stop dropping items when falling over.
            return; // Frontier

        var worldRotation = _transformSystem.GetWorldRotation(uid).ToVec();
        foreach (var hand in handsComp.Hands.Values)
        {
            if (hand.HeldEntity is not EntityUid held)
                continue;

            if (!_handsSystem.TryDrop(uid, hand, null, checkActionBlocker: false, handsComp: handsComp))
                continue;

            _throwingSystem.TryThrow(held,
                _random.NextAngle().RotateVec(direction / dropAngle + worldRotation / 50),
                0.5f * dropAngle * _random.NextFloat(-0.9f, 1.1f),
                uid, 0);
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StandingStateComponent, DropHandItemsEvent>(FallOver);
    }

}

/// <summary>
/// Raised after an entity falls down.
/// </summary>
public sealed class FellDownEvent : EntityEventArgs
{
    public EntityUid Uid { get; }
    public FellDownEvent(EntityUid uid)
    {
        Uid = uid;
    }
}
