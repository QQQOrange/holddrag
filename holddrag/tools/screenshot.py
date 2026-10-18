import ctypes, sys
from ctypes import wintypes

def capture(path, hwnd=None):
    user32 = ctypes.windll.user32
    gdi32 = ctypes.windll.gdi32
    if hwnd:
        r = wintypes.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(r))
        x, y, w, h = r.left, r.top, r.right - r.left, r.bottom - r.top
    else:
        x = user32.GetSystemMetrics(76)
        y = user32.GetSystemMetrics(77)
        w = user32.GetSystemMetrics(78)
        h = user32.GetSystemMetrics(79)
    hdc_screen = user32.GetDC(0)
    hdc_mem = gdi32.CreateCompatibleDC(hdc_screen)
    hbmp = gdi32.CreateCompatibleBitmap(hdc_screen, w, h)
    gdi32.SelectObject(hdc_mem, hbmp)
    gdi32.BitBlt(hdc_mem, 0, 0, w, h, hdc_screen, x, y, 0x00CC0020)
    # save BMP manually
    import struct
    class BITMAPINFOHEADER(ctypes.Structure):
        _fields_ = [("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long), ("biHeight", ctypes.c_long),
                    ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
                    ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", ctypes.c_long), ("biYPelsPerMeter", ctypes.c_long),
                    ("biClrUsed", wintypes.DWORD), ("biClrImportant", wintypes.DWORD)]
    bih = BITMAPINFOHEADER()
    bih.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bih.biWidth = w
    bih.biHeight = -h
    bih.biPlanes = 1
    bih.biBitCount = 32
    bih.biCompression = 0
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(hdc_mem, hbmp, 0, h, buf, ctypes.byref(bih), 0)
    row = w * 4
    with open(path, 'wb') as f:
        f.write(b'BM')
        f.write(struct.pack('<IHHI', 54 + w*h*4, 0, 0, 54))
        f.write(struct.pack('<IiiHHIIiiII', 40, w, h, 1, 32, 0, w*h*4, 0, 0, 0, 0))
        # BMP bottom-up: write rows reversed; also convert BGRA->BGR is fine (alpha dropped)
        for i in range(h-1, -1, -1):
            line = buf.raw[i*row:(i+1)*row]
            f.write(line)
    gdi32.DeleteObject(hbmp)
    gdi32.DeleteDC(hdc_mem)
    user32.ReleaseDC(0, hdc_screen)
    print('saved', path, w, h)

def find_window(title_part):
    user32 = ctypes.windll.user32
    found = []
    WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def cb(hwnd, lp):
        if user32.IsWindowVisible(hwnd):
            ln = user32.GetWindowTextLengthW(hwnd)
            if ln > 0:
                b = ctypes.create_unicode_buffer(ln + 1)
                user32.GetWindowTextW(hwnd, b, ln + 1)
                if title_part in b.value:
                    found.append(hwnd)
        return True
    user32.EnumWindows(WNDENUMPROC(cb), 0)
    return found[0] if found else None

if __name__ == '__main__':
    out = sys.argv[1]
    title = sys.argv[2] if len(sys.argv) > 2 else None
    hwnd = find_window(title) if title else None
    capture(out, hwnd)
