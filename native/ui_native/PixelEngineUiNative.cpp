#include <RmlUi/Core.h>
#include <RmlUi/Core/ElementDocument.h>
#include <RmlUi/Core/SystemInterface.h>
#include <RmlUi/Core/TextInputContext.h>
#include <RmlUi/Core/TextInputHandler.h>
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
constexpr int32_t MaxTrackedTextureUnits = 32;

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
    std::string variableName;
    PeUiNativeValue value;
    std::string resolvedText;
};

struct PeUiDocumentModel
{
    Rml::ElementDocument* document;
    int32_t documentHandle;
    std::string modelName;
    Rml::DataModelHandle modelHandle;
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
    int32_t documentHandle;
    int32_t actionHash;
    std::unique_ptr<PeUiEventListener> listener;
};

struct PeUiRenderer
{
    std::unique_ptr<RenderInterface_GL3> renderer;
    Rml::Context* context = nullptr;
    std::string contextName;
    std::vector<PeUiDocumentModel> documentModels;
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

// 捕获 RmlUi 焦点文本输入上下文，并提供 IME composition 预编辑写入/取消/确认（对齐 RmlUi TextInputContext 契约）。
class PeUiTextInputHandler final : public Rml::TextInputHandler
{
public:
    void OnActivate(Rml::TextInputContext* input_context) override
    {
        if (active_context != input_context && composing)
        {
            CancelComposition();
        }

        active_context = input_context;
    }

    void OnDeactivate(Rml::TextInputContext* input_context) override
    {
        if (active_context != input_context)
        {
            return;
        }

        if (composing)
        {
            CancelComposition();
        }

        active_context = nullptr;
    }

    void OnDestroy(Rml::TextInputContext* input_context) override
    {
        if (active_context != input_context)
        {
            return;
        }

        // 上下文即将销毁，不能再写回；只清理本地 composition 状态。
        composing = false;
        composition_range_start = 0;
        composition_range_end = 0;
        active_context = nullptr;
    }

    Rml::TextInputContext* ActiveContext() const
    {
        return active_context;
    }

    bool IsComposing() const
    {
        return composing;
    }

    // 更新预编辑字符串；无焦点时返回 0，成功返回 1。
    int32_t SetComposition(Rml::StringView composition, int32_t cursor_index)
    {
        if (active_context == nullptr)
        {
            return 0;
        }

        if (!composing)
        {
            composing = true;
            composition_range_start = 0;
            composition_range_end = 0;
            active_context->GetSelectionRange(composition_range_start, composition_range_end);
        }

        active_context->SetText(composition, composition_range_start, composition_range_end);
        const int length = static_cast<int>(Rml::StringUtilities::LengthUTF8(composition));
        composition_range_end = composition_range_start + length;

        const int clamped_cursor = std::max(0, std::min(cursor_index, length));
        active_context->SetCursorPosition(composition_range_start + clamped_cursor);
        active_context->SetCompositionRange(composition_range_start, composition_range_end);
        return 1;
    }

    // 取消预编辑并移除 composition 区间文本；未在 composing 时 no-op 成功返回 1。
    int32_t CancelComposition()
    {
        if (!composing)
        {
            return 1;
        }

        if (active_context != nullptr)
        {
            active_context->SetText(Rml::StringView(), composition_range_start, composition_range_end);
            active_context->SetCursorPosition(composition_range_start);
            active_context->SetCompositionRange(0, 0);
        }

        composing = false;
        composition_range_start = 0;
        composition_range_end = 0;
        return 1;
    }

