# 回归测试指南

本文档提供 Unity Agent Client 的完整回归测试清单。在代码变更后使用本文档验证所有工具和功能是否正常工作。

## 前置条件

- Unity 2021.3+ 项目，已安装 Unity Agent Client
- 场景中包含多种对象（GameObject、灯光、相机、地形、粒子系统、UI Canvas）
- ACP 兼容的 Agent 已连接，状态显示"Connected"

## 测试环境搭建

运行测试前，请准备以下场景：

1. 创建一个包含至少以下对象的场景：Camera、Directional Light、Cube、Sphere、空 GameObject
2. 在名为 "TestParticle" 的空 GameObject 上添加 `ParticleSystem` 组件
3. 添加一个 `Terrain`（3D Object > Terrain）
4. 添加一个 `Canvas`，其子对象包含 `Text`（用于 UI 测试）
5. 确保 `Assets/Materials/` 下至少有一个材质
6. 将 Cube 的 Tag 设为 "Player"，将 Sphere 的 Layer 设为 "Water"

---

## 1. unity_scene — 场景与 GameObject 操作

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 1.1 | `get_hierarchy` | 无参数调用 | 返回场景树，包含所有 GameObject | ☐ |
| 1.2 | `get_component_data` | 查询 Cube 的 Transform | 返回位置、旋转、缩放值 | ☐ |
| 1.3 | `modify_component` | 修改 Cube 的 Transform position 为 (1,2,3) | Cube 移动；Undo 可撤销 | ☐ |
| 1.4 | `create_object` | 创建名为 "TestCapsule" 的 Capsule，位置 (0,0,5)，颜色 "1,0,0,1" | 红色胶囊体出现在指定位置 | ☐ |
| 1.5 | `add_gameobject` | 创建名为 "TestCylinder" 的 Cylinder | 圆柱体出现在层级面板 | ☐ |
| 1.6 | `delete_gameobject` | 删除 "TestCylinder" | 已删除；需要权限确认（或 Auto Approve） | ☐ |
| 1.7 | `save` | 保存场景 | 场景保存成功，无错误 | ☐ |
| 1.8 | `set_selection` | 选中 Cube | Inspector 显示 Cube 属性 | ☐ |
| 1.9 | `reparent` | 将 "TestCapsule" 移至 Cube 下 | TestCapsule 成为 Cube 的子对象 | ☐ |
| 1.10 | `duplicate` | 复制 Sphere | 出现带 "(1)" 后缀的副本 | ☐ |
| 1.11 | `find_by_component` | 查找所有带 Light 组件的对象 | 返回 Directional Light | ☐ |
| 1.12 | `set_active` | 停用 Sphere | Sphere 变为非激活状态 | ☐ |
| 1.13 | `add_component` | 为 Cube 添加 Rigidbody | Rigidbody 组件出现 | ☐ |
| 1.14 | `remove_component` | 从 Cube 移除 Rigidbody | 组件已移除；需要权限确认 | ☐ |
| 1.15 | `rename` | 将 Cube 重命名为 "MainCube" | 层级面板中名称更新 | ☐ |
| 1.16 | `set_transform` | 设置 position=(0,1,0), rotation=(0,45,0) | Cube 移动并旋转 | ☐ |
| 1.17 | `open_scene` | 通过路径打开另一个场景 | 场景正确加载 | ☐ |
| 1.18 | `set_text` | 设置 UI Text 对象的文本 | 文本内容更新 | ☐ |
| 1.19 | `instantiate_prefab` | 实例化一个 Prefab | Prefab 实例出现 | ☐ |
| 1.20 | `assign_material` | 给 Sphere 指定红色 | Sphere 变为红色 | ☐ |
| 1.21 | `find_by_criteria` | 按 tag="Player" 查找 | 返回 Cube 及完整层级路径 | ☐ |
| 1.22 | `find_by_criteria` | 按 layer="Water" 查找 | 返回 Sphere 及完整层级路径 | ☐ |
| 1.23 | `find_by_criteria` | 按 componentType="Camera" 查找 | 返回 Camera 对象 | ☐ |
| 1.24 | `find_by_criteria` | 按 namePattern="test" 查找 | 返回名称中包含 "test" 的所有对象（不区分大小写） | ☐ |
| 1.25 | `find_by_criteria` | 未提供任何过滤条件 | 返回错误：需提供至少一个过滤条件 | ☐ |
| 1.26 | `describe` | 无参数调用 | 返回场景摘要：对象数量、相机、灯光、组件统计、包围盒 | ☐ |
| 1.27 | `place_on_ground` | 将 Sphere 放置到地面 | Sphere 的 Y 值调整至地面表面，底部接触地面 | ☐ |
| 1.28 | `place_on_ground` | 下方无碰撞体时放置 | 返回错误：未找到地面 | ☐ |

