#include "util/StringUtil.hpp"

// <stringapiset.h> alone doesn't pull in the _AMD64_/_X86_ target-architecture macro <winnt.h>
// needs (that normally comes from <windows.h> itself, which brings in stringapiset.h among many
// other headers) - including the umbrella header directly avoids "No Target Architecture" (C1189).
#include <windows.h>

using namespace std;

namespace StringUtil {

auto utf8toUTF16(const string& str) -> wstring
{
    if (str.empty()) {
        return {};
    }

    const int size = MultiByteToWideChar(CP_UTF8, 0, str.data(), static_cast<int>(str.size()), nullptr, 0);
    wstring result(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, str.data(), static_cast<int>(str.size()), result.data(), size);
    return result;
}

auto utf16toUTF8(const wstring& str) -> string
{
    if (str.empty()) {
        return {};
    }

    const int size
        = WideCharToMultiByte(CP_UTF8, 0, str.data(), static_cast<int>(str.size()), nullptr, 0, nullptr, nullptr);
    string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, str.data(), static_cast<int>(str.size()), result.data(), size, nullptr, nullptr);
    return result;
}

} // namespace StringUtil
