"""Packs Shiki's browser extension for the Chrome Web Store (and Edge Add-ons).

The store gives the extension its own id, and won't take a manifest with a "key" in it, so the
key (which only fixes the id when it's added by hand from Shiki's folder) is left out here.
Run from the repo:  python build/pack-extension.py
Writes Releases/Shiki-extension-<version>.zip
"""
import json
import pathlib
import zipfile

root = pathlib.Path(__file__).resolve().parent.parent
source = root / "browser-extension"
manifest = json.loads((source / "manifest.json").read_text(encoding="utf-8"))
manifest.pop("key", None)
manifest["homepage_url"] = "https://github.com/yashkumar174/Shiki"

out = root / "Releases" / f"Shiki-extension-{manifest['version']}.zip"
out.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("manifest.json", json.dumps(manifest, indent=2) + "\n")
    for f in sorted(source.iterdir()):
        if f.name != "manifest.json" and f.is_file():
            z.write(f, f.name)
print(out, out.stat().st_size, "bytes")
