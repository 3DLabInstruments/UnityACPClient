using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// Bootstraps the Core DLL's injectable services on editor load.
    /// Wires Logger → UnityEngine.Debug, SessionStore → Temp/,
    /// SkillRegistry → Editor/Skills paths.
    /// </summary>
    [InitializeOnLoad]
    internal static class CoreBootstrap
    {
        static CoreBootstrap()
        {
            // Logger → Unity console
            Logger.LogHandler = msg => Debug.Log(msg);
            Logger.ErrorHandler = msg => Debug.LogError(msg);
            Logger.WarningHandler = msg => Debug.LogWarning(msg);
            Logger.IsVerboseEnabled = () =>
            {
                var s = AgentSettingsProvider.Load();
                return s != null && s.VerboseLogging;
            };

            // SessionStore → project Temp/ folder
            var projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            SessionStore.PersistDirectory = System.IO.Path.Combine(projectRoot, "Temp");

            // SkillRegistry paths
            var packagePath = System.IO.Path.GetFullPath("Packages/com.yetsmarch.unity-agent-client");
            SkillRegistry.BuiltinSkillsPath = System.IO.Directory.Exists(packagePath)
                ? System.IO.Path.Combine(packagePath, "Editor", "Skills")
                : System.IO.Path.Combine(Application.dataPath, "UnityAgentClient", "Editor", "Skills");
            SkillRegistry.UserSkillsPath = System.IO.Path.Combine(projectRoot, "UserSettings", "UnityAgentClient", "Skills");
        }
    }
}