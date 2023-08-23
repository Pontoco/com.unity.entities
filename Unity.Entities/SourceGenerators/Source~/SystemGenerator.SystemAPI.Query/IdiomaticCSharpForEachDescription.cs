using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGenerator.Common;
using QueryDescription = Unity.Entities.SourceGen.SystemGenerator.Common.Query;

using static Unity.Entities.SourceGen.Common.SourceGenHelpers;

namespace Unity.Entities.SourceGen.SystemGenerator.SystemAPI.Query
{
    public class IdiomaticCSharpForEachDescription
    {
        internal struct QueryData
        {
            public string TypeSymbolFullName { get; set; }
            public ITypeSymbol TypeSymbol { get; set; }
            public ITypeSymbol TypeParameterSymbol { get; set; }
            public QueryType QueryType { get; set; }
            public bool IsGeneric => TypeParameterSymbol != null;
            public bool IsReadOnly => QueryType == QueryType.RefRO ||
                                      QueryType == QueryType.EnabledRefRO ||
                                      QueryType == QueryType.ValueTypeComponent ||
                                      QueryType == QueryType.UnmanagedSharedComponent ||
                                      QueryType == QueryType.ManagedSharedComponent;
        }

        internal (bool IsGenerated, IFEType Value) IFEType { get; }
        internal string AspectLookupTypeHandleFieldName { get; set; }
        internal IReadOnlyCollection<QueryData> QueryDatas { get; }
        internal bool IsBurstEnabled { get; }
        internal Location Location { get; }
        internal SystemDescription SystemDescription { get; }
        internal bool RequiresAspectLookupField { get; }
        AttributeData BurstCompileAttribute { get; }

        internal IReadOnlyCollection<QueryDescription> NoneQueryTypes => _noneQueryTypes;
        internal IReadOnlyCollection<QueryDescription> AnyQueryTypes => _anyQueryTypes;
        internal IReadOnlyCollection<QueryDescription> AllQueryTypes => _allQueryTypes;
        internal IReadOnlyCollection<QueryDescription> DisabledQueryTypes => _disabledQueryTypes;
        internal IReadOnlyCollection<QueryDescription> AbsentQueryTypes => _absentQueryTypes;
        internal IReadOnlyCollection<QueryDescription> ChangeFilterQueryTypes => _changeFilterQueryTypes;
        internal IReadOnlyCollection<QueryDescription> SharedComponentFilterQueryTypes => _sharedComponentFilterQueryTypes;
        internal bool HasSharedComponentFilter { get; }

        internal IEnumerable<(SyntaxNode Original, SyntaxNode Replacement)> GetOriginalNodesToReplacementNodes()
        {
            yield return (Original: _queryCandidate.FullInvocationChainSyntaxNode, Replacement: GetQueryInvocationNodeReplacement());

            if (_queryCandidate.ContainingStatementNode is ForEachStatementSyntax forEachStatementSyntax)
            {
                switch (forEachStatementSyntax.Type)
                {
                    // foreach ((RefRW<TypeA>, RefRO<TypeB>) result in SystemAPI.Query<RefRW<TypeA>, RefRO<TypeB>>())
                    case TupleTypeSyntax tupleTypeSyntax:
                    {
                        for (int i = 0; i < QueryDatas.Count; i++)
                        {
                            var queryData = QueryDatas.ElementAt(i);
                            var correspondingElementInReturnedTuple = tupleTypeSyntax.Elements[i];

                            switch (queryData.QueryType)
                            {
                                case QueryType.RefRW:
                                case QueryType.RefRW_TagComponent:
                                    yield return (correspondingElementInReturnedTuple.Type, GetUncheckedRefTypeSyntax("UncheckedRefRW", queryData.TypeParameterSymbol.ToFullName()));
                                    break;
                                case QueryType.RefRO:
                                case QueryType.RefRO_TagComponent:
                                    yield return (correspondingElementInReturnedTuple.Type, GetUncheckedRefTypeSyntax("UncheckedRefRO", queryData.TypeParameterSymbol.ToFullName()));
                                    break;
                            }
                        }
                        break;
                    }
                    case GenericNameSyntax genericNameSyntax:
                    {
                        var queryData = QueryDatas.Single();
                        var queryType = queryData.QueryType;

                        // foreach (RefRW<TypeA> result in SystemAPI.Query<RefRW<TypeA>>())
                        if (genericNameSyntax.Identifier.ValueText != "var")
                        {
                            switch (queryType)
                            {
                                case QueryType.RefRO_TagComponent:
                                case QueryType.RefRO:
                                    yield return (genericNameSyntax, GetUncheckedRefTypeSyntax("UncheckedRefRO", queryData.TypeParameterSymbol.ToFullName()));
                                    break;
                                case QueryType.RefRW_TagComponent:
                                case QueryType.RefRW:
                                    yield return (genericNameSyntax, GetUncheckedRefTypeSyntax("UncheckedRefRW", queryData.TypeParameterSymbol.ToFullName()));
                                    break;

                            }
                        }
                        break;
                    }
                }
            }

            static SyntaxNode GetUncheckedRefTypeSyntax(string uncheckedRefTypeName, string typeArgName)
            {
                var genericNameSyntax =
                    SyntaxFactory.GenericName(
                        SyntaxFactory.Identifier(uncheckedRefTypeName),
                        SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(new[] { SyntaxFactory.ParseTypeName(typeArgName) })));

                return SyntaxFactory.QualifiedName(SyntaxFactory.IdentifierName("Unity.Entities.Internal.InternalCompilerInterface"), genericNameSyntax);
            }
        }

