"""Renders the design (docs/design/source/*.html, exported from the design tool) to PNGs.

Each source is a bundle of pages. This unpacks every page to a scratch folder, renders it with
headless Edge, and saves docs/design/<part>/<nn>-<name>.png. Game and editor pages are cropped
to 1280x720, the game's window, so they line up with check.ps1 screenshots.

    py -3.12 docs/design/render.py            (every part)
    py -3.12 docs/design/render.py game       (one part)

Run it again after a new export replaces a source file.
"""
import base64, gzip, json, os, re, subprocess, sys, tempfile
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
# the page names in each bundle's order; a new page in an export gets a name here
NAMES = {
    "game": ["flow", "main-menu", "lobby", "character-creation", "portrait-crop", "options", "play", "party-inventory",
             "chest-or-shop", "spellbook", "conversation", "pause"],
    "editor": ["flow", "create-pipeline", "edit-projects", "import-from-book", "story-graph", "build-conflict", "map",
               "encounters", "dialogue", "cutscene", "compendium", "publish"],
    "web": ["flow", "home", "adventure-library", "adventure-page", "rankings", "profile", "rules-page", "compendium",
            "forums", "forum-thread", "download", "submit-adventure", "report-bug", "account"],
    "parts": ["wordmark", "wordmark-small", "chat-closed", "chat-open", "web-header", "web-footer", "editor-toolbar"],
}
SIZES = {"game": (1600, 1000), "editor": (1600, 1000), "parts": (1600, 1000), "web": (1440, 2600)}
CROP = {"game": (1280, 720), "editor": (1280, 720)}


def pages(part):
    text = open(os.path.join(HERE, "source", part + ".html"), encoding="utf-8").read()
    get = lambda kind: re.search(r'<script type="%s">(.*?)</script>' % re.escape(kind), text, re.S).group(1)
    manifest, order = json.loads(get("__bundler/manifest")), json.loads(get("__bundler/page_order"))
    for key in order:
        entry = manifest[key]
        data = base64.b64decode(entry["data"])
        yield (gzip.decompress(data) if entry.get("compressed") else data).decode("utf-8", "replace")


def render(part, scratch):
    names = NAMES[part]
    os.makedirs(os.path.join(HERE, part), exist_ok=True)
    for i, html in enumerate(pages(part)):
        name = "%02d-%s" % (i, names[i] if i < len(names) else "page")
        page = os.path.join(scratch, part + "-" + name + ".html")
        open(page, "w", encoding="utf-8").write(html)
        png = os.path.join(HERE, part, name + ".png")
        w, h = SIZES[part]
        subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--window-size=%d,%d" % (w, h),
                        "--virtual-time-budget=8000", "--screenshot=" + png, "file:///" + page.replace("\\", "/")],
                       timeout=90, capture_output=True)
        if part in CROP:
            Image.open(png).crop((0, 0) + CROP[part]).save(png)
        print(part + "/" + name + ".png")


if __name__ == "__main__":
    with tempfile.TemporaryDirectory() as scratch:
        for part in sys.argv[1:] or NAMES:
            render(part, scratch)
