using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;

namespace UnityAgentClient
{
    /// <summary>
    /// Bake NavMesh.
    /// </summary>
    public class NavMeshBakeTool : IMcpTool
    {
        public string Name => "navmesh_bake";
        public string Description => "Bake the NavMesh for the active scene using current NavMesh settings.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            return McpToolResult.Success("NavMesh bake completed");
        }
    }

    /// <summary>
    /// Get NavMesh settings.
    /// </summary>
    public class NavMeshGetSettingsTool : IMcpTool
    {
        public string Name => "navmesh_get_settings";
        public string Description => "Get NavMesh build settings and list all NavMesh agent types configured in the project.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var sb = new StringBuilder();

            sb.AppendLine("== NavMesh Build Settings ==");
            var settings = NavMesh.GetSettingsByIndex(0);
            sb.AppendLine($"Agent Radius: {settings.agentRadius}");
            sb.AppendLine($"Agent Height: {settings.agentHeight}");
            sb.AppendLine($"Max Slope: {settings.agentSlope}");
            sb.AppendLine($"Step Height: {settings.agentClimb}");
            sb.AppendLine();

            sb.AppendLine("== NavMesh Agent Types ==");
            var count = NavMesh.GetSettingsCount();
            for (int i = 0; i < count; i++)
            {
                var s = NavMesh.GetSettingsByIndex(i);
                var name = NavMesh.GetSettingsNameFromID(s.agentTypeID);
                sb.AppendLine($"  [{i}] {name} (radius: {s.agentRadius}, height: {s.agentHeight})");
            }

            // List NavMesh areas
            sb.AppendLine();
            sb.AppendLine("== NavMesh Areas ==");
            var areaNames = NavMesh.GetAreaNames();
            for (int i = 0; i < areaNames.Length; i++)
            {
                var cost = NavMesh.GetAreaCost(i);
                sb.AppendLine($"  [{i}] {areaNames[i]} (cost: {cost})");
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Set layer collision matrix entries.
    /// </summary>
    public class PhysicsSetLayerCollisionTool : IMcpTool
    {
        public string Name => "physics_set_layer_collision";
        public string Description => "Enable or disable collision between two physics layers.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""layer1"": { ""type"": ""string"", ""description"": ""First layer name (e.g. 'Default', 'Water')."" },
                ""layer2"": { ""type"": ""string"", ""description"": ""Second layer name."" },
                ""ignore"": { ""type"": ""boolean"", ""description"": ""true to ignore (disable) collision, false to enable collision."" }
            },
            ""required"": [""layer1"", ""layer2"", ""ignore""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var layer1Name = args.GetProperty("layer1").GetString();
            var layer2Name = args.GetProperty("layer2").GetString();
            var ignore = args.GetProperty("ignore").GetBoolean();

            int layer1 = LayerMask.NameToLayer(layer1Name);
            int layer2 = LayerMask.NameToLayer(layer2Name);

            if (layer1 < 0) return McpToolResult.Error($"Layer not found: {layer1Name}");
            if (layer2 < 0) return McpToolResult.Error($"Layer not found: {layer2Name}");

            Physics.IgnoreLayerCollision(layer1, layer2, ignore);
            var state = ignore ? "disabled" : "enabled";
            return McpToolResult.Success($"Collision between '{layer1Name}' and '{layer2Name}' {state}");
        }
    }
}