## 2. unity_editor — 编辑器控制

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 2.1 | `get_state` | 无参数调用 | 返回播放模式状态、活动场景信息 | ☐ |
| 2.2 | `enter_playmode` | 进入播放模式 | 编辑器进入播放模式；需要权限确认 | ☐ |
| 2.3 | `pause_playmode` | 播放中暂停 | 游戏暂停 | ☐ |
| 2.4 | `stop_playmode` | 停止播放模式 | 编辑器回到编辑模式 | ☐ |
| 2.5 | `screenshot` | 截图 | 图片文件保存到指定路径 | ☐ |
| 2.6 | `undo` | 场景修改后撤销 | 恢复到之前的状态 | ☐ |
| 2.7 | `redo` | 撤销后重做 | 修改重新应用 | ☐ |
| 2.8 | `get_console_logs` | 读取控制台输出 | 返回最近的日志条目 | ☐ |
| 2.9 | `get_console_errors` | 读取仅错误日志 | 仅返回错误/异常条目 | ☐ |
| 2.10 | `get_project_settings` | 查询项目设置 | 返回项目名称、公司、目标平台 | ☐ |
| 2.11 | `execute_menu_item` | 执行有效的菜单项 | 菜单操作执行成功 | ☐ |
| 2.12 | `build` | 触发构建 | 构建开始；需要 Dangerous 权限 | ☐ |

## 3. unity_asset — 资源管理

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 3.1 | `list` | 列出 "Assets/Materials" 下的资源 | 返回材质文件列表 | ☐ |
| 3.2 | `get_info` | 获取材质资源信息 | 返回资源类型、路径、依赖 | ☐ |
| 3.3 | `find_references` | 查找贴图的引用 | 返回使用该贴图的资源 | ☐ |
| 3.4 | `create_folder` | 创建 "Assets/TestFolder" | 文件夹出现在 Project 面板 | ☐ |
| 3.5 | `create_material` | 创建新材质 | 材质资源已创建 | ☐ |
| 3.6 | `create_prefab` | 从 Cube 创建 Prefab | Prefab 保存在 Assets 中 | ☐ |
| 3.7 | `rename` | 重命名测试材质 | 资源名称已更新 | ☐ |
| 3.8 | `move` | 将材质移动到 TestFolder | 资源已移动 | ☐ |
| 3.9 | `delete` | 删除 TestFolder | 文件夹已删除；需要权限确认 | ☐ |
| 3.10 | `refresh` | 刷新资源数据库 | AssetDatabase 已刷新 | ☐ |

## 4. unity_material — 材质操作

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 4.1 | `get_properties` | 读取材质属性 | 返回 Shader、颜色、贴图属性 | ☐ |
| 4.2 | `set_property` | 设置颜色为蓝色 | 材质颜色更新 | ☐ |
| 4.3 | `assign` | 通过颜色为 Sphere 指定材质 | Sphere 以新颜色渲染 | ☐ |
| 4.4 | `get_render_settings` | 读取渲染管线设置 | 返回质量等级、管线信息 | ☐ |
| 4.5 | `create` | 创建新材质资源 | 材质已创建 | ☐ |

