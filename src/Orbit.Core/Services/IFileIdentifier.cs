namespace Orbit.Core.Services;

using Orbit.Core.Models;

public interface IFileIdentifier
{
    FileIdentity Identify(string filePath);
    FileIdentity[] IdentifyMultiple(string[] filePaths);
    FileFamily DetectFamily(string extension);
    string? DetectMimeType(string filePath);
}
