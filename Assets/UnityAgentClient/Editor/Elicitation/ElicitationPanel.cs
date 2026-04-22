using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace UnityAgentClient.Elicitation
{
    /// <summary>
    /// Full form builder (Steps 2+3). Maps ACP restricted JSON Schema to UI Toolkit controls:
    ///   - boolean       → Toggle
    ///   - integer       → IntegerField (or SliderInt when min+max present)
    ///   - number        → FloatField  (or Slider when min+max present)
    ///   - string        → TextField   (multiline when format=multiline)
    ///   - enum/oneOf    → PopupField
    ///   - array+items.enum → multi-select Toggles
    ///   - format:email/uri → TextField with validation
    ///   - format:unity-object → ObjectField (asset path)
    ///   - format:unity-scene-object → ObjectField (hierarchy path, scene only)
    ///   - format:unity-vector3 → Vector3Field ("x,y,z")
    ///   - format:unity-color → ColorField ("#RRGGBBAA")
    /// See docs/ELICITATION.md §5-6 for full mapping table.
    /// </summary>
    internal static class ElicitationPanel
    {
        public static VisualElement Build(
            ElicitationRequest request,
            Action<ElicitationResponse> onAction)
        {
            var panel = new VisualElement();
            panel.AddToClassList("elicitation-panel");

            var header = new Label("Agent needs input");
            header.AddToClassList("bold-label");
            panel.Add(header);

            if (!string.IsNullOrEmpty(request.Message))
            {
                var msg = new Label(request.Message);
                msg.AddToClassList("elicitation-message");
                msg.style.whiteSpace = WhiteSpace.Normal;
                panel.Add(msg);
            }

            if (request.Mode == "url")
            {
                BuildUrlMode(panel, request, onAction);
                return panel;
            }

            var fields = new List<FormField>();
            if (request.RequestedSchema is JsonElement schema
                && schema.ValueKind == JsonValueKind.Object
                && schema.TryGetProperty("properties", out var propsEl)
                && propsEl.ValueKind == JsonValueKind.Object)
            {
                var required = new HashSet<string>();
                if (schema.TryGetProperty("required", out var reqEl)
                    && reqEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in reqEl.EnumerateArray())
                    {
                        if (r.ValueKind == JsonValueKind.String) required.Add(r.GetString());
                    }
                }

                foreach (var prop in propsEl.EnumerateObject())
                {
                    var field = BuildField(prop.Name, prop.Value, required.Contains(prop.Name));
                    if (field != null)
                    {
                        fields.Add(field);
                        panel.Add(field.Container);
                    }
                }
            }

            // Form-level summary error (only shown when there are field errors)
            var summaryError = new Label();
            summaryError.AddToClassList("elicitation-error");
            summaryError.style.display = DisplayStyle.None;
            summaryError.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(summaryError);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("elicitation-buttons");

            var submitBtn = new Button(() =>
            {
                if (!TryCollect(fields, out var content, summaryError))
                    return;
                onAction?.Invoke(ElicitationResponse.Accept(content));
            }) { text = "Submit" };
            submitBtn.AddToClassList("elicitation-submit");
            buttonRow.Add(submitBtn);

            buttonRow.Add(new Button(() => onAction?.Invoke(ElicitationResponse.Decline())) { text = "Decline" });
            buttonRow.Add(new Button(() => onAction?.Invoke(ElicitationResponse.Cancel())) { text = "Cancel" });

            panel.Add(buttonRow);

            panel.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Escape)
                {
                    onAction?.Invoke(ElicitationResponse.Cancel());
                    evt.StopPropagation();
                }
            });

            return panel;
        }

        static void BuildUrlMode(VisualElement panel, ElicitationRequest request, Action<ElicitationResponse> onAction)
        {
            var urlLabel = new Label(request.Url ?? "<no url>");
            urlLabel.AddToClassList("elicitation-url");
            urlLabel.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(urlLabel);

            var warn = new Label("Opening this URL will leave the editor. Continue?");
            warn.AddToClassList("elicitation-warning");
            panel.Add(warn);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("elicitation-buttons");

            var openBtn = new Button(() =>
            {
                if (!string.IsNullOrEmpty(request.Url))
                    UnityEngine.Application.OpenURL(request.Url);
                onAction?.Invoke(ElicitationResponse.Accept(
                    JsonSerializer.SerializeToElement(new { })));
            }) { text = "Open & Accept" };
            openBtn.AddToClassList("elicitation-submit");
            buttonRow.Add(openBtn);

            buttonRow.Add(new Button(() => onAction?.Invoke(ElicitationResponse.Decline())) { text = "Decline" });
            buttonRow.Add(new Button(() => onAction?.Invoke(ElicitationResponse.Cancel())) { text = "Cancel" });
            panel.Add(buttonRow);
        }

        // ── Field model ──

        sealed class FormField
        {
            public string Name;
            public bool Required;
            public VisualElement Container;
            public Label ErrorLabel;
            /// Returns (hasValue, value, error). hasValue=false means "omit from result".
            public Func<(bool hasValue, JsonElement value, string error)> Collect;
        }

        // ── Dispatch ──
        // Priority: array+items.enum → enum/oneOf/anyOf → numeric range → format → primitive fallback

        static FormField BuildField(string name, JsonElement spec, bool required)
        {
            if (spec.ValueKind != JsonValueKind.Object) return null;

            string title = name;
            if (spec.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
                title = titleEl.GetString();

            string description = null;
            if (spec.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
                description = descEl.GetString();

            string type = "string";
            if (spec.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                type = typeEl.GetString();

            string format = null;
            if (spec.TryGetProperty("format", out var fmtEl) && fmtEl.ValueKind == JsonValueKind.String)
                format = fmtEl.GetString();

            // 1. array + items.enum → multi-select toggles
            if (type == "array" && spec.TryGetProperty("items", out var itemsEl))
            {
                var enumVals = ExtractEnum(itemsEl, out var enumLabels);
                if (enumVals != null)
                    return BuildMultiSelectField(name, title, description, required, enumVals, enumLabels);
            }

            // 2. scalar enum / oneOf / anyOf → PopupField
            var scalarEnum = ExtractEnum(spec, out var scalarLabels);
            if (scalarEnum != null)
                return BuildEnumField(name, title, description, required, spec, scalarEnum, scalarLabels);

            // 3. numeric with both bounds → Slider
            if ((type == "integer" || type == "number") && HasValidRange(spec, type, out var min, out var max))
                return BuildSliderField(name, title, description, required, spec, type, min, max);

            // 4. Unity-native format extensions (Step 3)
            if (type == "string" && format != null)
            {
                switch (format)
                {
                    case "unity-object":
                        return BuildObjectField(name, title, description, required, spec, sceneOnly: false);
                    case "unity-scene-object":
                        return BuildObjectField(name, title, description, required, spec, sceneOnly: true);
                    case "unity-vector3":
                        return BuildVector3Field(name, title, description, required, spec);
                    case "unity-color":
                        return BuildColorField(name, title, description, required, spec);
                    case "multiline":
                        return BuildMultilineField(name, title, description, required, spec);
                }
            }

            // 5. primitive fallback
            return type switch
            {
                "boolean" => BuildBooleanField(name, title, description, required, spec),
                "integer" => BuildIntegerField(name, title, description, required, spec),
                "number"  => BuildNumberField(name, title, description, required, spec),
                _         => BuildStringField(name, title, description, required, spec, format),
            };
        }

        // ── Container helper ──

        static (VisualElement container, Label errorLabel) MakeFieldContainer(string title, string description, bool required)
        {
            var container = new VisualElement();
            container.AddToClassList("elicitation-field");

            var labelText = required ? $"{title} *" : title;
            var label = new Label(labelText);
            label.AddToClassList("elicitation-field-label");
            container.Add(label);

            if (!string.IsNullOrEmpty(description))
            {
                var desc = new Label(description);
                desc.AddToClassList("elicitation-field-desc");
                desc.style.whiteSpace = WhiteSpace.Normal;
                container.Add(desc);
            }

            var errorLabel = new Label();
            errorLabel.AddToClassList("elicitation-field-error");
            errorLabel.style.display = DisplayStyle.None;
            return (container, errorLabel);
        }

        // ── Boolean ──

        static FormField BuildBooleanField(string name, string title, string desc, bool required, JsonElement spec)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            bool def = false;
            if (spec.TryGetProperty("default", out var defEl) && defEl.ValueKind == JsonValueKind.True)
                def = true;

            var toggle = new Toggle { value = def };
            toggle.AddToClassList("elicitation-toggle");
            container.Add(toggle);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () => (true, JsonSerializer.SerializeToElement(toggle.value), null)
            };
        }

        // ── Integer ──

        static FormField BuildIntegerField(string name, string title, string desc, bool required, JsonElement spec)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            int def = 0;
            bool hasDef = TryGetInt(spec, "default", out def);
            TryGetInt(spec, "minimum", out var minVal);
            bool hasMin = spec.TryGetProperty("minimum", out _);
            TryGetInt(spec, "maximum", out var maxVal);
            bool hasMax = spec.TryGetProperty("maximum", out _);

            var field = new IntegerField { value = hasDef ? def : 0 };
            field.AddToClassList("elicitation-intfield");
            container.Add(field);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () =>
                {
                    int v = field.value;
                    if (hasMin && v < minVal) return (false, default, $"Minimum is {minVal}");
                    if (hasMax && v > maxVal) return (false, default, $"Maximum is {maxVal}");
                    return (true, JsonSerializer.SerializeToElement(v), null);
                }
            };
        }

        // ── Number (float/double) ──

        static FormField BuildNumberField(string name, string title, string desc, bool required, JsonElement spec)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            float def = 0f;
            bool hasDef = TryGetFloat(spec, "default", out def);
            TryGetFloat(spec, "minimum", out var minVal);
            bool hasMin = spec.TryGetProperty("minimum", out _);
            TryGetFloat(spec, "maximum", out var maxVal);
            bool hasMax = spec.TryGetProperty("maximum", out _);

            var field = new FloatField { value = hasDef ? def : 0f };
            field.AddToClassList("elicitation-floatfield");
            container.Add(field);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () =>
                {
                    float v = field.value;
                    if (hasMin && v < minVal) return (false, default, $"Minimum is {minVal}");
                    if (hasMax && v > maxVal) return (false, default, $"Maximum is {maxVal}");
                    return (true, JsonSerializer.SerializeToElement((double)v), null);
                }
            };
        }

        // ── Slider (integer or float with valid min+max) ──

        static FormField BuildSliderField(string name, string title, string desc, bool required, JsonElement spec,
            string type, float min, float max)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            if (type == "integer")
            {
                int iMin = (int)min, iMax = (int)max;
                int iDef = iMin;
                if (TryGetInt(spec, "default", out var d))
                    iDef = Math.Clamp(d, iMin, iMax);

                var slider = new SliderInt(iMin, iMax) { value = iDef, showInputField = true };
                slider.AddToClassList("elicitation-slider");
                container.Add(slider);
                container.Add(errLabel);

                return new FormField
                {
                    Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                    Collect = () => (true, JsonSerializer.SerializeToElement(slider.value), null)
                };
            }
            else
            {
                float fDef = min;
                if (TryGetFloat(spec, "default", out var d))
                    fDef = Math.Clamp(d, min, max);

                var slider = new Slider(min, max) { value = fDef, showInputField = true };
                slider.AddToClassList("elicitation-slider");
                container.Add(slider);
                container.Add(errLabel);

                return new FormField
                {
                    Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                    Collect = () => (true, JsonSerializer.SerializeToElement((double)slider.value), null)
                };
            }
        }

        // ── Enum / oneOf / anyOf dropdown ──

        static FormField BuildEnumField(string name, string title, string desc, bool required, JsonElement spec,
            List<string> values, List<string> labels)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            string def = TryGetStringDefault(spec);
            int defIdx = def != null ? values.IndexOf(def) : -1;
            if (defIdx < 0) defIdx = 0;

            var popup = new PopupField<string>(labels, defIdx);
            popup.AddToClassList("elicitation-popup");
            container.Add(popup);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () =>
                {
                    int idx = popup.index;
                    if (idx < 0 || idx >= values.Count)
                    {
                        if (required) return (false, default, $"Select a value for '{name}'");
                        return (false, default, null); // omit
                    }
                    return (true, JsonSerializer.SerializeToElement(values[idx]), null);
                }
            };
        }

        // ── Multi-select (array + items.enum) ──

        static FormField BuildMultiSelectField(string name, string title, string desc, bool required,
            List<string> values, List<string> labels)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            var toggles = new List<(Toggle toggle, string value)>();
            var group = new VisualElement();
            group.AddToClassList("elicitation-multiselect");

            for (int i = 0; i < values.Count; i++)
            {
                var t = new Toggle(labels[i]) { value = false };
                t.AddToClassList("elicitation-toggle");
                group.Add(t);
                toggles.Add((t, values[i]));
            }
            container.Add(group);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () =>
                {
                    var selected = new List<string>();
                    foreach (var (toggle, val) in toggles)
                    {
                        if (toggle.value) selected.Add(val);
                    }
                    if (required && selected.Count == 0)
                        return (false, default, $"Select at least one option for '{name}'");
                    if (!required && selected.Count == 0)
                        return (false, default, null); // omit
                    return (true, JsonSerializer.SerializeToElement(selected), null);
                }
            };
        }

        // ── String (plain) ──

        static FormField BuildStringField(string name, string title, string desc, bool required, JsonElement spec, string format)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            string def = TryGetStringDefault(spec);
            var text = new TextField { value = def ?? "" };
            text.AddToClassList("elicitation-textfield");

            if (!string.IsNullOrEmpty(format))
            {
                var hint = new Label($"Format: {format}");
                hint.AddToClassList("elicitation-field-desc");
                container.Add(hint);
            }

            container.Add(text);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () =>
                {
                    var raw = text.value ?? "";
                    if (required && string.IsNullOrWhiteSpace(raw))
                        return (false, default, $"'{name}' is required");
                    if (!required && string.IsNullOrEmpty(raw))
                        return (false, default, null); // omit

                    if (format == "email" && !IsValidEmail(raw))
                        return (false, default, "Invalid email address");
                    if (format == "uri" && !Uri.IsWellFormedUriString(raw, UriKind.Absolute))
                        return (false, default, "Invalid URI");

                    return (true, JsonSerializer.SerializeToElement(raw), null);
                }
            };
        }

        // ── String (multiline) ──

        static FormField BuildMultilineField(string name, string title, string desc, bool required, JsonElement spec)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            string def = TryGetStringDefault(spec);
            var text = new TextField { value = def ?? "", multiline = true };
            text.AddToClassList("elicitation-textfield");
            text.AddToClassList("elicitation-multiline");
            text.style.minHeight = 60;
            container.Add(text);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () =>
                {
                    var raw = text.value ?? "";
                    if (required && string.IsNullOrWhiteSpace(raw))
                        return (false, default, $"'{name}' is required");
                    if (!required && string.IsNullOrEmpty(raw))
                        return (false, default, null);
                    return (true, JsonSerializer.SerializeToElement(raw), null);
                }
            };
        }

        // ── Unity-native formats (Step 3) ──

        static FormField BuildObjectField(string name, string title, string desc, bool required, JsonElement spec, bool sceneOnly)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            var objField = new ObjectField
            {
                objectType = sceneOnly ? typeof(GameObject) : typeof(UnityEngine.Object),
                allowSceneObjects = sceneOnly,
            };
            objField.AddToClassList("elicitation-objectfield");

            // Parse default from asset path
            string defPath = TryGetStringDefault(spec);
            if (!string.IsNullOrEmpty(defPath) && !sceneOnly)
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(defPath);
                if (asset != null) objField.value = asset;
            }

            container.Add(objField);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () =>
                {
                    var obj = objField.value;
                    if (obj == null)
                    {
                        if (required) return (false, default, $"'{name}' is required");
                        return (false, default, null); // omit
                    }

                    string value;
                    if (sceneOnly)
                    {
                        // Scene objects → full hierarchy path
                        value = GetHierarchyPath(obj);
                    }
                    else
                    {
                        // Project assets → asset path
                        value = UnityEditor.AssetDatabase.GetAssetPath(obj);
                        if (string.IsNullOrEmpty(value))
                            value = obj.name;
                    }
                    return (true, JsonSerializer.SerializeToElement(value), null);
                }
            };
        }

        static string GetHierarchyPath(UnityEngine.Object obj)
        {
            if (obj is GameObject go)
                return GetGameObjectPath(go.transform);
            if (obj is Component comp)
                return GetGameObjectPath(comp.transform);
            return obj.name;
        }

        static string GetGameObjectPath(Transform t)
        {
            var path = "/" + t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = "/" + t.name + path;
            }
            return path;
        }

        static FormField BuildVector3Field(string name, string title, string desc, bool required, JsonElement spec)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            var def = Vector3.zero;
            string defStr = TryGetStringDefault(spec);
            if (!string.IsNullOrEmpty(defStr))
                TryParseVector3(defStr, out def);

            var field = new Vector3Field { value = def };
            field.AddToClassList("elicitation-vector3field");
            container.Add(field);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () =>
                {
                    var v = field.value;
                    var str = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", v.x, v.y, v.z);
                    return (true, JsonSerializer.SerializeToElement(str), null);
                }
            };
        }

        static bool TryParseVector3(string s, out Vector3 result)
        {
            result = Vector3.zero;
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(',');
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result.x)) return false;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result.y)) return false;
            if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result.z)) return false;
            return true;
        }

        static FormField BuildColorField(string name, string title, string desc, bool required, JsonElement spec)
        {
            var (container, errLabel) = MakeFieldContainer(title, desc, required);

            var def = Color.white;
            string defStr = TryGetStringDefault(spec);
            if (!string.IsNullOrEmpty(defStr))
                ColorUtility.TryParseHtmlString(defStr, out def);

            var field = new ColorField { value = def, showAlpha = true };
            field.AddToClassList("elicitation-colorfield");
            container.Add(field);
            container.Add(errLabel);

            return new FormField
            {
                Name = name, Required = required, Container = container, ErrorLabel = errLabel,
                Collect = () =>
                {
                    // ColorUtility.ToHtmlStringRGBA returns "RRGGBBAA" without "#"
                    var str = "#" + ColorUtility.ToHtmlStringRGBA(field.value);
                    return (true, JsonSerializer.SerializeToElement(str), null);
                }
            };
        }

        // ── Helpers ──

        static List<string> ExtractEnum(JsonElement spec, out List<string> labels)
        {
            labels = null;

            if (spec.TryGetProperty("enum", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array)
            {
                var vals = new List<string>();
                var lbls = new List<string>();
                foreach (var e in enumEl.EnumerateArray())
                {
                    var s = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
                    vals.Add(s);
                    lbls.Add(s);
                }
                if (vals.Count > 0) { labels = lbls; return vals; }
            }

            foreach (var key in new[] { "oneOf", "anyOf" })
            {
                if (!spec.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
                    continue;

                var vals = new List<string>();
                var lbls = new List<string>();
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!item.TryGetProperty("const", out var constEl)) continue;
                    var v = constEl.ValueKind == JsonValueKind.String ? constEl.GetString() : constEl.GetRawText();
                    string lab = v;
                    if (item.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                        lab = tEl.GetString();
                    vals.Add(v);
                    lbls.Add(lab);
                }
                if (vals.Count > 0) { labels = lbls; return vals; }
            }

            return null;
        }

        static bool HasValidRange(JsonElement spec, string type, out float min, out float max)
        {
            min = max = 0;
            if (!TryGetFloat(spec, "minimum", out min)) return false;
            if (!TryGetFloat(spec, "maximum", out max)) return false;
            if (min >= max) return false;
            if (type == "integer" && (max - min) > 10000) return false;
            return true;
        }

        static string TryGetStringDefault(JsonElement spec)
        {
            if (!spec.TryGetProperty("default", out var defEl)) return null;
            return defEl.ValueKind == JsonValueKind.String ? defEl.GetString() : defEl.GetRawText();
        }

        static bool TryGetInt(JsonElement spec, string prop, out int result)
        {
            result = 0;
            if (!spec.TryGetProperty(prop, out var el)) return false;
            if (el.ValueKind == JsonValueKind.Number) { result = el.GetInt32(); return true; }
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out result)) return true;
            return false;
        }

        static bool TryGetFloat(JsonElement spec, string prop, out float result)
        {
            result = 0;
            if (!spec.TryGetProperty(prop, out var el)) return false;
            if (el.ValueKind == JsonValueKind.Number) { result = (float)el.GetDouble(); return true; }
            if (el.ValueKind == JsonValueKind.String &&
                float.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;
            return false;
        }

        static bool IsValidEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return false;
            int at = email.IndexOf('@');
            return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0;
        }

        /// <summary>
        /// Validate ALL fields, show inline errors, collect values.
        /// Optional fields with no value are omitted from the result.
        /// </summary>
        static bool TryCollect(List<FormField> fields, out JsonElement content, Label summaryLabel)
        {
            content = default;
            int errorCount = 0;

            var results = new List<(FormField field, bool hasValue, JsonElement value)>();
            foreach (var f in fields)
            {
                var (hasValue, val, err) = f.Collect();
                if (err != null)
                {
                    errorCount++;
                    f.ErrorLabel.text = err;
                    f.ErrorLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    f.ErrorLabel.style.display = DisplayStyle.None;

                    if (f.Required && !hasValue)
                    {
                        errorCount++;
                        f.ErrorLabel.text = $"'{f.Name}' is required";
                        f.ErrorLabel.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        results.Add((f, hasValue, val));
                    }
                }
            }

            if (errorCount > 0)
            {
                summaryLabel.text = errorCount == 1
                    ? "Please fix the highlighted field."
                    : $"Please fix {errorCount} highlighted fields.";
                summaryLabel.style.display = DisplayStyle.Flex;
                return false;
            }

            summaryLabel.style.display = DisplayStyle.None;

            using var ms = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var (field, hasValue, val) in results)
                {
                    if (!hasValue) continue;
                    writer.WritePropertyName(field.Name);
                    val.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            content = JsonSerializer.Deserialize<JsonElement>(ms.ToArray());
            return true;
        }
    }
}