## 5. unity_lighting — 灯光与环境

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 5.1 | `get_settings` | 读取灯光设置 | 返回环境光、雾、天空盒、光照贴图、场景灯光信息 | ☐ |
| 5.2 | `set_ambient` | 设置环境光颜色为暖黄色 | 环境光更新 | ☐ |
| 5.3 | `bake` | 烘焙光照贴图 | 烘焙过程启动 | ☐ |
| 5.4 | `bake` | 播放模式中烘焙 | 返回错误：播放模式下无法烘焙 | ☐ |
| 5.5 | `setup_time_of_day` | 设置为 "morning"（早晨） | 方向光角度低、暖色调、雾效开启 | ☐ |
| 5.6 | `setup_time_of_day` | 设置为 "noon"（正午） | 方向光高角度、明亮白色、无雾 | ☐ |
| 5.7 | `setup_time_of_day` | 设置为 "sunset"（日落） | 方向光低角度、橙色调 | ☐ |
| 5.8 | `setup_time_of_day` | 设置为 "night"（夜晚） | 极暗蓝色光、暗环境光 | ☐ |
| 5.9 | `setup_time_of_day` | 设置为 "overcast"（阴天） | 灰色光、雾效开启 | ☐ |
| 5.10 | `setup_time_of_day` | 场景中无方向光 | 自动创建 Directional Light | ☐ |
| 5.11 | `setup_time_of_day` | 应用后 Undo | 所有灯光恢复到之前的状态 | ☐ |
| 5.12 | `setup_time_of_day` | 无效的 timeOfDay 值 | 返回描述性错误 | ☐ |

## 6. unity_animation — 动画与导航

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 6.1 | `get_controllers` | 列出项目中的 AnimatorController | 返回控制器列表 | ☐ |
| 6.2 | `get_states` | 读取控制器的状态 | 返回状态机信息 | ☐ |
| 6.3 | `get_parameters` | 读取参数 | 返回参数名称、类型、默认值 | ☐ |
| 6.4 | `navmesh_bake` | 烘焙 NavMesh | NavMesh 已生成 | ☐ |
| 6.5 | `navmesh_get_settings` | 读取 NavMesh 设置 | 返回 Agent 半径、高度、坡度 | ☐ |
| 6.6 | `navmesh_query_path` | 查询两点间路径 | 返回路径有效性、距离、路点 | ☐ |

## 7. unity_spatial — 空间智能

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 7.1 | `raycast` | 从 (0,10,0) 向下发射射线 | 返回命中对象和命中点 | ☐ |
| 7.2 | `camera_visibility` | 检查 Cube 是否在相机视野内 | 返回可见性状态 | ☐ |
| 7.3 | `check_line_of_sight` | 检查 Camera 到 Cube 的视线 | 返回通畅/被遮挡状态 | ☐ |
| 7.4 | `detect_visible_objects` | 检测所有可见对象 | 返回可见对象列表 | ☐ |

## 8. unity_ui — UI 操作

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 8.1 | `get_canvas_hierarchy` | 读取 Canvas 层级 | 返回 UI 元素树 | ☐ |
| 8.2 | `modify_rect_transform` | 修改 Text 的 RectTransform 尺寸 | UI 元素大小改变 | ☐ |
| 8.3 | `set_text` | 设置 Text 内容为 "你好" | 文本显示"你好" | ☐ |

## 9. unity_generate — AI 3D 生成

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 9.1 | `text_to_3d` | 通过文本提示生成 | 返回任务 ID，开始生成 | ☐ |
| 9.2 | `image_to_3d` | 通过图片生成 | 返回任务 ID | ☐ |
| 9.3 | `list_tasks` | 列出生成任务 | 返回任务状态列表 | ☐ |

> **注意：** 需要 `MESHY_API_KEY` 环境变量。

