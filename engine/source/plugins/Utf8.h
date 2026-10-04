#pragma once
#include <cstdint>
#include <string>
namespace NcmaPlugin {
inline bool CopyUtf8(const uint8_t* bytes, uint32_t length, uint32_t maximum, std::string& result)
{
    if ((!bytes && length) || length > maximum) return false;
    for (uint32_t i = 0; i < length;) {
        uint32_t lead = bytes[i++], scalar = lead, extra = 0, minimum = 0;
        if (lead == 0) return false;
        if (lead < 0x80) continue;
        if ((lead & 0xE0) == 0xC0) { extra = 1; scalar = lead & 0x1F; minimum = 0x80; }
        else if ((lead & 0xF0) == 0xE0) { extra = 2; scalar = lead & 0x0F; minimum = 0x800; }
        else if ((lead & 0xF8) == 0xF0) { extra = 3; scalar = lead & 7; minimum = 0x10000; }
        else return false;
        if (extra > length - i) return false;
        for (uint32_t n = 0; n < extra; n++) {
            uint32_t next = bytes[i++]; if ((next & 0xC0) != 0x80) return false;
            scalar = (scalar << 6u) | (next & 0x3F);
        }
        if (scalar < minimum || scalar > 0x10FFFF || (scalar >= 0xD800 && scalar <= 0xDFFF)) return false;
    }
    result.assign(length ? reinterpret_cast<const char*>(bytes) : "", length); return true;
}
}
