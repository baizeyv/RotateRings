# 二维圆环（A/B 贴图与 Physics2D）

这次新增功能集中在此目录：Runtime 为运行时脚本，Editor 为父级 Inspector 和示例生成工具，Shaders 为二维透明材质，Textures、Materials、Scenes 为示例资源。此前的 UI 和 3D 原型仍保留在原目录。

通过 **Tools → Rotate Rings → 打开 A-B 二维圆环与物理示例** 打开示例，进入 Play 后拖动环身或连接头旋转。右下橙色环的头会被蓝色 C 环实体挡住；左上按钮可以解锁外环，旋转外环改变缺口位置。

通过 **GameObject → 2D Object → 程序化圆环（A/B）** 创建新对象。父级 `ProceduralRing2D` 管理本体、连接点和碰撞体，无须 Canvas。

## 图与形状

- `BodySprite`：A 部分，完整横向胶囊图；支持圆环 Circle、直线 Straight、C 环 CShape、门字环 Gate（开口向下）、7 字直角环 Seven（顶部横边 + 右侧竖边）、阶梯 Z 字环 ZShape（向右 → 向下 → 向右）。
- `ConnectionSprite`：B 部分，横向矩形图；杆和头各使用一次，纹理方向相差 90°。C 部分未使用。
- 可直接换成拆好的独立 Sprite。Sprite 建议使用 Full Rect、中心 Pivot；B 设置九宫格 Border 保留边缘，未设置时组件创建临时带边框 Sprite。A 横向 Border 决定圆头范围，未设置时由高度估算。
- 示例自动复制原图并切出 A/B，不改原来的 3D 贴图。A/B 资源采样使用各 Sprite 的 UV，无须继续使用原图坐标。避免 SpriteAtlas 旋转打包。
- 圆环和 C 环使用 Radius；直线使用 Width；门字、Seven 使用 Width、Height、CornerRadius；ZShape 使用独立的 TopWidth、BottomWidth，以及 Height、CornerRadius。90° 转向处用圆弧连接，开放端仍使用 A 图圆头。CornerRadius 为中心线圆角半径，必须大于 Thickness/2；门字最大为 min(Width/2, Height)，7 字为 min(Width, Height)，Z 字为 min(TopWidth, BottomWidth, Height/2)。Thickness 为环身宽度，Segments 为曲线路径分段，CapSegments 为端头分段。尺寸单位为世界空间局部单位。

## 连接点

在父级 Inspector 添加、删除、调整连接点（最多 16 个）。圆/C 环通过 AngleDegrees 指定位置，0° 向右、90° 向上；C 环缺口不能放连接点。直线/门字/7 字/Z 字环通过 PathPosition（0–1）指定位置，门字环从左下端经过顶部到右下端。

Seven 的 PathPosition 从左上端沿横边经过右上圆角，再向下到右下端，按包含圆弧的实际路径长度分配。可旋转父级获得其他方向。显示、连接点位置及碰撞中心线都使用相同圆角路径。

ZShape 为确认的阶梯折线：Inspector 的上横段宽度 TopWidth、下横段宽度 BottomWidth 独立调整。中间竖段固定在局部 X=0，上端延伸到 X=-TopWidth，下端延伸到 X=BottomWidth，单独修改一段不会移动另一段。Height 是上下横边的间距；两个转向分别用 CornerRadius 的四分之一圆弧连接。PathPosition 从左上端依次经过上横边、第一圆角、中竖边、第二圆角、下横边到右下端，沿实际路径长度分配。总水平跨度为 TopWidth + BottomWidth。

旧场景的 TopWidth/BottomWidth 未设置（0）时，组件沿用旧 Width/2，保持原造型；Inspector 显示实际宽度，首次修改任一宽度会把两段都保存为独立值，此后 Width 不参与 Z 形计算。运行时代码可直接设置 Settings.TopWidth 和 Settings.BottomWidth 为正数。