    // 确认预编辑为最终提交字符串（尊重 max length 等控件约束）。
    int32_t ConfirmComposition(Rml::StringView composition)
    {
        if (active_context == nullptr)
        {
            return 0;
        }

        if (!composing)
        {
            // 无预编辑时退回普通文本插入路径由调用方 ProcessTextInput 处理。
            return 0;
        }

        active_context->SetText(composition, composition_range_start, composition_range_end);
        const int length = static_cast<int>(Rml::StringUtilities::LengthUTF8(composition));
        composition_range_end = composition_range_start + length;
        active_context->SetCompositionRange(composition_range_start, composition_range_end);
        active_context->CommitComposition(composition);
        active_context->SetCursorPosition(composition_range_end);
        active_context->SetCompositionRange(0, 0);

        composing = false;
        composition_range_start = 0;
        composition_range_end = 0;
        return 1;
    }

private:
    Rml::TextInputContext* active_context = nullptr;
    bool composing = false;
    int composition_range_start = 0;
    int composition_range_end = 0;
};

PeUiTextInputHandler g_textInputHandler;

class PeUiGlStateGuard final
{
public:
    PeUiGlStateGuard()
    {
        glGetIntegerv(GL_CURRENT_PROGRAM, &program);
        glGetIntegerv(GL_VERTEX_ARRAY_BINDING, &vertexArray);
        glGetIntegerv(GL_ARRAY_BUFFER_BINDING, &arrayBuffer);
        glGetIntegerv(GL_ELEMENT_ARRAY_BUFFER_BINDING, &elementArrayBuffer);
        glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &drawFramebuffer);
        glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &readFramebuffer);
        glGetIntegerv(GL_ACTIVE_TEXTURE, &activeTexture);
        glGetIntegerv(GL_BLEND_SRC_RGB, &blendSrcRgb);
        glGetIntegerv(GL_BLEND_DST_RGB, &blendDstRgb);
        glGetIntegerv(GL_BLEND_SRC_ALPHA, &blendSrcAlpha);
        glGetIntegerv(GL_BLEND_DST_ALPHA, &blendDstAlpha);
        glGetIntegerv(GL_BLEND_EQUATION_RGB, &blendEquationRgb);
        glGetIntegerv(GL_BLEND_EQUATION_ALPHA, &blendEquationAlpha);
        glGetIntegerv(GL_UNPACK_ALIGNMENT, &unpackAlignment);
        glGetIntegerv(GL_VIEWPORT, viewport);
        glGetIntegerv(GL_SCISSOR_BOX, scissorBox);
        glGetIntegerv(GL_MAX_COMBINED_TEXTURE_IMAGE_UNITS, &textureUnitCount);
        textureUnitCount = std::max<GLint>(0, std::min<GLint>(textureUnitCount, MaxTrackedTextureUnits));

        blendEnabled = glIsEnabled(GL_BLEND);
        scissorEnabled = glIsEnabled(GL_SCISSOR_TEST);
        depthEnabled = glIsEnabled(GL_DEPTH_TEST);
        cullEnabled = glIsEnabled(GL_CULL_FACE);

        for (GLint i = 0; i < textureUnitCount; i++)
        {
            glActiveTexture(static_cast<GLenum>(GL_TEXTURE0 + i));
            glGetIntegerv(GL_TEXTURE_BINDING_2D, &texture2D[i]);
        }
    }

    PeUiGlStateGuard(const PeUiGlStateGuard&) = delete;
    PeUiGlStateGuard& operator=(const PeUiGlStateGuard&) = delete;

    ~PeUiGlStateGuard()
    {
        RestoreEnable(GL_BLEND, blendEnabled);
        RestoreEnable(GL_SCISSOR_TEST, scissorEnabled);
        RestoreEnable(GL_DEPTH_TEST, depthEnabled);
        RestoreEnable(GL_CULL_FACE, cullEnabled);
        glBlendFuncSeparate(blendSrcRgb, blendDstRgb, blendSrcAlpha, blendDstAlpha);
        glBlendEquationSeparate(blendEquationRgb, blendEquationAlpha);
        glViewport(viewport[0], viewport[1], viewport[2], viewport[3]);
        glScissor(scissorBox[0], scissorBox[1], scissorBox[2], scissorBox[3]);
        glPixelStorei(GL_UNPACK_ALIGNMENT, unpackAlignment);

        for (GLint i = 0; i < textureUnitCount; i++)
        {
            glActiveTexture(static_cast<GLenum>(GL_TEXTURE0 + i));
            glBindTexture(GL_TEXTURE_2D, static_cast<GLuint>(texture2D[i]));
        }

        glActiveTexture(static_cast<GLenum>(activeTexture));
        glBindFramebuffer(GL_DRAW_FRAMEBUFFER, static_cast<GLuint>(drawFramebuffer));
        glBindFramebuffer(GL_READ_FRAMEBUFFER, static_cast<GLuint>(readFramebuffer));
        glBindBuffer(GL_ARRAY_BUFFER, static_cast<GLuint>(arrayBuffer));
        glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, static_cast<GLuint>(elementArrayBuffer));
        glBindVertexArray(static_cast<GLuint>(vertexArray));
        glUseProgram(static_cast<GLuint>(program));
    }

