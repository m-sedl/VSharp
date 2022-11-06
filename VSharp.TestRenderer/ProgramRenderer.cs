using System.Diagnostics;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VSharp.TestExtensions;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static VSharp.TestRenderer.CodeRenderer;

namespace VSharp.TestRenderer;

internal interface IReferenceManager
{
    void AddUsing(string name);
    void AddStaticUsing(string name);
    void AddAssembly(Assembly assembly);
    void AddTestExtensions();
    void AddObjectsComparer();
}

internal class ProgramRenderer
{
    private readonly IdentifiersCache _cache;
    private readonly ReferenceManager _referenceManager;
    private readonly BaseNamespaceDeclarationSyntax _namespace;
    private readonly List<string> _alreadyOpenedNamespaces = new ();
    private readonly List<TypeRenderer> _renderingTypes = new ();

    public ProgramRenderer(string namespaceName)
    {
        // Creating identifiers cache
        _cache = new IdentifiersCache();

        // Creating reference manager
        var namespaces = new List<string>();
        var currentNamespace = "";
        foreach (var name in namespaceName.Split('.'))
        {
            currentNamespace += name;
            namespaces.Add(currentNamespace);
            currentNamespace += '.';
        }
        _referenceManager = new ReferenceManager(namespaces);

        var namespaceId = _cache.GenerateIdentifier(namespaceName);
        _namespace = FileScopedNamespaceDeclaration(namespaceId);
    }

    public void AddNUnitToUsigns()
    {
        _referenceManager.AddNUnit();
    }

    public TypeRenderer AddType(
        string name,
        bool isStruct,
        IEnumerable<Type>? baseTypes,
        AttributeListSyntax? attributes,
        SyntaxToken[]? modifiers)
    {
        SimpleNameSyntax typeId = _cache.GenerateIdentifier(name);
        var type =
            new TypeRenderer(
                _cache,
                _referenceManager,
                typeId,
                isStruct,
                baseTypes,
                attributes,
                modifiers
            );
        _renderingTypes.Add(type);
        return type;
    }

    public CompilationUnitSyntax Render()
    {
        var members = new List<MemberDeclarationSyntax>();
        members.AddRange(_renderingTypes.Select(type => type.Render()));
        var renderedNamespace = _namespace.WithMembers(List(members));
        return
            CompilationUnit()
                .AddUsings(_referenceManager.RenderUsings())
                .AddMembers(renderedNamespace);
    }

    private class ReferenceManager : IReferenceManager
    {
        // Needed usings
        private readonly HashSet<string> _usings = new ();

        // Needed static usings
        private readonly HashSet<string> _staticUsings = new ();

        // Needed static usings
        private readonly HashSet<string> _nonIncludingNamespaces;

        // Used assemblies
        private readonly HashSet<Assembly> _assemblies = new ();

        private bool _objectsComparerAdded;

        public ReferenceManager(IEnumerable<string> nonIncludingNamespaces)
        {
            _objectsComparerAdded = false;
            _nonIncludingNamespaces = new HashSet<string>(nonIncludingNamespaces);
        }

        public void AddUsing(string name)
        {
            if (!_nonIncludingNamespaces.Contains(name))
                _usings.Add(name);
        }

        public void AddStaticUsing(string name)
        {
            if (!_nonIncludingNamespaces.Contains(name))
                _staticUsings.Add(name);
        }

        public void AddAssembly(Assembly assembly)
        {
            _assemblies.Add(assembly);
        }

        public void AddNUnit()
        {
            _usings.Add("NUnit.Framework");
        }

        public void AddTestExtensions()
        {
            _usings.Add("VSharp.TestExtensions");
        }

        public void AddObjectsComparer()
        {
            if (_objectsComparerAdded) return;
            var name = typeof(ObjectsComparer).FullName;
            Debug.Assert(name != null);
            _staticUsings.Add(name);
            _assemblies.Add(typeof(ObjectsComparer).Assembly);
            _objectsComparerAdded = true;
        }

        public UsingDirectiveSyntax[] RenderUsings()
        {
            var nonStaticElems = _usings.Select(x =>
                UsingDirective(ParseName(x)));
            var staticElems = _staticUsings.Select(x =>
                UsingDirective(Static, null, ParseName(x)));
            return nonStaticElems.Concat(staticElems).ToArray();
        }

        // TODO: Use this as references for test project
        public Assembly[] UsedAssemblies()
        {
            return _assemblies.ToArray();
        }
    }
}