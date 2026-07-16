using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace PixelEngine.Hosting.Tests;

/// <summary>Editor 人工 UI 操作必须进入 production command catalog，禁止新增未映射 ImGui 入口。</summary>
public sealed class EditorAutomationUiClosureTests
{
    private static readonly HashSet<string> InteractiveMethods = new(StringComparer.Ordinal)
    {
        "AcceptDragDropPayload",
        "BeginCombo",
        "BeginDragDropSource",
        "BeginDragDropTarget",
        "BeginPopupContextItem",
        "BeginPopupContextWindow",
        "Button",
        "Checkbox",
        "CollapsingHeader",
        "ColorEdit3",
        "ColorEdit4",
        "Combo",
        "DragFloat",
        "DragFloat2",
        "DragFloat3",
        "DragFloat4",
        "DragInt",
        "DragInt2",
        "DragScalar",
        "ImageButton",
        "InputDouble",
        "InputFloat",
        "InputFloat2",
        "InputFloat3",
        "InputFloat4",
        "InputInt",
        "InputInt2",
        "InputScalar",
        "InputText",
        "InputTextMultiline",
        "InputTextWithHint",
        "InvisibleButton",
        "IsKeyPressed",
        "IsItemActivated",
        "IsItemActive",
        "IsItemClicked",
        "IsItemDeactivated",
        "IsItemDeactivatedAfterEdit",
        "IsMouseClicked",
        "IsMouseDoubleClicked",
        "IsMouseDown",
        "IsMouseDragging",
        "IsMouseReleased",
        "MenuItem",
        "RadioButton",
        "Selectable",
        "Shortcut",
        "SliderFloat",
        "SliderInt",
        "SmallButton",
        "TreeNode",
        "TreeNodeEx",
    };