## 10. unity_particle — 粒子系统工具

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 10.1 | `get_settings` | 读取 TestParticle 设置 | 返回所有模块配置 | ☐ |
| 10.2 | `get_settings` | 查询不存在的对象 | 返回错误：找不到 GameObject | ☐ |
| 10.3 | `get_settings` | 查询无 ParticleSystem 的对象 | 返回错误：无 ParticleSystem | ☐ |
| 10.4 | `set_main` | 设置 duration=3, loop=false, startSize=2, startColor="1,0,0,1" | 主模块已更新；Inspector 中可见 | ☐ |
| 10.5 | `set_main` | 设置 simulationSpace="World" | 模拟空间切换为 World | ☐ |
| 10.6 | `set_main` | 设置 maxParticles=500 | 最大粒子数已更新 | ☐ |
| 10.7 | `set_main` | 未提供任何属性 | 返回错误：需提供至少一个属性 | ☐ |
| 10.8 | `set_emission` | 设置 rateOverTime=50 | 发射率改变 | ☐ |
| 10.9 | `set_emission` | 添加 burst: time=0.5, count=20 | 发射模块中出现 Burst | ☐ |
| 10.10 | `set_emission` | 禁用 emission | 模块已禁用 | ☐ |
| 10.11 | `set_shape` | 设置 shapeType="Cone", radius=2, angle=30 | 形状变为锥体 | ☐ |
| 10.12 | `set_shape` | 设置 shapeType="Box", scale="2,1,3" | 盒子形状，自定义缩放 | ☐ |
| 10.13 | `set_shape` | 无效的 shapeType | 返回错误 | ☐ |
| 10.14 | `set_color` | 设置渐变：startColor="1,1,0,1", endColor="1,0,0,0" | 黄→红渐变，带 Alpha 淡出 | ☐ |
| 10.15 | `set_color` | 仅设置 startColor | 应用恒定颜色 | ☐ |
| 10.16 | `set_size` | 设置 startSize=1, endSize=0 | 粒子在生命周期内缩小 | ☐ |
| 10.17 | `set_size` | 仅设置 startSize=0.5 | 恒定大小倍数 | ☐ |
| 10.18 | `set_renderer` | 设置 renderMode="Stretch" | 渲染模式改变 | ☐ |
| 10.19 | `set_renderer` | 通过资源路径指定材质 | 材质应用到粒子渲染器 | ☐ |
| 10.20 | `set_renderer` | 无效的材质路径 | 返回错误：找不到材质 | ☐ |
| 10.21 | `preview` | command="play" | 粒子系统开始播放 | ☐ |
| 10.22 | `preview` | command="stop" | 粒子系统停止 | ☐ |
| 10.23 | `preview` | command="restart" | 清除并重新播放 | ☐ |
| 10.24 | `preview` | command="simulate", simulateTime=2.0 | 模拟到 t=2s | ☐ |
| 10.25 | Undo | set_main 后撤销 | 恢复到之前的值 | ☐ |

## 11. unity_terrain — 地形操作

| # | Action | 测试步骤 | 预期结果 | 通过 |
|---|--------|---------|---------|------|
| 11.1 | `get_settings` | 读取地形设置 | 返回尺寸、高度图分辨率、图层、树木、细节 | ☐ |
| 11.2 | `get_settings` | 场景中无地形 | 返回错误：找不到地形 | ☐ |
| 11.3 | `get_height` | 在地形中心采样高度 | 返回 Y 坐标 | ☐ |
| 11.4 | `get_height` | 在地形边界外采样 | 返回错误：超出边界 | ☐ |
| 11.5 | `modify_height` | mode="raise", x=50, z=50, height=5, radius=10 | 地形在该位置隆起 | ☐ |
| 11.6 | `modify_height` | mode="lower"，相同位置，height=3 | 地形降低 | ☐ |
| 11.7 | `modify_height` | mode="set", height=10, radius=5, falloff=0.8 | 在绝对高度处形成平滑山丘 | ☐ |
| 11.8 | `modify_height` | mode="flatten", height=5, radius=15 | 区域被展平到高度 5 | ☐ |
| 11.9 | `modify_height` | mode="smooth", radius=10, strength=0.5 | 区域被平滑 | ☐ |
| 11.10 | `modify_height` | mode="raise" 但未提供 height 参数 | 返回错误：需要 height 参数 | ☐ |
| 11.11 | `modify_height` | 位置在地形外 | 返回错误：超出边界 | ☐ |
| 11.12 | `paint_texture` | 在中心绘制 layer 0，radius=10, opacity=1 | 贴图绘制在该位置 | ☐ |
| 11.13 | `paint_texture` | opacity=0.5, falloff=0.3 绘制 | 部分绘制，边缘柔和 | ☐ |
| 11.14 | `paint_texture` | 无效的 layerIndex=99 | 返回错误：索引超出范围 | ☐ |
| 11.15 | `add_trees` | 在指定位置添加 3 棵树 | 树木出现在地形上 | ☐ |
| 11.16 | `add_trees` | 无效的 prototypeIndex | 返回错误：索引超出范围 | ☐ |
| 11.17 | `add_trees` | 位置在地形外 | 静默跳过超出边界的位置 | ☐ |
| 11.18 | Undo | modify_height 后撤销 | 高度图恢复 | ☐ |
| 11.19 | Undo | paint_texture 后撤销 | Splatmap 恢复 | ☐ |

