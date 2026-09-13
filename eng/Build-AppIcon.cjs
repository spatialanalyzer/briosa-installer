// Optional asset-authoring tool. Normal .NET builds use the committed ICO.
// Usage: node eng/Build-AppIcon.cjs [absolute path to the sharp module]
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const sharp = require(process.argv[2] || 'sharp');
const root = path.resolve(__dirname, '..', 'src/Briosa.Installer.App/Assets');
const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

async function main() {
  const sources = {};
  const frames = [];
  for (const size of sizes) {
    const name = size <= 32 ? 'favicon.svg' : 'app-icon.svg';
    const bytes = fs.readFileSync(path.join(root, 'Brand/icons', name));
    sources[`icons/${name}`] = sha256(bytes);
    // The bounding box is centered upstream, but the filled planes' centroid is
    // ~54 units below the tile's center. Move the intact symbol, not the tile.
    // Preserve the approved small-size optical geometry, fills and orientation.
    const svg = bytes.toString('utf8').replace('<path ', '<g transform="translate(0 -54)"><path ')
      .replace('</svg>', '</g></svg>');
    frames.push(await sharp(Buffer.from(svg), { density: 192 }).resize(size, size).png().toBuffer());
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
    source: 'spatialanalyzer/briosa-brand@v1',
    commit: '0a86718f66164e4a773bea37f738888a57c6bce0',
    inputs: sources,
    modification: 'Translate the complete three-plane symbol by (0, -54) in the 1024-unit canvas for optical vertical centering. Preserve the background tile, shapes, colors and small-size optical variant.',
    generator: 'eng/Build-AppIcon.cjs', renderer: { sharp: sharp.versions.sharp, vips: sharp.versions.vips },
    sizes, sha256: sha256(icon),
  };
  fs.writeFileSync(path.join(output, 'derivation.json'), JSON.stringify(provenance, null, 2) + '\n');
  console.log(`Generated ${sizes.length} icon frames; SHA-256 ${provenance.sha256}`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
