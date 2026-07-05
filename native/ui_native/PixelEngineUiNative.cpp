#include <RmlUi/Core.h>
#include <RmlUi/Core/ElementDocument.h>
#include <RmlUi/Core/SystemInterface.h>
#include <RmlUi_Renderer_GL3.h>
#include <RmlUi_Include_GL3.h>

#include <chrono>
#include <algorithm>
#include <cstdio>
#include <cstdint>
#include <cstdlib>
#include <memory>
#include <string>
#include <vector>

#if defined(_WIN32)
  #define PE_UI_NATIVE_API extern "C" __declspec(dllexport)
#else
  #define PE_UI_NATIVE_API extern "C" __attribute__((visibility("default")))
#endif

namespace
{
constexpr int32_t ApiVersion = 1;
constexpr int32_t EventCapacity = 256;

using PeUiGetProcAddress = void* (*)(void* user, const char* name);

struct PeUiNativeValue
{
    int32_t kind;
    int32_t reserved;
    union
    {
        int64_t integer;
        double number;
    };
};

struct PeUiNativeEvent
{
    int32_t document;
    int32_t element;
    int32_t action;
    int32_t value_kind;
    int64_t integer;
    double number;
};

struct PeUiRenderer;

struct PeUiModelBinding
{
    Rml::ElementDocument* document;
    Rml::Element* element;
    int32_t documentHandle;
    int32_t pathHash;
    std::string path;
    PeUiNativeValue value;
};

class PeUiEventListener final : public Rml::EventListener
{
public:
    PeUiEventListener(PeUiRenderer* owner, int32_t documentHandle, int32_t elementHash, int32_t actionHash)
        : owner(owner), documentHandle(documentHandle), elementHash(elementHash), actionHash(actionHash)
    {
    }

