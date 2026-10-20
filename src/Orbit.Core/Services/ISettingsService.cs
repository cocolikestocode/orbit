namespace Orbit.Core.Services;

using System;
using Orbit.Core.Models;

public interface ISettingsService
{
    OrbitSettings CurrentSettings { get; }
    void Save(OrbitSettings settings);
    OrbitSettings Load();
    event Action<OrbitSettings>? SettingsChanged;
}
