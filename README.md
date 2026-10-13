# 二维圆环（A/B 贴图与几何阻挡）

这次新增功能集中在此目录：Runtime 为运行时脚本，Editor 为父级 Inspector 和示例生成工具，Shaders 为二维透明材质，Textures、Materials、Scenes 为示例资源。此前的 UI 和 3D 原型仍保留在原目录。

通过 **Tools → Rotate Rings → 打开 A-B 二维圆环与几何阻挡示例** 打开示例，进入 Play 后拖动环身或连接头旋转。左上按钮可以解锁示例外环。圆/C 环身可沿橙色连接头的卡扣通道滑过；方向不匹配或碰到连接杆时仍受阻挡。

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

Direction 选择向内/向外。向内且 ToCenter 开启时，头中心默认到父级原点；关闭后 Length 控制杆长，DirectionOffsetDegrees 调整杆方向。RodWidth 为杆宽；HeadWidth 为头的横向宽度，HeadDepth 为沿杆方向的厚度，ShowHead 可关闭头。头相对杆始终垂直。连接点的“允许圆环身沿连接头穿过”（AllowRingBodyPassage）默认开启：连接头作为有通道的导向卡扣，圆/C 环的环身可沿头横轴滑过。“通道方向允许夹角”（PassageAngleDegrees，默认 30°）以连接头中心对应的圆环切线（圆心到连接头中心径向的垂线）与连接头横轴比较，正反方向均允许；不再使用各离散段的方向，因此收紧角度不会把卡扣两侧的曲率误判为未对齐；这是局部通道夹角，不是固定世界角度。超出夹角范围时头仍阻挡。连接杆、其他连接头和直线/门形/7/Z 形不享受通行规则。关闭此选项会将连接头恢复为实心障碍。生成的连接杆和连接头是独立 SpriteRenderer 子对象，配置仍由父级维护，手动改子对象会在刷新时覆盖。

## 几何阻挡（无真实物理）

### 固定 C 卡扣

直线、门形、7/Z 的父级 Inspector 新增「固定 C 卡扣」。直线、7/Z 可点击「添加固定 C 卡扣」，门形点击「添加左右两个竖向 C 卡扣」，左右两条竖边各放一个，两个通道均沿世界 Y 轴。卡扣为场景独立对象，不是移动物件的子对象，因此拖动物件时卡扣位置不变；它们的引用会随场景保存。

通过 **Tools → Rotate Rings → 打开固定 C 卡扣示例** 打开 ClaspDemo.unity，可直接体验四类物件，门形使用两个竖向固定卡扣。点击蓝色物件拖动，C 卡扣保持不动。

父级可编辑每个卡扣的世界位置、通道角度、外宽 Width、长度 Length、通道宽 ChannelWidth、C Sprite、材质及颜色。通道角度 0° 为左右，90° 为上下，也支持斜向。配置有效卡扣后，原 MovementAxis 不再决定方向，位移投影到卡扣通道；多个卡扣必须平行（反向也视为平行），不平行或尺寸无效时拒绝移动。没有有效启用的卡扣时仍保留旧移动轴配置。

C 图使用原 ringextra 图第三部分的 RingClasp_C 切片，追加切片时保留原 A/B 的 Sprite ID；以后可替换为拆好的 C Sprite。卡扣自身仅保留一个 SpriteRenderer，不使用显示子对象。旧布局自动迁移：对象旋转 90°，通道轴从局部 Y 改为局部 X，并交换显示尺寸，因此世界通道方向、显示朝向和轨道位置保持不变；旧的 C 卡扣图子对象会自动删除。选择场景卡扣可看到蓝色轨道轮廓和绿色通道方向线。ChannelWidth 建议大于 Thickness，组件默认留 0.06 的余量；与贴图视觉宽度独立，便于调整通行间隙。

卡扣两侧矩形轨道接入现有几何阻挡，不使用 Collider2D 或刚体。环身沿空通道滑过，弯角和宽连接头碰到轨道时会停住；C 卡扣不享受 B 连接头的角度通行豁免。启用的卡扣轨道也会阻挡其他几何模式物件。解除父级引用不会删除固定卡扣，仍需删除或停用场景卡扣对象；停用对象后它不再限制方向或参与阻挡。固定轨道阻挡目前不触发现有仅接收 ProceduralRing2D 的 CollisionBlocked 事件。

当前目录已移除旧 3D/UI 依赖，使用 Tools/Test-Ring2D.ps1 检查几何和编译；Tools/Prepare-RingClaspCheck.py 在隔离项目验证四类形状的卡扣移动、门形双卡扣、转角阻挡及场景重开。

Collision Mode 选择几何阻挡（旧字段 PhysicsMode 的 BlockRotation 数值保持兼容）。环身使用中心线路径加 Thickness/2 的胶囊段，连接杆与头使用实心矩形。Rebuild 缓存局部轮廓；运动时只更新世界坐标，包围盒先排除不可能接触的轮廓，再精确计算间距。

