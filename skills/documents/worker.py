"""Fixed document operations; one bounded JSON request over stdin, no shell commands."""
import csv
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import textwrap
import uuid
import zipfile
import xml.etree.ElementTree as ET
import subprocess
import tempfile
import hashlib
import html
import re
import shutil
from xml.sax.saxutils import escape

sys.stdin.reconfigure(encoding='utf-8', errors='strict')
sys.stdout.reconfigure(encoding='utf-8', errors='strict')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

MAX_FILE = 64 * 1024 * 1024
MAX_TEXT = 1_000_000
READ_EXTS = {'.pdf', '.docx', '.xlsx', '.xlsm', '.vsdx', '.txt', '.md', '.markdown', '.csv', '.tsv', '.log', '.json', '.xml', '.scl', '.awl'}
READ_EXTS.update({'.ini', '.yml', '.yaml', '.ps1', '.bat', '.cmd', '.cs', '.js', '.ts', '.html', '.css', '.sql', '.py', '.java', '.cpp', '.h'})


def package(path):
    archive = zipfile.ZipFile(path)
    if len(archive.infolist()) > 10000 or sum(x.file_size for x in archive.infolist()) > 128 * 1024 * 1024:
        archive.close()
        raise ValueError('压缩文档展开后过大，已停止解析')
    return archive


def xml(archive, name):
    data = archive.read(name)
    if len(data) > 16 * 1024 * 1024 or b'<!DOCTYPE' in data.upper() or b'<!ENTITY' in data.upper():
        raise ValueError('XML 大小或实体声明不被文档技能接受')
    return ET.fromstring(data)


def extract(path):
    path = Path(path)
    if path.stat().st_size > MAX_FILE:
        raise ValueError('文档超过 64 MB，请拆分后读取')
    ext = path.suffix.lower()
    if ext not in READ_EXTS:
        raise ValueError('不支持该格式；旧版 DOC/XLS/VSD 请先转换为 DOCX/XLSX/VSDX')
    used = 0
    if ext == '.pdf':
        import pymupdf
        found = False
        with pymupdf.open(path) as reader:
            if reader.needs_pass and not reader.authenticate(''):
                raise ValueError('PDF 已加密，需要先解密再读取')
            for page_no, page in enumerate(reader, 1):
                text = page.get_text(sort=True) or ''
                if text.strip():
                    found = True
                    yield f'[页 {page_no}]\n{text}'
                    used += len(text)
                if used >= MAX_TEXT or page_no >= 2000:
                    yield '[提取上限：仅返回前 2000 页或 100 万字符]'
                    break
        if not found:
            raise ValueError('PDF 没有可提取的正文，可能是扫描件；需要 OCR 后才能分析')
    elif ext == '.docx':
        with package(path) as archive:
            names = [n for n in archive.namelist() if n == 'word/document.xml' or n.startswith(('word/header', 'word/footer', 'word/footnotes', 'word/endnotes')) and n.endswith('.xml')]
            if 'word/document.xml' not in names:
                raise ValueError('无效 Word 文档：缺少正文')
            for name in sorted(names, key=lambda n: n != 'word/document.xml'):
                root = xml(archive, name)
                for number, p in enumerate(root.iter('{http://schemas.openxmlformats.org/wordprocessingml/2006/main}p'), 1):
                    text = ''.join(node.text or '' for node in p.iter() if node.tag.endswith('}t'))
                    if text.strip():
                        yield f'[{Path(name).name} 段落 {number}] {text}'
                        used += len(text)
                    if used >= MAX_TEXT:
                        yield '[提取上限：100 万字符]'
                        return
                ns = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
                for table_no, table in enumerate(root.iter(ns + 'tbl'), 1):
                    for row_no, row in enumerate(table.findall(ns + 'tr'), 1):
                        cells = [''.join(n.text or '' for n in cell.iter(ns + 't')) for cell in row.findall(ns + 'tc')]
                        if any(cells):
                            text = f'[{Path(name).name} 表格 {table_no} 行 {row_no}] ' + '\t'.join(f'列{i + 1}={value}' for i, value in enumerate(cells))
                            yield text; used += len(text)
                            if used >= MAX_TEXT:
                                yield '[提取上限：100 万字符]'
                                return
    elif ext in ('.xlsx', '.xlsm'):
        import openpyxl
        with package(path):
            pass
        workbook = openpyxl.load_workbook(path, read_only=True, data_only=False, keep_links=False)
        count = 0
        try:
            for sheet in workbook.worksheets:
                for row in sheet.iter_rows():
                    count += len(row)
                    values = [f'{cell.coordinate}={cell.value}' for cell in row if cell.value is not None]
                    if values:
                        row_number = next(cell.row for cell in row if cell.value is not None)
                        text = f'[工作表 {sheet.title} | 行 {row_number}] ' + '\t'.join(values)
                        yield text
                        used += len(text)
                    if count >= 200000 or used >= MAX_TEXT:
                        yield '[提取上限：20 万单元格或 100 万字符；公式显示原始表达式，不进行重算]'
                        return
        finally:
            workbook.close()
    elif ext == '.vsdx':
        with package(path) as archive:
            for name in archive.namelist():
                if name.startswith('visio/pages/page') and name.endswith('.xml') and '/_rels/' not in name:
                    root = xml(archive, name)
                    for shape in root.iter('{http://schemas.microsoft.com/office/visio/2012/main}Shape'):
                        text = ' '.join(''.join(n.itertext()) for n in shape.findall('{http://schemas.microsoft.com/office/visio/2012/main}Text'))
                        if text.strip():
                            yield f'[{Path(name).name} | 形状 {shape.get("ID", "?")}] {text}'
                            used += len(text)
                        if used >= MAX_TEXT:
                            yield '[提取上限：100 万字符]'
                            return
    else:
        raw = path.read_bytes()
        if raw.startswith((b'\xff\xfe', b'\xfe\xff')):
            text = raw.decode('utf-16')
        else:
            try:
                text = raw.decode('utf-8-sig')
            except UnicodeDecodeError:
                text = raw.decode('gb18030')
        for i, line in enumerate(text.splitlines(), 1):
            yield f'[行 {i}] {line}'
            used += len(line)
            if used >= MAX_TEXT:
                yield '[提取上限：100 万字符]'
                return


