"""Builds the "old RAG" server root for the evaluation: the current src\\, html\\ and skills\\ with the frozen old syntax
sheets (old-docs\\code-syntax) and models\\ joined to the real one, so the old-sheets arm runs exactly the same code.
Rebuild it after any code change:   python .claude\\claude_tests\\rag-eval\\make-oldroot.py
Started through .claude\\launch.json ("phoenix-oldrag"). Dev-only.
"""
import os, shutil, subprocess

here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.dirname(os.path.dirname(os.path.dirname(here)))
# outside claude_tests: gate.ps1 and the other test scripts compile every .cs file under claude_tests
root = os.path.join(repo, '.claude', 'rag-eval-oldroot')
models = os.path.join(root, 'models')
if os.path.isdir(models) and os.path.islink(models) is False:
    subprocess.run(['cmd', '/c', 'rmdir', models], check=False)   # the junction only: never the real models
for d in ('src', 'html', 'skills', 'docs'):
    shutil.rmtree(os.path.join(root, d), ignore_errors=True)
os.makedirs(root, exist_ok=True)
for d in ('src', 'html', 'skills'):
    shutil.copytree(os.path.join(repo, d), os.path.join(root, d))
shutil.copytree(os.path.join(here, 'old-docs', 'code-syntax'), os.path.join(root, 'docs', 'code-syntax'))
shutil.copytree(os.path.join(repo, 'docs', 'checkers'), os.path.join(root, 'docs', 'checkers'))
os.makedirs(os.path.join(root, 'logs'), exist_ok=True)
if not os.path.exists(models):
    subprocess.run(['cmd', '/c', 'mklink', '/J', models, os.path.join(repo, 'models')], check=True, capture_output=True)
print('old-docs root ready:', root)
