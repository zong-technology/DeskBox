# 银紫龙翼 · 软件内全息 AI 通话

入口：DeskBox → AI&SI / AI 助手 → **全息 AI 通话**。这是现有 WinForms 软件的 `LiveVoiceForm`，不是单独启动的网页或 Electron 应用。

## 使用

1. 点击「预览全息（无需麦克风）」可查看完整角色、底座和光效。预览不建立语音会话、不读取麦克风，也不模拟字幕或连接指标。点击「结束预览」「返回助手」或 Esc 回到配置。
2. 选择「千问百炼 · Omni Realtime」或「Google · Gemini Live」，填写服务的实时模型和 API 密钥。环境变量 `DASHSCOPE_API_KEY` / `GEMINI_API_KEY` 仍可使用；同一官方服务在 AI&SI 中已经保存的密钥可在内存中复用。通用聊天模型、代理或其他服务的密钥不会自动用于这里。
3. 点击「启动实时语音」。连接成功后进入全屏幕板式通话界面，麦克风和实际扬声器音量驱动底座；支持原有的打断、暂停、字幕、扬声器开关、静音和结束。
4. 「场景」可切换银紫龙翼、无面孔核心、反应器、PLC 和项目进度，也可选择投影光效强度。「减少动效」停用连续漂浮、旋转和扫描，状态与字幕继续工作。

沉浸视图中 Tab / Shift+Tab 选择控件，Enter / Space 操作，Esc 返回助手并结束通话，Ctrl+M 切换麦克风。关闭语音界面会取消会话并释放麦克风、扬声器和动画定时器。

## 实现

角色素材依据用户提供的五张参考图生成，保留完整龙角、龙翼、长发、裙摆和脚部。原件与来源说明为 `public/assets/silverwing-avatar.png`、`public/assets/NOVA-model-license.txt`。`build.ps1` 将透明 PNG 嵌入 `DeskBox.Silverwing` 资源，分发原生 EXE 时无需额外图片路径。

原生绘制位于 `src/UI/SilverwingProjection.cs`，使用透明轮廓光、柔和背景光、轻微漂浮、衣裙扫描、地面倒影、刻度和分层发射光环。面部保留原画，光效不会在脸上叠扫描网格。底座响应真实 RMS 音量；未读取频率的原生界面不把光柱宣称为真实频谱。

这是屏幕内 **2.5D 原画投影**，支持完整人物展示；当前没有角色骨骼、自由转身、表情绑定或口型同步。反应器等原有场景和讲解投影继续保留。

根目录 `public/`、`server.mjs`、`desktop/` 是先前独立的 WebGL / Electron 预览，说明在 [Web 预览文档](SILVERWING-WEB-PREVIEW.md)。原生软件不调用其服务器或 OpenAI WebRTC 链路，两套演示的模型和配置互相独立。

## 构建和验证

Windows 原生构建：

```powershell
.\build.ps1 -OutDir dist -NoPause
```

编译使用现有 .NET Framework 编译器和 C# 5，不引入运行时依赖。新增 `System.Security.dll` 显式引用供已有 DPAPI 存储代码使用。

隔离原生检查：

```powershell
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:dist\silverwing-accept.exe tools\silverwing-accept.cs
.\dist\silverwing-accept.exe .\dist\DeskBox.exe .\dist\hologram-qa
```

检查宿主只创建实时语音窗体和离屏绘图，不启动 DeskBox 主程序、不调用服务商、不读取音频设备。它检查嵌入资源、预览无会话、返回状态、控制边界、定时器释放，并在 1600×900、1280×800、1024×768 保存 WinForms 渲染图。GitHub Actions 的 `Windows hologram candidate` 自动执行相同检查，提供候选 EXE 与截图。

这些检查不替代 Windows 用户桌面的实际 DPI、麦克风、服务配额、通话延迟、安装升级和回退验收；参考仓库既有 `docs/UI-TOOLCHAIN.md`。真实千问 / Gemini 通话需要有效密钥；云端工作区没有使用用户的生产数据或安装目录。
