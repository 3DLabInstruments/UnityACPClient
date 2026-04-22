using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// List Animator Controllers in the project.
    /// </summary>
    public class AnimationGetControllersTool : IMcpTool
    {
        public string Name => "animation_get_controllers";
        public string Description => "List all Animator Controller assets in the project with their layer and parameter counts.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""path"": { ""type"": ""string"", ""description"": ""Folder to search in (default: 'Assets')."" }
            }
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var searchPath = "Assets";
            if (args.ValueKind != JsonValueKind.Undefined && args.TryGetProperty("path", out var p))
                searchPath = p.GetString();

            var guids = AssetDatabase.FindAssets("t:AnimatorController", new[] { searchPath });
            var sb = new StringBuilder();
            sb.AppendLine($"Found {guids.Length} Animator Controller(s):");
            sb.AppendLine();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null) continue;

                sb.AppendLine($"  {path}");
                sb.AppendLine($"    Layers: {controller.layers.Length}");
                sb.AppendLine($"    Parameters: {controller.parameters.Length}");
                sb.AppendLine();
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Get state machine structure of an Animator Controller.
    /// </summary>
    public class AnimationGetStatesTool : IMcpTool
    {
        public string Name => "animation_get_states";
        public string Description => "Get the state machine structure of an Animator Controller, including all layers, states, and transitions.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""controllerPath"": { ""type"": ""string"", ""description"": ""Path to the Animator Controller asset."" }
            },
            ""required"": [""controllerPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("controllerPath").GetString();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return McpToolResult.Error($"Animator Controller not found: {path}");

            var sb = new StringBuilder();
            sb.AppendLine($"Controller: {controller.name}");
            sb.AppendLine();

            for (int layerIdx = 0; layerIdx < controller.layers.Length; layerIdx++)
            {
                var layer = controller.layers[layerIdx];
                sb.AppendLine($"── Layer [{layerIdx}]: {layer.name} (weight: {layer.defaultWeight}) ──");

                var stateMachine = layer.stateMachine;
                PrintStateMachine(sb, stateMachine, "  ", controller.layers[layerIdx]);
                sb.AppendLine();
            }

            return McpToolResult.Success(sb.ToString());
        }

        static void PrintStateMachine(StringBuilder sb, AnimatorStateMachine sm, string indent, AnimatorControllerLayer layer)
        {
            var defaultState = sm.defaultState;

            foreach (var state in sm.states)
            {
                var s = state.state;
                var isDefault = s == defaultState ? " [DEFAULT]" : "";
                var motion = s.motion != null ? s.motion.name : "(none)";
                sb.AppendLine($"{indent}{s.name}{isDefault} → motion: {motion}, speed: {s.speed}");

                foreach (var transition in s.transitions)
                {
                    var dest = transition.destinationState != null ? transition.destinationState.name : "(exit)";
                    var conditions = string.Join(" && ", transition.conditions.Select(c => $"{c.parameter} {c.mode} {c.threshold}"));
                    sb.AppendLine($"{indent}  → {dest} [{(transition.hasExitTime ? $"exit@{transition.exitTime:F2}" : "immediate")}] {conditions}");
                }
            }

            // Sub-state machines
            foreach (var sub in sm.stateMachines)
            {
                sb.AppendLine($"{indent}[SubSM] {sub.stateMachine.name}:");
                PrintStateMachine(sb, sub.stateMachine, indent + "  ", layer);
            }
        }
    }

    /// <summary>
    /// Get/set Animator parameters.
    /// </summary>
    public class AnimationGetParametersTool : IMcpTool
    {
        public string Name => "animation_get_parameters";
        public string Description => "Get all parameters of an Animator Controller (name, type, default value). Optionally set a parameter's default value.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""controllerPath"": { ""type"": ""string"", ""description"": ""Path to the Animator Controller asset."" },
                ""setParameter"": { ""type"": ""string"", ""description"": ""Optional: parameter name to modify."" },
                ""setValue"": { ""type"": ""string"", ""description"": ""Optional: new default value (for Float/Int/Bool). Required if setParameter is provided."" }
            },
            ""required"": [""controllerPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("controllerPath").GetString();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return McpToolResult.Error($"Animator Controller not found: {path}");

            // Set parameter if requested
            if (args.TryGetProperty("setParameter", out var sp))
            {
                var paramName = sp.GetString();
                var valueStr = args.TryGetProperty("setValue", out var sv) ? sv.GetString() : null;
                if (valueStr == null)
                    return McpToolResult.Error("setValue is required when setParameter is provided");

                var param = controller.parameters.FirstOrDefault(p => p.name == paramName);
                if (param == null)
                    return McpToolResult.Error($"Parameter not found: {paramName}");

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        param.defaultFloat = float.Parse(valueStr);
                        break;
                    case AnimatorControllerParameterType.Int:
                        param.defaultInt = int.Parse(valueStr);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        param.defaultBool = bool.Parse(valueStr);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        return McpToolResult.Error("Cannot set default value for Trigger parameters");
                }

                EditorUtility.SetDirty(controller);
                return McpToolResult.Success($"Set parameter '{paramName}' default = {valueStr}");
            }

            // List all parameters
            var sb = new StringBuilder();
            sb.AppendLine($"Controller: {controller.name}");
            sb.AppendLine($"Parameters ({controller.parameters.Length}):");
            sb.AppendLine();

            foreach (var param in controller.parameters)
            {
                var defaultVal = param.type switch
                {
                    AnimatorControllerParameterType.Float => param.defaultFloat.ToString("F2"),
                    AnimatorControllerParameterType.Int => param.defaultInt.ToString(),
                    AnimatorControllerParameterType.Bool => param.defaultBool.ToString(),
                    AnimatorControllerParameterType.Trigger => "(trigger)",
                    _ => "?"
                };
                sb.AppendLine($"  {param.name} ({param.type}): {defaultVal}");
            }

            return McpToolResult.Success(sb.ToString());
        }
    }
}
