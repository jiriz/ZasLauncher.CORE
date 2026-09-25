"""Bundle Mach-O dependencies beside the adapter; never modify installed Homebrew libraries."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

root = Path(sys.argv[1]).resolve()
cache_file = root / '.dependencies.json'
cache = json.loads(cache_file.read_text()) if cache_file.exists() else {}
updated = {}
seen = {}

def output(*args):
    return subprocess.check_output(args, text=True)

def visit(source, destination):
    original = source.resolve()
    if destination.name in seen:
        if seen[destination.name] != original:
            raise RuntimeError(f'Conflicting native dependency: {destination.name}')
        return
    seen[destination.name] = original
    stat = original.stat()
    signature = [str(original), stat.st_mtime_ns, stat.st_size]
    rebuild = source == destination or cache.get(destination.name) != signature or not destination.exists()
    if source != destination and rebuild:
        shutil.copy2(original, destination)
        os.chmod(destination, 0o755)
    dependencies = [line.strip().split(' (compatibility')[0] for line in output('otool','-L',str(original)).splitlines()[2:]]
    for dep in dependencies:
        if dep.startswith(('/usr/lib/', '/System/Library/')): continue
        if not dep.startswith('/'):
            raise RuntimeError(f'Unresolved dependency {dep} in {source}')
        dependency = Path(dep)
        visit(dependency, root / dependency.name)
        if rebuild:
            subprocess.run(['install_name_tool','-change',dep,'@loader_path/'+dependency.name,str(destination)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
    if rebuild:
        subprocess.run(['install_name_tool','-id','@loader_path/'+destination.name,str(destination)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
        subprocess.run(['codesign','--force','--sign','-',str(destination)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
    updated[destination.name] = signature

visit(root/'libzasrdp.dylib', root/'libzasrdp.dylib')
cache_file.write_text(json.dumps(updated, indent=2)+'\n')
# Keep upstream licence files for the Homebrew formulae supplying bundled libraries.
licences = root / 'licenses'
licences.mkdir(exist_ok=True)
for source in set(seen.values()):
    parts = source.parts
    if 'Cellar' not in parts: continue
    index = parts.index('Cellar')
    formula_root = Path(*parts[:index+3])
    formula = parts[index+1]
    for item in formula_root.iterdir():
        if item.is_file() and item.name.upper().startswith(('LICENSE','LICENCE','COPYING','COPYRIGHT','NOTICE')):
            shutil.copy2(item, licences / (formula+'-'+item.name))
print(f'Bundled {len(seen)} native libraries ({sum((root/n).stat().st_size for n in seen)//1048576} MiB).')
