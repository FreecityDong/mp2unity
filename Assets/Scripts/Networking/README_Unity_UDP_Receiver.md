# Unity 侧 UDP 接收（阶段 B）

## 文件
- `Assets/Scripts/Networking/PoseUdpReceiver.cs`
- `Assets/Scripts/Networking/PoseGizmoSkeletonDrawer.cs`
- `Assets/Scripts/Networking/PoseRetargeter.cs`

## 使用方式
1. 当前示例场景已默认挂载到 `Main Camera`：
   - `PoseUdpReceiver`
   - `PoseGizmoSkeletonDrawer`
2. 配置参数：
   - `Listen Port`: `5005`（与 Python 发送端保持一致）
   - `Bind Address`: `0.0.0.0`
   - `Auto Start On Enable`: 勾选
3. 运行场景后，屏幕左上角会显示：
   - 最新 `frame_id`
   - `recv/pps/kbps`
   - `success/lost/ooo/bad`
   - `latency_avg/max`
4. Scene 视图开启 Gizmos 后，可看到 33 点骨架线框。

## Python 发送端示例
```bash
source .venv/bin/activate
python scripts/run_stage_b_sender.py \
  --host 127.0.0.1 --port 5005 \
  --send-hz 30 \
  --backend tasks --model-variant lite
```

## 说明
- `PoseUdpReceiver` 已实现后台线程收包 + 主线程解析统计。
- `PoseGizmoSkeletonDrawer` 用于验证“接收数据方向/连线”是否正确，作为阶段C动作重放前置。
- `PoseGizmoSkeletonDrawer` 默认按世界坐标绘制（`useObjectTransform=false`），避免挂在 `Main Camera` 时骨架被相机位姿带偏。
- 若出现“左右反了”，检查 `PoseGizmoSkeletonDrawer.flipX`（当前默认关闭）。
- 若出现“上下颠倒”，优先检查 `flipY`，并可开启 `autoUprightByShoulderHip`（已默认开启）自动纠正。
- 若编辑器中感觉卡顿：
  - 保持 `processLatestPacketOnly=true`（已默认开启）；
  - 关闭 `logStatsToConsole`；
  - 若仍卡顿可临时关闭点绘制（`drawPoints=false`）；
  - Python 发送频率可先降到 `--send-hz 20` 再观察。
- 当前阶段先完成链路验证；阶段 C 再在此基础上接机器人骨骼重定向。

## 阶段 C 前两步（已落地）
1. `Main Camera` 已挂载 `PoseRetargeter`。
2. 默认开启 `autoCreateProxyRig`，运行后会自动创建一个 `ProxyRig`（临时骨架）：
   - 无需先导入机器人模型；
   - 当前默认关闭 `proxyOneToOnePositionMode`，进入“旋转重定向”模式；
   - 首帧会自动做一次源姿态标定（`autoCalibrateSourcePoseOnFirstFrame=true`）；
   - 后续拿到正式模型后，只需在 Inspector 里把骨骼 Transform 映射到对应字段即可复用算法。

### 调试建议
- 先观察 `ProxyRig` 是否跟随 33 点骨架方向一致。
- 若希望“逐点核对模式”，可开启 `proxyOneToOnePositionMode`。
- 若方向不一致，优先调整 `PoseRetargeter` 的 `flipX/flipY/flipZ`。
- 若仅出现“左右手脚对调”（而非整体镜像），可开启 `PoseRetargeter.swapLeftRightLandmarks`。
- 若动作中包含倒立/翻转，保持 `autoUprightByShoulderHip=false`（避免被强制扶正）。
- 若抖动明显，可提高 `rotationLerp` 与 `rootPositionLerp`（建议 0.35 -> 0.5 逐步调）。
- 如需在不运行场景时先创建骨架：在 `PoseRetargeter` 组件右上角菜单执行 `Build Proxy Rig Now`。
- 如需重新设定“中立姿态”：
  - 在人物站立自然姿态时执行 `Capture Source Calibration (Current Pose)`；
  - 如标定错误可执行 `Reset Source Calibration` 后重做。
  - 当前算法会同时记录“源中立方向 -> Rig中立旋转偏置”，用于消除初始 T-Pose 偏差。
  - 且会按标定帧自动拟合 ProxyRig 骨长（包含大腿/小腿），减少比例失真。

## 正式模型挂载（Humanoid）
1. 导入模型后，确认 `Rig` 类型是 `Humanoid`，并存在 `Animator` 组件。
2. 在挂有 `PoseRetargeter` 的对象（当前通常是 `Main Camera`）里：
   - 将 `targetAnimator` 指向该模型的 `Animator`（也可留空，脚本会自动查找子节点）。
   - 执行组件菜单：`Auto Bind Humanoid Bones From Animator`。
3. 自动绑定成功后：
   - 脚本会填充 hips/spine/chest/四肢等骨骼引用；
   - 若 `disableProxyWhenHumanoidBound=true`，会自动关闭 Proxy 自动创建逻辑。
4. 再做一次源姿态标定：
   - `Reset Source Calibration`
   - `Capture Source Calibration (Current Pose)`

提示：
- `autoBindHumanoidAnimatorOnAwake=true` 时，运行后会自动尝试绑定一次。
- 若 `targetAnimator` 为空，脚本会先查当前对象子节点，再扫描场景内第一个 Humanoid Animator。
- 如果模型不是 Humanoid，请改为手动拖拽骨骼引用。
