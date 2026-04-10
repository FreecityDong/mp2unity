# PoseCatch Unity 端（MediaPipe UDP Receiver + Retarget）

这个 Unity 项目用于接收 Python 端发送的 MediaPipe Pose 33 点（`pose_v2_mp33`），并完成：
- UDP 接收与状态统计
- Gizmos 骨架可视化（33 点）
- Humanoid 骨骼重定向（以 Mixamo XBot/YBot 为主）

## 1. 版本要求
- Unity: `2022.3.62t6`（项目实际版本）

## 2. 目录与脚本
- `Assets/Scripts/Networking/PoseUdpReceiver.cs`
- `Assets/Scripts/Networking/PoseGizmoSkeletonDrawer.cs`
- `Assets/Scripts/Networking/PoseRetargeter.cs`
- 详细说明：`Assets/Scripts/Networking/README_Unity_UDP_Receiver.md`

## 3. 场景快速启动
1. 打开 `Assets/Scenes/SampleScene`。
2. 确认 `Main Camera` 上挂载组件：
   - `PoseUdpReceiver`
   - `PoseGizmoSkeletonDrawer`
   - `PoseRetargeter`
3. 核对关键参数：
   - `Listen Port = 5005`
   - `Bind Address = 0.0.0.0`
   - `Auto Start On Enable = true`
4. 点击 Play，等待 Python 端发送数据。

## 4. Python 发送端命令（在 poseCatch 仓库执行）

### 4.1 视频 + matplotlib + UDP（阶段 B/C 常用）
```bash
source .venv/bin/activate
python scripts/run_stage_b_visual_sender.py \
  --input-video /绝对路径/your_video.mp4 \
  --loop-video \
  --host 127.0.0.1 --port 5005 \
  --send-hz 30 \
  --backend tasks --model-variant lite \
  --show-index-labels
```

### 4.2 标定图发送（阶段 C 标定）
```bash
source .venv/bin/activate
python scripts/run_stage_c_image_calibration_sender.py \
  --input-image /绝对路径/calib_pose.jpg \
  --host 127.0.0.1 --port 5005 \
  --send-hz 15 --seconds 8
```

## 5. Humanoid 模型挂载（Mixamo XBot/YBot）
1. 导入 FBX，`Rig` 设置为 `Humanoid`。
2. 将模型拖入 Hierarchy。
3. 在 `PoseRetargeter` 中勾选/使用自动绑定：
   - `Auto Bind Humanoid Bones From Animator`
4. 完成后建议执行一次：
   - `Reset Source Calibration`
   - `Capture Source Calibration (Current Pose)`

## 6. 常见问题
- 收到 UDP 但看不到骨架：
  - 确认 Scene 视图 `Gizmos` 已开启；
  - 确认 `PoseGizmoSkeletonDrawer` 的 `receiver` 引用正确；
  - 检查 `useObjectTransform`（默认建议关闭，按世界坐标画）。
- 左右反了：
  - 检查 `PoseRetargeter.swapLeftRightLandmarks`；
  - 再检查 `flipX/flipY/flipZ`。
- 有卡顿：
  - 保持 `processLatestPacketOnly=true`；
  - 降低发送频率到 `--send-hz 20` 先验证。

## 7. Git 提交建议
Unity 项目建议只提交：
- `Assets/`
- `Packages/`
- `ProjectSettings/`
- `.gitignore`

不建议提交：
- `Library/`
- `Temp/`
- `Logs/`
- `UserSettings/`