def read(request):
    path = Path(request['path'])
    offset = max(0, int(request.get('offset', 0)))
    limit = max(100, min(200000, int(request.get('maxChars', 16000))))
    text = '\n'.join(extract(path))
    if not text.strip():
        raise ValueError('文档中没有可读取的正文或单元格')
    segment = text[offset:offset + limit]
    more = offset + len(segment) < len(text)
    return {'Ok': True, 'Text': f'文件：{path.resolve()}\n字符偏移：{offset}\n{segment}', 'Truncated': more, 'NextOffset': offset + len(segment) if more else -1}


def search(request):
    root = Path(request['root']).resolve()
    if not root.exists():
        raise FileNotFoundError('搜索文件或目录不存在')
    keyword = str(request.get('keyword', '')).strip()
    if not keyword:
        raise ValueError('内容搜索需要 keyword')
    limit = max(1, min(30, int(request.get('limit', 10))))
    scanned, hits, errors = 0, [], []
    if root.is_file() and root.suffix.lower() not in READ_EXTS:
        raise ValueError('该文件格式不支持内容搜索')
    sources = [(str(root.parent), [], [root.name])] if root.is_file() else os.walk(root, followlinks=False)
    for directory, dirs, files in sources:
        dirs[:] = [d for d in dirs if not os.path.islink(Path(directory) / d) and not os.path.isjunction(Path(directory) / d)]
        for name in files:
            if name.startswith('.DeskBox-'):
                continue
            path = Path(directory) / name
            if path.suffix.lower() not in READ_EXTS or path.is_symlink():
                continue
            scanned += 1
            if scanned > 100:
                return {'Ok': True, 'Text': json.dumps({'Hits': hits, 'Scanned': 100, 'Incomplete': True, 'Errors': errors[:10]}, ensure_ascii=False)}
            try:
                for segment in extract(path):
                    pos = segment.casefold().find(keyword.casefold())
                    if pos >= 0:
                        hits.append({'Path': str(path.resolve()), 'Source': segment[:segment.find(']') + 1], 'Excerpt': segment[max(0, pos - 100):pos + len(keyword) + 200]})
                        if len(hits) >= limit:
                            return {'Ok': True, 'Text': json.dumps({'Hits': hits, 'Scanned': scanned, 'Incomplete': True, 'Errors': errors[:10]}, ensure_ascii=False)}
            except Exception as exc:
                errors.append({'Path': str(path), 'Error': str(exc)[:200]})
    return {'Ok': True, 'Text': json.dumps({'Hits': hits, 'Scanned': scanned, 'Incomplete': False, 'Errors': errors[:10]}, ensure_ascii=False)}


