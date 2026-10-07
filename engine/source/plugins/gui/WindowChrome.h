#pragma once
#include <windows.h>
#include <dwmapi.h>
#include <cstdint>

namespace NcmaChrome {
// Presentation of the existing decorated window only; no subclass, hit-test or HWND export.
inline uint32_t Apply(HWND window, bool workspace) noexcept {
    if (!window) return 0;
    HIGHCONTRASTW contrast{}; contrast.cbSize = sizeof(contrast);
    if (!SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(contrast), &contrast, 0)) return 0;
    workspace = workspace && !(contrast.dwFlags & HCF_HIGHCONTRASTON);
    const BOOL dark = workspace ? TRUE : FALSE;
    const COLORREF caption = workspace ? RGB(9,19,31) : DWMWA_COLOR_DEFAULT;
    const COLORREF text = workspace ? RGB(223,233,243) : DWMWA_COLOR_DEFAULT;
    const COLORREF border = workspace ? RGB(43,67,90) : DWMWA_COLOR_DEFAULT;
    uint32_t applied = 0;
    // Unsupported attributes degrade individually without changing window styles/behavior.
    if (SUCCEEDED(DwmSetWindowAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(dark)))) applied |= 1;
    if (SUCCEEDED(DwmSetWindowAttribute(window, DWMWA_CAPTION_COLOR, &caption, sizeof(caption)))) applied |= 2;
    if (SUCCEEDED(DwmSetWindowAttribute(window, DWMWA_TEXT_COLOR, &text, sizeof(text)))) applied |= 4;
    if (SUCCEEDED(DwmSetWindowAttribute(window, DWMWA_BORDER_COLOR, &border, sizeof(border)))) applied |= 8;
    return applied;
}
}
