from html import escape
from pathlib import Path
from sys import argv
from zipfile import ZipFile, ZIP_DEFLATED

before, after, result = map(Path, argv[1:4])
version = argv[4].lstrip('v')
comparisons = [
    ('input', '程序编辑与需求输入', '空工程，鼠标焦点、英文及中文粘贴已测试', '工程状态、代码区与固定需求入口分层。'),
    ('input-selection', '输入框选区', '空工程，键盘全选、复制与多行已测试', '保留真实输入选区。'),
    ('analysis', '程序分析', '已导入测试资料', '结论优先，引用关系单独展示。'),
    ('hardware', '硬件组态', '已导入测试资料', '机架占整行，清单与设备属性并排。'),
    ('topology', '网络拓扑', '已导入 CAx AML 测试资料', '画布展示来源中已解析的连接。'),
    ('variables', '变量管理', '已导入 IO CSV 测试资料', '结构化字段、筛选栏与详情取代长文本。'),
    ('diff', 'SCL 差异', '本机模板草稿，待人工确认', '并排显示删减与新增行，有差异导航。'),
    ('validation', '测试验证', '已执行静态检查，V20 编译 BLOCKED', '验证状态和缺少的环境明确分层。'),
    ('environment', '环境检测', '真实 Windows 检测，V20 工程闭环 BLOCKED', '集中查看版本、依赖与用户组。'),
    ('assistant-collapsed', 'AI 面板折叠', '已操作折叠', '把收回的宽度交还中央工作区。'),
    ('layout-default', '恢复默认布局', '已恢复布局并显示本机 SCL 草稿', '核对 62px 栏、260px 工程树、350px 助手和 196px 输出。'),
    ('voice', '独立工程语音窗口', '已打开，供应商通话 BLOCKED', '真实空闲状态，可观察现有控制入口。'),
]
sections = []
index = [f'# DeskBox 安装截图对照', '', f'前 4.2.2-ui.3 · 后 {version}', '',
         '后版中标为测试资料的视图通过已安装程序读取测试文件。前版为发布包中的原始状态。部分页面状态不同。', '']
with ZipFile(before) as older, ZipFile(after) as newer, ZipFile(result, 'w', ZIP_DEFLATED) as out:
    for i, (key, title, state, detail) in enumerate(comparisons, 1):
        name = 'installed-tia-' + key + '.png'
        original, revised = f'before/{name}', f'after/{name}'
        out.writestr(original, older.read(name))
        out.writestr(revised, newer.read(name))
        index.append(f'{i}. {title} · {state}。{detail} [整改前]({original}) · [整改后]({revised})')
        sections.append(f'<section><h2>{i}. {escape(title)}</h2><p>{escape(state)}。{escape(detail)}</p>'
                        + '<div class="pair">'
                        + ''.join(f'<figure><figcaption>{side}</figcaption><a href="{path}"><img src="{path}" loading="lazy" alt="{escape(title)} {side}"></a></figure>'
                                  for side, path in [('整改前', original), ('整改后', revised)]) + '</div></section>')
    out.writestr('INDEX.md', '\n'.join(index) + '\n')
    out.writestr('index.html', '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>DeskBox TIA 安装截图对照</title><style>
body{background:#101827;color:#eaf3ff;font:15px/1.6 system-ui,sans-serif;margin:0}main{max-width:1680px;margin:auto;padding:28px}
section{padding:18px 0 26px;border-top:1px solid #34445e}figure{margin:0;min-width:0}img{display:block;width:100%;height:auto;border:1px solid #34445e}
.pair{display:grid;grid-template-columns:1fr 1fr;gap:14px}figcaption{color:#b6cae1;margin-bottom:6px}h1,h2{margin:0 0 6px}p{margin:0 0 10px}
@media(max-width:800px){.pair{grid-template-columns:1fr}} </style><main><h1>DeskBox TIA 安装截图对照</h1>'''
        + f'<p>4.2.2-ui.3 → {escape(version)}。点击图片查看原图。</p>'
        + '<p>后版部分页面使用标注的测试资料，图中数据来自实际导入。两版页面的数据状态可能不同。</p>'
        + ''.join(sections) + '</main></html>')
    out.writestr('AFTER-SCREENSHOT-INDEX.md', newer.read('SCREENSHOT-INDEX.md'))
