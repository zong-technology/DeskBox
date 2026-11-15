"""Syntax only: does not replace an Xcode type check, build, or device test."""
from pathlib import Path
import sys
import tempfile

sys.path.insert(0, str(Path(tempfile.gettempdir()) / 'deskbox-swift-syntax-runtime'))
try:
    from tree_sitter import Language, Parser
    import tree_sitter_swift
except ImportError:
    raise SystemExit('Install dev parser: python -m pip install --target "' + str(Path(tempfile.gettempdir()) / 'deskbox-swift-syntax-runtime') + '" tree-sitter tree-sitter-swift')

parser = Parser(Language(tree_sitter_swift.language()))
root = Path(__file__).resolve().parents[1] / 'mobile' / 'DeskBoxMobile' / 'DeskBoxMobile'
failed = False
for path in sorted(root.glob('*.swift')):
    tree = parser.parse(path.read_bytes())
    print(('FAIL ' if tree.root_node.has_error else 'PASS ') + path.name)
    failed |= tree.root_node.has_error
raise SystemExit(1 if failed else 0)
