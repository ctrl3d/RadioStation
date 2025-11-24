using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace work.ctrl3d.Editor
{
    [InitializeOnLoad]
    public class PackageInstaller
    {
        private const string UniTaskName = "com.cysharp.unitask";

        private const string UniTaskGitUrl =
            "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask";
        
        static PackageInstaller()
        {
            var isUniTaskInstalled = CheckPackageInstalled(UniTaskName);
            if (!isUniTaskInstalled) AddGitPackage(UniTaskName, UniTaskGitUrl);
        }

        private static void AddGitPackage(string packageName, string gitUrl)
        {
            var path = Path.Combine(Application.dataPath, "../Packages/manifest.json");
            var jsonString = File.ReadAllText(path);

            var indexOfLastBracket = jsonString.IndexOf("}", StringComparison.Ordinal);
            var dependenciesSubstring = jsonString[..indexOfLastBracket];
            var endOfLastPackage = dependenciesSubstring.LastIndexOf("\"", StringComparison.Ordinal);

            jsonString = jsonString.Insert(endOfLastPackage + 1, $", \n \"{packageName}\": \"{gitUrl}\"");

            File.WriteAllText(path, jsonString);
            Client.Resolve();
        }

        private static bool CheckPackageInstalled(string packageName)
        {
            var path = Path.Combine(Application.dataPath, "../Packages/manifest.json");
            var jsonString = File.ReadAllText(path);
            return jsonString.Contains(packageName);
        }
    }
}