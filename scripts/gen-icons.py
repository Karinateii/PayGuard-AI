#!/usr/bin/env python3
"""Generate PWA icons for PayGuard AI"""
import struct, zlib

def create_png_icon(size, filename):
    width = height = size
    pixels = []
    cx, cy = width / 2, height / 2
    
    for y in range(height):
        row = []
        for x in range(width):
            nx = (x - cx) / (width / 2)
            ny = (y - cy) / (height / 2)
            
            shield_top = -0.85
            shield_bottom = 0.85
            shield_width_top = 0.75
            
            if ny < shield_top or ny > shield_bottom:
                row.extend([0, 0, 0, 0])
                continue
            
            progress = (ny - shield_top) / (shield_bottom - shield_top)
            if progress < 0.6:
                max_x = shield_width_top
            else:
                taper = (progress - 0.6) / 0.4
                max_x = shield_width_top * (1 - taper * 0.85)
            
            if abs(nx) > max_x:
                row.extend([0, 0, 0, 0])
                continue
            
            r, g, b = 30, 58, 95
            border_ratio = abs(nx) / max_x if max_x > 0 else 0
            is_border = border_ratio > 0.85 or progress < 0.05 or progress > 0.95
            
            if is_border:
                r, g, b = 30, 136, 229
            
            row.extend([r, g, b, 255])
        pixels.append(bytes(row))
    
    def make_chunk(chunk_type, data):
        chunk = chunk_type + data
        return struct.pack('>I', len(data)) + chunk + struct.pack('>I', zlib.crc32(chunk) & 0xFFFFFFFF)
    
    sig = b'\x89PNG\r\n\x1a\n'
    ihdr = struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)
    
    raw = b''
    for row_pixels in pixels:
        raw += b'\x00' + row_pixels
    
    compressed = zlib.compress(raw, 9)
    
    png = sig
    png += make_chunk(b'IHDR', ihdr)
    png += make_chunk(b'IDAT', compressed)
    png += make_chunk(b'IEND', b'')
    
    with open(filename, 'wb') as f:
        f.write(png)

base = 'src/PayGuardAI.Web/wwwroot/icons'
for size in [72, 96, 128, 144, 152, 192, 384, 512]:
    create_png_icon(size, f'{base}/icon-{size}.png')
    print(f'Created icon-{size}.png')
print('Done!')
