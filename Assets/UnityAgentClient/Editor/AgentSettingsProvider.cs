using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace UnityAgentClient
{
    internal class SerializableAgentSettingsContainer : ScriptableObject
    {
        [SerializeField] public SerializableAgentSettings value;
    }

    [Serializable]
    internal class SerializableAgentSettings
    {
        [Serializable]
        class EnvironmentVariable
        {
            [SerializeField] string key;
            [SerializeField] string value;

            public string Key => key;
            public string Value => value;

            public EnvironmentVariable(string key, string value)
            {
                this.key = key;
                this.value = value;
            }
        }

        [SerializeField] string command;
        [SerializeField] string arguments;
        [SerializeField] EnvironmentVariable[] environmentVariables;
        [SerializeField] bool verboseLogging;

        public SerializableAgentSettings()
        {
            command = "";
            arguments = "";
            environmentVariables = new EnvironmentVariable[0];
        }

        public SerializableAgentSettings(AgentSettings settings)
        {
            command = settings.Command;
            arguments = settings.Arguments;
            environmentVariables = settings.EnvironmentVariables?
                .Select(x => new EnvironmentVariable(x.Key, x.Value))
                .ToArray() ?? new EnvironmentVariable[0];
            verboseLogging = settings.VerboseLogging;
        }

        public AgentSettings ToAgentSettings()
        {
            return new()
            {
                Command = command,
                Arguments = arguments,
                EnvironmentVariables = environmentVariables?.ToDictionary(x => x.Key, x => x.Value) ?? new Dictionary<string, string>(),
                VerboseLogging = verboseLogging,
            };
        }
    }

    public class AgentSettingsProvider : SettingsProvider
    {
        const string ConfigFileName = "UnityAgentClientSettings.json";
        static readonly string ConfigFilePath = Path.Combine(GetUserSettingsPath(), ConfigFileName);
        static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        SerializableAgentSettingsContainer container;
        SerializedObject serializedObject;
        Vector2 scrollPosition;

        public AgentSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) : base(path, scopes, keywords)
        {
        }

        public override void OnGUI(string searchContext)
        {
            if (serializedObject == null)
            {
                var settings = Load() ?? new();
                container = ScriptableObject.CreateInstance<SerializableAgentSettingsContainer>();
                container.value = new SerializableAgentSettings(settings);
                serializedObject = new SerializedObject(container);
            }
            else
            {
                serializedObject.Update();
            }

            EditorGUI.BeginChangeCheck();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            var settingsProperty = serializedObject.FindProperty("value");
            if (settingsProperty != null)
            {
                EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("command"));
                EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("arguments"));
                EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("environmentVariables"));
                EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("verboseLogging"));
            }

            EditorGUILayout.EndScrollView();

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                Save(container.value.ToAgentSettings());
            }
        }
        [SettingsProvider]
        public static SettingsProvider CreateAgentSettingsProvider()
        {
            var provider = new AgentSettingsProvider("Project/Unity Agent Client", SettingsScope.Project)
            {
                keywords = new HashSet<string>(new[] { "Agent", "Command", "Arguments", "Environment" })
            };
            return provider;
        }

        static string GetUserSettingsPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var userSettingsPath = Path.Combine(projectRoot, "UserSettings");

            if (!Directory.Exists(userSettingsPath))
            {
                Directory.CreateDirectory(userSettingsPath);
            }

            return userSettingsPath;
        }

        public static AgentSettings Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    return JsonSerializer.Deserialize<AgentSettings>(json, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load config: {ex.Message}");
            }

            return null;
        }

        public static void Save(AgentSettings config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save config: {ex.Message}");
            }
        }
    }
}
