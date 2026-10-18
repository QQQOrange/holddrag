import ctypes, sys
from ctypes import wintypes

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32

def find_window(title_part):
    found = []
    WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def cb(hwnd, lp):
        if user32.IsWindowVisible(hwnd):
            ln = user32.GetWindowTextLengthW(hwnd)
            if ln > 0:
                b = ctypes.create_unicode_buffer(ln + 1)
                user32.GetWindowTextW(hwnd, b, ln + 1)
                if title_part in b.value:
                    found.append((hwnd, b.value))
        return True
    user32.EnumWindows(WNDENUMPROC(cb), 0)
    return found

def capture(hwnd, path):
    r = wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    hdc_screen = user32.GetDC(0)
    hdc_mem = gdi32.CreateCompatibleDC(hdc_screen)
    hbmp = gdi32.CreateCompatibleBitmap(hdc_screen, w, h)
    old = gdi32.SelectObject(hdc_mem, hbmp)
    # PW_RENDERFULLCONTENT = 2
    ok = user32.PrintWindow(hwnd, hdc_mem, 2)
    import struct
    class BIH(ctypes.Structure):
        _fields_ = [("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long), ("biHeight", ctypes.c_long),
                    ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
                    ("biSizeImage", wintypes.DWORD), ("biX", ctypes.c_long), ("biY", ctypes.c_long),
                    ("biClrUsed", wintypes.DWORD), ("biClrImp", wintypes.DWORD)]
    bih = BIH(); bih.biSize = 40; bih.biWidth = w; bih.biHeight = -h; bih.biPlanes = 1; bih.biBitCount = 32
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(hdc_mem, hbmp, 0, h, buf, ctypes.byref(bih), 0)
    with open(path, 'wb') as f:
        f.write(b'BM')
        f.write(struct.pack('<IHHI', 54 + w*h*4, 0, 0, 54))
        f.write(struct.pack('<IiiHHIIiiII', 40, w, h, 1, 32, 0, w*h*4, 0, 0, 0, 0))
        row = w * 4
        for i in range(h-1, -1, -1):
            f.write(buf.raw[i*row:(i+1)*row])
    gdi32.SelectObject(hdc_mem, old)
    gdi32.DeleteObject(hbmp)
    gdi32.DeleteDC(hdc_mem)
    user32.ReleaseDC(0, hdc_screen)
    print('PrintWindow ok =', ok, 'size =', w, h)

if __name__ == '__main__':
    wins = find_window(sys.argv[2])
    print(wins)
    if wins:
        capture(wins[0][0], sys.argv[1])
