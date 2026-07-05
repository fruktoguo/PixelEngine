#include <RmlUi/Core.h>
#include <RmlUi/Core/ElementDocument.h>
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
    Rml::Context* context = nullptr;
    std::string contextName;
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
bool g_rmlInitialised = false;
int32_t g_rendererCount = 0;
int32_t g_nextContextId = 1;
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

    if (g_rendererCount != 0)
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
    if (!g_rmlInitialised)
    {
        Rml::SetSystemInterface(&g_systemInterface);
        Rml::SetRenderInterface(instance->renderer.get());
        if (!Rml::Initialise())
        {
            Rml::SetRenderInterface(nullptr);
            Rml::SetSystemInterface(nullptr);
            return nullptr;
        }

        g_rmlInitialised = true;
    }

    instance->contextName = "pixelengine_ui_" + std::to_string(g_nextContextId++);
    instance->context = Rml::CreateContext(instance->contextName, Rml::Vector2i(width, height), instance->renderer.get());
    if (instance->context == nullptr)
    {
        Rml::Shutdown();
        g_rmlInitialised = false;
        Rml::SetRenderInterface(nullptr);
        Rml::SetSystemInterface(nullptr);
        return nullptr;
    }

    g_rendererCount++;
    return instance.release();
}

PE_UI_NATIVE_API void peui_native_destroy_renderer(PeUiRenderer* renderer)
{
    if (renderer != nullptr && renderer->context != nullptr)
    {
        Rml::RemoveContext(renderer->contextName);
        renderer->context = nullptr;
    }

    if (g_rendererCount > 0)
    {
        g_rendererCount--;
    }

    if (g_rendererCount == 0 && g_rmlInitialised)
    {
        Rml::Shutdown();
        g_rmlInitialised = false;
        Rml::SetRenderInterface(nullptr);
        Rml::SetSystemInterface(nullptr);
    }

    delete renderer;
}

PE_UI_NATIVE_API void peui_native_renderer_set_viewport(PeUiRenderer* renderer, int32_t width, int32_t height)
{
    if (renderer == nullptr || renderer->renderer == nullptr || width <= 0 || height <= 0)
    {
        return;
    }

    renderer->renderer->SetViewport(width, height);
    if (renderer->context != nullptr)
    {
        renderer->context->SetDimensions(Rml::Vector2i(width, height));
    }
}

PE_UI_NATIVE_API Rml::ElementDocument* peui_native_load_document_memory(
    PeUiRenderer* renderer,
    const char* document,
    int32_t document_length,
    const char* source_url)
{
    if (renderer == nullptr || renderer->context == nullptr || document == nullptr || document_length <= 0)
    {
        return nullptr;
    }

    Rml::String rml(document, static_cast<size_t>(document_length));
    Rml::String source = source_url == nullptr ? "[pixelengine document]" : source_url;
    return renderer->context->LoadDocumentFromMemory(rml, source);
}

PE_UI_NATIVE_API void peui_native_document_show(Rml::ElementDocument* document, int32_t modal)
{
    if (document == nullptr)
    {
        return;
    }

    document->Show(modal != 0 ? Rml::ModalFlag::Modal : Rml::ModalFlag::None);
}

PE_UI_NATIVE_API void peui_native_document_hide(Rml::ElementDocument* document)
{
    if (document != nullptr)
    {
        document->Hide();
    }
}

PE_UI_NATIVE_API void peui_native_document_close(Rml::ElementDocument* document)
{
    if (document != nullptr)
    {
        document->Close();
    }
}

PE_UI_NATIVE_API void peui_native_update(PeUiRenderer* renderer)
{
    if (renderer != nullptr && renderer->context != nullptr)
    {
        renderer->context->Update();
    }
}

PE_UI_NATIVE_API void peui_native_render(PeUiRenderer* renderer)
{
    if (renderer == nullptr || renderer->context == nullptr || renderer->renderer == nullptr)
    {
        return;
    }

    renderer->renderer->BeginFrame();
    renderer->context->Render();
    renderer->renderer->EndFrame();
}