## 12. 横切关注点

| # | 功能 | 测试步骤 | 预期结果 | 通过 |
|---|------|---------|---------|------|
| 12.1 | **权限：ReadOnly** | 调用 get_hierarchy | 无需提示即执行 | ☐ |
| 12.2 | **权限：Write** | 调用 create_object | 执行（记录 Undo） | ☐ |
| 12.3 | **权限：Dangerous** | 关闭 Auto Approve，调用 delete_gameobject | 弹出确认提示 | ☐ |
| 12.4 | **权限：Auto Approve** | 开启 Auto Approve，调用 delete_gameobject | 无需提示即执行 | ☐ |
| 12.5 | **Undo 集成** | 执行 3 次修改，撤销 3 次 | 所有修改按顺序撤销 | ☐ |
| 12.6 | **unity_batch** | 批量操作：创建对象 + 指定材质 | 两个操作在一次调用中成功 | ☐ |
| 12.7 | **unity_tool** | 直接访问：unity_tool(name="scene_get_hierarchy") | 返回层级（绕过路由器） | ☐ |
| 12.8 | **unity_tool** | unity_tool(name="list") | 返回所有已注册工具名称 | ☐ |
| 12.9 | **unity_skill** | 列出可用技能 | 返回 14+ 内置技能 | ☐ |
| 12.10 | **Domain Reload** | 触发脚本编译 | Agent 在重载后自动重连 | ☐ |
| 12.11 | **Play Mode 保护** | 进入播放模式，尝试烘焙光照贴图 | 返回错误：播放模式下无法操作 | ☐ |
| 12.12 | **主线程调度** | 调用任何 RequiresMainThread 工具 | 在主线程上执行，不卡顿 | ☐ |
| 12.13 | **错误处理** | 调用时缺少必需参数 | 返回清晰的错误消息 | ☐ |
| 12.14 | **场景标记修改** | 执行任何写入操作 | 场景被标记为已修改（标题显示星号） | ☐ |
| 12.15 | **连接超时** | 将 Command 设为不存在的可执行文件，打开窗口 | 约 30 秒后超时；显示错误 + Retry + Open Settings 按钮 | ☐ |
| 12.16 | **取消连接** | 连接中（Pending）点击 Cancel | 连接中止；显示 Open Settings 按钮 | ☐ |
| 12.17 | **重试连接** | 失败后在设置中修正 Command，点击 Retry | 成功连接 | ☐ |
| 12.18 | **Mode 显示** | 连接支持多种模式的代理 | 工具栏显示友好名称（如 "Agent"）而非原始 URL | ☐ |
| 12.19 | **连接状态圆点** | 用正确配置打开窗口 | 工具栏圆点从黄→绿；失败时为红；悬停有提示 | ☐ |
| 12.20 | **Typing 动画** | 发送任意提示词 | 运行期间显示 "Agent is thinking…" 带动画圆点，完成后消失 | ☐ |
| 12.21 | **Enter 发送 / Shift+Enter 换行** | 输入文本后按 Enter | 发送。Shift+Enter 换行，输入框自动增高至最大值 | ☐ |
| 12.22 | **Esc 取消** | 发送长时请求，按 Esc | 请求取消，Stop 按钮消失 | ☐ |
| 12.23 | **拖入高亮** | 拖动资源到窗口 | 显示绿色 "Drop assets here" 覆盖层；松开或离开后隐藏 | ☐ |
| 12.24 | **附件卡片** | 拖入资源；点击卡片；点击 × | 卡片显示图标 + 名称；点击可在 Project 中高亮；× 移除 | ☐ |
| 12.25 | **自动滚动黏底** | 流式响应过程中向上滚动 | 自动滚动暂停；滚回底部附近后恢复 | ☐ |
| 12.26 | **窄幅布局** | 窗口宽度 < 500px | 工具栏压缩（Auto Approve 标签隐藏）；恢复宽度后复原 | ☐ |