    void ProcessEvent(Rml::Event&) override;

private:
    PeUiRenderer* owner;
    int32_t documentHandle;
    int32_t elementHash;
    int32_t actionHash;
};

struct PeUiEventBinding
{
    Rml::ElementDocument* document;
    Rml::Element* element;
    Rml::EventId eventId;
    std::unique_ptr<PeUiEventListener> listener;
};

struct PeUiRenderer
{
    std::unique_ptr<RenderInterface_GL3> renderer;
    Rml::Context* context = nullptr;
    std::string contextName;
    std::vector<PeUiModelBinding> modelBindings;
    std::vector<PeUiEventBinding> eventBindings;
    std::vector<PeUiNativeEvent> events;
    int32_t eventRead = 0;
    int32_t eventCount = 0;
    std::string lastError;
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

int32_t StableHashAscii(const std::string& value)
{
    constexpr uint32_t offset = 2166136261u;
    constexpr uint32_t prime = 16777619u;
    uint32_t hash = offset;
    for (unsigned char ch : value)
    {
        hash ^= ch;
        hash *= prime;
    }

    int32_t result = static_cast<int32_t>(hash & 0x7fffffffu);
    return result == 0 ? 1 : result;
}

bool IsAsciiStableId(const std::string& value)
{
    for (unsigned char ch : value)
    {
        if (ch > 0x7f)
        {
            return false;
        }
    }

    return true;
}

bool TryHashStableId(PeUiRenderer* renderer, const std::string& value, const char* kind, int32_t& hash)
{
    if (!IsAsciiStableId(value))
    {
        if (renderer != nullptr)
        {
            renderer->lastError = std::string("RmlUi DOM bridge currently requires ASCII ") + kind +
                " ids to match C# UiStableId.Hash: " + value;
        }

        return false;
    }

    hash = StableHashAscii(value);
    return true;
}

bool TryGetAttribute(Rml::Element* element, const char* name, std::string& value)
{
    if (element == nullptr)
    {
        return false;
    }

    Rml::String text = element->GetAttribute<Rml::String>(name, "");
    if (text.empty())
    {
        return false;
    }

    value.assign(text);
    return true;
}

bool TryReadBoolean(const std::string& value, bool& result)
{
    if (value == "true" || value == "1" || value == "checked")
    {
        result = true;
        return true;
    }

    if (value == "false" || value == "0")
    {
        result = false;
        return true;
    }

    return false;
}

PeUiNativeValue EmptyValue()
{
    PeUiNativeValue value{};
    value.kind = 0;
    value.integer = 0;
    return value;
}

PeUiNativeValue InitialValueForElement(Rml::Element* element)
{
    if (element == nullptr)
    {
        return EmptyValue();
    }

    const Rml::String tag = element->GetTagName();
    std::string type;
    const bool isCheckbox = tag == "checkbox" ||
        (tag == "input" && TryGetAttribute(element, "type", type) && type == "checkbox");
    if (isCheckbox)
    {
        std::string checked;
        bool parsed = false;
        PeUiNativeValue value{};
        value.kind = 1;
        value.integer = (TryGetAttribute(element, "checked", checked) && TryReadBoolean(checked, parsed) && parsed) ? 1 : 0;
        return value;
    }

    std::string rawValue;
    if (TryGetAttribute(element, "value", rawValue))
    {
        char* end = nullptr;
        const double number = std::strtod(rawValue.c_str(), &end);
        if (end != nullptr && *end == '\0')
        {
            PeUiNativeValue value{};
            value.kind = 3;
            value.number = number;
            return value;
        }
    }

    return EmptyValue();
}

std::string ValueToText(const PeUiNativeValue& value)
{
    char buffer[64];
    switch (value.kind)
    {
    case 1:
        return value.integer != 0 ? "true" : "false";
    case 2:
        std::snprintf(buffer, sizeof(buffer), "%lld", static_cast<long long>(value.integer));
        return buffer;
    case 3:
        std::snprintf(buffer, sizeof(buffer), "%.6g", value.number);
        return buffer;
    default:
        return "";
    }
}

bool ApplyValueToElement(Rml::Element* element, const PeUiNativeValue& value)
{
    if (element == nullptr)
    {
        return false;
    }

    const Rml::String tag = element->GetTagName();
    const std::string text = ValueToText(value);
    element->SetAttribute("value", text);
    if (value.kind == 1)
    {
        element->SetAttribute("checked", value.integer != 0);
    }

    if (tag != "input" && tag != "checkbox" && tag != "progress")
    {
        element->SetInnerRML(text);
    }

    return true;
}

PeUiModelBinding* FindModelBinding(PeUiRenderer* renderer, int32_t documentHandle, int32_t pathHash)
{
    if (renderer == nullptr)
    {
        return nullptr;
    }

    for (PeUiModelBinding& binding : renderer->modelBindings)
    {
        if (binding.documentHandle == documentHandle && binding.pathHash == pathHash)
        {
            return &binding;
        }
    }

    return nullptr;
}

PeUiModelBinding* FindModelBindingForElement(PeUiRenderer* renderer, int32_t documentHandle, Rml::Element* element)
{
    if (renderer == nullptr || element == nullptr)
    {
        return nullptr;
    }

    for (PeUiModelBinding& binding : renderer->modelBindings)
    {
        if (binding.documentHandle == documentHandle && binding.element == element)
        {
            return &binding;
        }
    }

    return nullptr;
}

void EnqueueEvent(PeUiRenderer* renderer, const PeUiNativeEvent& uiEvent)
{
    if (renderer == nullptr || renderer->events.empty())
    {
        return;
    }

    if (renderer->eventCount == static_cast<int32_t>(renderer->events.size()))
    {
        renderer->eventRead = (renderer->eventRead + 1) % static_cast<int32_t>(renderer->events.size());
        renderer->eventCount--;
    }

    const int32_t write = (renderer->eventRead + renderer->eventCount) % static_cast<int32_t>(renderer->events.size());
    renderer->events[write] = uiEvent;
    renderer->eventCount++;
}

void PeUiEventListener::ProcessEvent(Rml::Event& event)
{
    PeUiNativeEvent uiEvent{};
    uiEvent.document = documentHandle;
    uiEvent.element = elementHash;
    uiEvent.action = actionHash;
    uiEvent.value_kind = 0;

    if (owner != nullptr)
    {
        if (PeUiModelBinding* binding = FindModelBindingForElement(owner, documentHandle, event.GetTargetElement()))
        {
            uiEvent.value_kind = binding->value.kind;
            uiEvent.integer = binding->value.integer;
            uiEvent.number = binding->value.number;
        }
    }

    EnqueueEvent(owner, uiEvent);
}

void ClearDocumentBindings(PeUiRenderer* renderer, Rml::ElementDocument* document)
{
    if (renderer == nullptr || document == nullptr)
    {
        return;
    }

    for (auto it = renderer->eventBindings.begin(); it != renderer->eventBindings.end();)
    {
        if (it->document == document)
        {
            if (it->element != nullptr && it->listener != nullptr)
            {
                it->element->RemoveEventListener(it->eventId, it->listener.get());
            }

            it = renderer->eventBindings.erase(it);
        }
        else
        {
            ++it;
        }
    }

    renderer->modelBindings.erase(
        std::remove_if(
            renderer->modelBindings.begin(),
            renderer->modelBindings.end(),
            [document](const PeUiModelBinding& binding) { return binding.document == document; }),
        renderer->modelBindings.end());
}

void ClearAllBindings(PeUiRenderer* renderer)
{
    if (renderer == nullptr)
    {
        return;
    }

    for (PeUiEventBinding& binding : renderer->eventBindings)
    {
        if (binding.element != nullptr && binding.listener != nullptr)
        {
            binding.element->RemoveEventListener(binding.eventId, binding.listener.get());
        }
    }

    renderer->eventBindings.clear();
    renderer->modelBindings.clear();
    renderer->eventRead = 0;
    renderer->eventCount = 0;
}

bool TryAddModelBinding(PeUiRenderer* renderer, Rml::ElementDocument* document, int32_t documentHandle, Rml::Element* element, const std::string& path)
{
    if (renderer == nullptr || document == nullptr || element == nullptr || path.empty())
    {
        return true;
    }

    int32_t pathHash = 0;
    if (!TryHashStableId(renderer, path, "model path", pathHash))
    {
        return false;
    }

    for (const PeUiModelBinding& binding : renderer->modelBindings)
    {
        if (binding.documentHandle == documentHandle && binding.pathHash == pathHash && binding.path != path)
        {
            renderer->lastError = "UI data-model/path hash collision: " + binding.path + " vs " + path;
            return false;
        }
    }

    renderer->modelBindings.push_back(PeUiModelBinding{
        document,
        element,
        documentHandle,
        pathHash,
        path,
        InitialValueForElement(element),
    });
    return true;
}

void AddEventBinding(
    PeUiRenderer* renderer,
    Rml::ElementDocument* document,
    int32_t documentHandle,
    Rml::Element* element,
    Rml::EventId eventId,
    int32_t elementHash,
    int32_t actionHash)
{
    if (renderer == nullptr || document == nullptr || element == nullptr || actionHash <= 0)
    {
        return;
    }

    auto listener = std::make_unique<PeUiEventListener>(renderer, documentHandle, elementHash, actionHash);
    element->AddEventListener(eventId, listener.get());
    renderer->eventBindings.push_back(PeUiEventBinding{document, element, eventId, std::move(listener)});
}

bool BindElementTree(PeUiRenderer* renderer, Rml::ElementDocument* document, int32_t documentHandle, Rml::Element* element)
{
    if (element == nullptr)
    {
        return true;
    }

    std::string id;
    std::string path;
    std::string action;
    std::string changeAction;
    const Rml::String tag = element->GetTagName();
    TryGetAttribute(element, "id", id);
    if (TryGetAttribute(element, "data-model", path) || TryGetAttribute(element, "path", path))
    {
        if (!TryAddModelBinding(renderer, document, documentHandle, element, path))
        {
            return false;
        }
    }

    if (TryGetAttribute(element, "data-event-click", action) ||
        TryGetAttribute(element, "action", action) ||
        (tag == "button" && !id.empty() && (action = id, true)))
    {
        int32_t elementHash = 0;
        int32_t actionHash = 0;
        if (!TryHashStableId(renderer, !id.empty() ? id : action, "element", elementHash) ||
            !TryHashStableId(renderer, action, "action", actionHash))
        {
            return false;
        }

        AddEventBinding(renderer, document, documentHandle, element, Rml::EventId::Click, elementHash, actionHash);
    }

    std::string type;
    const bool isCheckbox = tag == "checkbox" ||
        (tag == "input" && TryGetAttribute(element, "type", type) && type == "checkbox");
    if (TryGetAttribute(element, "data-event-change", changeAction) || (isCheckbox && !id.empty() && (changeAction = id, true)))
    {
        int32_t elementHash = 0;
        int32_t actionHash = 0;
        if (!TryHashStableId(renderer, !id.empty() ? id : changeAction, "element", elementHash) ||
            !TryHashStableId(renderer, changeAction, "action", actionHash))
        {
            return false;
        }

        AddEventBinding(renderer, document, documentHandle, element, Rml::EventId::Change, elementHash, actionHash);
    }

    const int childCount = element->GetNumChildren();
    for (int i = 0; i < childCount; i++)
    {
        if (!BindElementTree(renderer, document, documentHandle, element->GetChild(i)))
        {
            return false;
        }
    }

    return true;
}
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

