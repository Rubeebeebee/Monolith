// SPDX-FileCopyrightText: 2023 metalgearsloth
// SPDX-FileCopyrightText: 2024 Leon Friedrich
// SPDX-FileCopyrightText: 2024 Pieter-Jan Briers
// SPDX-FileCopyrightText: 2025 SX-7
// SPDX-FileCopyrightText: 2025 SlamBamActionman
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client.Movement.Systems;

public sealed class ContentEyeSystem : SharedContentEyeSystem
{
    [Dependency] private readonly IPlayerManager _player = default!;

    public void RequestZoom(EntityUid uid, Vector2 zoom, bool ignoreLimit, bool scalePvs, ContentEyeComponent? content = null)
    {
        if (!Resolve(uid, ref content, false))
            return;

        RaisePredictiveEvent(new RequestTargetZoomEvent()
        {
            TargetZoom = zoom,
            IgnoreLimit = ignoreLimit,
        });

        if (scalePvs)
            RequestPvsScale(Math.Max(zoom.X, zoom.Y));
    }

    public void RequestPvsScale(float scale)
    {
        RaiseNetworkEvent(new RequestPvsScaleEvent(scale));
    }

    public void RequestToggleFov()
    {
        if (_player.LocalEntity is { } player)
            RequestToggleFov(player);
    }

    public void RequestToggleFov(EntityUid uid, EyeComponent? eye = null)
    {
        if (Resolve(uid, ref eye, false))
            RequestEye(!eye.DrawFov, eye.DrawLight);
    }

    public void RequestToggleLight(EntityUid uid, EyeComponent? eye = null)
    {
        if (Resolve(uid, ref eye, false))
            RequestEye(eye.DrawFov, !eye.DrawLight);
    }


    public void RequestEye(bool drawFov, bool drawLight)
    {
        RaisePredictiveEvent(new RequestEyeEvent(drawFov, drawLight));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var eyeEntities = AllEntityQuery<ContentEyeComponent, EyeComponent>();
        while (eyeEntities.MoveNext(out var entity, out ContentEyeComponent? contentComponent, out EyeComponent? eyeComponent))
        {
            UpdateEyeOffset((entity, eyeComponent));
        }
    }

    // <Goob - grabbed wizden PR #35087> {please remove this when you merge stable}
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var eyeEntities = AllEntityQuery<ContentEyeComponent, EyeComponent>();
        while (eyeEntities.MoveNext(out var entity, out ContentEyeComponent? contentComponent, out EyeComponent? eyeComponent))
        {
            UpdateEyeOffset((entity, eyeComponent));
        }
    }
    // </Goob - grabbed wizden PR #35087>
}
