using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace UnityAgentClient
{
    /// <summary>
    /// Get a specialized hierarchy view of UI Canvas elements.
    /// </summary>
    public class UIGetCanvasHierarchyTool : IMcpTool
    {
        public string Name => "ui_get_canvas_hierarchy";
        public string Description => "Get a specialized view of all Canvas objects and their UI element hierarchy (RectTransform, anchors, size).";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var canvases = SceneToolHelpers.FindAllIncludeInactive<Canvas>();
            if (canvases.Length == 0)
                return McpToolResult.Success("No Canvas objects found in scene");

            var sb = new StringBuilder();
            sb.AppendLine($"Found {canvases.Length} Canvas(es):");
            sb.AppendLine();

            foreach (var canvas in canvases)
            {
                sb.AppendLine($"── Canvas: {canvas.name} ──");
                sb.AppendLine($"  Render Mode: {canvas.renderMode}");
                sb.AppendLine($"  Sort Order: {canvas.sortingOrder}");

                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                    sb.AppendLine($"  Scale Mode: {scaler.uiScaleMode}, Ref Resolution: {scaler.referenceResolution}");

                sb.AppendLine();
                PrintUIElement(sb, canvas.transform, 1);
                sb.AppendLine();
            }

            return McpToolResult.Success(sb.ToString());
        }

        static void PrintUIElement(StringBuilder sb, Transform t, int depth)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                var rt = child.GetComponent<RectTransform>();
                var indent = new string(' ', depth * 2);
                var active = child.gameObject.activeSelf ? "" : " [inactive]";

                // Collect UI component types
                var uiComps = child.GetComponents<Component>()
                    .Where(c => c != null && !(c is Transform) && !(c is RectTransform) && !(c is CanvasRenderer))
                    .Select(c => c.GetType().Name);
                var compStr = string.Join(", ", uiComps);

                if (rt != null)
                {
                    sb.AppendLine($"{indent}{child.name}{active} [{compStr}]");
                    sb.AppendLine($"{indent}  size: {rt.sizeDelta}, anchors: ({rt.anchorMin})-({rt.anchorMax}), pivot: {rt.pivot}");
                }
                else
                {
                    sb.AppendLine($"{indent}{child.name}{active} [{compStr}] (no RectTransform)");
                }

                PrintUIElement(sb, child, depth + 1);
            }
        }
    }

    /// <summary>
    /// Modify a RectTransform's properties.
    /// </summary>
    public class UIModifyRectTransformTool : IMcpTool
    {
        public string Name => "ui_modify_rect_transform";
        public string Description => "Modify a UI element's RectTransform properties (size, anchors, pivot, anchored position).";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the UI GameObject."" },
                ""sizeDelta"": { ""type"": ""string"", ""description"": ""Size as 'width,height' (e.g. '200,50')."" },
                ""anchoredPosition"": { ""type"": ""string"", ""description"": ""Position as 'x,y'."" },
                ""anchorMin"": { ""type"": ""string"", ""description"": ""Anchor min as 'x,y' (e.g. '0,0')."" },
                ""anchorMax"": { ""type"": ""string"", ""description"": ""Anchor max as 'x,y' (e.g. '1,1')."" },
                ""pivot"": { ""type"": ""string"", ""description"": ""Pivot as 'x,y' (e.g. '0.5,0.5')."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("gameObjectPath").GetString();
            var go = GameObject.Find(path);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(path);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {path}");

            var rt = go.GetComponent<RectTransform>();
            if (rt == null)
                return McpToolResult.Error($"No RectTransform on: {path}");

            Undo.RecordObject(rt, $"Modify RectTransform {go.name}");
            var changes = new StringBuilder();

            if (args.TryGetProperty("sizeDelta", out var sd))
            {
                var v = sd.GetString().Split(',').Select(float.Parse).ToArray();
                rt.sizeDelta = new Vector2(v[0], v[1]);
                changes.AppendLine($"  sizeDelta = {rt.sizeDelta}");
            }

            if (args.TryGetProperty("anchoredPosition", out var ap))
            {
                var v = ap.GetString().Split(',').Select(float.Parse).ToArray();
                rt.anchoredPosition = new Vector2(v[0], v[1]);
                changes.AppendLine($"  anchoredPosition = {rt.anchoredPosition}");
            }

            if (args.TryGetProperty("anchorMin", out var amin))
            {
                var v = amin.GetString().Split(',').Select(float.Parse).ToArray();
                rt.anchorMin = new Vector2(v[0], v[1]);
                changes.AppendLine($"  anchorMin = {rt.anchorMin}");
            }

            if (args.TryGetProperty("anchorMax", out var amax))
            {
                var v = amax.GetString().Split(',').Select(float.Parse).ToArray();
                rt.anchorMax = new Vector2(v[0], v[1]);
                changes.AppendLine($"  anchorMax = {rt.anchorMax}");
            }

            if (args.TryGetProperty("pivot", out var pv))
            {
                var v = pv.GetString().Split(',').Select(float.Parse).ToArray();
                rt.pivot = new Vector2(v[0], v[1]);
                changes.AppendLine($"  pivot = {rt.pivot}");
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            return McpToolResult.Success($"Modified RectTransform '{go.name}':\n{changes}");
        }
    }

    /// <summary>
    /// Set UI Text content.
    /// </summary>
    public class UISetTextTool : IMcpTool
    {
        public string Name => "ui_set_text";
        public string Description => "Set the text content of a UI Text or TextMeshPro component.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the UI text GameObject."" },
                ""text"": { ""type"": ""string"", ""description"": ""New text content."" },
                ""fontSize"": { ""type"": ""number"", ""description"": ""Optional: set font size."" },
                ""color"": { ""type"": ""string"", ""description"": ""Optional: text color as 'r,g,b,a'."" }
            },
            ""required"": [""gameObjectPath"", ""text""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var path = args.GetProperty("gameObjectPath").GetString();
            var text = args.GetProperty("text").GetString();

            var go = GameObject.Find(path);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(path);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {path}");

            // Try legacy Text component
            var uiText = go.GetComponent<Text>();
            if (uiText != null)
            {
                Undo.RecordObject(uiText, "Set UI Text");
                uiText.text = text;

                if (args.TryGetProperty("fontSize", out var fs) && fs.TryGetInt32(out var size))
                    uiText.fontSize = size;

                if (args.TryGetProperty("color", out var c))
                {
                    var v = c.GetString().Split(',').Select(float.Parse).ToArray();
                    uiText.color = new Color(v[0], v[1], v.Length > 2 ? v[2] : 0, v.Length > 3 ? v[3] : 1);
                }

                EditorSceneManager.MarkSceneDirty(go.scene);
                return McpToolResult.Success($"Set Text on '{go.name}' = \"{text}\"");
            }

            // Try TextMeshPro via SerializedObject (to avoid hard dependency on TMP package)
            var tmpComponent = go.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().Name.Contains("TextMeshPro"));

            if (tmpComponent != null)
            {
                var so = new SerializedObject(tmpComponent);
                var textProp = so.FindProperty("m_text");
                if (textProp != null)
                {
                    textProp.stringValue = text;

                    if (args.TryGetProperty("fontSize", out var fs) && fs.TryGetSingle(out var size))
                    {
                        var sizeProp = so.FindProperty("m_fontSize");
                        if (sizeProp != null) sizeProp.floatValue = size;
                    }

                    if (args.TryGetProperty("color", out var c))
                    {
                        var colorProp = so.FindProperty("m_fontColor");
                        if (colorProp != null)
                        {
                            var v = c.GetString().Split(',').Select(float.Parse).ToArray();
                            colorProp.colorValue = new Color(v[0], v[1], v.Length > 2 ? v[2] : 0, v.Length > 3 ? v[3] : 1);
                        }
                    }

                    so.ApplyModifiedProperties();
                    EditorSceneManager.MarkSceneDirty(go.scene);
                    return McpToolResult.Success($"Set TextMeshPro on '{go.name}' = \"{text}\"");
                }
            }

            return McpToolResult.Error($"No Text or TextMeshPro component found on '{path}'");
        }
    }
}
