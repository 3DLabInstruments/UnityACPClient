# Unity Agent Client

[![GitHub license](https://img.shields.io/github/license/yetsmarch/UnityAgentClient)](./LICENSE)
![Unity 2021.3+](https://img.shields.io/badge/unity-2021.3+-000.svg)

[English](README.md) | [日本語](README_JA.md) | 中文

通过 Agent Client Protocol (ACP) 将任意 AI 代理（Gemini CLI、Claude Code、Codex CLI 等）集成到 Unity 编辑器。AI 代理可通过 60+ 个内置 MCP 工具直接操作场景、组件、资源，并支持 AI 驱动的 3D 资产生成。

![demo](/docs/images/img-demo.gif)

## 概述

Unity Agent Client 是一个编辑器扩展，使用 [Agent Client Protocol (ACP)](https://agentclientprotocol.com) 将 AI 代理接入 Unity 编辑器。代理不仅能聊天，还能通过 60+ 个内置工具**直接操作**你的项目。

### 实际效果

```
你：   "把 Player 的移动速度改成 10"
代理：  1. unity_scene(action="get_hierarchy")       → 找到 Player
       2. unity_scene(action="get_component_data")   → 读取组件属性
       3. unity_scene(action="modify_component")     → 修改 Speed = 10
       4. "已完成，Player 速度已设为 10。"
```

### 特性

- **任意 AI 代理** — 支持所有 ACP 兼容代理（Gemini CLI、Claude Code、Codex CLI、opencode、Goose 等）
- **60+ 个内置 MCP 工具** — 分组为元工具，高效交互
- **元工具架构** — 代理只看到约 12 个工具而非 60+，上下文开销减少约 80%
- **多会话支持** — 工具栏会话下拉菜单可切换对话；`＋` 按钮创建新会话
- **Elicitation 表单** — 代理可请求结构化输入（下拉菜单、开关、滑块、多选、颜色选择器、Vector3、对象选择等原生控件）；URL 模式可打开浏览器进行 OAuth 授权
- **AI 3D 资产生成** — 集成 Meshy AI，自然语言生成 3D 模型并自动导入场景
- **技能配方** — 14 个内置的分步操作指南，帮助代理完成复杂任务
- **可扩展工具系统** — 实现 `IMcpTool` 接口即可添加自定义工具
- **双 MCP 传输** — 根据代理能力自动选择 HTTP（无需 Node.js）或 stdio 代理
- **自动重连** — 指数退避策略，自动恢复崩溃的代理连接；修改配置自动重连

### 架构

```
┌────────────────────────────────────────────────┐
│  Unity 编辑器                                   │
│                                                │
│  AgentWindow ──ACP(stdio)──> 代理进程            │
│      │                        (Gemini/Claude)  │
│      │                             │           │
│      └── 权限请求 UI              │ MCP        │
│                                    ▼           │
│  BuiltinMcpServer  <──── HTTP 或 stdio 代理     │
│      │                                         │
│  McpToolRegistry                               │
│      ├── 场景工具 (层级, 组件, 对象操作)           │
│      ├── 编辑器工具 (运行模式, 菜单, 截图)         │
│      ├── 资源工具 (预制体, 材质, 引用查找)          │
│      ├── 动画工具 (状态机, 参数)                  │
│      └── UI 工具 (Canvas, RectTransform)        │
│              │                                 │
│          Unity Editor API                      │
└────────────────────────────────────────────────┘
```

### 为什么不用 Unity AI？

Unity 6.2 起提供了官方 [Unity AI](https://unity.com/products/ai)，但：

- **Unity AI 绑定特定模型** — 无法选择自己的 LLM 提供商
- **需要 Unity Cloud 积分** — 必须连接云端并付费
- **工具能力有限** — 没有可扩展的工具系统

Unity Agent Client **不依赖特定模型**，**免费使用**，支持任何 LLM 提供商。

## 内置 MCP 工具

代理看到 **8 个工具**，内部映射到 48 个具体操作：

```
代理看到的工具：
┌──────────────────────────────────────────────────────────────┐
│ unity_scene     — 场景和 GameObject 操作 (19 个动作)          │
│ unity_editor    — 编辑器控制和项目信息 (12 个动作)             │
│ unity_asset     — 资源管理 (14 个动作)                        │
│ unity_material  — 材质和渲染 (5 个动作)                       │
│ unity_lighting  — 灯光和环境 (3 个动作)                       │
│ unity_animation — 动画和导航 (6 个动作)                       │
│ unity_ui        — UI Canvas 操作 (6 个动作)                  │
│ unity_generate  — AI 3D 资产生成 (5 个动作)                   │
│ unity_spatial   — 空间查询与感知 (6 个动作)                    │
│ unity_skill     — 技能配方 (14 个内置技能)                    │
│ unity_tool      — 直接访问兜底 (按名称调用任意工具)             │
│ unity_batch     — 跨类别批量操作                              │
└──────────────────────────────────────────────────────────────┘
```

### unity_scene — 场景和 GameObject

| 动作 | 描述 |
|---|---|
| `get_hierarchy` | 获取完整 GameObject 层级树（含组件信息） |
| `get_component_data` | 读取任意组件的所有序列化属性 |
| `modify_component` | 修改组件属性（int、float、bool、string、Vector3、Color、Enum） |
| `create_object` / `add_gameobject` | 创建 GameObject（支持原始体、颜色、缩放、组件、父级，一步到位） |
| `delete_gameobject` | 删除 GameObject（支持撤销） |
| `save` | 保存当前场景或所有打开的场景 |
| `set_selection` | 选中 GameObject（Inspector 联动） |
| `reparent` | 更换 GameObject 父级 |
| `duplicate` | 复制 GameObject（含所有子对象） |
| `find_by_component` | 按组件类型搜索所有 GameObject |
| `set_active` | 激活/停用 GameObject |
| `rename` | 重命名 GameObject |
| `set_transform` | 一次设置位置、旋转、缩放 |
| `open_scene` | 按路径打开场景 |
| `set_text` | 设置 Text/TextMeshPro 内容（跨类别别名） |
| `instantiate_prefab` | 实例化预制体（跨类别别名） |
| `assign_material` | 赋予材质或颜色（跨类别别名） |

### unity_editor — 编辑器控制

| 动作 | 描述 |
|---|---|
| `enter_playmode` | 进入运行模式 |
| `pause_playmode` | 暂停/恢复 |
| `stop_playmode` | 停止运行模式 |
| `execute_menu_item` | 执行任意编辑器菜单命令 |
| `get_state` | 获取编辑器状态（运行模式、场景、选中对象、可用操作提示） |
| `screenshot` | 截取 Game 视图截图 |
| `undo` | 撤销最近 N 步操作 |
| `redo` | 重做最近 N 步操作 |
| `get_console_logs` | 读取控制台日志 |
| `get_console_errors` | 仅获取错误和警告 |
| `get_project_settings` | 获取项目配置 |
| `build` | 构建项目 |

### unity_lighting — 灯光和环境

| 动作 | 描述 |
|---|---|
| `get_settings` | 获取环境光、天空盒、雾、光照贴图设置和所有场景灯光 |
| `set_ambient` | 修改环境光颜色、强度、雾效开关/颜色/密度 |
| `bake` | 烘焙光照贴图 |

### unity_asset — 资源管理

| 动作 | 描述 |
|---|---|
| `list` | 按路径和类型搜索资源 |
| `get_info` | 获取资源详情、依赖、标签 |
| `find_references` | 查找引用某资源的所有资源 |
| `get_import_settings` | 读取资源导入设置 |
| `list_scenes` | 列出所有场景和构建设置 |
| `create_prefab` | 从场景对象创建预制体 |
| `instantiate_prefab` | 实例化预制体到场景 |
| `rename` | 重命名资源 |
| `move` | 移动资源到其他目录 |
| `delete` | 删除资源（移至回收站） |
| `refresh` | 强制刷新 AssetDatabase |
| `create_folder` | 创建项目文件夹 |
| `create_material` | 创建新材质 |

### unity_material — 材质和渲染

| 动作 | 描述 |
|---|---|
| `get_properties` | 读取材质所有 Shader 属性 |
| `set_property` | 修改材质属性（颜色、浮点数、纹理、向量） |
| `assign` | 赋予材质到 GameObject（按路径或按颜色自动创建） |
| `get_render_settings` | 获取画质、阴影、渲染管线设置 |
| `create` | 创建指定 Shader 的新材质 |

### unity_animation — 动画和导航

| 动作 | 描述 |
|---|---|
| `get_controllers` | 列出所有 Animator Controller |
| `get_states` | 获取完整状态机结构（状态、过渡、条件） |
| `get_parameters` | 读写 Animator 参数 |
| `navmesh_bake` | 烘焙 NavMesh |
| `navmesh_get_settings` | 获取 NavMesh 代理类型和区域设置 |

### unity_ui — UI (Canvas/UGUI)

| 动作 | 描述 |
|---|---|
| `get_canvas_hierarchy` | 获取 Canvas 专用层级视图（含 RectTransform 详情） |
| `modify_rect_transform` | 修改锚点、轴心、尺寸、位置 |
| `set_text` | 设置 Text 或 TextMeshPro 内容 |
| `get_component_data` | 读取组件属性（跨类别别名） |
| `modify_component` | 修改组件属性（跨类别别名） |
| `set_active` | 激活/停用（跨类别别名） |

### unity_generate — AI 3D 资产生成

集成 [Meshy AI](https://www.meshy.ai)，在 Unity 中直接通过自然语言生成游戏级 3D 模型。代理自动完成全流程：提示词 → API 调用 → 轮询状态 → 下载 → 导入 → 放置到场景。

| 动作 | 描述 |
|---|---|
| `text_to_3d` | 从文本描述生成 3D 模型（支持 glb/fbx/obj 格式、艺术风格、反向提示词） |
| `image_to_3d` | 从参考图片生成 3D 模型（支持 base64 或文件路径） |
| `list_tasks` | 列出最近的 Meshy 生成任务及其状态 |
| `instantiate_prefab` | 将生成的模型放入场景（跨类别别名） |
| `refresh` | 生成后刷新 AssetDatabase（跨类别别名） |

**配置：** 在 `Project Settings > Unity Agent Client > Environment Variables` 中设置 `MESHY_API_KEY`，或保存到 `UserSettings/UnityAgentClient/meshy_api_key.txt`。

**示例 — 端到端生成流程：**

```
你：   "生成一把低多边形中世纪椅子，放在位置 3,0,5"
代理：  1. unity_generate(action="text_to_3d",
          prompt="a medieval wooden chair with ornate carvings",
          artStyle="low-poly", outputFormat="glb")
       2. （轮询 Meshy API，约 30-120 秒等待模型生成）
       3. unity_generate(action="instantiate_prefab",
          prefabPath="Assets/Generated/Meshy/a_medieval_wooden_chair_143022.glb",
          position="3,0,5")
       4. "已完成。中世纪椅子已生成并放置在 (3, 0, 5)。"
```

**适用场景：**

| 场景 | 示例提示词 |
|---|---|
| **快速原型搭建** | "用家具填充这个房间 — 一张桌子、4 把椅子和一个书架" |
| **概念迭代** | "生成 3 种不同风格的科幻武器：写实、卡通和低多边形" |
| **场景布景** | "给中世纪酒馆场景添加装饰道具 — 木桶、酒杯、蜡烛" |
| **参考图建模** | （拖入图片）"根据这张概念设计图生成 3D 模型" |
| **资产管线自动化** | "生成低多边形树木并保存为预制体到 Assets/Prefabs/Environment" |
| **快速占位资产** | "我需要一个临时的机器人角色用于测试 — 生成一个并添加 Rigidbody" |
| **独立游戏开发** | "帮我生成一整套地牢道具：火把、宝箱、石柱、铁门" |
| **建筑可视化** | "生成现代风格的沙发、茶几和落地灯，布置到客厅场景" |

> 💡 使用 `artStyle="low-poly"` 获取面数优化的游戏级资产。生成的模型默认保存到 `Assets/Generated/Meshy/`。

### unity_spatial — 空间查询与感知

| 动作 | 描述 |
|---|---|
| `check_line_of_sight` | 检查两个对象（或位置）之间是否有视线通路，报告遮挡物 |
| `detect_visible_objects` | 列出摄像机可见的所有对象，按距离分组（近/中/远） |
| `raycast` | 射线检测（低层级，简单查询建议用 `check_line_of_sight`） |
| `camera_visibility` | 检查特定对象是否在摄像机视野内（视锥 + 遮挡检查） |
| `navmesh_query_path` | 计算两点间的导航路径（含距离和路径点） |
| `inject_texture` | 从 base64/文件创建纹理并可选应用到材质 |

### unity_skill — 技能配方

技能是预定义的分步指南。代理读取配方后，使用其他元工具逐步执行。

```
unity_skill(action="list")                         → 列出所有技能
unity_skill(action="search", query="button")       → 按关键词搜索
unity_skill(action="get", name="create_ui_button") → 获取完整配方
```

| 技能 | 类别 | 描述 |
|---|---|---|
| `setup_basic_scene` | scene | 地面 + 灯光 + 相机 |
| `create_ui_button` | ui | Canvas + Button + Text |
| `create_character_controller` | scene | Capsule + Rigidbody + 约束 |
| `setup_physics_layers` | physics | 查看图层和碰撞矩阵 |
| `create_animation_setup` | animation | 查看 Animator Controller 和状态 |
| `create_ui_health_bar` | ui | 基于 Slider 的血条 |
| `setup_main_menu` | ui | 标题 + 按钮组 |
| `create_spawn_system` | scene | 出生点容器模式 |
| `generate_and_place_3d_asset` | generation | AI 生成 3D 模型并放置到场景 |
| `create_colored_object` | scene | 形状 + 颜色 + 位置 + 物理，一步完成 |
| `setup_lighting` | scene | 基于氛围的灯光设置（明亮/温暖/冷调/暗黑/戏剧） |
| `create_physics_object` | physics | Rigidbody + 质量 + 约束设置 |
| `populate_scene` | scene | 批量创建对象（网格/圆形/直线/随机排列） |
| `analyze_scene` | analysis | 全面场景分析（层级 + 可见性 + 灯光） |

**自定义技能：** 在项目的 `UserSettings/UnityAgentClient/Skills/` 目录放置 `.yaml` 文件即可。

### 添加自定义工具

实现 `IMcpTool` 接口并在 `BuiltinMcpServer.RegisterTools()` 中注册：

```csharp
public class MyTool : IMcpTool
{
    public string Name => "my_custom_tool";
    public string Description => "做一些有用的事";
    public bool RequiresMainThread => true; // 使用 Unity API 时设为 true
    public JsonElement InputSchema => JsonDocument.Parse(@"{ ""type"": ""object"", ""properties"": {} }").RootElement;

    public McpToolResult Execute(JsonElement args)
    {
        // 你的 Unity API 调用
        return McpToolResult.Success("结果文本");
    }
}
```

## 安装

详细安装指南请参阅 **[docs/SETUP.md](docs/SETUP.md)**。

Unity Agent Client 需要 Unity 2021.3 或更高版本。Node.js 为可选项。

### 1. 克隆仓库

```bash
git clone https://github.com/3DLabInstruments/UnityACPClient.git
```

用 Unity Hub 打开克隆的文件夹即可。所有 DLL 已打包在内，无需额外步骤。

添加到已有项目：将 `Assets/UnityAgentClient/` 复制到你项目的 `Assets/` 目录下即可（DLL 已包含在 `Editor/Plugins/` 中）。

### 2. 配置 AI 代理

打开 `Project Settings > Unity Agent Client`，根据使用的代理填写设置。

| 代理 | Command | Arguments |
|---|---|---|
| GitHub Copilot CLI | `copilot` | `--acp` |
| Gemini CLI | `gemini` | `--experimental-acp` |
| opencode（推荐） | `opencode` | `acp` |
| Claude Code | `claude-code-acp` | — |
| Goose | `goose` | `acp` |

> ⚠️ 设置保存在 `UserSettings/` 目录，注意不要将 API Key 提交到版本控制。

## 使用方法

1. 打开 **Window > Unity Agent Client > AI Agent**
2. 自动连接代理。连接失败时会显示 **Retry** 和 **Open Settings** 按钮及错误信息；连接中可点击 **Cancel** 取消。
3. 输入提示词，按 **Enter** 或点击 **Send** 发送；**Shift+Enter** 换行，输入框会自动增高；**Esc** 取消正在执行的请求
4. 将 Assets 拖入窗口即可作为上下文附件，附件以带图标的小卡片展示，点击卡片可在 Project 面板中高亮，点击 × 移除

工具栏左侧的小圆点指示连接状态（绿 = 已连接、黄 = 连接中、红 = 失败），悬停查看详情。Agent 响应过程中会显示 "thinking…" 动画。工具栏显示当前 **Mode** 和 **Model**（代理支持时），Mode 显示友好名称（如 "Agent"、"Plan"），悬停可查看完整标识符。

**多会话管理：** 工具栏中的会话下拉菜单显示当前代理的所有会话，点击 `＋` 创建新会话。会话在 Domain Reload 后自动恢复（每个代理配置最多保留 20 个）。没有"断开连接"按钮——会话共存，代理进程保持活跃。

**Elicitation 表单：** 当代理需要结构化输入（如选择策略、配置构建参数）时，会内联显示原生 UI 表单——下拉菜单、开关、滑块、多选复选框、文本输入（含格式验证）。可以 Submit、Decline 或 Cancel（Esc）。

修改代理设置（命令/参数）后会自动重连——无需关闭窗口。

**入门提示词：**

```
"显示场景层级"
"查看项目设置"
"列出所有场景"
"把 Player 的速度改成 10"
"创建一个名为 Enemy 的 Cube，位置 5,0,3，添加 Rigidbody"
"生成一把低多边形中世纪椅子放在场景中"
"根据这张图片生成一个 3D 模型"
```

## 路线图

详见 [ROADMAP.md](ROADMAP.md)。

## 许可证

本项目基于 [MIT LICENSE](LICENSE) 授权。