private:
    static void RestoreEnable(GLenum capability, GLboolean enabled)
    {
        if (enabled == GL_TRUE)
        {
            glEnable(capability);
        }
        else
        {
            glDisable(capability);
        }
    }

    GLint program = 0;
    GLint vertexArray = 0;
    GLint arrayBuffer = 0;
    GLint elementArrayBuffer = 0;
    GLint drawFramebuffer = 0;
    GLint readFramebuffer = 0;
    GLint activeTexture = GL_TEXTURE0;
    GLint blendSrcRgb = GL_ONE;
    GLint blendDstRgb = GL_ZERO;
    GLint blendSrcAlpha = GL_ONE;
    GLint blendDstAlpha = GL_ZERO;
    GLint blendEquationRgb = GL_FUNC_ADD;
    GLint blendEquationAlpha = GL_FUNC_ADD;
    GLint unpackAlignment = 4;
    GLint viewport[4] = {};
    GLint scissorBox[4] = {};
    GLint textureUnitCount = 0;
    GLint texture2D[MaxTrackedTextureUnits] = {};
    GLboolean blendEnabled = GL_FALSE;
    GLboolean scissorEnabled = GL_FALSE;
    GLboolean depthEnabled = GL_FALSE;
    GLboolean cullEnabled = GL_FALSE;
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

bool IsAsciiLetter(unsigned char ch)
{
    return (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
}

bool IsAsciiDigit(unsigned char ch)
{
    return ch >= '0' && ch <= '9';
}

std::string BuildModelVariableName(const std::string& path)
{
    std::string name;
    name.reserve(path.size() + 12);
    for (unsigned char ch : path)
    {
        if (IsAsciiLetter(ch) || IsAsciiDigit(ch) || ch == '_')
        {
            name.push_back(static_cast<char>(ch));
            continue;
        }

        if (ch == '.' || ch == '-' || ch == '/' || ch == ':' || ch == ' ')
        {
            name.push_back('_');
            continue;
        }

        char escaped[8];
        std::snprintf(escaped, sizeof(escaped), "_u%04X_", static_cast<unsigned int>(ch));
        name.append(escaped);
    }

    if (name.empty() || IsAsciiDigit(static_cast<unsigned char>(name[0])))
    {
        name.insert(name.begin(), '_');
    }

    char suffix[12];
    std::snprintf(suffix, sizeof(suffix), "__%08X", static_cast<uint32_t>(StableHashAscii(path)));
    name.append(suffix);
    return name;
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

std::string ValueToText(const PeUiNativeValue& value, const std::string& resolvedText)
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
    case 4:
        return resolvedText;
    default:
        return "";
    }
}

void SetVariantFromValue(Rml::Variant& variant, const PeUiNativeValue& value, const std::string& resolvedText)
{
    switch (value.kind)
    {
    case 1:
        variant = value.integer != 0;
        break;
    case 2:
        variant = value.integer;
        break;
    case 3:
        variant = value.number;
        break;
    case 4:
        variant = resolvedText;
        break;
    default:
        variant.Clear();
        break;
    }
}

