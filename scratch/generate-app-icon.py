"""Convert the application icon master to Windows ICOs (requires Pillow).

Run from any directory: python scratch/generate-app-icon.py
The tray icon and in-app logo artwork are maintained separately.
"""

from pathlib import Path

from PIL import Image, ImageOps


ROOT = Path(__file__).resolve().parents[1]
SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)


def main():
    with Image.open(ROOT / "assets/app-icon-master.png") as source:
        artwork = source.convert("RGBA")
    bounds = artwork.getbbox()
    if bounds is None:
        raise ValueError("Icon master is empty")
    artwork = artwork.crop(bounds)
    # Leave a small transparent margin so the round silhouette does not touch
    # the shortcut cell edges, especially at the smallest Windows DPI sizes.
    canvas = Image.new("RGBA", (1024, 1024))
    fitted = ImageOps.contain(artwork, (960, 960), Image.Resampling.LANCZOS)
    canvas.alpha_composite(fitted, ((1024 - fitted.width) // 2, (1024 - fitted.height) // 2))
    destinations = (ROOT / "WinPieGestures/app_icon.ico", ROOT / "assets/app_icon.ico")
    for destination in destinations:
        canvas.save(destination, format="ICO", sizes=[(size, size) for size in SIZES])
        with Image.open(destination) as result:
            if result.ico.sizes() != {(size, size) for size in SIZES}:
                raise ValueError(f"Incomplete icon frames: {destination}")
        print(destination.relative_to(ROOT))


if __name__ == "__main__":
    main()
