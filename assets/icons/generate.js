// このフォルダの各 SVG を 128x128 の PNG へラスタライズし、
// PluginInfo.cs へ貼り付ける base64 文字列を出力する。
//
//   npm install
//   node generate.js
//
// PNG のエンコードは自前で行う。ライブラリが吐く PNG は
// Unity の Texture2D.LoadImage が読めないことがあるため、
// Node 標準の zlib で素直に組み立てている。

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { Resvg } = require('@resvg/resvg-js');

// タイルビューのタイルが 120x110 のため、ツールバー用の 32 ではなく 128 で出す
const SIZE = 128;

const ICONS = ['BgObject'];

const CRC_TABLE = (() => {
    const table = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) {
            c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        }
        table[n] = c;
    }
    return table;
})();

function crc32(buffer) {
    let c = -1;
    for (let i = 0; i < buffer.length; i++) {
        c = CRC_TABLE[(c ^ buffer[i]) & 0xff] ^ (c >>> 8);
    }
    return (c ^ -1) >>> 0;
}

function chunk(type, data) {
    const head = Buffer.alloc(8);
    head.writeUInt32BE(data.length, 0);
    head.write(type, 4, 'ascii');
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])), 0);
    return Buffer.concat([head, data, crc]);
}

function encodePng(rgba, width, height) {
    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(width, 0);
    ihdr.writeUInt32BE(height, 4);
    ihdr[8] = 8;  // ビット深度
    ihdr[9] = 6;  // カラータイプ: RGBA
    // 圧縮方式・フィルタ方式・インタレースはすべて既定 (0)

    // 各走査線の先頭にフィルタタイプ 0 (フィルタなし) を付ける
    const stride = width * 4;
    const raw = Buffer.alloc((stride + 1) * height);
    for (let y = 0; y < height; y++) {
        raw[y * (stride + 1)] = 0;
        rgba.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
    }

    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk('IHDR', ihdr),
        chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
        chunk('IEND', Buffer.alloc(0)),
    ]);
}

for (const name of ICONS) {
    const svg = fs.readFileSync(path.join(__dirname, `${name}.svg`), 'utf8');
    const rendered = new Resvg(svg, { fitTo: { mode: 'width', value: SIZE } }).render();
    const png = encodePng(rendered.pixels, rendered.width, rendered.height);
    fs.writeFileSync(path.join(__dirname, `${name}.png`), png);
    console.log(`// ${name} (${png.length} bytes)`);
    console.log(`"${png.toString('base64')}",`);
    console.log();
}
