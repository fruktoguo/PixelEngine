#include <RmlUi/Core.h>
#include <RmlUi/Core/SystemInterface.h>
#include <RmlUi_Renderer_GL3.h>
#include <RmlUi_Include_GL3.h>

#include <chrono>
#include <cstdint>
#include <memory>
#include <string>

#if defined(_WIN32)
  #define PE_UI_NATIVE_API extern "C" __declspec(dllexport)
#else
  #define PE_UI_NATIVE_API extern "C" __attribute__((visibility("default")))
#endif

namespace
{
constexpr int32_t ApiVersion = 1;

using PeUiGetProcAddress = void* (*)(void* user, const char* name);

struct PeUiRenderer
{
    std::unique_ptr<RenderInterface_GL3> renderer;
};

struct PeUiGlResolver
{
    PeUiGetProcAddress resolver;
    void* user;
};

GLADapiproc LoadGlFromHost(void* user, const char* name)
{
    PeUiGlResolver* state = static_cast<PeUiGlResolver*>(user);
    if (state == nullptr || state->resolver == nullptr)
    {
        return nullptr;
    }

    return reinterpret_cast<GLADapiproc>(state->resolver(state->user, name));
}

class PeUiSystemInterface final : public Rml::SystemInterface
{
public:
    double GetElapsedTime() override
    {
        using Clock = std::chrono::steady_clock;
        const Clock::duration elapsed = Clock::now() - started;
        return std::chrono::duration<double>(elapsed).count();
    }

    bool LogMessage(Rml::Log::Type, const Rml::String&) override
    {
        return true;
    }

private:
    std::chrono::steady_clock::time_point started = std::chrono::steady_clock::now();
};

PeUiSystemInterface g_systemInterface;
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

PE_UI_NATIVE_API int32_t peui_native_load_gl(PeUiGetProcAddress resolver, void* user, int32_t* out_major, int32_t* out_minor)
{
    if (resolver == nullptr)
    {
        return 0;
    }

    PeUiGlResolver state{resolver, user};
    const int version = gladLoadGLUserPtr(LoadGlFromHost, &state);
    if (version == 0)
    {
        return 0;
    }

    if (out_major != nullptr)
    {
        *out_major = GLAD_VERSION_MAJOR(version);
    }

    if (out_minor != nullptr)
    {
        *out_minor = GLAD_VERSION_MINOR(version);
    }

    return 1;
}

PE_UI_NATIVE_API PeUiRenderer* peui_native_create_renderer(int32_t width, int32_t height)
{
    if (width <= 0 || height <= 0)
    {
        return nullptr;
    }

    auto instance = std::make_unique<PeUiRenderer>();
    instance->renderer = std::make_unique<RenderInterface_GL3>();
    if (!*instance->renderer)
    {
        return nullptr;
    }

    instance->renderer->SetViewport(width, height);
    Rml::SetSystemInterface(&g_systemInterface);
    Rml::SetRenderInterface(instance->renderer.get());
    return instance.release();
}

PE_UI_NATIVE_API void peui_native_destroy_renderer(PeUiRenderer* renderer)
{
    Rml::SetRenderInterface(nullptr);
    delete renderer;
}

PE_UI_NATIVE_API void peui_native_renderer_set_viewport(PeUiRenderer* renderer, int32_t width, int32_t height)
{
    if (renderer == nullptr || renderer->renderer == nullptr || width <= 0 || height <= 0)
    {
        return;
    }

    renderer->renderer->SetViewport(width, height);
}
