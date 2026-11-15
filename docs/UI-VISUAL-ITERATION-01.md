# UI视觉迭代记录：首轮公共控件与证据基线

日期：2026-11-04。结论：未通过完整视觉验收，未升级安装版。

## 1. 现状表

| 项目 | 已核实事实 | 状态 |
|---|---|---|
|技术|WinForms/.NET Framework 4.8/GDI+，没有新增UI库|符合|
|Palette|四主题、14色；本轮未改Palette|保留|
|字号基线|24档字面量，另有动态字号；未全部收敛|未完成|
|圆角基线|2/3/4/5/6/7/8/9/10/12/14，共11档|未完成|
|文字绘制|基线99处DrawString；本轮仅移除UiKit中的混用|部分完成|
|双缓冲|7类未显式设置完整标志，本轮修DarkButton|部分完成|
|目标|docs/visual-targets/mission.png、focus.png、system.png，均1672×941|已读取|
|渲染工具|复用tools/render-v4-ui.cs，增加--reference分支，保留三布局|已实现|
|资料隔离|独立DesktopDir与文件索引，演示文件改为有效CSV|已实现|
|安装版截图|复用shot-rail；修正宽度识别和导航Enter激活；项目页已目视确认|候选版安装后截图未完成|

## 2. DesignTokens.cs / Dpi.cs 全文

```csharp
namespace DeskBox.UI
{
    public static class DesignTokens
    {
        public const float Caption=11,BodySm=12,Body=13,Subtitle=15,Title=18,Display=24,Hero=32;
        public const int Space4=4,Space8=8,Space12=12,Space16=16,Space24=24,Space32=32,Space48=48;
        public const int RadiusSmall=6,RadiusMedium=10,RadiusLarge=14;
    }
}
```

```csharp
using System;
using System.Windows.Forms;
namespace DeskBox.UI
{
    public static class Dpi
    {
        public static int D(int designPx){return D(Form.ActiveForm,designPx);}
        public static int D(Control owner,int designPx)
        {
            int dpi=owner==null?96:owner.DeviceDpi;
            return (int)Math.Round(designPx*dpi/96f,MidpointRounding.AwayFromZero);
        }
    }
}
```

新绘制坐标显式使用带owner重载；无owner时回退96，不宣称由此完成全应用DPI迁移。

## 3. 本轮实际收敛对照

|旧值|新值|影响文件数|范围|
|---|---|---|---|
|正文9pt|Caption 11pt|1|UiKit按钮默认字体和下拉绘制|
|图标12/13pt|BodySm/Body常量|1|UiKit图标字体|
|按钮默认圆角8|RadiusMedium 10|1|DarkButton默认值|
|其他字号与圆角|未映射|—|不能宣称全库≤7/≤3|

## 4. 确定性缺陷修复

- src/UI/UiKit.cs：DarkButton补ResizeRedraw；正文、图标、下拉文字和Chip改TextRenderer，NoPrefix保留&字符。
- UiKit.CachedFont：静态字典按字体家族/字号/字重缓存，借用字体不在OnPaint中释放；其他热点尚未迁移。
- tools/render-v4-ui.cs：支持当前12页并隔离索引及桌面资料；不启动第二个DeskBox.exe。
- tools/shot-rail.cs：修正旧导航尺寸假设、跳过未找到导航却返回成功的问题；增加目标导航HWND的Enter激活。

## 5. 逐页截图与差异记录

全部宿主前后图为1672×941；本轮共用的源码变更是UiKit公共控件，未冒称每个页面的业务布局都已重做。

