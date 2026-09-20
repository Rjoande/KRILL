"""Generates the plain-text README.txt and LICENSE.txt that ship INSIDE the
payload (GameData/KRILL/), from README.md and LICENSE at the repo root.
Plain text on purpose: a player opening the mod folder gets something Notepad
renders cleanly, no Markdown syntax, no links to files that only exist in the
repo.

Conversion rules (Markdown -> text):
  - "# Title" -> title line + "=" underline; "## Section" -> section + "-"
  - **bold**, *italic*, `code` markers stripped
  - image lines dropped, [text](http...) -> "text (http...)", links to files
    in the repo -> just their text
  - PAYLOAD_EDITS below rewrites the few sentences that only make sense to
    someone reading the repo rather than the unzipped mod folder
  - paragraphs and bullets re-wrapped at WIDTH columns, bullets with a
    hanging indent (nested bullets keep their extra indent)
  - CRLF line endings, UTF-8 without BOM (Windows Notepad friendly)

Run from anywhere:  python dev/make-release-txt.py
Re-run after every README.md change.
"""
import io
import os
import re
import textwrap

WIDTH = 78
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PAYLOAD = os.path.join(ROOT, "GameData", "KRILL")

# Sentences that address a reader of the GitHub page and have to address a
# player who unzipped the mod instead. Applied to the source before anything
# else; a miss here is silent, so keep them verbatim copies of README.md.
PAYLOAD_EDITS = [
    ("Copy the contents of this repository into your `GameData` folder",
     "Extract this archive's contents into your `GameData` folder"),
    # Next to this file the licence is LICENSE.txt, not the repo's LICENSE.
    ("[MIT](LICENSE).", "MIT. See LICENSE.txt."),
]

IMAGE_LINE = re.compile(r"^\s*!\[[^\]]*\]\([^)]*\)\s*$")
LINK = re.compile(r"\[([^\]]+)\]\(([^)]+)\)")


def delink(text):
    """[text](http://x) -> "text (http://x)"; a link to a repo file -> text."""
    def one(m):
        label, target = m.group(1), m.group(2)
        return "%s (%s)" % (label, target) if target.startswith("http") else label
    return LINK.sub(one, text)


def strip_inline(text):
    text = delink(text)
    text = re.sub(r"\*\*(.+?)\*\*", r"\1", text)
    text = re.sub(r"(?<!\w)\*(?!\s)(.+?)(?<!\s)\*(?!\w)", r"\1", text)
    text = text.replace("`", "")
    return text


def wrap(text, **kw):
    return textwrap.wrap(text, WIDTH, break_on_hyphens=False, **kw)


def flush_para(out, para):
    if para:
        blank_before(out)
        out.extend(wrap(strip_inline(" ".join(para))))
        out.append("")
        para.clear()


def blank_before(out):
    """A bullet list never emits a trailing blank; whatever follows it must."""
    if out and out[-1]:
        out.append("")


def convert_readme(md):
    out = []
    para = []
    for raw in md.splitlines():
        line = raw.rstrip()
        if not line.strip() or IMAGE_LINE.match(line):
            flush_para(out, para)
            continue
        if line.startswith("# "):
            flush_para(out, para)
            blank_before(out)
            title = strip_inline(line[2:].strip())
            out += [title, "=" * len(title), ""]
            continue
        if line.startswith("## "):
            flush_para(out, para)
            blank_before(out)
            title = strip_inline(line[3:].strip())
            out += [title, "-" * len(title), ""]
            continue
        m = re.match(r"^(\s*)- (.*)$", line)
        if m:
            flush_para(out, para)
            indent = len(m.group(1))
            body = strip_inline(m.group(2))
            out += wrap(body,
                        initial_indent=" " * indent + "- ",
                        subsequent_indent=" " * (indent + 2))
            continue
        para.append(line.strip())
    flush_para(out, para)
    while out and not out[-1]:
        out.pop()
    return out


def write_crlf(path, lines):
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write("\r\n".join(lines) + "\r\n")


def main():
    with io.open(os.path.join(ROOT, "README.md"), encoding="utf-8") as f:
        md = f.read()
    for old, new in PAYLOAD_EDITS:
        if old not in md:
            raise SystemExit("make-release-txt: README.md no longer contains %r "
                             "- update PAYLOAD_EDITS" % old)
        md = md.replace(old, new)
    readme = convert_readme(md)
    with io.open(os.path.join(ROOT, "LICENSE"), encoding="utf-8") as f:
        license_lines = [l.rstrip("\r\n") for l in f.read().splitlines()]
    write_crlf(os.path.join(PAYLOAD, "README.txt"), readme)
    write_crlf(os.path.join(PAYLOAD, "LICENSE.txt"), license_lines)
    print("wrote GameData/KRILL/README.txt (%d lines) and LICENSE.txt (%d lines)"
          % (len(readme), len(license_lines)))


if __name__ == "__main__":
    main()
