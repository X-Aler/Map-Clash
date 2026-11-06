"""Rebuild Map Clash's geographic assets from the committed public-domain sources.

Run from the project root: py Tools/MapData/build_world_map.py
Requires Python 3, numpy and Pillow. No data is downloaded by this script.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
import struct
import zipfile

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
SOURCES = ROOT / "Tools/MapData/Sources"
OUTPUT = ROOT / "Assets/_Project/Maps"
GENERATED = ROOT / "Tools/MapData/Generated"
MASK_WIDTH, MASK_HEIGHT = 4096, 2048
GRID_WIDTH, GRID_HEIGHT = 2048, 1024


def read_polygon_rings(path: Path):
    """Read ESRI Polygon rings directly; retain coordinates and ring winding."""
    with zipfile.ZipFile(path) as archive:
        data = archive.read(next(name for name in archive.namelist() if name.endswith(".shp")))
    offset = 100
    while offset < len(data):
        _, word_count = struct.unpack_from(">2i", data, offset)
        offset += 8
        record = memoryview(data)[offset:offset + word_count * 2]
        offset += word_count * 2
        shape_type = struct.unpack_from("<i", record)[0]
        if shape_type == 0:
            continue
        if shape_type != 5:
            raise ValueError(f"Expected polygon; got ESRI shape type {shape_type}")
        part_count, point_count = struct.unpack_from("<2i", record, 36)
        starts = list(struct.unpack_from(f"<{part_count}i", record, 44)) + [point_count]
        points = np.frombuffer(record, dtype="<f8", count=point_count * 2, offset=44 + part_count * 4).reshape(-1, 2)
        yield [points[starts[i]:starts[i + 1]].copy() for i in range(part_count)]


def rasterize(polygons, width: int, height: int):
    """Even/odd scan conversion at exact geographic cell centers.

    A half-open edge rule counts vertices once. Every ring in a feature participates
    in the same parity test, preserving holes. No dilation/closing connects islands.
    Raster rows are north to south for PNG; runtime serialization flips them.
    """
    result = np.zeros((height, width), dtype=np.bool_)
    for rings in polygons:
        transformed = []
        minimum_y, maximum_y = height, 0
        for ring in rings:
            points = np.empty_like(ring)
            points[:, 0] = (ring[:, 0] + 180.0) * width / 360.0
            points[:, 1] = (90.0 - ring[:, 1]) * height / 180.0
            transformed.append(points)
            minimum_y = min(minimum_y, max(0, int(np.ceil(points[:, 1].min() - 0.5))))
            maximum_y = max(maximum_y, min(height, int(np.ceil(points[:, 1].max() - 0.5))))
        # Most detailed coastline edges lie entirely between two row centers.
        # Bucket only the edges that cross a center instead of rescanning every
        # coastline vertex for every raster row.
        row_crossings = [[] for _ in range(maximum_y - minimum_y)]
        for points in transformed:
            following = np.roll(points, -1, axis=0)
            nonhorizontal = points[:, 1] != following[:, 1]
            starts, ends = points[nonhorizontal], following[nonhorizontal]
            first_rows = np.maximum(minimum_y, np.ceil(np.minimum(starts[:, 1], ends[:, 1]) - 0.5).astype(np.int32))
            stop_rows = np.minimum(maximum_y, np.ceil(np.maximum(starts[:, 1], ends[:, 1]) - 0.5).astype(np.int32))
            useful = first_rows < stop_rows
            for start, end, first_row, stop_row in zip(starts[useful], ends[useful], first_rows[useful], stop_rows[useful]):
                slope = (end[0] - start[0]) / (end[1] - start[1])
                for y in range(first_row, stop_row):
                    row_crossings[y - minimum_y].append(float(start[0] + (y + 0.5 - start[1]) * slope))
        for y in range(minimum_y, maximum_y):
            crossings = row_crossings[y - minimum_y]
            crossings.sort()
            if len(crossings) % 2:
                raise ValueError(f"Unpaired polygon intersections in raster row {y}")
            for left, right in zip(crossings[0::2], crossings[1::2]):
                start_x = max(0, int(np.ceil(left - 0.5)))
                stop_x = min(width, int(np.ceil(right - 0.5)))
                result[y, start_x:stop_x] = True
    return result


def save_svg(land, lakes):
    def geometry(polygons):
        for rings in polygons:
            pieces = []
            for ring in rings:
                points = [(float(x + 180.0) * MASK_WIDTH / 360.0, float(90.0 - y) * MASK_HEIGHT / 180.0) for x, y in ring]
                pieces.append("M" + "L".join(f"{x:.3f},{y:.3f}" for x, y in points) + "Z")
            yield '<path d="' + "".join(pieces) + '"/>'

    # This exact source geometry has over a million coordinates. Keep it outside
    # Assets: importing it through Unity's UI SVG importer blocks the Editor.
    GENERATED.mkdir(parents=True, exist_ok=True)
    target = GENERATED / "WorldCoastline.svg"
    temporary = GENERATED / "WorldCoastline.svg.tmp"
    with temporary.open("w", encoding="utf-8", newline="\n") as file:
        file.write(f'<svg xmlns="http://www.w3.org/2000/svg" width="{MASK_WIDTH}" height="{MASK_HEIGHT}" viewBox="0 0 {MASK_WIDTH} {MASK_HEIGHT}">\n')
        file.write('<rect width="4096" height="2048" fill="#182936"/>\n<g fill="#b5b5a4" fill-rule="evenodd">\n')
        file.write("\n".join(geometry(land)))
        file.write('\n</g><g fill="#182936" fill-rule="evenodd">\n')
        file.write("\n".join(geometry(lakes)))
        file.write("\n</g></svg>\n")
    temporary.replace(target)


def read_elevations(width: int, height: int):
    # Pillow's bundled libtiff decodes NOAA's GeoTIFF without a GDAL dependency.
    Image.MAX_IMAGE_PIXELS = None
    with Image.open(SOURCES / "ETOPO_2022_v1_60s_N90W180_surface.tif") as image:
        if image.size != (21600, 10800):
            raise ValueError(f"Unexpected ETOPO60s dimensions: {image.size}")
        # ETOPO2022 is cell registered. Nearest center samples preserve the exact
        # authored 500/1500m thresholds instead of averaging across coastlines.
        return np.asarray(image.resize((width, height), Image.Resampling.NEAREST), dtype=np.float32)


def classify(land, elevations):
    terrain = np.zeros(land.shape, dtype=np.uint8)
    terrain[land] = 1
    terrain[land & (elevations >= 500)] = 2
    terrain[land & (elevations >= 1500)] = 3
    return terrain


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as file:
        while block := file.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "Resources").mkdir(exist_ok=True)
    land = list(read_polygon_rings(SOURCES / "ne_10m_land.zip"))
    lakes = list(read_polygon_rings(SOURCES / "ne_10m_lakes.zip"))
    print("Rasterizing Natural Earth land and lakes at source-mask resolution", flush=True)
    land_mask = rasterize(land, MASK_WIDTH, MASK_HEIGHT)
    lake_mask = rasterize(lakes, MASK_WIDTH, MASK_HEIGHT)
    land_mask &= ~lake_mask
    Image.fromarray(land_mask.astype(np.uint8) * 255).save(OUTPUT / "LandMask.png")
    Image.fromarray((~land_mask).astype(np.uint8) * 255).save(OUTPUT / "WaterMask.png")
    Image.fromarray(lake_mask.astype(np.uint8) * 255).save(OUTPUT / "LakeMask.png")
    save_svg(land, lakes)
    print("Sampling ETOPO2022 and classifying terrain", flush=True)
    elevations = read_elevations(MASK_WIDTH, MASK_HEIGHT)
    Image.fromarray(classify(land_mask, elevations)).save(OUTPUT / "TerrainMask.png")
    print("Rasterizing exact runtime cell centers", flush=True)
    grid_land = rasterize(land, GRID_WIDTH, GRID_HEIGHT) & ~rasterize(lakes, GRID_WIDTH, GRID_HEIGHT)
    terrain = classify(grid_land, read_elevations(GRID_WIDTH, GRID_HEIGHT))
    land_count = int(grid_land.sum())
    binary = OUTPUT / "Resources/WorldTerrain.bytes"
    binary.write_bytes(struct.pack("<4s4i", b"MCMP", 1, GRID_WIDTH, GRID_HEIGHT, land_count) + np.flipud(terrain).tobytes())
    palette = np.array([[24, 41, 54], [181, 181, 164], [145, 151, 128], [110, 122, 115]], dtype=np.uint8)
    Image.fromarray(palette[terrain]).save(OUTPUT / "WorldPreview.png")
    manifest = {
        "projection": "EPSG:4326 equirectangular; -180..180 longitude, -90..90 latitude",
        "mask_size": [MASK_WIDTH, MASK_HEIGHT], "grid_size": [GRID_WIDTH, GRID_HEIGHT],
        "runtime_origin": "southwest; x east, y north; row-major",
        "rasterization": "Exact center even-odd scan conversion; no morphological edits; lakes subtract land",
        "elevation_sampling": "Nearest cell-center ETOPO2022 Ice Surface 60 arc-second",
        "terrain_thresholds_m": {"plains": "<500", "highlands": "500..1499", "mountains": ">=1500"},
        "total_land_tiles": land_count,
        "terrain_counts": np.bincount(terrain.ravel(), minlength=4).tolist(),
        "source_sha256": {path.name: sha256(path) for path in sorted(SOURCES.glob("*")) if path.suffix in (".zip", ".tif")},
        "output_sha256": {path.name: sha256(path) for path in sorted(OUTPUT.glob("*")) if path.is_file() and path.suffix == ".png"},
        "vector_sha256": {"WorldCoastline.svg": sha256(GENERATED / "WorldCoastline.svg")},
        "runtime_sha256": sha256(binary),
    }
    (SOURCES.parent / "map_manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2), flush=True)


if __name__ == "__main__":
    main()