Direction 选择向内/向外。向内且 ToCenter 开启时，头中心默认到父级原点；关闭后 Length 控制杆长，DirectionOffsetDegrees 调整杆方向。RodWidth 为杆宽；HeadWidth 为头的横向宽度，HeadDepth 为沿杆方向的厚度，ShowHead 可关闭头。头相对杆始终垂直。生成的连接杆和连接头是独立 SpriteRenderer 子对象，配置仍由父级维护，手动改子对象会在刷新时覆盖。

## 真实物理旋转

PhysicsMode 默认 **BlockRotation**：本体用带半径的 EdgeCollider2D 保留内孔，杆/头用 BoxCollider2D，全部为实体。动态 Rigidbody2D 无重力、冻结中心位置；在 FixedUpdate 通过 MoveRotation 提交旋转请求，由 Physics2D 接触求解阻挡，受阻后的角度以刚体实际结果为准。LockRotation 进一步冻结角度。

父级 `ColliderSegments` 独立控制碰撞采样，默认 32（范围 8–128），显示网格仍用 Settings.Segments。默认圆环 33 点（含闭合点）、270° C 环 25 点、门字环 20 点、7 字环 11 点、Z 字环 20 点、直线 2 点；门字/7/Z 的直边只保留端点，每个圆角默认 8 段。连接杆/头继续使用 BoxCollider2D。降低采样会使曲线碰撞轮廓更粗，狭窄缺口可单独提高 ColliderSegments；旋转时不会重新生成这些点。圆角占满直边时重合采样点会去重，点数可进一步减少。

TriggerDetection 仅触发检测，不阻挡；None 关闭自动碰撞体。运行时脚本请求旋转请调用 `RequestRotation(角度增量)`，不要直接改 Transform。请保持父级位于 XY 平面，使用均匀缩放，碰撞层之间允许接触。两个可动环接触时可以互相推动旋转；固定障碍可开启 LockRotation。

演示只展示形状、连接点编辑和物理阻挡，不自动判胜或移除圆环。运行时旋转不重建 Mesh；只有修改形状/连接参数才需要 Rebuild。

## 保存本体 Mesh

父级 Inspector 的 **另存本体 Mesh 并使用（Assets/Mesh）** 按钮将当前 A 本体另存为独立 `.asset`，默认目录为 Assets/Mesh，自动避开重名文件；取消不会切换网格。保存成功会自动绑定该资源，保存场景或 Prefab 后，重新打开和进入 Play 都直接使用 sharedMesh，跳过本体几何计算和顶点数组构建。多个复制的组件可以共享同一个已保存 Mesh，组件禁用/销毁不会删除资源。

颜色、材质、连接点、ColliderSegments 可继续调整，不使本体 Mesh 失效。Settings 中的形状参数或 BodySprite（包括 UV / 圆头范围）改变后自动回到程序化生成，避免显示和碰撞形状错位；Inspector 提示重新另存。**恢复程序化本体 Mesh** 清除绑定，保留已有资源。

这里只保存本体网格，不包含 B SpriteRenderer 子元素或碰撞体；组件启用仍需配置它们。要保存整件环及连接点，请同时保存 Prefab。

本次圆角更新会使此前烘焙的 Mesh 自动失效并恢复程序化预览，请重新点击另存按钮，以使用新版网格资源。

## 文件与验证

`ProceduralRing2D.cs` 负责显示、输入和碰撞体；`Ring2DGeometry.cs` 计算路径/UV/连接位置；`Ring2DConnectionView.cs` 保存子对象引用；`Ring2DDemoLabels.cs` 为示例说明；Editor 中的 `ProceduralRing2DEditor.cs` 管理 Inspector 和示例。

复用原目录的 `RingGeometry.cs`（网格数据）、`RingUIGeometry.cs`（圆/弧/直线几何）、`RingPuzzleRules.cs`（角度工具），因此此文件夹用于功能区分，复制到别的项目时还需带上这些共享依赖。

`Tools/Test-RingGeometry.ps1` 检查几何并编译 Unity 代码。`Tools/Prepare-RingSceneCheck.py`、`Tools/Prepare-Ring2DCheck.py` 在 Temp 中建立隔离测试项目，原生 Unity 检查示例渲染、实体接触阻挡、缺口通过、反向旋转、固定中心和碰撞体刷新。
