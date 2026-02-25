#!/usr/bin/env python3
"""Convert a screenshot PNG to 1-bit using Floyd-Steinberg dithering.

Usage:
    python tools/dither.py <input.png> [output.png]

If output is omitted, saves as <input>-1bit.png.
"""

import sys
from pathlib import Path
from PIL import Image


def dither(input_path: Path, output_path: Path) -> None:
    Image.open(input_path).convert("L").convert("1", dither=Image.Dither.FLOYDSTEINBERG).save(output_path)
    print(f"Saved {output_path}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    src = Path(sys.argv[1])
    dst = Path(sys.argv[2]) if len(sys.argv) > 2 else src.with_stem(src.stem + "-1bit")
    dither(src, dst)