PeUiNativeValue ValueFromVariant(const Rml::Variant& variant, const PeUiNativeValue& fallback)
{
    PeUiNativeValue value{};
    switch (variant.GetType())
    {
    case Rml::Variant::BOOL:
        value.kind = 1;
        value.integer = variant.Get<bool>() ? 1 : 0;
        return value;
    case Rml::Variant::BYTE:
    case Rml::Variant::CHAR:
    case Rml::Variant::INT:
    case Rml::Variant::UINT:
    case Rml::Variant::INT64:
    case Rml::Variant::UINT64:
        value.kind = 2;
        value.integer = variant.Get<int64_t>();
        return value;
    case Rml::Variant::FLOAT:
    case Rml::Variant::DOUBLE:
        value.kind = 3;
        value.number = variant.Get<double>();
        return value;
    case Rml::Variant::NONE:
        return EmptyValue();
    default:
        return fallback;
    }
}

bool ApplyValueToElement(Rml::Element* element, const PeUiNativeValue& value, const std::string& resolvedText)
{
    if (element == nullptr)
    {
        return false;
    }

    const Rml::String tag = element->GetTagName();
    const std::string text = ValueToText(value, resolvedText);
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

void ApplyDataBindingAttributes(Rml::Element* element, const std::string& modelName, const std::string& variableName)
{
    if (element == nullptr)
    {
        return;
    }

    const Rml::String tag = element->GetTagName();
    element->SetAttribute("data-model", modelName);

    std::string type;
    const bool isCheckbox = tag == "checkbox" ||
        (tag == "input" && TryGetAttribute(element, "type", type) && type == "checkbox");
    if (isCheckbox)
    {
        element->SetAttribute("data-checked", variableName);
        return;
    }

    if (tag == "input")
    {
        element->SetAttribute("data-value", variableName);
        return;
    }

    if (tag == "progress")
    {
        element->SetAttribute("data-attr-value", variableName);
        return;
    }

    element->SetAttribute("data-rml", variableName);
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

PeUiDocumentModel* FindDocumentModel(PeUiRenderer* renderer, Rml::ElementDocument* document)
{
    if (renderer == nullptr || document == nullptr)
    {
        return nullptr;
    }

    for (PeUiDocumentModel& model : renderer->documentModels)
    {
        if (model.document == document)
        {
            return &model;
        }
    }

    return nullptr;
}

bool IsFirstModelBindingForPath(PeUiRenderer* renderer, int32_t documentHandle, int32_t pathHash)
{
    if (renderer == nullptr)
    {
        return true;
    }

    for (const PeUiModelBinding& binding : renderer->modelBindings)
    {
        if (binding.documentHandle == documentHandle && binding.pathHash == pathHash)
        {
            return false;
        }
    }

    return true;
}

bool BindModelVariable(
    PeUiRenderer* renderer,
    PeUiDocumentModel* documentModel,
    int32_t pathHash,
    const std::string& variableName)
{
    if (renderer == nullptr || documentModel == nullptr || renderer->context == nullptr)
    {
        return false;
    }

    Rml::DataModelConstructor constructor = renderer->context->GetDataModel(documentModel->modelName);
    if (!constructor)
    {
        renderer->lastError = "RmlUi data model not found: " + documentModel->modelName;
        return false;
    }

    const int32_t documentHandle = documentModel->documentHandle;
    if (!constructor.BindFunc(
            variableName,
            [renderer, documentHandle, pathHash](Rml::Variant& variant) {
                if (PeUiModelBinding* binding = FindModelBinding(renderer, documentHandle, pathHash))
                {
                    SetVariantFromValue(variant, binding->value, binding->resolvedText);
                    return;
                }

                variant.Clear();
            },
            [renderer, documentHandle, pathHash](const Rml::Variant& variant) {
                PeUiModelBinding* first = FindModelBinding(renderer, documentHandle, pathHash);
                if (first == nullptr)
                {
                    return;
                }

                PeUiNativeValue value = ValueFromVariant(variant, first->value);
                for (PeUiModelBinding& binding : renderer->modelBindings)
                {
                    if (binding.documentHandle == documentHandle && binding.pathHash == pathHash)
                    {
                        binding.value = value;
                        ApplyValueToElement(binding.element, binding.value, binding.resolvedText);
                    }
                }
            }))
    {
        renderer->lastError = "RmlUi DataModelConstructor BindFunc failed for variable: " + variableName;
        return false;
    }

    return true;
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

    for (auto it = renderer->documentModels.begin(); it != renderer->documentModels.end();)
    {
        if (it->document == document)
        {
            if (renderer->context != nullptr)
            {
                renderer->context->RemoveDataModel(it->modelName);
            }

            it = renderer->documentModels.erase(it);
        }
        else
        {
            ++it;
        }
    }
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
    if (renderer->context != nullptr)
    {
        for (const PeUiDocumentModel& model : renderer->documentModels)
        {
            renderer->context->RemoveDataModel(model.modelName);
        }
    }

    renderer->documentModels.clear();
    renderer->eventRead = 0;
    renderer->eventCount = 0;
}

bool TryAddModelBinding(
    PeUiRenderer* renderer,
    PeUiDocumentModel* documentModel,
    Rml::ElementDocument* document,
    int32_t documentHandle,
    Rml::Element* element,
    const std::string& path)
{
    if (renderer == nullptr || documentModel == nullptr || document == nullptr || element == nullptr || path.empty())
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

    const std::string variableName = BuildModelVariableName(path);
    const bool firstForPath = IsFirstModelBindingForPath(renderer, documentHandle, pathHash);
    if (firstForPath && !BindModelVariable(renderer, documentModel, pathHash, variableName))
    {
        return false;
    }

    ApplyDataBindingAttributes(element, documentModel->modelName, variableName);
    renderer->modelBindings.push_back(PeUiModelBinding{
        document,
        element,
        documentHandle,
        pathHash,
        path,
        variableName,
        InitialValueForElement(element),
        std::string(),
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
    renderer->eventBindings.push_back(PeUiEventBinding{document, element, eventId, documentHandle, actionHash, std::move(listener)});
}

bool BindElementTree(
    PeUiRenderer* renderer,
    PeUiDocumentModel* documentModel,
    Rml::ElementDocument* document,
    int32_t documentHandle,
    Rml::Element* element)
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
        if (!TryAddModelBinding(renderer, documentModel, document, documentHandle, element, path))
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
        if (!BindElementTree(renderer, documentModel, document, documentHandle, element->GetChild(i)))
        {
            return false;
        }
    }

    return true;
}

PeUiDocumentModel* CreateDocumentModel(PeUiRenderer* renderer, Rml::ElementDocument* document, int32_t documentHandle)
{
    if (renderer == nullptr || renderer->context == nullptr || document == nullptr || documentHandle <= 0)
    {
        return nullptr;
    }

    const std::string modelName = "pixelengine_doc_" + std::to_string(documentHandle);
    Rml::DataModelConstructor constructor = renderer->context->CreateDataModel(modelName);
    if (!constructor)
    {
        renderer->lastError = "RmlUi DataModelConstructor creation failed: " + modelName;
        return nullptr;
    }

    renderer->documentModels.push_back(PeUiDocumentModel{
        document,
        documentHandle,
        modelName,
        constructor.GetModelHandle(),
    });
    return &renderer->documentModels.back();
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
        Rml::SetTextInputHandler(&g_textInputHandler);
        if (!Rml::Initialise())
        {
            Rml::SetTextInputHandler(nullptr);
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
        Rml::SetTextInputHandler(nullptr);
        Rml::SetRenderInterface(nullptr);
        Rml::SetSystemInterface(nullptr);
    }

    delete renderer;
}

// 读取当前焦点文本输入区域的 caret rect / 候选锚点（UI 坐标，左上原点）。
// 无焦点或无法取边界时返回 0；成功返回 1。
PE_UI_NATIVE_API int32_t peui_native_try_get_active_text_input_geometry(
    PeUiRenderer* renderer,
    float* out_caret_x,
    float* out_caret_y,
    float* out_caret_width,
    float* out_caret_height,
    float* out_anchor_x,
    float* out_anchor_y)
{
    if (renderer == nullptr ||
        out_caret_x == nullptr ||
        out_caret_y == nullptr ||
        out_caret_width == nullptr ||
        out_caret_height == nullptr ||
        out_anchor_x == nullptr ||
        out_anchor_y == nullptr)
    {
        return 0;
    }

    Rml::TextInputContext* context = g_textInputHandler.ActiveContext();
    if (context == nullptr)
    {
        return 0;
    }

    Rml::Rectanglef bounds;
    if (!context->GetBoundingBox(bounds))
    {
        return 0;
    }

    // RmlUi 边界为屏幕空间矩形；caret 取左上插入条近似，候选锚点取左下角。
    const float width = bounds.Width();
    const float height = bounds.Height();
    if (!(width > 0.0f) || !(height > 0.0f))
    {
        return 0;
    }

    const float caret_width = 2.0f;
    const float caret_height = std::min(height, 18.0f);
    *out_caret_x = bounds.Left();
    *out_caret_y = bounds.Top() + std::max(0.0f, (height - caret_height) * 0.5f);
    *out_caret_width = caret_width;
    *out_caret_height = caret_height;
    *out_anchor_x = bounds.Left();
    *out_anchor_y = bounds.Bottom();
    return 1;
}

// 更新 IME 预编辑字符串；is_active=0 表示取消预编辑。无焦点文本框时返回 0（安全忽略）。
// 不得用本 API 传递 committed text；确认提交请用 peui_native_confirm_text_composition。
PE_UI_NATIVE_API int32_t peui_native_set_text_composition(
    PeUiRenderer* renderer,
    const char* text_utf8,
    int32_t text_length,
    int32_t is_active,
    int32_t cursor_index)
{
    if (renderer == nullptr)
    {
        return 0;
    }

    if (is_active == 0)
    {
        return g_textInputHandler.CancelComposition();
    }

    Rml::StringView composition;
    if (text_utf8 != nullptr && text_length > 0)
    {
        composition = Rml::StringView(text_utf8, text_utf8 + text_length);
    }

    return g_textInputHandler.SetComposition(composition, cursor_index);
}

// 将当前预编辑确认提交为最终字符串；未在 composing 时返回 0，调用方应走 ProcessTextInput。
PE_UI_NATIVE_API int32_t peui_native_confirm_text_composition(
    PeUiRenderer* renderer,
    const char* text_utf8,
    int32_t text_length)
{
    if (renderer == nullptr)
    {
        return 0;
    }

    Rml::StringView composition;
    if (text_utf8 != nullptr && text_length > 0)
    {
        composition = Rml::StringView(text_utf8, text_utf8 + text_length);
    }

    return g_textInputHandler.ConfirmComposition(composition);
}

// 查询 native 是否正处于 composition 预编辑中。
PE_UI_NATIVE_API int32_t peui_native_is_text_composition_active(PeUiRenderer* renderer)
{
    if (renderer == nullptr)
    {
        return 0;
    }

    return g_textInputHandler.IsComposing() ? 1 : 0;
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

PE_UI_NATIVE_API void peui_native_renderer_set_viewport_region(PeUiRenderer* renderer, int32_t x, int32_t y, int32_t width, int32_t height)
{
    if (renderer == nullptr || renderer->renderer == nullptr || width <= 0 || height <= 0)
    {
        return;
    }

    renderer->renderer->SetViewport(width, height, x, y);
    if (renderer->context != nullptr)
    {
        renderer->context->SetDimensions(Rml::Vector2i(width, height));
    }
}

PE_UI_NATIVE_API int32_t peui_native_register_font_face(PeUiRenderer* renderer, const char* font_path)
{
    if (renderer == nullptr || font_path == nullptr || font_path[0] == '\0')
    {
        return 0;
    }

    renderer->lastError.clear();
    if (Rml::LoadFontFace(Rml::String(font_path), true))
    {
        return 1;
    }

    renderer->lastError = std::string("Rml::LoadFontFace failed for ") + font_path;
    return 0;
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
    PeUiDocumentModel* documentModel = CreateDocumentModel(renderer, document, document_handle);
    if (documentModel == nullptr)
    {
        return -2;
    }

    return BindElementTree(renderer, documentModel, document, document_handle, document) ? 1 : -2;
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

    PeUiGlStateGuard state;
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
        renderer->lastError = "RmlUi DOM bridge supports Empty/Boolean/Int64/Double here; StringHandle requires peui_native_set_model_string_value.";
        return -2;
    }

    PeUiModelBinding* first = FindModelBinding(renderer, document_handle, path_hash);
    if (first == nullptr)
    {
        return 0;
    }

    for (PeUiModelBinding& binding : renderer->modelBindings)
    {
        if (binding.documentHandle == document_handle && binding.pathHash == path_hash)
        {
            binding.value = *value;
            binding.resolvedText.clear();
            ApplyValueToElement(binding.element, binding.value, binding.resolvedText);
        }
    }

    if (PeUiDocumentModel* documentModel = FindDocumentModel(renderer, first->document))
    {
        documentModel->modelHandle.DirtyVariable(first->variableName);
    }

    return 1;
}

PE_UI_NATIVE_API int32_t peui_native_set_model_string_value(
    PeUiRenderer* renderer,
    int32_t document_handle,
    int32_t path_hash,
    const PeUiNativeValue* value,
    const char* text,
    int32_t text_length)
{
    if (renderer == nullptr || document_handle <= 0 || path_hash <= 0 || value == nullptr || text_length < 0)
    {
        return -1;
    }

    if (value->kind != 4)
    {
        renderer->lastError = "RmlUi DOM bridge string setter requires StringHandle kind.";
        return -2;
    }

    if (text == nullptr && text_length > 0)
    {
        renderer->lastError = "RmlUi DOM bridge string setter received null UTF-8 text.";
        return -2;
    }

    PeUiModelBinding* first = FindModelBinding(renderer, document_handle, path_hash);
    if (first == nullptr)
    {
        return 0;
    }

    const std::string resolvedText = text_length == 0 ? std::string() : std::string(text, static_cast<size_t>(text_length));
    for (PeUiModelBinding& binding : renderer->modelBindings)
    {
        if (binding.documentHandle == document_handle && binding.pathHash == path_hash)
        {
            binding.value = *value;
            binding.resolvedText = resolvedText;
            ApplyValueToElement(binding.element, binding.value, binding.resolvedText);
        }
    }

    if (PeUiDocumentModel* documentModel = FindDocumentModel(renderer, first->document))
    {
        documentModel->modelHandle.DirtyVariable(first->variableName);
    }

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

PE_UI_NATIVE_API int32_t peui_native_invoke_action(
    PeUiRenderer* renderer,
    int32_t document_handle,
    int32_t action_hash,
    const PeUiNativeValue* value)
{
    if (renderer == nullptr || document_handle <= 0 || action_hash <= 0 || value == nullptr)
    {
        return -1;
    }

    if (value->kind < 0 || value->kind > 3)
    {
        renderer->lastError = "RmlUi DOM action bridge supports Empty/Boolean/Int64/Double values only.";
        return -2;
    }

    bool invoked = false;
    const std::string resolvedText;
    for (PeUiEventBinding& binding : renderer->eventBindings)
    {
        if (binding.documentHandle != document_handle || binding.actionHash != action_hash || binding.element == nullptr)
        {
            continue;
        }

        if (PeUiModelBinding* model = FindModelBindingForElement(renderer, document_handle, binding.element))
        {
            model->value = *value;
            model->resolvedText.clear();
        }

        invoked = ApplyValueToElement(binding.element, *value, resolvedText) || invoked;
    }

    return invoked ? 1 : 0;
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
