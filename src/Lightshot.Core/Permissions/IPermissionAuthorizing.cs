// Ported from LightshotKit/Sources/LightshotKit/PermissionAuthorizing.swift
// MIT License, Copyright (c) 2026 Viet Le

using System.Threading.Tasks;

namespace Lightshot.Core;

/// <summary>
/// The permission surface for a single OS authorization the app must hold.
/// </summary>
public interface IPermissionAuthorizing
{
    Task<CaptureAuthorizationStatus> AuthorizationStatusAsync();

    Task<CaptureAuthorizationStatus> RequestAuthorizationAsync();

    bool RequestWaitsForAnswer => false;
}