def create(request):
    target = Path(request['path'])
    if target.exists():
        raise ValueError('目标已存在，不能覆盖；请使用新的文件名')
    content = str(request.get('content', ''))
    if len(content) > 200000:
        raise ValueError('创建内容超过 20 万字符')
    if any(ord(ch) < 32 and ch not in '\t\r\n' or 0xD800 <= ord(ch) <= 0xDFFF for ch in content):
        raise ValueError('内容含无效控制字符，不能生成合法文档')
    ext = target.suffix.lower()
    if ext not in {'.docx', '.xlsx', '.pdf', '.txt', '.md', '.markdown', '.csv', '.tsv'}:
        raise ValueError('当前支持新建 DOCX/XLSX/PDF/TXT/Markdown/CSV/TSV；不编辑 Visio 图形')
    # Stage next to the destination; failed/terminated writers cannot leave a final corrupt document.
    target.parent.mkdir(parents=True, exist_ok=True)
    path = target.with_name('.DeskBox-' + uuid.uuid4().hex + ext)
    with path.open('xb'):
        pass
    try:
        if ext == '.docx':
            ns = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
            doc = ET.Element('w:document', {'xmlns:w': ns})
            body = ET.SubElement(doc, 'w:body')
            for line in content.splitlines():
                p = ET.SubElement(body, 'w:p'); run = ET.SubElement(p, 'w:r')
                ET.SubElement(run, 'w:t', {'xml:space': 'preserve'}).text = line
            with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as archive:
                archive.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
                archive.writestr('_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
                archive.writestr('word/document.xml', ET.tostring(doc, encoding='utf-8', xml_declaration=True))
        elif ext == '.xlsx':
            import openpyxl
            workbook = openpyxl.Workbook(); sheet = workbook.active
            for row in csv.reader(io.StringIO(content), delimiter='\t'):
                sheet.append(row)
            for row in sheet:
                for cell in row:
                    if isinstance(cell.value, str): cell.data_type = 's'
            workbook.save(path); workbook.close()
        elif ext == '.pdf':
            import pymupdf
            document = pymupdf.open(); page = None; y = 800
            for paragraph in content.splitlines() or ['']:
                for line in textwrap.wrap(paragraph, 42, replace_whitespace=False) or ['']:
                    if y > 790: page = document.new_page(); y = 50
                    page.insert_text((50, y), line, fontname='china-s', fontsize=10); y += 17
            document.save(path); document.close()
        else:
            path.write_text(content, encoding='utf-8')
        if os.name == 'nt':
            os.rename(path, target)  # Windows rename refuses to overwrite an existing target.
        else:
            os.link(path, target)
            path.unlink()
    except Exception:
        path.unlink(missing_ok=True)
        raise
    return {'Ok': True, 'Text': f'已新建 {target.resolve()}'}


def digest(path):
    if path.stat().st_size > MAX_FILE:
        raise ValueError('文件超过 64 MB，不能执行定点改写')
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def edit(request):
    target = Path(request['path'])
    expected = str(request.get('expected_sha256', '')).lower()
    before = digest(target)
    if before != expected:
        raise ValueError('文件已经变化或缺少 expected_sha256；请先 doc.read 获取新校验值')
    find, replacement = str(request.get('find', '')), str(request.get('replacement', ''))
    if not find or len(find) > 100000 or len(replacement) > 200000:
        raise ValueError('查找文本为空或替换内容过长')
    if any(ord(ch) < 32 and ch not in '\t\r\n' or 0xD800 <= ord(ch) <= 0xDFFF for ch in find + replacement):
        raise ValueError('替换内容含无效控制字符，原文档保留')
    ext = target.suffix.lower()
    if ext in {'.pdf', '.xlsm', '.vsdx', '.doc', '.xls', '.vsd'}:
        raise ValueError('该格式暂不支持原位改写；可读取后经确认新建副本，不会破坏原件')
    if ext not in READ_EXTS:
        raise ValueError('不支持该格式改写')
    stage = target.with_name('.DeskBox-edit-' + uuid.uuid4().hex + ext)
    count = 0
    try:
        if ext in {'.docx', '.xlsx'}:
            with package(target) as source, zipfile.ZipFile(stage, 'x') as output:
                for item in source.infolist():
                    data = source.read(item)
                    text_part = (item.filename == 'word/document.xml' or item.filename.startswith(('word/header', 'word/footer', 'word/footnotes', 'word/endnotes')) and item.filename.endswith('.xml')) if ext == '.docx' else item.filename == 'xl/sharedStrings.xml' or item.filename.startswith('xl/worksheets/sheet') and item.filename.endswith('.xml')
                    if text_part:
                        xml(source, item.filename)
                        def replace_node(match):
                            nonlocal count
                            text = html.unescape(match.group('text'))
                            hits = text.count(find)
                            if not hits:
                                return match.group(0)
                            count += hits
                            attrs = match.group('attrs')
                            if 'xml:space=' not in attrs:
                                attrs += ' xml:space="preserve"'
                            tag = match.group('tag')
                            return '<' + tag + attrs + '>' + escape(text.replace(find, replacement)) + '</' + tag + '>'
                        data = re.sub(r'<(?P<tag>(?:[A-Za-z_][\w.-]*:)?t)\b(?P<attrs>[^>]*)>(?P<text>[^<]*)</(?P=tag)>', replace_node, data.decode('utf-8')).encode('utf-8')
                    output.writestr(item, data)
        else:
            raw = target.read_bytes()
            encoding = 'utf-8-sig' if raw.startswith(b'\xef\xbb\xbf') else 'utf-8'
            text = raw.decode(encoding)
            count = text.count(find)
            stage.write_bytes(text.replace(find, replacement).encode(encoding))
        if not count:
            raise ValueError('没有找到完整文本；Office 定点替换不跨格式片段，不修改公式或数值单元格')
        if digest(target) != before:
            raise ValueError('编辑期间文件发生变化，已取消改写')
        backup = Path(request['backup_dir']) / uuid.uuid4().hex / target.name
        backup.parent.mkdir(parents=True, exist_ok=False)
        shutil.copy2(target, backup)
        if digest(target) != before:
            raise ValueError('备份期间文件发生变化，已取消改写')
        os.replace(stage, target)
        return {'Ok': True, 'Text': f'已替换 {count} 处文本。原件备份：{backup}\nSHA256={digest(target)}'}
    finally:
        if stage.exists():
            stage.unlink()


def main():
    request = json.loads(sys.stdin.read(2_000_000))
    op = request.get('op', '')
    if op == 'support':
        return {'Ok': True, 'Text': json.dumps({x: importlib.util.find_spec(x) is not None for x in ['pypdf', 'openpyxl', 'pymupdf']}, ensure_ascii=False)}
    if op == 'read':
        path = Path(request['path'])
        before = digest(path)
        result = read(request)
        if digest(path) != before:
            raise ValueError('读取期间文件发生变化，请重新读取')
        result['Text'] = 'SHA256=' + before + '\n' + result.get('Text', '')
        return result
    if op == 'search': return search(request)
    if op == 'create': return create(request)
    if op == 'qrcode':
        import qrcode
        import base64
        content = str(request.get('content', ''))
        if not content.startswith('https://') or len(content) > 4096:
            raise ValueError('二维码仅接受有效长度的HTTPS云登录地址')
        image = qrcode.make(content)
        output = io.BytesIO()
        image.save(output, format='PNG')
        return {'Ok': True, 'Text': base64.b64encode(output.getvalue()).decode('ascii')}
    if op == 'edit': return edit(request)
    if op == 'ocr':
        import pymupdf
        path = Path(request['path'])
        if path.suffix.lower() != '.pdf' or path.stat().st_size > MAX_FILE:
            raise ValueError('OCR 仅支持 64 MB 内的 PDF')
        with pymupdf.open(path) as document:
            index = int(request.get('page', 1)) - 1
            if index < 0 or index >= len(document): raise ValueError('OCR 页码超出范围')
            page = document[index]
            scale = min(2.5, 2400 / max(page.rect.width, page.rect.height))
            with tempfile.TemporaryDirectory(prefix='DeskBox-OCR-') as directory:
                image = Path(directory) / 'page.png'
                page.get_pixmap(matrix=pymupdf.Matrix(scale, scale), alpha=False).save(image)
                result = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(Path(__file__).with_name('windows-ocr.ps1')), '-ImagePath', str(image)], capture_output=True, encoding='utf-8', timeout=23, creationflags=subprocess.CREATE_NO_WINDOW)
                response = json.loads(result.stdout.lstrip('\ufeff'))
                if not response.get('Ok'): raise ValueError(response.get('Error', 'Windows OCR failed'))
                text = response.get('Text', '').strip()
                if not text: raise ValueError('该页未识别出文字')
                return {'Ok': True, 'Text': f'文件：{path.resolve()}\n[页 {index + 1} / 共 {len(document)} 页 · Windows OCR]\n{text}\n[OCR 结果可能有误；关键数值请核对原件。后续页请用 page 参数继续读取。]'}
    raise ValueError('未知文档操作')


if __name__ == '__main__':
    try:
        output = main()
    except Exception as error:
        output = {'Ok': False, 'Error': f'{type(error).__name__}: {error}'}
    print(json.dumps(output, ensure_ascii=True))