圆环 Circle 和 C 环 CShape 用鼠标拖动旋转；直线 Straight、门形 Gate、7 形 Seven、Z 形 ZShape 用鼠标拖动平移。在父级 Inspector 的“移动方向（世界坐标）”选择左右或上下，默认左右；物件旋转后移动轴仍为世界坐标。连接杆与连接头跟随本体移动，并参与几何阻挡，可作为卡扣。受阻时丢弃剩余鼠标位移，反向拖动立即响应。`RequestRotation(角度增量)` 立即旋转，LockRotation 禁止旋转但不禁止四种移动形状的拖动。`RequestMovement(Vector2世界位移)` 对四种移动形状投影到配置轴；圆/C 环的程序化移动仍为 XY 位移。禁止直接修改 Transform 来执行需要阻挡的运动。

检测根据当前轮廓间距和最大运动距离保守推进，检查整个旋转或移动路径；遇阻立即丢弃剩余请求，停在安全位置，不再回退。主动拖动的物件 A 碰到 B 后，由 A 持有接触状态：B 的移动和旋转锁住，A 仍可沿解除接触的方向运动，但不能继续穿入 B。B 被锁期间的拖动不会反过来锁住 A。A 退出接触后，B 再次请求运动时自动解锁。例外：两个移动形状配置同一移动轴（都左右或都上下）时不建立对方锁定，双方都可沿脱离接触的方向移动，继续挤入仍被几何检测阻止。完整 Circle 的环身碰撞轮廓保持旋转不变，主动旋转只检测连接件；C 环的缺口与环身仍随旋转更新。Console 输出受阻双方的名称、轮廓序号、接触发起者、世界坐标、Z 角、碰撞点世界坐标和停止位置；同一对对象连续受阻时每秒最多输出一次。碰撞点取两个轮廓最近表面的中点，可用于播放接触动画。两个对象首次进入同一对接触时各触发一次 `CollisionBlocked(other, worldPoint)` 事件；接触解除后可再次触发。`LastCollisionPoint` 保存最近碰撞点，持续受阻时更新。不使用 FixedUpdate、MoveRotation、碰撞响应或刚体模拟，不会推动其他环。每次最多推进 512 步，达到上限停在已经验证的安全位置。

旧场景中的 Collider2D 被禁用，Rigidbody2D.simulated 设为 false；组件不再创建物理组件。None 和旧 TriggerDetection 模式自由运动，也不产生 OnTrigger/OnCollision 回调。阻挡对象为启用且选择几何阻挡模式的 ProceduralRing2D；普通场景 Collider2D 不参与此检测。

ColliderSegments 独立控制环身轮廓采样（8–128），显示网格仍用 Settings.Segments。保持父级和祖先在 XY 平面、使用均匀缩放；狭窄缺口可提高轮廓采样。实体阻挡部分的初始轮廓应避免互相穿插；环身与方向匹配的连接头通道可重叠，本方案不负责自动分离实体重叠对象。

演示展示形状、连接点编辑和几何阻挡，不自动判胜或移除圆环。运行时旋转不重建 Mesh；修改形状或连接参数后需 Rebuild。

## 保存本体 Mesh

父级 Inspector 的 **另存本体 Mesh 并使用（Assets/Mesh）** 按钮将当前 A 本体另存为独立 `.asset`，默认目录为 Assets/Mesh，自动避开重名文件；取消不会切换网格。保存成功会自动绑定该资源，保存场景或 Prefab 后，重新打开和进入 Play 都直接使用 sharedMesh，跳过本体几何计算和顶点数组构建。多个复制的组件可以共享同一个已保存 Mesh，组件禁用/销毁不会删除资源。

颜色、材质、连接点、ColliderSegments 可继续调整，不使本体 Mesh 失效。Settings 中的形状参数或 BodySprite（包括 UV / 圆头范围）改变后自动回到程序化生成，避免显示和碰撞形状错位；Inspector 提示重新另存。**恢复程序化本体 Mesh** 清除绑定，保留已有资源。

这里只保存本体网格，不包含 B SpriteRenderer 子元素或几何阻挡轮廓；组件启用仍需生成它们。要保存整件环及连接点，请同时保存 Prefab。

本次圆角更新会使此前烘焙的 Mesh 自动失效并恢复程序化预览，请重新点击另存按钮，以使用新版网格资源。

## 文件与验证

`ProceduralRing2D.cs` 负责显示、输入和几何受限运动；`Ring2DGeometry.cs` 计算路径/UV/连接位置；`Ring2DCollision.cs` 计算轮廓间距和保守运动；`Ring2DConnectionView.cs` 保存子对象引用；`Ring2DDemoLabels.cs` 为示例说明；Editor 中的 `ProceduralRing2DEditor.cs` 管理 Inspector 和示例。

网格数据、六种形状的几何生成和角度归一化均包含在 `Ring2DGeometry.cs` 中，不再依赖旧原型目录。

在项目根目录运行 `./Tools/Test-RingGeometry.ps1`，检查六种形状的网格、三角形朝向、UV、轮廓距离、快速旋转/移动阻挡、反向退出、C 环缺口、卡扣通道角度边界、周围四环旋转、四种移动形状的卡扣阻挡、Circle 接触 Z 后双向锁定、解除接触后恢复移动、Gate 主动接触 Circle 后退出、完整圆环旋转轮廓不变、同轴横向/竖向双方退出接触以及碰撞点计算。Unity 编译和运行验证需在编辑器中执行。