    instance->events.resize(EventCapacity);
    g_rendererCount++;
    return instance.release();
}

PE_UI_NATIVE_API void peui_native_destroy_renderer(PeUiRenderer* renderer)
{
    if (renderer != nullptr && renderer->context != nullptr)
    {
        ClearAllBindings(renderer);
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

PE_UI_NATIVE_API int32_t peui_native_document_bind(PeUiRenderer* renderer, Rml::ElementDocument* document, int32_t document_handle)
{
    if (renderer == nullptr || renderer->context == nullptr || document == nullptr || document_handle <= 0)
    {
        return -1;
    }

    renderer->lastError.clear();
    ClearDocumentBindings(renderer, document);
    return BindElementTree(renderer, document, document_handle, document) ? 1 : -2;
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

PE_UI_NATIVE_API void peui_native_document_close_bound(PeUiRenderer* renderer, Rml::ElementDocument* document)
{
    if (document != nullptr)
    {
        ClearDocumentBindings(renderer, document);
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

PE_UI_NATIVE_API int32_t peui_native_process_mouse_move(PeUiRenderer* renderer, int32_t x, int32_t y, int32_t modifiers)
{
    if (renderer == nullptr || renderer->context == nullptr)
    {
        return 0;
    }

    return renderer->context->ProcessMouseMove(x, y, modifiers) ? 1 : 0;
}

PE_UI_NATIVE_API int32_t peui_native_process_mouse_button(PeUiRenderer* renderer, int32_t button, int32_t is_down, int32_t modifiers)
{
    if (renderer == nullptr || renderer->context == nullptr || button < 0)
    {
        return 0;
    }

    const bool handled = is_down != 0
        ? renderer->context->ProcessMouseButtonDown(button, modifiers)
        : renderer->context->ProcessMouseButtonUp(button, modifiers);
    return handled ? 1 : 0;
}

PE_UI_NATIVE_API int32_t peui_native_process_mouse_wheel(PeUiRenderer* renderer, float delta_x, float delta_y, int32_t modifiers)
{
    if (renderer == nullptr || renderer->context == nullptr)
    {
        return 0;
    }

    return renderer->context->ProcessMouseWheel(Rml::Vector2f{delta_x, delta_y}, modifiers) ? 1 : 0;
}

PE_UI_NATIVE_API int32_t peui_native_process_key(PeUiRenderer* renderer, int32_t key, int32_t is_down, int32_t modifiers)
{
    if (renderer == nullptr || renderer->context == nullptr || key <= Rml::Input::KI_UNKNOWN)
    {
        return 0;
    }

    const auto key_identifier = static_cast<Rml::Input::KeyIdentifier>(key);
    const bool handled = is_down != 0
        ? renderer->context->ProcessKeyDown(key_identifier, modifiers)
        : renderer->context->ProcessKeyUp(key_identifier, modifiers);
    return handled ? 1 : 0;
}

PE_UI_NATIVE_API int32_t peui_native_process_text_utf8(PeUiRenderer* renderer, const char* text, int32_t text_length)
{
    if (renderer == nullptr || renderer->context == nullptr || text == nullptr || text_length <= 0)
    {
        return 0;
    }

    Rml::String input(text, static_cast<size_t>(text_length));
    return renderer->context->ProcessTextInput(input) ? 1 : 0;
}

PE_UI_NATIVE_API int32_t peui_native_hit_test(PeUiRenderer* renderer, float x, float y)
{
    if (renderer == nullptr || renderer->context == nullptr)
    {
        return 0;
    }

    int32_t flags = 0;
    Rml::Element* element = renderer->context->GetElementAtPoint(Rml::Vector2f{x, y});
    if (element != nullptr && element != renderer->context->GetRootElement())
    {
        flags |= 1;
    }

    if (renderer->context->IsMouseInteracting())
    {
        flags |= 2;
    }

    if (renderer->context->GetFocusElement() != nullptr)
    {
        flags |= 4;
    }

    return flags;
}

PE_UI_NATIVE_API int32_t peui_native_set_model_value(
    PeUiRenderer* renderer,
    int32_t document_handle,
    int32_t path_hash,
    const PeUiNativeValue* value)
{
    if (renderer == nullptr || document_handle <= 0 || path_hash <= 0 || value == nullptr)
    {
        return -1;
    }

    if (value->kind < 0 || value->kind > 3)
    {
        renderer->lastError = "RmlUi DOM bridge supports Empty/Boolean/Int64/Double values only.";
        return -2;
    }

    PeUiModelBinding* binding = FindModelBinding(renderer, document_handle, path_hash);
    if (binding == nullptr)
    {
        return 0;
    }

    binding->value = *value;
    ApplyValueToElement(binding->element, binding->value);
    return 1;
}

PE_UI_NATIVE_API int32_t peui_native_try_get_model_value(
    PeUiRenderer* renderer,
    int32_t document_handle,
    int32_t path_hash,
    PeUiNativeValue* value)
{
    if (renderer == nullptr || document_handle <= 0 || path_hash <= 0 || value == nullptr)
    {
        return -1;
    }

    PeUiModelBinding* binding = FindModelBinding(renderer, document_handle, path_hash);
    if (binding == nullptr)
    {
        return 0;
    }

    *value = binding->value;
    return 1;
}

PE_UI_NATIVE_API int32_t peui_native_copy_model_paths(
    PeUiRenderer* renderer,
    int32_t document_handle,
    int32_t* paths,
    int32_t capacity)
{
    if (renderer == nullptr || document_handle <= 0 || paths == nullptr || capacity <= 0)
    {
        return 0;
    }

    int32_t written = 0;
    for (const PeUiModelBinding& binding : renderer->modelBindings)
    {
        if (binding.documentHandle != document_handle)
        {
            continue;
        }

        bool duplicate = false;
        for (int32_t i = 0; i < written; i++)
        {
            if (paths[i] == binding.pathHash)
            {
                duplicate = true;
                break;
            }
        }

        if (duplicate)
        {
            continue;
        }

        paths[written++] = binding.pathHash;
        if (written == capacity)
        {
            break;
        }
    }

    return written;
}

PE_UI_NATIVE_API int32_t peui_native_drain_events(PeUiRenderer* renderer, PeUiNativeEvent* events, int32_t capacity)
{
    if (renderer == nullptr || events == nullptr || capacity <= 0)
    {
        return 0;
    }

    const int32_t written = std::min(capacity, renderer->eventCount);
    const int32_t ringSize = static_cast<int32_t>(renderer->events.size());
    for (int32_t i = 0; i < written; i++)
    {
        events[i] = renderer->events[renderer->eventRead];
        renderer->eventRead = (renderer->eventRead + 1) % ringSize;
    }

    renderer->eventCount -= written;
    return written;
}

PE_UI_NATIVE_API const char* peui_native_get_last_error(PeUiRenderer* renderer)
{
    if (renderer == nullptr || renderer->lastError.empty())
    {
        return "";
    }

    return renderer->lastError.c_str();
}