    /// <summary>每个直接 ImGui 人工输入点都必须属于 command handler 或纯 control primitive。</summary>
    [Fact]
    public void EveryInteractiveImGuiCallBelongsToDeclaredProductionSurface()
    {
        string root = RepositoryRoot();
        string[] sourceRoots =
        [
            Path.Combine(root, "apps", "PixelEngine.Editor.Shell"),
            Path.Combine(root, "src", "PixelEngine.Editor"),
        ];
        List<string> violations = [];
        for (int rootIndex = 0; rootIndex < sourceRoots.Length; rootIndex++)
        {
            foreach (string path in Directory.EnumerateFiles(
                         sourceRoots[rootIndex],
                         "*.cs",
                         SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(path);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(
                    source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                    path);
                SyntaxNode syntaxRoot = tree.GetRoot();
                foreach (InvocationExpressionSyntax invocation in syntaxRoot
                             .DescendantNodes()
                             .OfType<InvocationExpressionSyntax>())
                {
                    if (!TryGetInteractiveImGuiMethod(invocation, out string? imGuiMethod))
                    {
                        continue;
                    }

                    TypeDeclarationSyntax? type = invocation.Ancestors()
                        .OfType<TypeDeclarationSyntax>()
                        .FirstOrDefault();
                    if (type is null || !HasAttribute(type.AttributeLists, "EditorUiSurface"))
                    {
                        continue;
                    }

                    MethodDeclarationSyntax? method = invocation.Ancestors()
                        .OfType<MethodDeclarationSyntax>()
                        .FirstOrDefault();
                    if (method is not null && HasUiBindingAttribute(method))
                    {
                        continue;
                    }

                    FileLinePositionSpan line = tree.GetLineSpan(invocation.Span);
                    string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                    violations.Add(
                        $"{relative}:{line.StartLinePosition.Line + 1} " +
                        $"{method?.Identifier.ValueText ?? "<no-method>"} -> ImGui.{imGuiMethod}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "存在未进入 Editor UI command catalog 的人工输入点：\n" + string.Join('\n', violations));
    }

    /// <summary>每个稳定 Editor panel ID 必须由唯一同名 production UI surface 承担。</summary>
    [Fact]
    public void ProductionUiSurfacesAreUniqueAndCoverEveryStablePanel()
    {
        string root = RepositoryRoot();
        Dictionary<string, List<string>> declarations = new(StringComparer.Ordinal);
        foreach (string path in EnumerateUiSources(root))
        {
            SyntaxTree tree = ParseSource(path);
            foreach (TypeDeclarationSyntax type in tree.GetRoot().DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                AttributeSyntax? attribute = FindAttribute(type.AttributeLists, "EditorUiSurface");
                if (attribute?.ArgumentList is not { Arguments.Count: > 0 } argumentList ||
                    argumentList.Arguments[0].Expression is not
                    LiteralExpressionSyntax literal ||
                    literal.Token.ValueText is not { Length: > 0 } surfaceId)
                {
                    continue;
                }

                if (!declarations.TryGetValue(surfaceId, out List<string>? handlers))
                {
                    handlers = [];
                    declarations.Add(surfaceId, handlers);
                }

                handlers.Add(type.Identifier.ValueText);
            }
        }

        string panelIdsPath = Path.Combine(root, "apps", "PixelEngine.Editor.Shell", "EditorPanelIds.cs");
        SyntaxNode panelIdsRoot = ParseSource(panelIdsPath).GetRoot();
        string[] panelIds =
        [
            .. panelIdsRoot.DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .Where(static field => field.Modifiers.Any(SyntaxKind.ConstKeyword))
                .SelectMany(static field => field.Declaration.Variables)
                .Select(static variable => variable.Initializer?.Value)
                .OfType<LiteralExpressionSyntax>()
                .Select(static literal => literal.Token.ValueText)
                .Order(StringComparer.Ordinal),
        ];
        string[] duplicates =
        [
            .. declarations
                .Where(static entry => entry.Value.Count != 1)
                .Select(static entry => $"{entry.Key}=[{string.Join(',', entry.Value)}]")
                .Order(StringComparer.Ordinal),
        ];
        string[] missing =
        [
            .. panelIds.Where(panelId => !declarations.ContainsKey(panelId)),
        ];

        Assert.True(
            duplicates.Length == 0 && missing.Length == 0,
            $"UI surface 声明不唯一或未覆盖稳定 panel。duplicates=[{string.Join(';', duplicates)}]; " +
            $"missing=[{string.Join(',', missing)}]");
    }

    /// <summary>低层 ImGui helper 只能作为已登记 command handler 的可达实现细节。</summary>
    [Fact]
    public void EveryControlPrimitiveIsReachableFromADeclaredUiCommand()
    {
        string root = RepositoryRoot();
        List<string> violations = [];
        foreach (string path in EnumerateUiSources(root))
        {
            SyntaxTree tree = ParseSource(path);
            foreach (TypeDeclarationSyntax type in tree.GetRoot().DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                if (!HasAttribute(type.AttributeLists, "EditorUiSurface"))
                {
                    continue;
                }

                MethodDeclarationSyntax[] methods = [.. type.Members.OfType<MethodDeclarationSyntax>()];
                Dictionary<string, MethodDeclarationSyntax[]> byName = methods
                    .GroupBy(static method => method.Identifier.ValueText, StringComparer.Ordinal)
                    .ToDictionary(
                        static group => group.Key,
                        static group => group.ToArray(),
                        StringComparer.Ordinal);
                Queue<MethodDeclarationSyntax> pending = new(
                    methods.Where(static method =>
                        HasAttribute(method.AttributeLists, "EditorUiCommands")));
                HashSet<MethodDeclarationSyntax> reachable = [];
                while (pending.TryDequeue(out MethodDeclarationSyntax? method))
                {
                    if (!reachable.Add(method))
                    {
                        continue;
                    }

                    foreach (InvocationExpressionSyntax invocation in method.DescendantNodes()
                                 .OfType<InvocationExpressionSyntax>())
                    {
                        string? calledName = invocation.Expression switch
                        {
                            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                            MemberAccessExpressionSyntax member when
                                member.Expression is ThisExpressionSyntax => member.Name.Identifier.ValueText,
                            _ => null,
                        };
                        if (calledName is not null && byName.TryGetValue(
                                calledName,
                                out MethodDeclarationSyntax[]? callees))
                        {
                            for (int i = 0; i < callees.Length; i++)
                            {
                                pending.Enqueue(callees[i]);
                            }
                        }
                    }
                }

                foreach (MethodDeclarationSyntax primitive in methods.Where(static method =>
                             HasAttribute(method.AttributeLists, "EditorUiControlPrimitive")))
                {
                    if (reachable.Contains(primitive))
                    {
                        continue;
                    }

                    FileLinePositionSpan line = tree.GetLineSpan(primitive.Span);
                    violations.Add(
                        $"{Path.GetRelativePath(root, path).Replace('\\', '/')}" +
                        $":{line.StartLinePosition.Line + 1} {type.Identifier}.{primitive.Identifier}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "存在无法从任何 UI command handler 到达的 control primitive：\n" +
            string.Join('\n', violations));
    }

    private static bool TryGetInteractiveImGuiMethod(
        InvocationExpressionSyntax invocation,
        out string? method)
    {
        method = null;
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }

        string candidate = member.Name.Identifier.ValueText;
        bool directImGui = string.Equals(member.Expression.ToString(), "ImGui", StringComparison.Ordinal) &&
            InteractiveMethods.Contains(candidate);
        bool assetPayload = string.Equals(
                member.Expression.ToString(),
                "AssetBrowserDragPayloadImGui",
                StringComparison.Ordinal) &&
            string.Equals(candidate, "TryAcceptPayload", StringComparison.Ordinal);
        if (!directImGui && !assetPayload)
        {
            return false;
        }

        method = candidate;
        return true;
    }

    private static bool HasUiBindingAttribute(MethodDeclarationSyntax method)
    {
        return HasAttribute(method.AttributeLists, "EditorUiCommands") ||
            HasAttribute(method.AttributeLists, "EditorUiControlPrimitive");
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributes, string expectedName)
    {
        foreach (AttributeSyntax attribute in attributes.SelectMany(static list => list.Attributes))
        {
            string name = attribute.Name.ToString();
            if (name.EndsWith(expectedName, StringComparison.Ordinal) ||
                name.EndsWith(expectedName + "Attribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static AttributeSyntax? FindAttribute(
        SyntaxList<AttributeListSyntax> attributes,
        string expectedName)
    {
        return attributes.SelectMany(static list => list.Attributes).FirstOrDefault(attribute =>
        {
            string name = attribute.Name.ToString();
            return name.EndsWith(expectedName, StringComparison.Ordinal) ||
                name.EndsWith(expectedName + "Attribute", StringComparison.Ordinal);
        });
    }

    private static IEnumerable<string> EnumerateUiSources(string root)
    {
        string[] sourceRoots =
        [
            Path.Combine(root, "apps", "PixelEngine.Editor.Shell"),
            Path.Combine(root, "src", "PixelEngine.Editor"),
        ];
        return sourceRoots.SelectMany(sourceRoot =>
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories));
    }

    private static SyntaxTree ParseSource(string path)
    {
        return CSharpSyntaxTree.ParseText(
            File.ReadAllText(path),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null &&
               (!File.Exists(Path.Combine(current.FullName, "PixelEngine.sln")) ||
                !File.Exists(Path.Combine(current.FullName, "AGENTS.md"))))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("无法定位 PixelEngine 仓库根目录。");
    }
}
