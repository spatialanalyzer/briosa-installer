// Optional asset-authoring tool. Normal .NET builds use the committed ICO.
// Usage: node eng/Build-AppIcon.cjs [absolute path to the sharp module]
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const sharp = require(process.argv[2] || 'sharp');
const root = path.resolve(__dirname, '..', 'src/Briosa.Installer.App/Assets');
const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
// The brand pack's own split: the favicon's tighter fit up to 32 px, the app icon above.
const input = size => size <= 32 ? 'icons/favicon.svg' : 'icons/app-icon.svg';

async function main() {
  const bytes = {};
  for (const name of new Set(sizes.map(input))) bytes[name] = fs.readFileSync(path.join(root, 'Brand', name));
  const frames = [];
  for (const size of sizes) {
    // Both sources are square void tiles with the small-cut mark; render each
    // unmodified at the native frame size, keeping its transparent rounded corners.
    frames.push(await sharp(bytes[input(size)], { density: 192 }).resize(size, size, {
      fit: 'contain', position: 'centre', background: { r: 0, g: 0, b: 0, alpha: 0 },
    }).png().toBuffer());
  }
  // PNG-compressed, 32-bit ICO frames; native sizes avoid Windows upscaling 16 px.
  const header = Buffer.alloc(6 + sizes.length * 16);
  header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  frames.forEach((bytes, index) => {
    const entry = 6 + index * 16, size = sizes[index];
    header[entry] = header[entry + 1] = size === 256 ? 0 : size;
    header.writeUInt16LE(1, entry + 4); header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(bytes.length, entry + 8); header.writeUInt32LE(offset, entry + 12);
    offset += bytes.length;
  });
  const icon = Buffer.concat([header, ...frames]);
  const output = path.join(root, 'AppIcon'); fs.mkdirSync(output, { recursive: true });
  fs.writeFileSync(path.join(output, 'briosa.ico'), icon);
  const provenance = {
    source: 'spatialanalyzer/briosa-brand@f08fc75acdc36ebc4e9ac6167523efbbeb383f44',
    commit: 'f08fc75acdc36ebc4e9ac6167523efbbeb383f44',
    inputs: Object.fromEntries(Object.entries(bytes).map(([name, data]) => [name, sha256(data)])),
    frames: Object.fromEntries(Object.keys(bytes).map(name => [name, sizes.filter(size => input(size) === name)])),
    modification: 'Rasterize the unmodified void-tile app icon into square ICO frames: icons/favicon.svg, the tighter fit, at 32 px and below and icons/app-icon.svg above, as in the brand pack\'s icon exports. Preserve the geometry, colors, tile, edge and transparent rounded corners. No recoloring, cropping or optical translation.',
    generator: 'eng/Build-AppIcon.cjs', renderer: { sharp: sharp.versions.sharp, vips: sharp.versions.vips },
    sizes, sha256: sha256(icon),
  };
  fs.writeFileSync(path.join(output, 'derivation.json'), JSON.stringify(provenance, null, 2) + '\n');
  console.log(`Generated ${sizes.length} icon frames; SHA-256 ${provenance.sha256}`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
