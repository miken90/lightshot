// Ported from LightshotKit/Sources/LightshotKit/PermissionOnboardingModel.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Lightshot.Core;

/// <summary>
/// The first-run onboarding state machine: a checklist of the permissions Lightshot needs,
/// each with its live authorization status, driven purely through the IPermissionAuthorizing seam.
/// </summary>
public class PermissionOnboardingModel : INotifyPropertyChanged
{
    public class Requirement : INotifyPropertyChanged
    {
        private CaptureAuthorizationStatus _status;

        public PermissionKind Kind { get; }
        public string Title { get; }
        public string Rationale { get; }
        public IPermissionAuthorizing Source { get; }

        public CaptureAuthorizationStatus Status
        {
            get => _status;
            internal set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsGranted));
                }
            }
        }

        public bool IsGranted => Status == CaptureAuthorizationStatus.Authorized;

        public Requirement(
            PermissionKind kind,
            string title,
            string rationale,
            IPermissionAuthorizing source)
        {
            Kind = kind;
            Title = title;
            Rationale = rationale;
            Source = source;
            _status = CaptureAuthorizationStatus.NotDetermined;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private readonly Action<PermissionKind> _openSettings;
    private readonly List<Requirement> _requirements;

    public IReadOnlyList<Requirement> Requirements => _requirements;

    public bool IsSatisfied => _requirements.All(r => r.IsGranted);

    public event PropertyChangedEventHandler? PropertyChanged;

    public PermissionOnboardingModel(
        IEnumerable<Requirement> requirements,
        Action<PermissionKind>? openSettings = null)
    {
        _requirements = requirements.ToList();
        _openSettings = openSettings ?? (_ => { });

        foreach (var req in _requirements)
        {
            req.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Requirement.IsGranted))
                {
                    OnPropertyChanged(nameof(IsSatisfied));
                }
            };
        }
    }

    public async Task RefreshAsync()
    {
        foreach (Requirement req in _requirements)
        {
            req.Status = await req.Source.AuthorizationStatusAsync();
        }
        OnPropertyChanged(nameof(IsSatisfied));
    }

    public async Task EnableAsync(PermissionKind kind)
    {
        Requirement? req = _requirements.FirstOrDefault(r => r.Kind == kind);
        if (req is null) return;

        PermissionOutcome outcome = await PermissionGate.EnsureAsync(req.Source);
        req.Status = await req.Source.AuthorizationStatusAsync();
        if (outcome == PermissionOutcome.Denied)
        {
            _openSettings(kind);
        }
        OnPropertyChanged(nameof(IsSatisfied));
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
