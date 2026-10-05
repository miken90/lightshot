// Ported from LightshotKit/Sources/LightshotKit/PermissionGate.swift
// MIT License, Copyright (c) 2026 Viet Le

using System.Threading.Tasks;

namespace Lightshot.Core;

/// <summary>
/// What a permission ask concluded.
/// </summary>
public enum PermissionOutcome
{
    Granted,
    Prompting,
    Denied
}

/// <summary>
/// The one permission-asking policy.
/// </summary>
public static class PermissionGate
{
    public static async Task<PermissionOutcome> EnsureAsync(IPermissionAuthorizing source)
    {
        CaptureAuthorizationStatus before = await source.AuthorizationStatusAsync();
        if (before == CaptureAuthorizationStatus.Authorized)
        {
            return PermissionOutcome.Granted;
        }

        CaptureAuthorizationStatus after = await source.RequestAuthorizationAsync();
        if (after == CaptureAuthorizationStatus.Authorized)
        {
            return PermissionOutcome.Granted;
        }

        if (before == CaptureAuthorizationStatus.Denied || source.RequestWaitsForAnswer)
        {
            return PermissionOutcome.Denied;
        }

        return PermissionOutcome.Prompting;
    }
}