## 13. Elicitation（ACP RFD — Steps 1–4）

> 运行 mock agent：配置 command=`node`，args=`["<repo>/docs/samples/mock-elicitation-agent.js"]`。
> 关键词："full"/"all" → Step 2 完整表单，"unity"/"native" → Step 3 Unity 原生控件，"url"/"browser" → Step 4 URL 模式。

| # | 功能 | 测试步骤 | 预期结果 | 通过 |
|---|------|---------|---------|------|
| 13.1 | **表单出现** | 发送任意提示词；mock agent 发起 `elicitation/create` | 消息区下方出现面板，包含策略下拉菜单和提示文字 | ☐ |
| 13.2 | **Accept 往返** | 选择策略，点击 Submit | Agent 打印 `You chose: {"strategy":"<value>", …}` | ☐ |
| 13.3 | **Decline** | 点击 Decline | Agent 打印 `You declined.` | ☐ |
| 13.4 | **Cancel** | 点击 Cancel | Agent 打印 `You canceled.` | ☐ |
| 13.5 | **必填校验** | 发送 "full" 表单，不填必填字段直接 Submit | 字段下方显示内联错误；汇总提示 "Please fix N highlighted fields" | ☐ |
| 13.6 | **oneOf 标签** | 下拉菜单显示友好标题（如 "Balanced (Recommended)"）但提交值为 `const` | ☐ |
| 13.7 | **默认值** | 下拉菜单预选 `balanced`（schema 默认值） | ☐ |
| 13.8 | **Domain Reload** | 打开表单后编辑脚本触发重载 | 面板消失；无卡住；agent 收到 `cancel` | ☐ |
| 13.9 | **布尔开关** | 发送 "full"；"Enable logging" 显示为 Toggle | 默认选中（true） | ☐ |
| 13.10 | **整数滑块** | 发送 "full"；"Max retries" 显示为 SliderInt (0-10) | 带输入框，默认=3 | ☐ |
| 13.11 | **浮点滑块** | 发送 "full"；"Quality level" 显示为 Slider (0-1) | 带输入框，默认=0.8 | ☐ |
| 13.12 | **多选数组** | 发送 "full"；"Target platforms" 显示 Toggle 复选框 | 勾选部分平台，Submit → 结果为所选值数组 | ☐ |
| 13.13 | **多行文本** | 发送 "full"；"Build notes" 显示较高的多行 TextField | ☐ |
| 13.14 | **邮箱格式校验** | 发送 "full"；在 "Notification email" 输入无效邮箱，Submit | 内联错误 "Invalid email address" | ☐ |
| 13.15 | **可选字段省略** | 发送 "full"；留空可选字段，Submit | 返回 JSON 省略空字段（不是 null） | ☐ |
| 13.16 | **ObjectField（资产）** | 发送 "unity"；拖拽 Prefab 到 "Project Asset" | ObjectField 显示资产；Submit → 资产路径字符串 | ☐ |
| 13.17 | **ObjectField（场景）** | 发送 "unity"；拖拽场景 GO 到 "Scene Object" | 显示 GO；Submit → 完整层级路径 `/Parent/Child` | ☐ |
| 13.18 | **Vector3Field** | 发送 "unity"；"Spawn Position" 显示 Vector3Field 默认 (0,1,0) | 修改为 (1.5,-2,3.7)；Submit → `"1.5,-2,3.7"` | ☐ |
| 13.19 | **ColorField** | 发送 "unity"；"Tint Color" 显示 ColorField 默认橙色 | 改为绿色 50% 透明；Submit → `"#00FF0080"` | ☐ |
| 13.20 | **URL 模式打开浏览器** | 发送 "url" | 浏览器打开 example.com URL；Unity 中无表单 | ☐ |
| 13.21 | **URL 模式返回 accept** | 检查 agent stdout | Agent 收到 url 模式 elicitation 的 `accept` 响应 | ☐ |
| 13.22 | **AcpException -32042** | 使用返回 -32042 的 agent | 对话中显示 "⚠ Authorization required" | ☐ |
| 13.23 | **通用 AcpException** | 使用返回其他错误码的 agent | 对话中显示 "⚠ Agent error (CODE): message" | ☐ |

