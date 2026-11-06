# Map source licenses

Downloaded and verified on 2026-11-05.

## Natural Earth

Land 1:10m version **5.1.1**, lakes 1:10m version **5.0.0**.
The original ZIPs include shapefile geometry, WGS84 projection, version files,
attribute records and author README files. README, version and projection files
are also extracted beside them for convenient inspection.

Natural Earth releases its vector and raster map data into the **Public Domain**,
allowing modification and commercial redistribution without a permission request.
Made with Natural Earth. Authors include Tom Patterson, Nathaniel Vaughn Kelso
and the Natural Earth contributors.

- Terms: https://www.naturalearthdata.com/about/terms-of-use/
- Land: https://naturalearth.s3.amazonaws.com/10m_physical/ne_10m_land.zip
- Lakes: https://naturalearth.s3.amazonaws.com/10m_physical/ne_10m_lakes.zip

## NOAA NCEI ETOPO 2022

**ETOPO 2022 v1, Ice Surface, 60 arc-second**, single global GeoTIFF,
`ETOPO_2022_v1_60s_N90W180_surface.tif` (465,969,062 bytes).
The file is 21,600 × 10,800 float32 elevations, EPSG:4326 horizontal coordinates,
EGM2008 heights in meters, cell registered with northwest edge at -180°, +90°
and pixel spacing 1/60°.

NOAA's official metadata identifies the license as **CC0-1.0**: NOAA waives
potential copyright and related rights worldwide. The dataset is not suitable
for navigation; NOAA provides it without warranty. This project uses it solely
for game terrain classification.

Citation: NOAA National Centers for Environmental Information. 2022: ETOPO 2022
15 Arc-Second Global Relief Model. NOAA National Centers for Environmental
Information. https://doi.org/10.25921/fd45-gt74 . Accessed 2026-11-05.

- Product page: https://www.ncei.noaa.gov/products/etopo-global-relief-model
- License/metadata: https://www.ncei.noaa.gov/metadata/geoportal/rest/metadata/item/gov.noaa.ngdc.mgg.dem:etopo_2022/html
- Original data: https://www.ngdc.noaa.gov/mgg/global/relief/ETOPO2022/data/60s/60s_surface_elev_gtif/ETOPO_2022_v1_60s_N90W180_surface.tif

Source and generated-file SHA-256 hashes are recorded in `../map_manifest.json`.
