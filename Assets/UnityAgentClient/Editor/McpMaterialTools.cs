using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace UnityAgentClient
{
    /// <summary>
    /// Get all properties of a material.
    /// </summary>
    public class MaterialGetPropertiesTool : IMcpTool
    {
        public string Name => "material_get_properties";
        public string Description => "Get all shader properties of a material asset, including colors, floats, textures, and keywords.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""materialPath"": { ""type"": ""string"", ""description"": ""Path to the material asset (e.g. 'Assets/Materials/Wood.mat')."" }
            },
            ""required"": [""materialPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var matPath = args.GetProperty("materialPath").GetString();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
                return McpToolResult.Error($"Material not found: {matPath}");

            var shader = mat.shader;
            var sb = new StringBuilder();
            sb.AppendLine($"Material: {mat.name}");
            sb.AppendLine($"Shader: {shader.name}");
            sb.AppendLine($"Render Queue: {mat.renderQueue}");
            sb.AppendLine();

            var propCount = ShaderUtil.GetPropertyCount(shader);
            sb.AppendLine($"Properties ({propCount}):");

            for (int i = 0; i < propCount; i++)
            {
                var propName = ShaderUtil.GetPropertyName(shader, i);
                var propType = ShaderUtil.GetPropertyType(shader, i);
                var desc = ShaderUtil.GetPropertyDescription(shader, i);

                string value = propType switch
                {
                    ShaderUtil.ShaderPropertyType.Color => mat.GetColor(propName).ToString(),
                    ShaderUtil.ShaderPropertyType.Float => mat.GetFloat(propName).ToString("F4"),
                    ShaderUtil.ShaderPropertyType.Range => mat.GetFloat(propName).ToString("F4"),
                    ShaderUtil.ShaderPropertyType.Vector => mat.GetVector(propName).ToString(),
                    ShaderUtil.ShaderPropertyType.TexEnv =>
                        mat.GetTexture(propName) != null
                            ? $"{mat.GetTexture(propName).name} ({AssetDatabase.GetAssetPath(mat.GetTexture(propName))})"
                            : "null",
                    _ => "(unknown)"
                };

                sb.AppendLine($"  {desc} [{propName}] ({propType}): {value}");
            }

            // Shader keywords
            var keywords = mat.shaderKeywords;
            if (keywords.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Keywords: {string.Join(", ", keywords)}");
            }

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Modify a material shader property.
    /// </summary>
    public class MaterialSetPropertyTool : IMcpTool
    {
        public string Name => "material_set_property";
        public string Description => "Set a shader property on a material. Supports Color ('1,0,0,1'), Float, Vector4 ('x,y,z,w'), and Texture (asset path).";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""materialPath"": { ""type"": ""string"", ""description"": ""Path to the material asset."" },
                ""propertyName"": { ""type"": ""string"", ""description"": ""Shader property name (e.g. '_Color', '_MainTex', '_Metallic')."" },
                ""value"": { ""type"": ""string"", ""description"": ""Value to set. Color: '1,0,0,1'. Float: '0.5'. Texture: asset path."" }
            },
            ""required"": [""materialPath"", ""propertyName"", ""value""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var matPath = args.GetProperty("materialPath").GetString();
            var propName = args.GetProperty("propertyName").GetString();
            var valueStr = args.GetProperty("value").GetString();

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
                return McpToolResult.Error($"Material not found: {matPath}");

            if (!mat.HasProperty(propName))
                return McpToolResult.Error($"Property '{propName}' not found on material");

            var shader = mat.shader;
            int propIdx = -1;
            for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
            {
                if (ShaderUtil.GetPropertyName(shader, i) == propName)
                {
                    propIdx = i;
                    break;
                }
            }

            if (propIdx < 0)
                return McpToolResult.Error($"Property '{propName}' not found in shader");

            Undo.RecordObject(mat, $"Set {propName}");

            var propType = ShaderUtil.GetPropertyType(shader, propIdx);
            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    var c = valueStr.Split(',').Select(float.Parse).ToArray();
                    mat.SetColor(propName, new Color(c[0], c[1], c.Length > 2 ? c[2] : 0, c.Length > 3 ? c[3] : 1));
                    break;

                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    mat.SetFloat(propName, float.Parse(valueStr));
                    break;

                case ShaderUtil.ShaderPropertyType.Vector:
                    var v = valueStr.Split(',').Select(float.Parse).ToArray();
                    mat.SetVector(propName, new Vector4(v[0], v.Length > 1 ? v[1] : 0, v.Length > 2 ? v[2] : 0, v.Length > 3 ? v[3] : 0));
                    break;

                case ShaderUtil.ShaderPropertyType.TexEnv:
                    if (string.IsNullOrEmpty(valueStr) || valueStr == "null")
                    {
                        mat.SetTexture(propName, null);
                    }
                    else
                    {
                        var tex = AssetDatabase.LoadAssetAtPath<Texture>(valueStr);
                        if (tex == null)
                            return McpToolResult.Error($"Texture not found: {valueStr}");
                        mat.SetTexture(propName, tex);
                    }
                    break;

                default:
                    return McpToolResult.Error($"Unsupported property type: {propType}");
            }

            EditorUtility.SetDirty(mat);
            return McpToolResult.Success($"Set {matPath} [{propName}] = {valueStr}");
        }
    }

    /// <summary>
    /// Get rendering/quality settings.
    /// </summary>
    public class RenderingGetSettingsTool : IMcpTool
    {
        public string Name => "rendering_get_settings";
        public string Description => "Get current rendering and quality settings including quality levels, shadows, and render pipeline info.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var sb = new StringBuilder();

            sb.AppendLine("== Quality Settings ==");
            sb.AppendLine($"Current Level: {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            sb.AppendLine($"All Levels: {string.Join(", ", QualitySettings.names)}");
            sb.AppendLine($"VSync: {QualitySettings.vSyncCount}");
            sb.AppendLine($"Anti-Aliasing: {QualitySettings.antiAliasing}x");
            sb.AppendLine($"Shadow Quality: {QualitySettings.shadows}");
            sb.AppendLine($"Shadow Resolution: {QualitySettings.shadowResolution}");
            sb.AppendLine($"Shadow Distance: {QualitySettings.shadowDistance}");
            sb.AppendLine($"Texture Quality: {QualitySettings.globalTextureMipmapLimit}");
            sb.AppendLine();

            sb.AppendLine("== Render Pipeline ==");
            var srp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (srp != null)
            {
                sb.AppendLine($"Pipeline: {srp.GetType().Name}");
                sb.AppendLine($"Asset: {srp.name} ({AssetDatabase.GetAssetPath(srp)})");
            }
            else
            {
                sb.AppendLine("Pipeline: Built-in Render Pipeline");
            }
            sb.AppendLine();

            sb.AppendLine("== Player Graphics ==");
            sb.AppendLine($"Color Space: {PlayerSettings.colorSpace}");

            return McpToolResult.Success(sb.ToString());
        }
    }

    /// <summary>
    /// Assign a material to a GameObject's Renderer.
    /// </summary>
    public class MaterialAssignTool : IMcpTool
    {
        public string Name => "material_assign";
        public string Description => "Assign a material to a GameObject's Renderer component. Can also create a simple colored material on the fly.";
        public bool RequiresMainThread => true;

        public JsonElement InputSchema => JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""gameObjectPath"": { ""type"": ""string"", ""description"": ""Path or name of the target GameObject."" },
                ""materialPath"": { ""type"": ""string"", ""description"": ""Path to an existing material asset (e.g. 'Assets/Materials/Red.mat')."" },
                ""color"": { ""type"": ""string"", ""description"": ""Alternative: create and assign a material with this color as 'r,g,b,a' (e.g. '0,0,1,1' for blue). No materialPath needed."" },
                ""materialIndex"": { ""type"": ""number"", ""description"": ""Which material slot to assign to (default: 0, the first/main material)."" }
            },
            ""required"": [""gameObjectPath""]
        }").RootElement;

        public McpToolResult Execute(JsonElement args)
        {
            var goPath = args.GetProperty("gameObjectPath").GetString();

            var go = GameObject.Find(goPath);
            if (go == null)
                go = SceneToolHelpers.FindGameObjectIncludeInactive(goPath);
            if (go == null)
                return McpToolResult.Error($"GameObject not found: {goPath}");

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return McpToolResult.Error($"No Renderer component on: {goPath}");

            int matIndex = 0;
            if (args.TryGetProperty("materialIndex", out var mi) && mi.TryGetInt32(out var idx))
                matIndex = idx;

            Material mat = null;

            // Option 1: Use existing material
            if (args.TryGetProperty("materialPath", out var mp))
            {
                mat = AssetDatabase.LoadAssetAtPath<Material>(mp.GetString());
                if (mat == null)
                    return McpToolResult.Error($"Material not found: {mp.GetString()}");
            }
            // Option 2: Create material from color
            else if (args.TryGetProperty("color", out var colorProp))
            {
                var parts = colorProp.GetString().Split(',').Select(float.Parse).ToArray();
                var color = new Color(parts[0], parts[1],
                    parts.Length > 2 ? parts[2] : 0,
                    parts.Length > 3 ? parts[3] : 1);

                // Find the default shader
                var srp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                Shader shader;
                if (srp != null)
                    shader = srp.defaultShader ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                else
                    shader = Shader.Find("Standard");

                mat = new Material(shader);
                mat.color = color;

                // Save as asset
                var safeName = go.name.Replace(" ", "_").ToLowerInvariant();
                var savePath = $"Assets/Generated/Materials/{safeName}_mat.mat";
                var dir = System.IO.Path.GetDirectoryName(savePath);
                if (!AssetDatabase.IsValidFolder("Assets/Generated"))
                    AssetDatabase.CreateFolder("Assets", "Generated");
                if (!AssetDatabase.IsValidFolder("Assets/Generated/Materials"))
                    AssetDatabase.CreateFolder("Assets/Generated", "Materials");

                AssetDatabase.CreateAsset(mat, savePath);
                AssetDatabase.Refresh();
                mat = AssetDatabase.LoadAssetAtPath<Material>(savePath);
            }
            else
            {
                return McpToolResult.Error("Provide 'materialPath' or 'color'.");
            }

            // Assign to renderer
            Undo.RecordObject(renderer, $"Assign material to {go.name}");
            var mats = renderer.sharedMaterials;
            if (matIndex >= mats.Length)
            {
                var newMats = new Material[matIndex + 1];
                mats.CopyTo(newMats, 0);
                newMats[matIndex] = mat;
                renderer.sharedMaterials = newMats;
            }
            else
            {
                mats[matIndex] = mat;
                renderer.sharedMaterials = mats;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            return McpToolResult.Success(
                $"Assigned material '{mat.name}' to '{go.name}' (slot {matIndex})");
        }
    }
}
