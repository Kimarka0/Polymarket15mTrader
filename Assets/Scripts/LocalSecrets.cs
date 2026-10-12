using System;
using System.IO;
using UnityEngine;

// Shape of Secrets/polymarket.local.json. See Secrets/polymarket.example.json.
[Serializable]
public class LocalSecretsFile
{
    public string privateKey;
}

// Reads the private key from a local file outside Assets, so it never ends up in a scene or in git.
// The path is relative to the project folder, so this works in the Editor; a build needs its own location.
public static class LocalSecrets
{
    public const string RelativePath = "Secrets/polymarket.local.json";

    public static string FullPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", RelativePath));

    // Error messages contain only the path, never the key.
    public static bool TryLoadPrivateKey(out string privateKey, out string error)
    {
        privateKey = null;
        string path = FullPath;

        if (!File.Exists(path))
        {
            error = $"Private key file not found: {path}. Copy Secrets/polymarket.example.json to {RelativePath} and put the key there.";
            return false;
        }

        LocalSecretsFile file;
        try
        {
            file = JsonUtility.FromJson<LocalSecretsFile>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            error = $"Cannot read {RelativePath}: the file is not valid JSON.";
            return false;
        }

        if (file == null || string.IsNullOrWhiteSpace(file.privateKey))
        {
            error = $"{RelativePath} has no \"privateKey\" value.";
            return false;
        }

        privateKey = file.privateKey.Trim();
        error = null;
        return true;
    }
}