        public bool Success { get; internal set; } = true;
        public string ContainerOrAspectTypeHandleFieldName { get; set; }
        public string SourceGeneratedEntityQueryFieldName { get; set; }

        readonly QueryCandidate _queryCandidate;
        readonly string _systemStateName;
        readonly List<QueryDescription> _noneQueryTypes = new List<QueryDescription>();
        readonly List<QueryDescription> _anyQueryTypes = new List<QueryDescription>();
        readonly List<QueryDescription> _allQueryTypes = new List<QueryDescription>();
        readonly List<QueryDescription> _disabledQueryTypes = new List<QueryDescription>();
        readonly List<QueryDescription> _absentQueryTypes = new List<QueryDescription>();
        readonly List<QueryDescription> _changeFilterQueryTypes = new List<QueryDescription>();
        readonly List<QueryDescription> _sharedComponentFilterQueryTypes = new List<QueryDescription>();
        readonly List<ArgumentSyntax> _sharedComponentFilterArguments = new List<ArgumentSyntax>();
        readonly List<ArgumentSyntax> _entityQueryOptionsArguments = new List<ArgumentSyntax>();
        readonly bool _entityAccessRequired;
        readonly string _typeContainingTargetEnumerator_FullyQualifiedName;

        internal EntityQueryOptions GetEntityQueryOptionsArgument()
        {
            if (!_entityQueryOptionsArguments.Any())
                return EntityQueryOptions.Default;

            var options = EntityQueryOptions.Default;
            var argumentExpression = _entityQueryOptionsArguments.First().Expression;

            while (argumentExpression is BinaryExpressionSyntax binaryExpressionSyntax)
            {
                if (TryParseQualifiedEnumValue(binaryExpressionSyntax.Right.ToString(), out EntityQueryOptions optionArg))
                    options |= optionArg;

                argumentExpression = binaryExpressionSyntax.Left;
            }

            if (TryParseQualifiedEnumValue(argumentExpression.ToString(), out EntityQueryOptions option))
                options |= option;

            return options;
        }