|页面|优化前|优化后|未关闭差异|结果|
|---|---|---|---|---|
|工作台|[前](../shots/ui-refactor/before/home.png)|[后](../shots/ui-refactor/after/home.png)|顶栏仍缺项目选择器；主区不同于Mission阶段轴和活动表|待继续迭代|
|智能桌面|[前](../shots/ui-refactor/before/desktop.png)|[后](../shots/ui-refactor/after/desktop.png)|分类、文件区、预览三列仍需比例调整；壁纸来自当前系统，未做资产保真|待继续迭代|
|项目管理|[前](../shots/ui-refactor/before/projects.png)|[后](../shots/ui-refactor/after/projects.png)|右侧概览在1672宽仍出现纵向滚动；空目录状态留白大|待继续迭代|
|工程工具|[前](../shots/ui-refactor/before/engineering.png)|[后](../shots/ui-refactor/after/engineering.png)|按钮正文统一；下方空间、公式与结果层次仍需调整|待继续迭代|
|AI助手|[前](../shots/ui-refactor/before/ai.png)|[后](../shots/ui-refactor/after/ai.png)|仍是参数栏，未达到Focus上下文文档与执行事件协作区|待继续迭代|
|文件中心|[前](../shots/ui-refactor/before/search.png)|[后](../shots/ui-refactor/after/search.png)|筛选栏换行后高度及列表信息密度需继续核查|待继续迭代|
|任务|[前](../shots/ui-refactor/before/tasks.png)|[后](../shots/ui-refactor/after/tasks.png)|公共按钮字号变化后，下拉框宽度与行内标题层级仍需统一|待继续迭代|
|工作记录|[前](../shots/ui-refactor/before/history.png)|[后](../shots/ui-refactor/after/history.png)|过滤区窄控件仍可能省略；缺日期范围和目标样式活动表|待继续迭代|
|自动化|[前](../shots/ui-refactor/before/automation.png)|[后](../shots/ui-refactor/after/automation.png)|空流程图仍有横向滚动；节点、参数、运行日志完整状态待补充验收资料|待继续迭代|
|现场事件|[前](../shots/ui-refactor/before/incidents.png)|[后](../shots/ui-refactor/after/incidents.png)|当前只测空状态；非空事件、长文本与附件状态尚未完整视觉验收|待继续迭代|
|天气|[前](../shots/ui-refactor/before/weather.png)|[后](../shots/ui-refactor/after/weather.png)|数据随网络变化；大块垂直留白和天气资产样式待调整|待继续迭代|
|移动同步|[前](../shots/ui-refactor/before/mobile.png)|[后](../shots/ui-refactor/after/mobile.png)|未登录空状态二维码占位空白；设备列表、错误状态须继续验证|待继续迭代|

安装版基线保存在交付目录“视觉验收/installed-before/v3-页面ID.png”，含私人项目资料，不进入版本库。第一次鼠标消息未切页的截图已判无效，后用导航Enter激活重拍，并打开项目页确认切换。

三布局当前图：
- [Mission](../shots/ui-refactor/three-layouts/v30-home-mission.png)，目标docs/visual-targets/mission.png。
- [Focus](../shots/ui-refactor/three-layouts/v30-home-focus.png)，目标docs/visual-targets/focus.png。
- [System](../shots/ui-refactor/three-layouts/v30-home-system.png)，目标docs/visual-targets/system.png。

原九条差异继续保留blocked：外壳、Focus右栏、System右栏、单屏节奏、Focus圆节点、排版、颜色材质、资产保真、活动表。没有进行自动像素差异分析，没有设计师人工验收。

## 6. 回归原文与限制

layout-check完整原文：dist/ui-visual-loop/layout-check.txt，末行为：
```
==== 布局验收: PASS 10 / FAIL 0 ====
```
这是天气布局测试，不代表三布局视觉通过。

smoke-ui完整原文：
```
rail buttons = 0
```
结论：漏检当前导航，不能记为通过。

probe-ui原文：dist/ui-visual-loop/probe-ui.txt。该工具读出安装版文字，但没有通过/失败断言，不能据退出码宣布页面通过。日志含运行内容，不复制私人文字进报告。

候选构建：dist/ui-visual-loop-final/DeskBox.exe。额外回归原文：
```
==== DarkInput 验收: PASS 10 / FAIL 0 ====
Reference UI: PASS 37 / FAIL 0
```

## 7. 未完成与原因

- 12页已具备宿主前后图，但候选版安装后逐页验证尚未完成，故不发布升级。
- 已按1672×941输出；没有完成100%/150%两档真实DPI验证，不把PNG放大当DPI测试。
- 字号、圆角、DrawString、双缓冲与热点字体缓存尚未全库收敛。
- smoke-ui需在既有工具内修正导航识别与断言；probe-ui仍仅作文本证据。
- 9项P1/P2设计差距没有全部关闭，继续保留blocked。
- 空状态不能代替有文档、有事件、长对话、失败重试等真实内容状态验收。

## 8. 下一步

1. 修正既有smoke/probe的验收判定，并固定实际DPI记录。
2. 完成全库字体、圆角和绘制收敛，同时逐页解决由字号变化带来的截断。
3. 按九项差距逐条重构三布局，复拍后再进行候选安装版验证。