## 14. 会话管理器（多会话）

> 这些测试验证会话为一等公民、跨 Domain Reload 持久化、以及切换会话时 agent 进程复用。不存在 "Disconnect" UI —— 会话共存。

| # | 功能 | 测试步骤 | 预期结果 | 通过 |
|---|------|---------|---------|------|
| 14.1 | **单会话基线** | 全新项目；连接；发送 "hello" | 工具栏出现会话下拉菜单，标题含 "hello…"，旁边有 `＋` 按钮 | ☐ |
| 14.2 | **创建新会话** | 点击 `＋` | 对话清空；下拉菜单显示两个条目；agent 进程未重启（PID 不变） | ☐ |
| 14.3 | **切回旧会话** | 打开下拉菜单，选择第一个会话 | 原始消息重新出现（通过 `session/load` 重放）；无进程重启 | ☐ |
| 14.4 | **标题派生** | 新会话中发送 "Analyze Assets/Scenes/Main.unity"；检查下拉菜单 | 标题截断到约 28 字符带省略号；该会话排在最前 | ☐ |
| 14.5 | **Domain Reload 持久化** | 创建 2 个会话 → 触发 Domain Reload（编辑脚本） | 重载后下拉菜单恢复两个条目；最近活跃的会话自动加载 | ☐ |
| 14.6 | **配置变更清空存储** | 在设置中更改 agent Command | 下次连接时下拉菜单为空（旧会话属于之前的配置） | ☐ |
| 14.7 | **优雅关闭** | 关闭 Agent 窗口 | Agent 进程在约 1.5 秒内通过 stdin EOF 退出；任务管理器无残留进程 | ☐ |
| 14.8 | **进程崩溃恢复** | 会话打开时外部杀掉 agent 进程 | 窗口自动重连（创建新会话）；旧会话仍在列表中可尝试 `session/load` | ☐ |
| 14.9 | **运行中切换** | 发送长提示后立即切换会话 | 当前回合被取消（`session/cancel`）后再加载；无崩溃 | ☐ |

## 测试汇总

| 类别 | 总测试数 | 通过 | 失败 | 备注 |
|------|---------|------|------|------|
| unity_scene | 28 | | | |
| unity_editor | 12 | | | |
| unity_asset | 10 | | | |
| unity_material | 5 | | | |
| unity_lighting | 12 | | | |
| unity_animation | 6 | | | |
| unity_spatial | 4 | | | |
| unity_ui | 3 | | | |
| unity_generate | 3 | | | |
| unity_particle | 25 | | | |
| unity_terrain | 19 | | | |
| 横切关注点 | 26 | | | |
| Elicitation | 23 | | | |
| 会话管理器 | 9 | | | |
| **总计** | **185** | | | |

**测试人员：** _______________  
**日期：** _______________  
**Unity 版本：** _______________  
**Agent：** _______________  