        public IEnumerable<string> GetStatementsToInsertBeforeForEachIteration()
        {
            // Create a scope wrapping the whole foreach conditioned on there being entities
            // to work on, otherwise there is no point in doing any of the logic here.
            // Note, we do not close the scope for the if-statement here, it will be closed
            // in GetStatementsToInsertAfterForEachIteration.
            yield return
                @$"if(!{SourceGeneratedEntityQueryFieldName}.IsEmptyIgnoreFilter)
                {{
                    {_typeContainingTargetEnumerator_FullyQualifiedName}.CompleteDependencyBeforeRW(ref {_systemStateName});
                ";

            yield return $"__TypeHandle.{ContainerOrAspectTypeHandleFieldName}.Update(ref {_systemStateName});";

            if (RequiresAspectLookupField)
                yield return $"__TypeHandle.{AspectLookupTypeHandleFieldName}.Update(ref {_systemStateName});";


            foreach (var arg in _sharedComponentFilterArguments)
                yield return $"{SourceGeneratedEntityQueryFieldName}.SetSharedComponentFilter({arg});";
        }

        private InvocationExpressionSyntax GetQueryInvocationNodeReplacement()
        {
            var memberAccessExpressionSyntax =
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(_typeContainingTargetEnumerator_FullyQualifiedName),
                    SyntaxFactory.IdentifierName("Query"));

            return SyntaxFactory.InvocationExpression(memberAccessExpressionSyntax, SyntaxFactory.ArgumentList(GetArgumentsForStaticQueryMethod()));

            SeparatedSyntaxList<ArgumentSyntax> GetArgumentsForStaticQueryMethod()
            {
                var entityQueryArg = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(SourceGeneratedEntityQueryFieldName));
                // argument being e.g. `__TypeHandle.IFE_2490249,`
                var containerOrAspectTypeHandleArg = SyntaxFactory.Argument(
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("__TypeHandle"), SyntaxFactory.IdentifierName(ContainerOrAspectTypeHandleFieldName)));
                return SyntaxFactory.SeparatedList(new[] { entityQueryArg, containerOrAspectTypeHandleArg });
            }
        }

        public IdiomaticCSharpForEachDescription(SystemDescription systemDescription, QueryCandidate queryCandidate, int numForEachsPreviouslySeenInSystem)
        {
            if (systemDescription.SemanticModel.GetOperation(queryCandidate.FullInvocationChainSyntaxNode) is IInvocationOperation invocationOperation
                && IsSystemAPIQueryInvocation(invocationOperation))
            {
                _queryCandidate = queryCandidate;

                SystemDescription = systemDescription;
                Location = queryCandidate.FullInvocationChainSyntaxNode.GetLocation();

                if (TryGetQueryDatas(out var queryDatas))
                    QueryDatas = queryDatas.ToArray();
                else
                {
                    Success = false;
                    return;
                }

                var containingMethod = queryCandidate.FullInvocationChainSyntaxNode.AncestorOfKindOrDefault<MethodDeclarationSyntax>();
                if (containingMethod != null)
                {
                    var methodSymbol = SystemDescription.SemanticModel.GetDeclaredSymbol(containingMethod);

                    foreach (var node in queryCandidate.MethodInvocationNodes)
                    {
                        switch (node.Expression)
                        {
                            case MemberAccessExpressionSyntax { Name: GenericNameSyntax genericNameSyntax }:
                            {
                                switch (genericNameSyntax.Identifier.ValueText)
                                {
                                    case "WithDisabled":
                                        _disabledQueryTypes.AddRange(
                                            genericNameSyntax.TypeArgumentList.Arguments.Select(typeArg =>
                                                new QueryDescription
                                                {
                                                    TypeSymbol = (ITypeSymbol)systemDescription.SemanticModel.GetSymbolInfo(typeArg).Symbol,
                                                    Type = SystemGenerator.Common.QueryType.Disabled,
                                                    IsReadOnly = true
                                                }));
                                        break;
                                    case "WithAbsent":
                                        _absentQueryTypes.AddRange(
                                            genericNameSyntax.TypeArgumentList.Arguments.Select(typeArg =>
                                                new QueryDescription
                                                {
                                                    TypeSymbol = (ITypeSymbol)systemDescription.SemanticModel.GetSymbolInfo(typeArg).Symbol,
                                                    Type = SystemGenerator.Common.QueryType.Absent,
                                                    IsReadOnly = true
                                                }));
                                        break;
                                    case "WithAll":
                                        _allQueryTypes.AddRange(
                                            genericNameSyntax.TypeArgumentList.Arguments.Select(typeArg =>
                                                new QueryDescription
                                                {
                                                    TypeSymbol = (ITypeSymbol)systemDescription.SemanticModel.GetSymbolInfo(typeArg).Symbol,
                                                    Type = SystemGenerator.Common.QueryType.All,
                                                    IsReadOnly = true
                                                }));
                                        break;
                                    case "WithAny":
                                        _anyQueryTypes.AddRange(
                                            genericNameSyntax.TypeArgumentList.Arguments.Select(typeArg =>
                                                new QueryDescription
                                                {
                                                    TypeSymbol = (ITypeSymbol)systemDescription.SemanticModel.GetSymbolInfo(typeArg).Symbol,
                                                    Type = SystemGenerator.Common.QueryType.Any,
                                                    IsReadOnly = true
                                                }));
                                        break;
                                    case "WithNone":
                                        _noneQueryTypes.AddRange(
                                            genericNameSyntax.TypeArgumentList.Arguments.Select(typeArg =>
                                                new QueryDescription
                                                {
                                                    TypeSymbol = (ITypeSymbol)systemDescription.SemanticModel.GetSymbolInfo(typeArg).Symbol,
                                                    Type = SystemGenerator.Common.QueryType.None,
                                                    IsReadOnly = true
                                                }));
                                        break;
                                    case "WithChangeFilter":
                                        _changeFilterQueryTypes.AddRange(
                                            genericNameSyntax.TypeArgumentList.Arguments.Select(typeArg =>
                                                new QueryDescription
                                                {
                                                    TypeSymbol = (ITypeSymbol)systemDescription.SemanticModel.GetSymbolInfo(typeArg).Symbol,
                                                    Type = SystemGenerator.Common.QueryType.ChangeFilter,
                                                    IsReadOnly = true
                                                }));
                                        break;
                                    case "WithSharedComponentFilter":
                                        _sharedComponentFilterQueryTypes.AddRange(
                                            genericNameSyntax.TypeArgumentList.Arguments.Select(typeArg =>
                                                new QueryDescription
                                                {
                                                    TypeSymbol = (ITypeSymbol)systemDescription.SemanticModel.GetSymbolInfo(typeArg).Symbol,
                                                    Type = SystemGenerator.Common.QueryType.All,
                                                    IsReadOnly = true
                                                }));
                                        _sharedComponentFilterArguments.AddRange(node.ArgumentList.Arguments);
                                        HasSharedComponentFilter = true;
                                        break;
                                }
                                break;
                            }
                            case MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifierNameSyntax }:
                            {
                                switch (identifierNameSyntax.Identifier.ValueText)
                                {
                                    case "WithSharedComponentFilter":
                                        _sharedComponentFilterQueryTypes.AddRange(
                                            node.ArgumentList.Arguments.Select(arg =>
                                                new QueryDescription
                                                {
                                                    TypeSymbol = systemDescription.SemanticModel.GetTypeInfo(arg.Expression).Type,
                                                    Type = SystemGenerator.Common.QueryType.All,
                                                    IsReadOnly = true
                                                }));
                                        _sharedComponentFilterArguments.AddRange(node.ArgumentList.Arguments);
                                        HasSharedComponentFilter = true;
                                        break;
                                    case "WithOptions":
                                        _entityQueryOptionsArguments.Add(node.ArgumentList.Arguments.Single());
                                        break;
                                    case "WithEntityAccess":
                                        _entityAccessRequired = true;
                                        break;
                                }
                                break;
                            }
                        }
                    }
                    if (!_queryCandidate.IsContainedInForEachStatement)
                    {
                        IdiomaticCSharpForEachCompilerMessages.SGFE001(SystemDescription, Location);
                        Success = false;
                        return;
                    }

                    if (_changeFilterQueryTypes.Count > 2)
                        IdiomaticCSharpForEachCompilerMessages.SGFE003(SystemDescription, _changeFilterQueryTypes.Count, Location);
                    if (_sharedComponentFilterQueryTypes.Count > 2)
                        IdiomaticCSharpForEachCompilerMessages.SGFE007(SystemDescription, _sharedComponentFilterQueryTypes.Count, Location);
                    if (_entityQueryOptionsArguments.Count > 1)
                        IdiomaticCSharpForEachCompilerMessages.SGFE008(SystemDescription, _entityQueryOptionsArguments.Count, Location);

                    if (SystemDescription.TryGetSystemStateParameterName(_queryCandidate, out var systemStateExpression))
                        _systemStateName = systemStateExpression.ToFullString();
                    else
                    {
                        Success = false;
                        return;
                    }

                    var mustGenerateContainerType = MustGenerateContainerType(QueryDatas, _entityAccessRequired);

                    bool useAspect = !mustGenerateContainerType;
                    // Aspects always have nested `Lookup`s, which must be updated whenever there is a system update
                    RequiresAspectLookupField = useAspect;

                    var burstCompileAttribute = methodSymbol.GetAttributes().SingleOrDefault(a => a.AttributeClass.ToFullName() == "Unity.Burst.BurstCompileAttribute");
                    IsBurstEnabled = burstCompileAttribute != null;
                    BurstCompileAttribute = burstCompileAttribute;

                    if (mustGenerateContainerType)
                    {
                        string ifeTypeName = $"IFE_{systemDescription.HandlesDescription.UniqueId}_{numForEachsPreviouslySeenInSystem}";

                        var ifeType = new IFEType
                        {
                            PerformsCollectionChecks = SystemDescription.IsUnityCollectionChecksEnabled,
                            MustReturnEntityDuringIteration = _entityAccessRequired,
                            TypeName = ifeTypeName,
                            BurstCompileAttribute = BurstCompileAttribute,
                            FullyQualifiedTypeName =
                                queryCandidate
                                    .FullInvocationChainSyntaxNode
                                    .GetContainingTypesAndNamespacesFromMostToLeastNested()
                                    .Select(GetIdentifier)
                                    .Reverse()
                                    .Append(ifeTypeName)
                                    .SeparateByDot(),
                            ReturnedTupleElementsDuringEnumeration =
                                QueryDatas
                                    .Select((queryData, index) =>
                                        {
                                            string typeArgumentFullName;
                                            switch (queryData.QueryType)
                                            {
                                                case QueryType.RefRO:
                                                    typeArgumentFullName = queryData.TypeParameterSymbol.ToFullName();
                                                    return new ReturnedTupleElementDuringEnumeration(
                                                        $"Unity.Entities.Internal.InternalCompilerInterface.UncheckedRefRO<{typeArgumentFullName}>",
                                                        typeArgumentFullName: typeArgumentFullName,
                                                        elementName: $"item{index + 1}",
                                                        type: queryData.QueryType);
                                                case QueryType.RefRW:
                                                    typeArgumentFullName = queryData.TypeParameterSymbol.ToFullName();
                                                    return new ReturnedTupleElementDuringEnumeration(
                                                        $"Unity.Entities.Internal.InternalCompilerInterface.UncheckedRefRW<{typeArgumentFullName}>",
                                                        typeArgumentFullName: typeArgumentFullName,
                                                        elementName: $"item{index + 1}",
                                                        type: queryData.QueryType);
                                                default:
                                                    typeArgumentFullName = queryData.TypeParameterSymbol is { } symbol ? symbol.ToFullName() : string.Empty;
                                                    return new ReturnedTupleElementDuringEnumeration(
                                                        queryData.TypeSymbolFullName,
                                                        typeArgumentFullName: typeArgumentFullName,
                                                        elementName: $"item{index + 1}",
                                                        type: queryData.QueryType);
                                            }
                                        })
                                    .ToArray()
                        };

                        IFEType = (IsGenerated: true, ifeType);
                        SourceGeneratedEntityQueryFieldName = $"{IFEType.Value.TypeName}_Query";

                        _typeContainingTargetEnumerator_FullyQualifiedName = ifeType.FullyQualifiedTypeName;
                    }
                    else
                        _typeContainingTargetEnumerator_FullyQualifiedName = QueryDatas.Single().TypeSymbolFullName; // Use `Aspect` enumerator
                }
                else
                {
                    var propertyDeclarationSyntax = queryCandidate.FullInvocationChainSyntaxNode.AncestorOfKind<PropertyDeclarationSyntax>();
                    var propertySymbol = ModelExtensions.GetDeclaredSymbol(systemDescription.SemanticModel, propertyDeclarationSyntax);

                    IdiomaticCSharpForEachCompilerMessages.SGFE002(
                        systemDescription,
                        systemDescription.SystemTypeSymbol.ToFullName(),
                        propertySymbol.OriginalDefinition.ToString(),
                        queryCandidate.ContainingTypeNode.GetLocation());
                    Success = false;
                }
            }
            else
                Success = false;

            static bool IsSystemAPIQueryInvocation(IInvocationOperation operation)
            {
                var constructedFrom = operation.TargetMethod.ConstructedFrom.ToString();
                if (constructedFrom.StartsWith("Unity.Entities.QueryEnumerable<"))
                    return true;
                if (constructedFrom.StartsWith("Unity.Entities.QueryEnumerableWithEntity<"))
                    return true;
                return constructedFrom.StartsWith("Unity.Entities.SystemAPI.Query<");
            }

            // Returns true if we are querying only a single aspect without `.WithEntityAccess()` --
            // we don't need a container type in that case; we can just use the existing aspect type along with
            // its generated enumerators, nested `ResolvedChunk`, etc.
            static bool MustGenerateContainerType(IReadOnlyCollection<QueryData> queryDatas, bool entityAccessRequired)
            {
                if (queryDatas.Count > 1)
                    return true;
                if (entityAccessRequired)
                    return true;
                return queryDatas.Single().QueryType != QueryType.Aspect;
            }

            static string GetIdentifier(MemberDeclarationSyntax memberDeclarationSyntax)
            {
                switch (memberDeclarationSyntax)
                {
                    case ClassDeclarationSyntax classDeclarationSyntax:
                        return classDeclarationSyntax.Identifier.ValueText;
                    case StructDeclarationSyntax structDeclarationSyntax:
                        return structDeclarationSyntax.Identifier.ValueText;
                    case NamespaceDeclarationSyntax namespaceDeclarationSyntax:
                        var identifierName = namespaceDeclarationSyntax.ChildNodes().OfType<IdentifierNameSyntax>().FirstOrDefault();
                        return
                            identifierName != null
                                ? identifierName.Identifier.ValueText
                                : namespaceDeclarationSyntax.ChildNodes().OfType<QualifiedNameSyntax>().First().ToString();
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private bool TryGetQueryDatas(out ICollection<QueryData> queryTypes)
        {
            queryTypes = new List<QueryData>();

            foreach (var typeSyntax in _queryCandidate.QueryTypeNodes)
            {
                var typeSymbol = SystemDescription.SemanticModel.GetTypeInfo(typeSyntax).Type;
                var typeParameterSymbol = default(ITypeSymbol);

                var genericNameCandidate = typeSyntax;
                if (typeSyntax is QualifiedNameSyntax qualifiedNameSyntax) // This is the case when people type out their syntax Query<MyNameSpace.MyThing>
                    genericNameCandidate = qualifiedNameSyntax.Right;
                if (genericNameCandidate is GenericNameSyntax genericNameSyntax)
                    typeParameterSymbol = SystemDescription.SemanticModel.GetTypeInfo(genericNameSyntax.TypeArgumentList.Arguments.Single()).Type;

                var result = TryGetIdiomaticCSharpForEachQueryType(typeSymbol, typeSyntax.GetLocation());

                if (!result.Success)
                    return false;

                if (result.Value == QueryType.ValueTypeComponent)
                    IdiomaticCSharpForEachCompilerMessages.SGFE009(SystemDescription, typeSymbol.ToFullName(), Location);

                queryTypes.Add(new QueryData
                {
                    TypeParameterSymbol = typeParameterSymbol,
                    TypeSymbol = typeSymbol,
                    TypeSymbolFullName = typeSymbol.ToFullName(),
                    QueryType = result.Value
                });
            }
            return true;

            (bool Success, QueryType Value) TryGetIdiomaticCSharpForEachQueryType(ITypeSymbol typeSymbol, Location errorLocation)
            {
                if (typeSymbol.IsAspect())
                    return (true, QueryType.Aspect);

                if (typeSymbol.IsSharedComponent())
                    return (true, typeSymbol.IsUnmanagedType ? QueryType.UnmanagedSharedComponent : QueryType.ManagedSharedComponent);

                if (typeSymbol.IsComponent())
                {
                    if (typeSymbol.InheritsFromType("System.ValueType"))
                        return
                            (true, typeSymbol.IsZeroSizedComponent()
                                ? QueryType.TagComponent
                                : QueryType.ValueTypeComponent);
                    return (true, QueryType.ManagedComponent);
                }

                var typeArgument = ((INamedTypeSymbol)typeSymbol).TypeArguments[0];
                if (typeArgument is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.Arity != 0)
                {
                    IdiomaticCSharpForEachCompilerMessages.SGFE010(SystemDescription, errorLocation);
                    return (false, QueryType.Invalid);
                }

                return typeSymbol.Name switch
                {
                    "DynamicBuffer" => (true, QueryType.DynamicBuffer),
                    "RefRW" => (true, typeArgument.IsZeroSizedComponent() ? QueryType.RefRW_TagComponent : QueryType.RefRW),
                    "RefRO" => (true, typeArgument.IsZeroSizedComponent() ? QueryType.RefRO_TagComponent : QueryType.RefRO),
                    "EnabledRefRW" => (true, QueryType.EnabledRefRW),
                    "EnabledRefRO" => (true, QueryType.EnabledRefRO),
                    "UnityEngineComponent" => (true, QueryType.UnityEngineComponent),
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
        }

        public IEnumerable<string> GetStatementsToInsertAfterForEachIteration()
        {
            if (HasSharedComponentFilter)
                yield return $"{SourceGeneratedEntityQueryFieldName}.ResetFilter();";

            // End scope started in GetStatementsToInsertBeforeForEachIteration for the
            // if(!<query>.IsEmptyIgnoreFilter) { block 
            yield return $"}}";
        }
    }
}
