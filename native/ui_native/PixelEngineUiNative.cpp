#include <RmlUi/Core.h>

#include <cstdint>
#include <string>

#if defined(_WIN32)
  #define PE_UI_NATIVE_API extern "C" __declspec(dllexport)
#else
  #define PE_UI_NATIVE_API extern "C" __attribute__((visibility("default")))
#endif

namespace
{
constexpr int32_t ApiVersion = 1;
}

PE_UI_NATIVE_API int32_t peui_native_get_api_version()
{
    return ApiVersion;
}

PE_UI_NATIVE_API const char* peui_native_get_rmlui_version()
{
    static const std::string version = Rml::GetVersion();
    return version.c_str();
}
