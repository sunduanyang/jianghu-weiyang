"""Subset Noto Serif CJK SC (SIL OFL 1.1) into the TrueType files the Unity client bundles.

Keeps GB2312 plus every character used by the engine, web client, Unity scripts and data,
drops OpenType layout tables (IMGUI does not use them) and converts CFF outlines to TrueType.

    python unity/build_fonts.py --source-dir /usr/share/fonts/opentype/noto

The source directory must contain NotoSerifCJK-Regular.ttc and NotoSerifCJK-Bold.ttc
(Debian/Ubuntu package fonts-noto-cjk, or the Noto CJK release on GitHub). Requires fontTools.
"""
import argparse
import glob
from pathlib import Path

from fontTools import subset
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTCollection, newTable

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'unity' / 'JianghuUnity' / 'Assets' / 'Resources' / 'Fonts'
SC_INDEX = 2   # face order in the CJK collections: JP, KR, SC, TC, HK
EXTRA = '　、。，．：；？！‘’“”「」『』《》〈〉【】〔〕…—～·・●○◎◇◆□■△▲▽▼▶›‹→←↑↓↗＋－×÷＝％￥①②③④⑤⑥⑦⑧⑨⑩✓'
SOURCES = ['jianghu/*.py', 'static/*.js', 'static/*.json', 'static/*.html', '*.md',
           'unity/JianghuUnity/Assets/Scripts/*.cs', 'unity/JianghuUnity/Assets/Resources/*.json',
           'prototypes/shadow-puppets/*.js']


def characters():
    chars = {chr(c) for c in range(0x20, 0x7f)}
    for b1 in range(0xA1, 0xF8):
        for b2 in range(0xA1, 0xFF):
            try:
                chars.add(bytes([b1, b2]).decode('gb2312'))
            except UnicodeDecodeError:
                pass
    for pattern in SOURCES:
        for f in glob.glob(str(ROOT / pattern)):
            chars |= set(Path(f).read_text(encoding='utf-8', errors='ignore'))
    chars |= set(EXTRA)
    return ''.join(sorted(c for c in chars if ord(c) >= 0x20 and c != '﻿'))


def to_truetype(font):
    glyphs, order = font.getGlyphSet(), font.getGlyphOrder()
    glyf = {}
    for name in order:
        pen = TTGlyphPen(glyphs)
        glyphs[name].draw(Cu2QuPen(pen, 1.0, reverse_direction=True))
        glyf[name] = pen.glyph()
    font['loca'] = newTable('loca')
    table = newTable('glyf')
    table.glyphOrder, table.glyphs = order, glyf
    font['glyf'] = table
    del font['CFF ']
    font.sfntVersion = '\x00\x01\x00\x00'
    maxp = font['maxp']
    maxp.tableVersion = 0x00010000
    for attr in ('maxZones', 'maxTwilightPoints', 'maxStorage', 'maxFunctionDefs', 'maxInstructionDefs',
                 'maxStackElements', 'maxSizeOfInstructions', 'maxComponentElements', 'maxPoints', 'maxContours',
                 'maxCompositePoints', 'maxCompositeContours', 'maxComponentDepth'):
        setattr(maxp, attr, 0)
    maxp.maxZones = 1
    font['head'].glyphDataFormat = 0
    font['head'].indexToLocFormat = 1
    post = font['post']
    post.formatType, post.extraNames, post.mapping, post.glyphOrder = 2.0, [], {}, order


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-dir', default='/usr/share/fonts/opentype/noto')
    args = parser.parse_args()
    text = characters()
    OUT.mkdir(parents=True, exist_ok=True)
    for weight in ('Regular', 'Bold'):
        font = TTCollection(str(Path(args.source_dir) / f'NotoSerifCJK-{weight}.ttc')).fonts[SC_INDEX]
        options = subset.Options()
        options.layout_features = []
        options.drop_tables += ['GSUB', 'GPOS', 'GDEF', 'BASE', 'VORG', 'vhea', 'vmtx', 'DSIG']
        options.name_IDs = ['*']
        options.notdef_outline = True
        options.hinting = False
        options.desubroutinize = True
        subsetter = subset.Subsetter(options)
        subsetter.populate(text=text)
        subsetter.subset(font)
        to_truetype(font)
        path = OUT / f'NotoSerifSC-{weight}.ttf'
        font.save(path)
        print(path.relative_to(ROOT), len(font.getGlyphOrder()), 'glyphs', path.stat().st_size // 1024, 'KB')


if __name__ == '__main__':
    main()
